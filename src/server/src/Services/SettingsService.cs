/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MTGOTracker.Services;

/// <summary>
/// Atomically persists application and plugin preferences, preserving other
/// settings sections.
/// </summary>
public sealed class SettingsService
{
  internal sealed record PluginSettings(bool? Enabled = null, bool Remove = false)
  {
    public JsonObject Configuration { get; init; } = new();
  }

  private readonly object _lock = new();
  private readonly string _path;
  private JsonObject _document = new();
  private ApplicationSettings _application = new();
  private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

  public SettingsService(string path)
  {
    _path = path;
    try
    {
      if (File.Exists(path))
        _document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
          ?? throw new InvalidDataException("The settings document must be an object.");
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
      or JsonException or InvalidOperationException or ArgumentException)
    {
      // An unreadable document cannot be safely updated or used to configure plugins.
      Error = PluginError = $"Cannot read preferences at '{path}': {exception.Message} Restore the settings file and restart Tracker.";
      Startup = _application;
      return;
    }
    try
    {
      _application = _document["application"]?.Deserialize<ApplicationSettings>(s_json) ?? new();
      Validate(_application);
    }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
    {
      Error = $"Cannot read application preferences at '{path}': {exception.Message} Restore the application settings and restart Tracker.";
      _application = new();
    }
    try
    {
      if (_document["plugins"] is JsonNode plugins)
        foreach (var entry in plugins.AsObject()) _ = ReadPlugin(entry.Value);
    }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or InvalidDataException)
    {
      // Invalid plugin preferences must not change valid tracking or application preferences.
      PluginError = $"Cannot read plugin preferences at '{path}': {exception.Message} Restore the plugin settings and restart Tracker.";
    }
    Startup = _application;
  }

  public string? Error { get; }
  public string? PluginError { get; }
  public ApplicationSettings Startup { get; }
  public ApplicationSettings Current { get { lock (_lock) return _application; } }
  public bool RestartRequired
  {
    get
    {
      var current = Current;
      return current.UseCustomTitleBar != Startup.UseCustomTitleBar ||
        current.StartMaximized != Startup.StartMaximized || current.StartupPage != Startup.StartupPage ||
        current.RecordReplays != Startup.RecordReplays || current.RecordTrades != Startup.RecordTrades ||
        current.SynchronizeDecks != Startup.SynchronizeDecks;
    }
  }

  internal void EnsurePluginsAvailable()
  {
    if (PluginError is { } error) throw new InvalidDataException(error);
  }

  public ApplicationSettings Save(ApplicationSettings settings)
  {
    Validate(settings);
    lock (_lock)
    {
      if (Error is not null) throw new InvalidDataException(Error);
      var next = (JsonObject)_document.DeepClone();
      next["application"] = JsonSerializer.SerializeToNode(settings, s_json);
      Persist(next);
      return _application = settings;
    }
  }

  internal PluginSettings GetPlugin(string id)
  {
    lock (_lock)
    {
      EnsurePluginsAvailable();
      return ReadPlugin(_document["plugins"]?[id]);
    }
  }

  private static PluginSettings ReadPlugin(JsonNode? node)
  {
    if (node is null) return new();
    var value = node.Deserialize<PluginSettings>(s_json)
      ?? throw new InvalidDataException("Plugin preferences must be an object.");
    if (value.Configuration is null) throw new InvalidDataException("Plugin configuration must be an object.");
    return value;
  }

  internal void SavePlugin(string id, PluginSettings settings)
  {
    lock (_lock)
    {
      EnsurePluginsAvailable();
      var next = (JsonObject)_document.DeepClone();
      next["plugins"] ??= new JsonObject();
      next["plugins"]![id] = JsonSerializer.SerializeToNode(settings, s_json);
      Persist(next);
    }
  }

  internal void ForgetPlugin(string id)
  {
    lock (_lock)
    {
      EnsurePluginsAvailable();
      var next = (JsonObject)_document.DeepClone();
      if (next["plugins"] is JsonObject plugins && plugins.Remove(id)) Persist(next);
    }
  }

  private void Persist(JsonObject next)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
      File.WriteAllText(temporary, next.ToJsonString(s_json));
      File.Move(temporary, _path, overwrite: true);
      _document = next;
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
  }

  private static void Validate(ApplicationSettings settings)
  {
    if (settings.Theme is not ("dark" or "light" or "system"))
      throw new ArgumentException("Choose dark, light, or system theme.");
    if (settings.StartupPage is not ("/" or "/collection" or "/decks" or "/events" or "/history" or "/trades"))
      throw new ArgumentException("Choose an available page to open on launch.");
    if (settings.HistoryRangeDays is not (0 or 7 or 30 or 90))
      throw new ArgumentException("Choose all time, 7, 30, or 90 days for the initial date range.");
    if (settings.HistoryPageSize is not (25 or 50 or 100))
      throw new ArgumentException("Choose 25, 50, or 100 matches per history page.");
    if (settings.DeckSortMode is not ("cmc" or "colors" or "types" or "rarity"))
      throw new ArgumentException("Choose mana value, color, type, or rarity for deck grouping.");
  }
}
