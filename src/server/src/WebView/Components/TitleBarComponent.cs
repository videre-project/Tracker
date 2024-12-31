/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Drawing;
using System.Windows.Forms;


namespace Tracker.WebView.Components;

public class TitleBarComponent : Panel
{
  public Button closeButton;
  public Button minimizeButton;
  public Button maximizeButton;

  private bool _dragging = false;
  private Point _dragCursorPoint;
  private Point _dragFormPoint;
  private readonly Form _hostForm;

#pragma warning disable CS8618
  public TitleBarComponent(Form hostForm)
  {
    this._hostForm = hostForm;
    InitializeComponent();
  }
#pragma warning restore CS8618

  private void InitializeComponent()
  {
    this.closeButton = new TitleBarButton(TitleBarButtonStyle.Close);
    this.minimizeButton = new TitleBarButton(TitleBarButtonStyle.Minimize);
    this.maximizeButton = new TitleBarButton(TitleBarButtonStyle.Maximize);

    //
    // TitleBarComponent
    //
    this.BackColor = Color.FromArgb(45, 45, 48);
    this.Dock = DockStyle.Top;
    this.Height = 30;
    this.MouseDown += new MouseEventHandler(this.TitleBarComponent_MouseDown);
    this.MouseMove += new MouseEventHandler(this.TitleBarComponent_MouseMove);
    this.MouseUp += new MouseEventHandler(this.TitleBarComponent_MouseUp);

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
    this.closeButton.Click += new EventHandler(this.CloseButton_Click);
    this.minimizeButton.Click += new EventHandler(this.MinimizeButton_Click);
    this.maximizeButton.Click += new EventHandler(this.MaximizeButton_Click);

    this.Controls.Add(this.minimizeButton);
    this.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 2 });
    this.Controls.Add(this.maximizeButton);
    this.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 2 });
    this.Controls.Add(this.closeButton);
  }

  #region Window Dragging

  private void TitleBarComponent_MouseDown(object? sender, MouseEventArgs e)
  {
    _dragging = true;
    _dragCursorPoint = Cursor.Position;
    _dragFormPoint = _hostForm.Location;
  }

  private void TitleBarComponent_MouseMove(object? sender, MouseEventArgs e)
  {
    if (_dragging)
    {
      Point diff = Point.Subtract(Cursor.Position, new Size(_dragCursorPoint));
      _hostForm.Location = Point.Add(_dragFormPoint, new Size(diff));
    }
  }

  private void TitleBarComponent_MouseUp(object? sender, MouseEventArgs e)
  {
    _dragging = false;
  }

  #endregion

  #region Window Resizing

  // TODO

  #endregion

  #region Window Snapping

  // TODO

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
