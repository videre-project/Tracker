/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Tracker.Plugins;


namespace Tracker.Services;

/// <summary>
/// Registers installed backend plugins with Tracker's host.
/// </summary>
public static partial class PluginService
{
  /// <summary>
  /// Registers enabled plugins from the installation directory with the host.
  /// </summary>
  /// <param name="builder">The host builder to configure.</param>
  /// <param name="installationDirectory">The directory containing plugin installations.</param>
  /// <param name="dataDirectory">The parent directory for persistent plugin data.</param>
  /// <returns>The host builder for chaining.</returns>
  /// <remarks>
  /// Each immediate subdirectory may contain a plugin.json manifest. Failed
  /// plugins are reported and skipped without stopping Tracker. Service
  /// registration is staged before it is committed to the host. Changes to
  /// installed plugins take effect on the next host start.
  /// </remarks>
  public static IHostApplicationBuilder RegisterPlugins(
    this IHostApplicationBuilder builder,
    string installationDirectory,
    string dataDirectory)
  {
    var plugins = new List<PluginInfo>();
    var catalog = new PluginCatalog(plugins.AsReadOnly(), dataDirectory,
      builder.Configuration.GetValue("Plugins:Enabled", true));
    builder.Services.AddSingleton(catalog);
    var pluginManager = new PluginManager(installationDirectory, dataDirectory, catalog);
    builder.Services.AddSingleton(pluginManager);
    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!catalog.Enabled)
    {
      return builder;
    }
    if (!pluginManager.Prepare())
    {
      return builder;
    }
    string[] manifests;
    try
    {
      manifests = Directory.Exists(installationDirectory)
        ? Directory.EnumerateDirectories(installationDirectory)
          .Where(directory => !Path.GetFileName(directory).StartsWith('.'))
          .Select(directory => Path.Combine(directory, "plugin.json"))
          .Where(File.Exists).Order(StringComparer.Ordinal).ToArray()
        : Array.Empty<string>();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      catalog.ReportFailure(installationDirectory, exception);
      return builder;
    }

    foreach (string manifestPath in manifests)
    {
      try
      {
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        string id = Path.GetFileName(directory);
        var settings = pluginManager.ReadSettings(id);
        if (settings.Remove || settings.Enabled == false)
        {
          pluginManager.RecordStartupSettings(id, settings, enabled: false);
          continue;
        }
        var manifest = PluginManager.ReadManifest(directory);
        if (id != manifest.Id)
        {
          throw new InvalidDataException($"The installation folder must be named '{manifest.Id}'.");
        }
        bool enabled = settings.Enabled ?? manifest.Enabled;
        pluginManager.RecordStartupSettings(id, settings, enabled);
        if (!enabled)
        {
          continue;
        }

        if (!ids.Add(manifest.Id))
        {
          throw new InvalidDataException($"Duplicate plugin ID '{manifest.Id}'.");
        }

        PluginManager.ValidateManifest(manifest, directory);
        string assemblyPath = Path.Combine(directory, manifest.Assembly);
        var loadContext = new PluginLoadContext(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        Type type = assembly.GetType(manifest.EntryPoint, throwOnError: true)!;
        if (!type.IsVisible || type.IsAbstract || !typeof(ITrackerPlugin).IsAssignableFrom(type))
        {
          throw new InvalidDataException(
            $"'{manifest.EntryPoint}' must be a public ITrackerPlugin implementation.");
        }

        var plugin = (ITrackerPlugin)Activator.CreateInstance(type)!;
        string pluginData = Path.Combine(dataDirectory, manifest.Id);
        Directory.CreateDirectory(pluginData);
        // A failed callback must not leave registrations in the real host.
        // Copy descriptors, preserving existing services for TryAdd semantics.
        var staged = new ServiceCollection();
        foreach (var descriptor in builder.Services)
        {
          if (!descriptor.IsKeyedService && descriptor.ImplementationInstance is ApplicationPartManager manager)
          {
            // AddControllers extensions mutate this singleton during registration.
            // Give the callback its own copy so a throw cannot erase host routes.
            var copy = new ApplicationPartManager();
            foreach (var part in manager.ApplicationParts)
            {
              copy.ApplicationParts.Add(part);
            }
            foreach (var feature in manager.FeatureProviders)
            {
              copy.FeatureProviders.Add(feature);
            }
            ((IServiceCollection)staged).Add(ServiceDescriptor.Singleton(copy));
          }
          else
          {
            ((IServiceCollection)staged).Add(descriptor);
          }
        }
        var baseline = staged.ToArray();
        plugin.ConfigureServices(staged, new PluginContext(
          manifest.Id, directory, pluginData,
          pluginManager.BuildConfiguration(manifest.Id, directory, builder.Configuration)));

        // Plugins extend the host. Replacing/removing host registrations could
        // corrupt recording or prevent startup even after a plugin is skipped.
        ValidateRegistrations(baseline, staged);
        ValidateControllers(staged, assembly, manifest.Id);

        var additions = staged.Skip(builder.Services.Count).ToArray();
        var hosted = additions.Where(descriptor => !descriptor.IsKeyedService
          && descriptor.ServiceType == typeof(IHostedService)).ToArray();
        foreach (var descriptor in additions.Except(hosted))
        {
          builder.Services.Add(descriptor);
        }
        if (hosted.Length > 0)
        {
          builder.Services.AddSingleton<IHostedService>(provider =>
            new PluginHostedServices(provider, hosted, manifestPath, catalog));
        }
        builder.Services.AddControllers().AddApplicationPart(assembly)
          .AddMvcOptions(options => options.Conventions.Add(
            new PluginRouteConvention(assembly, manifest.Id)));
        plugins.Add(new(manifest.Id, assembly.GetName().Version?.ToString() ?? "unknown"));
      }
      catch (Exception exception)
      {
        catalog.ReportFailure(manifestPath, exception);
      }
    }

    return builder;
  }

