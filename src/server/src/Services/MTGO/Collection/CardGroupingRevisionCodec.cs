/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MessagePack;

using Tracker.Database.Models.Collection;


namespace Tracker.Services.MTGO.Collection;

public static class CardGroupingRevisionCodec
{
  public const byte PayloadVersion = 1;

  private static readonly MessagePackSerializerOptions s_options =
    MessagePackSerializerOptions.Standard.WithSecurity(
      MessagePackSecurity.UntrustedData);

  public static byte[] SerializeSnapshot(CardGroupingState state)
  {
    state = state.Normalize();
    return MessagePackSerializer.Serialize(
      new CardGroupingSnapshotPayload(
        PayloadVersion,
        state.Metadata,
        state.Items.ToArray()),
      s_options);
  }

  public static byte[] SerializeDelta(
    CardGroupingState previous,
    CardGroupingState current)
  {
    previous = previous.Normalize();
    current = current.Normalize();

    var previousItems = previous.Items.ToDictionary(ItemKey.From);
    var currentItems = current.Items.ToDictionary(ItemKey.From);
    var changes = previousItems.Keys
      .Union(currentItems.Keys)
      .OrderBy(key => key.CatalogId)
      .ThenBy(key => key.Region)
      .ThenBy(key => key.Annotation)
      .Where(key =>
        previousItems.GetValueOrDefault(key)?.Quantity !=
        currentItems.GetValueOrDefault(key)?.Quantity)
      .Select(key => currentItems.TryGetValue(key, out var item)
        ? item
        : new CardGroupingItemState(
            key.CatalogId,
            key.Region,
            key.Annotation,
            0))
      .ToArray();

    return MessagePackSerializer.Serialize(
      new CardGroupingDeltaPayload(
        PayloadVersion,
        BuildMetadataPatch(previous, current),
        changes),
      s_options);
  }

  public static CardGroupingState DeserializeSnapshot(
    CardGroupingKind kind,
    int netDeckId,
    byte[] payload)
  {
    var snapshot = MessagePackSerializer.Deserialize<CardGroupingSnapshotPayload>(
      payload,
      s_options);
    EnsureVersion(snapshot.Version);

    return new CardGroupingState(
      kind,
      netDeckId,
      snapshot.Metadata?.Timestamp,
      snapshot.Metadata?.Name,
      snapshot.Metadata?.FormatCode,
      snapshot.Items).Normalize();
  }

  public static CardGroupingDeltaPayload DeserializeDelta(byte[] payload)
  {
    var delta = MessagePackSerializer.Deserialize<CardGroupingDeltaPayload>(
      payload,
      s_options);
    EnsureVersion(delta.Version);
    return delta;
  }

  public static CardGroupingState ApplyDelta(
    CardGroupingState state,
    byte[] payload)
  {
    CardGroupingDeltaPayload delta = DeserializeDelta(payload);
    var items = state.Items.ToDictionary(ItemKey.From);
    foreach (var change in delta.ItemChanges)
    {
      var key = ItemKey.From(change);
      if (change.Quantity > 0)
        items[key] = change;
      else
        items.Remove(key);
    }

    DateTime? timestamp = state.Timestamp;
    string? name = state.Name;
    string? formatCode = state.FormatCode;
    if (delta.Metadata is { } metadata)
    {
      if (metadata.ChangedFields.HasFlag(CardGroupingMetadataFields.Timestamp))
        timestamp = metadata.Timestamp;
      if (metadata.ChangedFields.HasFlag(CardGroupingMetadataFields.Name))
        name = metadata.Name;
      if (metadata.ChangedFields.HasFlag(CardGroupingMetadataFields.FormatCode))
        formatCode = metadata.FormatCode;
    }

    return (state with
    {
      Timestamp = timestamp,
      Name = name,
      FormatCode = formatCode,
      Items = items.Values.ToArray(),
    }).Normalize();
  }

  public static bool StateEquals(
    CardGroupingState left,
    CardGroupingState right)
  {
    left = left.Normalize();
    right = right.Normalize();

    return left.Kind == right.Kind &&
      left.NetDeckId == right.NetDeckId &&
      string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
      string.Equals(left.FormatCode, right.FormatCode, StringComparison.Ordinal) &&
      left.Items.SequenceEqual(right.Items);
  }

  private static CardGroupingMetadataPatch? BuildMetadataPatch(
    CardGroupingState previous,
    CardGroupingState current)
  {
    if (current.Kind == CardGroupingKind.Collection)
      return null;

    CardGroupingMetadataFields fields = CardGroupingMetadataFields.None;
    if (!Nullable.Equals(previous.Timestamp, current.Timestamp))
      fields |= CardGroupingMetadataFields.Timestamp;
    if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
      fields |= CardGroupingMetadataFields.Name;
    if (!string.Equals(
          previous.FormatCode,
          current.FormatCode,
          StringComparison.Ordinal))
      fields |= CardGroupingMetadataFields.FormatCode;

    return fields == CardGroupingMetadataFields.None
      ? null
      : new(fields, current.Timestamp, current.Name, current.FormatCode);
  }

  private static void EnsureVersion(byte version)
  {
    if (version != PayloadVersion)
      throw new InvalidOperationException(
        $"Unsupported card grouping payload version {version}.");
  }

  private readonly record struct ItemKey(
    int CatalogId,
    int Region,
    uint Annotation)
  {
    public static ItemKey From(CardGroupingItemState item) =>
      new(item.CatalogId, item.Region, item.Annotation);
  }
}
