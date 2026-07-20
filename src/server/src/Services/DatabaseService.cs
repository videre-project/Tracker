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

    builder.Services.AddSqlite<T>(connectionString.ToString());
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
        }

        readiness.SetReady();
      }
      catch (Exception ex)
      {
        readiness.SetException(ex);
        throw;
      }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      return Task.CompletedTask;
    }
  }
}
