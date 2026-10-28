/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.Core.Logging;


namespace MTGOTracker.Services;

/// <summary>
/// Provides methods for configuring the SQLite database service.
/// </summary>
public static class DatabaseService
{
  public sealed class DatabaseReadiness<T> where T : DbContext
  {
    private readonly TaskCompletionSource _ready =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
      _ready.Task.WaitAsync(cancellationToken);

    internal void SetReady() => _ready.TrySetResult();
    internal void SetException(Exception exception) =>
      _ready.TrySetException(exception);
  }

  /// <summary>
  /// Initializes the ASP.NET Core SQLite database service.
  /// </summary>
  /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to configure.</param>
  /// <param name="options">The application options.</param>
  /// <returns>The <see cref="WebApplicationBuilder"/> for chaining.</returns>
  public static IHostApplicationBuilder UseDatabase<T>(
    this IHostApplicationBuilder builder,
    ApplicationOptions options) where T : DbContext
  {
    string name = typeof(T).Name.Replace("Context", string.Empty);
    string path = Path.Combine(options.DatabasePath, $"{name}.db");

    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = path,
      Pooling = true,
      Mode = SqliteOpenMode.ReadWriteCreate,
      DefaultTimeout = 5
    };

    builder.Services.AddSqlite<T>(
      connectionString.ToString(),
      sqliteOptions => sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    builder.Services.AddSingleton<DatabaseReadiness<T>>();
    builder.Services.AddTransient<IHostedService>(provider =>
    {
      return new MigrationService<T>(provider, path);
    });

    return builder;
  }

  private class MigrationService<T>(IServiceProvider provider, string path)
      : IHostedService where T : DbContext
  {
    public async Task StartAsync(CancellationToken cancellationToken)
    {
      var readiness = provider.GetRequiredService<DatabaseReadiness<T>>();
      try
      {
        // Create the database directory if it does not exist.
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        using (var scope = provider.CreateScope())
        {
          var context = scope.ServiceProvider.GetRequiredService<T>();
          var db = context.Database;

          if ((await db.GetPendingMigrationsAsync(cancellationToken)).Any())
          {
            Log.Debug("Performing database migrations for {0}.", typeof(T).Name);
            await db.MigrateAsync(cancellationToken);
          }
          else
          {
            Log.Debug("No pending migrations found for {0}. Ensuring database is created.", typeof(T).Name);
            await db.EnsureCreatedAsync(cancellationToken);
          }

          await db.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
          await db.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);

          if (typeof(T).Name == "EventContext")
          {
            await EnsureColumnExistsAsync(context, "Events", "FormatType", "INTEGER", cancellationToken);
            await EnsureColumnExistsAsync(context, "Events", "TotalMatches", "INTEGER", cancellationToken);
            await EnsureColumnExistsAsync(context, "Matches", "MatchNumber", "INTEGER", cancellationToken);
            await EnsureColumnExistsAsync(context, "Games", "GameNumber", "INTEGER", cancellationToken);
            await EnsureColumnExistsAsync(context, "Games", "StartTime", "TEXT", cancellationToken);
            await EnsureColumnExistsAsync(context, "GameStates", "ServerNonce", "INTEGER", cancellationToken);
            await EnsureColumnExistsAsync(context, "GameStates", "Interaction", "TEXT", cancellationToken);
            await EnsureColumnExistsAsync(context, "Matches", "OpponentDeckArchetype", "TEXT", cancellationToken);
            await EnsureColumnExistsAsync(context, "Matches", "OpponentDeckColors", "TEXT", cancellationToken);
          }
          else if (typeof(T).Name == "CollectionContext")
          {
            await EnsureColumnExistsAsync(
              context,
              "CardGroupings",
              "IsLocal",
              "INTEGER NOT NULL DEFAULT 0",
              cancellationToken);
            await EnsureColumnExistsAsync(
              context,
              "CardGroupings",
              "PendingOperation",
              "INTEGER NOT NULL DEFAULT 0",
              cancellationToken);
            await EnsureColumnExistsAsync(
              context,
              "CardGroupings",
              "SyncError",
              "TEXT",
              cancellationToken);
            await MigrateSideboardGuidesAsync(context, directory, cancellationToken);
          }
        }

        readiness.SetReady();
      }
      catch (Exception ex)
      {
        readiness.SetException(ex);
        throw;
      }
    }

