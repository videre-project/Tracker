/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Text.Json.Serialization;


namespace Tracker.Services.Videre;

internal sealed class VidereProductsResponse
{
  [JsonPropertyName("data")]
  public List<VidereProduct>? Data { get; set; }

  [JsonPropertyName("meta")]
  public VidereProductsMeta? Meta { get; set; }
}

internal sealed class VidereCollectionSearchResponse
{
  [JsonPropertyName("data")]
  public List<VidereCollectionSearchResult>? Data { get; set; }

  [JsonPropertyName("meta")]
  public VidereProductsMeta? Meta { get; set; }
}

internal sealed class VidereCollectionSearchResult
{
  [JsonPropertyName("id")]
  public int Id { get; set; }
}

internal sealed class VidereCardsSearchRequest
{
  [JsonPropertyName("collection")]
  public required VidereCardsSearchCollection Collection { get; set; }
}

internal sealed class VidereCardsSearchCollection
{
  [JsonPropertyName("ids")]
  public required int[] Ids { get; set; }

  [JsonPropertyName("mode")]
  public required string Mode { get; set; }

  [JsonPropertyName("match")]
  public required string Match { get; set; }
}

internal sealed class VidereProductsMeta
{
  [JsonPropertyName("has_more")]
  public bool HasMore { get; set; }

  [JsonPropertyName("next_offset")]
  public int? NextOffset { get; set; }
}

internal sealed class VidereProduct
{
  [JsonPropertyName("id")]
  public int Id { get; set; }

  [JsonPropertyName("set_code")]
  public string? SetCode { get; set; }

  [JsonPropertyName("set_name")]
  public string? SetName { get; set; }

  [JsonPropertyName("name")]
  public string? Name { get; set; }

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("object_type")]
  public string? ObjectType { get; set; }

  [JsonPropertyName("image_url")]
  public string? ImageUrl { get; set; }

  [JsonPropertyName("is_tradable")]
  public bool? IsTradable { get; set; }
}

internal sealed class ViderePricesRequest
{
  [JsonPropertyName("collection")]
  public required ViderePricesCollection Collection { get; set; }

  [JsonPropertyName("date")]
  public required string Date { get; set; }
}

internal sealed class ViderePricesCollection
{
  [JsonPropertyName("ids")]
  public required int[] Ids { get; set; }
}

internal sealed class ViderePricesResponse
{
  [JsonPropertyName("data")]
  public List<ViderePrice>? Data { get; set; }

  [JsonPropertyName("meta")]
  public ViderePricesMeta? Meta { get; set; }
}

internal sealed class ViderePricesMeta
{
  [JsonPropertyName("missing_ids")]
  public int[]? MissingIds { get; set; }
}

internal sealed class ViderePrice
{
  [JsonPropertyName("id")]
  public int Id { get; set; }

  [JsonPropertyName("price_date")]
  public string? PriceDate { get; set; }

  [JsonPropertyName("sell_price")]
  public decimal SellPrice { get; set; }

  [JsonPropertyName("source")]
  public string? Source { get; set; }
}

internal sealed class VidereCardDetailsResponse
{
  [JsonPropertyName("data")]
  public List<VidereCardDetail>? Data { get; set; }
}

internal sealed class VidereCardDetail
{
  [JsonPropertyName("id")]
  public int Id { get; set; }

  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("printed_name")]
  public string? PrintedName { get; set; }

  [JsonPropertyName("display_name")]
  public string? DisplayName { get; set; }

  [JsonPropertyName("set_code")]
  public string? SetCode { get; set; }

  [JsonPropertyName("set_name")]
  public string? SetName { get; set; }

  [JsonPropertyName("collector_number")]
  public string? CollectorNumber { get; set; }

  [JsonPropertyName("rarity")]
  public string? Rarity { get; set; }

  [JsonPropertyName("mana_cost")]
  public string? ManaCost { get; set; }

  [JsonPropertyName("mana_value")]
  public double? ManaValue { get; set; }

  [JsonPropertyName("type_line")]
  public string? TypeLine { get; set; }

  [JsonPropertyName("oracle_text")]
  public string? OracleText { get; set; }

  [JsonPropertyName("flavor_text")]
  public string? FlavorText { get; set; }

  [JsonPropertyName("colors")]
  public List<string>? Colors { get; set; }

  [JsonPropertyName("image_url")]
  public string? ImageUrl { get; set; }

  [JsonPropertyName("power")]
  public string? Power { get; set; }

  [JsonPropertyName("toughness")]
  public string? Toughness { get; set; }

  [JsonPropertyName("loyalty")]
  public string? Loyalty { get; set; }

  [JsonPropertyName("defense")]
  public string? Defense { get; set; }

  [JsonPropertyName("artist")]
  public string? Artist { get; set; }

  [JsonPropertyName("promo_label")]
  public string? PromoLabel { get; set; }
}
