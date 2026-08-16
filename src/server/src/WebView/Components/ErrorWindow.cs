/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;


namespace Tracker.WebView.Components;

public class ErrorWindow : Form, IResizableForm
{
  private Panel _panel = null!;
  private DwmTitleBar? _dwmTitleBar;

  public Size? RestoreSize { get; set; }

  public ErrorWindow(Exception exception, string label)
  {
  // Enable double buffering to reduce flicker
  this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
  this.UpdateStyles();

  this.InitializeComponent();
  this.SetError(exception, label);
  }

  private void InitializeComponent()
  {
    this.SuspendLayout();

    // Creates a panel that spans the entire form, below the titlebar.
    this._panel = new Panel();
    this._panel.Location = new Point(0, 30);
    this._panel.Size = new Size(820, 520);
    this._panel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
    this._panel.BackColor = this.BackColor;
    this._panel.SetDoubleBuffered(true);

    this.AutoScaleDimensions = new SizeF(8F, 20F);
    this.AutoScaleMode = AutoScaleMode.Font;
    this.StartPosition = FormStartPosition.CenterScreen;
    this.ClientSize = new Size(820, 550);
    this.Name = "ErrorWindow";
    this.Text = Application.ProductName;
    this.BackColor = Color.FromArgb(255, 177, 177, 177); // #b1b1b1

    if (Theme.UseCustomTitleBar)
    {
      this.FormBorderStyle = FormBorderStyle.None; // Hide the native title bar

      // Add custom titlebar (dark color)
      _dwmTitleBar = new DwmTitleBar(this)
      {
        ShowCaption = true,
        TitleBarHeightAdjustment = 0
      };
      _dwmTitleBar.UpdateColors(Color.FromArgb(32, 32, 32), Color.White);

      // Add content panel last so it's always on top
      this.Controls.Add(_panel);

    }

    this.ResumeLayout(false);
  }

  public void SetError(Exception exception, string label)
  {
    var icon = new PictureBox
    {
      Image = SystemIcons.Error.ToBitmap(),
      Location = new Point(10, 2),
      Size = new Size(32, 32),
      SizeMode = PictureBoxSizeMode.Zoom
    };

    var message = new Label
    {
      Text = label,
      AutoSize = true,
      MaximumSize = new Size(this.ClientSize.Width - 60, 0),
      MinimumSize = new Size(0, 32),
      TextAlign = ContentAlignment.MiddleLeft,
      Location = new Point(50, 2),
    };

    var messagePanel = new Panel
    {
      Location = new Point(10, 10),
      Size = new Size(this.ClientSize.Width - 20,
                      Math.Max(icon.Height, message.Height) + 2),
    };
    messagePanel.Controls.Add(icon);
    messagePanel.Controls.Add(message);
    _panel.Controls.Add(messagePanel);

    var stackTracePanel = new Panel
    {
      Location = new Point(10, messagePanel.Bottom + 10),
      Size = new Size(this.ClientSize.Width - 20, this.ClientSize.Height - messagePanel.Bottom - 50),
      BorderStyle = BorderStyle.FixedSingle,
    };
    _panel.Controls.Add(stackTracePanel);
    stackTracePanel.BringToFront();

    message.SizeChanged += (sender, e) =>
    {
      messagePanel.Size = new Size(this.ClientSize.Width - 20, Math.Max(32, message.Height) + 20);
      stackTracePanel.Location = new Point(10, messagePanel.Bottom + 10);
      stackTracePanel.Size = new Size(this.ClientSize.Width - 20, this.ClientSize.Height - messagePanel.Bottom - 50);
    };

    string exceptionMessage = exception.ToString();
    var stackTrace = new TextBox
    {
      Text = exceptionMessage,
      Font = new Font("Consolas", 10),
      SelectionStart = exceptionMessage.Length,
      ForeColor = Color.White,
      BackColor = Theme.Background_Dark,
      Multiline = true,
      ScrollBars = ScrollBars.Vertical,
      Dock = DockStyle.Fill,
    };

    stackTracePanel.Controls.Add(stackTrace);

    var closeButton = new Button
    {
      Text = "Close",
      Location = new Point(this.ClientSize.Width - 110, this.ClientSize.Height - 35),
      Size = new Size(100, 30),
      BackColor = Color.White
    };
    closeButton.Click += (sender, e) => this.Close();
    _panel.Controls.Add(closeButton);

    // Set the non-hovered border color to grey
    closeButton.FlatAppearance.BorderColor = Color.FromArgb(255, 200, 200, 200);

    var copyButton = new Button
    {
      Text = "Copy",
      Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 35),
      Size = new Size(100, 30),
      BackColor = Color.White
    };
    copyButton.Click += (sender, e) =>
    {
      Thread thread = new(() =>
      {
        Clipboard.Clear();
        Clipboard.SetDataObject(exceptionMessage, true);
      });
      thread.SetApartmentState(ApartmentState.STA);
      thread.Start();
      thread.Join();
    };
    _panel.Controls.Add(copyButton);

