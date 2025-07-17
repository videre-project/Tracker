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
  public DbSet<GameLogModel> GameLogs { get; set; }

  private static readonly JsonSerializerOptions s_jsonOptions = new()
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
          e => JsonSerializer.Serialize(e, s_jsonOptions),
          e => JsonSerializer.Deserialize<List<CardEntry>>(e, s_jsonOptions)!)
      .Metadata
        .SetValueComparer(CardEntryComparerExtensions.CardEntryListComparer);

    modelBuilder.Entity<DeckModel>()
      .Property(d => d.Sideboard)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_jsonOptions),
          e => JsonSerializer.Deserialize<List<CardEntry>>(e, s_jsonOptions)!)
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

    modelBuilder.Entity<GameModel>()
      .HasMany(g => g.GameLogs)
      .WithOne(e => e.Game)
      .HasForeignKey(e => e.GameId)
      .OnDelete(DeleteBehavior.Cascade);

    //
    // Match relationships
    //

    modelBuilder.Entity<MatchModel>()
      .Property(m => m.PlayerResults)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_jsonOptions),
          e => JsonSerializer.Deserialize<List<PlayerResult>>(e, s_jsonOptions)!)
      .Metadata
        .SetValueComparer(PlayerResultComparerExtensions.PlayerResultListComparer);

    modelBuilder.Entity<MatchModel>()
      .Property(m => m.SideboardChanges)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_jsonOptions),
          e => JsonSerializer.Deserialize<Dictionary<int, List<CardEntry>>>(e, s_jsonOptions)!)
      .Metadata
        .SetValueComparer(SideboardChangesComparerExtensions.SideboardChangesComparer);

    //
    // Game relationships
    //

    modelBuilder.Entity<GameModel>()
      .Property(d => d.GamePlayerResults)
      .HasConversion(
          e => JsonSerializer.Serialize(e, s_jsonOptions),
          e => JsonSerializer.Deserialize<List<GamePlayerResult>>(e, s_jsonOptions)!)
      .Metadata
        .SetValueComparer(GamePlayerResultComparerExtensions.GamePlayerResultListComparer);
  }
}
