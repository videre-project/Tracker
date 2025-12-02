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

    this.Resize += (sender, e) =>
    {
      messagePanel.Size = new Size(this.ClientSize.Width - 20, message.Height + 20);
      stackTracePanel.Size = new Size(this.ClientSize.Width - 20, this.ClientSize.Height - messagePanel.Bottom - 50);
      closeButton.Location = new Point(this.ClientSize.Width - 110, this.ClientSize.Height - 35);
      copyButton.Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 35);
    };
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
