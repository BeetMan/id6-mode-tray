using System.Drawing;
using System.Windows.Forms;

namespace ID6ModeTray;

internal static class PopupPlacement
{
    internal static Point Calculate(
        Rectangle monitorBounds,
        Rectangle workingArea,
        Size popupSize,
        Point cursor,
        int margin = 8)
    {
        int x;
        int y;
        if (workingArea.Top > monitorBounds.Top)
        {
            x = cursor.X - popupSize.Width + margin * 3;
            y = workingArea.Top + margin;
        }
        else if (workingArea.Bottom < monitorBounds.Bottom)
        {
            x = cursor.X - popupSize.Width + margin * 3;
            y = workingArea.Bottom - popupSize.Height - margin;
        }
        else if (workingArea.Left > monitorBounds.Left)
        {
            x = workingArea.Left + margin;
            y = cursor.Y - popupSize.Height + margin * 3;
        }
        else if (workingArea.Right < monitorBounds.Right)
        {
            x = workingArea.Right - popupSize.Width - margin;
            y = cursor.Y - popupSize.Height + margin * 3;
        }
        else
        {
            x = cursor.X - popupSize.Width + margin * 3;
            y = workingArea.Bottom - popupSize.Height - margin;
        }

        return Clamp(workingArea, popupSize, x, y, margin);
    }

    internal static Point BottomRight(Rectangle workingArea, Size size, int margin = 18) =>
        Clamp(
            workingArea,
            size,
            workingArea.Right - size.Width - margin,
            workingArea.Bottom - size.Height - margin,
            margin);

    internal static int FitHeight(int desiredHeight, int workingAreaHeight, int margin) =>
        Math.Max(1, Math.Min(desiredHeight, workingAreaHeight - margin));

    internal static int FitWidth(int desiredWidth, int workingAreaWidth, int margin) =>
        Math.Max(1, Math.Min(desiredWidth, workingAreaWidth - margin));

    private static Point Clamp(
        Rectangle workingArea,
        Size size,
        int x,
        int y,
        int margin)
    {
        margin = EffectiveMargin(workingArea, size, margin);
        var minimumX = workingArea.Left + margin;
        var minimumY = workingArea.Top + margin;
        var maximumX = workingArea.Right - size.Width - margin;
        var maximumY = workingArea.Bottom - size.Height - margin;
        return new Point(
            ClampValue(x, minimumX, maximumX),
            ClampValue(y, minimumY, maximumY));
    }

    private static int EffectiveMargin(Rectangle workingArea, Size size, int margin)
    {
        var horizontal = Math.Max(0, (workingArea.Width - size.Width) / 2);
        var vertical = Math.Max(0, (workingArea.Height - size.Height) / 2);
        return Math.Min(margin, Math.Min(horizontal, vertical));
    }

    private static int ClampValue(int value, int minimum, int maximum) =>
        maximum < minimum ? minimum : Math.Max(minimum, Math.Min(value, maximum));
}
