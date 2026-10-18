/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace MTGOTracker.Controllers.Models.Games;

public class PaginatedMatchesDTO
{
  public List<MatchHistoryDTO> Items { get; set; } = new();
  /// <summary>
  /// Total events when grouping is enabled; otherwise, total matches.
  /// </summary>
  public int TotalCount { get; set; }
  public int Page { get; set; }
  public int PageSize { get; set; }
  public int TotalPages { get; set; }
}
