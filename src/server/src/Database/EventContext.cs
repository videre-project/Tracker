/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using MTGOTracker.Database.Models;
using MTGOTracker.Database.Models.Events;
using MTGOTracker.Database.Extensions;
using MTGOTracker.Services.MTGO.Events;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;


namespace MTGOTracker.Database;

public class EventContext(DbContextOptions<EventContext> options)
    : DbContext(options)
{
  /// <summary>
  /// Queries recorded matches, including older recordings that identify players only in game rows.
  /// Completion filtering uses the recorded match result, never the presence of replay states.
  /// </summary>
  public IQueryable<MatchModel> MatchesForPlayer(string username,
    DateTime? minDate = null, DateTime? maxDate = null, string? format = null, bool completedOnly = false,
    PlayFormatType? formatType = null, string[]? legacyFormats = null)
  {
    var query = Matches.FromSqlInterpolated($"""
      SELECT m.* FROM Matches m
      WHERE EXISTS (SELECT 1 FROM json_each(m.PlayerResults)
        WHERE json_extract(value, '$.player') = {username} COLLATE NOCASE
          AND ({completedOnly} = 0 OR json_extract(value, '$.result') COLLATE NOCASE IN ('Win', 'Loss', 'Draw', {(int)MatchResult.Win}, {(int)MatchResult.Loss}, {(int)MatchResult.Draw})))
        OR ({completedOnly} = 0 AND EXISTS (
          SELECT 1 FROM Games g JOIN GamePlayers p ON p.GameId = g.Id
          WHERE g.MatchId = m.Id AND p.Name = {username} COLLATE NOCASE))
      """).AsNoTracking().Include(match => match.Event).AsQueryable();
    if (minDate.HasValue) query = query.Where(match => match.Event.StartTime >= minDate.Value);
    if (maxDate.HasValue) query = query.Where(match => match.Event.StartTime <= maxDate.Value);
    if (!string.IsNullOrWhiteSpace(format)) query = query.Where(match => match.Event.Format == format);
    if (formatType is { } type)
    {
      legacyFormats ??= [];
      query = query.Where(match => match.Event.FormatType.HasValue
        ? (match.Event.FormatType.Value & type) != 0
        : legacyFormats.Contains(EF.Functions.Collate(match.Event.Format, "NOCASE")));
    }
    return query;
  }

  public DbSet<EventModel> Events { get; set; }
  public DbSet<MatchModel> Matches { get; set; }
  public DbSet<GameModel> Games { get; set; }
  public DbSet<GameCardModel> GameCards { get; set; }
  public DbSet<GamePlayerModel> GamePlayers { get; set; }
  public DbSet<GameStateModel> GameStates { get; set; }
  public DbSet<GameActionModel> GameActions { get; set; }
  public DbSet<CardStateChangeModel> CardStateChanges { get; set; }
  public DbSet<ZoneTransferModel> ZoneTransfers { get; set; }
  public DbSet<PlayerStateChangeModel> PlayerStateChanges { get; set; }
  public DbSet<GameLogModel> GameLogs { get; set; }

  private static readonly JsonSerializerOptions s_databaseJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    Converters =
    {
      new JsonStringEnumConverter(),
    }
  };

  private static readonly JsonSerializerOptions s_gameStateJsonOptions = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  private static string SerializeGameState<T>(T value) =>
    JsonSerializer.Serialize(value, s_gameStateJsonOptions);

  private static T DeserializeGameState<T>(string json) =>
    JsonSerializer.Deserialize<T>(json, s_gameStateJsonOptions)!;

  // JSON values are compared and copied by content so collection changes are tracked.
  private static ValueComparer<T> JsonValueComparer<T>() where T : class => new(
    (left, right) => SerializeGameState(left) == SerializeGameState(right),
    value => SerializeGameState(value).GetHashCode(),
    value => DeserializeGameState<T>(SerializeGameState(value)));

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Ignore<CardEntry>();
    modelBuilder.Ignore<GamePlayerResult>();
    modelBuilder.Ignore<PlayerResult>();
    modelBuilder.Ignore<Dictionary<int, List<CardEntry>>>();

    //
    // Event relationships
    //

    modelBuilder.Entity<EventModel>()
      .HasMany(e => e.Matches)
      .WithOne(m => m.Event)
      .HasForeignKey(m => m.EventId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<MatchModel>()
      .HasMany(m => m.Games)
      .WithOne(g => g.Match)
      .HasForeignKey(g => g.MatchId)
      .OnDelete(DeleteBehavior.Cascade);

    // Game -> Cards, Players, States
    modelBuilder.Entity<GameModel>()
      .HasMany(g => g.Cards)
      .WithOne(c => c.Game)
      .HasForeignKey(c => c.GameId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<GameModel>()
      .HasMany(g => g.Players)
      .WithOne(p => p.Game)
      .HasForeignKey(p => p.GameId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<GameModel>()
      .HasMany(g => g.States)
      .WithOne(s => s.Game)
      .HasForeignKey(s => s.GameId)
      .OnDelete(DeleteBehavior.Cascade);

    // GameState -> child event tables
    modelBuilder.Entity<GameStateModel>()
      .HasMany(s => s.Actions)
      .WithOne(a => a.GameState)
      .HasForeignKey(a => a.GameStateId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<GameStateModel>()
      .HasMany(s => s.CardChanges)
      .WithOne(c => c.GameState)
      .HasForeignKey(c => c.GameStateId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<GameStateModel>()
      .HasMany(s => s.ZoneTransfers)
      .WithOne(z => z.GameState)
      .HasForeignKey(z => z.GameStateId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<GameStateModel>()
      .HasMany(s => s.PlayerChanges)
      .WithOne(p => p.GameState)
      .HasForeignKey(p => p.GameStateId)
      .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<GameStateModel>()
      .HasMany(s => s.Logs)
      .WithOne(l => l.GameState)
      .HasForeignKey(l => l.GameStateId)
      .OnDelete(DeleteBehavior.Cascade);

    // GameCard.FirstSeenState FK (no cascade — Game cascade handles cleanup)
    modelBuilder.Entity<GameCardModel>()
      .HasOne(c => c.FirstSeenState)
      .WithMany()
      .HasForeignKey(c => c.FirstSeenStateId)
      .OnDelete(DeleteBehavior.NoAction);

    // GamePlayer.FirstSeenState FK (no cascade)
    modelBuilder.Entity<GamePlayerModel>()
      .HasOne(p => p.FirstSeenState)
      .WithMany()
      .HasForeignKey(p => p.FirstSeenStateId)
      .OnDelete(DeleteBehavior.NoAction);

    // Unique index on GameStateModel (GameId, Nonce) for correlation queries
    // and to prevent duplicate state rows for the same snapshot tick.
    modelBuilder.Entity<GameStateModel>()
      .HasIndex(s => new { s.GameId, s.Nonce })
      .IsUnique();

    modelBuilder.Entity<GameStateModel>()
      .Property(state => state.PromptOptions)
      .HasConversion(
        value => SerializeGameState(value),
        json => DeserializeGameState<IReadOnlyList<PromptOptionData>>(json))
      .Metadata.SetValueComparer(JsonValueComparer<IReadOnlyList<PromptOptionData>>());

    modelBuilder.Entity<GameStateModel>()
      .Property(state => state.Interaction)
      .HasConversion(
        value => SerializeGameState(value),
        json => GameInteractionData.Deserialize(json, s_gameStateJsonOptions))
      .Metadata.SetValueComparer(JsonValueComparer<GameInteractionData>());

    //
    // Match relationships
    //

    modelBuilder.Entity<MatchModel>()
      .Property(m => m.PlayerResults)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_databaseJsonOptions),
          e => JsonSerializer.Deserialize<List<PlayerResult>>(e, s_databaseJsonOptions)!)
      .Metadata
        .SetValueComparer(PlayerResultComparerExtensions.PlayerResultListComparer);

    modelBuilder.Entity<MatchModel>()
      .Property(m => m.SideboardChanges)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_databaseJsonOptions),
          e => JsonSerializer.Deserialize<Dictionary<int, List<CardEntry>>>(e, s_databaseJsonOptions)!)
      .Metadata
        .SetValueComparer(SideboardChangesComparerExtensions.SideboardChangesComparer);

    modelBuilder.Entity<MatchModel>()
      .Property(m => m.OpponentDeckColors)
      .HasConversion(
          e => e != null ? JsonSerializer.Serialize(e, s_databaseJsonOptions) : null,
          e => e != null ? JsonSerializer.Deserialize<List<string>>(e, s_databaseJsonOptions) : null)
      .Metadata.SetValueComparer(JsonValueComparer<List<string>>());

    //
    // Game relationships
    //

    modelBuilder.Entity<GameModel>()
      .Property(d => d.GamePlayerResults)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_databaseJsonOptions),
          e => JsonSerializer.Deserialize<List<GamePlayerResult>>(e, s_databaseJsonOptions)!)
      .Metadata
        .SetValueComparer(GamePlayerResultComparerExtensions.GamePlayerResultListComparer);
  }
}
