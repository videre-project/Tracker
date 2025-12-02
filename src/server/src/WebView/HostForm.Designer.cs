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

partial class HostForm : IResizableForm
{
  /// <summary>
  /// Required designer variable.
  /// </summary>
  private IContainer components = null;

  private WebView2 webView21;

  public Size? RestoreSize { get; set; }

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
  private void InitializeComponent()
  {
    this.webView21 = new WebView2();
    ((ISupportInitialize)(this.webView21)).BeginInit();
    this.SuspendLayout();

    //
    // webView21
    //
    this.webView21.CreationProperties = null;
    this.webView21.Dock = DockStyle.None;
    this.webView21.Location = new Point(0, SystemInformation.CaptionHeight);
    this.webView21.Name = "webView21";
    this.webView21.Size = new Size(1280, 768 - SystemInformation.CaptionHeight);
    this.webView21.Source = null;
    this.webView21.TabIndex = 0;
    this.webView21.ZoomFactor = 1D;
    this.webView21.SetTheme();
    this.Controls.Add(this.webView21);

    //
    // HostForm
    //
    this.AutoScaleDimensions = new SizeF(8F, 20F);
    this.AutoScaleMode = AutoScaleMode.Font;
    this.StartPosition = FormStartPosition.CenterScreen;
    this.ClientSize = new Size(1550, 925);
    this.MinimumSize = new Size(1550, 925); // This matches MTGO's minimum size.
    this.Name = "HostForm";
    this.Text = Application.ProductName;

    this.SetTheme();

    ((ISupportInitialize)(this.webView21)).EndInit();
    this.ResumeLayout(false);
  }

  #endregion
}
