/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Trade;


namespace Tracker.Services.MTGO.Collection;

public sealed record CollectionStateSnapshot(
  int AccountId,
  long Version,
  DateTime ObservedAt,
  TransactionCorrelation? Correlation,
  IReadOnlyDictionary<int, int> Changes,
  IReadOnlyDictionary<int, int> Quantities);

public interface ICollectionStateFeed
{
  CollectionStateSnapshot? Current { get; }

  Task<CollectionStateSnapshot?> WaitForCurrentStateAsync(
    int accountId,
    TimeSpan maximumDelay,
    CancellationToken cancellationToken);

  Task<CollectionStateSnapshot?> WaitForEscrowStateAsync(
    Guid escrowToken,
    long afterVersion,
    TimeSpan maximumDelay,
    CancellationToken cancellationToken);
}

public sealed class CollectionStateFeed : ICollectionStateFeed
{
  private const int MaximumEscrowStates = 256;

  private readonly object _gate = new();
  private readonly Dictionary<Guid, CollectionStateSnapshot> _escrowStates = [];
  private CollectionStateSnapshot? _current;
  private TaskCompletionSource _changed = NewSignal();
  private long _version;

  public CollectionStateSnapshot? Current
  {
    get
    {
      lock (_gate)
        return _current;
    }
  }

  public void Publish(
    int accountId,
    DateTime observedAt,
    TransactionCorrelation? correlation,
    IReadOnlyDictionary<int, int> changes,
    IReadOnlyDictionary<int, int> quantities)
  {
    TaskCompletionSource changed;
    lock (_gate)
    {
      _current = new CollectionStateSnapshot(
        accountId,
        ++_version,
        EnsureUtc(observedAt),
        correlation,
        new Dictionary<int, int>(changes),
        new Dictionary<int, int>(quantities));
      if (correlation?.EscrowToken is Guid escrowToken)
      {
        if (!_escrowStates.ContainsKey(escrowToken) &&
            _escrowStates.Count >= MaximumEscrowStates)
        {
          KeyValuePair<Guid, CollectionStateSnapshot> oldest =
            _escrowStates.Aggregate((left, right) =>
              left.Value.Version <= right.Value.Version ? left : right);
          _escrowStates.Remove(oldest.Key);
        }
        _escrowStates[escrowToken] = _current;
      }
      changed = _changed;
      _changed = NewSignal();
    }
    changed.TrySetResult();
  }

  public void Reset()
  {
    TaskCompletionSource changed;
    lock (_gate)
    {
      _current = null;
      _escrowStates.Clear();
      changed = _changed;
      _changed = NewSignal();
    }
    changed.TrySetResult();
  }

  public async Task<CollectionStateSnapshot?> WaitForCurrentStateAsync(
    int accountId,
    TimeSpan maximumDelay,
    CancellationToken cancellationToken)
  {
    DateTime deadline = DateTime.UtcNow + maximumDelay;
    while (true)
    {
      Task changed;
      lock (_gate)
      {
        if (_current?.AccountId == accountId)
          return _current;
        changed = _changed.Task;
      }

      TimeSpan remaining = deadline - DateTime.UtcNow;
      if (remaining <= TimeSpan.Zero)
        return null;

      Task signaled = await Task.WhenAny(
        changed,
        Task.Delay(remaining, cancellationToken));
      if (signaled != changed)
        return null;
    }
  }

  public async Task<CollectionStateSnapshot?> WaitForEscrowStateAsync(
    Guid escrowToken,
    long afterVersion,
    TimeSpan maximumDelay,
    CancellationToken cancellationToken)
  {
    DateTime deadline = DateTime.UtcNow + maximumDelay;
    while (true)
    {
      Task changed;
      lock (_gate)
      {
        if (_escrowStates.TryGetValue(
              escrowToken,
              out CollectionStateSnapshot? snapshot) &&
            snapshot.Version > afterVersion)
        {
          _escrowStates.Remove(escrowToken);
          return snapshot;
        }
        changed = _changed.Task;
      }

      TimeSpan remaining = deadline - DateTime.UtcNow;
      if (remaining <= TimeSpan.Zero)
        return null;

      Task signaled = await Task.WhenAny(
        changed,
        Task.Delay(remaining, cancellationToken));
      if (signaled != changed)
        return null;
    }
  }

  private static TaskCompletionSource NewSignal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  private static DateTime EnsureUtc(DateTime value) => value.Kind switch
  {
    DateTimeKind.Utc => value,
    DateTimeKind.Local => value.ToUniversalTime(),
    _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
  };
}
