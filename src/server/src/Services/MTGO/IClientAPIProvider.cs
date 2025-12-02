/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API;


namespace Tracker.Services.MTGO;

public interface IClientAPIProvider
{
  Client Client { get; set; }
  ClientOptions Options { get; set; }
  ushort? Pid { get; set; }
  bool IsReady { get; }

  event EventHandler? ClientStateChanged;

  ManualResetEventSlim ReadyEvent { get; }

  void CheckAndUpdateReadyState();

  Task WaitForRemoteClientAsync(
    ClientOptions? options = null,
    CancellationToken cancellationToken = default);

  Task WaitSemaphoreAsync(
    CancellationToken cancellationToken = default);

  Task WaitForClientReadyAsync(
    CancellationToken cancellationToken = default);

  Task WaitForClientDisconnectAsync(
    CancellationToken cancellationToken = default);
}
