/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using MTGOTracker.Database.Models;


namespace MTGOTracker.Services.MTGO.Events;

public class MatchTracker: IDisposable
{
  private readonly record struct CardIdPair(int Id, int Quantity);

  private readonly Match m_match;
  private readonly EventDatabaseWriter _dbWriter;

  private readonly object _lock = new();
  private readonly AutoResetEvent _deckChanged = new(false);
  private readonly ConcurrentQueue<List<CardEntry>> _sideboardChanges = new();
  private int _matchCompletedHandled = 0;

  /// <summary>
  /// Eagerly initializes the static hooks used by MatchTracker so that
  /// the IPC type-dump cost is paid at startup, not on first match join.
  /// </summary>
  public static void EnsureHooksInitialized()
  {
    Match.GameStarted.EnsureInitialize();
    Match.MatchStateChanged.EnsureInitialize();
    Match.DeckForSideboardingChanged.EnsureInitialize();
  }

  public MatchTracker(
    Match match,
    EventDatabaseWriter dbWriter)
  {
    m_match = match;
    _dbWriter = dbWriter;

    match.OnGameStarted += Match_OnGameStarted;
    match.OnSideboardingDeckChanged += Match_OnSideboardingDeckChanged;
    match.OnMatchStateChanged += Match_OnMatchStateChanged;
  }

  private bool _disposed = false;

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    m_match.ClearEvents();
    GC.SuppressFinalize(this);
  }

  private void Match_OnGameStarted(Game game)
  {
    if (_disposed) return;
    WaitUntil(() => m_match.CurrentGame?.Id == game.Id).Wait();

    // Backfill the deck if it wasn't available at event creation time.
    // For challenge/bot matches, DeckUsedToJoin is set asynchronously
    // and may not be ready when TryAddEvent runs.
    _dbWriter.TryBackfillEventDeck(m_match);

    // Wait for the sideboard changes to be fully processed before writing.
    _deckChanged.WaitOne(100);
    lock(_lock)
    {
      if (m_match.Games[0].Id != game.Id &&
          _sideboardChanges.TryDequeue(out var sideboardChange))
      {
        Log.Trace("Handling sideboard changes for match {Id} (heading into game {GameId})",
          m_match.Id, game.Id);

        _dbWriter.TryUpdateSideboardChanges(m_match, game.Id, sideboardChange);
        string jsonRes = JsonSerializer.Serialize(sideboardChange);
        Log.Debug("Updated sideboard changes for match {Id} (heading into game {GameId}): {Changes}",
          m_match.Id, game.Id, jsonRes);
      }
    }
  }

  private void Match_OnSideboardingDeckChanged(Deck deck)
  {
    if (_disposed) return;

    lock(_lock)
    {
      Log.Trace("Processing sideboarding changes for match {Id}", m_match.Id);
      Deck? originalDeck = m_match.RegisteredDeck;
      if (originalDeck == null)
      {
        Log.Error("Match {Id} has no registered deck.", m_match.Id);
        return;
      }

      // Convert decks to dictionaries for efficient lookup
      Dictionary<int, int> originalMainboardQuantities = originalDeck
        .GetCards(DeckRegion.MainDeck)
        .ToDictionary(c => c.Id, c => c.Quantity);
      Dictionary<int, int> newMainboardQuantities = deck
        .GetCards(DeckRegion.MainDeck)
        .ToDictionary(c => c.Id, c => c.Quantity);

      // Get all unique card IDs present in either the original or new mainboard
      var allCardIds = originalMainboardQuantities.Keys.Union(newMainboardQuantities.Keys);

      List<CardEntry> changedCards = [];
      foreach (int cardId in allCardIds)
      {
        // Get quantities, defaulting to 0 if the card is not present
        originalMainboardQuantities.TryGetValue(cardId, out int originalQuantity);
        newMainboardQuantities.TryGetValue(cardId, out int newQuantity);

        // Calculate the difference in quantity
        int diff = newQuantity - originalQuantity;

        // If the quantity changed, record the difference
        if (diff != 0)
        {
          changedCards.Add(new CardEntry(cardId, diff));
        }
      }

      // Queue the sideboard changes to be processed on the next game start.
      Log.Trace("Finished processing sideboarding changes for match {Id}", m_match.Id);
      _sideboardChanges.Enqueue(changedCards);
      _deckChanged.Set();
    }
  }

  internal void OnRemoved() => Match_OnMatchStateChanged(MatchState.GameClosed);

  private void Match_OnMatchStateChanged(MatchState matchState)
  {
    if (_disposed) return;
    if (!matchState.HasFlag(MatchState.MatchCompleted) &&
        !matchState.HasFlag(MatchState.Terminal) &&
        !matchState.HasFlag(MatchState.GameClosed))
      return;
    if (Interlocked.Exchange(ref _matchCompletedHandled, 1) != 0) return;

    // Native result captures are delivered by the service independently of this
    // match's UI lifecycle, including results received after the window closes.
    GameAPIService.RemoveActiveMatch(m_match.Id);
    GameAPIService.MatchResultUpdated?.Invoke(this, m_match.Id);
    Dispose();
  }
}
