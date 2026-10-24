/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.Extensions.DependencyInjection;
using MTGOTracker.Plugins;

namespace Example;

public sealed class ExamplePlugin : ITrackerPlugin
{
  public void ConfigureServices(IServiceCollection services, PluginContext context)
  {
    // Keep context in this plugin's own options type so other plugins can have
    // their own configuration and directories without replacing this instance.
    services.AddSingleton(new ObserverOptions(context.DataDirectory,
      bool.Parse(context.Configuration["includeUserId"] ?? "true")));
    services.AddSingleton<SessionObserver>();
    services.AddHostedService(provider => provider.GetRequiredService<SessionObserver>());
  }
}

public sealed record ObserverOptions(string DataDirectory, bool IncludeUserId = true);
