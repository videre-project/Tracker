/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

using Microsoft.Web.WebView2.WinForms;

using Tracker.WebView.Components;


namespace Tracker.WebView;

partial class HostForm
{
  /// <summary>
  /// Required designer variable.
  /// </summary>
  private IContainer components = null;

  private WebView2 webView21;
  private TitleBarComponent titleBar;

  public Size? RestoreSize;
  public Color Background_Dark = Color.FromArgb(255, 36, 36, 36); // #242424
  public Color Background_Light = Color.FromArgb(255, 80, 80, 80); // #505050

  /// <summary>
  ///  Clean up any resources being used.
  /// </summary>
  /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
  protected override void Dispose(bool disposing)
  {
    if (disposing && (components != null))
    {
      components.Dispose();
    }
    base.Dispose(disposing);
  }

  #region Windows Form Designer generated code

  /// <summary>
  ///  Required method for Designer support - do not modify
  ///  the contents of this method with the code editor.
  /// </summary>
  private void InitializeComponent(ApplicationOptions options)
  {
    this.webView21 = new WebView2();
    ((ISupportInitialize)(this.webView21)).BeginInit();
    this.SuspendLayout();

    //
    // webView21
    //
    this.webView21.CreationProperties = null;
    this.webView21.Dock = DockStyle.Fill;
    this.webView21.Location = new Point(0, 0);
    this.webView21.Name = "webView21";
    this.webView21.Size = new Size(1280, 768);
    this.webView21.Source = null;
    this.webView21.TabIndex = 0;
    this.webView21.ZoomFactor = 1D;

    this.Controls.Add(this.webView21);
    this.webView21.BackColor = options.IsDarkMode
      ? this.Background_Dark
      : this.Background_Light;

    //
    // HostForm
    //
    this.AutoScaleDimensions = new SizeF(8F, 20F);
    this.AutoScaleMode = AutoScaleMode.Font;
    this.StartPosition = FormStartPosition.CenterScreen;
    this.ClientSize = new Size(1280, 768);
    this.MinimumSize = new Size(800, 600);
    this.Name = "HostForm";
    this.Text = "Videre Tracker";

    if (options.UseCustomTitleBar)
    {
      this.webView21.Location = new Point(0, 30);
      this.webView21.Size = new Size(this.Size.Width, this.Size.Height - 30);

      this.titleBar = new TitleBarComponent(this, parent: this.webView21);
      this.BackColor = Color.FromArgb(255, 80, 80, 80); // #505050
      this.FormBorderStyle = FormBorderStyle.None; // Hide the native title bar
      this.Controls.Add(this.titleBar);
    }

    ((ISupportInitialize)(this.webView21)).EndInit();
    this.ResumeLayout(false);
  }

  #endregion
}
