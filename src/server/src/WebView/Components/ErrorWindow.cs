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
  // private TitleBarComponent _titleBar = null!;

  public Size? RestoreSize { get; set; }

  public ErrorWindow(Exception exception, string label)
  {
    this.InitializeComponent();
    this.SetError(exception, label);
  }

  private void InitializeComponent()
  {
    this.SuspendLayout();

    // Create a panel that spans the entire form.
    this._panel = new Panel();
    this._panel.Dock = DockStyle.Fill;
    this.Controls.Add(_panel);

    this.AutoScaleDimensions = new SizeF(8F, 20F);
    this.AutoScaleMode = AutoScaleMode.Font;
    this.StartPosition = FormStartPosition.CenterScreen;
    this.ClientSize = new Size(600, 400);
    this.Name = "ErrorWindow";
    this.Text = Application.ProductName;
    this.BackColor = Color.FromArgb(255, 177, 177, 177); // #b1b1b1

    if (Theme.UseCustomTitleBar)
    {
      this._panel.Location = new Point(0, 30);
      this._panel.Size = new Size(this.Size.Width, this.Size.Height - 30);

      // this._titleBar = new TitleBarComponent(this, parent: this._panel);//, false);
      this.FormBorderStyle = FormBorderStyle.None; // Hide the native title bar
      // this.Controls.Add(this._titleBar);
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
    };

    var message = new Label
    {
      Text = label,
      AutoSize = true,
      Location = new Point(50, 10),
      MaximumSize = new Size(this.ClientSize.Width - 60, 0),
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

    message.SizeChanged += (sender, e) =>
    {
      messagePanel.Size = new Size(this.ClientSize.Width - 20, message.Height + 20);
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
}
