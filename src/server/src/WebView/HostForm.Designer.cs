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
  ///  Required designer variable.
  /// </summary>
  private IContainer components = null;
  private TitleBarComponent titleBar;

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
    this.webView21.DefaultBackgroundColor = Color.White;
    this.webView21.Dock = DockStyle.Fill;
    this.webView21.Location = new Point(0, 0);
    this.webView21.Name = "webView21";
    this.webView21.Size = new Size(1280, 768);
    this.webView21.Source = null;
    this.webView21.TabIndex = 0;
    this.webView21.ZoomFactor = 1D;

    //
    // HostForm
    //
    this.AutoScaleDimensions = new SizeF(8F, 20F);
    this.AutoScaleMode = AutoScaleMode.Font;
    this.ClientSize = new Size(1280, 768);
    this.Controls.Add(this.webView21);
    this.Name = "HostForm";
    this.Text = "Videre Tracker";
    // this.ShowIcon = false;

    if (options.UseCustomTitleBar)
    {
      // webView21
      this.webView21.Location = new Point(0, 30); // Move to below the title bar
      this.webView21.Size = new Size(1280, 738); // Adjusted height
      // HostForm
      this.titleBar = new TitleBarComponent(this);
      this.Controls.Add(this.titleBar);
      this.FormBorderStyle = FormBorderStyle.None; // Hide the native title bar
    }

    ((ISupportInitialize)(this.webView21)).EndInit();
    this.ResumeLayout(false);
  }

  #endregion

  private WebView2 webView21;
}
