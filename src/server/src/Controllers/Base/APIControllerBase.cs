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
using System.Threading.Channels;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;


namespace Tracker.Controllers.Base;

public abstract class APIController : ControllerBase
{
  protected sealed class CoalescingUpdateQueue<TKey, TValue>
    where TKey : notnull
  {
    private readonly object _gate = new();
    private readonly Dictionary<TKey, TValue> _pending = new();
    private readonly Channel<TKey> _readyKeys = Channel.CreateUnbounded<TKey>(
      new UnboundedChannelOptions
      {
        SingleReader = true,
        SingleWriter = false,
      });
    private bool _completed;

    public bool Enqueue(TKey key, TValue value)
    {
      bool shouldSignal;
      lock (_gate)
      {
        if (_completed) return false;

        shouldSignal = !_pending.ContainsKey(key);
        _pending[key] = value;
      }

      if (!shouldSignal)
      {
        return true;
      }

      return _readyKeys.Writer.TryWrite(key);
    }

    public async IAsyncEnumerable<TValue> ReadAllAsync(
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      while (await _readyKeys.Reader.WaitToReadAsync(cancellationToken))
      {
        while (_readyKeys.Reader.TryRead(out var key))
        {
          TValue value;
          lock (_gate)
          {
            if (!_pending.Remove(key, out value!))
            {
              continue;
            }
          }

          yield return value;
        }
      }
    }

    public void Complete()
    {
      lock (_gate)
      {
        _completed = true;
        _pending.Clear();
      }

      _readyKeys.Writer.TryComplete();
    }
  }

  private static BoundedChannelOptions StreamCallbackChannelOptions(int capacity = 256) => new(capacity)
  {
    SingleReader = true,
    SingleWriter = false,
    FullMode = BoundedChannelFullMode.DropOldest,
  };
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
    var channel = Channel.CreateBounded<TEvent>(StreamCallbackChannelOptions());
    var acceptingCallbacks = true;

    Task eventCallback(TEvent evt)
    {
      if (Volatile.Read(ref acceptingCallbacks) && !requestAborted.IsCancellationRequested)
      {
        channel.Writer.TryWrite(evt);
      }

      return Task.CompletedTask;
    }
    subscribe(eventCallback);

    using var cancellationRegistration = requestAborted.Register(() =>
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      channel.Writer.TryComplete();
    });

    try
    {
      await foreach (var evt in channel.Reader.ReadAllAsync(requestAborted))
      {
        await onEvent(evt);
      }
    }
    catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
    {
      // Stream cancelled gracefully.
    }
    finally
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      channel.Writer.TryComplete();
    }

    return new EmptyResult();
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

    var channel =
      Channel.CreateBounded<(T1 Arg1, T2 Arg2)>(StreamCallbackChannelOptions());
    var acceptingCallbacks = true;

    void eventCallback(T1 arg1, T2 arg2)
    {
      if (Volatile.Read(ref acceptingCallbacks) && !streamToken.IsCancellationRequested)
      {
        channel.Writer.TryWrite((arg1, arg2));
      }
    }
    subscribe(eventCallback);

    using var cancellationRegistration = streamToken.Register(() =>
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      channel.Writer.TryComplete();
    });

    try
    {
      await foreach (var evt in channel.Reader.ReadAllAsync(streamToken))
      {
        await onEvent(evt.Arg1, evt.Arg2);
      }
    }
    catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
    {
      // Stream cancelled gracefully.
    }
    finally
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      channel.Writer.TryComplete();
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
    var streamToken = linkedCts.Token;

    var channel = Channel.CreateBounded<(object? Sender, TEventArgs Args)>(
      StreamCallbackChannelOptions());
    var acceptingCallbacks = true;

    void eventCallback(object? sender, TEventArgs args)
    {
      if (Volatile.Read(ref acceptingCallbacks) && !streamToken.IsCancellationRequested)
      {
        channel.Writer.TryWrite((sender, args));
      }
    }
    subscribe(eventCallback);

    using var cancellationRegistration = streamToken.Register(() =>
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      channel.Writer.TryComplete();
    });

    try
    {
      await foreach (var evt in channel.Reader.ReadAllAsync(streamToken))
      {
        await onEvent(evt.Sender, evt.Args);
      }
    }
    catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
    {
      // Stream cancelled gracefully.
    }
    finally
    {
      Volatile.Write(ref acceptingCallbacks, false);
      unsubscribe(eventCallback);
      channel.Writer.TryComplete();
    }

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