    bool isCopied = false;
    var copyIconButton = new Button
    {
      Size = new Size(28, 28),
      Location = new Point(stackTracePanel.Width - 56, 8),
      Anchor = AnchorStyles.Top | AnchorStyles.Right,
      BackColor = Color.FromArgb(45, 45, 52),
      FlatStyle = FlatStyle.Flat,
      Cursor = Cursors.Hand
    };
    copyIconButton.FlatAppearance.BorderSize = 0;
    copyIconButton.MouseEnter += (s, e) => copyIconButton.BackColor = Color.FromArgb(60, 60, 68);
    copyIconButton.MouseLeave += (s, e) => copyIconButton.BackColor = Color.FromArgb(45, 45, 52);

    copyIconButton.Paint += (s, e) =>
    {
      var g = e.Graphics;
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

      using var bgBrush = new SolidBrush(copyIconButton.BackColor);
      g.FillRectangle(bgBrush, copyIconButton.ClientRectangle);

      if (isCopied)
      {
        using var checkPen = new Pen(Color.FromArgb(52, 211, 153), 2f)
        {
          StartCap = System.Drawing.Drawing2D.LineCap.Round,
          EndCap = System.Drawing.Drawing2D.LineCap.Round,
          LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        g.DrawLines(checkPen, new[]
        {
          new PointF(8f, 14f),
          new PointF(12f, 18f),
          new PointF(20f, 10f)
        });
      }
      else
      {
        using var iconPen = new Pen(Color.FromArgb(200, 200, 210), 1.6f)
        {
          LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };

        using var backPath = GetRoundedRectPath(new RectangleF(11.5f, 11.5f, 9.5f, 9.5f), 2.5f);
        g.DrawPath(iconPen, backPath);

        using var frontPath = GetRoundedRectPath(new RectangleF(7f, 7f, 9.5f, 9.5f), 2.5f);
        g.FillPath(bgBrush, frontPath);
        g.DrawPath(iconPen, frontPath);
      }
    };

    copyIconButton.Click += (s, e) =>
    {
      try
      {
        Clipboard.SetText(exceptionMessage);
        isCopied = true;
        copyIconButton.Invalidate();
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (st, et) =>
        {
          isCopied = false;
          copyIconButton.Invalidate();
          timer.Stop();
          timer.Dispose();
        };
        timer.Start();
      }
      catch { }
    };

    stackTracePanel.Controls.Add(copyIconButton);
    copyIconButton.BringToFront();

    this.Resize += (sender, e) =>
    {
      messagePanel.Size = new Size(this.ClientSize.Width - 20, message.Height + 20);
      stackTracePanel.Size = new Size(this.ClientSize.Width - 20, this.ClientSize.Height - messagePanel.Bottom - 50);
      copyIconButton.Location = new Point(stackTracePanel.Width - 56, 8);
      closeButton.Location = new Point(this.ClientSize.Width - 110, this.ClientSize.Height - 35);
      copyButton.Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 35);
    };
  }

  private static System.Drawing.Drawing2D.GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
  {
    var path = new System.Drawing.Drawing2D.GraphicsPath();
    float diameter = radius * 2;
    var size = new SizeF(diameter, diameter);
    var arc = new RectangleF(rect.Location, size);

    path.AddArc(arc, 180, 90);
    arc.X = rect.Right - diameter;
    path.AddArc(arc, 270, 90);
    arc.Y = rect.Bottom - diameter;
    path.AddArc(arc, 0, 90);
    arc.X = rect.Left;
    path.AddArc(arc, 90, 90);
    path.CloseFigure();

    return path;
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);
    if (Theme.UseCustomTitleBar && _dwmTitleBar != null)
    {
      _dwmTitleBar.PaintTitleBarInClientArea(e.Graphics);
    }
  }

  protected override void WndProc(ref Message m)
  {
    if (Theme.UseCustomTitleBar && _dwmTitleBar?.HandleMessage(ref m) == true)
    {
      return;
    }
    base.WndProc(ref m);
  }

  protected override CreateParams CreateParams
  {
    get
    {
      var cp = base.CreateParams;
      if (Theme.UseCustomTitleBar)
      {
        // Add WS_THICKFRAME, WS_MINIMIZEBOX, WS_MAXIMIZEBOX, WS_SYSMENU
        // for resizing/snap, but NOT WS_CAPTION to avoid native title bar.
        const int WS_THICKFRAME  = 0x00040000;
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_MAXIMIZEBOX = 0x00010000;
        const int WS_SYSMENU     = 0x00080000;
        cp.Style |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
        // Do NOT add WS_CAPTION (0x00C00000)
      }
      return cp;
    }
  }
}
