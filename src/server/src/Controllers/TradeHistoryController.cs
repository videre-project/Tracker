/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Trade.Enums;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Logging;

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Database.Models.Trades;
using Tracker.Services.MTGO;
using Tracker.Services.Videre;


namespace Tracker.Controllers;

/// <summary>
/// Read-only history of player and non-player trade escrows.
/// </summary>
[ApiController]
[Route("api/trades/history")]
public sealed class TradeHistoryController(
  TradeContext context,
  IClientAPIProvider clientProvider,
  VidereAPIClient videreAPIClient) : APIController
{
  private static readonly ConcurrentDictionary<int, VidereCatalogMetadataResult>
    s_catalogMetadataCache = new();
  private static readonly ConcurrentDictionary<int, CachedPartnerAvatar>
    s_partnerAvatarCache = new();
  private static readonly TimeSpan s_partnerAvatarCacheDuration =
    TimeSpan.FromMinutes(15);
  private static readonly TimeSpan s_missingPartnerAvatarCacheDuration =
    TimeSpan.FromMinutes(2);

  private sealed record CachedPartnerAvatar(
    int? CatalogId,
    string? Name,
    DateTimeOffset ExpiresAt);

  /// <summary>
  /// Get trade history for an account, newest first.
  /// </summary>
  [HttpGet]
  [ProducesResponseType(typeof(TradeHistoryPageDTO), StatusCodes.Status200OK)]
  public async Task<ActionResult<TradeHistoryPageDTO>> GetHistory(
    [FromQuery] int? accountId = null,
    [FromQuery] long? beforeId = null,
    [FromQuery] int limit = 50,
    [FromQuery] string? search = null,
    [FromQuery] TradeEscrowKind? kind = null,
    [FromQuery] TradeEscrowResult? result = null,
    CancellationToken cancellationToken = default)
  {
    limit = Math.Clamp(limit, 1, 200);
    int selectedAccountId = accountId ??
      clientProvider.CurrentUser?.Id ??
      await context.TradeEscrows
        .OrderByDescending(escrow => escrow.StartedAt)
        .Select(escrow => escrow.AccountId)
        .FirstOrDefaultAsync(cancellationToken);
    if (selectedAccountId <= 0)
      return Ok(new TradeHistoryPageDTO([], null));

    IQueryable<TradeEscrowModel> query = context.TradeEscrows
      .AsNoTracking()
      .Include(escrow => escrow.Items)
      .Where(escrow => escrow.AccountId == selectedAccountId);

    if (kind.HasValue)
      query = query.Where(escrow => escrow.Kind == kind.Value);
    if (result.HasValue)
      query = query.Where(escrow => escrow.Result == result.Value);

    string normalizedSearch = search?.Trim() ?? string.Empty;
    if (normalizedSearch.Length > 0)
    {
      string normalizedPartnerSearch = normalizedSearch.ToLowerInvariant();
      bool hasNumericSearch = int.TryParse(normalizedSearch, out int numericSearch);
      query = query.Where(escrow =>
        (escrow.PartnerName != null &&
          escrow.PartnerName.ToLower().Contains(normalizedPartnerSearch)) ||
        (hasNumericSearch &&
          (escrow.EscrowId == numericSearch ||
           escrow.PartnerId == numericSearch)));
    }

    if (beforeId.HasValue)
      query = query.Where(escrow => escrow.Id < beforeId.Value);

    List<TradeEscrowModel> rows = await query
      .OrderByDescending(escrow => escrow.Id)
      .Take(limit + 1)
      .ToListAsync(cancellationToken);
    bool hasMore = rows.Count > limit;
    if (hasMore)
      rows.RemoveAt(rows.Count - 1);

    var partnerAvatars = ResolvePartnerAvatars(rows);
    int[] catalogIds = rows
      .Where(escrow => escrow.Kind == TradeEscrowKind.NonPlayer)
      .SelectMany(escrow => escrow.Items)
      .Where(item => item.Role == TradeEscrowItemRole.LocalOffer)
      .Select(item => item.CatalogId)
      .Concat(partnerAvatars.Values
        .Where(avatar => avatar.CatalogId.HasValue)
        .Select(avatar => avatar.CatalogId!.Value))
      .Distinct()
      .ToArray();
    var metadata = await GetCatalogMetadataAsync(catalogIds, cancellationToken);
    var items = rows
      .Select(escrow => ToSummary(
        escrow,
        GetProductName(escrow, metadata),
        GetPartnerAvatar(escrow, partnerAvatars, metadata)))
      .ToArray();
    return Ok(new TradeHistoryPageDTO(
      items,
      hasMore && items.Length > 0 ? items[^1].Id : null));
  }

  /// <summary>
  /// Get one trade escrow with terminal offers, escrow-correlated product outputs,
  /// chat, and errors.
  /// </summary>
  [HttpGet("{id:long}")]
  [ProducesResponseType(typeof(TradeHistoryDetailDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<TradeHistoryDetailDTO>> GetHistoryDetail(
    long id,
    CancellationToken cancellationToken = default)
  {
    TradeEscrowModel? escrow = await context.TradeEscrows
      .AsNoTracking()
      .Include(candidate => candidate.Items)
      .Include(candidate => candidate.Messages)
      .Include(candidate => candidate.Errors)
      .AsSplitQuery()
      .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (escrow == null)
      return NotFound();

    var partnerAvatars = ResolvePartnerAvatars([escrow]);
    int[] catalogIds = escrow.Items
      .Select(item => item.CatalogId)
      .Concat(partnerAvatars.Values
        .Where(avatar => avatar.CatalogId.HasValue)
        .Select(avatar => avatar.CatalogId!.Value))
      .Distinct()
      .ToArray();
    var metadata = await GetCatalogMetadataAsync(catalogIds, cancellationToken);

    TradeHistoryItemDTO[] items = escrow.Items
      .OrderBy(item => item.Role)
      .ThenBy(item => item.CatalogId)
      .Select(item =>
      {
        metadata.TryGetValue(item.CatalogId, out var catalog);
        return new TradeHistoryItemDTO(
          item.Role,
          item.CatalogId,
          item.Quantity,
          catalog?.Name,
          catalog?.SetCode,
          catalog?.Rarity,
          catalog?.ObjectType);
      })
      .ToArray();

    TradeHistoryEffectDTO[] effects = GetEffects(escrow, items);
    return Ok(new TradeHistoryDetailDTO(
      ToSummary(
        escrow,
        GetProductName(escrow, metadata),
        GetPartnerAvatar(escrow, partnerAvatars, metadata)),
      escrow.Token,
      escrow.AccountId,
      items,
      effects,
      escrow.Messages
        .OrderBy(message => message.Id)
        .Select(message => new TradeHistoryMessageDTO(
          message.Id,
          message.Timestamp,
          message.SenderId,
          message.SenderName,
          message.Text))
        .ToArray(),
      escrow.Errors
        .OrderBy(error => error.Id)
        .Select(error => new TradeHistoryErrorDTO(
          error.Id,
          error.ObservedAt,
          error.ErrorCode,
          Enum.GetName(typeof(TradeError), error.ErrorCode)))
        .ToArray()));
  }

  private async Task<IReadOnlyDictionary<int, VidereCatalogMetadataResult>>
    GetCatalogMetadataAsync(
      IEnumerable<int> catalogIds,
      CancellationToken cancellationToken)
  {
    int[] requestedIds = catalogIds
      .Where(id => id > 0)
      .Distinct()
      .ToArray();
    var metadata = new Dictionary<int, VidereCatalogMetadataResult>();
    var missingIds = new List<int>();
    foreach (int id in requestedIds)
    {
      if (s_catalogMetadataCache.TryGetValue(id, out var cached))
        metadata[id] = cached;
      else
        missingIds.Add(id);
    }

    if (missingIds.Count == 0)
      return metadata;

    try
    {
      var fetched = await videreAPIClient.GetCatalogMetadataAsync(
        missingIds,
        cancellationToken);
      foreach (var item in fetched)
      {
        s_catalogMetadataCache[item.Key] = item.Value;
        metadata[item.Key] = item.Value;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      Log.Warning(ex,
        "Failed to enrich trade history catalog metadata; returning catalog IDs");
    }

    return metadata;
  }

  private Dictionary<int, CachedPartnerAvatar> ResolvePartnerAvatars(
    IEnumerable<TradeEscrowModel> escrows)
  {
    var avatars = new Dictionary<int, CachedPartnerAvatar>();
    var now = DateTimeOffset.UtcNow;
    foreach (var escrow in escrows.Where(escrow =>
      escrow.Kind == TradeEscrowKind.Player && escrow.PartnerId > 0))
    {
      int partnerId = escrow.PartnerId!.Value;
      if (s_partnerAvatarCache.TryGetValue(partnerId, out var cached) &&
          cached.ExpiresAt > now)
      {
        if (cached.CatalogId.HasValue)
          avatars[partnerId] = cached;
        continue;
      }

      if (!clientProvider.IsReady)
        continue;

      try
      {
        User user = string.IsNullOrWhiteSpace(escrow.PartnerName)
          ? UserManager.GetUser(partnerId)
          : UserManager.GetUser(partnerId, escrow.PartnerName);
        var avatar = user.Avatar;
        var resolved = avatar?.Id > 0
          ? new CachedPartnerAvatar(
              avatar.Id,
              avatar.Name,
              now.Add(s_partnerAvatarCacheDuration))
          : new CachedPartnerAvatar(
              null,
              null,
              now.Add(s_missingPartnerAvatarCacheDuration));
        s_partnerAvatarCache[partnerId] = resolved;
        if (resolved.CatalogId.HasValue)
          avatars[partnerId] = resolved;
      }
      catch (Exception ex)
      {
        s_partnerAvatarCache[partnerId] = new CachedPartnerAvatar(
          null,
          null,
          now.Add(s_missingPartnerAvatarCacheDuration));
        Log.Debug(ex,
          "Failed to resolve avatar for trade partner {PartnerId}",
          partnerId);
      }
    }

    return avatars;
  }

  private static string? GetProductName(
    TradeEscrowModel escrow,
    IReadOnlyDictionary<int, VidereCatalogMetadataResult> metadata)
  {
    if (escrow.Kind != TradeEscrowKind.NonPlayer)
      return null;

    int catalogId = escrow.Items
      .Where(item => item.Role == TradeEscrowItemRole.LocalOffer)
      .OrderByDescending(item => item.Quantity)
      .ThenBy(item => item.CatalogId)
      .Select(item => item.CatalogId)
      .FirstOrDefault();
    return metadata.TryGetValue(catalogId, out var product)
      ? product.Name
      : null;
  }

  private static TradePartnerAvatarDTO? GetPartnerAvatar(
    TradeEscrowModel escrow,
    IReadOnlyDictionary<int, CachedPartnerAvatar> avatars,
    IReadOnlyDictionary<int, VidereCatalogMetadataResult> metadata)
  {
    if (escrow.Kind != TradeEscrowKind.Player ||
        escrow.PartnerId is not int partnerId ||
        !avatars.TryGetValue(partnerId, out var avatar) ||
        avatar.CatalogId is not int catalogId)
    {
      return null;
    }

    metadata.TryGetValue(catalogId, out var product);
    return new TradePartnerAvatarDTO(
      catalogId,
      product?.Name ?? avatar.Name ?? $"Avatar {catalogId}",
      product?.ImageUrl);
  }

  private static TradeHistorySummaryDTO ToSummary(
    TradeEscrowModel escrow,
    string? productName,
    TradePartnerAvatarDTO? partnerAvatar)
  {
    IReadOnlyCollection<TradeEscrowItemModel> outgoing = [];
    IReadOnlyCollection<TradeEscrowItemModel> incoming = [];
    if (escrow.Result == TradeEscrowResult.Completed)
    {
      outgoing = escrow.Items
        .Where(item => item.Role == TradeEscrowItemRole.LocalOffer)
        .ToArray();
      TradeEscrowItemRole incomingRole = escrow.Kind == TradeEscrowKind.Player
        ? TradeEscrowItemRole.RemoteOffer
        : TradeEscrowItemRole.InferredOutput;
      incoming = escrow.Items
        .Where(item => item.Role == incomingRole)
        .ToArray();
    }

    return new TradeHistorySummaryDTO(
      escrow.Id,
      escrow.EscrowId,
      escrow.Kind,
      productName,
      escrow.PartnerId,
      escrow.PartnerName,
      partnerAvatar,
      escrow.StartedAt,
      escrow.ClosedAt,
      escrow.State,
      Enum.GetName(typeof(TradeState), escrow.State),
      escrow.Result,
      escrow.AttributionStatus,
      outgoing.Sum(item => item.Quantity),
      outgoing.Count,
      incoming.Sum(item => item.Quantity),
      incoming.Count);
  }

  private static TradeHistoryEffectDTO[] GetEffects(
    TradeEscrowModel escrow,
    IReadOnlyList<TradeHistoryItemDTO> items)
  {
    if (escrow.Result != TradeEscrowResult.Completed)
      return [];

    return items
      .Where(item => escrow.Kind == TradeEscrowKind.Player
        ? item.Role is TradeEscrowItemRole.LocalOffer or
          TradeEscrowItemRole.RemoteOffer
        : item.Role is TradeEscrowItemRole.LocalOffer or
          TradeEscrowItemRole.InferredOutput)
      .Select(item => new TradeHistoryEffectDTO(
        item.CatalogId,
        item.Role == TradeEscrowItemRole.LocalOffer
          ? -item.Quantity
          : item.Quantity,
        item.Role == TradeEscrowItemRole.InferredOutput))
      .OrderBy(effect => effect.CatalogId)
      .ToArray();
  }
}

public sealed record TradeHistoryPageDTO(
  IReadOnlyList<TradeHistorySummaryDTO> Items,
  long? NextBeforeId);

public sealed record TradeHistorySummaryDTO(
  long Id,
  int? EscrowId,
  TradeEscrowKind Kind,
  string? ProductName,
  int? PartnerId,
  string? PartnerName,
  TradePartnerAvatarDTO? PartnerAvatar,
  DateTime StartedAt,
  DateTime? ClosedAt,
  int State,
  string? StateName,
  TradeEscrowResult Result,
  TradeAttributionStatus AttributionStatus,
  int OutgoingQuantity,
  int OutgoingCatalogCount,
  int IncomingQuantity,
  int IncomingCatalogCount);

public sealed record TradeHistoryDetailDTO(
  TradeHistorySummaryDTO Summary,
  Guid Token,
  int AccountId,
  IReadOnlyList<TradeHistoryItemDTO> Items,
  IReadOnlyList<TradeHistoryEffectDTO> Effects,
  IReadOnlyList<TradeHistoryMessageDTO> Messages,
  IReadOnlyList<TradeHistoryErrorDTO> Errors);

public sealed record TradeHistoryItemDTO(
  TradeEscrowItemRole Role,
  int CatalogId,
  int Quantity,
  string? Name,
  string? SetCode,
  string? Rarity,
  string? ObjectType);

public sealed record TradePartnerAvatarDTO(
  int ProductCatalogId,
  string ProductName,
  string? ProductImageUrl);

public sealed record TradeHistoryEffectDTO(
  int CatalogId,
  int Quantity,
  bool IsInferred);

public sealed record TradeHistoryMessageDTO(
  long Id,
  DateTime Timestamp,
  int? SenderId,
  string? SenderName,
  string Text);

public sealed record TradeHistoryErrorDTO(
  long Id,
  DateTime ObservedAt,
  int ErrorCode,
  string? ErrorName);
