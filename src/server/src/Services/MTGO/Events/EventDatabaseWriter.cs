/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Serialization;

using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Database.Models.Events;
using Tracker.Database.Extensions;
using Tracker.Services.MTGO.Collection;


namespace Tracker.Services.MTGO.Events;

public class EventDatabaseWriter(IServiceProvider serviceProvider) : DLRWrapper
{
  private static readonly ConcurrentDictionary<int, int> s_matchIdMap = new();
  private static readonly ConcurrentDictionary<int, int> s_gameIdMap = new();

  public bool TryAddEvent(Event eventObj, out EventModel? eventModel)
  {
    DateTime startTime = DateTime.Now;
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();
      int eventId = eventObj.GetDatabaseId();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Determine event type and description for extracting event-specific properties
        string description = eventObj.Description;
        EventType type;
        int? leagueEventId = null;

        if (eventObj is League leagueObj)
        {
          leagueEventId = eventObj.Id;
          // Leagues use the Description field as a true description, so fall
          // back to the league's Name (and MTGO ID) for the event description.
          description = $"{leagueObj.Name} #{leagueObj.Id}";
          type = EventType.League;
        }
        else if (string.IsNullOrEmpty(description) && eventObj is Match m)
        {
          string bestOf = m.MaxGames == 3 ? "Bo3" : "Bo1";
          description = $"{bestOf} Match #{m.Id}";
          type = EventType.Match;
        }
        else if (eventObj is Tournament)
        {
          type = EventType.Tournament;
        }
        else
        {
          // Fallback for any other event type
          type = EventType.Match;
        }

        // Check if event already exists
        var existingEvent = context.Events.FirstOrDefault(e => e.Id == eventId);
        if (existingEvent != null)
        {
          eventModel = existingEvent;
          transaction.Commit();
          return false;
        }

        eventModel = new EventModel
        {
          Id = eventId,
          Format = eventObj.Format,
          Description = description,
          Type = type,
          LeagueEventId = leagueEventId,
          StartTime = eventObj is Tournament t ? t.StartTime : startTime
        };
        if (eventObj.RegisteredDeck is Deck deck)
        {
          var deckService =
            scope.ServiceProvider.GetRequiredService<CollectionDeckService>();
          eventModel.DeckRevisionId = deckService.ResolveRevision(deck);
        }

        context.Events.Add(eventModel);
        context.SaveChanges();

        transaction.Commit();
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while adding event {EventId}: {Message}",
                  eventObj.Id, ex.Message);

