/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;


namespace Tracker.WebView;

public class SplashForm : Form
{
  private Panel? _gradientHeader;
  private Panel? _historyPanel;
  private RichTextBox? _splashHistory;
  private Label? _splashStatus;
  private Panel? _splashProgressBar;
  private Timer? _splashTimer;
  private float _progress = 0;
  private Image? _logoImage;
  
  private record LogMessage(string Message, LogLevel Level, string? Timestamp, string? Header, string? Label);
  private readonly System.Collections.Concurrent.ConcurrentQueue<LogMessage> _logQueue = new();

  [System.Runtime.InteropServices.DllImport("user32.dll")]
  private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
  private const int WM_SETREDRAW = 0x0B;
  
  // Double buffered panel for smooth rendering
  private class DoubleBufferedPanel : Panel
  {
    public DoubleBufferedPanel()
    {
      this.DoubleBuffered = true;
      this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
      this.UpdateStyles();
    }
  }

  public SplashForm()
  {
    this.FormBorderStyle = FormBorderStyle.None;
    this.StartPosition = FormStartPosition.CenterScreen;
    this.Size = new Size(700, 450);
    this.BackColor = Color.FromArgb(24, 24, 27); // Zinc-950
    this.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
    this.DoubleBuffered = true;

    InitializeUI();
  }

  private void InitializeUI()
  {
    // Main container panel (optional since we are a Form now, but keeps structure)
    var mainPanel = new Panel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.FromArgb(18, 18, 20),
    };
    this.Controls.Add(mainPanel);

    // Gradient header panel
    _gradientHeader = new DoubleBufferedPanel
    {
      Dock = DockStyle.Top,
      Height = 120,
      BackColor = Color.Transparent
    };
    _gradientHeader.Paint += GradientHeader_Paint;

    // Load logo image from embedded resources
    try
    {
      var assembly = Assembly.GetExecutingAssembly();
      var resourceName = "logo.png";
      
      using var stream = assembly.GetManifestResourceStream(resourceName) 
        ?? assembly.GetManifestResourceStream("Tracker.server.src.client.dist.logo.png")
        ?? assembly.GetManifestResourceStream("Tracker.logo.png");

      if (stream != null)
      {
        _logoImage = Image.FromStream(stream);
      }
    }
    catch { /* Ignore logo load failure */ }

    _historyPanel = new Panel
    {
      Size = new Size(670, 200),
      BackColor = Color.FromArgb(28, 28, 30),
      Padding = new Padding(10)
    };

    _splashHistory = new RichTextBox
    {
      Dock = DockStyle.Fill,
      Text = "",
      Font = new Font("Consolas", 9, FontStyle.Regular),
      ForeColor = Color.FromArgb(180, 180, 190),
      BackColor = Color.FromArgb(28, 28, 30),
      BorderStyle = BorderStyle.None,
      ReadOnly = true,
      ScrollBars = RichTextBoxScrollBars.None,
      DetectUrls = false,
      ShortcutsEnabled = false
    };
    _historyPanel.Controls.Add(_splashHistory);

    _splashStatus = new Label
    {
      Text = "Initializing...",
      Font = new Font("Segoe UI", 10, FontStyle.Bold),
      ForeColor = Color.FromArgb(228, 228, 231), // Zinc-200
      AutoSize = true,
      MaximumSize = new Size(500, 0),
      BackColor = Color.Transparent,
      TextAlign = ContentAlignment.TopCenter
    };

    _splashProgressBar = new DoubleBufferedPanel
    {
      Dock = DockStyle.Bottom,
      Height = 4,
      BackColor = Color.FromArgb(39, 39, 42), // Zinc-800
    };
    _splashProgressBar.Paint += SplashProgressBar_Paint;

    _splashProgressBar.Paint += SplashProgressBar_Paint;

    mainPanel.Controls.Add(_gradientHeader);
    mainPanel.Controls.Add(_historyPanel);
    mainPanel.Controls.Add(_splashStatus);
    mainPanel.Controls.Add(_splashProgressBar);

    // Center labels
    CenterSplashLabels();
    this.Resize += (s, e) => CenterSplashLabels();

