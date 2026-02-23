/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace Tracker.Installer;

/// <summary>
/// Basic installer wizard UI.
/// </summary>
public sealed class InstallerWizardForm : Form
{
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
  private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

  private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
  private const int EM_LINESCROLL = 0x00B6;
  private const int EM_GETLINECOUNT = 0x00BA;

  private static readonly Color SecondaryButtonBackColor = Color.FromArgb(39, 39, 42);
  private static readonly Color SecondaryButtonHoverBackColor = Color.FromArgb(63, 63, 70);
  private static readonly Color SecondaryButtonBorderColor = Color.FromArgb(82, 82, 91);
  private static readonly Color PrimaryButtonBackColor = Color.FromArgb(37, 99, 235);
  private static readonly Color PrimaryButtonHoverBackColor = Color.FromArgb(29, 78, 216);
  private static readonly Color PrimaryButtonBorderColor = Color.FromArgb(59, 130, 246);
  private static readonly Color DisabledButtonBackColor = Color.FromArgb(24, 24, 27);
  private static readonly Color DisabledButtonBorderColor = Color.FromArgb(63, 63, 70);

  private readonly Panel _contentPanel;
  private readonly Button _backButton;
  private readonly Button _nextButton;
  private readonly Button _cancelButton;

  private readonly Panel _welcomeStep;
  private readonly Panel _eulaStep;
  private readonly Panel _directoryStep;
  private readonly Panel _installStep;

  private readonly Func<InstallerWizardOptions, IProgress<InstallerProgressUpdate>, bool>? _installAction;

  private readonly CheckBox _acceptEulaCheckBox;
  private readonly RadioButton _portableModeCheckBox;
  private readonly TextBox _installDirectoryTextBox;
  private readonly CheckBox _desktopShortcutCheckBox;
  private readonly CheckBox _startMenuShortcutCheckBox;
  private readonly ProgressBar _installProgressBar;
  private readonly Label _installStatusLabel;
  private readonly Panel _headerPanel;
  private readonly Timer _headerAnimationTimer;
  private Bitmap? _cachedHeaderBackground;
  private float _headerAnimationTime;
  private readonly List<LinkSpan> _eulaLinkSpans = new();
  private RichTextBox? _eulaRichTextBox;
  private Panel? _eulaScrollIndicator;
  private int _eulaScrollThumbY;
  private int _eulaScrollThumbHeight;
  private int _eulaFirstVisibleLine;
  private int _eulaVisibleLineCount;
  private int _eulaTotalLines;
  private int _eulaWheelDeltaRemainder;
  private int _pendingEulaScrollLines;
  private readonly Timer _eulaScrollAnimationTimer;
  private bool _isDraggingEulaScrollThumb;
  private int _eulaDragOffsetY;
  private EulaMouseWheelFilter? _eulaMouseWheelFilter;

  private int _stepIndex;
  private bool _isInstalling;
  private bool _canGoBack;
  private bool _canProceed;
  private bool _canCancel;

  public bool InstallationSucceeded { get; private set; }

  /// <summary>
  /// Final user selection when <see cref="DialogResult.OK"/> is returned.
  /// </summary>
  public InstallerWizardOptions? Options { get; private set; }

  public InstallerWizardForm(
    string defaultInstallDirectory,
    string eulaText,
    Func<InstallerWizardOptions, IProgress<InstallerProgressUpdate>, bool>? installAction = null)
  {
    _installAction = installAction;

    this.Text = $"{ProductInfo.Name} Setup";
    this.StartPosition = FormStartPosition.CenterScreen;
    this.FormBorderStyle = FormBorderStyle.None;
    this.MinimizeBox = false;
    this.MaximizeBox = false;
    this.ClientSize = new Size(760, 520);
    this.BackColor = Color.FromArgb(24, 24, 27);
    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);

