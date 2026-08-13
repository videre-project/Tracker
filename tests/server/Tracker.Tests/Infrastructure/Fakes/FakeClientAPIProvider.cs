/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API;

using Tracker.Services.MTGO;


namespace Tracker.Tests.Fakes;

internal sealed class FakeClientAPIProvider : IClientAPIProvider
{
  private readonly ManualResetEventSlim _readyEvent = new(false);

  public Client Client { get; set; } = null!;
  public ClientOptions Options { get; set; }
  public ushort? Pid { get; set; }
  public bool IsReady { get; private set; }
  public UserIdentity? CurrentUser { get; private set; }
  public ManualResetEventSlim ReadyEvent => _readyEvent;

  public event EventHandler? ClientStateChanged;

  public void SetReady(
    UserIdentity identity,
    ushort? processId = 1234)
  {
    CurrentUser = identity;
    Pid = processId;
    IsReady = true;
    _readyEvent.Set();
    ClientStateChanged?.Invoke(this, EventArgs.Empty);
  }

  public void SetDisconnected()
  {
    CurrentUser = null;
    IsReady = false;
    _readyEvent.Reset();
    ClientStateChanged?.Invoke(this, EventArgs.Empty);
  }

  public void CheckAndUpdateReadyState()
  {
  }

  public Task WaitForRemoteClientAsync(
    ClientOptions? options = null,
    CancellationToken cancellationToken = default) =>
    Task.CompletedTask;

  public Task WaitSemaphoreAsync(
    CancellationToken cancellationToken = default) =>
    Task.CompletedTask;

  public Task WaitForClientReadyAsync(
    CancellationToken cancellationToken = default) =>
    IsReady ? Task.CompletedTask : Task.Delay(Timeout.Infinite, cancellationToken);

  public Task WaitForClientDisconnectAsync(
    CancellationToken cancellationToken = default) =>
    IsReady ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;
}
