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

public interface IManafoldArchetypeClient
{
  Task<JsonElement> DetectArchetypeAsync(
    IReadOnlyCollection<ManafoldDeckCard> cards,
    string format,
    CancellationToken cancellationToken = default);
}

public sealed record ManafoldDeckCard(
  [property: JsonPropertyName("name")] string Name,
  [property: JsonPropertyName("quantity")] int Quantity);

public sealed class ManafoldAPIException(
  string message,
  int? statusCode = null,
  string? response = null,
  Exception? innerException = null) : Exception(message, innerException)
{
  public int? StatusCode { get; } = statusCode;
  public string? Response { get; } = response;
}

public sealed class ManafoldArchetypeClient(
  HttpClient httpClient,
  ApplicationOptions appOptions) : IManafoldArchetypeClient
{
  public async Task<JsonElement> DetectArchetypeAsync(
    IReadOnlyCollection<ManafoldDeckCard> cards,
    string format,
    CancellationToken cancellationToken = default)
  {
    var requestBody = JsonSerializer.Serialize(cards);
    using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
    var endpoint = $"{appOptions.ManafoldApiUrl.TrimEnd('/')}/{Uri.EscapeDataString(format.Trim().ToLowerInvariant())}";

    try
    {
      using var response = await httpClient.PostAsync(
        endpoint,
        content,
        cancellationToken);
      var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        throw new ManafoldAPIException(
          "Manafold API request failed",
          (int)response.StatusCode,
          responseContent);
      }

      return JsonSerializer.Deserialize<JsonElement>(responseContent);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (ManafoldAPIException)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
    {
      throw new ManafoldAPIException("Failed to connect to Manafold API", innerException: ex);
    }
  }
}
