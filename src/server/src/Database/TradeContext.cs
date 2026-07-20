/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Tracker.Database.Models.Trades;


namespace Tracker.Database;

public sealed class TradeContext(DbContextOptions<TradeContext> options)
    : DbContext(options)
{
  public DbSet<AccountModel> Accounts => Set<AccountModel>();
  public DbSet<TradeEscrowModel> TradeEscrows => Set<TradeEscrowModel>();
  public DbSet<TradeEscrowItemModel> TradeEscrowItems =>
    Set<TradeEscrowItemModel>();
  public DbSet<TradeEscrowMessageModel> TradeEscrowMessages =>
    Set<TradeEscrowMessageModel>();
  public DbSet<TradeEscrowErrorModel> TradeEscrowErrors =>
    Set<TradeEscrowErrorModel>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<AccountModel>(entity =>
    {
      entity.ToTable("Accounts", table =>
      {
        table.HasCheckConstraint("CK_Accounts_Id", "Id > 0");
        table.HasCheckConstraint(
          "CK_Accounts_Username",
          "length(trim(Username)) > 0");
      });
      entity.HasKey(account => account.Id);
      entity.Property(account => account.Id).ValueGeneratedNever();
      entity.Property(account => account.Username)
        .IsRequired()
        .UseCollation("NOCASE");
    });

    modelBuilder.Entity<TradeEscrowModel>(entity =>
    {
      entity.ToTable("TradeEscrows", table =>
      {
        table.HasCheckConstraint(
          "CK_TradeEscrows_EscrowId",
          "EscrowId IS NULL OR EscrowId > 0");
        table.HasCheckConstraint(
          "CK_TradeEscrows_Kind",
          "Kind IN (0, 1)");
        table.HasCheckConstraint(
          "CK_TradeEscrows_Result",
          "Result IN (0, 1, 2, 3, 4, 5)");
        table.HasCheckConstraint(
          "CK_TradeEscrows_AttributionStatus",
          "AttributionStatus IN (0, 1, 2, 3, 4)");
      });
      entity.HasKey(escrow => escrow.Id);
      var escrowId = entity.Property(escrow => escrow.Id)
        .ValueGeneratedOnAdd();
      escrowId.Metadata.SetValueGenerationStrategy(
        SqliteValueGenerationStrategy.None);
      entity.Property(escrow => escrow.Token)
        .HasConversion(
          value => value.ToString("D"),
          value => Guid.Parse(value));
      entity.Property(escrow => escrow.PartnerName)
        .UseCollation("NOCASE");
      entity.Property(escrow => escrow.StartedAt)
        .HasConversion(
          value => value.Ticks,
          value => new DateTime(value, DateTimeKind.Utc));
      entity.Property(escrow => escrow.ClosedAt)
        .HasConversion(
          value => value.HasValue ? value.Value.Ticks : (long?)null,
          value => value.HasValue
            ? new DateTime(value.Value, DateTimeKind.Utc)
            : null);
      entity.HasIndex(escrow => new { escrow.AccountId, escrow.Token })
        .IsUnique();
      entity.HasIndex(escrow => new { escrow.AccountId, escrow.StartedAt });
      entity.HasOne(escrow => escrow.Account)
        .WithMany(account => account.TradeEscrows)
        .HasForeignKey(escrow => escrow.AccountId)
        .OnDelete(DeleteBehavior.Restrict);
    });

    modelBuilder.Entity<TradeEscrowItemModel>(entity =>
    {
      entity.ToTable("TradeEscrowItems", table =>
      {
        table.HasCheckConstraint(
          "CK_TradeEscrowItems_Role",
          "Role IN (0, 1, 2)");
        table.HasCheckConstraint(
          "CK_TradeEscrowItems_CatalogId",
          "CatalogId > 0");
        table.HasCheckConstraint(
          "CK_TradeEscrowItems_Quantity",
          "Quantity > 0");
      });
      entity.HasKey(item => new
      {
        item.TradeEscrowId,
        item.Role,
        item.CatalogId,
      });
      entity.HasOne(item => item.TradeEscrow)
        .WithMany(escrow => escrow.Items)
        .HasForeignKey(item => item.TradeEscrowId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<TradeEscrowMessageModel>(entity =>
    {
      entity.ToTable("TradeEscrowMessages");
      entity.HasKey(message => message.Id);
      var messageId = entity.Property(message => message.Id)
        .ValueGeneratedOnAdd();
      messageId.Metadata.SetValueGenerationStrategy(
        SqliteValueGenerationStrategy.None);
      entity.Property(message => message.Timestamp)
        .HasConversion(
          value => value.Ticks,
          value => new DateTime(value, DateTimeKind.Utc));
      entity.Property(message => message.Text).IsRequired();
      entity.HasIndex(message => new { message.TradeEscrowId, message.Id });
      entity.HasOne(message => message.TradeEscrow)
        .WithMany(escrow => escrow.Messages)
        .HasForeignKey(message => message.TradeEscrowId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<TradeEscrowErrorModel>(entity =>
    {
      entity.ToTable("TradeEscrowErrors");
      entity.HasKey(error => error.Id);
      var errorId = entity.Property(error => error.Id)
        .ValueGeneratedOnAdd();
      errorId.Metadata.SetValueGenerationStrategy(
        SqliteValueGenerationStrategy.None);
      entity.Property(error => error.ObservedAt)
        .HasConversion(
          value => value.Ticks,
          value => new DateTime(value, DateTimeKind.Utc));
      entity.HasIndex(error => new { error.TradeEscrowId, error.Id });
      entity.HasOne(error => error.TradeEscrow)
        .WithMany(escrow => escrow.Errors)
        .HasForeignKey(error => error.TradeEscrowId)
        .OnDelete(DeleteBehavior.Cascade);
    });
  }
}
