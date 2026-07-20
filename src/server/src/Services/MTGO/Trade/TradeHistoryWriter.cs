/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Tracker.Database;
using Tracker.Database.Models.Trades;


namespace Tracker.Services.MTGO.Trade;

internal sealed record TradeItemWrite(
  TradeEscrowItemRole Role,
  int CatalogId,
  int Quantity);

internal sealed record TradeMessageWrite(
  DateTime Timestamp,
  int? SenderId,
  string? SenderName,
  string Text);

internal sealed record TradeErrorWrite(DateTime ObservedAt, int ErrorCode);

internal sealed record TradeEscrowWrite(
  int AccountId,
  Guid Token,
  int? EscrowId,
  TradeEscrowKind Kind,
  int? PartnerId,
  string? PartnerName,
  DateTime StartedAt,
  DateTime? ClosedAt,
  int State,
  TradeEscrowResult Result,
  TradeAttributionStatus AttributionStatus,
  IReadOnlyList<TradeItemWrite> Items,
  IReadOnlyList<TradeMessageWrite> Messages,
  IReadOnlyList<TradeErrorWrite> Errors);

public sealed class TradeHistoryWriter
{
  public async Task UpsertAccountAsync(
    TradeContext context,
    UserIdentity identity,
    CancellationToken cancellationToken)
  {
    if (!identity.IsValid)
      throw new InvalidOperationException(
        "A valid current user ID and username are required.");

    var account = await context.Accounts.FindAsync(
      [identity.Id],
      cancellationToken);
    if (account == null)
    {
      context.Accounts.Add(new Database.Models.Trades.AccountModel
      {
        Id = identity.Id,
        Username = identity.Username,
      });
    }
    else if (!string.Equals(
      account.Username,
      identity.Username,
      StringComparison.Ordinal))
    {
      account.Username = identity.Username;
    }

    await context.SaveChangesAsync(cancellationToken);
  }

  internal async Task<long> ApplyAsync(
    TradeContext context,
    TradeEscrowWrite write,
    CancellationToken cancellationToken)
  {
    await using var transaction =
      await context.Database.BeginTransactionAsync(cancellationToken);

    TradeEscrowModel? escrow = await context.TradeEscrows
      .SingleOrDefaultAsync(candidate =>
        candidate.AccountId == write.AccountId &&
        candidate.Token == write.Token,
        cancellationToken);
    if (escrow == null)
    {
      escrow = new TradeEscrowModel
      {
        AccountId = write.AccountId,
        Token = write.Token,
        Kind = write.Kind,
        StartedAt = write.StartedAt,
      };
      context.TradeEscrows.Add(escrow);
    }

    escrow.EscrowId = write.EscrowId;
    escrow.Kind = write.Kind;
    escrow.PartnerId = write.PartnerId;
    escrow.PartnerName = write.PartnerName;
    if (write.StartedAt < escrow.StartedAt)
      escrow.StartedAt = write.StartedAt;
    escrow.ClosedAt = write.ClosedAt;
    escrow.State = write.State;
    escrow.Result = write.Result;
    escrow.AttributionStatus = write.AttributionStatus;

    await context.SaveChangesAsync(cancellationToken);

    foreach (TradeItemWrite item in write.Items)
    {
      var existing = await context.TradeEscrowItems.FindAsync(
        [escrow.Id, item.Role, item.CatalogId],
        cancellationToken);
      if (item.Quantity <= 0)
      {
        if (existing != null)
          context.TradeEscrowItems.Remove(existing);
      }
      else if (existing == null)
      {
        context.TradeEscrowItems.Add(new TradeEscrowItemModel
        {
          TradeEscrowId = escrow.Id,
          Role = item.Role,
          CatalogId = item.CatalogId,
          Quantity = item.Quantity,
        });
      }
      else
      {
        existing.Quantity = item.Quantity;
      }
    }

    foreach (TradeMessageWrite message in write.Messages)
    {
      context.TradeEscrowMessages.Add(new TradeEscrowMessageModel
      {
        TradeEscrowId = escrow.Id,
        Timestamp = message.Timestamp,
        SenderId = message.SenderId,
        SenderName = message.SenderName,
        Text = message.Text,
      });
    }

    foreach (TradeErrorWrite error in write.Errors)
    {
      context.TradeEscrowErrors.Add(new TradeEscrowErrorModel
      {
        TradeEscrowId = escrow.Id,
        ObservedAt = error.ObservedAt,
        ErrorCode = error.ErrorCode,
      });
    }

    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return escrow.Id;
  }

