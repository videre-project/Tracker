/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Database.Models;
using MTGOTracker.Database.Models.Collection;
using MTGOTracker.Services.MTGO.Collection;

namespace MTGOTracker.Controllers;

public partial class DecksController
{
  /// <summary>
  /// Saved plans and notes across revisions of the selected deck.
  /// </summary>
  [HttpGet("{revisionId:long}/sideboard-guides")]
  public async Task<ActionResult<IReadOnlyList<SideboardGuideData>>> GetSideboardGuides(
    long revisionId, string? opponent, CancellationToken cancellationToken)
  {
    if (clientProvider.CurrentUser is not { } account) return BadRequest("Client not ready.");
    long groupingId = await collection.CardGroupingRevisions.AsNoTracking()
      .Where(revision => revision.Id == revisionId && revision.CardGrouping.AccountId == account.Id &&
        revision.CardGrouping.Kind == CardGroupingKind.Deck)
      .Select(revision => revision.CardGroupingId).SingleOrDefaultAsync(cancellationToken);
    if (groupingId == 0) return NotFound();
    var revisions = collection.CardGroupingRevisions.Where(revision => revision.CardGroupingId == groupingId).Select(revision => revision.Id);
    var query = collection.SideboardGuides.AsNoTracking().Where(guide => guide.AccountId == account.Id && revisions.Contains(guide.DeckRevisionId));
    if (opponent != null) query = query.Where(guide => guide.OpponentArchetype == opponent.Trim());
    var guides = await query.Include(guide => guide.Cards).OrderByDescending(guide => guide.UpdatedAt).ToListAsync(cancellationToken);
    var decks = await deckService.GetRevisionsAsync(guides.Select(guide => guide.DeckRevisionId), cancellationToken);
    return Ok(guides.Where(guide => decks.ContainsKey(guide.DeckRevisionId)).Select(guide => ToGuide(guide, decks[guide.DeckRevisionId])).ToArray());
  }

  private static SideboardGuideData ToGuide(SideboardGuideModel guide, DeckRevisionView deck)
  {
    var catalog = deck.Mainboard.Concat(deck.Sideboard).GroupBy(card => card.catalogId)
      .ToDictionary(group => group.Key, group => group.First());
    List<CardEntry> Cards(bool bringIn) => guide.Cards.Where(card => card.BringIn == bringIn)
      .Select(card => catalog[card.CatalogId] with { quantity = card.Quantity })
      .OrderBy(card => card.name).ToList();
    return new(guide.Id, new(deck.CardGroupingId, deck.RevisionId, deck.Name, deck.Format,
      deck.Archetype, deck.Timestamp, deck.Colors, deck.FeaturedCards), guide.OpponentArchetype, guide.Notes,
      guide.UpdatedAt, Cards(true), Cards(false));
  }

}
