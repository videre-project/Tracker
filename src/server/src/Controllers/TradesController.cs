/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade;
using MTGOSDK.API.Trade.Enums;
using MTGOSDK.Core.Logging;

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Trades;
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

/// <summary>
/// Read-only trade API surface backed by the MTGO client.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TradesController(
  IClientAPIProvider clientProvider,
  ClientStateMonitor clientMonitor) : APIController
{
  private static readonly TimeSpan s_postsCacheTtl = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan s_marketplaceUpdateCoalesceWindow =
    TimeSpan.FromSeconds(1);
  private const int PostsCacheMaxEntries = 32;
  private static readonly object s_postsCacheLock = new();
  private static readonly Dictionary<TradePostsCacheKey, TradePostsCacheEntry> s_postsCache = new();

  private sealed record TradePostsCacheKey(
    int Page,
    int PageSize,
    TradePostFormat? Format,
    string? User,
    string? Message);

  private sealed record TradePostsCacheEntry(
    DateTimeOffset ExpiresAt,
    TradePostsPageDTO Page);

  /// <summary>
  /// Get a snapshot of trade partners and the current trade.
  /// </summary>
  [HttpGet] // GET /api/trades
  [ProducesResponseType(typeof(TradeSnapshotDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public ActionResult<TradeSnapshotDTO> GetTrades()
  {
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    try
    {
      return Ok(new TradeSnapshotDTO
      {
        MyPost = TradeManager.MyPost is { } myPost
          ? ToTradePostDTO(myPost)
          : null,
        TradePartners = TradeManager.TradePartners?
          .ToList()
          ?? new List<TradePartner>(),
        CurrentTrade = TradeManager.CurrentTrade
      });
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to read trade snapshot");
      return StatusCode(StatusCodes.Status500InternalServerError, new
      {
        error = "Failed to read trade snapshot",
        message = ex.Message
      });
    }
  }

  /// <summary>
  /// Get a paged snapshot of marketplace trade posts.
  /// </summary>
  [HttpGet("posts")] // GET /api/trades/posts?page=1&pageSize=10&format=Message&user=example&message=buy
  [ProducesResponseType(typeof(TradePostsPageDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public ActionResult<TradePostsPageDTO> GetPosts(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] string? format = null,
    [FromQuery] string? user = null,
    [FromQuery] string? message = null,
    [FromQuery] bool force = false)
  {
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);

    if (!TryParsePostFormat(format, out var postFormat))
    {
      return BadRequest(new
      {
        error = "Invalid trade post format",
        validValues = new[] { "all", "message", "offeredWantedList" }
      });
    }

    try
    {
      var userSearch = user?.Trim();
      var messageSearch = message?.Trim();
      var cacheKey = new TradePostsCacheKey(
        page,
        pageSize,
        postFormat,
        NormalizeCacheSearch(userSearch),
        NormalizeCacheSearch(messageSearch));

      if (!force && TryGetCachedPosts(cacheKey, out var cachedPage))
      {
        SetPostPageHeaders(cachedPage);
        return Ok(cachedPage);
      }

      var totalCount = CountTradePosts(postFormat, userSearch, messageSearch);
      var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

      page = Math.Min(page, totalPages);
      cacheKey = cacheKey with { Page = page };
      var start = (page - 1) * pageSize;
      List<TradePostDTO> pagePosts;
      var posts = GetTradePosts(
        start + pageSize,
        postFormat,
        userSearch,
        messageSearch);
      try
      {
        pagePosts = posts
          .Skip(start)
          .Take(pageSize)
          .Select(ToTradePostDTO)
          .ToList();
      }
      finally
      {
        posts.Clear();
      }

      var result = new TradePostsPageDTO
      {
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = totalPages,
        HasNextPage = page < totalPages,
        HasPreviousPage = page > 1,
        Posts = pagePosts
      };

      SetPostPageHeaders(result);
      SetCachedPosts(cacheKey, result);
      return Ok(result);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to read trade posts");
      return StatusCode(StatusCodes.Status500InternalServerError, new
      {
        error = "Failed to read trade posts",
        message = ex.Message
      });
    }
  }

  /// <summary>
  /// Watch marketplace update notifications.
  /// </summary>
  [HttpGet("watchmarketplace")] // GET /api/trades/watchmarketplace
  [ProducesResponseType(typeof(TradeMarketplaceUpdateDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchMarketplace()
  {
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    using var linkedCts = BeginNDJSONStream(clientMonitor.Token);
    var streamToken = linkedCts.Token;

    var updateChannel = System.Threading.Channels.Channel.CreateBounded<DateTime>(
      new BoundedChannelOptions(1)
      {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
      });
    var updatePending = 0;
    var updateGate = new object();
    var latestTimestamp = DateTime.UtcNow;

    void onMarketplaceUpdated(DateTime timestamp)
    {
      InvalidatePostsCache();
      bool shouldWrite;
      lock (updateGate)
      {
        latestTimestamp = timestamp;
        shouldWrite = updatePending == 0;
        updatePending = 1;
      }

      if (shouldWrite)
      {
        updateChannel.Writer.TryWrite(timestamp);
      }
      else
      {
        RecordStreamCoalesce();
      }
    }

    TradeManager.MarketplaceUpdated += onMarketplaceUpdated;
    using var cancellationRegistration = streamToken.Register(() =>
      updateChannel.Writer.TryComplete());

    try
    {
      await foreach (var timestamp in updateChannel.Reader.ReadAllAsync(streamToken))
      {
        DateTime emittedTimestamp;
        lock (updateGate)
        {
          emittedTimestamp = latestTimestamp > timestamp ? latestTimestamp : timestamp;
        }

        await StreamResponse(
          [new TradeMarketplaceUpdateDTO(emittedTimestamp)],
          streamToken);

        await Task.Delay(s_marketplaceUpdateCoalesceWindow, streamToken);

        DateTime queuedTimestamp = emittedTimestamp;
        bool shouldQueueNext;
        lock (updateGate)
        {
          shouldQueueNext = latestTimestamp > emittedTimestamp;
          if (shouldQueueNext)
          {
            queuedTimestamp = latestTimestamp;
          }
          else
          {
            updatePending = 0;
          }
        }

        if (shouldQueueNext)
        {
          updateChannel.Writer.TryWrite(queuedTimestamp);
        }
      }
    }
    catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
    {
      // Stream cancelled gracefully.
    }
    finally
    {
      TryRemoveMarketplaceHook(onMarketplaceUpdated);
      updateChannel.Writer.TryComplete();
    }

    return new EmptyResult();
  }

  private static void TryRemoveMarketplaceHook(Action<DateTime> callback)
  {
    try
    {
      TradeManager.MarketplaceUpdated -= callback;
    }
    catch (Exception ex)
    {
      Log.Debug(
        ex,
        "Skipped marketplace hook removal because the MTGO event source is unavailable.");
    }
  }

  private static string? NormalizeCacheSearch(string? value) =>
    string.IsNullOrWhiteSpace(value)
      ? null
      : value.Trim().ToLowerInvariant();

  private static bool TryGetCachedPosts(
    TradePostsCacheKey key,
    out TradePostsPageDTO page)
  {
    lock (s_postsCacheLock)
    {
      if (s_postsCache.TryGetValue(key, out var entry) &&
          entry.ExpiresAt > DateTimeOffset.UtcNow)
      {
        page = entry.Page;
        return true;
      }

      s_postsCache.Remove(key);
    }

    page = null!;
    return false;
  }

  private static void SetCachedPosts(
    TradePostsCacheKey key,
    TradePostsPageDTO page)
  {
    lock (s_postsCacheLock)
    {
      PruneExpiredPostsCacheEntries();
      if (s_postsCache.Count >= PostsCacheMaxEntries &&
          !s_postsCache.ContainsKey(key))
      {
        var oldest = s_postsCache
          .OrderBy(kvp => kvp.Value.ExpiresAt)
          .FirstOrDefault();
        if (oldest.Key != null)
        {
          s_postsCache.Remove(oldest.Key);
        }
      }

      s_postsCache[key] = new TradePostsCacheEntry(
        DateTimeOffset.UtcNow + s_postsCacheTtl,
        page);
    }
  }

  private static void InvalidatePostsCache()
  {
    lock (s_postsCacheLock)
    {
      s_postsCache.Clear();
    }
  }

  [NonAction]
  public static object GetDiagnosticsSnapshot()
  {
    lock (s_postsCacheLock)
    {
      PruneExpiredPostsCacheEntries();
      return new
      {
        PostsCacheEntries = s_postsCache.Count,
      };
    }
  }

  private static void PruneExpiredPostsCacheEntries()
  {
    var now = DateTimeOffset.UtcNow;
    foreach (var key in s_postsCache
      .Where(kvp => kvp.Value.ExpiresAt <= now)
      .Select(kvp => kvp.Key)
      .ToArray())
    {
      s_postsCache.Remove(key);
    }
  }

  private void SetPostPageHeaders(TradePostsPageDTO page)
  {
    Response.Headers["X-Page"] = page.Page.ToString();
    Response.Headers["X-Page-Size"] = page.PageSize.ToString();
    Response.Headers["X-Total-Count"] = page.TotalCount.ToString();
    Response.Headers["X-Total-Pages"] = page.TotalPages.ToString();
    Response.Headers["X-Has-Next-Page"] = page.HasNextPage.ToString();
    Response.Headers["X-Has-Previous-Page"] = page.HasPreviousPage.ToString();
  }

  private static IList<ITradePostSnapshot> GetTradePosts(
    int maxItems,
    TradePostFormat? format,
    string? posterNameSearch,
    string? messageSearch)
  {
    return TradeManager.SerializePostsAs<ITradePostSnapshot>(
      maxItems,
      format,
      posterNameSearch,
      messageSearch).ToList();
  }

  private static int CountTradePosts(
    TradePostFormat? format,
    string? posterNameSearch,
    string? messageSearch)
  {
    return TradeManager.CountPosts(
      format,
      posterNameSearch,
      messageSearch);
  }

  private static TradePostDTO ToTradePostDTO(TradePost post) => new()
    {
      PosterName = post.PosterName,
      Format = post.Format,
      Message = ChatTextNormalizer.Normalize(post.Message),
      Wanted = post.Wanted.ToList(),
      Offered = post.Offered.ToList()
    };

  private static TradePostDTO ToTradePostDTO(ITradePostSnapshot post) => new()
    {
      PosterName = post.PosterName,
      Format = post.Format,
      Message = ChatTextNormalizer.Normalize(post.Message),
      Wanted = post.Wanted?.ToList() ?? [],
      Offered = post.Offered?.ToList() ?? [],
    };

  private static bool TryParsePostFormat(
    string? value,
    out TradePostFormat? format)
  {
    format = null;
    if (string.IsNullOrWhiteSpace(value) ||
        value.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (value.Equals("message", StringComparison.OrdinalIgnoreCase))
    {
      format = TradePostFormat.Message;
      return true;
    }

    if (value.Equals("offeredWantedList", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("list", StringComparison.OrdinalIgnoreCase))
    {
      format = TradePostFormat.OfferedWantedList;
      return true;
    }

    return false;
  }

  public class TradeSnapshotDTO
  {
    public TradePostDTO? MyPost { get; set; }
    public IList<TradePartner> TradePartners { get; set; } =
      new List<TradePartner>();
    public TradeEscrow? CurrentTrade { get; set; }
  }

  public class TradePostsPageDTO
  {
    public required int Page { get; set; }
    public required int PageSize { get; set; }
    public required int TotalCount { get; set; }
    public required int TotalPages { get; set; }
    public required bool HasNextPage { get; set; }
    public required bool HasPreviousPage { get; set; }
    public IList<TradePostDTO> Posts { get; set; } = new List<TradePostDTO>();
  }

  public class TradePostDTO
  {
    public required string PosterName { get; set; }
    public required TradePostFormat Format { get; set; }
    public required string Message { get; set; }
    public required IList<CardQuantityPair> Wanted { get; set; }
    public required IList<CardQuantityPair> Offered { get; set; }
  }

  public class TradeMarketplaceUpdateDTO
  {
    [SetsRequiredMembers]
    public TradeMarketplaceUpdateDTO()
    {
      Timestamp = DateTime.UtcNow;
    }

    [SetsRequiredMembers]
    public TradeMarketplaceUpdateDTO(DateTime timestamp)
    {
      Timestamp = timestamp;
    }

    public required DateTime Timestamp { get; set; }
  }
}
