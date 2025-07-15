/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;


namespace Tracker.Controllers;

public abstract class APIController : ControllerBase
{
  public class NdjsonStreamActionResult<T>(
    APIController controller,
    IEnumerable<T> enumerable)
      : IActionResult
  {
    public async Task ExecuteResultAsync(ActionContext context)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));

      controller.DisableBuffering();
      controller.SetNdjsonContentType();
      await controller.StreamResponse(
        ToAsyncEnumerable(enumerable, context.HttpContext.RequestAborted));
    }
  }

  private readonly byte[] _ndDelimiter = Encoding.UTF8.GetBytes("\n");

  private JsonSerializerOptions _serializerOptions = null!;
  private JsonSerializerOptions GetSerializerOptions() =>
    _serializerOptions ??= new(
      HttpContext.RequestServices
        .GetRequiredService<IOptions<JsonOptions>>()
        .Value
        .JsonSerializerOptions
    )
    {
      WriteIndented = false,
    };

  public void DisableBuffering()
  {
    HttpContext.Features.Get<IHttpResponseBodyFeature>()!.DisableBuffering();
  }

  public void SetNdjsonContentType()
  {
    Response.ContentType = "application/x-ndjson";
    Response.Headers.XContentTypeOptions = "nosniff";
  }

  public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
    IEnumerable<T> enumerable,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    foreach (var item in enumerable)
    {
      await Task.Yield();
      cancellationToken.ThrowIfCancellationRequested();
      yield return item;
    }
  }

  public async Task StreamResponse<T>(IEnumerable<T> enumerable) =>
    await StreamResponse(
      ToAsyncEnumerable(enumerable, HttpContext.RequestAborted));

  public async Task StreamResponse<T>(IAsyncEnumerable<T> asyncEnumerable)
  {
    var serializerOptions = GetSerializerOptions();
    await foreach (var item in asyncEnumerable.WithCancellation(HttpContext.RequestAborted))
    {
      if (HttpContext.RequestAborted.IsCancellationRequested) break;

      await JsonSerializer.SerializeAsync<T>(
        Response.Body,
        item,
        serializerOptions,
        HttpContext.RequestAborted
      );
      await Response.Body.WriteAsync(_ndDelimiter, HttpContext.RequestAborted);
      await Response.Body.FlushAsync(HttpContext.RequestAborted);
    }
  }

  protected NdjsonStreamActionResult<T> NdjsonStream<T>(IEnumerable<T> enumerable)
  {
    return new NdjsonStreamActionResult<T>(this, enumerable);
  }
}
