/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace Tracker.WebView.Components;

public class DwmTitleBar
{
  /// <summary>
  /// Amount (in pixels) to add to the system caption height.
  /// </summary>
  /// <remarks>
  /// Setting this value to a negative number will reduce the title bar height.
  /// The default of -5 is chosen to create a slightly more compact title bar
  /// that still accommodates the window control buttons and text.
  /// </remarks>
  public int TitleBarHeightAdjustment { get; set; } = -5;

  /// <summary>
  /// The effective caption height after adjustment.
  /// </summary>
  /// <remarks>
  /// Ensures a minimum height of 16 pixels to prevent too-small title bars.
  /// </remarks>
  public int EffectiveCaptionHeight =>
    Math.Max(16, SystemInformation.CaptionHeight + TitleBarHeightAdjustment);

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

  // Dynamic colors that can be updated
  private Color _currentTitleBarColor;
  private Color _currentTextColor;

  /// <summary>
  /// If true, show the window caption (title text) in the titlebar. If false, hide it.
  /// </summary>
  public bool ShowCaption { get; set; } = false;

  // Custom button icon support
  public Image? MinimizeIcon { get; set; }
  public Image? MaximizeIcon { get; set; }
  public Image? RestoreIcon { get; set; }
  public Image? CloseIcon { get; set; }

  //
  // Rendering options for icons
  //

  /// <summary>
  /// Padding (in pixels) to inset the icon inside the button area.
  /// </summary>
  public int ButtonPadding { get; set; } = 6;

