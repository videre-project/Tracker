/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

using Tracker.WebView;


namespace Tracker;

public class Program
{
  /// <summary>
  /// Initializes the builder for the Web API host.
  /// </summary>
  /// <param name="args">The command-line arguments.</param>
  /// <returns>A new <see cref="WebApplicationBuilder"/> instance.</returns>
  public static WebApplicationBuilder CreateHostBuilder(string[] args)
  {
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
      Args = args,
      WebRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"),
      ContentRootPath = AppDomain.CurrentDomain.BaseDirectory,
    });

    builder.WebHost.UseUrls("https://localhost:7183"); // Set the HTTPS endpoint

    // Add services to the container.
    builder.Services.AddControllers();

    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
      options.SwaggerDoc("v1", new OpenApiInfo
      {
        Version = "v1",
        Title = "Videre Tracker API",
        Description = "An ASP.NET Core Web API for the Videre Tracker.",
        TermsOfService = new Uri("https://videreproject.com/terms")
      });
    });

    return builder;
  }

  /// <summary>
  /// Initializes the ASP.NET Core Web API service.
  /// </summary>
  /// <param name="builder">The builder for the Web API host.</param>
  /// <returns>A new <see cref="WebApplication"/> instance.</returns>
  public static WebApplication CreateAPIService(WebApplicationBuilder builder)
  {
    var api = builder.Build();

    api.UseDefaultFiles();
    api.UseStaticFiles();

    // Configure the HTTP request pipeline.
    if (api.Environment.IsDevelopment())
    {
      api.UseSwagger();
      api.UseSwaggerUI();
    }

    api.UseAuthorization();

    api.MapControllers();
    api.MapFallbackToFile("index.html");

    return api;
  }

  /// <summary>
  /// The main entry point for the application.
  /// </summary>
  [STAThread]
  public static void Main(string[] args)
  {
    // Create a new thread to run the ASP.NET Core Web API.
    var builder = CreateHostBuilder(args);
    var api = CreateAPIService(builder);
    var apiThread = new Thread(() => api.Run())
    {
      Name = "API Thread",
      IsBackground = true
    };

    // Use API lifecycle events to stop the application.
    api.Lifetime.ApplicationStopping.Register(Application.Exit);
    apiThread.Start();

    // Start the WebView2 application.
    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new HostForm()
    {
      Source = new Uri(builder.Configuration[WebHostDefaults.ServerUrlsKey]!),
      Logger = api.Logger,
    });
  }
}
