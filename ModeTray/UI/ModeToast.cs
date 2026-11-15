using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ID6ModeTray;

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
        DpiChanged += (_, _) =>
        {
            if (Visible)
            {
                MoveToCurrentMonitor();
            }
        };
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowMessage(string title, string message, bool isError)
    {
        titleLabel.Text = title;
        messageLabel.Text = message;
        accent.BackColor = isError
            ? Color.FromArgb(244, 91, 105)
            : Color.FromArgb(57, 211, 181);

        MoveToCurrentMonitor();
        ApplyRoundedRegion();
        hideTimer.Stop();
        Show();
        MoveToCurrentMonitor();
        hideTimer.Start();
    }

    private void MoveToCurrentMonitor()
    {
        Location = PopupPlacement.BottomRight(
            Screen.FromPoint(Cursor.Position).WorkingArea,
            Size,
            LogicalToDeviceUnits(18));
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
