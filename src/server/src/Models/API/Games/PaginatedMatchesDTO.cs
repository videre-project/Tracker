/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Games;

public class PaginatedMatchesDTO
{
  public List<MatchHistoryDTO> Items { get; set; } = new();
  public int TotalCount { get; set; }
  public int Page { get; set; }
  public int PageSize { get; set; }
  public int TotalPages { get; set; }
}
