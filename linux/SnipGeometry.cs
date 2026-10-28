using Avalonia;

namespace OpenSnip;

static class SnipGeometry
{
    public static PixelRect ToScreen(PixelRect image, PixelRect virtualBounds, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0) return image;
        if (frameWidth == virtualBounds.Width && frameHeight == virtualBounds.Height)
            return new PixelRect(virtualBounds.X + image.X, virtualBounds.Y + image.Y, image.Width, image.Height);

        double sx = virtualBounds.Width / (double)frameWidth;
        double sy = virtualBounds.Height / (double)frameHeight;
        return new PixelRect(
            virtualBounds.X + (int)Math.Round(image.X * sx),
            virtualBounds.Y + (int)Math.Round(image.Y * sy),
            Math.Max(1, (int)Math.Round(image.Width * sx)),
            Math.Max(1, (int)Math.Round(image.Height * sy)));
    }

    public static PixelRect ScreenToImage(PixelRect screen, PixelRect virtualBounds, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0) return new PixelRect(0, 0, 1, 1);
        if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
            return new PixelRect(0, 0, frameWidth, frameHeight);

        double sx = frameWidth / (double)virtualBounds.Width;
        double sy = frameHeight / (double)virtualBounds.Height;
        int x = (int)Math.Round((screen.X - virtualBounds.X) * sx);
        int y = (int)Math.Round((screen.Y - virtualBounds.Y) * sy);
        int width = Math.Max(1, (int)Math.Round(screen.Width * sx));
        int height = Math.Max(1, (int)Math.Round(screen.Height * sy));
        x = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        width = Math.Clamp(width, 1, frameWidth - x);
        height = Math.Clamp(height, 1, frameHeight - y);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>Screen that contains the most of <paramref name="rect"/>, or the nearest when none overlap.</summary>
    public static int BestScreen(PixelRect rect, IReadOnlyList<PixelRect> screens)
    {
        if (screens == null || screens.Count == 0) return -1;

        int best = 0;
        long bestArea = -1;
        for (int i = 0; i < screens.Count; i++)
        {
            long area = OverlapArea(rect, screens[i]);
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }
        if (bestArea > 0) return best;

        int closest = 0;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < screens.Count; i++)
        {
            long distance = Separation(rect, screens[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = i;
            }
        }
        return closest;
    }

    public static PixelPoint BottomCenter(PixelRect area, int width, int height, int margin)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        int x = area.X + Math.Max(0, area.Width - width) / 2;
        int y = area.Bottom - height - Math.Max(0, margin);
        if (x < area.X) x = area.X;
        if (y < area.Y) y = area.Y;
        return new PixelPoint(x, y);
    }

    public static PixelPoint ClampTo(int x, int y, PixelRect bounds)
    {
        int maxX = bounds.Width <= 1 ? bounds.X : bounds.Right - 1;
        int maxY = bounds.Height <= 1 ? bounds.Y : bounds.Bottom - 1;
        return new PixelPoint(Math.Clamp(x, bounds.X, maxX), Math.Clamp(y, bounds.Y, maxY));
    }

    public static (double X, double Y) ControlDelta(int dx, int dy, double controlWidth, double controlHeight, int imageWidth, int imageHeight)
    {
        double sx = controlWidth <= 1 || imageWidth <= 0 ? 1 : controlWidth / imageWidth;
        double sy = controlHeight <= 1 || imageHeight <= 0 ? 1 : controlHeight / imageHeight;
        return (dx * sx, dy * sy);
    }

    static long OverlapArea(PixelRect a, PixelRect b)
    {
        int left = Math.Max(a.X, b.X);
        int top = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top) return 0;
        return (long)(right - left) * (bottom - top);
    }

    static long Separation(PixelRect a, PixelRect b)
    {
        long dx = 0;
        if (a.Right <= b.X) dx = b.X - a.Right;
        else if (b.Right <= a.X) dx = a.X - b.Right;
        long dy = 0;
        if (a.Bottom <= b.Y) dy = b.Y - a.Bottom;
        else if (b.Bottom <= a.Y) dy = a.Y - b.Bottom;
        return dx + dy;
    }
}
