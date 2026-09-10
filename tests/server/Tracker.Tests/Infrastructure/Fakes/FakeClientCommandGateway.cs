/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Tracker.Services.MTGO;


namespace Tracker.Tests.Fakes;

internal sealed class FakeClientCommandGateway : IClientCommandGateway
{
  private int _nextDeckId = 900_000;

  public int? OpenedEventId { get; private set; }
  public ConcurrentQueue<DeckSyncState> ImportedDecks { get; } = new();
  public ConcurrentQueue<DeckSyncState> UpdatedDecks { get; } = new();
  public ConcurrentQueue<int> DeletedDeckIds { get; } = new();

  public Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default)
  {
    OpenedEventId = eventId;
    return Task.CompletedTask;
  }

  public Task<DeckSyncResult> ImportDeckAsync(
    DeckSyncState deck,
    CancellationToken cancellationToken = default)
  {
    ImportedDecks.Enqueue(deck);
    int id = Interlocked.Increment(ref _nextDeckId);
    return Task.FromResult(new DeckSyncResult(id, deck.Name));
  }

  public Task UpdateDeckAsync(
    DeckSyncState deck,
    CancellationToken cancellationToken = default)
  {
    UpdatedDecks.Enqueue(deck);
    return Task.CompletedTask;
  }

  public Task DeleteDeckAsync(
    int netDeckId,
    CancellationToken cancellationToken = default)
  {
    DeletedDeckIds.Enqueue(netDeckId);
    return Task.CompletedTask;
  }
}
