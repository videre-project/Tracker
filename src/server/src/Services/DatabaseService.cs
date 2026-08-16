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


namespace Tracker.Services;

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
            await EnsureColumnExistsAsync(context, "Matches", "OpponentDeckArchetype", "TEXT", cancellationToken);
            await EnsureColumnExistsAsync(context, "Matches", "OpponentDeckColors", "TEXT", cancellationToken);
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
          await context.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};",
            cancellationToken);
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
