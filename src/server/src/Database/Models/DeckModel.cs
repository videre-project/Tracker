/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using MTGOSDK.API.Collection;


namespace Tracker.Database.Models;

[method: JsonConstructor]
public record CardEntry(int catalogId, string name, int quantity)
{
  public CardEntry(int catalogId, int quantity) :
      this(catalogId, CollectionManager.GetCard(catalogId).Name, quantity) { }
}

public class DeckModel
{
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }

  public required string Hash { get; set; }
  public required DateTime Timestamp { get; set; }

  public required List<CardEntry> Mainboard { get; set; }
  public required List<CardEntry> Sideboard { get; set; }

  /// <summary>
  /// The deck's color identity as a list of color characters (W, U, B, R, G)
  /// </summary>
  public List<string> Colors { get; set; } = new();

  /// <summary>
  /// The deck's archetype as classified by the NBAC API (e.g., "Burn", "Affinity")
  /// </summary>
  public string? Archetype { get; set; }

  /// <summary>
  /// The featured/key card for this deck's archetype (from NBAC explain data)
  /// </summary>
  public string? FeaturedCard { get; set; }

  public static DeckModel ToModel(Deck deck)
  {
    var model = new DeckModel
    {
      Id = deck.DeckId,
      Name = deck.Name,
      Format = deck.Format!,
      Timestamp = deck.Timestamp,
      Hash = deck.Hash,
      Mainboard = new(),
      Sideboard = new(),
      Colors = new()
    };

    // Collect unique colors while iterating over mainboard cards
    var colorSet = new HashSet<char>();

    foreach (var card in deck.GetCards(DeckRegion.MainDeck))
    {
      model.Mainboard.Add(new CardEntry(card.Id, card.Card, card.Quantity));

      // Extract colors from the card
      try
      {
        var colors = card.Card.Colors;
        if (!string.IsNullOrEmpty(colors))
        {
          foreach (char color in colors)
          {
            if ("WUBRG".Contains(color))
            {
              colorSet.Add(color);
            }
          }
        }
      }
      catch
      {
        // Skip cards that fail color lookup
      }
    }

    foreach (var card in deck.GetCards(DeckRegion.Sideboard))
    {
      model.Sideboard.Add(new CardEntry(card.Id, card.Card, card.Quantity));
    }

    // Sort colors in WUBRG order
    model.Colors = colorSet
      .OrderBy(c => "WUBRG".IndexOf(c))
      .Select(c => c.ToString())
      .ToList();

    return model;
  }

  /// <summary>
  /// Populates the Colors property by looking up each card in the mainboard.
  /// </summary>
  public void PopulateColors()
  {
    var colorSet = new HashSet<char>();

    foreach (var cardEntry in Mainboard)
    {
      try
      {
        var card = CollectionManager.GetCard(cardEntry.catalogId);
        if (card != null && !string.IsNullOrEmpty(card.Colors))
        {
          foreach (char color in card.Colors)
          {
            if ("WUBRG".Contains(color))
            {
              colorSet.Add(color);
            }
          }
        }
      }
      catch
      {
        // Skip cards that fail color lookup
      }
    }

    // Sort colors in WUBRG order
    Colors = colorSet
      .OrderBy(c => "WUBRG".IndexOf(c))
      .Select(c => c.ToString())
      .ToList();
  }

  /// <summary>
  /// Populates the Archetype and FeaturedCard properties by fetching from the NBAC API.
  /// </summary>
  /// <param name="httpClient">The HttpClient instance to use for the request.</param>
  /// <param name="nbacApiUrl">The base URL for the NBAC API endpoint.</param>
  public async Task PopulateArchetypeAsync(HttpClient httpClient, string nbacApiUrl)
  {
    var (archetype, featuredCard) = await FetchArchetypeDataAsync(this, httpClient, nbacApiUrl);
    Archetype = archetype;
    FeaturedCard = featuredCard;
  }

  /// <summary>
  /// Fetches the deck's archetype and featured card from the NBAC API.
  /// </summary>
  /// <param name="deck">The deck model to classify.</param>
  /// <param name="httpClient">The HttpClient instance to use for the request.</param>
  /// <param name="nbacApiUrl">The base URL for the NBAC API endpoint.</param>
  /// <returns>A tuple of (archetype name, featured card name), or (null, null) if classification fails.</returns>
  public static async Task<(string? Archetype, string? FeaturedCard)> FetchArchetypeDataAsync(
    DeckModel deck,
    HttpClient httpClient,
    string nbacApiUrl)
  {
    // Build request body with card names and quantities
    var cards = deck.Mainboard
      .Select(c => new { name = c.name, quantity = c.quantity })
      .ToList();

    // NBAC requires at least 2 cards
    if (cards.Count < 2)
    {
      return (null, null);
    }

    try
    {
      var jsonOptions = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      var requestBody = JsonSerializer.Serialize(cards, jsonOptions);
      var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

      // Use explain=1 to get the top contributing card
      var response = await httpClient.PostAsync(
        $"{nbacApiUrl}?format={Uri.EscapeDataString(deck.Format.ToLower())}&explain=1",
        content
      );

      if (!response.IsSuccessStatusCode)
      {
        return (null, null);
      }

      var responseContent = await response.Content.ReadAsStringAsync();
      var nbacResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

      string? archetype = null;
      string? featuredCard = null;

      // Extract the top archetype from the response
      if (nbacResponse.TryGetProperty("data", out var dataElement))
      {
        var archetypes = dataElement.EnumerateObject().ToList();
        if (archetypes.Count > 0)
        {
          // Get the archetype with the highest probability
          archetype = archetypes
            .OrderByDescending(a => a.Value.GetDouble())
            .First()
            .Name;

          // Extract the top card from explain data for this archetype
          if (archetype != null &&
              nbacResponse.TryGetProperty("explain", out var explainElement) &&
              explainElement.TryGetProperty("archetypes", out var archetypeExplain) &&
              archetypeExplain.TryGetProperty(archetype, out var cardList))
          {
            var topCardEntry = cardList.EnumerateArray().FirstOrDefault();
            if (topCardEntry.ValueKind != JsonValueKind.Undefined)
            {
              featuredCard = topCardEntry.GetProperty("card").GetString();
            }
          }
        }
      }

      return (archetype, featuredCard);
    }
    catch
    {
      return (null, null);
    }
  }

  /// <summary>
  /// Fetches just the deck's archetype from the NBAC API (without featured card).
  /// </summary>
  /// <param name="deck">The deck model to classify.</param>
  /// <param name="httpClient">The HttpClient instance to use for the request.</param>
  /// <param name="nbacApiUrl">The base URL for the NBAC API endpoint.</param>
  /// <returns>The top archetype name, or null if classification fails.</returns>
  public static async Task<string?> FetchArchetypeAsync(DeckModel deck, HttpClient httpClient, string nbacApiUrl)
  {
    var (archetype, _) = await FetchArchetypeDataAsync(deck, httpClient, nbacApiUrl);
    return archetype;
  }
}