    _headerPanel = new DoubleBufferedPanel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.Transparent,
      Margin = Padding.Empty,
    };
    _headerPanel.Paint += HeaderPanel_Paint;
    _headerPanel.SizeChanged += (_, _) =>
    {
      _cachedHeaderBackground?.Dispose();
      _cachedHeaderBackground = null;
    };

    var title = new Label
    {
      Text = $"{ProductInfo.Name} Installer",
      ForeColor = Color.White,
      Font = new Font("Segoe UI", 16, FontStyle.Bold),
      AutoSize = true,
      Location = new Point(20, 24 + 10),
      BackColor = Color.Transparent,
    };

    var subtitle = new Label
    {
      Text = $"Version {ProductInfo.Version}",
      ForeColor = Color.FromArgb(220, 220, 225),
      Font = new Font("Segoe UI", 9, FontStyle.Regular),
      AutoSize = true,
      Location = new Point(22, 62 + 10),
      BackColor = Color.Transparent,
    };

    _headerPanel.Controls.Add(title);
    _headerPanel.Controls.Add(subtitle);

    _headerAnimationTimer = new Timer { Interval = 16 };
    _headerAnimationTimer.Tick += (_, _) =>
    {
      _headerAnimationTime += 0.008f;
      _headerPanel.Invalidate();
    };
    _headerAnimationTimer.Start();

    _eulaScrollAnimationTimer = new Timer { Interval = 14 };
    _eulaScrollAnimationTimer.Tick += (_, _) => ProcessPendingEulaScroll();

    _contentPanel = new Panel
    {
      Dock = DockStyle.Fill,
      Padding = new Padding(20, 16, 20, 12),
      BackColor = Color.FromArgb(18, 18, 20),
      Margin = Padding.Empty,
    };

    var footer = new Panel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.FromArgb(18, 18, 20),
      Padding = new Padding(30, 10, 30, 10),
      Margin = Padding.Empty,
    };

    var footerLeftButtons = new FlowLayoutPanel
    {
      Dock = DockStyle.Left,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      BackColor = Color.Transparent,
      Margin = Padding.Empty,
      Padding = Padding.Empty,
    };

    var footerRightButtons = new FlowLayoutPanel
    {
      Dock = DockStyle.Right,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.RightToLeft,
      WrapContents = false,
      BackColor = Color.Transparent,
      Margin = Padding.Empty,
      Padding = Padding.Empty,
    };

    _backButton = new Button
    {
      Text = "< Back",
      Width = 92,
      Height = 30,
      Margin = new Padding(6, 0, 0, 0),
      UseVisualStyleBackColor = false,
    };
    ApplySecondaryButtonStyle(_backButton);
    _backButton.Click += (_, _) =>
    {
      if (_stepIndex > 0 && _stepIndex < 3)
      {
        ChangeStep(_stepIndex - 1);
      }
    };

    _nextButton = new Button
    {
      Text = "Next >",
      Width = 92,
      Height = 30,
      Margin = new Padding(6, 0, 0, 0),
      UseVisualStyleBackColor = false,
    };
    ApplyPrimaryButtonStyle(_nextButton);
    _nextButton.Click += NextButton_Click;

    _cancelButton = new Button
    {
      Text = "Cancel",
      Width = 92,
      Height = 30,
      Margin = Padding.Empty,
      UseVisualStyleBackColor = false,
    };
    ApplySecondaryButtonStyle(_cancelButton);
    _cancelButton.Click += (_, _) =>
    {
      if (!_canCancel)
      {
        return;
      }

      this.DialogResult = DialogResult.Cancel;
      this.Close();
    };

    footerLeftButtons.Controls.Add(_cancelButton);
    footerRightButtons.Controls.Add(_nextButton);
    footerRightButtons.Controls.Add(_backButton);
    footer.Controls.Add(footerRightButtons);
    footer.Controls.Add(footerLeftButtons);

    _welcomeStep = CreateStepPanel();
    _eulaStep = CreateStepPanel();
    _directoryStep = CreateStepPanel();
    _installStep = CreateStepPanel();

    _portableModeCheckBox = BuildWelcomeStep(_welcomeStep);

    _acceptEulaCheckBox = BuildEulaStep(_eulaStep, eulaText);

    (_installDirectoryTextBox, _desktopShortcutCheckBox, _startMenuShortcutCheckBox) =
      BuildDirectoryStep(_directoryStep, defaultInstallDirectory);

    (_installProgressBar, _installStatusLabel) = BuildInstallStep(_installStep);

    _contentPanel.Controls.Add(_welcomeStep);
    _contentPanel.Controls.Add(_eulaStep);
    _contentPanel.Controls.Add(_directoryStep);
    _contentPanel.Controls.Add(_installStep);

    var rootLayout = new TableLayoutPanel
    {
      Dock = DockStyle.None,
      ColumnCount = 1,
      RowCount = 3,
      Margin = Padding.Empty,
      Padding = Padding.Empty,
      CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
    };
    rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128f));
    rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
    rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));

    rootLayout.Controls.Add(_headerPanel, 0, 0);
    rootLayout.Controls.Add(_contentPanel, 0, 1);
    rootLayout.Controls.Add(footer, 0, 2);

    this.Controls.Add(rootLayout);

    void updateRootLayoutBounds()
    {
      rootLayout.SetBounds(
        0,
        0,
        this.ClientSize.Width,
        this.ClientSize.Height);
    }

    updateRootLayoutBounds();
    this.SizeChanged += (_, _) => updateRootLayoutBounds();

    this.AcceptButton = _nextButton;
    this.CancelButton = _cancelButton;

    ChangeStep(0);
  }

  private static Panel CreateStepPanel()
  {
    return new Panel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.FromArgb(18, 18, 20),
      Visible = false,
    };
  }

  private sealed class DoubleBufferedPanel : Panel
  {
    public DoubleBufferedPanel()
    {
      this.DoubleBuffered = true;
      this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
      this.UpdateStyles();
    }
  }

  private void HeaderPanel_Paint(object? sender, PaintEventArgs e)
  {
    if (_headerPanel.Width <= 0 || _headerPanel.Height <= 0)
    {
      return;
    }

    if (_cachedHeaderBackground == null
        || _cachedHeaderBackground.Width != _headerPanel.Width
        || _cachedHeaderBackground.Height != _headerPanel.Height)
    {
      _cachedHeaderBackground?.Dispose();
      _cachedHeaderBackground = new Bitmap(_headerPanel.Width, _headerPanel.Height);

      using var g = Graphics.FromImage(_cachedHeaderBackground);
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      DrawHeaderStaticBackground(g, _headerPanel.ClientRectangle);
    }

    e.Graphics.DrawImage(_cachedHeaderBackground, 0, 0);
    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    DrawHeaderMeshPattern(e.Graphics, _headerPanel.ClientRectangle, _headerAnimationTime);
  }

  private static void DrawHeaderStaticBackground(Graphics g, Rectangle rect)
  {
    using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
      rect,
      Color.FromArgb(30, 58, 138),
      Color.FromArgb(15, 23, 42),
      System.Drawing.Drawing2D.LinearGradientMode.Horizontal);

    g.FillRectangle(brush, rect);

    using var overlayBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
      rect,
      Color.FromArgb(120, 0, 0, 0),
      Color.FromArgb(0, 0, 0, 0),
      System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
    g.FillRectangle(overlayBrush, rect);
  }

  private static void DrawHeaderMeshPattern(Graphics g, Rectangle bounds, float time)
  {
    using var pen = new Pen(Color.FromArgb(35, 255, 255, 255), 1.5f);

    const int lineCount = 28;
    const int segments = 60;

    for (int i = 0; i < lineCount; i++)
    {
      float normalizedY = i / (float)(lineCount - 1);
      float baseY = bounds.Height * (-0.2f + normalizedY * 1.4f);

      var points = new List<PointF>(segments + 1);
      for (int j = 0; j <= segments; j++)
      {
        float normalizedX = j / (float)segments;

        float xOffset = (float)(Math.Sin(normalizedY * 8.0 + normalizedX * 4.0 + time * 0.5) * 15.0);
        float x = bounds.Width * normalizedX + xOffset;

        double offset = 0;
        offset += Math.Sin(normalizedX * 5.0 + normalizedY * 4.0 + time) * 20.0;
        offset += Math.Sin(normalizedX * 7.0 - normalizedY * 5.0 - time * 0.8) * 15.0;
        offset += Math.Cos(normalizedX * 12.0 + normalizedY * 10.0 + Math.Sin(normalizedX * 5.0) + time * 1.5) * 8.0;
        offset += Math.Sin(normalizedX * 25.0 + time * 2.0) * 3.0;

        points.Add(new PointF(x, baseY + (float)offset));
      }

      if (points.Count > 1)
      {
        g.DrawCurve(pen, points.ToArray(), 0.5f);
      }
    }
  }

  private RadioButton BuildWelcomeStep(Panel panel)
  {
    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Top,
      ColumnCount = 1,
      RowCount = 3,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Padding = new Padding(10, 10, 10, 10),
      Margin = Padding.Empty,
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    var heading = new Label
    {
      Text = $"Welcome to {ProductInfo.Name}",
      ForeColor = Color.FromArgb(235, 235, 240),
      Font = new Font("Segoe UI", 16, FontStyle.Bold),
      Dock = DockStyle.Top,
      AutoSize = true,
      Margin = Padding.Empty,
    };

    var body = new Label
    {
      Text = "Track your Magic: The Gathering Online matches, decks, and collection in one place. Choose your preferred installation mode below to get started.",
      ForeColor = Color.FromArgb(200, 200, 208),
      Font = new Font("Segoe UI", 10, FontStyle.Regular),
      MaximumSize = new Size(680, 0),
      Dock = DockStyle.Top,
      AutoSize = true,
      Margin = new Padding(0, 6, 0, 0),
    };

    // --- Install option card ---
    var installPanel = new Panel
    {
      Dock = DockStyle.Top,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      BackColor = Color.FromArgb(22, 22, 24),
      Padding = new Padding(12, 10, 10, 10),
      Margin = Padding.Empty,
      Cursor = Cursors.Hand,
    };

    var installRadio = new RadioButton
    {
      Text = "Install Videre Tracker",
      ForeColor = Color.FromArgb(228, 228, 231),
      Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
      AutoSize = true,
      Dock = DockStyle.Top,
      Checked = true,
      Margin = Padding.Empty,
    };

    var installHint = new Label
    {
      Text = "Copies files to %LocalAppData%, creates optional shortcuts, and launches from the installed location going forward.",
      ForeColor = Color.FromArgb(161, 161, 170),
      Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
      Dock = DockStyle.Top,
      MaximumSize = new Size(640, 0),
      AutoSize = true,
      Margin = new Padding(20, 3, 0, 0),
    };

    installPanel.Controls.Add(installHint);
    installPanel.Controls.Add(installRadio);

    // --- Portable option card ---
    var portablePanel = new Panel
    {
      Dock = DockStyle.Top,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      BackColor = Color.FromArgb(22, 22, 24),
      Padding = new Padding(12, 10, 10, 10),
      Margin = new Padding(0, 8, 0, 0),
      Cursor = Cursors.Hand,
    };

    var portableRadio = new RadioButton
    {
      Text = "Run in portable mode",
      ForeColor = Color.FromArgb(228, 228, 231),
      Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
      AutoSize = true,
      Dock = DockStyle.Top,
      Checked = false,
      Margin = Padding.Empty,
    };

    var portableHint = new Label
    {
      Text = "Launches directly from this executable with no files copied. Future runs from this location will also skip the installer.",
      ForeColor = Color.FromArgb(161, 161, 170),
      Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
      Dock = DockStyle.Top,
      MaximumSize = new Size(640, 0),
      AutoSize = true,
      Margin = new Padding(20, 3, 0, 0),
    };

    portablePanel.Controls.Add(portableHint);
    portablePanel.Controls.Add(portableRadio);

    void selectInstall(object? s, EventArgs e) { installRadio.Checked = true; portableRadio.Checked = false; UpdateButtons(); }
    void selectPortable(object? s, EventArgs e) { portableRadio.Checked = true; installRadio.Checked = false; UpdateButtons(); }

    // Mutual exclusion between the two options
    installRadio.Click += selectInstall;
    installPanel.Click += selectInstall;
    installHint.Click += selectInstall;
    installHint.Cursor = Cursors.Hand;

    portableRadio.Click += selectPortable;
    portablePanel.Click += selectPortable;
    portableHint.Click += selectPortable;
    portableHint.Cursor = Cursors.Hand;

    // Stack both cards in a shared container so WinForms groups the radios
    var optionsContainer = new Panel
    {
      Dock = DockStyle.Top,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      BackColor = Color.Transparent,
      Margin = new Padding(0, 14, 0, 0),
    };

    // Controls added bottom-up because Dock=Top reverses rendering order
    optionsContainer.Controls.Add(portablePanel);
    optionsContainer.Controls.Add(installPanel);

    layout.Controls.Add(heading, 0, 0);
    layout.Controls.Add(body, 0, 1);
    layout.Controls.Add(optionsContainer, 0, 2);
    panel.Controls.Add(layout);

    return portableRadio;
  }

  private CheckBox BuildEulaStep(Panel panel, string eulaText)
  {
    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Padding = new Padding(10, 10, 10, 10),
      Margin = Padding.Empty,
    };
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

    var heading = new Label
    {
      Text = "End User License Agreement",
      ForeColor = Color.FromArgb(235, 235, 240),
      Font = new Font("Segoe UI", 12, FontStyle.Bold),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = Padding.Empty,
    };

    var eulaHost = new Panel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.FromArgb(22, 22, 24),
      Margin = new Padding(0, 8, 0, 8),
      Padding = new Padding(10, 8, 8, 8),
    };

    var eulaContentLayout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 1,
      Margin = Padding.Empty,
      Padding = Padding.Empty,
      BackColor = Color.Transparent,
    };
    eulaContentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
    eulaContentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12f));

    var eulaBox = new RichTextBox
    {
      ReadOnly = true,
      BorderStyle = BorderStyle.None,
      BackColor = Color.FromArgb(22, 22, 24),
      ForeColor = Color.FromArgb(214, 214, 220),
      Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
      Dock = DockStyle.Fill,
      Margin = new Padding(0, 0, 6, 0),
      ScrollBars = RichTextBoxScrollBars.None,
      DetectUrls = false,
      ShortcutsEnabled = true,
    };
    ApplyMarkdownToEula(eulaBox, eulaText);
    eulaBox.MouseMove += EulaBox_MouseMove;
    eulaBox.MouseUp += EulaBox_MouseUp;
    eulaBox.MouseEnter += (_, _) => eulaBox.Focus();

    var scrollIndicator = new Panel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.Transparent,
      Margin = Padding.Empty,
      Cursor = Cursors.Hand,
    };
    scrollIndicator.Paint += (_, paintArgs) => PaintEulaScrollIndicator(paintArgs.Graphics, scrollIndicator.ClientRectangle);
    scrollIndicator.MouseDown += EulaScrollIndicator_MouseDown;
    scrollIndicator.MouseMove += EulaScrollIndicator_MouseMove;
    scrollIndicator.MouseUp += EulaScrollIndicator_MouseUp;
    scrollIndicator.MouseLeave += EulaScrollIndicator_MouseLeave;

    eulaContentLayout.Controls.Add(eulaBox, 0, 0);
    eulaContentLayout.Controls.Add(scrollIndicator, 1, 0);
    eulaHost.Controls.Add(eulaContentLayout);

    _eulaRichTextBox = eulaBox;
    _eulaScrollIndicator = scrollIndicator;
    eulaBox.VScroll += (_, _) => UpdateEulaScrollIndicator();
    eulaBox.TextChanged += (_, _) => UpdateEulaScrollIndicator();
    eulaBox.Resize += (_, _) => UpdateEulaScrollIndicator();
    this.Shown += (_, _) => UpdateEulaScrollIndicator();

    if (_eulaMouseWheelFilter == null)
    {
      _eulaMouseWheelFilter = new EulaMouseWheelFilter(this);
      Application.AddMessageFilter(_eulaMouseWheelFilter);
    }

    var checkBox = new CheckBox
    {
      Text = "I accept the terms in the License Agreement",
      ForeColor = Color.FromArgb(228, 228, 231),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = Padding.Empty,
    };
    checkBox.CheckedChanged += (_, _) => UpdateButtons();

    layout.Controls.Add(heading, 0, 0);
    layout.Controls.Add(eulaHost, 0, 1);
    layout.Controls.Add(checkBox, 0, 2);
    panel.Controls.Add(layout);

    UpdateEulaScrollIndicator();

    return checkBox;
  }

  private void UpdateEulaScrollIndicator()
  {
    if (_eulaRichTextBox == null || _eulaScrollIndicator == null)
    {
      return;
    }

    if (!TryGetEulaScrollMetrics(out var firstLine, out var visibleLines, out var totalLines))
    {
      return;
    }

    _eulaFirstVisibleLine = Math.Max(0, firstLine);
    _eulaVisibleLineCount = visibleLines;
    _eulaTotalLines = totalLines;

    var trackHeight = Math.Max(1, _eulaScrollIndicator.ClientSize.Height);

    if (totalLines <= visibleLines)
    {
      _eulaScrollThumbHeight = trackHeight;
      _eulaScrollThumbY = 0;
      _eulaScrollIndicator.Invalidate();
      return;
    }

    _eulaScrollThumbHeight = Math.Max(20, (int)Math.Round(trackHeight * (visibleLines / (double)totalLines)));
    _eulaScrollThumbHeight = Math.Min(trackHeight, _eulaScrollThumbHeight);

    var progress = firstLine / (double)Math.Max(1, totalLines - visibleLines);
    _eulaScrollThumbY = (int)Math.Round((_eulaScrollIndicator.ClientSize.Height - _eulaScrollThumbHeight) * progress);
    _eulaScrollIndicator.Invalidate();
  }

  private void PaintEulaScrollIndicator(Graphics graphics, Rectangle bounds)
  {
    if (_eulaScrollIndicator == null || bounds.Width <= 0 || bounds.Height <= 0)
    {
      return;
    }

    graphics.SmoothingMode = SmoothingMode.AntiAlias;

    var thumbWidth = Math.Max(4, bounds.Width - 4);
    var thumbX = bounds.X + ((bounds.Width - thumbWidth) / 2);
    var thumbY = bounds.Y + _eulaScrollThumbY;
    var thumbHeight = Math.Max(20, _eulaScrollThumbHeight);

    var thumbRect = new Rectangle(thumbX, thumbY, thumbWidth, Math.Min(bounds.Height - _eulaScrollThumbY, thumbHeight));

    using var brush = new SolidBrush(Color.FromArgb(170, 113, 113, 122));
    using var path = CreateRoundedRectPath(thumbRect, radius: Math.Max(2, thumbRect.Width / 2));
    graphics.FillPath(brush, path);
  }

  private void EulaScrollIndicator_MouseDown(object? sender, MouseEventArgs e)
  {
    if (_eulaScrollIndicator == null || _eulaRichTextBox == null || e.Button != MouseButtons.Left)
    {
      return;
    }

    var thumbRect = new Rectangle(0, _eulaScrollThumbY, _eulaScrollIndicator.ClientSize.Width, _eulaScrollThumbHeight);
    if (thumbRect.Contains(e.Location))
    {
      _isDraggingEulaScrollThumb = true;
      _eulaDragOffsetY = e.Y - _eulaScrollThumbY;
      _eulaScrollIndicator.Capture = true;
      return;
    }

    var direction = e.Y < _eulaScrollThumbY ? -1 : 1;
    ScrollEulaByLines(direction * Math.Max(1, _eulaVisibleLineCount - 1));
  }

  private void EulaScrollIndicator_MouseMove(object? sender, MouseEventArgs e)
  {
    if (!_isDraggingEulaScrollThumb || _eulaScrollIndicator == null)
    {
      return;
    }

    var trackHeight = Math.Max(1, _eulaScrollIndicator.ClientSize.Height);
    var movableHeight = Math.Max(1, trackHeight - _eulaScrollThumbHeight);
    var newThumbY = e.Y - _eulaDragOffsetY;
    newThumbY = Math.Max(0, Math.Min(movableHeight, newThumbY));

    var progress = movableHeight == 0 ? 0 : newThumbY / (double)movableHeight;
    var maxFirstLine = Math.Max(0, _eulaTotalLines - _eulaVisibleLineCount);
    var targetFirstLine = (int)Math.Round(progress * maxFirstLine);
    ScrollEulaToFirstVisibleLine(targetFirstLine);
  }

  private void EulaScrollIndicator_MouseUp(object? sender, MouseEventArgs e)
  {
    _isDraggingEulaScrollThumb = false;
    if (_eulaScrollIndicator != null)
    {
      _eulaScrollIndicator.Capture = false;
    }
  }

  private void EulaScrollIndicator_MouseLeave(object? sender, EventArgs e)
  {
    if (!_isDraggingEulaScrollThumb)
    {
      return;
    }

    _isDraggingEulaScrollThumb = false;
    if (_eulaScrollIndicator != null)
    {
      _eulaScrollIndicator.Capture = false;
    }
  }

  private void ScrollEulaByLines(int lineDelta)
  {
    if (_eulaRichTextBox == null || lineDelta == 0 || !_eulaRichTextBox.IsHandleCreated)
    {
      return;
    }

    _ = SendMessage(
      _eulaRichTextBox.Handle,
      EM_LINESCROLL,
      IntPtr.Zero,
      new IntPtr(lineDelta));

    UpdateEulaScrollIndicator();
  }

  private void ScrollEulaToFirstVisibleLine(int targetFirstLine)
  {
    if (_eulaRichTextBox == null || !_eulaRichTextBox.IsHandleCreated)
    {
      return;
    }

    if (!TryGetEulaScrollMetrics(out var currentFirstLine, out var visibleLines, out var totalLines))
    {
      return;
    }

    var maxFirstLine = Math.Max(0, totalLines - visibleLines);
    var clampedTarget = Math.Max(0, Math.Min(maxFirstLine, targetFirstLine));

    var delta = clampedTarget - currentFirstLine;
    if (delta == 0)
    {
      return;
    }

    _ = SendMessage(
      _eulaRichTextBox.Handle,
      EM_LINESCROLL,
      IntPtr.Zero,
      new IntPtr(delta));

    UpdateEulaScrollIndicator();
  }

  private bool TryGetEulaScrollMetrics(out int firstLine, out int visibleLines, out int totalLines)
  {
    firstLine = 0;
    visibleLines = 1;
    totalLines = 1;

    if (_eulaRichTextBox == null || !_eulaRichTextBox.IsHandleCreated)
    {
      return false;
    }

    firstLine = Math.Max(0, (int)SendMessage(
      _eulaRichTextBox.Handle,
      EM_GETFIRSTVISIBLELINE,
      IntPtr.Zero,
      IntPtr.Zero));

    totalLines = Math.Max(1, (int)SendMessage(
      _eulaRichTextBox.Handle,
      EM_GETLINECOUNT,
      IntPtr.Zero,
      IntPtr.Zero));

    var lineHeight = Math.Max(1, TextRenderer.MeasureText(
      "Ag",
      _eulaRichTextBox.Font,
      new Size(int.MaxValue, int.MaxValue),
      TextFormatFlags.NoPadding).Height);

    visibleLines = Math.Max(1, _eulaRichTextBox.ClientSize.Height / lineHeight);
    visibleLines = Math.Min(totalLines, visibleLines);

    return true;
  }

  private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
  {
    var path = new GraphicsPath();
    var diameter = Math.Max(1, radius * 2);
    var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

    path.AddArc(arc, 180, 90);
    arc.X = rect.Right - diameter;
    path.AddArc(arc, 270, 90);
    arc.Y = rect.Bottom - diameter;
    path.AddArc(arc, 0, 90);
    arc.X = rect.Left;
    path.AddArc(arc, 90, 90);
    path.CloseFigure();
    return path;
  }

  private readonly record struct MarkdownToken(string Text, bool Bold, bool Italic, string? Url = null);
  private readonly record struct LinkSpan(int Start, int Length, string Url);

  private static readonly Regex BareUrlRegex = new(
    @"(?:(?:https?|ftp)://|www\.)[^\s<>()]+",
    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

  private void ApplyMarkdownToEula(RichTextBox eulaBox, string markdown)
  {
    _eulaLinkSpans.Clear();
    eulaBox.Clear();

    var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
    {
      var tokens = ParseMarkdownInline(lines[lineIndex]);
      foreach (var token in tokens)
      {
        AppendStyledToken(eulaBox, token);
      }

      if (lineIndex < lines.Length - 1)
      {
        eulaBox.AppendText(Environment.NewLine);
      }
    }

    eulaBox.SelectionStart = 0;
    eulaBox.SelectionLength = 0;
  }

  private void AppendStyledToken(RichTextBox eulaBox, MarkdownToken token)
  {
    var style = FontStyle.Regular;
    if (token.Bold)
    {
      style |= FontStyle.Bold;
    }

    if (token.Italic)
    {
      style |= FontStyle.Italic;
    }

    if (!string.IsNullOrWhiteSpace(token.Url))
    {
      style |= FontStyle.Underline;
    }

    var start = eulaBox.TextLength;
    eulaBox.SelectionStart = start;
    eulaBox.SelectionLength = 0;
    eulaBox.SelectionFont = new Font("Segoe UI", 9.5F, style);
    eulaBox.SelectionColor = string.IsNullOrWhiteSpace(token.Url)
      ? Color.FromArgb(214, 214, 220)
      : Color.FromArgb(96, 165, 250);
    eulaBox.AppendText(token.Text);

    if (!string.IsNullOrWhiteSpace(token.Url) && token.Text.Length > 0)
    {
      _eulaLinkSpans.Add(new LinkSpan(start, token.Text.Length, token.Url));
    }
  }

  private static List<MarkdownToken> ParseMarkdownInline(string line)
  {
    var tokens = new List<MarkdownToken>();
    ParseMarkdownSegment(line, 0, line.Length, tokens, bold: false, italic: false, url: null);
    return tokens;
  }

  private static void ParseMarkdownSegment(
    string text,
    int start,
    int end,
    List<MarkdownToken> tokens,
    bool bold,
    bool italic,
    string? url)
  {
    int i = start;
    while (i < end)
    {
      if (i + 1 < end && text[i] == '*' && text[i + 1] == '*')
      {
        var closing = text.IndexOf("**", i + 2, StringComparison.Ordinal);
        if (closing >= 0 && closing < end)
        {
          ParseMarkdownSegment(text, i + 2, closing, tokens, bold: true, italic: italic, url: url);
          i = closing + 2;
          continue;
        }
      }

      if (text[i] == '*' || text[i] == '_')
      {
        var marker = text[i];
        var closing = text.IndexOf(marker, i + 1);
        if (closing >= 0 && closing < end)
        {
          ParseMarkdownSegment(text, i + 1, closing, tokens, bold: bold, italic: true, url: url);
          i = closing + 1;
          continue;
        }
      }

      if (text[i] == '[')
      {
        var closeBracket = text.IndexOf(']', i + 1);
        if (closeBracket > i + 1 && closeBracket + 1 < end && text[closeBracket + 1] == '(')
        {
          var closeParen = text.IndexOf(')', closeBracket + 2);
          if (closeParen > closeBracket + 2 && closeParen < end)
          {
            var linkText = text.Substring(i + 1, closeBracket - i - 1);
            var linkUrl = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
            ParseMarkdownSegment(linkText, 0, linkText.Length, tokens, bold: bold, italic: italic, url: linkUrl);
            i = closeParen + 1;
            continue;
          }
        }
      }

      int next = i + 1;
      while (next < end)
      {
        if ((next + 1 < end && text[next] == '*' && text[next + 1] == '*')
            || text[next] == '*'
            || text[next] == '_'
            || text[next] == '[')
        {
          break;
        }

        next++;
      }

      var chunk = text.Substring(i, next - i);
      AppendChunkWithAutoLinks(tokens, chunk, bold, italic, url);
      i = next;
    }
  }

  private static void AppendChunkWithAutoLinks(
    List<MarkdownToken> tokens,
    string chunk,
    bool bold,
    bool italic,
    string? inheritedUrl)
  {
    if (string.IsNullOrEmpty(chunk))
    {
      return;
    }

    if (!string.IsNullOrWhiteSpace(inheritedUrl))
    {
      tokens.Add(new MarkdownToken(chunk, Bold: bold, Italic: italic, Url: inheritedUrl));
      return;
    }

    int currentIndex = 0;
    foreach (Match match in BareUrlRegex.Matches(chunk))
    {
      if (!match.Success || match.Length == 0)
      {
        continue;
      }

      var rawUrl = match.Value;
      var normalizedUrl = NormalizeAutoDetectedUrl(rawUrl, out var trimmedDisplayLength);

      if (trimmedDisplayLength <= 0)
      {
        continue;
      }

      if (match.Index > currentIndex)
      {
        var plainText = chunk.Substring(currentIndex, match.Index - currentIndex);
        tokens.Add(new MarkdownToken(plainText, Bold: bold, Italic: italic, Url: null));
      }

      var displayUrl = rawUrl.Substring(0, trimmedDisplayLength);
      tokens.Add(new MarkdownToken(displayUrl, Bold: bold, Italic: italic, Url: normalizedUrl));
      currentIndex = match.Index + trimmedDisplayLength;
    }

    if (currentIndex < chunk.Length)
    {
      var tail = chunk.Substring(currentIndex);
      tokens.Add(new MarkdownToken(tail, Bold: bold, Italic: italic, Url: null));
    }
  }

  private static string NormalizeAutoDetectedUrl(string rawUrl, out int displayLength)
  {
    displayLength = rawUrl.Length;

    while (displayLength > 0)
    {
      var c = rawUrl[displayLength - 1];
      if (c is '.' or ',' or ';' or ':' or '!' or '?')
      {
        displayLength--;
        continue;
      }

      break;
    }

    var display = displayLength > 0
      ? rawUrl.Substring(0, displayLength)
      : string.Empty;

    if (display.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
      return $"https://{display}";
    }

    return display;
  }

  private void EulaBox_MouseMove(object? sender, MouseEventArgs e)
  {
    if (sender is not RichTextBox eulaBox)
    {
      return;
    }

    if (!eulaBox.Focused)
    {
      eulaBox.Focus();
    }

    int index = eulaBox.GetCharIndexFromPosition(e.Location);
    var isLink = _eulaLinkSpans.Any(span => index >= span.Start && index < span.Start + span.Length);
    eulaBox.Cursor = isLink ? Cursors.Hand : Cursors.IBeam;
  }

  private void EulaBox_MouseUp(object? sender, MouseEventArgs e)
  {
    if (sender is not RichTextBox eulaBox || e.Button != MouseButtons.Left)
    {
      return;
    }

    int index = eulaBox.GetCharIndexFromPosition(e.Location);
    foreach (var span in _eulaLinkSpans)
    {
      if (index >= span.Start && index < span.Start + span.Length)
      {
        try
        {
          Process.Start(new ProcessStartInfo
          {
            FileName = span.Url,
            UseShellExecute = true,
          });
        }
        catch
        {
          // Ignore invalid link open failures.
        }

        break;
      }
    }
  }

  private bool TryHandleEulaMouseWheel(int wheelDelta)
  {
    if (_eulaRichTextBox == null || !_eulaRichTextBox.Visible)
    {
      return false;
    }

    var rect = _eulaRichTextBox.RectangleToScreen(_eulaRichTextBox.ClientRectangle);
    if (!rect.Contains(Control.MousePosition))
    {
      return false;
    }

    _eulaWheelDeltaRemainder += wheelDelta;
    var wheelSteps = _eulaWheelDeltaRemainder / 120;
    _eulaWheelDeltaRemainder %= 120;

    if (wheelSteps == 0)
    {
      return true;
    }

    _pendingEulaScrollLines += -wheelSteps;
    if (!_eulaScrollAnimationTimer.Enabled)
    {
      _eulaScrollAnimationTimer.Start();
    }

    return true;
  }

  private void ProcessPendingEulaScroll()
  {
    if (_pendingEulaScrollLines == 0)
    {
      _eulaScrollAnimationTimer.Stop();
      return;
    }

    var step = Math.Sign(_pendingEulaScrollLines);
    ScrollEulaByLines(step);
    _pendingEulaScrollLines -= step;
  }

  private sealed class EulaMouseWheelFilter(InstallerWizardForm owner) : IMessageFilter
  {
    private const int WM_MOUSEWHEEL = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
      if (m.Msg != WM_MOUSEWHEEL)
      {
        return false;
      }

      var wheelDelta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
      return owner.TryHandleEulaMouseWheel(wheelDelta);
    }
  }

  private (TextBox installDirectory, CheckBox desktopShortcut, CheckBox startMenuShortcut)
    BuildDirectoryStep(Panel panel, string defaultInstallDirectory)
  {
    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 7,
      Padding = new Padding(10, 10, 10, 10),
      Margin = Padding.Empty,
    };
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

    var heading = new Label
    {
      Text = "Choose installation options",
      ForeColor = Color.FromArgb(235, 235, 240),
      Font = new Font("Segoe UI", 12, FontStyle.Bold),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = Padding.Empty,
    };

    var directoryLabel = new Label
    {
      Text = "Installation root directory:",
      ForeColor = Color.FromArgb(210, 210, 218),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = new Padding(0, 2, 0, 0),
    };

    var directoryHintLabel = new Label
    {
      Text = $"The installer will create {GetVersionFolderName()} under this directory.",
      ForeColor = Color.FromArgb(160, 160, 170),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = new Padding(0, 2, 0, 0),
    };

    var pathRow = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 1,
      Margin = new Padding(0, 4, 0, 0),
      Padding = Padding.Empty,
    };
    pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
    pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));

    var directoryTextBox = new TextBox
    {
      Text = defaultInstallDirectory,
      Dock = DockStyle.Fill,
      Margin = Padding.Empty,
    };

    var browseButton = new Button
    {
      Text = "Browse...",
      Width = 92,
      Height = 26,
      Dock = DockStyle.Fill,
      Margin = new Padding(8, 0, 0, 0),
      UseVisualStyleBackColor = true,
    };
    browseButton.Click += (_, _) =>
    {
      using var dialog = new FolderBrowserDialog
      {
        Description = "Select installation directory",
        ShowNewFolderButton = true,
        UseDescriptionForTitle = true,
        SelectedPath = Directory.Exists(directoryTextBox.Text)
          ? directoryTextBox.Text
          : Path.GetDirectoryName(directoryTextBox.Text) ?? string.Empty,
      };

      if (dialog.ShowDialog(this) == DialogResult.OK)
      {
        directoryTextBox.Text = dialog.SelectedPath;
      }
    };

    pathRow.Controls.Add(directoryTextBox, 0, 0);
    pathRow.Controls.Add(browseButton, 1, 0);

    var desktopShortcut = new CheckBox
    {
      Text = "Create desktop shortcut",
      ForeColor = Color.FromArgb(228, 228, 231),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = new Padding(0, 4, 0, 0),
      Checked = true,
    };

    var startMenuShortcut = new CheckBox
    {
      Text = "Create Start Menu shortcut",
      ForeColor = Color.FromArgb(228, 228, 231),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = new Padding(0, 2, 0, 0),
      Checked = true,
    };

    layout.Controls.Add(heading, 0, 0);
    layout.Controls.Add(directoryLabel, 0, 1);
    layout.Controls.Add(pathRow, 0, 2);
    layout.Controls.Add(directoryHintLabel, 0, 3);
    layout.Controls.Add(desktopShortcut, 0, 4);
    layout.Controls.Add(startMenuShortcut, 0, 5);
    panel.Controls.Add(layout);

    return (directoryTextBox, desktopShortcut, startMenuShortcut);
  }

  private static (ProgressBar progressBar, Label statusLabel) BuildInstallStep(Panel panel)
  {
    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 4,
      Padding = new Padding(10, 10, 10, 10),
      Margin = Padding.Empty,
    };
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

    var heading = new Label
    {
      Text = "Installing...",
      ForeColor = Color.FromArgb(235, 235, 240),
      Font = new Font("Segoe UI", 12, FontStyle.Bold),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = Padding.Empty,
    };

    var progressBar = new ProgressBar
    {
      Dock = DockStyle.Fill,
      Minimum = 0,
      Maximum = 100,
      Style = ProgressBarStyle.Continuous,
      Margin = new Padding(0, 4, 0, 0),
    };

    var statusLabel = new Label
    {
      Text = "Preparing installation...",
      ForeColor = Color.FromArgb(200, 200, 208),
      AutoSize = true,
      Dock = DockStyle.Left,
      Margin = new Padding(0, 6, 0, 0),
    };

    layout.Controls.Add(heading, 0, 0);
    layout.Controls.Add(progressBar, 0, 1);
    layout.Controls.Add(statusLabel, 0, 2);
    panel.Controls.Add(layout);

    return (progressBar, statusLabel);
  }

  private void NextButton_Click(object? sender, EventArgs e)
  {
    if (!_canProceed)
    {
      return;
    }

    if (_stepIndex == 0)
    {
      if (_portableModeCheckBox.Checked)
      {
        Options = new InstallerWizardOptions(
          UsePortableMode: true,
          InstallationRootDirectory: string.Empty,
          CreateDesktopShortcut: false,
          CreateStartMenuShortcut: false);
        InstallationSucceeded = true;
        this.DialogResult = DialogResult.OK;
        this.Close();
        return;
      }

      ChangeStep(1);
      return;
    }

    if (_stepIndex == 1)
    {
      if (!_acceptEulaCheckBox.Checked)
      {
        MessageBox.Show(this,
          "You must accept the license agreement to continue.",
          "License Agreement",
          MessageBoxButtons.OK,
          MessageBoxIcon.Information);
        return;
      }

      ChangeStep(2);
      return;
    }

    var installDirectory = _installDirectoryTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(installDirectory))
    {
      MessageBox.Show(this,
        "Please provide an installation directory.",
        "Installation Directory",
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
      return;
    }

    var options = new InstallerWizardOptions(
      UsePortableMode: false,
      InstallationRootDirectory: installDirectory,
      CreateDesktopShortcut: _desktopShortcutCheckBox.Checked,
      CreateStartMenuShortcut: _startMenuShortcutCheckBox.Checked);

    Options = options;

    if (_installAction == null)
    {
      this.DialogResult = DialogResult.OK;
      this.Close();
      return;
    }

    _ = RunInstallStepAsync(options);
  }

  private async Task RunInstallStepAsync(InstallerWizardOptions options)
  {
    if (_isInstalling)
    {
      return;
    }

    _isInstalling = true;
    ChangeStep(3);
    _backButton.Enabled = false;
    _nextButton.Enabled = false;
    _cancelButton.Enabled = false;
    this.UseWaitCursor = true;

    var progress = new Progress<InstallerProgressUpdate>(update =>
    {
      _installStatusLabel.Text = update.Message;
      _installProgressBar.Value = Math.Clamp(update.Percent, 0, 100);
    });

    bool success = false;
    try
    {
      success = await Task.Run(() => _installAction!.Invoke(options, progress));
    }
    catch
    {
      success = false;
    }

    this.UseWaitCursor = false;

    if (success)
    {
      InstallationSucceeded = true;
      _installProgressBar.Value = 100;
      _installStatusLabel.Text = "Installation complete.";
      this.DialogResult = DialogResult.OK;
      this.Close();
      return;
    }

    _isInstalling = false;
    MessageBox.Show(this,
      "Installation failed. Please verify the selected directory and try again.",
      "Installation Error",
      MessageBoxButtons.OK,
      MessageBoxIcon.Error);
    ChangeStep(2);
    _cancelButton.Enabled = true;
  }

  private static string GetVersionFolderName()
  {
    var version = ProductInfo.Version?.Trim() ?? "0.0.0";
    return version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
      ? version
      : $"v{version}";
  }

  private void ChangeStep(int step)
  {
    _stepIndex = Math.Clamp(step, 0, 3);

    _welcomeStep.Visible = _stepIndex == 0;
    _eulaStep.Visible = _stepIndex == 1;
    _directoryStep.Visible = _stepIndex == 2;
    _installStep.Visible = _stepIndex == 3;

    UpdateButtons();
  }

  private void UpdateButtons()
  {
    _canGoBack = _stepIndex > 0 && _stepIndex < 3;
    _nextButton.Text = _stepIndex == 2
      ? "Install"
      : (_stepIndex == 0 && _portableModeCheckBox.Checked ? "Launch" : "Next >");

    if (_stepIndex == 3)
    {
      _canProceed = false;
      _canCancel = false;
      UpdateButtonEnabledVisualState(_backButton, isPrimary: false, isEnabled: false);
      UpdateButtonEnabledVisualState(_nextButton, isPrimary: true, isEnabled: false);
      UpdateButtonEnabledVisualState(_cancelButton, isPrimary: false, isEnabled: false);
      return;
    }

    if (_stepIndex == 1)
    {
      _canProceed = _acceptEulaCheckBox.Checked;
    }
    else
    {
      _canProceed = true;
    }
    _canCancel = true;

    UpdateButtonEnabledVisualState(_backButton, isPrimary: false, isEnabled: _canGoBack);
    UpdateButtonEnabledVisualState(_nextButton, isPrimary: true, isEnabled: _canProceed);
    UpdateButtonEnabledVisualState(_cancelButton, isPrimary: false, isEnabled: _canCancel);
  }

  private static void ApplySecondaryButtonStyle(Button button)
  {
    ApplyButtonBaseStyle(button);
    SetButtonColors(
      button,
      SecondaryButtonBackColor,
      SecondaryButtonHoverBackColor,
      SecondaryButtonBorderColor,
      SecondaryButtonBackColor,
      SecondaryButtonBorderColor,
      Color.FromArgb(228, 228, 231));
  }

  private static void ApplyPrimaryButtonStyle(Button button)
  {
    ApplyButtonBaseStyle(button);
    SetButtonColors(
      button,
      PrimaryButtonBackColor,
      PrimaryButtonHoverBackColor,
      PrimaryButtonBorderColor,
      PrimaryButtonBackColor,
      PrimaryButtonBorderColor,
      Color.White);
  }

  private static void ApplyButtonBaseStyle(Button button)
  {
    button.FlatStyle = FlatStyle.Flat;
    button.FlatAppearance.BorderSize = 1;
    button.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
    button.Cursor = Cursors.Hand;
  }

  private static void SetButtonColors(
    Button button,
    Color normalBack,
    Color hoverBack,
    Color border,
    Color downBack,
    Color downBorder,
    Color foreColor)
  {
    button.BackColor = normalBack;
    button.ForeColor = foreColor;
    button.FlatAppearance.BorderColor = border;
    button.FlatAppearance.MouseOverBackColor = hoverBack;
    button.FlatAppearance.MouseDownBackColor = downBack;
  }

  private static void UpdateButtonEnabledVisualState(Button button, bool isPrimary, bool isEnabled)
  {
    if (isEnabled)
    {
      if (isPrimary)
      {
        SetButtonColors(
          button,
          PrimaryButtonBackColor,
          PrimaryButtonHoverBackColor,
          PrimaryButtonBorderColor,
          PrimaryButtonBackColor,
          PrimaryButtonBorderColor,
          Color.White);
      }
      else
      {
        SetButtonColors(
          button,
          SecondaryButtonBackColor,
          SecondaryButtonHoverBackColor,
          SecondaryButtonBorderColor,
          SecondaryButtonBackColor,
          SecondaryButtonBorderColor,
          Color.FromArgb(228, 228, 231));
      }
    }
    else
    {
      button.BackColor = DisabledButtonBackColor;
      button.ForeColor = Color.FromArgb(161, 161, 170);
      button.FlatAppearance.BorderColor = DisabledButtonBorderColor;
      button.FlatAppearance.MouseOverBackColor = DisabledButtonBackColor;
      button.FlatAppearance.MouseDownBackColor = DisabledButtonBackColor;
    }
  }

  protected override void OnFormClosed(FormClosedEventArgs e)
  {
    if (_eulaMouseWheelFilter != null)
    {
      Application.RemoveMessageFilter(_eulaMouseWheelFilter);
      _eulaMouseWheelFilter = null;
    }

    _eulaScrollAnimationTimer.Stop();
    _eulaScrollAnimationTimer.Dispose();
    _headerAnimationTimer.Stop();
    _headerAnimationTimer.Dispose();
    _cachedHeaderBackground?.Dispose();
    base.OnFormClosed(e);
  }
}
