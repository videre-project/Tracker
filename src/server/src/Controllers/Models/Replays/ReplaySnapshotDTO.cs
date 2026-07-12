/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Tracker.Services.MTGO.Events;


namespace Tracker.Controllers.Models.Replays;

public class ReplaySnapshotDTO
{
  public int Index { get; set; }
  public int Nonce { get; set; }
  public DateTime Timestamp { get; set; }
  public int TurnNumber { get; set; }
  public required string CurrentPhase { get; set; }
  public int PromptedPlayer { get; set; }
  public required string PromptText { get; set; }
  /// <summary>
  /// JSON array of available prompt actions, e.g. [{"type":"ChooseOption","name":"OK"}].
  /// </summary>
  public string? PromptOptions { get; set; }
  public List<ZoneTransferData> ZoneTransfers { get; set; } = new();
  public List<CardChangeData> CardChanges { get; set; } = new();
  public List<PlayerChangeData> PlayerChanges { get; set; } = new();
  public List<ReplayActionDTO> Actions { get; set; } = new();
  public List<ReplayLogDTO> Logs { get; set; } = new();
}
