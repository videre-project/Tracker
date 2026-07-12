/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using Tracker.Database.Models;
using Tracker.Database.Extensions;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;


namespace Tracker.Database;

public class EventContext(DbContextOptions<EventContext> options)
    : DbContext(options)
{
  public DbSet<EventModel> Events { get; set; }
  public DbSet<DeckModel> Decks { get; set; }
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

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Ignore<CardEntry>();
    modelBuilder.Ignore<GamePlayerResult>();
    modelBuilder.Ignore<PlayerResult>();
    modelBuilder.Ignore<Dictionary<int, List<CardEntry>>>();

    //
    // Deck relationships
    //

    modelBuilder.Entity<DeckModel>()
      .HasKey(d => d.Hash);

    modelBuilder.Entity<DeckModel>()
      .Property(d => d.Mainboard)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_databaseJsonOptions),
          e => JsonSerializer.Deserialize<List<CardEntry>>(e, s_databaseJsonOptions)!)
      .Metadata
        .SetValueComparer(CardEntryComparerExtensions.CardEntryListComparer);

    modelBuilder.Entity<DeckModel>()
      .Property(d => d.Sideboard)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_databaseJsonOptions),
          e => JsonSerializer.Deserialize<List<CardEntry>>(e, s_databaseJsonOptions)!)
      .Metadata
        .SetValueComparer(CardEntryComparerExtensions.CardEntryListComparer);

    modelBuilder.Entity<EventModel>()
      .HasOne(e => e.Deck)
      .WithMany()
      .HasForeignKey(e => e.DeckHash)
      .IsRequired(false);

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

    // Game → Cards, Players, States
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

    // GameState → child event tables
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
