/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace MTGOTracker.Services.MTGO;

public sealed record DeckSyncCard(
  int CatalogId,
  int Region,
  uint Annotation,
  int Quantity);

public sealed record DeckSyncState(
  int NetDeckId,
  string Name,
  string? FormatCode,
  IReadOnlyList<DeckSyncCard> Items);

public sealed record DeckSyncResult(int NetDeckId, string Name);

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

  Task<DeckSyncResult> ImportDeckAsync(
    DeckSyncState deck,
    CancellationToken cancellationToken = default);

  Task UpdateDeckAsync(
    DeckSyncState deck,
    CancellationToken cancellationToken = default);

  Task DeleteDeckAsync(
    int netDeckId,
    CancellationToken cancellationToken = default);
}