    /// <summary>
    /// Moves guides into collection history once. Keep the old file as a backup;
    /// the marker prevents later startups from restoring guides the user has deleted.
    /// </summary>
    private static async Task MigrateSideboardGuidesAsync(DbContext context, string directory, CancellationToken token)
    {
      var db = context.Database;
      string legacy = Path.Combine(directory, "Performance.db");
      bool attached = false;
      await db.OpenConnectionAsync(token);
      try
      {
        if (File.Exists(legacy))
        {
          await db.ExecuteSqlInterpolatedAsync($"ATTACH DATABASE {legacy} AS guide_backup", token);
          attached = true;
        }
        await using var transaction = await db.BeginTransactionAsync(token);
        await db.ExecuteSqlRawAsync("""
          CREATE TABLE IF NOT EXISTS SideboardGuides (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, AccountId INTEGER NOT NULL, DeckRevisionId INTEGER NOT NULL,
            OpponentArchetype TEXT COLLATE NOCASE NOT NULL, Notes TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
          CREATE UNIQUE INDEX IF NOT EXISTS IX_SideboardGuides_AccountId_DeckRevisionId_OpponentArchetype
            ON SideboardGuides (AccountId, DeckRevisionId, OpponentArchetype);
          CREATE TABLE IF NOT EXISTS SideboardGuideCards (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, SideboardGuideId INTEGER NOT NULL REFERENCES SideboardGuides(Id) ON DELETE CASCADE,
            CatalogId INTEGER NOT NULL, Quantity INTEGER NOT NULL, BringIn INTEGER NOT NULL);
          CREATE UNIQUE INDEX IF NOT EXISTS IX_SideboardGuideCards_SideboardGuideId_CatalogId_BringIn
            ON SideboardGuideCards (SideboardGuideId, CatalogId, BringIn);
          CREATE TABLE IF NOT EXISTS __TrackerMigrations (Id TEXT PRIMARY KEY NOT NULL);
          """, token);
        bool migrated = await db.SqlQueryRaw<int>("""
          SELECT COUNT(*) AS Value FROM __TrackerMigrations WHERE Id = 'collection-sideboard-guides'
          """).SingleAsync(token) > 0;
        if (!migrated && attached)
        {
          var tables = await db.SqlQueryRaw<string>("""
            SELECT name AS Value FROM guide_backup.sqlite_master WHERE type = 'table'
              AND name IN ('SideboardGuides', 'SideboardGuideCardModel')
            """).ToListAsync(token);
          if (tables.Contains("SideboardGuides"))
          {
            // Preserve IDs where possible, then import any guides whose IDs collided with existing rows.
            await db.ExecuteSqlRawAsync("""
              INSERT OR IGNORE INTO SideboardGuides (Id, AccountId, DeckRevisionId, OpponentArchetype, Notes, UpdatedAt)
                SELECT Id, AccountId, DeckRevisionId, OpponentArchetype, Notes, UpdatedAt FROM guide_backup.SideboardGuides;
              INSERT INTO SideboardGuides (AccountId, DeckRevisionId, OpponentArchetype, Notes, UpdatedAt)
                SELECT old.AccountId, old.DeckRevisionId, old.OpponentArchetype, old.Notes, old.UpdatedAt
                FROM guide_backup.SideboardGuides old WHERE NOT EXISTS (SELECT 1 FROM SideboardGuides current
                  WHERE current.AccountId = old.AccountId AND current.DeckRevisionId = old.DeckRevisionId
                    AND current.OpponentArchetype = old.OpponentArchetype);
              """, token);
            if (tables.Contains("SideboardGuideCardModel")) await db.ExecuteSqlRawAsync("""
              INSERT OR IGNORE INTO SideboardGuideCards (SideboardGuideId, CatalogId, Quantity, BringIn)
                SELECT current.Id, card.CatalogId, card.Quantity, card.BringIn
                FROM guide_backup.SideboardGuideCardModel card
                JOIN guide_backup.SideboardGuides old ON old.Id = card.SideboardGuideId
                JOIN SideboardGuides current ON current.AccountId = old.AccountId
                  AND current.DeckRevisionId = old.DeckRevisionId AND current.OpponentArchetype = old.OpponentArchetype;
              """, token);
          }
        }
        await db.ExecuteSqlRawAsync("INSERT OR IGNORE INTO __TrackerMigrations VALUES ('collection-sideboard-guides')", token);
        await transaction.CommitAsync(token);
      }
      finally
      {
        if (attached) await db.ExecuteSqlRawAsync("DETACH DATABASE guide_backup", CancellationToken.None);
        await db.CloseConnectionAsync();
      }
    }

    private static async Task EnsureColumnExistsAsync(
      DbContext context,
      string tableName,
      string columnName,
      string columnType,
      CancellationToken cancellationToken)
    {
      var connection = context.Database.GetDbConnection();
      bool wasOpen = connection.State == System.Data.ConnectionState.Open;
      if (!wasOpen) await connection.OpenAsync(cancellationToken);

      try
      {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        bool exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
          if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
          {
            exists = true;
            break;
          }
        }
        await reader.CloseAsync();

        if (!exists)
        {
#pragma warning disable EF1002
          await context.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};",
            cancellationToken);
#pragma warning restore EF1002
        }
      }
      finally
      {
        if (!wasOpen) await connection.CloseAsync();
      }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      return Task.CompletedTask;
    }
  }
}
