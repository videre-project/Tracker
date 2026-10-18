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
using MTGOSDK.API.Play.Games;
using MTGOTracker.Controllers.Models.Games;
using MTGOTracker.Database.Models.Events;
using static MTGOTracker.Services.MTGO.Events.MatchHistorySerialization;

namespace MTGOTracker.Controllers;

public partial class DecksController
{
  /// <summary>
  /// Recorded card observations for a deck or one immutable revision.
  /// </summary>
  [HttpGet("{revisionId:long}/statistics")]
  public async Task<ActionResult<DeckStatisticsData>> GetDeckStatistics(
    long revisionId, CancellationToken cancellationToken, string scope = "deck",
    DateTime? minDate = null, DateTime? maxDate = null, string? opponent = null,
    string gameScope = "all")
  {
    if (clientProvider.CurrentUser is not { } account) return BadRequest("Client not ready.");
    if (scope is not ("deck" or "revision"))
      return BadRequest(new { error = "Choose deck or revision scope." });
    if (gameScope is not ("all" or "one" or "postboard"))
      return BadRequest(new { error = "Choose all games, Game 1, or Games 2–3." });

    var deck = await deckService.GetRevisionAsync(revisionId, cancellationToken);
    if (deck?.AccountId != account.Id) return NotFound();
    long[] revisionIds = scope == "revision"
      ? [revisionId]
      : (await deckService.GetRevisionGroupingIdsAsync(
          new[] { deck.CardGroupingId }, cancellationToken)).Keys.ToArray();
    var query = context.MatchesForPlayer(account.Username, minDate, maxDate, completedOnly: true)
      .Where(match => match.Event.DeckRevisionId.HasValue &&
        revisionIds.Contains(match.Event.DeckRevisionId.Value));

    if (!string.IsNullOrWhiteSpace(opponent))
      query = query.Where(match =>
        EF.Functions.Collate((match.OpponentDeckArchetype ?? "").Trim(), "NOCASE") == opponent.Trim());

    var matches = await query.Include(match => match.Games).ThenInclude(game => game.Players)
      .AsSplitQuery().ToListAsync(cancellationToken);
    var games = matches.SelectMany(match => match.Games.OrderBy(game => game.Id)
        .Where((_, index) => gameScope switch
        {
          "one" => index == 0,
          "postboard" => index > 0,
          _ => true,
        }))
      .Where(game => game.GamePlayerResults.Any(result =>
        string.Equals(result.Player, account.Username, StringComparison.OrdinalIgnoreCase) &&
        result.Result is GameResult.Win or GameResult.Loss))
      .ToList();
    await ReadPlayDrawAsync(context, games, account.Username, cancellationToken);
    var playDraw = games.ToDictionary(game => game.Id, game => ResolvePlayDraw(game, account.Username));
    var gameIds = games.Select(game => game.Id).ToHashSet();
    var players = games.ToDictionary(game => game.Id, game => game.Players.FirstOrDefault(player =>
      string.Equals(player.Name, account.Username, StringComparison.OrdinalIgnoreCase)));
    var capturedCards = await context.GameCards.AsNoTracking()
      .Where(card => gameIds.Contains(card.GameId) &&
        !card.IsToken && !card.IsActivatedAbility && !card.IsTriggeredAbility)
      .Select(card => new { card.GameId, card.OwnerId, card.CardId, card.SourceId, card.Name,
        card.InitialZone, card.FirstSeenStateId, card.IsLand })
      .ToListAsync(cancellationToken);
    var cardsByGame = capturedCards.Where(card => players[card.GameId]?.PlayerIndex == card.OwnerId)
      .ToLookup(card => card.GameId);
    var recordedGameIds = cardsByGame.Select(group => group.Key).ToArray();
    var frames = await context.GameStates.AsNoTracking()
      .Where(frame => recordedGameIds.Contains(frame.GameId))
      .Select(frame => new { frame.GameId, frame.Id, frame.TurnNumber, frame.CurrentPhase })
      .ToListAsync(cancellationToken);
    var keeps = await context.GameActions.AsNoTracking()
      .Where(action => recordedGameIds.Contains(action.GameState.GameId) && action.ActionName == "Keep")
      .Select(action => new { action.GameState.GameId, action.GameStateId, action.GameState.PromptedPlayer })
      .ToListAsync(cancellationToken);
    var transfers = await context.ZoneTransfers.AsNoTracking()
      .Where(move => recordedGameIds.Contains(move.GameState.GameId))
      .Select(move => new { move.GameState.GameId, move.Id, move.GameStateId, move.CardId,
        move.SourceId, move.FromZone, move.ToZone })
      .ToListAsync(cancellationToken);
    var handCounts = await context.PlayerStateChanges.AsNoTracking()
      .Where(change => recordedGameIds.Contains(change.GameState.GameId) && change.Property == "HandCount")
      .Select(change => new { change.GameState.GameId, change.Id, change.GameStateId,
        change.PlayerIndex, change.NewValue })
      .ToListAsync(cancellationToken);
    var framesByGame = frames.ToLookup(frame => frame.GameId);
    var movesByGame = transfers.ToLookup(move => move.GameId);
    var keepsByGame = keeps.ToLookup(keep => keep.GameId);
    var countsByGame = handCounts.ToLookup(count => count.GameId);
    var observations = games.ToDictionary(game => game.Id, ObserveGame);
    WinLossRecord Record(IEnumerable<GameModel> source) => GameRecord(source, account.Username);

    var revisions = await deckService.GetRevisionsAsync(
      matches.Select(match => match.Event.DeckRevisionId!.Value), cancellationToken);
    var startingDecks = new Dictionary<int, Dictionary<string, int>>();
    foreach (var match in matches)
    {
      if (match.Event.DeckRevisionId is not long id || !revisions.TryGetValue(id, out var revision)) continue;
      var ordered = match.Games.OrderBy(game => game.Id).ToList();
      foreach (var game in ordered.Where(game => gameIds.Contains(game.Id)))
      {
        // Each game's sideboard changes are relative to the registered list.
        // Missing changes mean the starting list is unknown; an empty list
        // means the registered mainboard was kept.
        if (game != ordered[0] && !match.SideboardChanges.ContainsKey(game.Id)) continue;
        var quantities = revision.Mainboard.GroupBy(card => card.name.Trim(), StringComparer.OrdinalIgnoreCase)
          .ToDictionary(group => group.Key, group => group.Sum(card => card.quantity), StringComparer.OrdinalIgnoreCase);
        if (game != ordered[0])
        {
          foreach (var change in match.SideboardChanges[game.Id])
            quantities[change.name.Trim()] = quantities.GetValueOrDefault(change.name.Trim()) + change.quantity;
        }
        startingDecks[game.Id] = quantities;
      }
    }

    var mainboard = deck.Mainboard.ToLookup(card => card.name.Trim(), StringComparer.OrdinalIgnoreCase);
    var sideboard = deck.Sideboard.ToLookup(card => card.name.Trim(), StringComparer.OrdinalIgnoreCase);
    var rows = deck.Mainboard.Concat(deck.Sideboard)
      .GroupBy(card => card.name.Trim(), StringComparer.OrdinalIgnoreCase).Select(group =>
      {
        string name = group.Key;
        // Only recorded games with this card in a known starting list contribute.
        var eligibleGames = games.Where(game => observations[game.Id].HasRecording &&
          startingDecks.TryGetValue(game.Id, out var list) && list.GetValueOrDefault(name) > 0).ToList();
        return new DeckCardStatisticsData(
          Card: group.First() with { quantity = group.Sum(card => card.quantity) },
          MainboardCopies: mainboard[name].Sum(card => card.quantity),
          SideboardCopies: sideboard[name].Sum(card => card.quantity),
          InHand: Record(eligibleGames.Where(game => observations[game.Id].InHand.Contains(name))),
          OpeningHand: Record(eligibleGames.Where(game => observations[game.Id].OpeningHand?.Contains(name) == true)),
          LibraryToHand: Record(eligibleGames.Where(game => observations[game.Id].LibraryToHand.Contains(name))),
          Cast: Record(eligibleGames.Where(game => observations[game.Id].Cast.Contains(name))),
          InHandOnPlay: Record(eligibleGames.Where(game =>
            observations[game.Id].InHand.Contains(name) && playDraw[game.Id] == "Play")),
          InHandOnDraw: Record(eligibleGames.Where(game =>
            observations[game.Id].InHand.Contains(name) && playDraw[game.Id] == "Draw")));
      }).OrderByDescending(card => card.InHand.Total).ThenBy(card => card.Card.name).ToList();

    return Ok(new DeckStatisticsData(
      Games: Record(games),
      RecordedGames: observations.Values.Count(row => row.HasRecording),
      Cards: rows));

    (bool HasRecording, HashSet<string>? OpeningHand, HashSet<string> InHand,
      HashSet<string> LibraryToHand, HashSet<string> Cast) ObserveGame(GameModel game)
    {
      var cards = cardsByGame[game.Id].ToList();
      var states = framesByGame[game.Id].OrderBy(frame => frame.Id).ToList();
      var moves = movesByGame[game.Id].OrderBy(move => move.GameStateId).ThenBy(move => move.Id).ToList();
      int firstTurnStateId = states.FirstOrDefault(frame => frame.TurnNumber > 0)?.Id ?? int.MaxValue;
      // Zone transfers assign new ThingIDs. Both recorded sources link the
      // changing IDs to the same physical card.
      var sources = cards.Where(card => card.SourceId.HasValue)
        .GroupBy(card => card.CardId).ToDictionary(group => group.Key, group => group.First().SourceId!.Value);
      foreach (var move in moves.Where(move => move.SourceId.HasValue))
        sources[move.CardId] = move.SourceId!.Value;
      int Root(int id)
      {
        var visited = new HashSet<int>();
        while (visited.Add(id) && sources.TryGetValue(id, out int source)) id = source;
        return id;
      }
      var cardNames = cards.Where(card => !string.IsNullOrWhiteSpace(card.Name) && card.Name != "Card" &&
          !card.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        .OrderBy(card => card.FirstSeenStateId).GroupBy(card => Root(card.CardId))
        .ToDictionary(group => group.Key, group => group.First().Name.Trim());
      var cardsById = cards.GroupBy(card => card.CardId).ToDictionary(group => group.Key, group => group.First());
      var openingZones = cards.Where(card => card.FirstSeenStateId < firstTurnStateId)
        .OrderBy(card => card.FirstSeenStateId).GroupBy(card => Root(card.CardId))
        .ToDictionary(group => group.Key, group => (string?)group.Last().InitialZone);
      foreach (var move in moves.Where(move => move.GameStateId < firstTurnStateId))
        openingZones[Root(move.CardId)] = move.ToZone;
      var startingHand = openingZones
        .Where(pair => pair.Value == nameof(CardZone.Hand) && cardNames.ContainsKey(pair.Key))
        .Select(pair => cardNames[pair.Key]).ToList();
      HashSet<string> Names(IEnumerable<int> ids) => ids.Select(Root).Where(cardNames.ContainsKey)
        .Select(id => cardNames[id]).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var inHand = Names(cards
        .Where(card => card.FirstSeenStateId >= firstTurnStateId && card.InitialZone == nameof(CardZone.Hand))
        .Select(card => card.CardId)
        .Concat(moves.Where(move => move.GameStateId >= firstTurnStateId &&
          (move.FromZone == nameof(CardZone.Hand) || move.ToZone == nameof(CardZone.Hand)))
          .Select(move => move.CardId)));
      if (firstTurnStateId != int.MaxValue) inHand.UnionWith(startingHand);
      var libraryToHand = Names(moves.Where(move => move.GameStateId >= firstTurnStateId &&
        move.FromZone == nameof(CardZone.Library) && move.ToZone == nameof(CardZone.Hand))
        .Select(move => move.CardId));
      var cast = Names(moves.Where(move => move.GameStateId >= firstTurnStateId &&
        move.ToZone == nameof(CardZone.Stack) && cardsById.TryGetValue(move.CardId, out var card) && !card.IsLand)
        .Select(move => move.CardId));

      var player = players[game.Id];
      var handCount = countsByGame[game.Id]
        .Where(count => count.PlayerIndex == player?.PlayerIndex && count.GameStateId < firstTurnStateId)
        .OrderBy(count => count.GameStateId).ThenBy(count => count.Id).LastOrDefault();
      int expectedHandSize = player?.InitialHandCount ?? 0;
      if (handCount != null)
        expectedHandSize = int.TryParse(handCount.NewValue, out int size) ? size : 0;
      // An opening hand requires a captured Keep and every physical copy in the
      // recorded hand count. Midgame reconnects still contribute observations
      // without claiming a complete opening hand.
      bool openingCaptured = firstTurnStateId != int.MaxValue && expectedHandSize > 0 &&
        startingHand.Count == expectedHandSize &&
        states.Any(frame => frame.Id < firstTurnStateId && frame.TurnNumber == 0 &&
          frame.CurrentPhase is nameof(GamePhase.PreGame1) or nameof(GamePhase.PreGame2)) &&
        keepsByGame[game.Id].Any(keep =>
          keep.GameStateId < firstTurnStateId && keep.PromptedPlayer == player?.PlayerIndex);
      return (
        HasRecording: firstTurnStateId != int.MaxValue && cardNames.Count > 0,
        OpeningHand: openingCaptured ? startingHand.ToHashSet(StringComparer.OrdinalIgnoreCase) : null,
        InHand: inHand,
        LibraryToHand: libraryToHand,
        Cast: cast);
    }
  }
}
