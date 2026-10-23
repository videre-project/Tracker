/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Tracker.Services;

/// <summary>Stages plugin installations and preferences without modifying the running host.</summary>
public sealed class PluginManager
{
  private readonly string _directory;
  private readonly PluginSettingsStore _settings;
  private readonly PluginService.PluginCatalog _catalog;
  private readonly object _lock = new();
  private readonly Dictionary<string, JsonObject> _startupConfiguration = new();
  private readonly Dictionary<string, bool> _startupEnabled = new();

  public PluginManager(string directory, string dataDirectory, PluginService.PluginCatalog catalog)
  {
    _directory = Path.GetFullPath(directory);
    string dataPath = Path.GetFullPath(dataDirectory);
    _catalog = catalog;
    _settings = new PluginSettingsStore(Path.Combine(Path.GetDirectoryName(dataPath)!, "settings.json"));
  }

  public sealed record PluginInfo(
    string Id, string Name, string? Description, string Version,
    bool Enabled, bool Loaded, bool PendingUpdate, bool PendingRemoval,
    bool RestartRequired, string? Error);

  public sealed record ConfigurationField(
    string Key, string Label, string? Description, string Type, bool Required,
    bool Secret, JsonNode? Default, JsonArray? Choices);

  public sealed record PluginConfiguration(JsonObject Values, IReadOnlyList<ConfigurationField> Fields);
  public sealed record PluginSettings(bool? Enabled = null, bool Remove = false)
  {
    public JsonObject Configuration { get; init; } = new();
  }

  internal sealed record PluginManifest
  {
    public required string Id { get; init; }
    public required string Assembly { get; init; }
    public required string EntryPoint { get; init; }
    public required int ApiVersion { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
  }

  public bool HostEnabled => _catalog.Enabled;
  public string? SettingsError => _settings.Error;
  public bool RestartRequired => List().Any(plugin => plugin.RestartRequired);

  internal bool Prepare()
  {
    try
    {
      _settings.EnsureAvailable();
      Directory.CreateDirectory(_directory);
      ApplyPendingChanges();
      return true;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
      _catalog.ReportFailure(_directory, exception);
      return false;
    }
  }

  internal PluginSettings ReadSettings(string id) => _settings.GetPlugin(id);

  internal void RecordStartupSettings(string id, PluginSettings settings, bool enabled)
  {
    _startupConfiguration[id] = (JsonObject)settings.Configuration.DeepClone();
    _startupEnabled[id] = enabled;
  }

