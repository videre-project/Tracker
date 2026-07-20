/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Database.Models.Collection;

public sealed class AccountModel
{
  public int Id { get; set; }
  public required string Username { get; set; }

  public ICollection<CardGroupingModel> CardGroupings { get; set; } = [];
}
