/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;

using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Database.Models.Events;


namespace Tracker.Tests.Infrastructure;

internal static class ControllerFixtureData
{
  public static async Task SeedGameAsync(TrackerTestHost host)
  {
    using var scope = host.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<EventContext>();
    var eventModel = new EventModel
    {
      Id = 1001,
      Format = "Pauper",
      Type = EventType.Match,
      Description = "Fixture match",
      StartTime = DateTime.UnixEpoch,
      EndTime = DateTime.UnixEpoch.AddMinutes(10),
    };
    var match = new MatchModel
    {
      Id = 1002,
      PlayerResults =
      [
        new PlayerResult("player1", MatchResult.Win, 1, 0, 0),
        new PlayerResult("player2", MatchResult.Loss, 0, 1, 0),
      ],
    };
    var state = new GameStateModel
    {
      Nonce = 1,
      Timestamp = 1,
      ActionTimestamp = DateTime.UnixEpoch,
      ClientTimestamp = DateTime.UnixEpoch,
      TurnNumber = 1,
      CurrentPhase = "Main",
      PromptedPlayer = 0,
      PromptText = "Play a land.",
      PromptOptions = "[]",
    };
    state.Actions.Add(new GameActionModel
    {
      ActionType = "CardAction",
      ActionName = "Play",
      Timestamp = 1,
      ClientTimestamp = DateTime.UnixEpoch,
      CardId = 11,
      CardName = "Island",
      Data = "{}",
    });
    state.Logs.Add(new GameLogModel
    {
      Timestamp = DateTime.UnixEpoch,
      GameLogType = "LogMessage",
      Data = "player1 plays Island.",
    });

    var game = new GameModel
    {
      Id = 1003,
      GamePlayerResults =
      [
        new GamePlayerResult(
          "player1", PlayDrawResult.Play, GameResult.Win,
          TimeSpan.FromMinutes(5)),
        new GamePlayerResult(
          "player2", PlayDrawResult.Draw, GameResult.Loss,
          TimeSpan.FromMinutes(5)),
      ],
    };
    game.States.Add(state);
    match.SideboardChanges[1003] =
    [
      new CardEntry(15530, "Force Spike", 2),
    ];
    match.Games.Add(game);
    eventModel.Matches.Add(match);
    context.Events.Add(eventModel);
    context.Events.Add(new EventModel
    {
      Id = 2001,
      Format = "Modern",
      Type = EventType.Match,
      Description = "Other player match",
      StartTime = DateTime.UnixEpoch.AddHours(1),
      EndTime = DateTime.UnixEpoch.AddHours(2),
      Matches =
      [
        new MatchModel
        {
          Id = 2002,
          PlayerResults =
          [
            new PlayerResult("player3", MatchResult.Win, 2, 0, 0),
            new PlayerResult("player4", MatchResult.Loss, 0, 2, 0),
          ],
        },
      ],
    });
    await context.SaveChangesAsync();

    game.Players.Add(new GamePlayerModel
    {
      PlayerIndex = 0,
      Name = "player1",
      PlayDraw = "Play",
      InitialLife = 20,
      InitialHandCount = 7,
      InitialLibraryCount = 53,
      InitialGraveyardCount = 0,
      IsActivePlayer = true,
      UserId = 7,
      FirstSeenStateId = state.Id,
    });
    game.Players.Add(new GamePlayerModel
    {
      PlayerIndex = 1,
      Name = "player2",
      PlayDraw = "Draw",
      InitialLife = 20,
      InitialHandCount = 7,
      InitialLibraryCount = 53,
      InitialGraveyardCount = 0,
      IsActivePlayer = false,
      UserId = 8,
      FirstSeenStateId = state.Id,
    });
    game.Cards.Add(new GameCardModel
    {
      CardId = 11,
      Name = "Island",
      CatalogId = 102392,
      InitialZone = "Hand",
      OwnerId = 0,
      ControllerId = 0,
      IsLand = true,
      FirstSeenStateId = state.Id,
    });
    await context.SaveChangesAsync();
  }
}
