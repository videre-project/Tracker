/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MTGOSDK.Core.Logging;
using MTGOTracker.Installer;

namespace MTGOTracker.Services;

/// <summary>
/// Discovers releases, stages their executable, and activates updates after MTGO exits.
/// </summary>
public sealed class UpdateService : BackgroundService
{
  private const string DefaultFeed = "https://api.github.com/repos/videre-project/Tracker/releases/latest";
  private const string ExecutableName = "Videre Tracker.exe";
  private readonly HttpClient _http;
  private readonly ApplicationOptions _options;
  private readonly SettingsService _settings;
  private readonly IHostApplicationLifetime _lifetime;
  private readonly SemaphoreSlim _operation = new(1);
  private readonly Uri _feed;
  private readonly string _directory;
  private readonly string _receipt;
  private readonly string _errorFile;
  private Release? _available;
  private StagedUpdate? _staged;
  private volatile UpdateStatus _status;

  public sealed record UpdateStatus(
    string CurrentVersion, string? AvailableVersion, string? StagedVersion,
    string? ReleaseUrl, string? ReleaseNotes, DateTimeOffset? PublishedAt,
    DateTimeOffset? LastChecked, long DownloadedBytes, long? TotalBytes,
    bool Busy, bool Applying, bool Scheduled, bool MTGORunning, bool CustomSource, string? Error);