    // Timer for animation
    _splashTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
    _splashTimer.Tick += (s, e) =>
    {
      _progress += 0.02f;
      if (_progress > 2.0f) _progress = 0.0f;
      _time += 0.008f;
      _splashProgressBar?.Invalidate();
      _gradientHeader?.Invalidate();

      // Process log queue in batches
      if (!_logQueue.IsEmpty && _splashHistory != null)
      {
        SendMessage(_splashHistory.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);
        try
        {
          LogMessage? lastLog = null;
          int count = 0;
          // Process up to 50 messages per frame to avoid freezing
          while (count < 50 && _logQueue.TryDequeue(out var log))
          {
            lastLog = log;
            count++;

            if (log.Timestamp != null && log.Header != null && log.Label != null)
            {
              _splashHistory.SelectionStart = _splashHistory.TextLength;
              _splashHistory.SelectionLength = 0;

              // Timestamp (Purple)
              _splashHistory.SelectionColor = Color.FromArgb(189, 147, 249);
              _splashHistory.AppendText(log.Timestamp + " ");

              // Header (Based on Level)
              _splashHistory.SelectionColor = GetColorForLevel(log.Level);
              _splashHistory.AppendText(log.Header + " ");

              // Label (Gray)
              _splashHistory.SelectionColor = Color.Gray;
              _splashHistory.AppendText(log.Label + "\n");

              // Message (White/Default)
              _splashHistory.SelectionColor = Color.FromArgb(228, 228, 231);
              _splashHistory.AppendText(log.Message + "\n");
            }
            else
            {
              _splashHistory.SelectionStart = _splashHistory.TextLength;
              _splashHistory.SelectionColor = GetColorForLevel(log.Level);
              _splashHistory.AppendText(log.Message + "\n");
            }
          }
          
          _splashHistory.ScrollToCaret();

          // Update status label with the last message
          if (lastLog != null && _splashStatus != null && lastLog.Level != LogLevel.Warning)
          {
            _splashStatus.Text = lastLog.Message;
            CenterSplashLabels();
          }
        }
        finally
        {
          SendMessage(_splashHistory.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
          _splashHistory.Invalidate();
        }
      }
    };
    _splashTimer.Start();
  }

  private void CenterSplashLabels()
  {
    if (_splashStatus != null && _historyPanel != null && _gradientHeader != null && _splashProgressBar != null)
    {
      // Helper to avoid unnecessary layout updates
      void SetLocation(Control c, Point p)
      {
        if (c.Location != p) c.Location = p;
      }
      
      // Position history panel below gradient
      if (_historyPanel.Width != this.ClientSize.Width - 30)
        _historyPanel.Width = this.ClientSize.Width - 30;
        
      SetLocation(_historyPanel, new Point(
        15,
        _gradientHeader.Height + 15));

      // Position status centered between history panel and progress bar
      var availableSpace = this.ClientSize.Height - _historyPanel.Bottom - _splashProgressBar.Height;
      SetLocation(_splashStatus, new Point(
        (this.ClientSize.Width - _splashStatus.Width) / 2,
        _historyPanel.Bottom + (availableSpace - _splashStatus.Height) / 2));
    }
  }

  public void UpdateStatus(string message, LogLevel level = LogLevel.Information, string? timestamp = null, string? header = null, string? label = null)
  {
    _logQueue.Enqueue(new LogMessage(message, level, timestamp, header, label));
  }

  private Color GetColorForLevel(LogLevel level)
  {
    return level switch
    {
      LogLevel.Information => Color.FromArgb(8, 159, 162), // Teal
      LogLevel.Warning => Color.FromArgb(255, 140, 0),     // Orange
      LogLevel.Error or LogLevel.Critical => Color.Red,
      _ => Color.FromArgb(228, 228, 231) // Zinc-200
    };
  }

  private Bitmap? _cachedBackground;
  private float _time = 0;

  private void GradientHeader_Paint(object? sender, PaintEventArgs e)
  {
    if (_gradientHeader == null) return;
    
    if (_cachedBackground == null || _cachedBackground.Width != _gradientHeader.Width || _cachedBackground.Height != _gradientHeader.Height)
    {
      _cachedBackground?.Dispose();
      _cachedBackground = new Bitmap(_gradientHeader.Width, _gradientHeader.Height);
      
      using var g = Graphics.FromImage(_cachedBackground);
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      DrawStaticBackground(g, _gradientHeader.ClientRectangle);
    }

    e.Graphics.DrawImage(_cachedBackground, 0, 0);
    
    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    DrawMeshPattern(e.Graphics, _gradientHeader.ClientRectangle, _time);
    
    DrawLogo(e.Graphics, new Rectangle(20, 30, 60, 60));

    using var titleFont = new Font("Segoe UI", 24, FontStyle.Bold);
    using var versionFont = new Font("Segoe UI", 10, FontStyle.Regular);
    using var titleBrush = new SolidBrush(Color.White);
    using var versionBrush = new SolidBrush(Color.FromArgb(200, 200, 200));

    var titleSize = e.Graphics.MeasureString("Videre Tracker", titleFont);
    float titleY = (_gradientHeader.Height - titleSize.Height) / 2 - 5;
    
    e.Graphics.DrawString("Videre Tracker", titleFont, titleBrush, 90, titleY);
    
    var versionText = $"v{Tracker.ProductInfo.Version}";
    e.Graphics.DrawString(versionText, versionFont, versionBrush, 100, titleY + titleSize.Height - 10);
  }

