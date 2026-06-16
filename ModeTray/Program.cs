using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace ID6ModeTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.ThreadException += (_, eventArgs) =>
            DiagnosticLog.Write($"Unhandled UI exception: {eventArgs.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            DiagnosticLog.Write($"Unhandled process exception: {eventArgs.ExceptionObject}");

        if (args.Length == 2 && args[0].Equals("--fan-boost", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var enabled = args[1].ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException("一键强冷参数应为 on 或 off。")
                };
                ModeSwitcher.SetFanBoost(enabled);
                DiagnosticLog.Write($"Command-line fan boost switch completed: {args[1]}");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Command-line fan boost switch failed: {ex}");
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length == 2 && args[0].Equals("--oc", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var enabled = args[1].ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException("OC 参数应为 on 或 off。")
                };
                ModeSwitcher.SetTurboOc(enabled);
                DiagnosticLog.Write($"Command-line OC switch completed: {args[1]}");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Command-line OC switch failed: {ex}");
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length == 2 && args[0].Equals("--switch", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ModeSwitcher.Switch(ModeDefinition.Parse(args[1]));
                DiagnosticLog.Write($"Command-line switch completed: {args[1]}");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Command-line switch failed: {ex}");
                Environment.ExitCode = 1;
            }
            return;
        }

        using var mutex = new Mutex(true, @"Local\ID6ModeTray", out var createdNew);
        if (!createdNew)
        {
            DiagnosticLog.Write("Tray startup skipped because another instance owns the mutex.");
            MessageBox.Show("\u6a21\u5f0f\u5207\u6362\u5668\u5df2\u7ecf\u5728\u8fd0\u884c\u3002", "ID6 Mode Tray");
            return;
        }

        try
        {
            DiagnosticLog.Write($"Tray startup begin: {Environment.ProcessPath}");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
            DiagnosticLog.Write("Tray message loop exited normally.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Tray startup failed: {ex}");
            MessageBox.Show(
                $"托盘程序启动失败。\n\n{ex.Message}\n\n详细信息已写入 ID6ModeTray.log。",
                "ID6 Mode Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly ModePopup popup;
    private readonly ModeToast toast;
    private readonly System.Windows.Forms.Timer refreshTimer;

    public TrayApplicationContext()
    {
        popup = new ModePopup(SwitchMode, SetTurboOc, SetFanBoost, ExitThread);
        toast = new ModeToast();
        notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "ID6 Mode Tray",
            Visible = true
        };
        notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button is MouseButtons.Left or MouseButtons.Right)
            {
                TogglePopup();
            }
        };

        refreshTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        refreshTimer.Tick += (_, _) => RefreshState();
        refreshTimer.Start();
        RefreshState();
        DiagnosticLog.Write("Tray application context initialized.");
    }

    private void TogglePopup()
    {
        if (popup.Visible)
        {
            popup.Hide();
            return;
        }

        RefreshState();
        popup.ShowNearTray();
    }

    private void SwitchMode(ModeDefinition mode)
    {
        popup.SetBusy(true);
        try
        {
            ModeSwitcher.Switch(mode);
            RefreshState();
            toast.ShowMessage("\u6a21\u5f0f\u5df2\u5207\u6362", mode.Label, false);
        }
        catch (Exception ex)
        {
            toast.ShowMessage("\u5207\u6362\u5931\u8d25", ex.Message, true);
        }
        finally
        {
            popup.SetBusy(false);
        }
    }

    private void SetTurboOc(bool enabled)
    {
        popup.SetBusy(true);
        try
        {
            ModeSwitcher.SetTurboOc(enabled);
            RefreshState();
            toast.ShowMessage(
                enabled ? "GPU OC 已开启" : "GPU OC 已关闭",
                enabled ? "核心频率偏移 +150 MHz" : "核心频率偏移已恢复为 0",
                false);
        }
        catch (Exception ex)
        {
            toast.ShowMessage("GPU OC 切换失败", ex.Message, true);
        }
        finally
        {
            popup.SetBusy(false);
        }
    }

    private void SetFanBoost(bool enabled)
    {
        popup.SetBusy(true);
        try
        {
            ModeSwitcher.SetFanBoost(enabled);
            RefreshState();
            toast.ShowMessage(
                enabled ? "一键强冷已开启" : "一键强冷已关闭",
                enabled ? "CPU 与 GPU 风扇已进入强冷状态" : "风扇已恢复当前模式控制",
                false);
        }
        catch (Exception ex)
        {
            toast.ShowMessage("一键强冷切换失败", ex.Message, true);
        }
        finally
        {
            popup.SetBusy(false);
        }
    }

    private void RefreshState()
    {
        var state = ModeState.TryRead();
        popup.UpdateState(state);
        var active = ModeDefinition.Load().FirstOrDefault(mode => state is not null && mode.Matches(state.Value));
        notifyIcon.Text = active is null ? "ID6 Mode Tray" : $"ID6 - {active.Label}";
    }

    protected override void ExitThreadCore()
    {
        DiagnosticLog.Write("Tray exit requested.");
        refreshTimer.Stop();
        refreshTimer.Dispose();
        toast.Dispose();
        popup.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class ModeToast : Form
{
    private const int CornerRadius = 16;
    private readonly SafeTextLabel titleLabel;
    private readonly SafeTextLabel messageLabel;
    private readonly Panel accent;
    private readonly System.Windows.Forms.Timer hideTimer;

    public ModeToast()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        BackColor = Color.FromArgb(24, 27, 33);
        ClientSize = new Size(360, 112);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(14);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        accent = new Panel
        {
            BackColor = Color.FromArgb(57, 211, 181),
            Dock = DockStyle.Left,
            Width = 4
        };
        Controls.Add(accent);

        var icon = new PictureBox
        {
            Image = IconFactory.CreateBitmap(40),
            Location = new Point(24, 36),
            Size = new Size(40, 40),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        Controls.Add(icon);

        titleLabel = new SafeTextLabel
        {
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            Location = new Point(78, 18),
            Size = new Size(264, 36)
        };
        Controls.Add(titleLabel);

        messageLabel = new SafeTextLabel
        {
            ForeColor = Color.FromArgb(180, 187, 200),
            Font = new Font("Microsoft YaHei UI", 9F),
            Location = new Point(79, 52),
            Size = new Size(263, 34)
        };
        Controls.Add(messageLabel);

        hideTimer = new System.Windows.Forms.Timer { Interval = 2600 };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
        Click += (_, _) => Hide();
        foreach (Control control in Controls)
        {
            control.Click += (_, _) => Hide();
        }
        Paint += (_, args) =>
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(Color.FromArgb(58, 63, 74));
            args.Graphics.DrawPath(border, RoundedRectangle(ClientRectangle, CornerRadius));
        };
        Resize += (_, _) => ApplyRoundedRegion();
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowMessage(string title, string message, bool isError)
    {
        titleLabel.Text = title;
        messageLabel.Text = message;
        accent.BackColor = isError
            ? Color.FromArgb(244, 91, 105)
            : Color.FromArgb(57, 211, 181);

        var workingArea = Screen.PrimaryScreen?.WorkingArea
            ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            workingArea.Right - Width - 18,
            workingArea.Bottom - Height - 18);
        ApplyRoundedRegion();
        hideTimer.Stop();
        Show();
        hideTimer.Start();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hideTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

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
    private readonly Action<ModeDefinition> switchMode;
    private readonly Action<bool> setTurboOc;
    private readonly Action<bool> setFanBoost;
    private bool busy;
    private bool turboModeActive;
    private bool turboOcEnabled;
    private bool fanBoostEnabled;

    public ModePopup(
        Action<ModeDefinition> switchMode,
        Action<bool> setTurboOc,
        Action<bool> setFanBoost,
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
        var modes = ModeDefinition.Load();
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

        AddSectionLabel("\u81ea\u5b9a\u4e49\u6a21\u5f0f");
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
        footer.Controls.Add(CreateFooterButton("\u5237\u65b0", (_, _) => UpdateState(ModeState.TryRead())), 0, 0);
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
    }

    protected override bool ShowWithoutActivation => false;

    public void ShowNearTray()
    {
        FitToContent();
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        var x = Math.Min(Cursor.Position.X - Width + 24, screen.Right - Width - 8);
        var y = screen.Bottom - Height - 8;
        Location = new Point(Math.Max(screen.Left + 8, x), Math.Max(screen.Top + 8, y));
        Show();
        Activate();
    }

    public void UpdateState(ModeState? state)
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

        statusLabel.Text = active is null
            ? "\u672a\u8bfb\u53d6\u5230\u5f53\u524d\u6a21\u5f0f"
            : $"\u5f53\u524d\uff1a{active.Label}";

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
        Application.DoEvents();
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

    private void AddSectionLabel(string text)
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

    private void AddMode(ModeDefinition mode)
    {
        var tile = new ModeTile(mode.Label)
        {
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
        var maxClientHeight = Math.Max(520, workingArea.Height - 32);
        ClientSize = new Size(PopupWidth, Math.Min(desiredClientHeight + Padding.Vertical, maxClientHeight));
        content.AutoScroll = desiredClientHeight > ClientSize.Height;
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

internal sealed class SafeTextLabel : Control
{
    public SafeTextLabel()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var brush = new SolidBrush(ForeColor);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        var bounds = new RectangleF(0, -2, Width, Height + 4);
        e.Graphics.DrawString(Text, Font, brush, bounds, format);
    }
}

internal sealed class ModeTile : Control
{
    private bool hovered;
    private bool selected;

    public ModeTile(string text)
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Text = text;
        Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }

    public void SetSelected(bool value)
    {
        selected = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 12);
        var background = selected
            ? Color.FromArgb(32, 64, 72)
            : hovered ? Color.FromArgb(40, 43, 52) : Color.FromArgb(27, 30, 36);
        using var brush = new SolidBrush(background);
        e.Graphics.FillPath(brush, path);

        if (selected)
        {
            using var border = new Pen(Color.FromArgb(57, 211, 181), 1.5F);
            e.Graphics.DrawPath(border, path);
        }

        var dotRect = new Rectangle(17, Height / 2 - 5, 10, 10);
        using var dot = new SolidBrush(selected ? Color.FromArgb(57, 211, 181) : Color.FromArgb(82, 89, 103));
        e.Graphics.FillEllipse(dot, dotRect);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(42, 2, Width - 72, Height - 6),
            selected ? Color.White : Color.FromArgb(224, 227, 234),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (selected)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "\u2713",
                new Font(Font.FontFamily, 11F, FontStyle.Bold),
                new Rectangle(Width - 38, 2, 24, Height - 6),
                Color.FromArgb(57, 211, 181),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class IconFactory
{
    public static Icon CreateTrayIcon()
    {
        using var bitmap = CreateBitmap(64);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Bitmap CreateBitmap(int size)
    {
        var bitmap = new Bitmap(size, size);
        bitmap.SetResolution(192, 192);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var padding = size * 0.08F;
        var bounds = new RectangleF(padding, padding, size - padding * 2, size - padding * 2);
        using var background = new LinearGradientBrush(
            bounds,
            Color.FromArgb(35, 225, 185),
            Color.FromArgb(36, 120, 255),
            45F);
        using var shape = Hexagon(bounds);
        graphics.FillPath(background, shape);

        using var innerPen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(2F, size * 0.075F))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var points = new[]
        {
            new PointF(size * 0.27F, size * 0.58F),
            new PointF(size * 0.43F, size * 0.36F),
            new PointF(size * 0.43F, size * 0.64F),
            new PointF(size * 0.72F, size * 0.34F)
        };
        graphics.DrawLines(innerPen, points);
        return bitmap;
    }

    private static GraphicsPath Hexagon(RectangleF rect)
    {
        var path = new GraphicsPath();
        path.AddPolygon(
        [
            new PointF(rect.Left + rect.Width * 0.25F, rect.Top),
            new PointF(rect.Left + rect.Width * 0.75F, rect.Top),
            new PointF(rect.Right, rect.Top + rect.Height * 0.5F),
            new PointF(rect.Left + rect.Width * 0.75F, rect.Bottom),
            new PointF(rect.Left + rect.Width * 0.25F, rect.Bottom),
            new PointF(rect.Left, rect.Top + rect.Height * 0.5F)
        ]);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ID6ModeTray";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
    }
}

internal static class ModeSwitcher
{
    public static void Switch(ModeDefinition mode)
    {
        var client = new MqttClient(
            "127.0.0.1",
            13688,
            false,
            null,
            null,
            MqttSslProtocols.None);
        try
        {
            var result = client.Connect(
                "PluginClient_19",
                "PluginClient_User_19",
                "PluginClient_Pwd888881772688_19");
            DiagnosticLog.Write($"MQTT connect result={result}, connected={client.IsConnected}");
            if (result != 0 || !client.IsConnected)
            {
                throw new InvalidOperationException($"无法连接 GCUBridge，MQTT 返回码 {result}。");
            }

            PublishAndWait(client, new
            {
                Action = mode.Action,
                ProfileIndex = mode.ProfileIndex
            });

            if (mode.OperatingMode == 2 && mode.Silent is not null && mode.Extreme is not null)
            {
                WaitForState(state => state.OperatingMode == 2, TimeSpan.FromSeconds(5));
                if (mode.Silent == 1)
                {
                    PublishAndWait(client, new
                    {
                        Action = "SET_CPU_CORE_OFFSET_SILENT",
                        SILENT = 1
                    });
                }
                else
                {
                    PublishAndWait(client, new
                    {
                        Action = "SET_CPU_CORE_OFFSET_EXTREME",
                        EXTREME = 1
                    });
                }
            }

            WaitForState(mode.Matches, TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    public static void SetTurboOc(bool enabled)
    {
        var state = ModeState.TryRead();
        if (state is null || state.Value.OperatingMode != 2)
        {
            throw new InvalidOperationException("GPU OC 只能在狂暴模式下切换。");
        }

        var client = new MqttClient(
            "127.0.0.1",
            13688,
            false,
            null,
            null,
            MqttSslProtocols.None);
        try
        {
            var result = client.Connect(
                "PluginClient_19",
                "PluginClient_User_19",
                "PluginClient_Pwd888881772688_19");
            DiagnosticLog.Write($"MQTT OC connect result={result}, connected={client.IsConnected}");
            if (result != 0 || !client.IsConnected)
            {
                throw new InvalidOperationException($"无法连接 GCUBridge，MQTT 返回码 {result}。");
            }

            var target = enabled ? 150 : 0;
            PublishAndWait(client, new
            {
                Action = "SET_OPERATING_MODE_DETAIL",
                GpuCoreClockOffsetOC = target
            });
            WaitForState(
                current => current.OperatingMode == 2 && current.TurboGpuOcOffset == target,
                TimeSpan.FromSeconds(5));
            DiagnosticLog.Write($"GPU OC switched to {target} MHz");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    public static void SetFanBoost(bool enabled)
    {
        var client = new MqttClient(
            "127.0.0.1",
            13688,
            false,
            null,
            null,
            MqttSslProtocols.None);
        try
        {
            var result = client.Connect(
                "PluginClient_19",
                "PluginClient_User_19",
                "PluginClient_Pwd888881772688_19");
            DiagnosticLog.Write($"MQTT fan boost connect result={result}, connected={client.IsConnected}");
            if (result != 0 || !client.IsConnected)
            {
                throw new InvalidOperationException($"无法连接 GCUBridge，MQTT 返回码 {result}。");
            }

            PublishAndWait(client, new
            {
                Action = enabled ? "FAN_BOOST_ON" : "FAN_BOOST_OFF"
            });
            WaitForState(
                state => state.FanBoostEnabled == (enabled ? 1 : 0),
                TimeSpan.FromSeconds(5));
            DiagnosticLog.Write($"Fan boost switched to {(enabled ? "on" : "off")}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    private static void PublishAndWait(MqttClient client, object message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            using var published = new ManualResetEventSlim(false);
            ushort messageId = 0;
            client.MqttMsgPublished += (_, args) =>
            {
                if (args.MessageId == messageId && args.IsPublished)
                {
                    published.Set();
                }
            };
            messageId = client.Publish(
                "Fan/Control",
                payload,
                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                false);
            if (!published.Wait(TimeSpan.FromSeconds(3)))
            {
                throw new TimeoutException("GCUBridge 未确认收到切换命令。");
            }
    }

    private static void WaitForState(Func<ModeState, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var state = ModeState.TryRead();
            if (state is not null && predicate(state.Value))
            {
                return;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("命令已发送，但模式状态没有在 5 秒内更新。");
    }
}

internal static class DiagnosticLog
{
    private static readonly object Sync = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "ID6ModeTray.log");

    public static void Write(string message)
    {
        lock (Sync)
        {
            File.AppendAllText(PathName, $"{DateTime.Now:O} {message}{Environment.NewLine}", Encoding.UTF8);
        }
    }
}

internal readonly record struct ModeState(
    int OperatingMode,
    int CustomProfileIndex,
    int TurboProfileIndex,
    int TurboSilent,
    int TurboExtreme,
    int TurboGpuOcOffset,
    int FanBoostEnabled)
{
    public static ModeState? TryRead()
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(ControlCenterPaths.MainOption));
            var operatingMode = json?["OperatingMode"]?.GetValue<int>() ?? -1;
            var turboIndex = json?["TurboProfileIndex"]?.GetValue<int>() ?? 0;
            var turboSilent = -1;
            var turboExtreme = -1;
            if (operatingMode == 2)
            {
                var profile = JsonNode.Parse(File.ReadAllText(ControlCenterPaths.TurboProfile(turboIndex)));
                turboSilent = profile?["Silent"]?.GetValue<int>() ?? -1;
                turboExtreme = profile?["Extreme"]?.GetValue<int>() ?? -1;
            }
            return new ModeState(
                operatingMode,
                json?["CustomProfileIndex"]?.GetValue<int>() ?? -1,
                turboIndex,
                turboSilent,
                turboExtreme,
                json?["TurboGPUOCOffset"]?.GetValue<int>() ?? 0,
                json?["FanBoostEnable"]?.GetValue<int>() ?? 0);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record ModeDefinition(
    string Label,
    string Action,
    int OperatingMode,
    int ProfileIndex,
    int? Silent = null,
    int? Extreme = null)
{
    public static IReadOnlyList<ModeDefinition> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "profiles.json");
        var configured = File.Exists(path)
            ? JsonSerializer.Deserialize<List<ModeDefinition>>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;
        configured ??= Defaults.ToList();

        var modes = configured.Where(mode => mode.OperatingMode != 3).ToList();
        var customOverrides = configured
            .Where(mode => mode.OperatingMode == 3)
            .GroupBy(mode => mode.ProfileIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var controlCenterAliases = ControlCenterProfileNames.Load();

        try
        {
            foreach (var profile in ControlCenterPaths.CustomProfiles())
            {
                var json = JsonNode.Parse(File.ReadAllText(profile.Path));
                if (json?["Activated"]?.GetValue<bool>() != true)
                {
                    continue;
                }

                customOverrides.TryGetValue(profile.Index, out var profileOverride);
                var customizeName = json?["CustomizeName"]?.GetValue<string>()?.Trim();
                var oemLabel = !string.IsNullOrWhiteSpace(customizeName) &&
                               !int.TryParse(customizeName, out _)
                    ? customizeName
                    : null;
                var alias = controlCenterAliases.GetValueOrDefault(profile.Index);
                var label = !string.IsNullOrWhiteSpace(alias)
                    ? $"Profile {profile.Index + 1} - {alias}"
                    : profileOverride?.Label;
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = oemLabel is null
                        ? $"Profile {profile.Index + 1}"
                        : $"Profile {profile.Index + 1} - {oemLabel}";
                }

                modes.Add(new ModeDefinition(
                    label,
                    "OPERATING_CUSTOM_MODE",
                    3,
                    profile.Index));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Custom profile discovery failed: {ex}");
            modes.AddRange(configured.Where(mode => mode.OperatingMode == 3));
        }

        return modes;
    }

    public bool Matches(ModeState state) =>
        state.OperatingMode == OperatingMode &&
        (OperatingMode != 3 || state.CustomProfileIndex == ProfileIndex) &&
        (OperatingMode != 2 ||
            (state.TurboProfileIndex == ProfileIndex &&
             (Silent is null || state.TurboSilent == Silent) &&
             (Extreme is null || state.TurboExtreme == Extreme)));

    public static ModeDefinition Parse(string value)
    {
        var parts = value.Split(':', 2);
        var modes = Load();
        return value.ToLowerInvariant() switch
        {
            "office" => modes.First(mode => mode.OperatingMode == 0),
            "gaming" => modes.First(mode => mode.OperatingMode == 1),
            "turbo" => modes.First(mode => mode.OperatingMode == 2 && mode.Extreme == 1),
            "turbo:silent" => modes.First(mode => mode.OperatingMode == 2 && mode.Silent == 1),
            "turbo:extreme" => modes.First(mode => mode.OperatingMode == 2 && mode.Extreme == 1),
            _ when parts[0].Equals("custom", StringComparison.OrdinalIgnoreCase) &&
                   parts.Length == 2 &&
                   int.TryParse(parts[1], out var slot) &&
                   slot >= 1 =>
                modes.FirstOrDefault(mode =>
                    mode.OperatingMode == 3 &&
                    mode.ProfileIndex == slot - 1)
                ?? throw new ArgumentException($"自定义 Profile {slot} 未启用或不存在。"),
            _ => throw new ArgumentException("模式格式应为 office、gaming、turbo 或 custom:N。")
        };
    }

    private static readonly IReadOnlyList<ModeDefinition> Defaults =
    [
        new("办公模式", "OPERATING_OFFICE_MODE", 0, 0),
        new("游戏模式", "OPERATING_GAMING_MODE", 1, 0),
        new("狂暴 · 静技", "OPERATING_TURBO_MODE", 2, 0, 1, 0),
        new("狂暴 · 极速", "OPERATING_TURBO_MODE", 2, 0, 0, 1),
        new("Profile 1 - 静音高性能", "OPERATING_CUSTOM_MODE", 3, 0),
        new("Profile 2 - 静音办公", "OPERATING_CUSTOM_MODE", 3, 1),
        new("Profile 3 - 静音游戏", "OPERATING_CUSTOM_MODE", 3, 2),
        new("Profile 4", "OPERATING_CUSTOM_MODE", 3, 3)
    ];
}

internal static class ControlCenterProfileNames
{
    private const string PackageFamilyName = "CCU.WinUI_wrbgcf7aesyd8";
    private static IReadOnlyDictionary<int, string> cachedAliases =
        new Dictionary<int, string>();

    public static IReadOnlyDictionary<int, string> Load()
    {
        var aliases = new Dictionary<int, string>();
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                PackageFamilyName,
                "Settings",
                "settings.dat");
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var hiveText = Encoding.Unicode.GetString(memory.ToArray());
            const string customMarker = "\"Mode\": \"Custom\"";
            var markerIndex = hiveText.IndexOf(customMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return aliases;
            }

            var arrayStart = hiveText.LastIndexOf('[', markerIndex);
            var arrayEnd = hiveText.IndexOf(']', markerIndex);
            if (arrayStart < 0 || arrayEnd <= arrayStart)
            {
                return aliases;
            }

            var profiles = JsonNode.Parse(hiveText[arrayStart..(arrayEnd + 1)])?.AsArray();
            if (profiles is null)
            {
                return aliases;
            }

            foreach (var profile in profiles)
            {
                if (!string.Equals(
                        profile?["Mode"]?.GetValue<string>(),
                        "Custom",
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(profile?["Index"]?.GetValue<string>(), out var index))
                {
                    continue;
                }

                var alias = profile?["AliasName"]?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    aliases[index] = alias;
                }
            }

            DiagnosticLog.Write(
                $"Loaded {aliases.Count} custom profile aliases from Control Center settings.");
            if (aliases.Count > 0)
            {
                cachedAliases = aliases;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"Control Center profile alias discovery failed; using {cachedAliases.Count} cached aliases: {ex.Message}");
            return cachedAliases;
        }

        return aliases.Count > 0 ? aliases : cachedAliases;
    }
}

internal static class ControlCenterPaths
{
    private static string? install;

    private static string Install => install ??= Directory.GetDirectories(@"C:\Program Files\OEM")
        .Select(path => Path.Combine(path, "AiStoneService", "MyControlCenter"))
        .FirstOrDefault(Directory.Exists)
        ?? throw new DirectoryNotFoundException("没有找到智控中心配置目录。");

    public static string MainOption
    {
        get => Path.Combine(Install, "UserPofiles", "MainOption.json");
    }

    public static string TurboProfile(int index) =>
        Path.Combine(Install, "UserPofiles", $"Mode3_Profile{index + 1}.json");

    public static IEnumerable<(int Index, string Path)> CustomProfiles()
    {
        var directory = Path.Combine(Install, "UserPofiles");
        return Directory.EnumerateFiles(directory, "Mode4_Profile*.json")
            .Select(path => (
                Path: path,
                Suffix: Path.GetFileNameWithoutExtension(path)["Mode4_Profile".Length..]))
            .Where(profile => int.TryParse(profile.Suffix, out var number) && number >= 1)
            .Select(profile => (
                Index: int.Parse(profile.Suffix) - 1,
                profile.Path))
            .OrderBy(profile => profile.Index);
    }
}
