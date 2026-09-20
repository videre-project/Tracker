/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API;
using MTGOSDK.Core.Security;
using MTGOSDK.Core.Remoting;


namespace MTGOLauncher;

internal static class Program
{
  [STAThread]
  public static async Task<int> Main(string[] args)
  {
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
      eventArgs.Cancel = true;
      shutdown.Cancel();
    };

    try
    {
      while (!await Client.IsOnline())
      {
        Console.WriteLine("MTGO servers are currently offline. Waiting...");
        await Task.Delay(TimeSpan.FromMinutes(30), shutdown.Token);
      }

      var client = new Client(
        new ClientOptions
        {
          CreateProcess = true,
          AcceptEULAPrompt = true
        }
      );

      if (!client.IsLoggedIn)
      {
        DotEnv.LoadFile("/workspace/.env");
        await client.LogOn(
          username: DotEnv.Get("USERNAME"),
          password: DotEnv.Get("PASSWORD")
        );
        Console.WriteLine($"Connected as {client.CurrentUser.Name}");
      }

      Console.WriteLine("MTGO startup flow completed.");

      Process? process = null;
      while (!shutdown.IsCancellationRequested)
      {
        process = RemoteClient.ClientProcess;
        if (process is not null && !process.HasExited)
          break;
        await Task.Delay(TimeSpan.FromSeconds(1), shutdown.Token);
      }

      if (process is not null && !process.HasExited)
        await process.WaitForExitAsync(shutdown.Token);

      return 0;
    }
    catch (OperationCanceledException)
    {
      return 0;
    }
  }
}
