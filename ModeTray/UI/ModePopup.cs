using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ID6ModeTray;

internal sealed class ModePopup : Form
{
    private const int CornerRadius = 22;
    private const int PopupWidth = 470;
    private const int ContentWidth = 420;
    private const int SectionLabelWidth = 396;
    private const string UiFontName = "Microsoft YaHei UI";
    private readonly List<(ModeTile Tile, ModeDefinition Mode)> modeTiles = [];
    private readonly FlowLayoutPanel content;
    private readonly SafeTextLabel statusLabel;
    private readonly Button turboOcButton;
    private readonly Button fanBoostButton;
    private readonly SafeTextLabel customSectionLabel;
    private readonly Action<ModeDefinition> switchMode;
    private readonly Action<bool> setTurboOc;
    private readonly Action<bool> setFanBoost;
    private bool busy;
    private bool turboModeActive;
    private bool turboOcEnabled;
    private bool fanBoostEnabled;

    public ModePopup(
        IReadOnlyList<ModeDefinition> modes,
        Action<ModeDefinition> switchMode,
        Action<bool> setTurboOc,
        Action<bool> setFanBoost,
        Action refresh,
        Action exit)
    {
        this.switchMode = switchMode;
        this.setTurboOc = setTurboOc;
        this.setFanBoost = setFanBoost;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        BackColor = Color.FromArgb(18, 20, 25);
        ClientSize = new Size(PopupWidth, 760);
        DoubleBuffered = true;
        Font = new Font(UiFontName, 10F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(14);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        content = new FlowLayoutPanel
        {
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = true,
            Padding = new Padding(10, 8, 10, 12),
            WrapContents = false
        };
        Controls.Add(content);

        var header = new Panel
        {
            BackColor = Color.Transparent,
            Margin = new Padding(8, 4, 8, 18),
            Padding = new Padding(66, 7, 8, 9),
            Size = new Size(ContentWidth, 104)
        };
        header.Controls.Add(new PictureBox
        {
            Image = IconFactory.CreateBitmap(44),
            Location = new Point(2, 22),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        });

        var headerText = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2,
        };
        headerText.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        headerText.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        header.Controls.Add(headerText);

        var titleLabel = new Label
        {
            AutoEllipsis = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Margin = new Padding(0),
            Text = "ID6 Mode Tray",
            TextAlign = ContentAlignment.MiddleLeft
        };
        headerText.Controls.Add(titleLabel, 0, 0);

        statusLabel = new SafeTextLabel
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(143, 151, 166),
            Font = new Font(UiFontName, 9.5F),
            Margin = new Padding(1, 0, 0, 4),
            Text = "\u6b63\u5728\u8bfb\u53d6\u5f53\u524d\u6a21\u5f0f"
        };
        headerText.Controls.Add(statusLabel, 0, 1);
        content.Controls.Add(header);

        AddSectionLabel("\u6807\u51c6\u6a21\u5f0f");
        foreach (var mode in modes.Where(mode => mode.OperatingMode != 3))
        {
            AddMode(mode);
        }

