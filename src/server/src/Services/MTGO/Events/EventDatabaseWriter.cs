/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Serialization;

using MTGOTracker.Database;
using MTGOTracker.Database.Models;
using MTGOTracker.Database.Models.Events;
using MTGOTracker.Database.Extensions;
using MTGOTracker.Services.MTGO.Collection;


namespace MTGOTracker.Services.MTGO.Events;

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

        PlayFormatType? formatType = null;
        try
        {
          var category = eventObj.Format.Type;
          if (category != PlayFormatType.Null) formatType = category;
        }
        catch (Exception exception)
        {
          // Event recording must continue if MTGO hasn't exposed format metadata yet.
          Log.Debug(exception, "Could not read format type for event {EventId}", eventId);
        }

        eventModel = new EventModel
        {
          Id = eventId,
          Format = eventObj.Format,
          FormatType = formatType,
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
          existingGame.StartTime = game.StartTime.ToUniversalTime();
          context.SaveChanges();
          gameModel = existingGame;
          transaction.Commit();
          return false;
        }

        // First create the game
        gameModel = new GameModel
        {
          Id = game.Id,
          StartTime = game.StartTime.ToUniversalTime()
        };

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
  /// Reads recorded games for a match, including their saved results.
  /// </summary>
  public async Task<List<GameModel>> GetGamesAsync(int matchId)
  {
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<EventContext>();
    return await context.Games.AsNoTracking()
      .Where(game => game.MatchId == matchId).ToListAsync();
  }

  /// <summary>
  /// Reads the saved game opening before SDK processor initialization.
  /// </summary>
  internal DateTime? GetRecordedGameStartTime(int gameId)
  {
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<EventContext>();
    return context.Games.Where(game => game.Id == gameId)
      .Select(game => game.StartTime).FirstOrDefault();
  }

  //
  // Structured game state methods
  //

  internal int? GetLastRecordedNonce(int gameId)
  {
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<EventContext>();
    return context.GameStates.Where(state => state.GameId == gameId)
      .OrderByDescending(state => state.Timestamp).ThenByDescending(state => state.Id)
      .Select(state => (int?)state.Nonce).FirstOrDefault();
  }

  /// <summary>
  /// Builds replay associations, including the SDK's display attachment parent.
  /// </summary>
  internal static Dictionary<string, List<int>> BuildCardAssociations(GameCard card)
  {
    var associations = card.Associations
      .GroupBy(association => association.Association.ToString())
      .ToDictionary(group => group.Key, group => group.Select(association => association.TargetId).ToList());
    int attachedToId = card.AttachedToId;
    if (attachedToId > 0) associations["AttachedTo"] = [attachedToId];
    return associations;
  }

  /// <summary>
  /// Builds a GameCardModel from a GameCard without touching the database.
  /// FirstSeenStateId is set during flush.
  /// </summary>
  internal static GameCardModel BuildGameCardModel(
    int gameId, GameCard card,
    int ownerIndex, int controllerIndex,
    string? initialZoneOverride = null)
  {
    bool hasState = Unbind(card) is not GameCardPartial { HasResolvedState: false };
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
      if (hasState) manaCost = card.Definition?.ManaCost;
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
      RulesText = hasState && !string.IsNullOrEmpty(card.RulesText) ? card.RulesText : null,
      ManaCost = !string.IsNullOrEmpty(manaCost) ? manaCost : null,
      TextureId = ctn > 0 ? ctn : null,
      CatalogId = catalogId,
      InitialZone = initialZoneOverride ?? card.Zone?.Name ?? "Unknown",
      InitialPower = hasState && card.Toughness != 0 ? card.Power.ToString() : null,
      InitialToughness = hasState && card.Toughness != 0 ? card.Toughness.ToString() : null,
      InitialCounters = countersJson,
      InitialAbilities = abilitiesJson,
      OwnerId = ownerIndex,
      ControllerId = controllerIndex,
      IsTapped = hasState && card.IsTapped,
      IsToken = hasState && card.IsToken,
      IsLand = hasState && card.IsLand,
      IsActivatedAbility = hasState && card.IsActivatedAbility,
      IsTriggeredAbility = hasState && card.IsTriggeredAbility
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
  /// Persists a completed publication and its buffered gameplay changes in one
  /// transaction. Server-only final messages can omit the publication metadata.
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
    IReadOnlyList<PromptOptionData>? promptOptions = null,
    int? nonce = null,
    GameInteractionData? interaction = null,
    DateTime? capturedAt = null)
  {
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<EventContext>();

    try
    {
      using var transaction = context.Database.BeginTransaction();

      // Get or create the GameStateModel
      var state = context.GameStates
        .FirstOrDefault(s => s.GameId == gameId && s.Nonce == (nonce ?? snapshot.Nonce));

      if (state == null)
      {
        state = new GameStateModel
        {
          GameId = gameId,
          Nonce = nonce ?? snapshot.Nonce,
          ServerNonce = nonce.HasValue ? snapshot.Nonce : null,
          Timestamp = snapshot.Timestamp,
          ActionTimestamp = snapshot.ActionTimestamp,
          ClientTimestamp = capturedAt ?? snapshot.ClientTimestamp,
          TurnNumber = snapshot.TurnNumber,
          CurrentPhase = snapshot.CurrentPhase.ToString(),
          PromptedPlayer = snapshot.PromptedPlayer,
          PromptText = snapshot.PromptText,
          PromptOptions = promptOptions,
          Interaction = interaction
        };
        context.GameStates.Add(state);
        // SaveChanges to get the auto-generated state.Id for child FKs
        context.SaveChanges();
      }
      else
      {
        state.PromptOptions ??= promptOptions;
        state.Interaction ??= interaction;
        state.ServerNonce ??= nonce.HasValue ? snapshot.Nonce : null;
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
      // Reconnecting observes existing cards and players again. Keep their
      // original metadata and first-seen state rather than adding new identities.
      if (cards.Count > 0)
      {
        var recordedCards = context.GameCards.Where(card => card.GameId == gameId)
          .OrderBy(card => card.FirstSeenStateId).ThenBy(card => card.Id)
          .ToList().DistinctBy(card => card.CardId).ToDictionary(card => card.CardId);
        ReconcileCardObservations(context, gameId, stateId, snapshot,
          cards, recordedCards, cardChanges);
        var cardIds = recordedCards.Keys.ToHashSet();
        context.GameCards.AddRange(cards.Where(card => cardIds.Add(card.CardId)));
      }
      if (players.Count > 0)
      {
        var recordedPlayers = context.GamePlayers.Where(player => player.GameId == gameId)
          .OrderBy(player => player.FirstSeenStateId).ThenBy(player => player.Id)
          .ToList().DistinctBy(player => player.PlayerIndex).ToDictionary(player => player.PlayerIndex);
        ReconcilePlayerObservations(context, gameId, stateId, snapshot,
          players, recordedPlayers, playerChanges);
        var playerIndices = recordedPlayers.Keys.ToHashSet();
        context.GamePlayers.AddRange(players.Where(player => playerIndices.Add(player.PlayerIndex)));
      }
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

  // A resumed SDK processor reports existing cards as initial observations.
  // Keep their original rows, and record changes from their latest saved values.
  private static void ReconcileCardObservations(
    EventContext context, int gameId, int stateId, GameStateSnapshot snapshot,
    List<GameCardModel> observations, Dictionary<int, GameCardModel> recordedCards,
    List<CardStateChangeModel> changes)
  {
    var observedIds = observations.Where(card => recordedCards.ContainsKey(card.CardId))
      .Select(card => card.CardId).ToArray();
    if (observedIds.Length == 0) return;

    var latestChanges = context.CardStateChanges
      .Where(change => change.GameState.GameId == gameId
        && observedIds.Contains(change.CardId))
      .OrderByDescending(change => change.GameState.Timestamp)
      .ThenByDescending(change => change.GameStateId).ThenByDescending(change => change.Id)
      .ToList().DistinctBy(change => (change.CardId, change.Property))
      .ToDictionary(change => (change.CardId, change.Property), change => (string?)change.NewValue);

    foreach (var observed in observations.Where(card => recordedCards.ContainsKey(card.CardId)))
    {
      // Revealed placeholders establish identity without supplying card state.
      var captured = snapshot.Cards.GetValueOrDefault(observed.CardId)
        ?? snapshot.HiddenCards.GetValueOrDefault(observed.CardId)
        ?? snapshot.RevealedCards.Values.FirstOrDefault(card => card.Id == observed.CardId);
      if (captured is not null
        && Unbind(captured) is GameCardPartial { HasResolvedState: false })
        continue;

      var previous = CardObservationValues(recordedCards[observed.CardId]);
      var current = CardObservationValues(observed);
      if (captured is not null)
      {
        current["Associations"] = JsonSerializer.Serialize(BuildCardAssociations(captured), JsonSerializerOptions.Web);
        previous["Associations"] = "{}";
      }
      var pending = changes.Where(change => change.CardId == observed.CardId).ToArray();
      // Initial property rows (for example TypeLine) also carry current values.
      foreach (var change in pending)
      {
        current[change.Property] = change.NewValue;
        previous.TryAdd(change.Property, change.OldValue);
      }
      changes.RemoveAll(change => change.CardId == observed.CardId);

      foreach (var (property, value) in current)
      {
        string? oldValue = latestChanges.GetValueOrDefault((observed.CardId, property),
          previous.GetValueOrDefault(property));
        if (string.Equals(oldValue, value, StringComparison.Ordinal)) continue;
        changes.Add(new CardStateChangeModel
        {
          GameStateId = stateId, CardId = observed.CardId, CardName = observed.Name,
          Property = property, OldValue = oldValue, NewValue = value ?? ""
        });
      }
    }
  }

  private static Dictionary<string, string?> CardObservationValues(GameCardModel card) => new()
  {
    ["Name"] = card.Name,
    ["IsTapped"] = card.IsTapped.ToString(),
    ["Power"] = card.InitialPower,
    ["Toughness"] = card.InitialToughness,
    ["Counters"] = card.InitialCounters ?? "{}",
    ["Abilities"] = card.InitialAbilities ?? "[]",
    ["RulesText"] = card.RulesText,
    ["TypeLine"] = card.TypeLine,
    ["IsToken"] = card.IsToken.ToString(),
    ["IsLand"] = card.IsLand.ToString(),
    ["IsActivatedAbility"] = card.IsActivatedAbility.ToString(),
    ["IsTriggeredAbility"] = card.IsTriggeredAbility.ToString()
  };

  // Reattachment supplies a fresh player baseline. Preserve the original row
  // and replace initial changes with deltas from the last recorded values.
  private static void ReconcilePlayerObservations(
    EventContext context, int gameId, int stateId, GameStateSnapshot snapshot,
    List<GamePlayerModel> observations, Dictionary<int, GamePlayerModel> recordedPlayers,
    List<PlayerStateChangeModel> changes)
  {
    var observedIndices = observations.Where(player => recordedPlayers.ContainsKey(player.PlayerIndex))
      .Select(player => player.PlayerIndex).ToArray();
    if (observedIndices.Length == 0) return;

    var latestChanges = context.PlayerStateChanges
      .Where(change => change.GameState.GameId == gameId
        && observedIndices.Contains(change.PlayerIndex))
      .OrderByDescending(change => change.GameState.Timestamp)
      .ThenByDescending(change => change.GameStateId).ThenByDescending(change => change.Id)
      .ToList().DistinctBy(change => (change.PlayerIndex, change.Property))
      .ToDictionary(change => (change.PlayerIndex, change.Property), change => change.NewValue);

    foreach (var observed in observations.Where(player => recordedPlayers.ContainsKey(player.PlayerIndex)))
    {
      var previous = PlayerObservationValues(recordedPlayers[observed.PlayerIndex]);
      var current = PlayerObservationValues(observed);
      var captured = snapshot.Players[observed.PlayerIndex];
      current["HasPriority"] = captured.HasPriority.ToString();
      current["Counters"] = JsonSerializer.Serialize(captured.Counters, JsonSerializerOptions.Web);
      changes.RemoveAll(change => change.PlayerIndex == observed.PlayerIndex);

      foreach (var (property, value) in current)
      {
        string oldValue = latestChanges.GetValueOrDefault((observed.PlayerIndex, property), previous[property]);
        if (SamePlayerValue(property, oldValue, value)) continue;

        changes.Add(new PlayerStateChangeModel
        {
          GameStateId = stateId, PlayerIndex = observed.PlayerIndex, PlayerName = observed.Name,
          Property = property, OldValue = oldValue, NewValue = value
        });
      }
    }
  }

  private static Dictionary<string, string> PlayerObservationValues(GamePlayerModel player) => new()
  {
    ["Life"] = player.InitialLife.ToString(CultureInfo.InvariantCulture),
    ["HandCount"] = player.InitialHandCount.ToString(CultureInfo.InvariantCulture),
    ["LibraryCount"] = player.InitialLibraryCount.ToString(CultureInfo.InvariantCulture),
    ["GraveyardCount"] = player.InitialGraveyardCount.ToString(CultureInfo.InvariantCulture),
    ["ManaPool"] = player.InitialManaPool ?? "[]",
    ["IsActivePlayer"] = player.IsActivePlayer.ToString(),
    ["ClockRemaining"] = player.ClockRemaining.ToString("F1", CultureInfo.InvariantCulture),
    ["HasPriority"] = bool.FalseString,
    ["Counters"] = "{}"
  };

  private static bool SamePlayerValue(string property, string previous, string current) => property switch
  {
    "ManaPool" => NormalizeRecordedManaPool(previous) == NormalizeRecordedManaPool(current),
    "Counters" => JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(previous),
      JsonSerializer.Deserialize<JsonElement>(current)),
    _ => string.Equals(previous, current, StringComparison.Ordinal)
  };

  private static string NormalizeRecordedManaPool(string json) =>
    JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json).EnumerateArray()
      .GroupBy(mana => mana.GetProperty("symbol").GetString())
      .OrderBy(group => group.Key)
      .Select(group => new { Symbol = group.Key, Amount = group.Sum(mana => mana.GetProperty("amount").GetInt32()) }),
      JsonSerializerOptions.Web);

  //
  // Table mutations
  //

  public bool TryUpdateGameResults(
    int gameId, IList<GamePlayerResult> results, out int matchId)
  {
    matchId = 0;
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Check if game already exists
        var existingGame = context.Games.FirstOrDefault(g => g.Id == gameId);

        if (existingGame == null)
        {
          Log.Warning("Game {Id} not found for updating results", gameId);
          return false;
        }

        var recorded = context.GameStates
          .Where(state => state.GameId == gameId)
          .GroupBy(state => state.GameId)
          .Select(group => new
          {
            Start = group.Min(state => state.ClientTimestamp),
            End = group.Max(state => state.ClientTimestamp)
          }).FirstOrDefault();
        TimeSpan? recordedDuration = recorded != null && recorded.End > recorded.Start
          ? recorded.End - recorded.Start : null;

        // Match-server rankings can arrive after game-server results. Preserve
        // play/draw from the richer message rather than replacing it with Unknown.
        existingGame.GamePlayerResults = results.Select(result =>
        {
          var previous = existingGame.GamePlayerResults
            .FirstOrDefault(entry => entry.Player == result.Player);
          var playDraw = result.PlayDraw == PlayDrawResult.Unknown && previous != null
            ? previous.PlayDraw : result.PlayDraw;
          return new GamePlayerResult(result.Player, playDraw, result.Result,
            recordedDuration ?? result.Clock);
        }).ToList();

        context.Entry(existingGame).State = EntityState.Modified;
        context.SaveChanges();
        transaction.Commit();
        matchId = existingGame.MatchId;
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while updating game results for game {GameId}: {Message}",
                  gameId, ex.Message);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error updating game results for game {GameId}", gameId);
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

  public bool TryUpdateMatchResults(int matchId, IList<PlayerResult> results)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Check if match already exists
        var existingMatch = context.Matches.FirstOrDefault(m => m.Id == matchId);

        if (existingMatch == null)
        {
          Log.Warning("Match {Id} not found for updating results", matchId);
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
                  matchId, ex.Message);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error updating match results for match {MatchId}", matchId);
        return false;
      }
    }
  }
}
