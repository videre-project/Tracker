/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;

using Microsoft.Data.Sqlite;

using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
public sealed class FixtureReferenceTests
{
  [Test]
  public void Test_Wine()
  {
    Assert.That(File.Exists(FixtureFiles.EventDatabase), Is.True);
    EventFixtureMetadata metadata = FixtureFiles.ReadEventMetadata();

    Assert.That(metadata.Sanitized, Is.True);
    Assert.That(metadata.Source, Does.Contain("bot-vs-bot reference"));
    Assert.That(metadata.Scenario,
      Is.EqualTo("bot-vs-bot multi-game replay with sideboarding"));
    Assert.That(metadata.GameIds, Has.Count.EqualTo(3));
    Assert.That(metadata.OpeningHands, Has.Count.EqualTo(3));

    using var connection = new SqliteConnection(
      $"Data Source={FixtureFiles.EventDatabase};Mode=ReadOnly");
    connection.Open();
    Assert.That(Scalar(connection, "SELECT count(*) FROM Events"), Is.EqualTo(1));
    Assert.That(Scalar(connection, "SELECT count(*) FROM Matches"), Is.EqualTo(1));
    Assert.That(Scalar(connection, "SELECT count(*) FROM Games"), Is.EqualTo(3));
    Assert.That(
      Scalar(connection, "SELECT count(*) FROM GameStates"),
      Is.GreaterThan(100));
    Assert.That(
      Scalar(connection, "SELECT count(*) FROM ZoneTransfers WHERE ToZone = 'Hand'"),
      Is.GreaterThan(0));
    Assert.That(
      Scalar(connection,
        "SELECT count(*) FROM GamePlayers WHERE Name NOT IN ('PlayerA', 'PlayerB')"),
      Is.EqualTo(0));
  }

  private static int Scalar(SqliteConnection connection, string sql)
  {
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt32(command.ExecuteScalar());
  }
}