  private static void ValidateRegistrations(IReadOnlyList<ServiceDescriptor> host, IServiceCollection staged)
  {
    if (staged.Count < host.Count || !host.SequenceEqual(staged.Take(host.Count)))
    {
      throw new InvalidOperationException("Plugins must not replace, reorder, or remove existing host services.");
    }

    foreach (var addition in staged.Skip(host.Count))
    {
      if (!host.Any(existing => existing.ServiceType == addition.ServiceType
          && existing.IsKeyedService == addition.IsKeyedService
          && Equals(existing.ServiceKey, addition.ServiceKey)))
      {
        continue;
      }
      Type type = addition.ServiceType;
      // These are extension points designed for multiple registrations. Other
      // services already belong to the host or a previously registered plugin.
      string? definition = type.IsGenericType ? type.GetGenericTypeDefinition().FullName : null;
      if (type != typeof(IHostedService)
          && definition is not "Microsoft.Extensions.Options.IConfigureOptions`1"
            and not "Microsoft.Extensions.Options.IPostConfigureOptions`1"
            and not "Microsoft.Extensions.Options.IValidateOptions`1")
      {
        throw new InvalidOperationException($"Plugin registration would replace existing service '{type}'. Use a plugin-specific service type or key.");
      }
    }
  }

  private static void ValidateControllers(IServiceCollection staged, Assembly assembly, string id)
  {
    var validation = new ServiceCollection();
    foreach (var descriptor in staged)
    {
      if (descriptor.ServiceType != typeof(ApplicationPartManager))
      {
        ((IServiceCollection)validation).Add(descriptor);
      }
    }
    // Use an independent part manager: MVC's manager is a mutable singleton and
    // must not acquire controllers from a plugin whose validation later fails.
    validation.AddControllers().ConfigureApplicationPartManager(manager =>
    {
      manager.ApplicationParts.Clear();
      manager.ApplicationParts.Add(new AssemblyPart(assembly));
    }).AddMvcOptions(options => options.Conventions.Add(new PluginRouteConvention(assembly, id)))
      .AddControllersAsServices();
    using var provider = validation.BuildServiceProvider(new ServiceProviderOptions
    {
      ValidateOnBuild = true,
    });
    _ = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors;
  }

  /// <summary>
  /// Identifies one loaded plugin and its assembly version.
  /// </summary>
  public sealed record PluginInfo(string Id, string Version);

  /// <summary>
  /// The plugins configured for this host's lifetime.
  /// </summary>
  public sealed class PluginCatalog(IReadOnlyList<PluginInfo> plugins, string dataDirectory, bool enabled)
  {
    private readonly object _lock = new();
    private readonly List<PluginFailure> _failures = new();
    public IReadOnlyList<PluginInfo> Plugins { get; } = plugins;
    public bool Enabled { get; } = enabled;
    public IReadOnlyList<PluginFailure> Failures
    {
      get { lock (_lock) return _failures.ToArray(); }
    }

