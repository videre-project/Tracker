/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Tracker.Database.Models;
using Tracker.Database.Models.Events;


namespace Tracker.Services.MTGO.Events;

public class MatchTracker: IDisposable
{
  private readonly record struct CardIdPair(int Id, int Quantity);

  private readonly Match m_match;
  private readonly EventDatabaseWriter _dbWriter;

  private readonly object _lock = new();
  private readonly AutoResetEvent _deckChanged = new(false);
  private readonly ConcurrentQueue<List<CardEntry>> _sideboardChanges = new();

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

  private void Match_OnMatchStateChanged(MatchState matchState)
  {
    if (_disposed) return;

    if (matchState.HasFlag(MatchState.MatchCompleted) ||
        matchState.HasFlag(MatchState.GameClosed))
    {
      // Capture game/player data now while the MTGO objects are still alive.
      // After RemoveFromSystem, the match's JoinedUsers may be cleared.
      int[] gameIds;
      IList<Game> matchGames;
      IEnumerable<User> players;
      try
      {
        matchGames = m_match.Games;
        gameIds = matchGames.Select(g => g.Id).ToArray();
        players = m_match.Players.ToList();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Match {Id} failed to capture game/player data on state change", m_match.Id);
        Dispose();
        return;
      }

      // Wait for the game to finish processing events before disposing.
      Task.Delay(10_000).ContinueWith(_ =>
      {
        try
        {
          Log.Debug("Match {Id} completed (state: {State})", m_match.Id, matchState);
          Dispose();
          UpdateMatchResults(gameIds, matchGames, players);
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Match {Id} failed to update results", m_match.Id);
        }
      });
    }
  }

  private void UpdateMatchResults(
    int[] gameIds,
    IList<Game> matchGames,
    IEnumerable<User> players)
  {
    // Wait until all games have completed being written to the database.
    foreach(int gameId in gameIds)
    {
      if (!_dbWriter.WaitForGameCompletionAsync(gameId).Result)
      {
        //
        // Results event never fired (e.g. window closed too quickly).
        //
        // Reconstruct from the live Game object which still has data
        // populated via the FLS HandlePlayerRanking path.
        //
        Game? game = matchGames.FirstOrDefault(g => g.Id == gameId);
        if (game == null)
        {
          Log.Error("Game {Id} not found in match games list", gameId);
          continue;
        }
        var fallbackResults = BuildFallbackResults(game);
        if (fallbackResults != null)
        {
          Log.Warning("Game {Id} results event missed, reconstructing from game object", gameId);
          _dbWriter.TryUpdateGameResults(game, fallbackResults);
        }
        else
        {
          Log.Error("Game {Id} results event missed and fallback reconstruction failed", gameId);
        }
      }
    }

    IEnumerable<GameModel> games = _dbWriter.GetGamesAsync(m_match.Id).Result;
    if (games == null || games.Count() == 0)
    {
      Log.Error("Match {Id} has no game models.", m_match.Id);
      return;
    }

    // Check that all game models' Ids match the gameIds and aren't missing any.
    int[] gameModelIds = games.Select(g => g.Id).ToArray();
    if (gameModelIds.Length != gameIds.Length || !gameModelIds.All(id => gameIds.Contains(id)))
    {
      Log.Error("Match {Id} has missing or mismatched game models.", m_match.Id);
      Log.Error("Game IDs: {GameIds}", string.Join(", ", gameModelIds));
      return;
    }

    // Build match results from the DB game records and pre-captured player list.
    IList<PlayerResult> results = [];
    foreach (User player in players)
    {
      IList<GameResult> gameResults = games
        .Select(g => g.GamePlayerResults
          .FirstOrDefault(gp => gp.Player == player.Name)?.Result
            ?? GameResult.Loss)
        .ToList();

      results.Add(new(player, gameResults));
    }

    // Update the match results in the database.
    if (_dbWriter.TryUpdateMatchResults(m_match, results))
    {
      string jsonRes = JsonSerializer.Serialize(results);
      Log.Debug("Updated match results for {Id}: {Results}", m_match.Id, jsonRes);

      // Notify listeners that match results are available.
      GameAPIService.MatchResultUpdated?.Invoke(this, m_match.Id);
      GameAPIService.RemoveActiveMatch(m_match.Id);
    }
  }

  /// <summary>
  /// Reconstructs GamePlayerResults from the live Game object when the
  /// GameResultsChanged event was missed (e.g. window closed too fast).
  /// The Game object's WinningPlayers and Players are populated via the
  /// FLS HandlePlayerRanking path independently of CompileWinningPlayers.
  /// </summary>
  private static IList<GamePlayerResult>? BuildFallbackResults(Game game)
  {
    try
    {
      var players = game.Players;
      var winners = game.WinningPlayers;
      if (players == null || players.Count == 0) return null;

      var winnerNames = new HashSet<string>(
        winners?.Select(w => w.Name) ?? []);

      TimeSpan elapsed = game.EndTime is DateTime endTime
                      && endTime > game.StartTime
        ? endTime - game.StartTime
        : DateTime.Now - game.StartTime;

      List<GamePlayerResult> results = [];
      for (int i = 0; i < players.Count; i++)
      {
        GamePlayer player = players[i];
        GameResult result = winnerNames.Contains(player.Name)
          ? GameResult.Win
          : GameResult.Loss;

        // Play/draw info (MovedFirst) is only available in the
        // GameResultsMessage which we missed.
        PlayDrawResult playDraw = PlayDrawResult.Unknown;

        results.Add(new(player, playDraw, result, elapsed));
      }

      return results;
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to build fallback results for game {Id}", game.Id);
      return null;
    }
  }
}
