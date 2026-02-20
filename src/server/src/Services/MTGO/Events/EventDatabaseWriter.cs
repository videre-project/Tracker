/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;

using Tracker.Database;
using Tracker.Database.Models;


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

      try
      {
        using var transaction = context.Database.BeginTransaction();

        // Check if event already exists (use FirstOrDefault to prevent multiple enumeration)
        var existingEvent = context.Events.FirstOrDefault(e => e.Id == eventObj.Id);
        if (existingEvent != null)
        {
          eventModel = existingEvent;
          transaction.Commit();
          return false;
        }

        eventModel = new EventModel
        {
          Id = eventObj.Id,
          Format = eventObj.Format,
          Description = eventObj.Description,
          StartTime = Try(() => (eventObj as Tournament)?.StartTime) ?? startTime
        };
        if (eventObj.RegisteredDeck is Deck deck)
        {
          eventModel.DeckHash = deck.Hash;

          // Check if the deck already exists in the database
          DeckModel? deckModel = context.Decks.FirstOrDefault(d => d.Hash == deck.Hash);
          if (deckModel == null)
          {
            // If it doesn't exist, create a new DeckModel entry
            deckModel = DeckModel.ToModel(deck);

            // Fetch archetype and featured card from NBAC API
            try
            {
              var httpClientFactory = scope.ServiceProvider.GetService<IHttpClientFactory>();
              var appOptions = scope.ServiceProvider.GetService<ApplicationOptions>();
              if (httpClientFactory != null && appOptions != null)
              {
                var httpClient = httpClientFactory.CreateClient();
                // PopulateArchetypeAsync sets both Archetype and FeaturedCard
                deckModel.PopulateArchetypeAsync(httpClient, appOptions.NbacApiUrl)
                  .GetAwaiter().GetResult();
              }
            }
            catch (Exception ex)
            {
              Log.Warning("Failed to fetch archetype for deck {DeckHash}: {Message}",
                deck.Hash, ex.Message);
            }

            context.Decks.Add(deckModel);
          }

          eventModel.Deck = deckModel;
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
        eventModel = context.Events.FirstOrDefault(e => e.Id == eventObj.Id);
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

  public async Task<bool> WaitForGameModelAsync(
    int gameId,
    CancellationToken cancellationToken = default)
  {
    // Check if the game ID is already in the map
    if (s_gameIdMap.ContainsKey(gameId)) return true;

    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      // Wait until the GameModel is created before adding the log entry.
      if (!await WaitUntilAsync(async () =>
        await context.Games.AnyAsync(g => g.Id == gameId, cancellationToken),
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
                    cancellationToken)
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

  public async Task<bool> TryAddGameLogAsync(
    GameLogEntry entry,
    CancellationToken cancellationToken = default)
  {
    using (var scope = serviceProvider.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();

      try
      {
        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Hash the event to create a unique ID for it
        int hashId = entry.GameId * 1000000 +
          Math.Abs(
            HashCode.Combine(entry.Timestamp, entry.Type, entry.Data.GetHashCode())
          ) % 1000000;

        // Check if this log entry already exists
        if (await context.GameLogs.AnyAsync(g => g.Id == hashId, cancellationToken))
        {
          await transaction.CommitAsync(cancellationToken);
          return false;
        }

        var gameLogEntry = new GameLogModel
        {
          Id = hashId,
          GameId = entry.GameId,
          Timestamp = entry.Timestamp,
          GameLogType = entry.Type.ToString(),
          Data = entry.Data
        };

        await context.GameLogs.AddAsync(gameLogEntry, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        Log.Warning("Concurrency conflict occurred while adding game log for game {GameId}: {Message}",
                  entry.GameId, ex.Message);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error adding game log for game {GameId}", entry.GameId);
        return false;
      }
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
