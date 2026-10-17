/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using MTGOSDK.API.Interface.Controls;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Reflection.Serialization;


namespace Tracker.Services.MTGO.Events;

/// <summary>
/// A captured prompt option with typed action identity and local control fields.
/// Native action-specific fields retain the schema emitted by MTGOSDK's ToJSON().
/// </summary>
public sealed record PromptOptionData
{
  [JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
  public required ActionType Type { get; init; }
  public string? Name { get; init; }

  [JsonPropertyName("$type")]
  public string? ActionClass { get; init; }
  public int? ActionId { get; init; }

  public bool? Enabled { get; init; }
  public PromptControlId? ControlId { get; init; }
  public string? ButtonType { get; init; }
  public string? Color { get; init; }
  public int? PromptNonce { get; init; }

  /// <summary>
  /// Decimal representation of the SDK's 64-bit revision, preserved across JavaScript clients.
  /// </summary>
  [JsonConverter(typeof(ClientRevisionJsonConverter))]
  public string? ClientRevision { get; init; }

  /// <summary>
  /// SDK-defined action fields, such as targets, choices, and distributions.
  /// These are written alongside the common fields without changing their shape.
  /// </summary>
  [JsonExtensionData]
  public Dictionary<string, JsonElement>? ActionData { get; init; }

  internal static PromptOptionData FromAction(GameAction action) =>
    JsonSerializer.Deserialize<PromptOptionData>(action.ToJSON(), JsonSerializerOptions.Web)!;

  internal bool HasSameValue(PromptOptionData other)
  {
    if (Type != other.Type || Name != other.Name
      || ActionClass != other.ActionClass || ActionId != other.ActionId
      || Enabled != other.Enabled || ControlId != other.ControlId
      || ButtonType != other.ButtonType || Color != other.Color
      || PromptNonce != other.PromptNonce || ClientRevision != other.ClientRevision)
      return false;

    if (ActionData is null || ActionData.Count == 0)
      return other.ActionData is null || other.ActionData.Count == 0;
    if (other.ActionData is null || ActionData.Count != other.ActionData.Count)
      return false;

    // Native action object member order is not meaningful.
    return ActionData.All(pair => other.ActionData.TryGetValue(pair.Key, out var value)
      && JsonElement.DeepEquals(pair.Value, value));
  }
}

/// <summary>
/// Reads older numeric client revisions and writes their exact decimal strings.
/// </summary>
internal sealed class ClientRevisionJsonConverter : JsonConverter<string>
{
  public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
    reader.TokenType == JsonTokenType.Number
      ? reader.GetInt64().ToString(CultureInfo.InvariantCulture)
      : reader.GetString()!;

  public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
    writer.WriteStringValue(value);
}
