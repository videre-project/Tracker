/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;


namespace Tracker.Database.Models.Events;

public class GameCardModel
{
  public int Id { get; set; }

  public int GameId { get; set; }
  public GameModel Game { get; set; }

  /// <summary>
  /// The snapshot where this card first appeared.
  /// </summary>
  public int FirstSeenStateId { get; set; }
  public GameStateModel FirstSeenState { get; set; }

  /// <summary>
  /// Prior ThingID from the ZoneTransfer move event (ancestry chain).
  /// Null if this is an original card (not transformed/copied).
  /// </summary>
  public int? SourceId { get; set; }

  /// <summary>
  /// MTGO ThingID for this card instance.
  /// </summary>
  public int CardId { get; set; }

  public string Name { get; set; }
  public string? RulesText { get; set; }
  public string? ManaCost { get; set; }
  public string? TypeLine { get; set; }

  /// <summary>
  /// Card texture number for rendering.
  /// </summary>
  public int? TextureId { get; set; }

  /// <summary>
  /// Catalog ID for CDN card image URL. Resolved from TextureId (CTN)
  /// via ICardDataManager.GetCardDefinitionForTextureNumber().
  /// </summary>
  public int? CatalogId { get; set; }

  public string InitialZone { get; set; }
  public string? InitialPower { get; set; }
  public string? InitialToughness { get; set; }

  /// <summary>
  /// JSON of counter dictionary if counters exist.
  /// </summary>
  public string? InitialCounters { get; set; }

  /// <summary>
  /// JSON array of CardAbility enum values.
  /// </summary>
  public string? InitialAbilities { get; set; }

  public int OwnerId { get; set; }
  public int ControllerId { get; set; }
  public bool IsTapped { get; set; }
  public bool IsToken { get; set; }
  public bool IsLand { get; set; }
  public bool IsActivatedAbility { get; set; }
  public bool IsTriggeredAbility { get; set; }
}
