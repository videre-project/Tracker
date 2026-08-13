/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

using Tracker.Controllers;
using Tracker.Database;
using Tracker.Services;
using Tracker.Services.MTGO;
using Tracker.Tests.Fakes;
using Tracker.WebView;


namespace Tracker.Tests.Infrastructure;

internal sealed class TrackerTestHost : IAsyncDisposable
{
  private readonly string _userDataFolder;
  private readonly WebApplication _application;

  private TrackerTestHost(
    WebApplication application,
    HttpClient client,
    FakeClientAPIProvider clientProvider,
    FakeClientCommandGateway commandGateway,
    string userDataFolder)
  {
    _application = application;
    Client = client;
    ClientProvider = clientProvider;
    CommandGateway = commandGateway;
    _userDataFolder = userDataFolder;
  }

  public HttpClient Client { get; }
  public FakeClientAPIProvider ClientProvider { get; }
  public FakeClientCommandGateway CommandGateway { get; }
  public string UserDataFolder => _userDataFolder;
  public IServiceProvider Services => _application.Services;

  public IServiceScope CreateScope() => _application.Services.CreateScope();

  public static async Task<TrackerTestHost> StartAsync(
    string? eventDatabasePath = null)
  {
    string userDataFolder = Path.Combine(
      Path.GetTempPath(),
      $"tracker-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(userDataFolder);
    if (eventDatabasePath is not null)
    {
      string databaseFolder = Path.Combine(userDataFolder, "Database");
      Directory.CreateDirectory(databaseFolder);
      File.Copy(
        eventDatabasePath,
        Path.Combine(databaseFolder, "Event.db"),
        overwrite: true);
    }

    string? previousUserDataFolder =
      Environment.GetEnvironmentVariable("TRACKER_USER_DATA_FOLDER");
    string? previousDisableUi =
      Environment.GetEnvironmentVariable("TRACKER_DISABLE_UI");
    Environment.SetEnvironmentVariable("TRACKER_USER_DATA_FOLDER", userDataFolder);
    Environment.SetEnvironmentVariable("TRACKER_DISABLE_UI", "1");

    try
    {
      var options = new ApplicationOptions(Array.Empty<string>());
      options.DisableUI = true;
      var clientProvider = new FakeClientAPIProvider();
      var commandGateway = new FakeClientCommandGateway();

      WebApplicationBuilder builder = WebAPIService.CreateHostBuilder(options);
      builder.WebHost.UseTestServer();
      builder.Logging.AddFilter(
        "Microsoft.EntityFrameworkCore.Database.Command",
        LogLevel.Warning);
      builder.Services.AddControllers()
        .AddApplicationPart(typeof(ClientController).Assembly);
      builder.Services.PostConfigureAll<HttpClientFactoryOptions>(httpOptions =>
      {
        httpOptions.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
        {
          handlerBuilder.PrimaryHandler = new NoNetworkHandler();
        });
      });
      builder.Services.AddSingleton<IClientAPIProvider>(clientProvider);
      builder.Services.AddSingleton(new WebViewHostAccessor(null));
      builder.Services.AddScoped<ClientStateMonitor>();
      builder.Services.AddSingleton<IClientCommandGateway>(commandGateway);
      builder.UseDatabase<EventContext>(options);
      builder.UseDatabase<CollectionContext>(options);
      builder.UseDatabase<TradeContext>(options);
      builder.RegisterCollectionService();

      WebApplication application = builder.Build();
      application.UseClientMiddleware();
      application.CreateAPIService(options);
      await application.StartAsync();

      return new TrackerTestHost(
        application,
        application.GetTestClient(),
        clientProvider,
        commandGateway,
        userDataFolder);
    }
    finally
    {
      Environment.SetEnvironmentVariable(
        "TRACKER_USER_DATA_FOLDER", previousUserDataFolder);
      Environment.SetEnvironmentVariable("TRACKER_DISABLE_UI", previousDisableUi);
    }
  }

  public async ValueTask DisposeAsync()
  {
    Client.Dispose();
    await _application.StopAsync();
    await _application.DisposeAsync();
    SqliteConnection.ClearAllPools();

    for (var attempt = 0; attempt < 10; attempt++)
    {
      try
      {
        Directory.Delete(_userDataFolder, recursive: true);
        return;
      }
      catch (IOException) when (attempt < 9)
      {
        await Task.Delay(50);
      }
    }
  }

  private sealed class NoNetworkHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken) =>
      Task.FromException<HttpResponseMessage>(new InvalidOperationException(
        $"Unexpected external HTTP request in Tracker tests: {request.RequestUri}"));
  }
}