  /// <summary>
  /// Scale factor for the button icon size relative to the button area.
  /// </summary>
  /// <remarks>
  /// Value should be between 0.2 and 1.0, where 1.0 is full size (minus padding).
  /// </remarks>
  public float ButtonIconScale { get; set; } = 1.0f;

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
    // Default to a neutral color, but will be updated from client CSS
    _currentTitleBarColor = Color.FromArgb(45, 45, 48);
    _currentTextColor = Color.White;
  }

  /// <summary>
  /// Updates the titlebar colors dynamically. Should be called with the client background color.
  /// </summary>
  public void UpdateColors(Color backgroundColor, Color textColor)
  {
    _currentTitleBarColor = backgroundColor;
    _currentTextColor = textColor;
  }

  public bool HandleMessage(ref Message m)
  {
    switch (m.Msg)
    {
      case WM_NCACTIVATE:
        _isActive = m.WParam.ToInt32() != 0;
        // Only invalidate the title bar area to prevent flashing
        _hostForm.Invalidate(new Rectangle(0, 0, _hostForm.ClientSize.Width, EffectiveCaptionHeight));
        _hostForm.Update();

        // Let the default proc handle the message but return our result
        m.Result = new IntPtr(1); // Return TRUE to indicate we handled it
        return true;

      case WM_NCPAINT:
        // Don't paint anything in non-client area
        m.Result = IntPtr.Zero;
        return true;

      case WM_NCCALCSIZE:
        if (m.WParam.ToInt32() == 1)
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
          EffectiveCaptionHeight
        );

        // Use consistent colors regardless of activation state
        var backgroundColor = _currentTitleBarColor;
        var foregroundColor = _currentTextColor;

        // Draw custom title bar background
        using (var brush = new SolidBrush(backgroundColor))
        {
          graphics.FillRectangle(brush, titleBarRect);
        }

        // Draw title text if enabled
        if (ShowCaption)
        {
          using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
          {
            var textRect = new Rectangle(8, 0, titleBarRect.Width - 120, titleBarRect.Height);
            TextRenderer.DrawText(graphics, _hostForm.Text, font, textRect, foregroundColor,
              TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
          }
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
    var titleBarRect = new Rectangle(0, 0, _hostForm.ClientSize.Width, EffectiveCaptionHeight + topPadding);

    // Use consistent colors regardless of activation state
    var backgroundColor = _currentTitleBarColor;
    var foregroundColor = _currentTextColor;

    // Draw custom title bar background (including padding area)
    using (var brush = new SolidBrush(backgroundColor))
    {
      graphics.FillRectangle(brush, titleBarRect);
    }


    // Draw title text if enabled
    if (ShowCaption)
    {
      var textLeftMargin = isMaximized ? 16 : 8;
      var textRect = new Rectangle(textLeftMargin, topPadding, titleBarRect.Width - 120 - rightPadding, EffectiveCaptionHeight);
      using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
      {
        TextRenderer.DrawText(graphics, _hostForm.Text, font, textRect, foregroundColor,
          TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
      }
    }

    // Draw window control buttons
    DrawWindowButtons(graphics, titleBarRect, backgroundColor, foregroundColor, isMaximized, topPadding, rightPadding);
  }

  private void DrawWindowButtons(Graphics graphics, Rectangle titleBarRect, Color backgroundColor, Color foregroundColor, bool isMaximized = false, int topPadding = 0, int rightPadding = 0)
  {
    var buttonWidth = 32;
    var buttonHeight = EffectiveCaptionHeight;
    var buttonY = topPadding;

    // Close button - account for right padding
    var closeRect = new Rectangle(titleBarRect.Width - buttonWidth - rightPadding, buttonY, buttonWidth, buttonHeight);
    var closeBackColor = _currentHover == HoveredButton.Close ? Color.FromArgb(255, 0, 0) : backgroundColor;
    DrawButton(graphics, closeRect, "ｘ", closeBackColor, foregroundColor, TitleBarButtonStyle.Close);

    // Maximize button
    var maxRect = new Rectangle(titleBarRect.Width - (buttonWidth * 2) - rightPadding, buttonY, buttonWidth, buttonHeight);
    var maxText = _hostForm.WindowState == FormWindowState.Maximized ? "⧉" : "□";
    var maxBackColor = _currentHover == HoveredButton.Maximize ? Color.FromArgb(60, 60, 60) : backgroundColor;
    DrawButton(graphics, maxRect, maxText, maxBackColor, foregroundColor, TitleBarButtonStyle.Maximize);

    // Minimize button
    var minRect = new Rectangle(titleBarRect.Width - (buttonWidth * 3) - rightPadding, buttonY, buttonWidth, buttonHeight);
    var minBackColor = _currentHover == HoveredButton.Minimize ? Color.FromArgb(60, 60, 60) : backgroundColor;
    DrawButton(graphics, minRect, "—", minBackColor, foregroundColor, TitleBarButtonStyle.Minimize);
  }

  private void DrawButton(Graphics graphics, Rectangle rect, string text, Color backColor, Color foreColor, TitleBarButtonStyle buttonStyle = TitleBarButtonStyle.Close)
  {
    using (var brush = new SolidBrush(backColor))
    {
      graphics.FillRectangle(brush, rect);
    }

    // If a custom icon is provided, draw it centered; otherwise, fallback to glyph text
    Image? iconToDraw = null;
    if (buttonStyle == TitleBarButtonStyle.Minimize)
      iconToDraw = MinimizeIcon;
    else if (buttonStyle == TitleBarButtonStyle.Maximize)
      iconToDraw = (_hostForm.WindowState == FormWindowState.Maximized ? RestoreIcon : MaximizeIcon) ?? MaximizeIcon;
    else if (buttonStyle == TitleBarButtonStyle.Close)
      iconToDraw = CloseIcon;

    if (iconToDraw != null)
    {
      // Compute target rectangle with padding and scale, preserving aspect ratio
      var padded = Rectangle.Inflate(rect, -ButtonPadding, -ButtonPadding);
      if (padded.Width > 0 && padded.Height > 0)
      {
        float targetSize = Math.Min(padded.Width, padded.Height) * Math.Max(0.2f, Math.Min(1.0f, ButtonIconScale));
        float aspect = (float) iconToDraw.Width / Math.Max(1, iconToDraw.Height);
        SizeF iconSize = aspect >= 1
          ? new SizeF(targetSize, targetSize / aspect)
          : new SizeF(targetSize * aspect, targetSize);
        var drawX = padded.X + (padded.Width - iconSize.Width) / 2f;
        var drawY = padded.Y + (padded.Height - iconSize.Height) / 2f;

        var state = graphics.Save();
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.DrawImage(iconToDraw, drawX, drawY, iconSize.Width, iconSize.Height);
        graphics.Restore(state);
      }
      return;
    }

    // Fallback glyph rendering
    if (buttonStyle == TitleBarButtonStyle.Maximize)
    {
      // Custom drawing for maximize/restore buttons
      DrawCustomMaximizeGlyph(graphics, rect, foreColor, _hostForm.WindowState == FormWindowState.Maximized);
    }
    else if (buttonStyle == TitleBarButtonStyle.Close)
    {
      // Custom drawing for close button
      DrawCustomCloseGlyph(graphics, rect, foreColor);
    }
    else
    {
      using var font = buttonStyle switch
      {
        TitleBarButtonStyle.Minimize => new Font("Courier New", 9.5F, FontStyle.Regular),
        _ => new Font("Courier New", 9.8F, FontStyle.Bold)
      };
      var drawRectText = rect;
      TextRenderer.DrawText(graphics, text, font, drawRectText, foreColor,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
  }

  private void DrawCustomMaximizeGlyph(Graphics graphics, Rectangle rect, Color foreColor, bool isRestore)
  {
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.PixelOffsetMode = PixelOffsetMode.Half;

    // Smaller glyphs: increase vertical padding more to shrink the icons
    int vPad = Math.Max(6, (int) Math.Round(rect.Height * 0.3f));
    int iconSide = Math.Max(1, rect.Height - (vPad * 2));
    int hPad = Math.Max(0, (rect.Width - iconSide) / 2);

    int left = rect.X + hPad;
    int top = rect.Y + vPad;
    int right = left + iconSide;
    int bottom = top + iconSide;

    // Slightly thinner stroke to match smaller icon
    float penW = Math.Max(1.1f, iconSide / 14f);

    using (var pen = new Pen(foreColor, penW))
    {
      pen.StartCap = LineCap.Round;
      pen.EndCap = LineCap.Round;

      if (isRestore)
      {
        // Restore: two overlapping boxes with swapped heights
        // Back box will be taller and positioned lower
        // Front box will be shorter and positioned higher (only top and right sides)
        int offset = Math.Max(2, iconSide / 7);

        // Back box (taller, positioned lower) - draw full rectangle
        int backX = left;
        int backY = top + offset;
        int backW = iconSide - offset;
        int backH = iconSide - offset;

        // Front box (shorter, positioned higher) - only top and right sides
        int frontX = left + offset;
        int frontY = top;
        int frontW = iconSide - offset;
        int frontH = iconSide - offset;

        // Draw back box first (appears behind)
        if (backW > 0 && backH > 0)
        {
          graphics.DrawRectangle(pen, new Rectangle(backX, backY, backW, backH));
        }

        // Draw front box - only top and right sides
        if (frontW > 0 && frontH > 0)
        {
          // Top line
          graphics.DrawLine(pen, frontX, frontY, frontX + frontW, frontY);
          // Right line
          graphics.DrawLine(pen, frontX + frontW, frontY, frontX + frontW, frontY + frontH);
        }
      }
      else
      {
        // Maximize: single square
        var box = new Rectangle(left, top, iconSide, iconSide);
        if (box.Width > 0 && box.Height > 0)
          graphics.DrawRectangle(pen, box);
      }
    }
  }

  private void DrawCustomCloseGlyph(Graphics graphics, Rectangle rect, Color foreColor)
  {
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.PixelOffsetMode = PixelOffsetMode.Half;

    // Smaller glyphs: increase vertical padding to match other buttons
    int vPad = Math.Max(6, (int) Math.Round(rect.Height * 0.3f));
    int iconSide = Math.Max(1, rect.Height - (vPad * 2));
    int hPad = Math.Max(0, (rect.Width - iconSide) / 2);

    int left = rect.X + hPad;
    int top = rect.Y + vPad;
    int right = left + iconSide;
    int bottom = top + iconSide;

    // Slightly thicker stroke for the X to make it more visible
    float penW = Math.Max(1.5f, iconSide / 12f);

    using (var pen = new Pen(foreColor, penW))
    {
      pen.StartCap = LineCap.Round;
      pen.EndCap = LineCap.Round;

      // Draw X: two diagonal lines forming a square cross
      // Top-left to bottom-right
      graphics.DrawLine(pen, left, top, right, bottom);
      // Top-right to bottom-left
      graphics.DrawLine(pen, right, top, left, bottom);
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
    var titleBarHeight = EffectiveCaptionHeight + topPadding;

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

    if (clientPoint.Y <= EffectiveCaptionHeight)
    {
      var buttonWidth = 32;
      var windowWidth = _hostForm.Width;

      if (clientPoint.X >= windowWidth - buttonWidth)
      {
        m.Result = (IntPtr) HTCLOSE;
        return true;
      }
      if (clientPoint.X >= windowWidth - (buttonWidth * 2))
      {
        m.Result = (IntPtr) HTMAXBUTTON;
        return true;
      }
      if (clientPoint.X >= windowWidth - (buttonWidth * 3))
      {
        m.Result = (IntPtr) HTMINBUTTON;
        return true;
      }

      m.Result = (IntPtr) HTCAPTION;
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