    internal void ReportFailure(string path, Exception exception)
    {
      lock (_lock)
      {
        if (_failures.Any(failure => failure.Path == path))
        {
          return;
        }
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
          messages.Add(current.Message);
        }
        _failures.Add(new(path, string.Join(" ", messages.Distinct())));
        Console.Error.WriteLine($"Plugin '{path}' failed: {exception}");
        try
        {
          Directory.CreateDirectory(dataDirectory);
          File.AppendAllText(Path.Combine(dataDirectory, "plugin-errors.log"),
            $"{DateTimeOffset.UtcNow:O} Plugin '{path}'\n{exception}\n\n");
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
          Console.Error.WriteLine($"Could not save plugin error details: {writeError.Message}");
        }
      }
    }

  }

  /// <summary>Identifies a failed installation and its actionable error.</summary>
  public sealed record PluginFailure(string Path, string Message);

  private sealed class PluginHostedServices(IServiceProvider provider,
    IReadOnlyList<ServiceDescriptor> descriptors, string manifestPath, PluginCatalog catalog)
    : IHostedService, IAsyncDisposable
  {
    private readonly List<(IHostedService Service, bool Owned)> _services = new();
    private readonly SemaphoreSlim _stopLock = new(1, 1);
    private bool _stopped;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
      try
      {
        foreach (var descriptor in descriptors)
        {
          var factoryProvider = new HostedServiceFactoryProvider(provider);
          var service = (IHostedService)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(factoryProvider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
          bool fromContainer = factoryProvider.CompleteResolution(service);
          bool owned = descriptor.ImplementationInstance is null && !fromContainer;
          _services.Add((service, owned));
          await service.StartAsync(cancellationToken);
        }
        foreach (var (service, _) in _services)
        {
          if (service is BackgroundService { ExecuteTask: { } task })
          {
            _ = ObserveBackgroundWork(task);
          }
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception exception)
      {
        catalog.ReportFailure(manifestPath, exception);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await StopAsync(timeout.Token);
      }
    }

    private async Task ObserveBackgroundWork(Task task)
    {
      try { await task; }
      catch (OperationCanceledException) { }
      catch (Exception exception)
      {
        catalog.ReportFailure(manifestPath, exception);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await StopAsync(timeout.Token);
      }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
      // A background failure and host shutdown may both request cleanup.
      await _stopLock.WaitAsync(CancellationToken.None);
      try
      {
        if (_stopped)
        {
          return;
        }
        _stopped = true;
        foreach (var (service, _) in _services.AsEnumerable().Reverse())
        {
          try
          {
            await service.StopAsync(cancellationToken).WaitAsync(cancellationToken);
          }
          catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
          catch (Exception exception)
          {
            catalog.ReportFailure(manifestPath, exception);
          }
        }
      }
      finally
      {
        _stopLock.Release();
      }
    }

    public async ValueTask DisposeAsync()
    {
      foreach (var (service, owned) in _services.AsEnumerable().Reverse())
      {
        if (!owned)
        {
          continue;
        }
        try
        {
          if (service is IAsyncDisposable asyncDisposable)
          {
            await asyncDisposable.DisposeAsync();
          }
          else if (service is IDisposable disposable)
          {
            disposable.Dispose();
          }
        }
        catch (Exception exception)
        {
          catalog.ReportFailure(manifestPath, exception);
        }
      }
    }
  }

  private sealed class HostedServiceFactoryProvider(IServiceProvider provider) : IServiceProvider, IKeyedServiceProvider
  {
    private ConcurrentDictionary<object, byte>? _resolved = new(ReferenceEqualityComparer.Instance);

    // A factory may retain this provider for runtime use. Stop tracking once
    // its result's ownership is known, without retaining later resolutions.
    internal bool CompleteResolution(object service) =>
      Interlocked.Exchange(ref _resolved, null)?.ContainsKey(service) == true;

    public object? GetService(Type serviceType) => serviceType == typeof(IServiceProvider)
      ? this : Record(serviceType, provider.GetService(serviceType));

    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
      Record(serviceType, KeyedProvider.GetKeyedService(serviceType, serviceKey));

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
      Record(serviceType, KeyedProvider.GetRequiredKeyedService(serviceType, serviceKey))!;

    private IKeyedServiceProvider KeyedProvider => provider as IKeyedServiceProvider
      ?? throw new InvalidOperationException("This service provider does not support keyed services.");

    private object? Record(Type serviceType, object? value)
    {
      var resolved = Volatile.Read(ref _resolved);
      if (value is not null && resolved is not null)
      {
        // DI owns factory-resolved services, including singleton aliases and
        // members returned by GetServices<T>. Only new factory results are ours.
        resolved.TryAdd(value, 0);
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
          && value is IEnumerable services)
        {
          foreach (object service in services)
          {
            resolved.TryAdd(service, 0);
          }
        }
      }
      return value;
    }
  }

  /// <summary>
  /// Resolves private managed/native dependencies beside a plugin while sharing
  /// the host's framework, contracts and SDK identity. Plugins load once at startup.
  /// </summary>
  private sealed class PluginLoadContext : AssemblyLoadContext
  {
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string assemblyPath)
      : base(Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: false)
    {
      _resolver = new(assemblyPath);
      // SDK releases currently share an assembly version. Check the actual
      // package dependency, since CLR identity alone cannot detect an SDK mismatch.
      string depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
      if (!File.Exists(depsPath))
      {
        throw new InvalidDataException(
          $"Missing '{depsPath}'. Publish the plugin with EnableDynamicLoading=true and install its complete output.");
      }

      using var dependencies = JsonDocument.Parse(File.ReadAllText(depsPath));
      string? sdk = dependencies.RootElement.GetProperty("libraries").EnumerateObject()
        .Select(library => library.Name)
        .SingleOrDefault(name => name.StartsWith("MTGOSDK/", StringComparison.OrdinalIgnoreCase));
      string expectedSdk = typeof(ITrackerPlugin).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "MTGOSDKVersion").Value!;
      if (!string.Equals(sdk, $"MTGOSDK/{expectedSdk}", StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException(
          $"This Tracker host requires MTGOSDK {expectedSdk}; the plugin declares '{sdk ?? "no MTGOSDK dependency"}'. Rebuild the plugin against the supported SDK package.");
      }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
      string name = assemblyName.Name!;
      bool requiresHost = name == typeof(ITrackerPlugin).Assembly.GetName().Name
        || name == "MTGOSDK" || name.StartsWith("MTGOSDK.", StringComparison.Ordinal)
        || SharedFrameworkAssemblies.Contains(name);
      if (requiresHost)
      {
        try
        {
          // Includes framework assemblies embedded in a single-file publish.
          var shared = Default.LoadFromAssemblyName(assemblyName);
          var actual = shared.GetName();
          if ((assemblyName.Version is not null && actual.Version < assemblyName.Version)
              || !string.Equals(actual.CultureName, assemblyName.CultureName, StringComparison.OrdinalIgnoreCase)
              || !(actual.GetPublicKeyToken() ?? Array.Empty<byte>())
                .SequenceEqual(assemblyName.GetPublicKeyToken() ?? Array.Empty<byte>()))
          {
            throw new FileLoadException($"Host provides '{actual.FullName}', below or incompatible with the requested identity.");
          }
          return shared;
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
        {
          throw new FileLoadException(
            $"Plugin requires shared assembly '{assemblyName.FullName}', which this Tracker host cannot provide. Use a plugin build compatible with this host.", exception);
        }
      }

      // Private dependencies keep their own identity even if Tracker happens
      // to use another version of the same library.
      string? path = _resolver.ResolveAssemblyToPath(assemblyName);
      return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
      string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
      return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
  }

  /// <summary>
  /// Keeps each plugin's attribute routes beneath its own API prefix.
  /// </summary>
  private sealed class PluginRouteConvention(Assembly assembly, string id) : IApplicationModelConvention
  {
    public void Apply(ApplicationModel application)
    {
      var prefix = new AttributeRouteModel(new RouteAttribute($"api/plugins/{id}"));
      foreach (var controller in application.Controllers)
      {
        if (controller.ControllerType.Assembly != assembly)
        {
          continue;
        }

        foreach (var action in controller.Actions)
        {
          foreach (var selector in action.Selectors)
          {
            RequireRelative(selector.AttributeRouteModel?.Template);
          }
        }

        foreach (var selector in controller.Selectors)
        {
          RequireRelative(selector.AttributeRouteModel?.Template);
          selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(
            prefix, selector.AttributeRouteModel);
        }
      }
    }

    private static void RequireRelative(string? template)
    {
      if (template?.StartsWith('/') == true
        || template?.StartsWith("~/", StringComparison.Ordinal) == true)
      {
        throw new InvalidOperationException(
          "Plugin controllers must use relative attribute routes.");
      }
    }
  }
}