        turboOcButton = new Button
        {
            BackColor = Color.FromArgb(27, 30, 36),
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(160, 167, 181),
            Font = new Font(UiFontName, 9.5F, FontStyle.Bold),
            Margin = new Padding(8, 8, 8, 6),
            Padding = new Padding(0, 2, 0, 4),
            Size = new Size(ContentWidth, 54),
            Text = "GPU OC +150 MHz  ·  仅狂暴模式"
        };
        turboOcButton.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 71);
        turboOcButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 43, 52);
        turboOcButton.Click += (_, _) =>
        {
            if (!busy && turboModeActive)
            {
                setTurboOc(!turboOcEnabled);
            }
        };
        content.Controls.Add(turboOcButton);

        fanBoostButton = new Button
        {
            BackColor = Color.FromArgb(27, 30, 36),
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(224, 227, 234),
            Font = new Font(UiFontName, 9.5F, FontStyle.Bold),
            Margin = new Padding(8, 2, 8, 6),
            Padding = new Padding(0, 2, 0, 4),
            Size = new Size(ContentWidth, 54),
            Text = "一键强冷  ·  已关闭"
        };
        fanBoostButton.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 71);
        fanBoostButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 43, 52);
        fanBoostButton.Click += (_, _) =>
        {
            if (!busy)
            {
                setFanBoost(!fanBoostEnabled);
            }
        };
        content.Controls.Add(fanBoostButton);

        customSectionLabel = AddSectionLabel("\u81ea\u5b9a\u4e49\u6a21\u5f0f");
        foreach (var mode in modes.Where(mode => mode.OperatingMode == 3))
        {
            AddMode(mode);
        }

        var footer = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 3,
            Margin = new Padding(8, 18, 8, 2),
            RowCount = 1,
            Size = new Size(ContentWidth, 62)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        footer.Controls.Add(CreateFooterButton("\u5237\u65b0", (_, _) => refresh()), 0, 0);
        footer.Controls.Add(CreateFooterButton("\u667a\u63a7\u4e2d\u5fc3", (_, _) => SafeOpenControlCenter()), 1, 0);
        footer.Controls.Add(CreateFooterButton(
            StartupManager.IsEnabled ? "\u5df2\u542f\u7528" : "\u5f00\u673a\u542f\u52a8",
            (sender, _) =>
            {
                StartupManager.IsEnabled = !StartupManager.IsEnabled;
                ((Button)sender!).Text = StartupManager.IsEnabled
                    ? "\u5df2\u542f\u7528"
                    : "\u5f00\u673a\u542f\u52a8";
            }), 2, 0);
        content.Controls.Add(footer);

        var exitButton = new Button
        {
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(150, 158, 172),
            Font = new Font(UiFontName, 10F),
            Margin = new Padding(8, 8, 8, 4),
            Padding = new Padding(0, 1, 0, 4),
            Size = new Size(ContentWidth, 50),
            Text = "\u9000\u51fa"
        };
        exitButton.FlatAppearance.BorderSize = 0;
        exitButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 41, 49);
        exitButton.Click += (_, _) => exit();
        content.Controls.Add(exitButton);

        Deactivate += (_, _) => Hide();
        Paint += (_, args) =>
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(53, 58, 69));
            args.Graphics.DrawPath(pen, RoundedRectangle(ClientRectangle, CornerRadius));
        };
        Resize += (_, _) => ApplyRoundedRegion();
        content.ClientSizeChanged += (_, _) => UpdateContentWidths();
        Shown += (_, _) => FitToContent();
        DpiChanged += (_, _) =>
        {
            if (Visible)
            {
                FitToContent();
                RepositionNearTray();
            }
        };
    }

    protected override bool ShowWithoutActivation => false;

    public void ShowNearTray()
    {
        FitToContent();
        RepositionNearTray();
        Show();
        Activate();
        RepositionNearTray();
        FocusSelectedMode();
    }

    private void RepositionNearTray()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        Location = PopupPlacement.Calculate(
            screen.Bounds,
            screen.WorkingArea,
            Size,
            cursor,
            LogicalToDeviceUnits(8));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void FocusSelectedMode()
    {
        foreach (var (tile, _) in modeTiles)
        {
            if (tile.Selected && tile.Enabled)
            {
                tile.Focus();
                return;
            }
        }

        if (modeTiles.Count > 0 && modeTiles[0].Tile.Enabled)
        {
            modeTiles[0].Tile.Focus();
        }
    }

    public void SetModes(IReadOnlyList<ModeDefinition> modes)
    {
        foreach (var entry in modeTiles.Where(entry => entry.Mode.OperatingMode == 3).ToArray())
        {
            content.Controls.Remove(entry.Tile);
            modeTiles.Remove(entry);
            entry.Tile.Dispose();
        }

        var insertionIndex = content.Controls.IndexOf(customSectionLabel) + 1;
        foreach (var mode in modes.Where(mode => mode.OperatingMode == 3))
        {
            AddMode(mode, insertionIndex++);
        }

        FitToContent();
    }

    public void UpdateState(
        ModeState? state,
        ModeStateError? error = null,
        bool preserveStatus = false)
    {
        ModeDefinition? active = null;
        foreach (var (tile, mode) in modeTiles)
        {
            var selected = state is not null && mode.Matches(state.Value);
            tile.SetSelected(selected);
            if (selected)
            {
                active = mode;
            }
        }

        if (!preserveStatus)
        {
            statusLabel.Text = active is null
                ? error?.UserMessage ?? "\u672a\u8bfb\u53d6\u5230\u5f53\u524d\u6a21\u5f0f"
                : $"\u5f53\u524d\uff1a{active.Label}";
        }

        turboModeActive = state?.OperatingMode == 2;
        turboOcEnabled = turboModeActive && state?.TurboGpuOcOffset == 150;
        fanBoostEnabled = state?.FanBoostEnabled == 1;
        UpdateTurboOcButton();
        UpdateFanBoostButton();
    }

    public void SetBusy(bool value)
    {
        busy = value;
        foreach (var (tile, _) in modeTiles)
        {
            tile.Enabled = !value;
        }
        UpdateTurboOcButton();
        UpdateFanBoostButton();
        statusLabel.Text = value ? "\u6b63\u5728\u5207\u6362\u2026" : statusLabel.Text;
        Cursor = value ? Cursors.WaitCursor : Cursors.Default;
    }

    private void UpdateTurboOcButton()
    {
        turboOcButton.Enabled = !busy && turboModeActive;
        turboOcButton.Cursor = turboOcButton.Enabled ? Cursors.Hand : Cursors.Default;
        turboOcButton.BackColor = turboOcEnabled
            ? Color.FromArgb(32, 64, 72)
            : Color.FromArgb(27, 30, 36);
        turboOcButton.ForeColor = turboModeActive
            ? turboOcEnabled ? Color.FromArgb(57, 211, 181) : Color.FromArgb(224, 227, 234)
            : Color.FromArgb(105, 112, 125);
        turboOcButton.FlatAppearance.BorderColor = turboOcEnabled
            ? Color.FromArgb(57, 211, 181)
            : Color.FromArgb(55, 60, 71);
        turboOcButton.Text = turboModeActive
            ? $"GPU OC +150 MHz  ·  {(turboOcEnabled ? "已开启" : "已关闭")}"
            : "GPU OC +150 MHz  ·  仅狂暴模式";
    }

    private SafeTextLabel AddSectionLabel(string text)
    {
        var label = new SafeTextLabel
        {
            ForeColor = Color.FromArgb(130, 138, 153),
            Font = new Font(UiFontName, 8.5F, FontStyle.Bold),
            Margin = new Padding(14, 12, 8, 8),
            Size = new Size(SectionLabelWidth, 30),
            Text = text.ToUpperInvariant()
        };
        label.Tag = "section";
        content.Controls.Add(label);
        return label;
    }

    private void UpdateFanBoostButton()
    {
        fanBoostButton.Enabled = !busy;
        fanBoostButton.Cursor = fanBoostButton.Enabled ? Cursors.Hand : Cursors.Default;
        fanBoostButton.BackColor = fanBoostEnabled
            ? Color.FromArgb(32, 64, 72)
            : Color.FromArgb(27, 30, 36);
        fanBoostButton.ForeColor = fanBoostEnabled
            ? Color.FromArgb(57, 211, 181)
            : Color.FromArgb(224, 227, 234);
        fanBoostButton.FlatAppearance.BorderColor = fanBoostEnabled
            ? Color.FromArgb(57, 211, 181)
            : Color.FromArgb(55, 60, 71);
        fanBoostButton.Text = $"一键强冷  ·  {(fanBoostEnabled ? "已开启" : "已关闭")}";
    }

    private void AddMode(ModeDefinition mode, int? insertionIndex = null)
    {
        var tile = new ModeTile(mode.Label)
        {
            Enabled = !busy,
            Margin = new Padding(8, 4, 8, 4),
            Size = new Size(ContentWidth, 64)
        };
        tile.Click += (_, _) =>
        {
            if (!busy)
            {
                switchMode(mode);
            }
        };
        modeTiles.Add((tile, mode));
        content.Controls.Add(tile);
        if (insertionIndex is not null)
        {
            content.Controls.SetChildIndex(tile, insertionIndex.Value);
        }
    }

    private Button CreateFooterButton(string text, EventHandler click)
    {
        var button = new Button
        {
            BackColor = Color.FromArgb(31, 34, 41),
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(218, 222, 231),
            Font = new Font(UiFontName, 9F),
            Margin = new Padding(4, 3, 4, 3),
            Padding = new Padding(0, 1, 0, 4),
            Text = text
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 71);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 48, 58);
        button.Click += click;
        return button;
    }

    private void FitToContent()
    {
        var desiredClientHeight = content.Padding.Vertical;
        foreach (Control control in content.Controls)
        {
            desiredClientHeight += control.Height + control.Margin.Vertical;
        }

        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var desiredHeight = desiredClientHeight + Padding.Vertical;
        ClientSize = new Size(
            PopupPlacement.FitWidth(
                PopupWidth,
                workingArea.Width,
                LogicalToDeviceUnits(16)),
            PopupPlacement.FitHeight(
                desiredHeight,
                workingArea.Height,
                LogicalToDeviceUnits(32)));
        content.AutoScroll = desiredHeight > ClientSize.Height;
        UpdateContentWidths();
    }

    private void UpdateContentWidths()
    {
        if (content.ClientSize.Width <= 0)
        {
            return;
        }

        var availableWidth = content.ClientSize.Width - content.Padding.Horizontal;
        foreach (Control control in content.Controls)
        {
            var width = availableWidth - control.Margin.Horizontal;
            if (control.Tag as string == "section")
            {
                width -= 24;
            }
            control.Width = Math.Max(120, width);
        }
    }

    private static void SafeOpenControlCenter()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = @"shell:AppsFolder\CCU.WinUI_wrbgcf7aesyd8!App",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Open Control Center failed: {ex}");
            MessageBox.Show(
                "\u65e0\u6cd5\u6253\u5f00\u667a\u63a7\u4e2d\u5fc3\u3002\u8bf7\u786e\u8ba4\u5e94\u7528\u5df2\u6b63\u786e\u5b89\u88c5\u3002",
                "ID6 Mode Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRectangle(ClientRectangle, CornerRadius);
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
