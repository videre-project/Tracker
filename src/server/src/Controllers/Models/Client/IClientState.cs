/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Controllers.Models.Client;

public interface IClientState
{
  bool IsConnected { get; }
  bool IsInitialized { get; }
  ushort? ProcessId { get; }
  string Status { get; }
  long? MemoryUsage { get; }
  long? WorkingSet { get; }
  long? VirtualMemory { get; }
}
