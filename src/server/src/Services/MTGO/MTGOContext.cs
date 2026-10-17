/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;

using Tracker.Plugins;


namespace Tracker.Services.MTGO;

/// <summary>
/// Publishes host-owned games after their recording processors are ready.
/// </summary>
public sealed class MTGOContext : IMTGOContext, IDisposable
{
  private readonly IClientAPIProvider _provider;
  private readonly object _lock = new();
  private readonly List<(int Id, Game Game)> _games = new();

  /// <summary>
  /// Observes the host's client readiness and publishes its initialized games.
  /// </summary>
  /// <param name="provider">The host's client provider.</param>
  public MTGOContext(IClientAPIProvider provider)
  {
    _provider = provider;
    _provider.ClientStateChanged += OnClientStateChanged;
  }

  /// <inheritdoc/>
  public Client? Client => _provider.IsReady ? _provider.Client : null;

  /// <inheritdoc/>
  public int? UserId => _provider.IsReady ? _provider.CurrentUser?.Id : null;

  /// <inheritdoc/>
  public IReadOnlyList<Game> Games
  {
    get
    {
      lock (_lock)
      {
        return _games.Select(entry => entry.Game).ToArray();
      }
    }
  }

  /// <inheritdoc/>
  public event EventHandler? Changed;

  internal void AddGame(Game game)
  {
    int gameId = game.Id;
    lock (_lock)
    {
      if (_games.Any(entry => entry.Id == gameId))
      {
        return;
      }

      _games.Add((gameId, game));
    }

    NotifyChanged();
  }

  internal void ClearGames()
  {
    lock (_lock)
    {
      if (_games.Count == 0)
      {
        return;
      }

      _games.Clear();
    }

    NotifyChanged();
  }

  internal void RemoveGame(int gameId)
  {
    lock (_lock)
    {
      if (_games.RemoveAll(entry => entry.Id == gameId) == 0)
      {
        return;
      }
    }

    NotifyChanged();
  }

  private void OnClientStateChanged(object? sender, EventArgs args)
  {
    if (!_provider.IsReady)
    {
      lock (_lock)
      {
        _games.Clear();
      }
    }

    NotifyChanged();
  }

  private void NotifyChanged()
  {
    if (Changed is not { } handlers)
    {
      return;
    }

    foreach (EventHandler handler in handlers.GetInvocationList())
    {
      try
      {
        handler(this, EventArgs.Empty);
      }
      catch (Exception exception)
      {
        Log.Error(exception, "An MTGO session subscriber failed.");
      }
    }
  }

  /// <inheritdoc/>
  public void Dispose() => _provider.ClientStateChanged -= OnClientStateChanged;
}
