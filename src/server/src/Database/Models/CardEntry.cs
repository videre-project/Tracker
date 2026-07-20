/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json.Serialization;

using MTGOSDK.API.Collection;


namespace Tracker.Database.Models;

[method: JsonConstructor]
public sealed record CardEntry(int catalogId, string name, int quantity)
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
