/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading;
using System.Threading.Tasks;


namespace Tracker.Services.MTGO;

/// <summary>
/// Dispatches Tracker-owned commands that may have an interactive MTGO effect.
/// </summary>
/// <remarks>
/// The gateway intentionally describes Tracker intent rather than MTGO UI
/// automation. The production implementation can use MTGOSDK capabilities
/// when they are available; tests can record commands without starting MTGO.
/// </remarks>
public interface IClientCommandGateway
{
  Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe default for commands that are not implemented by the current client.
/// </summary>
public sealed class NoOpClientCommandGateway : IClientCommandGateway
{
  public Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
}
