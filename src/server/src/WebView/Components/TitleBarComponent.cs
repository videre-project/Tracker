/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace Tracker.WebView.Components;

public class TitleBarComponent : Panel
{
  public Button closeButton;
  public Button minimizeButton;
  public Button maximizeButton;

  public bool DoLayoutOnResize { get; set; } = false;

  private bool _dragging = false;
  private Point _dragCursorPoint;
  private Point _dragFormPoint;
  private readonly Form _hostForm;
  private readonly Control _parent;

#pragma warning disable CS8618
  public TitleBarComponent(Form hostForm, Control parent, bool allowResize = true)
  {
    this._hostForm = hostForm;
    this._parent = parent;
    this.InitializeComponent();
    if (allowResize)
      this.AddResizeTriangle(parent);
  }
#pragma warning restore CS8618

  private void InitializeComponent()
  {
    //
    // TitleBarComponent
    //
    this.BackColor = Color.FromArgb(45, 45, 48);
    this.Dock = DockStyle.Top;
    this.Height = 30;
    this.MouseDown += new MouseEventHandler(this.TitleBarComponent_MouseDown);
    this.MouseMove += new MouseEventHandler(this.TitleBarComponent_MouseMove);
    this.MouseUp += new MouseEventHandler(this.TitleBarComponent_MouseUp);

    // Configure double-click events to maximize/restore the window size.
    this.DoubleClick += (sender, e) =>
    {
      if (_hostForm.WindowState == FormWindowState.Maximized)
      {
        _hostForm.WindowState = FormWindowState.Normal;
      }
      else
      {
        ((IResizableForm)_hostForm).RestoreSize ??= _hostForm.Size;
        _hostForm.WindowState = FormWindowState.Maximized;
      }
    };

    this.Controls.Add(new Label
    {
      AutoSize = true,
      ForeColor = Color.White,
      Font = new Font("Segoe UI", 9, FontStyle.Bold),
      Location = new Point(10, 5),
      Text = _hostForm.Text
    });

    //
    // TitleBarButton
    //
    this.minimizeButton = new TitleBarButton(TitleBarButtonStyle.Minimize);
    this.minimizeButton.Click += new EventHandler(this.MinimizeButton_Click);
    this.Controls.Add(this.minimizeButton);

    this.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 2 });

    this.maximizeButton = new TitleBarButton(TitleBarButtonStyle.Maximize);
    this.maximizeButton.Click += new EventHandler(this.MaximizeButton_Click);
    this.Controls.Add(this.maximizeButton);

    this.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 2 });

    this.closeButton = new TitleBarButton(TitleBarButtonStyle.Close);
    this.closeButton.Click += new EventHandler(this.CloseButton_Click);
    this.Controls.Add(this.closeButton);
  }

  #region Native Methods

  [DllImport("user32.dll")]
  private static extern bool SetWindowPos(
    IntPtr hWnd,
    IntPtr hWndInsertAfter,
    int X,
    int Y,
    int cx,
    int cy,
    uint uFlags
  );

  private static readonly IntPtr HWND_TOP = IntPtr.Zero;
  private const uint SWP_SHOWWINDOW = 0x0040;

  #endregion

  #region Window Dragging

  private void TitleBarComponent_MouseDown(object? sender, MouseEventArgs e)
  {
    _dragging = true;
    _dragCursorPoint = Cursor.Position;
    _dragFormPoint = _hostForm.Location;
    ((IResizableForm)_hostForm).RestoreSize ??= _hostForm.Size;
  }

  private void TitleBarComponent_MouseMove(object? sender, MouseEventArgs e)
  {
    if (_dragging)
    {
      Point diff = Point.Subtract(Cursor.Position, new Size(_dragCursorPoint));
      _hostForm.Location = Point.Add(_dragFormPoint, new Size(diff));
      SnapWindow();
    }
  }

  private void TitleBarComponent_MouseUp(object? sender, MouseEventArgs e)
  {
    _dragging = false;
  }

  #endregion

  #region Window Snapping

  private void SetWindowPosition(int x, int y, int width, int height)
  {
    // Disable dragging of the TitleBarComponent to prevent ghosting artifacts.
    _dragging = false;
    Task.Delay(500).ContinueWith(_ => _dragging = true);

    if (_hostForm.Location.X == x     && _hostForm.Location.Y == y &&
        _hostForm.Size.Width == width && _hostForm.Size.Height == height)
    {
      return; // Avoid snapping if the size and position is already the same
    }
    SetWindowPos(_hostForm.Handle, HWND_TOP, x, y, width, height, SWP_SHOWWINDOW);
  }

  private void SnapWindow()
  {
    var screen = Screen.FromControl(_hostForm);
    var cursorPosition = Cursor.Position;
    int cornerBuffer = 20; // Increased buffer for corner detection.
    int edgeBuffer = 40; // Buffer for edge detection to prevent corner overlap.
    int centerBuffer = 100; // Additional buffer for center detection.

    if (cursorPosition.X >= screen.WorkingArea.Left &&
        cursorPosition.X <= screen.WorkingArea.Left + cornerBuffer)
    {
      if (cursorPosition.Y >= screen.WorkingArea.Top &&
          cursorPosition.Y <= screen.WorkingArea.Top + cornerBuffer)
      {
        // Snap to top-left corner
        SetWindowPosition(
          screen.WorkingArea.Left,
          screen.WorkingArea.Top,
          screen.WorkingArea.Width / 2,
          screen.WorkingArea.Height / 2
        );
      }
      else if (cursorPosition.Y <= screen.WorkingArea.Bottom &&
               cursorPosition.Y >= screen.WorkingArea.Bottom - cornerBuffer)
      {
        // Snap to bottom-left corner
        SetWindowPosition(
          screen.WorkingArea.Left,
          screen.WorkingArea.Bottom - screen.WorkingArea.Height / 2,
          screen.WorkingArea.Width / 2,
          screen.WorkingArea.Height / 2
        );
      }
      else if (cursorPosition.Y > screen.WorkingArea.Top + cornerBuffer &&
               cursorPosition.Y < screen.WorkingArea.Bottom - cornerBuffer)
      {
        // Snap to left
        SetWindowPosition(
          screen.WorkingArea.Left,
          screen.WorkingArea.Top,
          screen.WorkingArea.Width / 2,
          screen.WorkingArea.Height
        );
      }
    }
    else if (cursorPosition.X <= screen.WorkingArea.Right &&
             cursorPosition.X >= screen.WorkingArea.Right - cornerBuffer)
    {
      if (cursorPosition.Y >= screen.WorkingArea.Top &&
          cursorPosition.Y <= screen.WorkingArea.Top + cornerBuffer)
      {
        // Snap to top-right corner
        SetWindowPosition(
          screen.WorkingArea.Right - screen.WorkingArea.Width / 2,
          screen.WorkingArea.Top,
          screen.WorkingArea.Width / 2,
          screen.WorkingArea.Height / 2
        );
      }
      else if (cursorPosition.Y <= screen.WorkingArea.Bottom &&
               cursorPosition.Y >= screen.WorkingArea.Bottom - cornerBuffer)
      {
        // Snap to bottom-right corner
        SetWindowPosition(
          screen.WorkingArea.Right - screen.WorkingArea.Width / 2,
          screen.WorkingArea.Bottom - screen.WorkingArea.Height / 2,
          screen.WorkingArea.Width / 2,
          screen.WorkingArea.Height / 2
        );
      }
      else if (cursorPosition.Y > screen.WorkingArea.Top + cornerBuffer &&
               cursorPosition.Y < screen.WorkingArea.Bottom - cornerBuffer)
      {
        // Snap to right
        SetWindowPosition(
          screen.WorkingArea.Right - screen.WorkingArea.Width / 2,
          screen.WorkingArea.Top,
          screen.WorkingArea.Width / 2,
          screen.WorkingArea.Height
        );
      }
    }
    else if (cursorPosition.Y >= screen.WorkingArea.Top &&
             cursorPosition.Y <= screen.WorkingArea.Top + edgeBuffer &&
             cursorPosition.X > screen.WorkingArea.Left + cornerBuffer &&
             cursorPosition.X < screen.WorkingArea.Right - cornerBuffer)
    {
      // Snap to top
      SetWindowPosition(
        screen.WorkingArea.Left,
        screen.WorkingArea.Top,
        screen.WorkingArea.Width,
        screen.WorkingArea.Height
      );
    }
    else if (cursorPosition.Y <= screen.WorkingArea.Bottom &&
             cursorPosition.Y >= screen.WorkingArea.Bottom - edgeBuffer &&
             cursorPosition.X > screen.WorkingArea.Left + cornerBuffer &&
             cursorPosition.X < screen.WorkingArea.Right - cornerBuffer)
    {
      // Snap to bottom
      SetWindowPosition(
        screen.WorkingArea.Left,
        screen.WorkingArea.Top,
        screen.WorkingArea.Width,
        screen.WorkingArea.Height
      );
    }
    else if (cursorPosition.X > screen.WorkingArea.Left + centerBuffer &&
             cursorPosition.X < screen.WorkingArea.Right - centerBuffer &&
             cursorPosition.Y > screen.WorkingArea.Top + centerBuffer &&
             cursorPosition.Y < screen.WorkingArea.Bottom - centerBuffer)
    {
      // Reset window size when dragging towards the center
      _hostForm.WindowState = FormWindowState.Normal;
      // Reset to the 'DefaultSize' when the window is not maximized
      _hostForm.Size = (Size)((IResizableForm)_hostForm).RestoreSize!;
    }
  }

  #endregion

  #region Window Resizing

  private int _formWidth => _hostForm.ClientSize.Width;
  private int _formHeight => _hostForm.ClientSize.Height;

  private class TransparentPanel : Panel
  {
    protected override CreateParams CreateParams
    {
      get
      {
        CreateParams cp =  base.CreateParams;
        cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
        return cp;
      }
    }
  }

  private void AddResizeTriangle(Control parent)
  {
    var width = (int)(1.1 * this.Height);
    var resizeTriangle = new TransparentPanel
    {
      Size = new Size(width, width),
      Cursor = Cursors.SizeNWSE,
      Location = new Point(_formWidth - width, _formHeight - width),
      Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
    };

    resizeTriangle.MouseEnter += ResizeTriangle_MouseEnter;
    resizeTriangle.MouseDown += ResizeTriangle_MouseDown;
    resizeTriangle.MouseMove += ResizeTriangle_MouseMove;
    resizeTriangle.MouseUp += ResizeTriangle_MouseUp;

    _hostForm.Controls.Add(resizeTriangle);
    resizeTriangle.BringToFront();

    _hostForm.SizeChanged += (s, e) =>
    {
      // Cut a triangle from the bottom-right corner of the webview
      GraphicsPath path = new GraphicsPath();
      path.AddPolygon(new Point[]
      {
        new Point(_formWidth, _formHeight),
        new Point(_formWidth, _formHeight - width),
        new Point(_formWidth - width, _formHeight)
      });
      Region region = new Region(new Rectangle(0, 0, _formWidth, _formHeight));
      region.Exclude(path);
      parent.Region = region;
    };
  }

  private bool _resizing = false;
  private Point _resizeCursorPoint;
  private Size _resizeFormSize;

  private void ResizeTriangle_MouseEnter(object? sender, EventArgs e)
  {
    Cursor.Current = Cursors.SizeNWSE;
  }

  private void ResizeTriangle_MouseDown(object? sender, MouseEventArgs e)
  {
    // _hostForm.TransparencyKey = _hostForm.BackColor;
    if (!DoLayoutOnResize) _hostForm.SuspendLayout();
    _resizing = true;
    _resizeCursorPoint = Cursor.Position;
    _resizeFormSize = _hostForm.Size;
    Cursor.Current = Cursors.SizeNWSE;
  }

  private void ResizeTriangle_MouseMove(object? sender, MouseEventArgs e)
  {
    if (_resizing)
    {
      Point diff = Point.Subtract(Cursor.Position, new Size(_resizeCursorPoint));

      int minWidth = _hostForm.MinimumSize.Width;
      int minHeight = _hostForm.MinimumSize.Height;
      int newWidth = _resizeFormSize.Width + diff.X;
      int newHeight = _resizeFormSize.Height + diff.Y;

      _hostForm.Size = new Size(newWidth < minWidth ? minWidth : newWidth,
                                newHeight < minHeight ? minHeight : newHeight);
      ((IResizableForm)_hostForm).RestoreSize = _hostForm.Size;
    }
  }

  private void ResizeTriangle_MouseUp(object? sender, MouseEventArgs e)
  {
    // _hostForm.TransparencyKey = Color.Empty;
    if (!DoLayoutOnResize) _hostForm.ResumeLayout();
    _resizing = false;
    Cursor.Current = Cursors.Default;
  }

  #endregion

  #region Window Buttons

  private void CloseButton_Click(object? sender, EventArgs e)
  {
    _hostForm.Close();
  }

  private void MinimizeButton_Click(object? sender, EventArgs e)
  {
    _hostForm.WindowState = FormWindowState.Minimized;
  }

  private void MaximizeButton_Click(object? sender, EventArgs e)
  {
    if (_hostForm.WindowState == FormWindowState.Maximized)
    {
      _hostForm.WindowState = FormWindowState.Normal;
    }
    else
    {
      _hostForm.WindowState = FormWindowState.Maximized;
    }
  }

  #endregion
}
