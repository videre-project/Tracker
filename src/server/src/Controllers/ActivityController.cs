/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MTGOTracker.Controllers.Base;
using MTGOTracker.Controllers.Models.Events;
using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Database;
using MTGOTracker.Database.Models.Collection;
using MTGOTracker.Services.MTGO;
using MTGOTracker.Services.MTGO.Collection;
using MTGOTracker.Services.MTGO.Events;

namespace MTGOTracker.Controllers;

[ApiController]
[Route("api/activity")]
public sealed class ActivityController(
  ClientStateMonitor clientMonitor,
  IClientAPIProvider clientProvider,
  EventContext events,
  CollectionContext collection,
  CollectionDeckService decks) : APIController
{
  /// <summary>
  /// Your joined events, league courses, and active matches.
  /// </summary>
  [HttpGet]
  [ProducesResponseType(typeof(PersonalActivityDTO[]), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<IActionResult> Get(CancellationToken cancellationToken)
  {
    if (!clientMonitor.IsClientReady || clientProvider.CurrentUser is not { } user)
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Connect MTGO to view your activity." });
    return Ok(await ReadAsync(user, cancellationToken));
  }

  /// <summary>
  /// Full personal activity snapshots; removals and registration changes
  /// replace the previous snapshot.
  /// </summary>
  [HttpGet("watch")]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> Watch()
  {
    if (!clientMonitor.IsClientReady || clientProvider.CurrentUser is not { } user)
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Connect MTGO to view your activity." });
    using var cancellation = BeginNDJSONStream(clientMonitor.Token);
    var token = cancellation.Token;
    string? previous = null;
    try
    {
      while (!token.IsCancellationRequested && clientProvider.CurrentUser == user)
      {
        var snapshot = await ReadAsync(user, token);
        string fingerprint = JsonSerializer.Serialize(snapshot);
        if (fingerprint != previous)
        {
          await StreamResponse(new[] { snapshot }, token);
          previous = fingerprint;
        }
        // The personal collection is small; polling also covers queue and league
        // participant changes that do not produce public tournament updates.
        await Task.Delay(TimeSpan.FromSeconds(5), token);
      }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    return new EmptyResult();
  }

  private async Task<PersonalActivityDTO[]> ReadAsync(UserIdentity user, CancellationToken token)
  {
    var snapshot = PersonalActivity.Read(user.Username);
    if (snapshot.Length == 0) return snapshot;
    var eventIds = snapshot.Select(item => item.CourseId ?? item.EventId).ToArray();
    var matchIds = snapshot.Where(item => item.Match != null).Select(item => item.Match!.Id).ToArray();
    var eventRevisions = await events.Events.AsNoTracking()
      .Where(item => eventIds.Contains(item.Id) && item.DeckRevisionId != null)
      .ToDictionaryAsync(item => item.Id, item => item.DeckRevisionId!.Value, token);
    var matchRevisions = await events.Matches.AsNoTracking()
      .Where(match => matchIds.Contains(match.Id) && match.Event.DeckRevisionId != null)
      .ToDictionaryAsync(match => match.Id, match => match.Event.DeckRevisionId!.Value, token);

    // Unstarted registrations may not have a recorded event yet. Resolve their
    // selected deck by native ID, never by a potentially duplicated deck name.
    var nativeIds = snapshot.Where(item => item.NetDeckId.HasValue).Select(item => item.NetDeckId!.Value).ToArray();
    var currentRevisions = await collection.CardGroupings.AsNoTracking()
      .Where(group => group.AccountId == user.Id && group.Kind == CardGroupingKind.Deck &&
        !group.IsDeleted && nativeIds.Contains(group.NetDeckId))
      .Select(group => new { group.NetDeckId, RevisionId = group.Revisions.Select(revision => (long?)revision.Id).Max() })
      .ToDictionaryAsync(group => group.NetDeckId, group => group.RevisionId, token);
    long? GetRevisionId(PersonalActivityDTO item)
    {
      if (item.Match is { } match && matchRevisions.TryGetValue(match.Id, out long matchRevision))
        return matchRevision;
      if (eventRevisions.TryGetValue(item.CourseId ?? item.EventId, out long eventRevision))
        return eventRevision;
      if (item.NetDeckId is int nativeId)
        return currentRevisions.GetValueOrDefault(nativeId);
      return null;
    }

    var activityRevisions = snapshot.Select(item => (Activity: item, RevisionId: GetRevisionId(item))).ToArray();
    var revisions = await decks.GetRevisionsAsync(activityRevisions.Select(item => item.RevisionId)
      .Where(id => id.HasValue).Select(id => id!.Value), token);
    return activityRevisions.Select(item =>
    {
      if (item.RevisionId is not long id || !revisions.TryGetValue(id, out var deck) || deck.AccountId != user.Id)
        return item.Activity;
      return item.Activity with {
        Deck = new DeckReferenceData(deck.CardGroupingId, deck.RevisionId, deck.Name, deck.Format,
          deck.Archetype, deck.Timestamp, deck.Colors, deck.FeaturedCards)
      };
    }).ToArray();
  }
}
