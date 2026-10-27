/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading;
using System.Threading.Tasks;

using MTGOTracker.Services.MTGO.Collection;

namespace MTGOTracker.Services.MTGO;

/// <summary>
/// Dispatches Tracker-owned commands that may have an interactive MTGO effect.
/// </summary>
public interface IClientCommandGateway
{
  Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Resolves the original queued list to its registered MTGO identity.
  /// </summary>
  CardGroupingState? FindDeck(CardGroupingState deck);

  Task<CardGroupingState> ImportDeckAsync(
    CardGroupingState deck,
    CancellationToken cancellationToken = default);

  Task UpdateDeckAsync(
    CardGroupingState deck,
    CancellationToken cancellationToken = default);

  Task DeleteDeckAsync(
    int netDeckId,
    CancellationToken cancellationToken = default);
}
