/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.EntityFrameworkCore;

using Tracker.Database.Models;


namespace Tracker.Database;

public class EventContext(DbContextOptions<EventContext> options)
    : DbContext(options)
{
  public DbSet<EventModel> Events { get; set; }
  public DbSet<MatchModel> Matches { get; set; }
  public DbSet<GameModel> Games { get; set; }
  public DbSet<GameLogModel> GameLogs { get; set; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
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
  }
}