  private void DrawStaticBackground(Graphics g, Rectangle rect)
  {
    using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
      rect,
      Color.FromArgb(30, 58, 138), // Dark Blue (blue-900)
      Color.FromArgb(15, 23, 42),  // Very Dark Blue (slate-900)
      System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
    
    g.FillRectangle(brush, rect);
    
    // Add black-to-transparent overlay gradient
    using var overlayBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
      rect,
      Color.FromArgb(120, 0, 0, 0),
      Color.FromArgb(0, 0, 0, 0),
      System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
    g.FillRectangle(overlayBrush, rect);
  }

  private void DrawMeshPattern(Graphics g, Rectangle bounds, float time)
  {
    using var pen = new Pen(Color.FromArgb(35, 255, 255, 255), 1.5f);
    
    int lineCount = 28;
    int segments = 60;
    
    for (int i = 0; i < lineCount; i++)
    {
      float normalizedY = i / (float)(lineCount - 1);
      float baseY = bounds.Height * (-0.2f + normalizedY * 1.4f);
      
      var points = new List<PointF>();
      for (int j = 0; j <= segments; j++)
      {
        float normalizedX = j / (float)segments;
        
        // Add X-variation with slow time offset
        float xOffset = (float)(Math.Sin(normalizedY * 8.0 + normalizedX * 4.0 + time * 0.5) * 15.0);
        float x = bounds.Width * normalizedX + xOffset;
        
        double offset = 0;
        // Primary wave (diagonal) - moving
        offset += Math.Sin(normalizedX * 5.0 + normalizedY * 4.0 + time) * 20.0;
        
        // Cross wave - moving opposite
        offset += Math.Sin(normalizedX * 7.0 - normalizedY * 5.0 - time * 0.8) * 15.0;
        
        // Interference/Ripple - faster
        offset += Math.Cos(normalizedX * 12.0 + normalizedY * 10.0 + Math.Sin(normalizedX * 5.0) + time * 1.5) * 8.0;
        
        // Fine detail
        offset += Math.Sin(normalizedX * 25.0 + time * 2.0) * 3.0;

        points.Add(new PointF(x, baseY + (float)offset));
      }
      
      if (points.Count > 1)
      {
        g.DrawCurve(pen, points.ToArray(), 0.5f);
      }
    }
  }

  private void DrawLogo(Graphics g, Rectangle bounds)
  {
    if (_logoImage != null)
    {
      var aspectRatio = (float)_logoImage.Width / _logoImage.Height;
      var targetWidth = bounds.Width;
      var targetHeight = (int)(targetWidth / aspectRatio);
      
      if (targetHeight > bounds.Height)
      {
        targetHeight = bounds.Height;
        targetWidth = (int)(targetHeight * aspectRatio);
      }
      
      var x = bounds.X + (bounds.Width - targetWidth) / 2;
      var y = bounds.Y + (bounds.Height - targetHeight) / 2;
      
      g.DrawImage(_logoImage, x, y, targetWidth, targetHeight);
    }
    else
    {
      using var whitePen = new Pen(Color.White, 3);
      using var whiteBrush = new SolidBrush(Color.White);
      
      var eyeBounds = new Rectangle(bounds.X, bounds.Y + bounds.Height / 4, bounds.Width, bounds.Height / 2);
      g.DrawEllipse(whitePen, eyeBounds);
      
      var pupilSize = bounds.Width / 3;
      var pupilBounds = new Rectangle(
        bounds.X + (bounds.Width - pupilSize) / 2,
        bounds.Y + (bounds.Height - pupilSize) / 2,
        pupilSize,
        pupilSize);
      g.FillEllipse(whiteBrush, pupilBounds);
    }
  }

  private void SplashProgressBar_Paint(object? sender, PaintEventArgs e)
  {
    if (_splashProgressBar == null) return;
    
    var rect = _splashProgressBar.ClientRectangle;
    using var bgBrush = new SolidBrush(Color.FromArgb(39, 39, 42));
    e.Graphics.FillRectangle(bgBrush, rect);

    int width = (int)(rect.Width * 0.4f);
    int x = (int)((rect.Width + width) * (_progress / 2.0f)) - width;

    using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
        new Rectangle(x, 0, width, rect.Height),
        Color.Transparent, Color.Transparent, 0f)
    {
      InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
      {
          Positions = new[] { 0.0f, 0.5f, 1.0f },
          Colors = new[] 
          { 
            Color.FromArgb(0, 59, 130, 246),
            Color.FromArgb(255, 59, 130, 246),
            Color.FromArgb(0, 59, 130, 246)
          }
      }
    };
    
    e.Graphics.FillRectangle(brush, x, 0, width, rect.Height);
  }

  protected override void OnFormClosed(FormClosedEventArgs e)
  {
    _splashTimer?.Stop();
    _splashTimer?.Dispose();
    _cachedBackground?.Dispose();
    base.OnFormClosed(e);
  }
}