        // Check if event was added by another process
        eventModel = context.Events.FirstOrDefault(e => e.Id == eventId);
        return eventModel != null;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error adding event {EventId}", eventObj.Id);
        eventModel = null;
        return false;
      }
    }
  }

  public bool TryBackfillEventDeck(Event eventObj)
  {
    if (eventObj.RegisteredDeck is not Deck deck) return false;

    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        int eventId = eventObj.GetDatabaseId();

        var eventModel = context.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventModel == null || eventModel.DeckRevisionId != null) return false;

        var deckService =
          scope.ServiceProvider.GetRequiredService<CollectionDeckService>();
        eventModel.DeckRevisionId = deckService.ResolveRevision(deck);
        context.SaveChanges();
        transaction.Commit();
        Log.Debug(
          "Backfilled deck revision {DeckRevisionId} for event {EventId}",
          eventModel.DeckRevisionId,
          eventId);
        return true;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error backfilling deck for event {EventId}", eventObj.Id);
        return false;
      }
    }
  }

  public bool TryUpdateEventEndTime(int eventId, DateTime endTime)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        var eventModel = context.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventModel == null)
        {
          Log.Warning("Event {Id} not found for updating end time", eventId);
          return false;
        }

        // Only update if EndTime is not already set (Idempotence)
        if (eventModel.EndTime == null)
        {
          eventModel.EndTime = endTime;
          context.SaveChanges();
          transaction.Commit();
          return true;
        }
        
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error updating end time for event {EventId}", eventId);
        return false;
      }
    }
  }

  public bool TryAddMatch(Match match, int eventId, out MatchModel? matchModel)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Wait until the parent event is created before adding the match.
        if (!s_matchIdMap.ContainsKey(eventId) &&
            !WaitForEventModelAsync(eventId).Result)
        {
          Log.Warning("Parent event {Id} not found for match {MatchId}", eventId, match.Id);
          matchModel = null;
          transaction.Rollback();
          return false;
        }

        // Check if match already exists
        var existingMatch = context.Matches.FirstOrDefault(m => m.Id == match.Id);
        if (existingMatch != null)
        {
          matchModel = existingMatch;
          transaction.Commit();
          return false;
        }

        // Create new match
        matchModel = new MatchModel { Id = match.Id };

        // First add the match to the context
        context.Matches.Add(matchModel);

        // Then find parent event and create relationship
        var eventModel = context.Events
          .Include(e => e.Matches)
          .FirstOrDefault(e => e.Id == eventId);

        if (eventModel != null)
        {
          eventModel.Matches ??= new List<MatchModel>();
          eventModel.Matches.Add(matchModel);
        }

        context.SaveChanges();
        transaction.Commit();

        s_matchIdMap.TryAdd(match.Id, eventId);
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while adding match {MatchId}: {Message}",
                  match.Id, ex.Message);

        // Check if match was added by another process
        matchModel = context.Matches.FirstOrDefault(m => m.Id == match.Id);
        return matchModel != null;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error adding match {MatchId} to event {EventId}", match.Id, eventId);
        matchModel = null;
        return false;
      }
    }
  }

  public bool TryAddGame(Game game, int matchId, out GameModel? gameModel)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Wait until the match is created before adding the game.
        if (!s_matchIdMap.ContainsKey(matchId) &&
            !WaitForMatchModelAsync(matchId).Result)
        {
          Log.Warning("Match {Id} not found for game {GameId}", matchId, game.Id);
          gameModel = null;
          transaction.Rollback();
          return false;
        }

        // Check if game already exists
        var existingGame = context.Games.FirstOrDefault(g => g.Id == game.Id);
        if (existingGame != null)
        {
          gameModel = existingGame;
          transaction.Commit();
          return false;
        }

        // First create the game
        gameModel = new GameModel { Id = game.Id };

        // Find parent match
        var matchModel = context.Matches
          .Include(m => m.Games)
          .FirstOrDefault(m => m.Id == matchId);

        if (matchModel == null)
        {
          Log.Warning("Parent match {Id} not found for game {GameId}", matchId, game.Id);
          gameModel = null;
          transaction.Rollback();
          return false;
        }

        // Add game to context and relate to match
        context.Games.Add(gameModel);
        matchModel.Games ??= new List<GameModel>();
        matchModel.Games.Add(gameModel);

        context.SaveChanges();
        transaction.Commit();

        s_gameIdMap.TryAdd(game.Id, matchId);
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while adding game {GameId}: {Message}",
                  game.Id, ex.Message);

        // Check if game was added by another process
        gameModel = context.Games.FirstOrDefault(g => g.Id == game.Id);
        return gameModel != null;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error adding game {GameId} to match {MatchId}", game.Id, matchId);
        gameModel = null;
        return false;
      }
    }
  }

  //
  //
  //

  public async Task<bool> WaitForEventModelAsync(
    int eventId,
    CancellationToken cancellationToken = default)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      // Wait until the EventModel is created before adding the log entry.
      if (!await WaitUntilAsync(async () =>
        await context.Events.AnyAsync(e => e.Id == eventId, cancellationToken),
        retries: int.MaxValue,
        ct: cancellationToken
      ))
      {
        return false;
      }
    }

    return true;
  }

  public async Task<bool> WaitForMatchModelAsync(
    int matchId,
    CancellationToken cancellationToken = default)
  {
    // Check if the match ID is already in the map
    if (s_matchIdMap.ContainsKey(matchId)) return true;

    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      // Wait until the MatchModel is created before adding the log entry.
      if (!await WaitUntilAsync(async () =>
        await context.Matches.AnyAsync(m => m.Id == matchId, cancellationToken),
        retries: int.MaxValue,
        ct: cancellationToken
      ))
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Waits for the game to contain a non-empty GamePlayerResults entry.
  /// </summary>
  public async Task<bool> WaitForGameCompletionAsync
  (
    int gameId,
    CancellationToken cancellationToken = default)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      // Wait until the GamePlayerResults entry is non-empty
      return await WaitUntilAsync(async () =>
        await context.Games
          .AnyAsync(g => g.Id == gameId && g.GamePlayerResults.Count > 0,
                    cancellationToken),
        delay: 250,
        retries: 8,
        ct: cancellationToken
      );
    }
  }

  //
  //
  //

  public async Task<IEnumerable<GameModel>> GetGamesAsync(
    int matchId,
    CancellationToken cancellationToken = default)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      // Check if the match ID is already in the map
      if (!s_matchIdMap.ContainsKey(matchId)) return Enumerable.Empty<GameModel>();

      // Fetch games related to the match ID
      return await context.Games
        .Where(g => g.MatchId == matchId)
        .ToListAsync(cancellationToken);
    }
  }

  //
  // Structured game state methods
  //

  /// <summary>
  /// Builds a GameCardModel from a GameCard without touching the database.
  /// FirstSeenStateId is set during flush.
  /// </summary>
  internal static GameCardModel BuildGameCardModel(
    int gameId, GameCard card,
    int ownerIndex, int controllerIndex,
    string? initialZoneOverride = null)
  {
    string? countersJson = null;
    try
    {
      var counters = card.Counters?
        .GroupBy(c => c)
        .ToDictionary(g => g.Key.ToString(), g => g.Count());
      if (counters?.Count > 0)
        countersJson = JsonSerializer.Serialize(counters, JsonSerializerOptions.Web);
    }
    catch { }

    string? abilitiesJson = null;
    try
    {
      var abilities = card.Abilities?.ToList();
      if (abilities?.Count > 0)
        abilitiesJson = JsonSerializer.Serialize(
          abilities.Select(a => a.ToString()), JsonSerializerOptions.Web);
    }
    catch { }

    string? manaCost = null;
    try
    {
      manaCost = card.Definition.ManaCost;
    }
    catch { }

    int? catalogId = null;
    int ctn = card.CTN;
    if (ctn > 0)
    {
      try { catalogId = CollectionManager.GetCardByTextureId(ctn).Id; }
      catch (Exception ex)
      {
        Log.Warning("CatalogId resolution failed for CTN {Ctn} ({Name}): {Error}",
          ctn, card.Name, ex.Message);
      }
    }

    return new GameCardModel
    {
      GameId = gameId,
      SourceId = card.SourceId > 0 ? card.SourceId : null,
      CardId = card.Id,
      Name = card.Name,
      RulesText = !string.IsNullOrEmpty(card.RulesText) ? card.RulesText : null,
      ManaCost = !string.IsNullOrEmpty(manaCost) ? manaCost : null,
      TextureId = ctn > 0 ? ctn : null,
      CatalogId = catalogId,
      InitialZone = initialZoneOverride ?? card.Zone?.Name ?? "Unknown",
      InitialPower = card.Toughness != 0 ? card.Power.ToString() : null,
      InitialToughness = card.Toughness != 0 ? card.Toughness.ToString() : null,
      InitialCounters = countersJson,
      InitialAbilities = abilitiesJson,
      OwnerId = ownerIndex,
      ControllerId = controllerIndex,
      IsTapped = card.IsTapped,
      IsToken = card.IsToken,
      IsLand = card.IsLand,
      IsActivatedAbility = card.IsActivatedAbility,
      IsTriggeredAbility = card.IsTriggeredAbility
    };
  }

  /// <summary>
  /// Builds a GamePlayerModel without touching the database.
  /// FirstSeenStateId is set during flush.
  /// </summary>
  internal static GamePlayerModel BuildGamePlayerModel(
    int gameId, GamePlayer player, int playerIndex)
  {
    string? manaPoolJson = null;
    try
    {
      List<Mana>? mana = null;
      if (Unbind(player) is GamePlayerPartial partial)
        mana = partial.ManaPool.ToList();
      else
        mana = player.ManaPool?.ToList();

      if (mana?.Count > 0)
        manaPoolJson = JsonSerializer.Serialize(
          mana.Select(m => new
          {
            Symbol = Mana.ToSymbol(m.Color),
            Amount = m.Amount
          }), JsonSerializerOptions.Web);
    }
    catch { }

    return new GamePlayerModel
    {
      GameId = gameId,
      PlayerIndex = playerIndex,
      Name = player.Name,
      InitialLife = player.Life,
      InitialHandCount = player.HandCount,
      InitialLibraryCount = player.LibraryCount,
      InitialGraveyardCount = player.GraveyardCount,
      InitialManaPool = manaPoolJson,
      IsActivePlayer = player.IsActivePlayer,
      ClockRemaining = player.ChessClock.TotalSeconds,
      UserId = player.UserId,
      AvatarId = player.AvatarId
    };
  }

  /// <summary>
  /// Flushes all buffered models for a single snapshot tick in one
  /// scope + transaction. Creates the GameStateModel and sets all child FKs.
  /// Returns the GameStateModel's database ID, or -1 on failure.
  /// </summary>
  public int FlushStateData(
    int gameId,
    GameStateSnapshot snapshot,
    List<GameCardModel> cards,
    List<GamePlayerModel> players,
    List<ZoneTransferModel> zoneTransfers,
    List<CardStateChangeModel> cardChanges,
    List<PlayerStateChangeModel> playerChanges,
    List<GameActionModel> actions,
    List<GameLogModel> logs,
    string? promptOptions = null)
  {
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<EventContext>();

    try
    {
      using var transaction = context.Database.BeginTransaction();

      // Get or create the GameStateModel
      var state = context.GameStates
        .FirstOrDefault(s => s.GameId == gameId && s.Nonce == snapshot.Nonce);

      if (state == null)
      {
        state = new GameStateModel
        {
          GameId = gameId,
          Nonce = snapshot.Nonce,
          Timestamp = snapshot.Timestamp,
          ActionTimestamp = snapshot.ActionTimestamp,
          ClientTimestamp = snapshot.ClientTimestamp,
          TurnNumber = snapshot.TurnNumber,
          CurrentPhase = snapshot.CurrentPhase.ToString(),
          PromptedPlayer = snapshot.PromptedPlayer,
          PromptText = snapshot.PromptText,
          PromptOptions = promptOptions
        };
        context.GameStates.Add(state);
        // SaveChanges to get the auto-generated state.Id for child FKs
        context.SaveChanges();
      }
      else if (promptOptions != null && state.PromptOptions == null)
      {
        // Re-flush from ForceFlush recovery — merge prompt options that
        // were missing from the initial flush into the existing record.
        state.PromptOptions = promptOptions;
        context.SaveChanges();
      }

      int stateId = state.Id;

      // Set FKs on all child models
      foreach (var card in cards)
        card.FirstSeenStateId = stateId;
      foreach (var player in players)
        player.FirstSeenStateId = stateId;
      foreach (var transfer in zoneTransfers)
        transfer.GameStateId = stateId;
      foreach (var change in cardChanges)
        change.GameStateId = stateId;
      foreach (var playerChange in playerChanges)
        playerChange.GameStateId = stateId;
      foreach (var action in actions)
        action.GameStateId = stateId;
      foreach (var log in logs)
        log.GameStateId = stateId;

      // Batch insert everything
      if (cards.Count > 0) context.GameCards.AddRange(cards);
      if (players.Count > 0) context.GamePlayers.AddRange(players);
      if (zoneTransfers.Count > 0) context.ZoneTransfers.AddRange(zoneTransfers);
      if (cardChanges.Count > 0) context.CardStateChanges.AddRange(cardChanges);
      if (playerChanges.Count > 0) context.PlayerStateChanges.AddRange(playerChanges);
      if (actions.Count > 0) context.GameActions.AddRange(actions);
      if (logs.Count > 0) context.GameLogs.AddRange(logs);

      context.SaveChanges();
      transaction.Commit();

      return stateId;
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error flushing game state data for game {GameId}: {Message}",
        gameId, ex.ToString());
      return -1;
    }
  }

  //
  // Table mutations
  //

  public bool TryUpdateGameResults(Game game, IList<GamePlayerResult> results)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Check if game already exists
        var existingGame = context.Games.FirstOrDefault(g => g.Id == game.Id);

        if (existingGame == null)
        {
          Log.Warning("Game {Id} not found for updating results", game.Id);
          return false;
        }

        // Update player results
        existingGame.GamePlayerResults.Clear();
        foreach (var result in results)
        {
          existingGame.GamePlayerResults.Add(result);
        }

        context.Entry(existingGame).State = EntityState.Modified;
        context.SaveChanges();
        transaction.Commit();
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while updating game results for game {GameId}: {Message}",
                  game.Id, ex.Message);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error updating game results for game {GameId}", game.Id);
        Log.Debug(ex.Message + "\n" + ex.StackTrace);
        return false;
      }
    }
  }

  public bool TryUpdateSideboardChanges(Match match, int gameId, List<CardEntry> changes)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Check if match already exists
        var existingMatch = context.Matches.FirstOrDefault(m => m.Id == match.Id);

        if (existingMatch == null)
        {
          Log.Warning("Match {Id} not found for updating sideboard changes", match.Id);
          return false;
        }

        // Update sideboard changes
        existingMatch.SideboardChanges.Add(gameId, changes);

        context.Entry(existingMatch).State = EntityState.Modified;
        context.SaveChanges();
        transaction.Commit();
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while updating sideboard changes for match {MatchId}: {Message}",
                  match.Id, ex.Message);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error updating sideboard changes for match {MatchId}", match.Id);
        return false;
      }
    }
  }

  public bool TryUpdateMatchResults(Match match, IList<PlayerResult> results)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Check if match already exists
        var existingMatch = context.Matches.FirstOrDefault(m => m.Id == match.Id);

        if (existingMatch == null)
        {
          Log.Warning("Match {Id} not found for updating results", match.Id);
          return false;
        }

        // Update player results
        existingMatch.PlayerResults.Clear();
        foreach (var result in results)
        {
          existingMatch.PlayerResults.Add(result);
        }

        context.Entry(existingMatch).State = EntityState.Modified;
        context.SaveChanges();
        transaction.Commit();
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while updating match results for match {MatchId}: {Message}",
                  match.Id, ex.Message);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error updating match results for match {MatchId}", match.Id);
        return false;
      }
    }
  }
}
