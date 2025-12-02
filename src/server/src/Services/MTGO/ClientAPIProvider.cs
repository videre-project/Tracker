/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using MTGOSDK.API;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;


namespace Tracker.Services.MTGO;

public class ClientAPIProvider : IClientAPIProvider
{
  public Client Client { get; set; } = null!;
  public ClientOptions Options { get; set; } = default;
  public ushort? Pid { get; set; } = null;
  public bool IsReady { get; private set; } = false;

  private static readonly SemaphoreSlim _semaphore = new(1, 1);
  private TaskCompletionSource<bool> _semaphoreReleased =
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  /// <summary>
  /// Event raised when the client connection state changes
  /// </summary>
  public event EventHandler? ClientStateChanged;

  public ManualResetEventSlim ReadyEvent { get; } = new(false);

  public ClientAPIProvider()
  {
  }

  private void InitializeClient(ClientOptions options, Process? process = null)
  {
    this.Client = new(options, process: process);
    this.Pid = RemoteClient.Port;
    this.Options = options;
  }

  /// <summary>
  /// Check if the client is already initialized and update IsReady accordingly
  /// </summary>
  public void CheckAndUpdateReadyState()
  {
    if (RemoteClient.IsInitialized && !RemoteClient.IsDisposed)
    {
      if (!IsReady)
      {
        IsReady = true;
        OnClientStateChanged();
      }
    }
    else if (IsReady)
    {
      IsReady = false;
      OnClientStateChanged();
    }
  }

  public async Task RunClientLoopAsync(
    ClientOptions options,
    CancellationToken cancellationToken = default)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      try
      {
        // Mark as not ready during initialization
        if (IsReady)
        {
          IsReady = false;
          OnClientStateChanged();
        }
        await WaitForRemoteClientAsync(options, cancellationToken);

        // Wait for the client to be disposed
        var disposedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => disposedTcs.TrySetResult(true));

        void OnRemoteDisposed(object? _, EventArgs __) => disposedTcs.TrySetResult(true);
        RemoteClient.Disposed += OnRemoteDisposed;

        // Check if we missed the event or the client failed to initialize
        if (RemoteClient.IsDisposed ||
            !await WaitForClientInitialization(disposedTcs, cancellationToken))
        {
          disposedTcs.TrySetResult(true);
        }

