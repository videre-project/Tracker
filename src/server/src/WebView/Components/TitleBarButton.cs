/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System.Drawing;
using System.Windows.Forms;


namespace Tracker.WebView.Components;

public enum TitleBarButtonStyle
{
  Minimize,
  Maximize,
  Close,
}

public class TitleBarButton : Button
{
  private readonly ToolTip _tooltip = new()
  {
    InitialDelay = 1_000, // 1 second delay
    ReshowDelay = 500, // 0.5 second delay
  };

  public TitleBarButtonStyle ButtonStyle { get; set; }

  public TitleBarButton(TitleBarButtonStyle style)
  {
    this.FlatStyle = FlatStyle.Flat;
    this.FlatAppearance.BorderSize = 0;
    this.UseCompatibleTextRendering = true;
    this.TabStop = false;

    this.BackColor = Color.FromArgb(45, 45, 48); // rgb(45, 45, 48)
    this.ForeColor = Color.White;
    this.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 60, 60, 60);
    this.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 0, 0, 0);

    this.Dock = DockStyle.Right;
    this.Width = 30;
    this.Height = 30;

    this.ButtonStyle = style;
    switch (style)
    {
      case TitleBarButtonStyle.Minimize:
        this.Text = "—"; // U+2014
        this.Font = new Font("Courier New", 9.5F, FontStyle.Regular);
        break;
      case TitleBarButtonStyle.Maximize:
        this.Text = "□"; // U+25a1
        this.Font = new Font("Courier New", 9.5F, FontStyle.Regular);
        break;
      case TitleBarButtonStyle.Close:
        this.Text = "ｘ"; // U+ff58
        this.Font = new Font("Courier New", 9.8F, FontStyle.Bold);
        this.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 255, 0, 0);
        break;
    }
    this.TextAlign = ContentAlignment.MiddleCenter;

    // Add a hover tooltip to the button
    _tooltip.SetToolTip(this, style.ToString());
    this.MouseLeave += (sender, e) => _tooltip.Hide(this);
  }

  public new void Dispose()
  {
    _tooltip.Dispose();
    base.Dispose();
  }
}
