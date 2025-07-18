/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Tracker.WebView.Components;

public enum ResizeDirection
{
  Left,
  Right,
  Top,
  Bottom,
  TopLeft,
  TopRight,
  BottomLeft,
  BottomRight
}

public class InvisibleResizePanel : Panel
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

  private readonly Form _parentForm;
  private readonly ResizeDirection _direction;
  private readonly int _hitTestCode;
  private readonly Cursor _resizeCursor;

  public InvisibleResizePanel(Form parentForm, ResizeDirection direction, int thickness = 6)
  {
    _parentForm = parentForm;
    _direction = direction;

    // Configure the panel
    this.BackColor = Color.Transparent;
    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.DoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
    this.TabStop = false;

    // Set up hit test code and cursor based on direction
    (_hitTestCode, _resizeCursor) = direction switch
    {
      ResizeDirection.Left => (HT_LEFT, Cursors.SizeWE),
      ResizeDirection.Right => (HT_RIGHT, Cursors.SizeWE),
      ResizeDirection.Top => (HT_TOP, Cursors.SizeNS),
      ResizeDirection.Bottom => (HT_BOTTOM, Cursors.SizeNS),
      ResizeDirection.TopLeft => (HT_TOPLEFT, Cursors.SizeNWSE),
      ResizeDirection.TopRight => (HT_TOPRIGHT, Cursors.SizeNESW),
      ResizeDirection.BottomLeft => (HT_BOTTOMLEFT, Cursors.SizeNESW),
      ResizeDirection.BottomRight => (HT_BOTTOMRIGHT, Cursors.SizeNWSE),
      _ => (0, Cursors.Default)
    };

    // Position the panel based on direction
    PositionPanel(thickness);
  }

  private void PositionPanel(int thickness)
  {
    switch (_direction)
    {
      case ResizeDirection.Left:
        this.Dock = DockStyle.Left;
        this.Width = thickness;
        break;
      case ResizeDirection.Right:
        this.Dock = DockStyle.Right;
        this.Width = thickness;
        break;
      case ResizeDirection.Top:
        this.Dock = DockStyle.Top;
        this.Height = thickness;
        break;
      case ResizeDirection.Bottom:
        this.Dock = DockStyle.Bottom;
        this.Height = thickness;
        break;
      case ResizeDirection.TopLeft:
        this.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        this.Size = new Size(thickness * 2, thickness * 2);
        this.Location = new Point(0, 0);
        break;
      case ResizeDirection.TopRight:
        this.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.Size = new Size(thickness * 2, thickness * 2);
        this.Location = new Point(_parentForm.ClientSize.Width - thickness * 2, 0);
        break;
      case ResizeDirection.BottomLeft:
        this.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        this.Size = new Size(thickness * 2, thickness * 2);
        this.Location = new Point(0, _parentForm.ClientSize.Height - thickness * 2);
        break;
      case ResizeDirection.BottomRight:
        this.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        this.Size = new Size(thickness * 2, thickness * 2);
        this.Location = new Point(_parentForm.ClientSize.Width - thickness * 2, _parentForm.ClientSize.Height - thickness * 2);
        break;
    }
  }

  protected override void OnMouseEnter(EventArgs e)
  {
    // Don't show resize cursor if we're over the title bar buttons
    if (!IsOverTitleBarButtons())
    {
      this.Cursor = _resizeCursor;
    }
    base.OnMouseEnter(e);
  }

  protected override void OnMouseMove(MouseEventArgs e)
  {
    // Update cursor based on whether we're over title bar buttons
    if (IsOverTitleBarButtons())
    {
      this.Cursor = Cursors.Default;
    }
    else
    {
      this.Cursor = _resizeCursor;
    }
    base.OnMouseMove(e);
  }

  protected override void OnMouseLeave(EventArgs e)
  {
    this.Cursor = Cursors.Default;
    base.OnMouseLeave(e);
  }

  protected override void OnMouseDown(MouseEventArgs e)
  {
    // Don't resize if we're over title bar buttons
    if (e.Button == MouseButtons.Left && !IsOverTitleBarButtons())
    {
      ReleaseCapture();
      SendMessage(_parentForm.Handle, WM_NCLBUTTONDOWN, _hitTestCode, 0);
    }
    base.OnMouseDown(e);
  }

  private bool IsOverTitleBarButtons()
  {
    // Only check for top-related resize panels (Top, TopRight, Right when near top)
    if (_direction != ResizeDirection.Top && _direction != ResizeDirection.TopRight &&
        _direction != ResizeDirection.Right && _direction != ResizeDirection.TopLeft)
    {
      return false;
    }

    var mousePos = this.PointToClient(Cursor.Position);
    var formMousePos = _parentForm.PointToClient(Cursor.Position);

    // Check if mouse is in title bar area
    var isMaximized = _parentForm.WindowState == FormWindowState.Maximized;
    var topPadding = isMaximized ? 8 : 0;
    var rightPadding = isMaximized ? 8 : 0;
    var titleBarHeight = SystemInformation.CaptionHeight + topPadding;

    if (formMousePos.Y > titleBarHeight) return false;

    // Check if mouse is over button area (rightmost 96px + padding for 3 buttons)
    var buttonAreaWidth = (32 * 3) + rightPadding;
    return formMousePos.X >= _parentForm.ClientSize.Width - buttonAreaWidth;
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    // Don't paint anything - we're invisible
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