  internal IConfiguration BuildConfiguration(string id, string directory, IConfiguration host)
  {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ReadConfiguration(id, directory).Values.ToJsonString()));
    return new ConfigurationBuilder().AddJsonStream(stream)
      .AddConfiguration(host.GetSection($"Plugins:{id}")).Build();
  }

  public IReadOnlyList<PluginInfo> List()
  {
    lock (_lock)
    {
      var directories = InstallationDirectories().ToDictionary(directory => Path.GetFileName(directory)!, StringComparer.OrdinalIgnoreCase);
      string pendingRoot = Path.Combine(_directory, ".pending");
      if (Directory.Exists(pendingRoot))
      {
        foreach (string pending in Directory.EnumerateDirectories(pendingRoot))
        {
          directories[Path.GetFileName(pending)] = pending;
        }
      }
      var result = new List<PluginInfo>();
      foreach (var entry in directories.OrderBy(entry => entry.Key, StringComparer.Ordinal))
      {
        string id = entry.Key;
        var settings = SettingsError is null ? _settings.GetPlugin(id) : new PluginSettings();
        bool pending = Path.GetDirectoryName(entry.Value) == pendingRoot;
        var loadedPlugin = _catalog.Plugins.FirstOrDefault(plugin => plugin.Id == id);
        bool loaded = loadedPlugin is not null;
        string manifestPath = Path.Combine(_directory, id, "plugin.json");
        string? failure = _catalog.Failures.FirstOrDefault(error => error.Path == manifestPath)?.Message;
        try
        {
          var manifest = ReadValidatedManifest(entry.Value);
          if (manifest.Id != id)
          {
            throw new InvalidDataException($"The installation folder must be named '{manifest.Id}'.");
          }
          bool enabled = settings.Enabled ?? manifest.Enabled;
          bool changed = pending || settings.Remove ||
            (_catalog.Enabled && enabled != _startupEnabled.GetValueOrDefault(id)) ||
            (_startupConfiguration.TryGetValue(id, out var startup) && !JsonNode.DeepEquals(startup, settings.Configuration));
          result.Add(new(id, manifest.Name ?? id, manifest.Description,
            manifest.Version ?? loadedPlugin?.Version ?? AssemblyName.GetAssemblyName(Path.Combine(entry.Value, manifest.Assembly)).Version?.ToString() ?? "unknown",
            enabled, loaded, pending, settings.Remove, changed, failure));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
          or JsonException or ArgumentException or BadImageFormatException)
        {
          result.Add(new(id, id, null, loadedPlugin?.Version ?? "unknown", settings.Enabled ?? true,
            loaded, pending, settings.Remove, settings.Remove || pending, failure ?? exception.Message));
        }
      }
      return result;
    }
  }

  /// <summary>
  /// Stages a published plugin ZIP for installation on the next host start.
  /// </summary>
  public void Install(Stream package)
  {
    lock (_lock)
    {
      _settings.EnsureAvailable();
      string staging = Path.Combine(_directory, ".staging", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(staging);
      try
      {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        const long expandedLimit = 512L * 1024 * 1024;
        if (archive.Entries.Count > 10000 || archive.Entries.Any(entry => entry.Length > expandedLimit)
          || archive.Entries.Sum(entry => entry.Length) > expandedLimit)
        {
          throw new InvalidDataException("The expanded plugin package exceeds 512 MiB or 10,000 entries.");
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
          string path = Path.GetFullPath(Path.Combine(staging, entry.FullName));
          if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || (!entry.FullName.EndsWith('/') && !files.Add(path)))
          {
            throw new InvalidDataException("The ZIP contains duplicate files or paths outside its plugin folder.");
          }
        }
        archive.ExtractToDirectory(staging);
        string[] manifests = Directory.GetFiles(staging, "plugin.json", SearchOption.AllDirectories);
        if (manifests.Length != 1)
        {
          throw new InvalidDataException("The ZIP must contain exactly one plugin.json manifest.");
        }
        string source = Path.GetDirectoryName(manifests[0])!;
        var manifest = ReadValidatedManifest(source);
        if (!File.Exists(Path.Combine(source, Path.ChangeExtension(manifest.Assembly, ".deps.json"))))
        {
          throw new InvalidDataException("The package is missing its .deps.json file. Install the complete published plugin output.");
        }
        ReadConfigurationFields(source);
        string target = Path.Combine(_directory, ".pending", manifest.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var settings = _settings.GetPlugin(manifest.Id);
        _settings.SavePlugin(manifest.Id, settings with { Remove = false });
        try
        {
          ReplaceDirectory(source, target);
        }
        catch
        {
          _settings.SavePlugin(manifest.Id, settings);
          throw;
        }
      }
      finally
      {
        if (Directory.Exists(staging))
        {
          Directory.Delete(staging, recursive: true);
        }
      }
    }
  }

  public void SetEnabled(string id, bool enabled)
  {
    lock (_lock)
    {
      string directory = FindDirectory(id);
      if (enabled)
      {
        ReadValidatedManifest(directory);
      }
      var settings = _settings.GetPlugin(id);
      _settings.SavePlugin(id, settings with { Enabled = enabled });
    }
  }

  public void Remove(string id)
  {
    lock (_lock)
    {
      FindDirectory(id);
      _settings.SavePlugin(id, _settings.GetPlugin(id) with { Remove = true });
    }
  }

  public void CancelRemoval(string id)
  {
    lock (_lock)
    {
      FindDirectory(id);
      _settings.SavePlugin(id, _settings.GetPlugin(id) with { Remove = false });
    }
  }

  public PluginConfiguration GetConfiguration(string id)
  {
    lock (_lock)
    {
      return ReadConfiguration(id, FindDirectory(id));
    }
  }

  private PluginConfiguration ReadConfiguration(string id, string directory)
  {
    JsonObject values = _settings.GetPlugin(id).Configuration;
    var fields = ReadConfigurationFields(directory);
    foreach (var field in fields)
    {
      if (!values.ContainsKey(field.Key) && field.Default is not null)
      {
        values[field.Key] = field.Default.DeepClone();
      }
    }
    return new(values, fields);
  }

  public void SaveConfiguration(string id, JsonObject values)
  {
    lock (_lock)
    {
      var fields = ReadConfigurationFields(FindDirectory(id));
      foreach (var field in fields)
      {
        bool supplied = values.TryGetPropertyValue(field.Key, out var value);
        if (!supplied)
        {
          value = field.Default;
        }
        if (!supplied && value is null)
        {
          if (field.Required)
          {
            throw new ArgumentException($"{field.Label} is required.");
          }
          continue;
        }
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        bool valid = field.Type switch
        {
          "string" => kind == JsonValueKind.String,
          "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
          "integer" => kind == JsonValueKind.Number && value!.AsValue().TryGetValue<long>(out _),
          "number" => kind == JsonValueKind.Number,
          "object" => kind == JsonValueKind.Object,
          "array" => kind == JsonValueKind.Array,
          _ => true
        };
        if (!valid || (field.Choices is not null && !field.Choices.Any(choice => JsonNode.DeepEquals(choice, value))))
        {
          throw new ArgumentException($"Invalid value for {field.Label}.");
        }
      }
      _settings.SavePlugin(id, _settings.GetPlugin(id) with { Configuration = (JsonObject)values.DeepClone() });
    }
  }

  private string FindDirectory(string id)
  {
    ValidateDirectoryName(id);
    string pending = Path.Combine(_directory, ".pending", id);
    if (Directory.Exists(pending))
    {
      return pending;
    }
    string installed = Path.Combine(_directory, id);
    if (!Directory.Exists(installed))
    {
      throw new KeyNotFoundException($"Plugin '{id}' is not installed.");
    }
    return installed;
  }

  private IEnumerable<string> InstallationDirectories() => Directory.Exists(_directory)
    ? Directory.EnumerateDirectories(_directory).Where(directory => !Path.GetFileName(directory).StartsWith('.'))
    : Array.Empty<string>();

  private void ApplyPendingChanges()
  {
    string root = Path.Combine(_directory, ".pending");
    var directories = InstallationDirectories().ToList();
    if (Directory.Exists(root))
    {
      directories.AddRange(Directory.EnumerateDirectories(root));
    }
    foreach (var group in directories.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
    {
      string id = group.Key!;
      string installed = Path.Combine(_directory, id);
      string pending = Path.Combine(root, id);
      try
      {
        if (_settings.GetPlugin(id).Remove)
        {
          foreach (string directory in group)
          {
            Directory.Delete(directory, recursive: true);
          }
          _settings.ForgetPlugin(id);
        }
        else if (Directory.Exists(pending))
        {
          var manifest = ReadValidatedManifest(pending);
          if (manifest.Id != id)
          {
            throw new InvalidDataException("The pending installation folder does not match its manifest ID.");
          }
          ReplaceDirectory(pending, installed);
        }
      }
      catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
      {
        _catalog.ReportFailure(Path.Combine(installed, "plugin.json"), exception);
      }
    }
  }

  private void ReplaceDirectory(string source, string target)
  {
    string backup = Path.Combine(_directory, ".backup", Guid.NewGuid().ToString("N"));
    bool previous = Directory.Exists(target);
    if (previous)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
      Directory.Move(target, backup);
    }
    try
    {
      Directory.Move(source, target);
    }
    catch
    {
      if (previous)
      {
        Directory.Move(backup, target);
      }
      throw;
    }
    if (previous)
    {
      try
      {
        Directory.Delete(backup, recursive: true);
      }
      catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
      {
        Console.Error.WriteLine($"Could not remove plugin backup '{backup}': {exception.Message}");
      }
    }
  }

  internal static PluginManifest ReadManifest(string directory) =>
    JsonSerializer.Deserialize<PluginManifest>(
      File.ReadAllText(Path.Combine(directory, "plugin.json")), JsonSerializerOptions.Web)
      ?? throw new InvalidDataException("The plugin manifest is empty.");

  private static PluginManifest ReadValidatedManifest(string directory)
  {
    var manifest = ReadManifest(directory);
    ValidateManifest(manifest, directory);
    return manifest;
  }

  internal static void ValidateManifest(PluginManifest manifest, string directory)
  {
    ValidateId(manifest.Id);
    if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
    {
      throw new InvalidDataException("The plugin entry point is required.");
    }
    if (manifest.ApiVersion != 1)
    {
      throw new InvalidDataException($"Unsupported plugin API version {manifest.ApiVersion}; expected 1.");
    }
    ValidateDirectoryName(manifest.Assembly);
    if (!File.Exists(Path.Combine(directory, manifest.Assembly)))
    {
      throw new InvalidDataException("The plugin assembly is missing from its package.");
    }
  }

  private static void ValidateId(string id)
  {
    if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[a-z][a-z0-9-]*$"))
    {
      throw new InvalidDataException("Plugin IDs must begin with a lowercase letter and contain only lowercase letters, digits, or hyphens.");
    }
  }

  private static void ValidateDirectoryName(string name)
  {
    if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.') || name.Contains('/') || name.Contains('\\')
      || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.'))
    {
      throw new InvalidDataException("The installation name must be a filename inside the plugin directory.");
    }
  }

  private static IReadOnlyList<ConfigurationField> ReadConfigurationFields(string directory)
  {
    string path = Path.Combine(directory, "settings.schema.json");
    if (!File.Exists(path))
    {
      return Array.Empty<ConfigurationField>();
    }
    try
    {
      var schema = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException("The settings schema must be an object.");
      if (schema["type"]?.GetValue<string>() != "object" || schema["properties"] is not JsonObject properties)
      {
        throw new InvalidDataException("The settings schema must describe an object with properties.");
      }
      var required = schema["required"]?.AsArray().Select(item => item?.GetValue<string>()
          ?? throw new InvalidDataException("Required field names must be strings.")).ToHashSet()
        ?? new HashSet<string>();
      if (required.Any(key => !properties.ContainsKey(key)))
      {
        throw new InvalidDataException("Required field names must refer to declared properties.");
      }
      return properties.Select(property =>
      {
        if (property.Value is not JsonObject field)
        {
          throw new InvalidDataException($"The settings field '{property.Key}' must be an object.");
        }
        string type = field["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var name)
          ? name : "any";
        return new ConfigurationField(property.Key, field["title"]?.GetValue<string>() ?? property.Key,
          field["description"]?.GetValue<string>(), type, required.Contains(property.Key),
          field["writeOnly"]?.GetValue<bool>() ?? false,
          field["default"]?.DeepClone(), field["enum"]?.DeepClone().AsArray());
      }).ToArray();
    }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
    {
      throw new InvalidDataException("Invalid plugin settings schema: " + exception.Message, exception);
    }
  }

}
