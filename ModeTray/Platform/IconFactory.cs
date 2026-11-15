using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ID6ModeTray;

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
