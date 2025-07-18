/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace Tracker.WebView.Components;

public class DwmTitleBar
{
  [DllImport("user32.dll")]
  private static extern IntPtr GetWindowDC(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

  [DllImport("user32.dll")]
  private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT
  {
    public int Left, Top, Right, Bottom;
  }

  private const int WM_NCPAINT = 0x85;
  private const int WM_NCACTIVATE = 0x86;
  private const int WM_NCLBUTTONDOWN = 0xA1;
  private const int WM_NCHITTEST = 0x84;
  private const int WM_NCMOUSEMOVE = 0xA0;
  private const int WM_NCMOUSELEAVE = 0x2A2;
  private const int WM_NCCALCSIZE = 0x83;
  private const int WM_WINDOWPOSCHANGED = 0x0047;
  private const int WM_SIZE = 0x0005;
  private const int HTCAPTION = 2;
  private const int HTCLIENT = 1;
  private const int HTMINBUTTON = 8;
  private const int HTMAXBUTTON = 9;
  private const int HTCLOSE = 20;

  [DllImport("user32.dll")]
  private static extern bool RedrawWindow(
    IntPtr hWnd,
    IntPtr lprcUpdate,
    IntPtr hrgnUpdate,
    uint flags
  );

  private readonly Form _hostForm;
  private readonly Color _titleBarColor;
  private readonly Color _textColor;
  private readonly Color _hoverColor;
  private readonly Color _closeHoverColor;

  private enum HoveredButton
  {
    None,
    Minimize,
    Maximize,
    Close
  }

  private HoveredButton _currentHover = HoveredButton.None;
  private bool _isActive = true;

  public DwmTitleBar(Form hostForm)
  {
    _hostForm = hostForm;
    _titleBarColor = Color.FromArgb(45, 45, 48);
    _textColor = Color.White;
    _hoverColor = Color.FromArgb(60, 60, 60);
    _closeHoverColor = Color.FromArgb(255, 0, 0);
  }

  public bool HandleMessage(ref Message m)
  {
    switch (m.Msg)
    {
      case WM_NCACTIVATE:
        _isActive = m.WParam.ToInt32() != 0;
        // Only invalidate the title bar area to prevent flashing
        _hostForm.Invalidate(new Rectangle(0, 0, _hostForm.ClientSize.Width, SystemInformation.CaptionHeight));
        _hostForm.Update(); // Force immediate repaint
        // Let the default proc handle the message but return our result
        m.Result = new IntPtr(1); // Return TRUE to indicate we handled it
        return true;

      case WM_NCPAINT:
        // Don't paint anything in non-client area
        m.Result = IntPtr.Zero;
        return true;

      case WM_NCCALCSIZE:
        if (m.WParam.ToInt32() == 1) // TRUE
        {
          // Extend the client area to cover the entire window
          // This removes the default title bar
          m.Result = IntPtr.Zero;
          return true;
        }
        return false;

      case WM_WINDOWPOSCHANGED:
      case WM_SIZE:
        // Let the form handle repainting
        _hostForm.Invalidate();
        return false;

      case WM_NCHITTEST:
        return HandleHitTest(ref m);

      case WM_NCLBUTTONDOWN:
        return HandleTitleBarClick(m);

      case WM_NCMOUSEMOVE:
        return HandleMouseMove(m);

      case WM_NCMOUSELEAVE:
        return HandleMouseLeave();

      default:
        return false;
    }
  }

  private void PaintTitleBar()
  {
    var hdc = GetWindowDC(_hostForm.Handle);
    if (hdc == IntPtr.Zero) return;

    try
    {
      using (var graphics = Graphics.FromHdc(hdc))
      {
        GetWindowRect(_hostForm.Handle, out var windowRect);
        var titleBarRect = new Rectangle(
          0, 0,
          windowRect.Right - windowRect.Left,
          SystemInformation.CaptionHeight
        );

        // Adjust colors based on activation state
        var backgroundColor = _isActive ? _titleBarColor : Color.FromArgb(60, 60, 63);
        var foregroundColor = _isActive ? _textColor : Color.FromArgb(128, 128, 128);

        // Draw custom title bar background
        using (var brush = new SolidBrush(backgroundColor))
        {
          graphics.FillRectangle(brush, titleBarRect);
        }

        // Draw title text
        using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
        {
          var textRect = new Rectangle(8, 0, titleBarRect.Width - 120, titleBarRect.Height);
          TextRenderer.DrawText(graphics, _hostForm.Text, font, textRect, foregroundColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
        }

        // Draw window control buttons
        DrawWindowButtons(graphics, titleBarRect, backgroundColor, foregroundColor);
      }
    }
    finally
    {
      ReleaseDC(_hostForm.Handle, hdc);
    }
  }

  public void PaintTitleBarInClientArea(Graphics graphics)
  {
    // When maximized, add padding to compensate for missing window frame
    var isMaximized = _hostForm.WindowState == FormWindowState.Maximized;
    var topPadding = isMaximized ? 8 : 0;   // 8px top padding when maximized
    var rightPadding = isMaximized ? 8 : 0; // 8px right padding when maximized

    // Extend title bar to include padding area
    var titleBarRect = new Rectangle(0, 0, _hostForm.ClientSize.Width, SystemInformation.CaptionHeight + topPadding);

    // Adjust colors based on activation state
    var backgroundColor = _isActive ? _titleBarColor : Color.FromArgb(60, 60, 63);
    var foregroundColor = _isActive ? _textColor : Color.FromArgb(128, 128, 128);

    // Draw custom title bar background (including padding area)
    using (var brush = new SolidBrush(backgroundColor))
    {
      graphics.FillRectangle(brush, titleBarRect);
    }

    // Adjust text positioning - keep it in the actual title bar area
    var textLeftMargin = isMaximized ? 16 : 8;
    var textRect = new Rectangle(textLeftMargin, topPadding, titleBarRect.Width - 120 - rightPadding, SystemInformation.CaptionHeight);

    // Draw title text
    using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
    {
      TextRenderer.DrawText(graphics, _hostForm.Text, font, textRect, foregroundColor,
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
    }

    // Draw window control buttons
    DrawWindowButtons(graphics, titleBarRect, backgroundColor, foregroundColor, isMaximized, topPadding, rightPadding);
  }

  private void DrawWindowButtons(Graphics graphics, Rectangle titleBarRect, Color backgroundColor, Color foregroundColor, bool isMaximized = false, int topPadding = 0, int rightPadding = 0)
  {
    var buttonWidth = 32;
    var buttonHeight = SystemInformation.CaptionHeight;
    var buttonY = topPadding;

    // Close button - account for right padding
    var closeRect = new Rectangle(titleBarRect.Width - buttonWidth - rightPadding, buttonY, buttonWidth, buttonHeight);
    var closeBackColor = _currentHover == HoveredButton.Close ?
        (_isActive ? _closeHoverColor : Color.FromArgb(180, 0, 0)) :
        backgroundColor;
    DrawButton(graphics, closeRect, "ｘ", closeBackColor, foregroundColor, TitleBarButtonStyle.Close);

    // Maximize button
    var maxRect = new Rectangle(titleBarRect.Width - (buttonWidth * 2) - rightPadding, buttonY, buttonWidth, buttonHeight);
    var maxText = _hostForm.WindowState == FormWindowState.Maximized ? "⧉" : "□";
    var maxBackColor = _currentHover == HoveredButton.Maximize ?
        (_isActive ? _hoverColor : Color.FromArgb(70, 70, 73)) :
        backgroundColor;
    DrawButton(graphics, maxRect, maxText, maxBackColor, foregroundColor, TitleBarButtonStyle.Maximize);

    // Minimize button
    var minRect = new Rectangle(titleBarRect.Width - (buttonWidth * 3) - rightPadding, buttonY, buttonWidth, buttonHeight);
    var minBackColor = _currentHover == HoveredButton.Minimize ?
        (_isActive ? _hoverColor : Color.FromArgb(70, 70, 73)) :
        backgroundColor;
    DrawButton(graphics, minRect, "—", minBackColor, foregroundColor, TitleBarButtonStyle.Minimize);
  }

  private void DrawButton(Graphics graphics, Rectangle rect, string text, Color backColor, Color foreColor, TitleBarButtonStyle buttonStyle = TitleBarButtonStyle.Close)
  {
    using (var brush = new SolidBrush(backColor))
    {
      graphics.FillRectangle(brush, rect);
    }

    Font font = buttonStyle switch
    {
      TitleBarButtonStyle.Minimize => new Font("Courier New", 9.5F, FontStyle.Regular),
      TitleBarButtonStyle.Maximize => new Font("Courier New", 9.5F, FontStyle.Regular),
      TitleBarButtonStyle.Close => new Font("Courier New", 9.8F, FontStyle.Bold),
      _ => new Font("Courier New", 9.8F, FontStyle.Bold)
    };

    using (font)
    {
      // Adjust rectangle for close button to counteract its natural positioning
      var drawRect = rect;
      if (buttonStyle == TitleBarButtonStyle.Close)
      {
        // Move the close button text up slightly to align with other buttons
        drawRect = new Rectangle(rect.X, rect.Y - 2, rect.Width, rect.Height);
      }

      TextRenderer.DrawText(graphics, text, font, drawRect, foreColor,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
  }

  private bool HandleTitleBarClick(Message m)
  {
    // Get click position relative to window
    var clickPos = new Point(m.LParam.ToInt32());
    GetWindowRect(_hostForm.Handle, out var windowRect);
    var relativePos = new Point(clickPos.X - windowRect.Left, clickPos.Y - windowRect.Top);

    // Check if click is in title bar area
    var isMaximized = _hostForm.WindowState == FormWindowState.Maximized;
    var topPadding = isMaximized ? 8 : 0;
    var rightPadding = isMaximized ? 8 : 0;
    var titleBarHeight = SystemInformation.CaptionHeight + topPadding;

    if (relativePos.Y > titleBarHeight) return false;

    var buttonWidth = 32;
    var windowWidth = windowRect.Right - windowRect.Left;

    // Check which button was clicked - account for padding
    if (relativePos.X >= windowWidth - buttonWidth - rightPadding &&
        relativePos.X < windowWidth - rightPadding &&
        relativePos.Y >= topPadding)
    {
      // Close button
      _hostForm.Close();
      return true;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 2) - rightPadding &&
             relativePos.X < windowWidth - buttonWidth - rightPadding &&
             relativePos.Y >= topPadding)
    {
      // Maximize button
      _hostForm.WindowState = _hostForm.WindowState == FormWindowState.Maximized
        ? FormWindowState.Normal
        : FormWindowState.Maximized;

      // Force repaint after state change to clear any highlight artifacts
      _currentHover = HoveredButton.None;
      _hostForm.Invalidate();
      return true;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 3) - rightPadding &&
             relativePos.X < windowWidth - (buttonWidth * 2) - rightPadding &&
             relativePos.Y >= topPadding)
    {
      // Minimize button
      _hostForm.WindowState = FormWindowState.Minimized;
      return true;
    }

    return false;
  }

  private bool HandleMouseMove(Message m)
  {
    // Get mouse position relative to window
    var mousePos = new Point(m.LParam.ToInt32());
    GetWindowRect(_hostForm.Handle, out var windowRect);
    var relativePos = new Point(mousePos.X - windowRect.Left, mousePos.Y - windowRect.Top);

    // Check if mouse is in title bar area
    var isMaximized = _hostForm.WindowState == FormWindowState.Maximized;
    var topPadding = isMaximized ? 8 : 0;
    var rightPadding = isMaximized ? 8 : 0;
    var titleBarHeight = SystemInformation.CaptionHeight + topPadding;

    if (relativePos.Y > titleBarHeight)
    {
      if (_currentHover != HoveredButton.None)
      {
        _currentHover = HoveredButton.None;
        _hostForm.Invalidate(new Rectangle(0, 0, _hostForm.ClientSize.Width, titleBarHeight));
      }
      return false;
    }

    var buttonWidth = 32;
    var windowWidth = windowRect.Right - windowRect.Left;
    var newHover = HoveredButton.None;

    // Determine which button is being hovered - account for padding
    if (relativePos.X >= windowWidth - buttonWidth - rightPadding &&
        relativePos.X < windowWidth - rightPadding &&
        relativePos.Y >= topPadding)
    {
      newHover = HoveredButton.Close;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 2) - rightPadding &&
             relativePos.X < windowWidth - buttonWidth - rightPadding &&
             relativePos.Y >= topPadding)
    {
      newHover = HoveredButton.Maximize;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 3) - rightPadding &&
             relativePos.X < windowWidth - (buttonWidth * 2) - rightPadding &&
             relativePos.Y >= topPadding)
    {
      newHover = HoveredButton.Minimize;
    }

    // If hover state changed, repaint
    if (_currentHover != newHover)
    {
      _currentHover = newHover;
      _hostForm.Invalidate(new Rectangle(0, 0, _hostForm.ClientSize.Width, titleBarHeight));
    }

    return false;
  }

  private bool HandleMouseLeave()
  {
    if (_currentHover != HoveredButton.None)
    {
      _currentHover = HoveredButton.None;
      var isMaximized = _hostForm.WindowState == FormWindowState.Maximized;
      var topPadding = isMaximized ? 8 : 0;
      var titleBarHeight = SystemInformation.CaptionHeight + topPadding;
      _hostForm.Invalidate(new Rectangle(0, 0, _hostForm.ClientSize.Width, titleBarHeight));
    }
    return false;
  }

  private bool HandleHitTest(ref Message m)
  {
    // Let the default procedure handle hit-testing first.
    // This correctly identifies standard parts like resize borders.
    m.Result = DefWindowProc(_hostForm.Handle, m.Msg, m.WParam, m.LParam);
    if (m.Result.ToInt32() != HTCLIENT)
    {
      // If it's not the client area, we don't need to do anything custom.
      return true;
    }

    // If the default proc thinks it's the client area, we need to check
    // if it's actually our custom title bar or its buttons.
    var screenPoint = new Point(m.LParam.ToInt32());
    var clientPoint = _hostForm.PointToClient(screenPoint);

    if (clientPoint.Y <= SystemInformation.CaptionHeight)
    {
      var buttonWidth = 32;
      var windowWidth = _hostForm.Width;

      if (clientPoint.X >= windowWidth - buttonWidth)
      {
        m.Result = (IntPtr)HTCLOSE;
        return true;
      }
      if (clientPoint.X >= windowWidth - (buttonWidth * 2))
      {
        m.Result = (IntPtr)HTMAXBUTTON;
        return true;
      }
      if (clientPoint.X >= windowWidth - (buttonWidth * 3))
      {
        m.Result = (IntPtr)HTMINBUTTON;
        return true;
      }

      m.Result = (IntPtr)HTCAPTION;
      return true;
    }

    // It's genuinely the client area (the WebView).
    return true;
  }

  [DllImport("user32.dll")]
  private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

  [DllImport("user32.dll")]
  private static extern IntPtr DefWindowProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

  [StructLayout(LayoutKind.Sequential)]
  public struct NCCALCSIZE_PARAMS
  {
    public RECT rect0, rect1, rect2;
    public IntPtr lppos;
  }
}