  private sealed record Release(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("html_url")] string? Url,
    [property: JsonPropertyName("body")] string? Notes,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] Asset[] Assets);

  private sealed record Asset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string Url,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest);

  private sealed record StagedUpdate(string Version, string Executable, string Digest, bool Scheduled);

  public UpdateService(IHttpClientFactory clients, IConfiguration configuration,
    ApplicationOptions options, SettingsService settings, IHostApplicationLifetime lifetime)
  {
    _http = clients.CreateClient();
    _http.Timeout = TimeSpan.FromMinutes(15);
    _http.DefaultRequestHeaders.UserAgent.ParseAdd($"VidereTracker/{ProductInfo.Version}");
    _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    string? feed = configuration["Updates:FeedUrl"];
    _feed = new Uri(string.IsNullOrWhiteSpace(feed) ? DefaultFeed : feed);
    _options = options;
    _settings = settings;
    _lifetime = lifetime;
    _directory = Path.Combine(options.UserDataFolder, "Updates");
    _receipt = Path.Combine(_directory, "staged.json");
    _errorFile = Path.Combine(_directory, "install-error.txt");
    string? error = null;
    try
    {
      if (File.Exists(_errorFile)) error = File.ReadAllText(_errorFile);
      if (File.Exists(_receipt))
      {
        var staged = JsonSerializer.Deserialize<StagedUpdate>(File.ReadAllText(_receipt));
        if (staged is null || string.IsNullOrWhiteSpace(staged.Version) ||
            string.IsNullOrWhiteSpace(staged.Executable) || string.IsNullOrWhiteSpace(staged.Digest))
          throw new InvalidDataException("The staged update receipt is incomplete. Download the update again.");
        if (UpdateInstaller.ParseVersion(staged.Version) <= UpdateInstaller.ParseVersion(ProductInfo.Version))
          File.Delete(_receipt);
        else _staged = staged;
      }
    }
    catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
    {
      // Invalid update state is recoverable; never offer it for installation.
      _staged = null;
      error = exception.Message;
    }
    _status = new(ProductInfo.Version, null, _staged?.Version, null, null, null, null,
      0, null, false, false, _staged?.Scheduled ?? false, false, _feed.AbsoluteUri != DefaultFeed, error);
  }

  public UpdateStatus Status => _status with { MTGORunning = UpdateInstaller.MTGORunning };

  public Task CheckAsync(CancellationToken cancellationToken) =>
    RunAsync(() => CheckCoreAsync(cancellationToken), cancellationToken);

  private async Task CheckCoreAsync(CancellationToken cancellationToken)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(15));
    using var response = await _http.GetAsync(_feed, timeout.Token);
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
      _available = null;
      _status = _status with
      {
        AvailableVersion = null, ReleaseUrl = null, ReleaseNotes = null,
        PublishedAt = null, LastChecked = DateTimeOffset.UtcNow
      };
      return;
    }
    response.EnsureSuccessStatusCode();
    Release release;
    try
    {
      release = await response.Content.ReadFromJsonAsync<Release>(timeout.Token)
        ?? throw new InvalidDataException("The release feed is empty.");
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException("The release feed contains invalid JSON.", exception);
    }
    if (string.IsNullOrWhiteSpace(release.Tag) || release.Assets is null || release.Assets.Any(asset => asset is null))
      throw new InvalidDataException("The release feed is missing a version or a valid asset list.");
    _available = !release.Draft && !release.Prerelease &&
      UpdateInstaller.ParseVersion(release.Tag) > UpdateInstaller.ParseVersion(ProductInfo.Version)
      ? release : null;
    _status = _status with
    {
      AvailableVersion = _available?.Tag, ReleaseUrl = _available?.Url,
      ReleaseNotes = _available?.Notes, PublishedAt = _available?.PublishedAt,
      LastChecked = DateTimeOffset.UtcNow
    };
  }

  public Task DownloadAsync(CancellationToken cancellationToken) => RunAsync(async () =>
  {
    var release = _available ?? throw new InvalidOperationException("Check for an available release first.");
    // GitHub replaces spaces with dots in uploaded asset filenames.
    var asset = release.Assets.FirstOrDefault(asset => asset.Name == ExecutableName)
      ?? release.Assets.FirstOrDefault(asset => asset.Name == "Videre.Tracker.exe")
      ?? throw new InvalidDataException("The release does not contain Videre Tracker.exe.");
    if (string.IsNullOrWhiteSpace(asset.Url))
      throw new InvalidDataException("The release asset has no download URL.");
    string version = release.Tag.TrimStart('v', 'V');
    string directory = Path.Combine(_directory, "v" + version);
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, ExecutableName);
    string temporary = path + ".download";
    try
    {
      using var response = await _http.GetAsync(new Uri(_feed, asset.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
      response.EnsureSuccessStatusCode();
      _status = _status with { DownloadedBytes = 0, TotalBytes = asset.Size > 0 ? asset.Size : response.Content.Headers.ContentLength };
      using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
      await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
      {
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
          await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
          hash.AppendData(buffer, 0, count);
          _status = _status with { DownloadedBytes = _status.DownloadedBytes + count };
        }
      }
      string digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
      if (asset.Size > 0 && _status.DownloadedBytes != asset.Size)
        throw new InvalidDataException("The downloaded file size does not match the release asset.");
      if (asset.Digest is not null && !asset.Digest.Equals(digest, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("The downloaded file does not match the release asset's SHA-256 digest.");
      File.Move(temporary, path, overwrite: true);
      SaveStaged(new(release.Tag, path, digest, false));
      _status = _status with { StagedVersion = release.Tag, Scheduled = false };
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
  }, cancellationToken);

  public Task ScheduleAsync(bool scheduled, CancellationToken cancellationToken) => RunAsync(() =>
  {
    var staged = _staged ?? throw new InvalidOperationException("Download an update first.");
    SaveStaged(staged with { Scheduled = scheduled });
    _status = _status with { Scheduled = scheduled };
    return Task.CompletedTask;
  }, cancellationToken);

  public Task ApplyAsync(CancellationToken cancellationToken) =>
    RunAsync(() => ApplyCoreAsync(onlyIfScheduled: false, cancellationToken), cancellationToken);

  private async Task<bool> ApplyCoreAsync(bool onlyIfScheduled, CancellationToken cancellationToken)
  {
    if (onlyIfScheduled && _staged?.Scheduled != true) return false;
    var staged = _staged ?? throw new InvalidOperationException("Download an update first.");
    if (UpdateInstaller.MTGORunning)
    {
      if (onlyIfScheduled) return false;
      throw new InvalidOperationException("Close MTGO before applying the update.");
    }
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
      await using var file = File.OpenRead(staged.Executable);
      string digest = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
      if (digest != staged.Digest) throw new InvalidDataException("The staged executable has changed. Download it again.");
    }
    catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
    {
      File.Delete(_receipt);
      _staged = null;
      _status = _status with { StagedVersion = null, Scheduled = false };
      throw;
    }
    cancellationToken.ThrowIfCancellationRequested();
    _status = _status with { Applying = true };
    string ready = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".ready");
    string planPath = Path.Combine(_directory, "install.json");
    string installDirectory = Path.Combine(_options.UserDataFolder, "v" + staged.Version.TrimStart('v', 'V'));
    var plan = new UpdateInstaller.UpdatePlan(staged.Version, _options.UserDataFolder,
      installDirectory, Environment.ProcessId, ready, _errorFile);
    File.Delete(_errorFile);
    File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
    var start = new ProcessStartInfo(staged.Executable) { UseShellExecute = false };
    start.ArgumentList.Add("--install-update");
    start.ArgumentList.Add(planPath);
    using var installer = Process.Start(start) ?? throw new IOException("The update installer could not start.");
    try
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(TimeSpan.FromMinutes(2));
      while (!File.Exists(ready))
      {
        if (installer.HasExited)
          throw new IOException(File.Exists(_errorFile) ? File.ReadAllText(_errorFile) : "The update installer exited before it was ready.");
        await Task.Delay(100, timeout.Token);
      }
      SaveStaged(staged with { Scheduled = false });
      _status = _status with { Scheduled = false };

    }
    catch
    {
      if (!installer.HasExited) installer.Kill();
      _status = _status with { Applying = false };
      throw;
    }
    finally { File.Delete(ready); }
    return true;
  }

  public void CompleteHandoff() => _lifetime.StopApplication();

  private async Task RunAsync(Func<Task> action, CancellationToken cancellationToken, bool skipIfBusy = false)
  {
    if (!await _operation.WaitAsync(0, cancellationToken))
    {
      if (skipIfBusy) return;
      throw new InvalidOperationException("An update operation is already running.");
    }
    if (_status.Applying)
    {
      _operation.Release();
      if (skipIfBusy) return;
      throw new InvalidOperationException("The update is being applied.");
    }
    _status = _status with { Busy = true, Error = null };
    try { await action(); }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      _status = _status with { Applying = false };
      throw;
    }
    catch (OperationCanceledException exception)
    {
      const string message = "The update operation timed out.";
      _status = _status with { Error = message, Applying = false };
      throw new TimeoutException(message, exception);
    }
    catch (Exception exception)
    {
      _status = _status with { Error = exception.Message, Applying = false };
      throw;
    }
    finally
    {
      _status = _status with { Busy = false };
      _operation.Release();
    }
  }

  private void SaveStaged(StagedUpdate staged)
  {
    Directory.CreateDirectory(_directory);
    File.WriteAllText(_receipt + ".tmp", JsonSerializer.Serialize(staged));
    File.Move(_receipt + ".tmp", _receipt, overwrite: true);
    _staged = staged;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    DateTimeOffset nextCheck = DateTimeOffset.MinValue;
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        if (!_status.Busy && !_status.Applying && _status.Scheduled && !UpdateInstaller.MTGORunning)
        {
          await RunAsync(async () =>
          {
            try
            {
              // Recheck the scheduled state after acquiring the operation lock.
              if (await ApplyCoreAsync(onlyIfScheduled: true, stoppingToken)) CompleteHandoff();
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
              if (_staged?.Scheduled == true)
              {
                SaveStaged(_staged with { Scheduled = false });
                _status = _status with { Scheduled = false };
              }
              throw;
            }
          }, stoppingToken, skipIfBusy: true);
        }
        if (!_status.Busy && !_status.Applying && _settings.Current.CheckForUpdates && DateTimeOffset.UtcNow >= nextCheck)
        {
          await RunAsync(async () =>
          {
            nextCheck = DateTimeOffset.UtcNow.AddHours(6);
            await CheckCoreAsync(stoppingToken);
          }, stoppingToken, skipIfBusy: true);
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
      catch (Exception exception)
      {
        Log.Warning("Update operation failed: {Message}", exception.Message);
      }
      try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }
}
