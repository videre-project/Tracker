/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Text.Json.Serialization;

using MTGOSDK.API.Collection;


namespace Tracker.Database.Models;

[method: JsonConstructor]
public sealed record CardEntry(
  int catalogId,
  string name,
  int quantity,
  int cmc = 0,
  List<string>? colors = null,
  List<string>? types = null,
  string rarity = "common")
{
  public CardEntry(int catalogId, int quantity)
    : this(catalogId, ResolveName(catalogId), quantity)
  { }

  private static string ResolveName(int catalogId)
  {
    try
    {
      return CollectionManager.GetCard(catalogId).Name;
    }
    catch
    {
      return catalogId.ToString();
    }
  }
}
