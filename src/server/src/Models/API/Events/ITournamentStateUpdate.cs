/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Events;

public interface ITournamentStateUpdate : ITournamentStateCore
{
  IEnumerable<string> ActivePlayerNames { get; }
  IEnumerable<string> PlayerNamesWithMatchesInProgress { get; }
}
