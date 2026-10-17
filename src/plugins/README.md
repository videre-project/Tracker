# Tracker.Plugins

Build backend extensions for Videre Tracker using its existing MTGO session.
The host supplies service registration, configuration, storage, and initialized
MTGOSDK games. UI extensions and hot reload are not supported in this preview.

## Requirements and compatibility

| Component | Supported version |
| --- | --- |
| Tracker application | 0.3.0 |
| .NET SDK / target | .NET 10 / `net10.0-windows7.0` |
| Tracker.Plugins package | `0.1.0-preview.1` |
| Manifest `apiVersion` | `1` |
| API assembly version | `1.0.0.0` |
| MTGOSDK package | `1.8.0.20261018` |
| Runtime | Windows x64; the installed Tracker host supplies the runtime |

API generation 1 keeps assembly version `1.0.0.0` across compatible package
releases. A newer host in that generation supports plugins built with earlier
compatible API packages, provided their SDK dependency matches the host. A plugin
using an API added in a newer package requires a host that ships that API. Package
versions and application versions are independent; newer plugins are not promised
to work on older hosts. Breaking API changes require a new assembly major version
and manifest API generation. Preview breaking changes follow the same rule.

Build against the exact supported MTGOSDK package. Its releases currently share
an assembly version, so the host checks your `.deps.json` package version before
loading the plugin. A mismatch produces an actionable startup error.

## Build your first plugin

Copy the complete [session observer example](https://github.com/videre-project/Tracker/tree/main/examples/session-observer)
to a new folder. It is a standalone project using public NuGet packages, with no
reference to Tracker source. It includes:

- `SessionObserver.csproj`: target framework, package references, and `EnableDynamicLoading`.
- `plugin.json`: installation manifest and public parameterless entry point.
- `ExamplePlugin.cs`: service registration and plugin-specific options.
- `SessionObserver.cs`: subscribes to session changes, queues work, writes a snapshot,
  and removes its subscription on shutdown.
- `SessionController.cs`: serves the latest snapshot through Tracker's HTTP server.

From the copied folder:

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

Keep the entire publish output together, including `plugin.json`, `.deps.json`,
managed dependencies, and native runtime files. Do not publish a self-contained
plugin or bundle it into a single executable.

## Install and verify

1. Close Tracker.
2. Copy the contents of `publish` into
   `%LocalAppData%\Videre Tracker\Plugins\session-observer\`.
   For a custom installation, use `<installRoot>\Plugins\session-observer\`.
   `TRACKER_USER_DATA_FOLDER` overrides the data root when configured.
3. Restart Tracker. Installations, updates, removals, and enablement changes take
   effect on restart.
4. Open `https://localhost:7101/api/plugins` to confirm `session-observer` is listed.
   The example endpoint is
   `https://localhost:7101/api/plugins/session-observer/session`.
   Tracker uses a local HTTPS certificate; use its API Docs page if your browser
   does not trust the local certificate.
5. Inspect `PluginData\session-observer\session.json` beneath the same data root.
   With MTGO disconnected, the snapshot has no user and zero games. Opening or
   closing a tracked game updates the snapshot; reconnecting reconciles the list.

The manifest is:

```json
{
  "id": "session-observer",
  "assembly": "SessionObserver.dll",
  "entryPoint": "Example.ExamplePlugin",
  "apiVersion": 1,
  "enabled": true
}
```

IDs start with a lowercase letter and contain lowercase letters, digits, or
hyphens. Each plugin lives in its own immediate subdirectory of `Plugins`.
`assembly` is a filename in that directory. The entry point must be public,
non-abstract, implement `ITrackerPlugin`, and have a public parameterless constructor.

## Configuration and lifecycle

`ConfigureServices(IServiceCollection, PluginContext)` runs before the service
provider is built. Register background work with `IHostedService` or
`BackgroundService`. Register controllers in the entry assembly with relative
attribute routes; Tracker adds `/api/plugins/{id}`. Absolute routes are rejected.
Do not create another host/service provider or start background work in the
entry-point constructor or `ConfigureServices`.

`PluginContext` supplies `Id`, `InstallationDirectory`, `DataDirectory`, and the
`Plugins:{id}` configuration section. These are available during registration;
pass them into your own options type as the example does. Standard .NET environment
configuration works, for example `Plugins__session-observer__SomeOption=value`.
`Plugins__Directory` overrides installation discovery.

Inject `IMTGOContext` for the host-owned client, cached user ID, and games whose
recording processors are ready. Subscribe to `Changed` before the initial read.
Notifications are synchronous, can overlap on worker threads, and cover readiness
and game-list changes, including removals. Queue substantial work and reconcile
against a fresh `Games` snapshot. Marshal UI work to its owning thread. Remove
only your own subscriptions during shutdown. Borrowed SDK objects can become
unavailable during disconnect; do not dispose them, replace processors, or clear
their events. Individual game events come from MTGOSDK.

Tracker shares its API, MTGOSDK, and .NET/ASP.NET/Desktop framework assemblies.
Other dependencies resolve from your plugin's published dependency manifest first,
so an unrelated library used by Tracker does not replace your private copy. Use
host framework versions for framework types exchanged through DI or controllers.
Plugins execute with Tracker's permissions and can affect its process; this is
not a security or crash-isolation boundary.

## Plugin failures

Tracker continues starting when a plugin fails. Invalid manifests, unsupported
API/SDK dependencies, invalid service registrations, and invalid controller routes
are reported and skipped. Registration runs against a staged service collection;
a failed callback never commits its descriptors to the host. Plugins may add
services and supported options extensions, but cannot remove/reorder existing
registrations or replace a service owned by the host or another plugin. Use your
own service types or keyed registrations for plugin-specific state.

Plugin hosted-service constructors, startup, background execution, and shutdown
have an error boundary. Tracker also catches disposal errors for hosted-service
instances it creates directly or that a factory creates. Services resolved by a
factory from its supplied service provider remain owned and disposed by DI; their
disposal follows normal DI behavior. Keep disposal idempotent and non-throwing.
Errors caught by Tracker are reported while Tracker and healthy plugins continue
running. `GET /api/plugins/status` reports the affected installation and error;
full exceptions are saved to `PluginData/plugin-errors.log`. Fix or remove the
affected plugin and restart to retry it. No automatic restart or file deletion
is required for Tracker to finish starting.

For troubleshooting you can explicitly bypass all plugins:

```powershell
& './Videre Tracker.exe' --Plugins:Enabled=false
```

Alternatively set `Plugins__Enabled=false` in the environment. This bypasses
manifest discovery and all plugin code. Launch normally without the flag or
environment override to re-enable plugins. You can disable an individual plugin
with `enabled: false` in an otherwise valid manifest. Installed files and
persistent data are preserved.

`GET /api/plugins/status` reports enablement and failures for the current process.
`GET /api/plugins` lists configured plugins; check status for service failures.

See [architecture](https://github.com/videre-project/Tracker/blob/main/ARCHITECTURE.md#6-extensibility)
for host implementation details.
