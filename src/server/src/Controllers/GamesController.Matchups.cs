/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MTGOSDK.API.Play;
using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Controllers.Models.Games;
using MTGOTracker.Database.Models.Events;
using MTGOTracker.Services.MTGO.Collection;
using static MTGOTracker.Services.MTGO.Events.MatchHistorySerialization;

namespace MTGOTracker.Controllers;

public partial class GamesController
{
  /// <summary>
  /// Matchup records grouped by saved deck or by archetype within a format.
  /// </summary>
  [HttpGet("matchups")]
  public async Task<ActionResult<MatchupOverviewData>> GetMatchups(
    DateTime? minDate, DateTime? maxDate, string? format, CancellationToken cancellationToken,
    string groupBy = "deck", string? gameType = null)
  {
    if (clientProvider.CurrentUser is not { } account) return BadRequest("Client not ready.");
    if (groupBy is not ("deck" or "archetype")) return BadRequest("Group by deck or archetype.");
    if (!DeckFormatResolver.TryGetGameType(gameType, clientProvider.Pid, out var formatType, out var legacyFormats))
      return BadRequest("Choose all, constructed, or limited games.");
    var matches = await context.MatchesForPlayer(account.Username, minDate, maxDate, format, completedOnly: true,
        formatType: formatType, legacyFormats: legacyFormats)
      .Where(match => match.Event.DeckRevisionId.HasValue)
      .Include(match => match.Games).ThenInclude(game => game.Players)
      .AsSplitQuery().ToListAsync(cancellationToken);
    var (revisions, decks) = await ReadMatchupDecksAsync(
      matches.Select(match => match.Event.DeckRevisionId!.Value), account.Id, format, cancellationToken);
    if (formatType.HasValue)
    {
      var playedDecks = revisions.Values.ToHashSet();
      decks = decks.Where(pair => playedDecks.Contains(pair.Key) || legacyFormats.Contains(pair.Value.Format, StringComparer.OrdinalIgnoreCase))
        .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
    matches = matches.Where(match => revisions.TryGetValue(match.Event.DeckRevisionId!.Value, out long deckId) && decks.ContainsKey(deckId)).ToList();
    var groups = decks.Values.GroupBy(deck => groupBy == "archetype" && !string.IsNullOrWhiteSpace(deck.Archetype)
      ? $"archetype:{deck.Format.ToUpperInvariant()}:{deck.Archetype.Trim().ToUpperInvariant()}"
      : $"deck:{deck.CardGroupingId}")
      .Select(group => new {
        Deck = groupBy == "archetype" && !string.IsNullOrWhiteSpace(group.First().Archetype)
          ? ToArchetype(group) : ToDeck(group.First()),
        Ids = group.Select(deck => deck.CardGroupingId)
      }).ToList();
    var groupDecks = groups.SelectMany(group => group.Ids.Select(id => (Id: id, group.Deck)))
      .ToDictionary(item => item.Id, item => item.Deck);
    await ReadPlayDrawAsync(context, matches.SelectMany(match => match.Games), account.Username, cancellationToken);
    var matchups = matches.GroupBy(match => groupDecks[revisions[match.Event.DeckRevisionId!.Value]])
      .SelectMany(deck => deck.GroupBy(match => match.OpponentDeckArchetype?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .Select(opponent => SummarizeMatchup(deck.Key, opponent.Key, opponent, account.Username)))
      .OrderByDescending(row => row.Matches.Total).ThenBy(row => row.Deck.Name).ThenBy(row => row.OpponentArchetype).ToList();
    return Ok(new MatchupOverviewData(groups.Select(group => group.Deck).OrderBy(deck => deck.Name).ToList(), matchups));
  }

  private async Task<(IReadOnlyDictionary<long, long> Revisions, Dictionary<long, DeckRevisionView> Decks)> ReadMatchupDecksAsync(
    IEnumerable<long> revisionIds, int accountId, string? format, CancellationToken cancellationToken)
  {
    var revisions = await deckService.GetCardGroupingIdsAsync(revisionIds, cancellationToken);
    var current = await deckService.GetCurrentDecksAsync(cancellationToken);
    // Results need revision IDs, not every historical card list. Materialize only
    // the latest played revision for decks no longer in the current collection.
    var currentIds = current.Select(deck => deck.CardGroupingId).ToHashSet();
    var archived = await deckService.GetRevisionsAsync(revisions.Where(pair => !currentIds.Contains(pair.Value))
      .GroupBy(pair => pair.Value).Select(group => group.Max(pair => pair.Key)), cancellationToken);
    var decks = archived.Values.Concat(current).Where(deck => deck.AccountId == accountId &&
        (string.IsNullOrWhiteSpace(format) || deck.Format == format))
      .GroupBy(deck => deck.CardGroupingId).ToDictionary(group => group.Key, group => group.MaxBy(deck => deck.RevisionId)!);
    return (revisions, decks);
  }

  // Use the same current/archived deck classification as the overview, including
  // older revisions whose archetype may have changed since the match was played.
  private async Task<(DeckReferenceData? Deck, long[] Revisions)> ReadArchetypeScopeAsync(
    IQueryable<MatchModel> query, string archetype, string format, int accountId, CancellationToken cancellationToken)
  {
    var revisionIds = await query.Where(match => match.Event.DeckRevisionId.HasValue)
      .Select(match => match.Event.DeckRevisionId!.Value).Distinct().ToListAsync(cancellationToken);
    var (revisions, decks) = await ReadMatchupDecksAsync(revisionIds, accountId, format, cancellationToken);
    var members = decks.Values.Where(deck => string.Equals(deck.Archetype?.Trim(), archetype.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    var ids = members.Select(deck => deck.CardGroupingId).ToHashSet();
    return (members.Count == 0 ? null : ToArchetype(members),
      revisions.Where(pair => ids.Contains(pair.Value)).Select(pair => pair.Key).ToArray());
  }

  private static MatchupData SummarizeMatchup(
    DeckReferenceData deck, string opponent, IEnumerable<MatchModel> source, string username)
  {
    var matches = source.ToList();
    var results = matches.Select(match => match.PlayerResults.First(result => string.Equals(result.Player, username, StringComparison.OrdinalIgnoreCase)).Result).ToList();
    var games = matches.SelectMany(match => match.Games.OrderBy(game => game.Id)
      .Select((game, index) => (Game: game, Number: index + 1, PlayDraw: ResolvePlayDraw(game, username)))).ToList();
    return new(deck, opponent,
      new(results.Count(result => result == MatchResult.Win),
        results.Count(result => result == MatchResult.Loss),
        results.Count(result => result == MatchResult.Draw)),
      GameRecord(games.Select(item => item.Game), username),
      GameRecord(games.Where(item => item.Number == 1).Select(item => item.Game), username),
      GameRecord(games.Where(item => item.Number > 1).Select(item => item.Game), username),
      GameRecord(games.Where(item => item.PlayDraw == "Play").Select(item => item.Game), username),
      GameRecord(games.Where(item => item.PlayDraw == "Draw").Select(item => item.Game), username),
      GameRecord(games.Where(item => item.Number == 1 && item.PlayDraw == "Play").Select(item => item.Game), username),
      GameRecord(games.Where(item => item.Number == 1 && item.PlayDraw == "Draw").Select(item => item.Game), username),
      GameRecord(games.Where(item => item.Number > 1 && item.PlayDraw == "Play").Select(item => item.Game), username),
      GameRecord(games.Where(item => item.Number > 1 && item.PlayDraw == "Draw").Select(item => item.Game), username),
      matches.OrderByDescending(match => match.Event.StartTime)
        .Select(match => match.OpponentDeckColors).FirstOrDefault(colors => colors != null));
  }

  private static DeckReferenceData ToDeck(DeckRevisionView deck) => new(
    deck.CardGroupingId, deck.RevisionId, deck.Name, deck.Format, deck.Archetype, deck.Timestamp,
    deck.Colors, deck.FeaturedCards);

  private static DeckReferenceData ToArchetype(IEnumerable<DeckRevisionView> decks)
  {
    var deck = decks.MaxBy(deck => deck.RevisionId)!;
    return ToDeck(deck) with {
      Id = null, RevisionId = null, Name = deck.Archetype!.Trim(), Archetype = deck.Archetype.Trim(),
      Colors = decks.SelectMany(deck => deck.Colors).Distinct()
        .OrderBy(color => "WUBRG".IndexOf(color, StringComparison.Ordinal)).ToList()
    };
  }

}