        try
        {
          Log.Debug("RunClientLoopAsync: Waiting for client disposal...");
          await disposedTcs.Task;
          Log.Debug("RunClientLoopAsync: Client disposal detected.");

          if (IsReady)
          {
            IsReady = false;
            OnClientStateChanged();
          }
        }
        finally
        {
          RemoteClient.Disposed -= OnRemoteDisposed;
        }
      }
      catch (OperationCanceledException)
      {
        // Graceful shutdown
        break;
      }
      catch (Exception ex)
      {
        Log.Error("Error in client loop: {ex}", ex);
        // Wait a bit before retrying to avoid tight loops on error
        await Task.Delay(1000, cancellationToken);
      }
    }
  }

  public async Task WaitForRemoteClientAsync(
    ClientOptions? options = null,
    CancellationToken cancellationToken = default)
  {
    await _semaphore.WaitAsync(cancellationToken);
    try
    {
      do
      {
        if (this.Client != null)
        {
          // Wait for the previous client to dispose.
          Log.Information("MTGO process crashed or disconnected, cleaning up previous client...");
          this.Client.Dispose();

          // Use a timeout to prevent hanging if disposal takes too long
          using var disposeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          disposeCts.CancelAfter(TimeSpan.FromSeconds(10));

          try
          {
            await RemoteClient.WaitForDisposeAsync().WaitAsync(disposeCts.Token);
            Log.Information("Previous client cleaned up successfully.");
          }
          catch (OperationCanceledException)
          {
            Log.Warning("Client cleanup timed out after 10 seconds, proceeding to reconnect anyway.");
          }
          catch (Exception ex)
          {
            Log.Error("Error during client cleanup: {ex}", ex.Message);
          }

          this.Pid = null;
        }

        Log.Trace("Waiting for a new MTGO process to start...");
        await Client.WaitForMTGOProcess(TimeSpan.MaxValue);

        // Initialize a new Client instance with the new MTGO process.
        try
        {
          InitializeClient(options.HasValue ? options.Value : this.Options);
          Log.Trace("MTGO client has finished initializing.");
          return;
        }
        catch (Exception ex)
        {
          Log.Warning("Failed to initialize client: {Error}", ex.Message);
          throw;
        }
      }
      while (this.Pid != null && !cancellationToken.IsCancellationRequested);
    }
    finally
    {
      _semaphore.Release();
      _semaphoreReleased.TrySetResult(true);
      _semaphoreReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
  }

  public Task WaitSemaphoreAsync(CancellationToken cancellationToken = default)
  {
    return _semaphoreReleased.Task.WaitAsync(cancellationToken);
  }

  public async Task WaitForClientReadyAsync(CancellationToken cancellationToken = default)
  {
    if (IsReady) return;
    await Task.Run(() => ReadyEvent.Wait(cancellationToken), cancellationToken);
  }

  public async Task WaitForClientDisconnectAsync(CancellationToken cancellationToken = default)
  {
    if (!IsReady) return;

    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

    EventHandler handler = (s, e) =>
    {
      if (!IsReady) tcs.TrySetResult();
    };

    ClientStateChanged += handler;
    try
    {
      // Double check in case it changed while subscribing
      if (!IsReady) return;
      await tcs.Task;
    }
    finally
    {
      ClientStateChanged -= handler;
    }
  }

  public async Task<bool> WaitForClientInitialization(TaskCompletionSource<bool> disposedTcs, CancellationToken cancellationToken)
  {
    try
    {
      Log.Trace("Waiting for the user to log in...");
      var loginTask = this.Client!.WaitForUserLogin(TimeSpan.MaxValue);
      var completedTask = await Task.WhenAny(loginTask, disposedTcs.Task);

      if (completedTask == disposedTcs.Task || disposedTcs.Task.IsCompleted)
      {
        Log.Warning("Client disposed while waiting for user login.");
        // Ensure we remain not ready in this failure path
        if (IsReady)
        {
          IsReady = false;
          OnClientStateChanged();
        }
        return false;
      }

      if (!await loginTask)
      {
        Log.Warning("User login failed or timed out.");
        return false;
      }

      Log.Trace("Waiting for the client to finish initializing...");
      var readyTask = this.Client!.WaitForClientReady();
      completedTask = await Task.WhenAny(readyTask, disposedTcs.Task);

      if (completedTask == disposedTcs.Task || disposedTcs.Task.IsCompleted)
      {
        Log.Warning("Client disposed while waiting for client ready.");
        // Ensure we remain not ready in this failure path
        if (IsReady)
        {
          IsReady = false;
          OnClientStateChanged();
        }
        return false;
      }

      if (!await readyTask)
      {
        Log.Warning("Client initialization failed or timed out.");
        return false;
      }

      // Double check that the client is still alive before marking as ready
      if (!RemoteClient.IsInitialized || RemoteClient.IsDisposed)
      {
        Log.Warning("Client disposed before initialization could complete.");
        return false;
      }

      // Mark as ready and notify listeners
      IsReady = true;
      OnClientStateChanged();
      return true;
    }
    catch (OperationCanceledException)
    {
      Log.Warning("Client initialization canceled.");
      return false;
    }
  }

  protected virtual void OnClientStateChanged()
  {
    ClientStateChanged?.Invoke(this, EventArgs.Empty);
    if (IsReady)
    {
      ReadyEvent.Set();
    }
    else
    {
      ReadyEvent.Reset();
    }
  }
}
