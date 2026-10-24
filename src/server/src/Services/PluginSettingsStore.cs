/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MTGOTracker.Services;

/// <summary>Atomically persists plugin preferences while preserving other settings sections.</summary>
internal sealed class PluginSettingsStore
{
  private readonly string _path;
  private JsonObject _document = new();
  private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true
  };

  internal PluginSettingsStore(string path)
  {
    _path = path;
    try
    {
      if (File.Exists(path))
      {
        _document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
          ?? throw new InvalidDataException("The settings document must be an object.");
      }
      if (_document["plugins"] is JsonNode plugins)
      {
        foreach (var entry in plugins.AsObject())
        {
          _ = Read(entry.Value);
        }
      }
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
      or JsonException or InvalidOperationException)
    {
      // Never overwrite unreadable preferences or load plugins against guessed defaults.
      Error = $"Cannot read plugin preferences at '{path}': {exception.Message} Restore the settings file and restart Tracker.";
    }
  }

  internal string? Error { get; }

  internal void EnsureAvailable()
  {
    if (Error is not null)
    {
      throw new InvalidDataException(Error);
    }
  }

  internal PluginManager.PluginSettings GetPlugin(string id)
  {
    EnsureAvailable();
    return Read(_document["plugins"]?[id]);
  }

  private static PluginManager.PluginSettings Read(JsonNode? node)
  {
    if (node is null)
    {
      return new();
    }
    var value = node.Deserialize<PluginManager.PluginSettings>(s_json)
      ?? throw new InvalidDataException("Plugin preferences must be an object.");
    if (value.Configuration is null)
    {
      throw new InvalidDataException("Plugin configuration must be an object.");
    }
    return value;
  }

  internal void SavePlugin(string id, PluginManager.PluginSettings settings)
  {
    EnsureAvailable();
    var next = (JsonObject)_document.DeepClone();
    next["plugins"] ??= new JsonObject();
    next["plugins"]![id] = JsonSerializer.SerializeToNode(settings, s_json);
    Persist(next);
  }

  internal void ForgetPlugin(string id)
  {
    EnsureAvailable();
    var next = (JsonObject)_document.DeepClone();
    if (next["plugins"] is JsonObject plugins && plugins.Remove(id))
    {
      Persist(next);
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
    finally
    {
      if (File.Exists(temporary))
      {
        File.Delete(temporary);
      }
    }
  }
}
