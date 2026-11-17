using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ID6ModeTray;

internal sealed class ModeTile : Control
{
    private bool hovered;
    private bool selected;

    public ModeTile(string text)
    {
        DoubleBuffered = true;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand;
        TabStop = true;
        Text = text;
        Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
    }

    public bool Selected => selected;

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new ModeTileAccessibleObject(this);

    internal void Activate() => OnClick(EventArgs.Empty);

    public void SetSelected(bool value)
    {
        selected = value;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Activate();
            e.Handled = true;
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
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
        else if (hovered)
        {
            using var border = new Pen(Color.FromArgb(68, 78, 94), 1.25F);
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

        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(
                e.Graphics,
                new Rectangle(4, 4, Width - 8, Height - 8),
                Color.FromArgb(180, 187, 200),
                background);
        }
    }

    private sealed class ModeTileAccessibleObject(ModeTile owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override AccessibleStates State =>
            base.State | (owner.Selected ? AccessibleStates.Selected : AccessibleStates.None);

        public override void DoDefaultAction() => owner.Activate();
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
