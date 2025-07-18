/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Tracker.WebView.Components;

public class ResizeHandleOverlay : Control
{
  [DllImport("user32.dll")]
  private static extern bool ReleaseCapture();

  [DllImport("user32.dll")]
  private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

  private const int WM_NCLBUTTONDOWN = 0xA1;
  private const int HT_LEFT = 0xA;
  private const int HT_RIGHT = 0xB;
  private const int HT_TOP = 0xC;
  private const int HT_TOPLEFT = 0xD;
  private const int HT_TOPRIGHT = 0xE;
  private const int HT_BOTTOM = 0xF;
  private const int HT_BOTTOMLEFT = 0x10;
  private const int HT_BOTTOMRIGHT = 0x11;

  private const int ResizeHandleSize = 0; // 0px - no visible resize handles
  private readonly Form _parentForm;

  public ResizeHandleOverlay(Form parentForm)
  {
    _parentForm = parentForm;

    // Optimize for performance and reduce flicker
    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

    this.BackColor = Color.Transparent;
    this.Dock = DockStyle.Fill;

    // Don't participate in tab order
    this.TabStop = false;
  }

  protected override void OnMouseMove(MouseEventArgs e)
  {
    // With 0px handles, never show resize cursors
    if (this.Cursor != Cursors.Default)
    {
      this.Cursor = Cursors.Default;
    }
    base.OnMouseMove(e);
  }

  protected override void OnMouseDown(MouseEventArgs e)
  {
    // With 0px handles, no resize functionality
    // All mouse events pass through to underlying controls
    base.OnMouseDown(e);
  }

  protected override void OnMouseLeave(EventArgs e)
  {
    this.Cursor = Cursors.Default;
    base.OnMouseLeave(e);
  }

  private Cursor GetResizeCursor(Point location)
  {
    var hitTest = GetHitTest(location);

    return hitTest switch
    {
      HT_LEFT or HT_RIGHT => Cursors.SizeWE,
      HT_TOP or HT_BOTTOM => Cursors.SizeNS,
      HT_TOPLEFT or HT_BOTTOMRIGHT => Cursors.SizeNWSE,
      HT_TOPRIGHT or HT_BOTTOMLEFT => Cursors.SizeNESW,
      _ => Cursors.Default
    };
  }

  private int GetHitTest(Point location)
  {
    var bounds = this.ClientRectangle;
    var isInLeftEdge = location.X <= ResizeHandleSize;
    var isInRightEdge = location.X >= bounds.Width - ResizeHandleSize;
    var isInTopEdge = location.Y <= ResizeHandleSize;
    var isInBottomEdge = location.Y >= bounds.Height - ResizeHandleSize;

    // Check corners first (higher priority)
    if (isInLeftEdge && isInTopEdge) return HT_TOPLEFT;
    if (isInRightEdge && isInTopEdge) return HT_TOPRIGHT;
    if (isInLeftEdge && isInBottomEdge) return HT_BOTTOMLEFT;
    if (isInRightEdge && isInBottomEdge) return HT_BOTTOMRIGHT;

    // Check edges
    if (isInLeftEdge) return HT_LEFT;
    if (isInRightEdge) return HT_RIGHT;
    if (isInTopEdge) return HT_TOP;
    if (isInBottomEdge) return HT_BOTTOM;

    return 0; // Not in a resize area
  }

  private void StartResize(int direction)
  {
    ReleaseCapture();
    SendMessage(_parentForm.Handle, WM_NCLBUTTONDOWN, direction, 0);
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    // Don't paint anything - we're transparent
    // This prevents unnecessary redraws
  }

  protected override CreateParams CreateParams
  {
    get
    {
      var cp = base.CreateParams;
      cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
      return cp;
    }
  }
}