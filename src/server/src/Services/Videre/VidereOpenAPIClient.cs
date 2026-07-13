/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace Tracker.Services.Videre.Generated;

internal partial class VidereOpenAPIClient
{
  static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
  {
    settings.PropertyNameCaseInsensitive = true;
    settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    settings.Converters.Add(new ColorCollectionConverter<Colors>());
    settings.Converters.Add(new ColorCollectionConverter<Colors2>());
    settings.Converters.Add(new ColorCollectionConverter<Color_identity>());
    settings.Converters.Add(new JsonStringEnumConverter());
  }

  private sealed class ColorCollectionConverter<TColor> : JsonConverter<ICollection<TColor>>
    where TColor : struct, Enum
  {
    public override ICollection<TColor> Read(
      ref Utf8JsonReader reader,
      Type typeToConvert,
      JsonSerializerOptions options)
    {
      if (reader.TokenType != JsonTokenType.StartArray)
      {
        throw new JsonException("Expected a Videre color array.");
      }

      var colors = new List<TColor>();
      while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
      {
        if (reader.TokenType != JsonTokenType.String)
        {
          throw new JsonException("Expected a Videre color string.");
        }

        var value = reader.GetString();
        if (value == "C") continue;
        if (!Enum.TryParse<TColor>(value, out var color))
        {
          throw new JsonException($"Unknown Videre color '{value}'.");
        }
        colors.Add(color);
      }

      if (reader.TokenType != JsonTokenType.EndArray)
      {
        throw new JsonException("Incomplete Videre color array.");
      }
      return colors;
    }

    public override void Write(
      Utf8JsonWriter writer,
      ICollection<TColor> value,
      JsonSerializerOptions options)
    {
      writer.WriteStartArray();
      foreach (var color in value) writer.WriteStringValue(color.ToString());
      writer.WriteEndArray();
    }
  }
}
