/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Trade.Enums;

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Database.Models.Trades;
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

/// <summary>
/// Read-only history of player and non-player trade escrows.
/// </summary>
[ApiController]
[Route("api/trades/history")]
public sealed class TradeHistoryController(
  TradeContext context,
  IClientAPIProvider clientProvider) : APIController
{
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

    var items = rows.Select(ToSummary).ToArray();
    return Ok(new TradeHistoryPageDTO(
      items,
      hasMore && items.Length > 0 ? items[^1].Id : null));
  }

  /// <summary>
  /// Get one trade escrow with terminal offers, inferred outputs, chat, and errors.
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

    TradeHistoryItemDTO[] items = escrow.Items
      .OrderBy(item => item.Role)
      .ThenBy(item => item.CatalogId)
      .Select(item => new TradeHistoryItemDTO(
        item.Role,
        item.CatalogId,
        item.Quantity))
      .ToArray();

    TradeHistoryEffectDTO[] effects = GetEffects(escrow, items);
    return Ok(new TradeHistoryDetailDTO(
      ToSummary(escrow),
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

  private static TradeHistorySummaryDTO ToSummary(TradeEscrowModel escrow)
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
      escrow.PartnerId,
      escrow.PartnerName,
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
  int? PartnerId,
  string? PartnerName,
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
  int Quantity);

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
