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
      Cache = SqliteCacheMode.Shared,
      Pooling = true,
    };

    builder.Services.AddSqlite<T>(connectionString.ToString());
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
      // Create the database directory if it does not exist.
      string directory = Path.GetDirectoryName(path)!;
      Directory.CreateDirectory(directory);

      using (var scope = provider.CreateScope())
      {
        var context = scope.ServiceProvider.GetRequiredService<T>();
        var db = context.Database;

        // Check if there are any pending migrations
        if ((await db.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
          Log.Debug("Performing database migrations for {0}.", typeof(T).Name);
          await db.MigrateAsync(cancellationToken);
        }
        else
        {
          // If there are no pending migrations, ensure the database is created.
          Log.Debug("No pending migrations found for {0}. Ensuring database is created.", typeof(T).Name);
          await db.EnsureCreatedAsync(cancellationToken);
        }
      }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      return Task.CompletedTask;
    }
  }
}
