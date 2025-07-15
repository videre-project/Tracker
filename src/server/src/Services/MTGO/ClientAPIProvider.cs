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

  private static readonly SemaphoreSlim _semaphore = new(1, 1);

  private void InitializeClient(ClientOptions options)
  {
    this.Client = new(options);
    this.Pid = RemoteClient.Port;
    this.Options = options;
  }

  public async Task WaitForRemoteClientAsync(
    ClientOptions? options = null,
    CancellationToken cancellationToken = default)
  {
    await _semaphore.WaitAsync(cancellationToken);
    try
    {
      if (this.Client != null)
      {
        // Wait for the previous client to dispose.
        Log.Trace("Waiting for previous client to dispose...");
        await RemoteClient.WaitForDisposeAsync();
      }

      Log.Trace("Waiting for a new MTGO process to start...");
      await Client.WaitForMTGOProcess(TimeSpan.MaxValue);

      // Log the process ID of the new MTGO process.
      int pid = RemoteClient.MTGOProcess()!.Id;
      Log.Trace("Found a new MTGO process with PID {pid}.", pid);

      Log.Trace("Waiting for the user to log in...");
      await Client.WaitForUserLogin(TimeSpan.FromMinutes(5));

      // Initialize the client (re-using the new RemoteClient instance).
      Log.Trace("User is logged in, initializing the client instance...");
      InitializeClient(options.HasValue ? options.Value : this.Options);
      if (pid != (int) this.Pid!)
      {
        throw new InvalidOperationException(
          $"The MTGO process ID {pid} does not match the expected PID {this.Pid}.");
      }

      Log.Trace("Waiting for the client to finish initializing...");
      await this.Client!.WaitForClientReady();

      Log.Trace("MTGO client has finished initializing.");
    }
    finally
    {
      _semaphore.Release();
    }
  }

  public Task WaitSemaphoreAsync(
    CancellationToken cancellationToken = default)
  {
    return _semaphore.WaitAsync(cancellationToken);
  }

  public void ReleaseSemaphore()
  {
    _semaphore.Release();
  }
}
