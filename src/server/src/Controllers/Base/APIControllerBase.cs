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


namespace Tracker.Controllers.Base;

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

  [NonAction]
  public void DisableBuffering()
  {
    HttpContext.Features.Get<IHttpResponseBodyFeature>()!.DisableBuffering();
  }

  [NonAction]
  public void SetNdjsonContentType()
  {
    Response.ContentType = "application/x-ndjson";
    Response.Headers.XContentTypeOptions = "nosniff";
  }

  [NonAction]
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

  [NonAction]
  public async Task StreamResponse<T>(IEnumerable<T> enumerable) =>
    await StreamResponse(
      ToAsyncEnumerable(enumerable, HttpContext.RequestAborted));

  [NonAction]
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

  /// <summary>
  /// Streams server-sent events as newline-delimited JSON (NDJSON).
  /// </summary>
  /// <typeparam name="TEvent">The type of event to stream.</typeparam>
  /// <param name="subscribe">An action to subscribe to the event source.</param>
  /// <param name="unsubscribe">An action to unsubscribe from the event source.</param>
  /// <param name="onEvent">A callback to invoke when an event is received.</param>
  /// <returns>An <see cref="IActionResult"/> that streams the events.</returns
  [NonAction]
  public async Task<IActionResult> StreamNdjsonEvent<TEvent>(
    Action<Func<TEvent, Task>> subscribe,
    Action<Func<TEvent, Task>> unsubscribe,
    Func<TEvent, Task> onEvent)
  {
    DisableBuffering();
    SetNdjsonContentType();

    using var semaphore = new SemaphoreSlim(1, 1);
    async Task eventCallback(TEvent evt)
    {
      await semaphore.WaitAsync(HttpContext.RequestAborted);
      try
      {
        await onEvent(evt);
      }
      finally
      {
        semaphore.Release();
      }
    }
    subscribe(eventCallback);

    var cts = new TaskCompletionSource<bool>();
    HttpContext.RequestAborted.Register(() =>
    {
      unsubscribe(eventCallback);
      cts.SetResult(true);
    });
    await cts.Task;

    return Ok();
  }

  /// <summary>
  /// Streams server-sent events as newline-delimited JSON (NDJSON) for events with two parameters.
  /// </summary>
  /// <typeparam name="T1">The type of the first event argument.</typeparam>
  /// <typeparam name="T2">The type of the second event argument.</typeparam>
  /// <param name="subscribe">An action to subscribe to the event source.</param>
  /// <param name="unsubscribe">An action to unsubscribe from the event source.</param>
  /// <param name="onEvent">A callback to invoke when an event is received.</param>
  /// <param name="externalCancellationToken">Optional external cancellation token to cancel the stream.</param>
  /// <returns>An <see cref="IActionResult"/> that streams the events.</returns
  [NonAction]
  public async Task<IActionResult> StreamNdjsonEvent<T1, T2>(
    Action<Action<T1, T2>> subscribe,
    Action<Action<T1, T2>> unsubscribe,
    Func<T1, T2, Task> onEvent,
    CancellationToken externalCancellationToken = default)
  {
    DisableBuffering();
    SetNdjsonContentType();

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      HttpContext.RequestAborted,
      externalCancellationToken);

    using var semaphore = new SemaphoreSlim(1, 1);
    async void eventCallback(T1 arg1, T2 arg2)
    {
      if (linkedCts.Token.IsCancellationRequested) return;

      await semaphore.WaitAsync(linkedCts.Token);
      try
      {
        await onEvent(arg1, arg2);
      }
      catch (OperationCanceledException)
      {
        // Stream was cancelled, ignore
      }
      finally
      {
        semaphore.Release();
      }
    }
    subscribe(eventCallback);

    var tcs = new TaskCompletionSource<bool>();
    linkedCts.Token.Register(() =>
    {
      unsubscribe(eventCallback);
      tcs.SetResult(true);
    });

    try
    {
      await tcs.Task;
    }
    catch (OperationCanceledException)
    {
      // Stream cancelled gracefully
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Streams server-sent events as newline-delimited JSON (NDJSON) for events with sender/args pattern.
  /// </summary>
  /// <typeparam name="TEventArgs">The type of event arguments to stream.</typeparam>
  /// <param name="subscribe">An action to subscribe to the event source.</param>
  /// <param name="unsubscribe">An action to unsubscribe from the event source.</param>
  /// <param name="onEvent">A callback to invoke when an event is received.</param>
  /// <param name="externalCancellationToken">Optional external cancellation token to cancel the stream.</param>
  /// <returns>An <see cref="IActionResult"/> that streams the events.</returns>
  [NonAction]
  public async Task<IActionResult> StreamNdjsonEventHandler<TEventArgs>(
    Action<EventHandler<TEventArgs>> subscribe,
    Action<EventHandler<TEventArgs>> unsubscribe,
    Func<object?, TEventArgs, Task> onEvent,
    CancellationToken externalCancellationToken = default)
  {
    DisableBuffering();
    SetNdjsonContentType();

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      HttpContext.RequestAborted,
      externalCancellationToken);

    using var semaphore = new SemaphoreSlim(1, 1);
    async void eventCallback(object? sender, TEventArgs args)
    {
      if (linkedCts.Token.IsCancellationRequested) return;

      await semaphore.WaitAsync(linkedCts.Token);
      try
      {
        await onEvent(sender, args);
      }
      catch (OperationCanceledException)
      {
        // Stream was cancelled, ignore
      }
      finally
      {
        semaphore.Release();
      }
    }
    subscribe(eventCallback);

    var tcs = new TaskCompletionSource<bool>();
    linkedCts.Token.Register(() =>
    {
      unsubscribe(eventCallback);
      tcs.SetResult(true);
    });

    try
    {
      await tcs.Task;
    }
    catch (OperationCanceledException)
    {
      // Stream cancelled gracefully
    }

    return new EmptyResult();
  }

  [NonAction]
  protected NdjsonStreamActionResult<T> NdjsonStream<T>(IEnumerable<T> enumerable)
  {
    return new NdjsonStreamActionResult<T>(this, enumerable);
  }
}
