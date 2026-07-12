/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;


namespace Tracker.Services.Videre;

public interface INBACArchetypeClient
{
  Task<JsonElement> DetectArchetypeAsync(
    IReadOnlyCollection<NBACDeckCard> cards,
    string format,
    CancellationToken cancellationToken = default);
}

public sealed record NBACDeckCard(
  [property: JsonPropertyName("name")] string Name,
  [property: JsonPropertyName("quantity")] int Quantity);

public sealed class NBACAPIException(
  string message,
  int? statusCode = null,
  string? response = null,
  Exception? innerException = null) : Exception(message, innerException)
{
  public int? StatusCode { get; } = statusCode;
  public string? Response { get; } = response;
}

public sealed class NBACArchetypeClient(
  HttpClient httpClient,
  ApplicationOptions appOptions) : INBACArchetypeClient
{
  public async Task<JsonElement> DetectArchetypeAsync(
    IReadOnlyCollection<NBACDeckCard> cards,
    string format,
    CancellationToken cancellationToken = default)
  {
    var requestBody = JsonSerializer.Serialize(cards);
    using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

    try
    {
      using var response = await httpClient.PostAsync(
        $"{appOptions.NbacApiUrl}?format={Uri.EscapeDataString(format.ToLowerInvariant())}&explain=1",
        content,
        cancellationToken);
      var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        throw new NBACAPIException(
          "NBAC API request failed",
          (int)response.StatusCode,
          responseContent);
      }

      return JsonSerializer.Deserialize<JsonElement>(responseContent);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (NBACAPIException)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
    {
      throw new NBACAPIException("Failed to connect to NBAC API", innerException: ex);
    }
  }
}
