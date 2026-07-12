/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Events;

public interface IEventStructure
{
  string Name { get; }

  bool IsConstructed { get; }
  bool IsLimited { get; }
  bool IsDraft { get; }
  bool IsSealed { get; }
  bool IsSingleElimination { get; }
  bool IsSwiss { get; }
  bool HasPlayoffs { get; }
}
