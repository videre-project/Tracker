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

[MessagePackObject]
public sealed record CardGroupingItemState(
  [property: Key(0)] int CatalogId,
  [property: Key(1)] int Region,
  [property: Key(2)] uint Annotation,
  [property: Key(3)] int Quantity);

[MessagePackObject]
public sealed record CardGroupingMetadataState(
  [property: Key(0)] DateTime? Timestamp,
  [property: Key(1)] string? Name,
  [property: Key(2)] string? FormatCode);

[Flags]
public enum CardGroupingMetadataFields : byte
{
  None = 0,
  Timestamp = 1,
  Name = 2,
  FormatCode = 4,
}

[MessagePackObject]
public sealed record CardGroupingMetadataPatch(
  [property: Key(0)] CardGroupingMetadataFields ChangedFields,
  [property: Key(1)] DateTime? Timestamp,
  [property: Key(2)] string? Name,
  [property: Key(3)] string? FormatCode);

[MessagePackObject]
public sealed record CardGroupingSnapshotPayload(
  [property: Key(0)] byte Version,
  [property: Key(1)] CardGroupingMetadataState? Metadata,
  [property: Key(2)] CardGroupingItemState[] Items);

[MessagePackObject]
public sealed record CardGroupingDeltaPayload(
  [property: Key(0)] byte Version,
  [property: Key(1)] CardGroupingMetadataPatch? Metadata,
  [property: Key(2)] CardGroupingItemState[] ItemChanges);

public sealed record CardGroupingState(
  CardGroupingKind Kind,
  int NetDeckId,
  DateTime? Timestamp,
  string? Name,
  string? FormatCode,
  IReadOnlyList<CardGroupingItemState> Items)
{
  public CardGroupingMetadataState? Metadata =>
    Kind == CardGroupingKind.Collection
      ? null
      : new(Timestamp, Name, FormatCode);

  public CardGroupingState Normalize()
  {
    var items = Items
      .GroupBy(item => new
      {
        item.CatalogId,
        item.Region,
        item.Annotation,
      })
      .Select(group => new CardGroupingItemState(
        group.Key.CatalogId,
        group.Key.Region,
        group.Key.Annotation,
        group.Sum(item => item.Quantity)))
      .Where(item => item.CatalogId > 0 && item.Quantity > 0)
      .OrderBy(item => item.CatalogId)
      .ThenBy(item => item.Region)
      .ThenBy(item => item.Annotation)
      .ToArray();

    return Kind == CardGroupingKind.Collection
      ? this with
      {
        Timestamp = null,
        Name = null,
        FormatCode = null,
        Items = items,
      }
      : this with { Items = items };
  }
}
