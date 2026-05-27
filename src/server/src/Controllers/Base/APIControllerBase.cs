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
        ToAsyncEnumerable(enumerable, context.HttpContext.RequestAborted),
        context.HttpContext.RequestAborted);
    }
  }

  public class NdjsonStreamAsyncActionResult<T>(
    APIController controller,
    IAsyncEnumerable<T> asyncEnumerable)
      : IActionResult
  {
    public async Task ExecuteResultAsync(ActionContext context)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));

      controller.DisableBuffering();
      controller.SetNdjsonContentType();
      await controller.StreamResponse(
        asyncEnumerable,
        context.HttpContext.RequestAborted);
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
    if (Response.HasStarted) return;
    HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
  }

  [NonAction]
  public void SetNdjsonContentType()
  {
    if (Response.HasStarted) return;
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
      enumerable,
      HttpContext.RequestAborted);

  [NonAction]
  public async Task StreamResponse<T>(
    IEnumerable<T> enumerable,
    CancellationToken cancellationToken) =>
    await StreamResponse(ToAsyncEnumerable(enumerable, cancellationToken), cancellationToken);

  [NonAction]
  public async Task StreamResponse<T>(IAsyncEnumerable<T> asyncEnumerable)
  {
    await StreamResponse(asyncEnumerable, HttpContext.RequestAborted);
  }

  [NonAction]
  public async Task StreamResponse<T>(
    IAsyncEnumerable<T> asyncEnumerable,
    CancellationToken cancellationToken)
  {
    try
    {
      var serializerOptions = GetSerializerOptions();
      await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
      {
        if (cancellationToken.IsCancellationRequested) break;

        await JsonSerializer.SerializeAsync<T>(
          Response.Body,
          item,
          serializerOptions,
          cancellationToken
        );
        await Response.Body.WriteAsync(_ndDelimiter, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Client disconnected or stream owner cancelled the request.
    }
    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
    {
      // ASP.NET may dispose response features before late stream writes unwind.
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

    var requestAborted = HttpContext.RequestAborted;
    var semaphore = new SemaphoreSlim(1, 1);
    var acceptingCallbacks = true;
    async Task eventCallback(TEvent evt)
    {
      if (!Volatile.Read(ref acceptingCallbacks) || requestAborted.IsCancellationRequested) return;

      var entered = false;
      try
      {
        await semaphore.WaitAsync(requestAborted);
        entered = true;
        if (!Volatile.Read(ref acceptingCallbacks) || requestAborted.IsCancellationRequested) return;
        await onEvent(evt);
      }
      catch (OperationCanceledException)
      {
        // Stream was cancelled, ignore
      }
      catch (ObjectDisposedException) when (!Volatile.Read(ref acceptingCallbacks) || requestAborted.IsCancellationRequested)
      {
        // ASP.NET can dispose HttpContext features before a late event callback unwinds.
      }
      finally
      {
        if (entered)
        {
          semaphore.Release();
        }
      }
    }
    subscribe(eventCallback);

    var cts = new TaskCompletionSource<bool>();
    using var cancellationRegistration = requestAborted.Register(() =>
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      cts.TrySetResult(true);
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
    var streamToken = linkedCts.Token;

    var semaphore = new SemaphoreSlim(1, 1);
    var callbacks = new List<Task>();
    var callbacksLock = new object();
    var acceptingCallbacks = true;

    async void eventCallback(T1 arg1, T2 arg2)
    {
      if (!Volatile.Read(ref acceptingCallbacks) || streamToken.IsCancellationRequested) return;

      var callbackTask = handleEventCallback(arg1, arg2);
      lock (callbacksLock)
      {
        callbacks.Add(callbackTask);
      }

      try
      {
        await callbackTask;
      }
      finally
      {
        lock (callbacksLock)
        {
          callbacks.Remove(callbackTask);
        }
      }
    }

    async Task handleEventCallback(T1 arg1, T2 arg2)
    {
      var entered = false;
      try
      {
        await semaphore.WaitAsync(streamToken);
        entered = true;
        if (!Volatile.Read(ref acceptingCallbacks) || streamToken.IsCancellationRequested) return;
        await onEvent(arg1, arg2);
      }
      catch (OperationCanceledException)
      {
        // Stream was cancelled, ignore
      }
      catch (ObjectDisposedException) when (!Volatile.Read(ref acceptingCallbacks) || streamToken.IsCancellationRequested)
      {
        // ASP.NET can dispose HttpContext features before a late event callback unwinds.
      }
      finally
      {
        if (entered)
        {
          semaphore.Release();
        }
      }
    }
    subscribe(eventCallback);

    var tcs = new TaskCompletionSource<bool>();
    using var cancellationRegistration = streamToken.Register(() =>
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      tcs.TrySetResult(true);
    });

    try
    {
      await tcs.Task;
    }
    catch (OperationCanceledException)
    {
      // Stream cancelled gracefully
    }

    Task[] pendingCallbacks;
    lock (callbacksLock)
    {
      pendingCallbacks = callbacks.ToArray();
    }
    await Task.WhenAll(pendingCallbacks);

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
    var streamToken = linkedCts.Token;

    var semaphore = new SemaphoreSlim(1, 1);
    var callbacks = new List<Task>();
    var callbacksLock = new object();
    var acceptingCallbacks = true;

    async void eventCallback(object? sender, TEventArgs args)
    {
      if (!Volatile.Read(ref acceptingCallbacks) || streamToken.IsCancellationRequested) return;

      var callbackTask = handleEventCallback(sender, args);
      lock (callbacksLock)
      {
        callbacks.Add(callbackTask);
      }

      try
      {
        await callbackTask;
      }
      finally
      {
        lock (callbacksLock)
        {
          callbacks.Remove(callbackTask);
        }
      }
    }

    async Task handleEventCallback(object? sender, TEventArgs args)
    {
      var entered = false;
      try
      {
        await semaphore.WaitAsync(streamToken);
        entered = true;
        if (!Volatile.Read(ref acceptingCallbacks) || streamToken.IsCancellationRequested) return;
        await onEvent(sender, args);
      }
      catch (OperationCanceledException)
      {
        // Stream was cancelled, ignore
      }
      catch (ObjectDisposedException) when (!Volatile.Read(ref acceptingCallbacks) || streamToken.IsCancellationRequested)
      {
        // ASP.NET can dispose HttpContext features before a late event callback unwinds.
      }
      finally
      {
        if (entered)
        {
          semaphore.Release();
        }
      }
    }
    subscribe(eventCallback);

    var tcs = new TaskCompletionSource<bool>();
    using var cancellationRegistration = streamToken.Register(() =>
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      tcs.TrySetResult(true);
    });

    try
    {
      await tcs.Task;
    }
    catch (OperationCanceledException)
    {
      // Stream cancelled gracefully
    }

    Task[] pendingCallbacks;
    lock (callbacksLock)
    {
      pendingCallbacks = callbacks.ToArray();
    }
    await Task.WhenAll(pendingCallbacks);

    return new EmptyResult();
  }

  [NonAction]
  protected NdjsonStreamActionResult<T> NdjsonStream<T>(IEnumerable<T> enumerable)
  {
    return new NdjsonStreamActionResult<T>(this, enumerable);
  }

  [NonAction]
  protected NdjsonStreamAsyncActionResult<T> NdjsonStream<T>(IAsyncEnumerable<T> asyncEnumerable)
  {
    return new NdjsonStreamAsyncActionResult<T>(this, asyncEnumerable);
  }
}
