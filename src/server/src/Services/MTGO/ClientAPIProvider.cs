/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API;
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

  private void InitializeClient(ClientOptions options)
  {
    this.Client = new(options);
    this.Pid = RemoteClient.Port;
    this.Options = options;
  }

  /// <summary>
  /// Check if the client is already initialized and update IsReady accordingly
  /// </summary>
  public void CheckAndUpdateReadyState()
  {
    bool wasReady = IsReady;
    bool clientExists = Client != null;
    bool remoteInitialized = RemoteClient.IsInitialized;
    IsReady = clientExists && remoteInitialized;

    // Fire event if state changed
    if (wasReady != IsReady)
    {
      OnClientStateChanged();
    }
  }

  public async Task WaitForRemoteClientAsync(
    ClientOptions? options = null,
    CancellationToken cancellationToken = default)
  {
    await _semaphore.WaitAsync(cancellationToken);
    try
    {
      // Mark as not ready during initialization
      IsReady = false;
      OnClientStateChanged();

      if (this.Client != null)
      {
        // Wait for the previous client to dispose.
        Log.Information("MTGO process crashed or disconnected, cleaning up previous client...");

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

      // Log the process ID of the new MTGO process.
      int pid = RemoteClient.MTGOProcess()!.Id;
      Log.Trace("Found a new MTGO process with PID {pid}.", pid);

      // Initialize the client (re-using the new RemoteClient instance).
      Log.Trace("Initializing the client instance for PID {pid}...", pid);
      InitializeClient(options.HasValue ? options.Value : this.Options);
      if (pid != (int) this.Pid!)
      {
        throw new InvalidOperationException(
          $"The MTGO process ID {pid} does not match the expected PID {this.Pid}.");
      }

      Log.Trace("Waiting for the user to log in...");
      await this.Client!.WaitForUserLogin(TimeSpan.FromMinutes(5));

      Log.Trace("Waiting for the client to finish initializing...");
      await this.Client!.WaitForClientReady();

      Log.Trace("MTGO client has finished initializing.");

      // Mark as ready and notify listeners
      IsReady = true;
      OnClientStateChanged();
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

  protected virtual void OnClientStateChanged()
  {
    ClientStateChanged?.Invoke(this, EventArgs.Empty);
  }
}
