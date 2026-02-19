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
  private const int SIZE_RESTORED = 0;
  private const int SIZE_MAXIMIZED = 2;
  private const int WM_GETMINMAXINFO = 0x0024;
  private const int HTCAPTION = 2;
  private const int HTCLIENT = 1;
  private const int HTMINBUTTON = 8;
  private const int HTMAXBUTTON = 9;
  private const int HTCLOSE = 20;

  [StructLayout(LayoutKind.Sequential)]
  public struct POINT
  {
    public int x;
    public int y;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct MINMAXINFO
  {
    public POINT ptReserved;
    public POINT ptMaxSize;
    public POINT ptMaxPosition;
    public POINT ptMinTrackSize;
    public POINT ptMaxTrackSize;
  }

  private const int GWL_STYLE = -16;
  private const int WS_MAXIMIZE = 0x01000000;
  private const int WS_THICKFRAME = 0x00040000;
  private const int WS_CAPTION = 0x00C00000;
  private const int MONITOR_DEFAULTTONEAREST = 0x00000002;
  private const int SWP_NOSIZE = 0x0001;
  private const int SWP_NOMOVE = 0x0002;
  private const int SWP_NOZORDER = 0x0004;
  private const int SWP_NOACTIVATE = 0x0010;
  private const int SWP_FRAMECHANGED = 0x0020;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll")]
  private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

  [DllImport("user32.dll")]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

  [DllImport("user32.dll")]
  private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

  [DllImport("user32.dll")]
  private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

  [DllImport("user32.dll")]
  private static extern bool RedrawWindow(
    IntPtr hWnd,
    IntPtr lprcUpdate,
    IntPtr hrgnUpdate,
    uint flags
  );

  [DllImport("user32.dll")]
  private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

  [DllImport("user32.dll")]
  private static extern uint GetDpiForWindow(IntPtr hwnd);

  private const int SM_CXFRAME = 32;
  private const int SM_CXPADDEDBORDER = 92;

  [DllImport("dwmapi.dll")]
  private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

  [StructLayout(LayoutKind.Sequential)]
  public struct MARGINS
  {
    public int cxLeftWidth;
    public int cxRightWidth;
    public int cyTopHeight;
    public int cyBottomHeight;
  }

  private readonly Form _hostForm;

  // Dynamic colors that can be updated
  private Color _currentTitleBarColor;
  private Color _currentTextColor;

  public void EnableDwmDropShadow(bool maximized = false)
  {
    var margins = maximized 
      ? new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 }
      : new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
      
    DwmExtendFrameIntoClientArea(_hostForm.Handle, ref margins);
  }

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

    // Extend frame into client area to fix window border artifacts
    EnableDwmDropShadow();
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
      case WM_SIZE:
        {
          // When maximizing, remove the WS_THICKFRAME and WS_CAPTION styles to achieve a
          // truly borderless window. This prevents the OS from drawing grey borders around
          // the edges.
          // ALSO, we must update DWM margins to 0 to prevent a thin grey bar at the top.
          
          int wParam = m.WParam.ToInt32();
          if (wParam == SIZE_MAXIMIZED)
          {
            int style = GetWindowLong(_hostForm.Handle, GWL_STYLE);
            style &= ~(WS_THICKFRAME | WS_CAPTION);
            SetWindowLong(_hostForm.Handle, GWL_STYLE, style);
            
            EnableDwmDropShadow(true); // Margins = 0

            // Force style update
            SetWindowPos(_hostForm.Handle, IntPtr.Zero, 0, 0, 0, 0,
              SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            
            _hostForm.Padding = new Padding(0);
          }
          else if (wParam == SIZE_RESTORED)
          {
            int style = GetWindowLong(_hostForm.Handle, GWL_STYLE);
            style |= (WS_THICKFRAME | WS_CAPTION);
            SetWindowLong(_hostForm.Handle, GWL_STYLE, style);
            
            EnableDwmDropShadow(false); // Margins = 0,0,1,0

            // Force style update
            SetWindowPos(_hostForm.Handle, IntPtr.Zero, 0, 0, 0, 0,
              SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
              
             _hostForm.Padding = new Padding(0);
          }
        
          _hostForm.Invalidate();
          return false;
        }

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
          var nccsp = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));

          // When maximized, the window bounds are inherently expanded by the system to conceal the sizeable border behind the screen edges.
          // Because we don't draw a border, our client area (which is the whole window) gets pushed off-screen.
          // We must shrink the client area rect back to the monitor bounds.
          if (_hostForm.WindowState == FormWindowState.Maximized)
          {
            var hMonitor = MonitorFromWindow(_hostForm.Handle, MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                // Shrink the rect to precisely the monitor's work area
                nccsp.rect0.Left = monitorInfo.rcWork.Left;
                nccsp.rect0.Top = monitorInfo.rcWork.Top;
                nccsp.rect0.Right = monitorInfo.rcWork.Right;
                nccsp.rect0.Bottom = monitorInfo.rcWork.Bottom;
                
                Marshal.StructureToPtr(nccsp, m.LParam, false);
            }
          }

          m.Result = IntPtr.Zero;
          return true;
        }
        return false;

      case WM_GETMINMAXINFO:
        {
          // Constrain the window's maximized size to the monitor's work area.
          // This prevents the window from extending underneath/over the taskbar.

          var hMonitor = MonitorFromWindow(_hostForm.Handle, MONITOR_DEFAULTTONEAREST);
          var monitorInfo = new MONITORINFO();
          monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

          if (GetMonitorInfo(hMonitor, ref monitorInfo))
          {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
            
            // Set the maximized position relative to the monitor bounds (no inflation)
            mmi.ptMaxPosition.x = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
            mmi.ptMaxPosition.y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
            
            // Set the maximized size strictly to the Work Area size
            mmi.ptMaxSize.x = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
            mmi.ptMaxSize.y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;

            // Track Size must also match
            mmi.ptMaxTrackSize = mmi.ptMaxSize;

            Marshal.StructureToPtr(mmi, m.LParam, true);
          }
          
          m.Result = IntPtr.Zero;
          return true;
        }

      case WM_WINDOWPOSCHANGED:
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

  // Removed GetPaddedBorderThickness and UpdateFormPadding as they are no longer needed.



  public void PaintTitleBarInClientArea(Graphics graphics)
  {
    // Extend title bar to include padding area (0 padding now)
    var titleBarRect = new Rectangle(0, 0, _hostForm.ClientSize.Width, EffectiveCaptionHeight);

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
      var textLeftMargin = 8;
      var textRect = new Rectangle(textLeftMargin, 0, titleBarRect.Width - 120, EffectiveCaptionHeight);
      using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
      {
        TextRenderer.DrawText(graphics, _hostForm.Text, font, textRect, foregroundColor,
          TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
      }
    }

    // Draw window control buttons
    DrawWindowButtons(graphics, titleBarRect, backgroundColor, foregroundColor);
  }

  private void DrawWindowButtons(Graphics graphics, Rectangle titleBarRect, Color backgroundColor, Color foregroundColor)
  {
    var buttonWidth = 32;
    var buttonHeight = EffectiveCaptionHeight;
    var buttonY = 0;

    // Close button
    var closeRect = new Rectangle(titleBarRect.Width - buttonWidth, buttonY, buttonWidth, buttonHeight);
    var closeBackColor = _currentHover == HoveredButton.Close ? Color.FromArgb(255, 0, 0) : backgroundColor;
    DrawButton(graphics, closeRect, "ｘ", closeBackColor, foregroundColor, TitleBarButtonStyle.Close);

    // Maximize button
    var maxRect = new Rectangle(titleBarRect.Width - (buttonWidth * 2), buttonY, buttonWidth, buttonHeight);
    var maxText = _hostForm.WindowState == FormWindowState.Maximized ? "⧉" : "□";
    var maxBackColor = _currentHover == HoveredButton.Maximize ? Color.FromArgb(60, 60, 60) : backgroundColor;
    DrawButton(graphics, maxRect, maxText, maxBackColor, foregroundColor, TitleBarButtonStyle.Maximize);

    // Minimize button
    var minRect = new Rectangle(titleBarRect.Width - (buttonWidth * 3), buttonY, buttonWidth, buttonHeight);
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
    var titleBarHeight = EffectiveCaptionHeight;

    if (relativePos.Y < 0 || relativePos.Y > titleBarHeight) return false;

    var buttonWidth = 32;
    // Calculate the logical window width
    var windowWidth = windowRect.Right - windowRect.Left;

    // Check which button was clicked
    if (relativePos.X >= windowWidth - buttonWidth &&
        relativePos.X < windowWidth)
    {
      // Close button
      _hostForm.Close();
      return true;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 2) &&
             relativePos.X < windowWidth - buttonWidth)
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
    else if (relativePos.X >= windowWidth - (buttonWidth * 3) &&
             relativePos.X < windowWidth - (buttonWidth * 2))
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
    var titleBarHeight = SystemInformation.CaptionHeight;

    if (relativePos.Y < 0 || relativePos.Y > titleBarHeight)
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

    // Determine which button is being hovered
    if (relativePos.X >= windowWidth - buttonWidth &&
        relativePos.X < windowWidth)
    {
      newHover = HoveredButton.Close;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 2) &&
             relativePos.X < windowWidth - buttonWidth)
    {
      newHover = HoveredButton.Maximize;
    }
    else if (relativePos.X >= windowWidth - (buttonWidth * 3) &&
             relativePos.X < windowWidth - (buttonWidth * 2))
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
      var titleBarHeight = SystemInformation.CaptionHeight;
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
        var xCheck = clientPoint.X;

        if (xCheck >= windowWidth - buttonWidth)
        {
          m.Result = (IntPtr) HTCLOSE;
          return true;
        }
        if (xCheck >= windowWidth - (buttonWidth * 2))
        {
          m.Result = (IntPtr) HTMAXBUTTON;
          return true;
        }
        if (xCheck >= windowWidth - (buttonWidth * 3))
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

  [StructLayout(LayoutKind.Sequential)]
  public struct MONITORINFO
  {
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
  }
}
