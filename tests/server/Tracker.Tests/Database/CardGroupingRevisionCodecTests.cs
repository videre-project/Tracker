/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Tracker.Database.Models.Collection;
using Tracker.Services.MTGO.Collection;


namespace Tracker.Tests;

[TestFixture]
public sealed class CardGroupingRevisionCodecTests
{
  [Test]
  public void Test_Delta()
  {
    var previous = new CardGroupingState(
      CardGroupingKind.Deck,
      91,
      new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc),
      "Fixture deck",
      "Pauper",
      [
        new CardGroupingItemState(1, 0, 0, 4),
        new CardGroupingItemState(2, 0, 0, 2),
      ]);
    var current = previous with
    {
      Items =
      [
        new CardGroupingItemState(1, 0, 0, 3),
        new CardGroupingItemState(3, 0, 0, 4),
      ],
      Name = "Fixture deck v2",
    };

    var payload = CardGroupingRevisionCodec.SerializeDelta(previous, current);
    var result = CardGroupingRevisionCodec.ApplyDelta(previous, payload);

    Assert.That(CardGroupingRevisionCodec.StateEquals(result, current), Is.True);
  }
}
