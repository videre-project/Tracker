/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Tracker.Database.Models.Collection;


namespace Tracker.Database;

public sealed class CollectionContext(DbContextOptions<CollectionContext> options)
    : DbContext(options)
{
  public DbSet<AccountModel> Accounts => Set<AccountModel>();
  public DbSet<CardGroupingModel> CardGroupings => Set<CardGroupingModel>();
  public DbSet<CardGroupingRevisionModel> CardGroupingRevisions =>
    Set<CardGroupingRevisionModel>();
  public DbSet<DeckRevisionEnrichmentModel> DeckRevisionEnrichments =>
    Set<DeckRevisionEnrichmentModel>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<AccountModel>(entity =>
    {
      entity.ToTable("Accounts", table =>
      {
        table.HasCheckConstraint(
          "CK_Accounts_Id", "Id > 0");
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

    modelBuilder.Entity<CardGroupingModel>(entity =>
    {
      entity.ToTable("CardGroupings", table =>
      {
        table.HasCheckConstraint(
          "CK_CardGroupings_Kind",
          "Kind IN (0, 1, 2, 3)");
        table.HasCheckConstraint(
          "CK_CardGroupings_NetDeckId",
          "(Kind = 0 AND NetDeckId = 0) OR " +
          "(Kind IN (1, 2, 3) AND NetDeckId > 0)");
        table.HasCheckConstraint(
          "CK_CardGroupings_CollectionMetadata",
          "Kind <> 0 OR " +
          "(Timestamp IS NULL AND Name IS NULL AND FormatCode IS NULL)");
      });
      entity.HasKey(grouping => grouping.Id);
      var groupingId = entity.Property(grouping => grouping.Id)
        .ValueGeneratedOnAdd();
      groupingId.Metadata.SetValueGenerationStrategy(
        SqliteValueGenerationStrategy.None);
      entity.Property(grouping => grouping.Timestamp)
        .HasConversion(
          value => value.HasValue ? value.Value.Ticks : (long?)null,
          value => value.HasValue
            ? new DateTime(value.Value, DateTimeKind.Unspecified)
            : null);
      entity.HasIndex(grouping => new
      {
        grouping.AccountId,
        grouping.Kind,
        grouping.NetDeckId,
      }).IsUnique();
      entity.HasOne(grouping => grouping.Account)
        .WithMany(account => account.CardGroupings)
        .HasForeignKey(grouping => grouping.AccountId)
        .OnDelete(DeleteBehavior.Restrict);
    });

    modelBuilder.Entity<CardGroupingRevisionModel>(entity =>
    {
      entity.ToTable("CardGroupingRevisions", table =>
      {
        table.HasCheckConstraint(
          "CK_CardGroupingRevisions_Type",
          "RevisionType IN (0, 1, 2)");
        table.HasCheckConstraint(
          "CK_CardGroupingRevisions_Payload",
          "(RevisionType = 2 AND Payload IS NULL) OR " +
          "(RevisionType IN (0, 1) AND Payload IS NOT NULL)");
      });
      entity.HasKey(revision => revision.Id);
      var revisionId = entity.Property(revision => revision.Id)
        .ValueGeneratedOnAdd();
      revisionId.Metadata.SetValueGenerationStrategy(
        SqliteValueGenerationStrategy.None);
      entity.Property(revision => revision.ObservedAt)
        .HasConversion(
          value => value.Ticks,
          value => new DateTime(value, DateTimeKind.Utc));
      entity.HasIndex(revision => new
      {
        revision.CardGroupingId,
        revision.Id,
      });
      entity.HasOne(revision => revision.CardGrouping)
        .WithMany(grouping => grouping.Revisions)
        .HasForeignKey(revision => revision.CardGroupingId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<DeckRevisionEnrichmentModel>(entity =>
    {
      entity.ToTable("DeckRevisionEnrichments");
      entity.HasKey(enrichment => enrichment.CardGroupingRevisionId);
      entity.Property(enrichment => enrichment.CardGroupingRevisionId)
        .ValueGeneratedNever();
      entity.HasOne(enrichment => enrichment.CardGroupingRevision)
        .WithOne(revision => revision.DeckEnrichment)
        .HasForeignKey<DeckRevisionEnrichmentModel>(
          enrichment => enrichment.CardGroupingRevisionId)
        .OnDelete(DeleteBehavior.Cascade);
    });
  }
}