  internal async Task ReplaceRoleAsync(
    TradeContext context,
    int accountId,
    Guid token,
    TradeEscrowItemRole role,
    IReadOnlyDictionary<int, int> quantities,
    TradeAttributionStatus? attributionStatus,
    CancellationToken cancellationToken)
  {
    await using var transaction =
      await context.Database.BeginTransactionAsync(cancellationToken);
    TradeEscrowModel escrow = await context.TradeEscrows
      .SingleAsync(candidate =>
        candidate.AccountId == accountId &&
        candidate.Token == token,
        cancellationToken);

    var normalized = quantities
      .Where(item => item.Key > 0 && item.Value > 0)
      .ToDictionary(item => item.Key, item => item.Value);
    var existing = await context.TradeEscrowItems
      .AsNoTracking()
      .Where(item => item.TradeEscrowId == escrow.Id && item.Role == role)
      .ToDictionaryAsync(
        item => item.CatalogId,
        item => item.Quantity,
        cancellationToken);
    bool itemsChanged = existing.Count != normalized.Count ||
      existing.Any(item => normalized.GetValueOrDefault(item.Key) != item.Value);
    bool attributionChanged = attributionStatus.HasValue &&
      escrow.AttributionStatus != attributionStatus.Value;
    if (!itemsChanged && !attributionChanged)
    {
      await transaction.RollbackAsync(cancellationToken);
      return;
    }

    if (itemsChanged)
    {
      await context.TradeEscrowItems
        .Where(item => item.TradeEscrowId == escrow.Id && item.Role == role)
        .ExecuteDeleteAsync(cancellationToken);

      foreach (var item in normalized.OrderBy(item => item.Key))
      {
        context.TradeEscrowItems.Add(new TradeEscrowItemModel
        {
          TradeEscrowId = escrow.Id,
          Role = role,
          CatalogId = item.Key,
          Quantity = item.Value,
        });
      }
    }

    if (attributionChanged)
      escrow.AttributionStatus = attributionStatus!.Value;

    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  internal Task<int> DeleteUnassignedAsync(
    TradeContext context,
    int accountId,
    Guid token,
    CancellationToken cancellationToken) => context.TradeEscrows
      .Where(escrow =>
        escrow.AccountId == accountId &&
        escrow.Token == token &&
        escrow.EscrowId == null)
      .ExecuteDeleteAsync(cancellationToken);

  internal Task<int> DeleteUnassignedClosedAsync(
    TradeContext context,
    int accountId,
    CancellationToken cancellationToken) => context.TradeEscrows
      .Where(escrow =>
        escrow.AccountId == accountId &&
        escrow.EscrowId == null &&
        escrow.ClosedAt != null)
      .ExecuteDeleteAsync(cancellationToken);

  public async Task EndSessionAsync(
    TradeContext context,
    int accountId,
    CancellationToken cancellationToken)
  {
    await context.TradeEscrows
      .Where(escrow =>
        escrow.AccountId == accountId &&
        escrow.Result == TradeEscrowResult.InProgress)
      .ExecuteUpdateAsync(setters => setters
        .SetProperty(
          escrow => escrow.Result,
          TradeEscrowResult.Interrupted),
        cancellationToken);

    await context.TradeEscrows
      .Where(escrow =>
        escrow.AccountId == accountId &&
        escrow.AttributionStatus == TradeAttributionStatus.Pending)
      .ExecuteUpdateAsync(setters => setters
        .SetProperty(
          escrow => escrow.AttributionStatus,
          TradeAttributionStatus.Unavailable),
        cancellationToken);
  }
}
