/* @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace Tracker.Plugins;

/// <summary>
/// Registers a plugin's dependencies before Tracker builds its service provider.
/// </summary>
/// <remarks>
/// Controllers are discovered from the entry assembly. Background work and
/// cleanup use the normal <see cref="IHostedService"/>,
/// <see cref="IDisposable"/>, and <see cref="IAsyncDisposable"/> service lifecycles.
/// Hosted-service factories resolve shared instances from their supplied service
/// provider or return a new instance. DI disposes resolved services; Tracker
/// disposes the newly created hosted-service instances.
/// </remarks>
public interface ITrackerPlugin
{
  /// <summary>
  /// Registers services using this plugin's configuration and paths.
  /// </summary>
  /// <param name="services">A staged copy of the host's service registrations.</param>
  /// <param name="context">Configuration and directories for this plugin.</param>
  /// <remarks>
  /// Add plugin services without replacing, removing, or reordering existing
  /// registrations. A failed callback discards the additions. Start background
  /// work in a hosted service rather than in this callback or the constructor.
  /// </remarks>
  void ConfigureServices(IServiceCollection services, PluginContext context);
}
