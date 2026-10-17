/* @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
*/

using MTGOSDK.API;
using MTGOSDK.API.Play.Games;


namespace Tracker.Plugins;

/// <summary>
/// Provides access to Tracker's ready SDK client and initialized games.
/// </summary>
/// <remarks>
/// These objects and their processors belong to the host; consumers must not
/// dispose them or clear their events. <see cref="Games"/> contains the same
/// wrapper instances used by recording.
/// </remarks>
public interface IMTGOContext
{
  /// <summary>
  /// The host's ready SDK client, or null while disconnected.
  /// </summary>
  Client? Client { get; }

  /// <summary>
  /// The ready session's user ID, read from Tracker's cached identity.
  /// </summary>
  int? UserId { get; }

  /// <summary>
  /// The host's games, with their recording processors initialized.
  /// </summary>
  IReadOnlyList<Game> Games { get; }

  /// <summary>
  /// Occurs when connection readiness or the game list changes.
  /// </summary>
  /// <remarks>
  /// Subscribe before reading <see cref="Games"/> to include games already open
  /// at plugin startup. Handlers should return promptly; unsubscribe during
  /// service disposal. Notifications run synchronously on host worker threads
  /// and may overlap. Serialize access to plugin state; marshal UI work to its
  /// owning thread. Read Games again to reconcile changes, including removals
  /// after a game finishes or the session disconnects.
  /// </remarks>
  event EventHandler? Changed;
}
