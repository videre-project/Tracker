/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace MTGOTracker.Services.Videre.Generated;

internal partial class VidereOpenAPIClient
{
  // Retain response bodies in generated exceptions for Tracker diagnostics.
  partial void Initialize() => ReadResponseAsString = true;

  static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
  {
    settings.PropertyNameCaseInsensitive = true;
    settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    settings.Converters.Add(new ColorCollectionConverter());
    settings.Converters.Add(new JsonStringEnumConverter());
  }

  partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
  {
    // The API advertises QUERY as x-query; NSwag generates the compatible POST operation.
    if (request.Method == HttpMethod.Post && (url == "prices" || url.StartsWith("cards/search?", StringComparison.Ordinal)))
      request.Method = new HttpMethod("QUERY");
  }

  private sealed class ColorCollectionConverter : JsonConverter<ICollection<CardColor>>
  {
    public override ICollection<CardColor> Read(
      ref Utf8JsonReader reader,
      Type typeToConvert,
      JsonSerializerOptions options)
    {
      if (reader.TokenType != JsonTokenType.StartArray)
      {
        throw new JsonException("Expected a Videre color array.");
      }

      var colors = new List<CardColor>();
      while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
      {
        if (reader.TokenType != JsonTokenType.String)
        {
          throw new JsonException("Expected a Videre color string.");
        }

        var value = reader.GetString();
        // Older catalog responses represent colorless as C instead of an empty array.
        if (value == "C") continue;
        if (!Enum.TryParse<CardColor>(value, out var color))
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
      ICollection<CardColor> value,
      JsonSerializerOptions options)
    {
      writer.WriteStartArray();
      foreach (var color in value) writer.WriteStringValue(color.ToString());
      writer.WriteEndArray();
    }
  }
}
