/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors.Snapshots;


namespace Tracker.Services.MTGO.Events;

/// <summary>
/// Complete replay interaction flags, with display data for pending abilities.
/// Empty lists clear the preceding state's flags.
/// </summary>
public sealed record GameInteractionData(
  [property: JsonConverter(typeof(JsonStringEnumConverter<InteractionMode>))] InteractionMode Mode,
  string? PromptText,
  IReadOnlyList<int> Selectable,
  IReadOnlyList<int> Targetable,
  IReadOnlyList<int> Targeted,
  IReadOnlyList<int> Casting,
  IReadOnlyList<int> Dimmed,
  IReadOnlyList<AbilityInteractionData> Abilities,
  IReadOnlyList<PlayerInteractionData> Players)
{
  // Brightness and cast markers accompany decisions; alone they are UI effects.
  internal bool HasSameDecision(GameInteractionData other) =>
    Mode == other.Mode && PromptText == other.PromptText
    && Selectable.SequenceEqual(other.Selectable)
    && Targetable.SequenceEqual(other.Targetable)
    && Targeted.SequenceEqual(other.Targeted)
    && Abilities.Count == other.Abilities.Count
    && Abilities.Zip(other.Abilities).All(pair => pair.First.HasSameDecision(pair.Second))
    && Players.Select(player => (player.PlayerIndex, player.IsTargetable, player.IsTargeted))
      .SequenceEqual(other.Players.Select(player => (player.PlayerIndex, player.IsTargetable, player.IsTargeted)));

  public static GameInteractionData FromSnapshot(InteractionStateSnapshot snapshot) =>
    FromCards(snapshot.Mode, snapshot.PromptBoxText,
      snapshot.Cards.Values.Select(card => new CardFlags(
        card.LogicalAddress.ToString(CultureInfo.InvariantCulture), card.CardId,
        card.Selection.IsSelectable, card.Selection.IsTargetable, card.Selection.IsTargeted,
        card.Selection.IsActive, card.Selection.IsBeingCast,
        card.Zone, card.Name, card.RulesText, card.CatalogId)).ToArray(),
      snapshot.Players.Select(player => new PlayerInteractionData(
        player.PlayerIndex, player.IsTargetable, player.IsTargeted, player.IsActive
      )).ToArray());

  /// <summary>
  /// Reads compact recordings or projects older per-card interaction objects.
  /// Available actions are not needed to reconstruct replay visuals.
  /// </summary>
  internal static GameInteractionData Deserialize(string json, JsonSerializerOptions options)
  {
    using var document = JsonDocument.Parse(json);
    if (!document.RootElement.TryGetProperty("cards", out _))
      return JsonSerializer.Deserialize<GameInteractionData>(json, options)!;

    var legacy = JsonSerializer.Deserialize<LegacyInteraction>(json, options)!;
    return FromCards(legacy.Mode, legacy.PromptText, legacy.Cards, legacy.Players);
  }

  private static GameInteractionData FromCards(
    InteractionMode mode, string? promptText,
    IReadOnlyList<CardFlags> cards, IReadOnlyList<PlayerInteractionData> players)
  {
    // Scene-only objects can share unassigned IDs. Keep their display identity
    // separately, and never apply ambiguous IDs to ordinary replay cards.
    var regular = cards.Where(card => card.CardId > 0)
      .GroupBy(card => card.CardId).Where(group => group.Count() == 1)
      .Select(group => group.Single()).OrderBy(card => card.CardId).ToArray();
    int[] Select(Func<CardFlags, bool> predicate) =>
      regular.Where(predicate).Select(card => card.CardId).ToArray();
    bool IsDimmed(CardFlags card) => card.IsBeingCast != true &&
      (card.IsActive.HasValue ? !card.IsActive.Value
        : mode is not InteractionMode.None and not InteractionMode.Undefined
          && !card.IsSelectable && !card.IsTargetable && !card.IsTargeted);

    return new(mode, promptText,
      Select(card => card.IsSelectable), Select(card => card.IsTargetable),
      Select(card => card.IsTargeted), Select(card => card.IsBeingCast == true),
      Select(IsDimmed),
      cards.Where(card => card.Zone is CardZone.LocalTriggers or CardZone.OpponentTriggers)
        .Select(card => new AbilityInteractionData(
          card.LogicalAddress, card.Zone!.Value, card.Name, card.RulesText, card.CatalogId,
          card.IsSelectable, card.IsTargetable, card.IsTargeted,
          card.IsBeingCast == true, !IsDimmed(card))).ToArray(),
      players.OrderBy(player => player.PlayerIndex).ToArray());
  }

  private sealed record LegacyInteraction(
    [property: JsonConverter(typeof(JsonStringEnumConverter<InteractionMode>))] InteractionMode Mode,
    string? PromptText,
    IReadOnlyList<CardFlags> Cards,
    IReadOnlyList<PlayerInteractionData> Players);

  private sealed record CardFlags(
    string LogicalAddress,
    int CardId,
    bool IsSelectable,
    bool IsTargetable,
    bool IsTargeted,
    bool? IsActive,
    bool? IsBeingCast,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CardZone>))] CardZone? Zone,
    string? Name,
    string? RulesText,
    int? CatalogId);
}

/// <summary>
/// Display identity and visual flags for an ability awaiting placement on the stack.
/// </summary>
public sealed record AbilityInteractionData(
  string LogicalAddress,
  [property: JsonConverter(typeof(JsonStringEnumConverter<CardZone>))] CardZone Zone,
  string? Name,
  string? RulesText,
  int? CatalogId,
  bool IsSelectable,
  bool IsTargetable,
  bool IsTargeted,
  bool IsBeingCast,
  bool IsActive)
{
  internal bool HasSameDecision(AbilityInteractionData other) =>
    LogicalAddress == other.LogicalAddress && Zone == other.Zone
    && Name == other.Name && RulesText == other.RulesText && CatalogId == other.CatalogId
    && IsSelectable == other.IsSelectable && IsTargetable == other.IsTargetable
    && IsTargeted == other.IsTargeted;
}

public sealed record PlayerInteractionData(
  int PlayerIndex,
  bool IsTargetable,
  bool IsTargeted,
  bool IsActive
);
