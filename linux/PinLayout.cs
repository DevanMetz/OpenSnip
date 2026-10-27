namespace OpenSnip;

/// <summary>Which edges of a pin the pointer is over. Corners are combinations.</summary>
[Flags]
enum PinEdge
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
}

readonly record struct PinRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Geometry for a floating pin. Hit targets match the Windows pin: corners are easier
/// to grab than edges, and a resize keeps the image's proportions unless Shift is held.
/// </summary>
static class PinLayout
{
    public const int Border = 1;
    public const int MinContent = 16;
    public const double MinZoom = 0.1;
    public const double MaxZoom = 8;
    public const double MinOpacity = 0.2;
    public const double MaxOpacity = 1;

    public static int Grip(int width, int height, double scale)
    {
        int side = Math.Min(Math.Max(0, width), Math.Max(0, height));
        int scaled = (int)(10 * (scale <= 0 ? 1 : scale));
        return Math.Max(4, Math.Min(Math.Max(scaled, 1), Math.Max(1, side / 4)));
    }

    public static PinEdge HitTest(double x, double y, double width, double height, double scale)
    {
        if (width <= 0 || height <= 0) return PinEdge.None;
        int grip = Grip((int)width, (int)height, scale);
        int corner = grip * 2;
        bool l = x < grip, r = x >= width - grip, t = y < grip, b = y >= height - grip;
        bool cl = x < corner, cr = x >= width - corner, ct = y < corner, cb = y >= height - corner;
        if ((t && cl) || (l && ct)) return PinEdge.Top | PinEdge.Left;
        if ((t && cr) || (r && ct)) return PinEdge.Top | PinEdge.Right;
        if ((b && cl) || (l && cb)) return PinEdge.Bottom | PinEdge.Left;
        if ((b && cr) || (r && cb)) return PinEdge.Bottom | PinEdge.Right;
        if (l) return PinEdge.Left;
        if (r) return PinEdge.Right;
        if (t) return PinEdge.Top;
        if (b) return PinEdge.Bottom;
        return PinEdge.None;
    }

    public static PinRect Resize(PinRect origin, PinEdge edge, int dx, int dy, double aspect, bool keepAspect)
    {
        if (edge == PinEdge.None) return origin;

        int left = origin.X;
        int top = origin.Y;
        int right = origin.Right;
        int bottom = origin.Bottom;
        if ((edge & PinEdge.Left) != 0) left += dx;
        if ((edge & PinEdge.Right) != 0) right += dx;
        if ((edge & PinEdge.Top) != 0) top += dy;
        if ((edge & PinEdge.Bottom) != 0) bottom += dy;

        int w = Math.Max(MinContent, right - left - Border * 2);
        int h = Math.Max(MinContent, bottom - top - Border * 2);
        if (keepAspect && aspect > 0 && !double.IsNaN(aspect) && !double.IsInfinity(aspect))
        {
            bool vertical = edge == PinEdge.Top || edge == PinEdge.Bottom;
            if (vertical) w = Math.Max(1, (int)Math.Round(h * aspect));
            else h = Math.Max(1, (int)Math.Round(w / aspect));
        }

        int windowW = w + Border * 2;
        int windowH = h + Border * 2;
        if ((edge & PinEdge.Left) != 0) left = right - windowW;
        else right = left + windowW;
        if ((edge & PinEdge.Top) != 0) top = bottom - windowH;
        else bottom = top + windowH;
        return new PinRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static double ClampZoom(double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom)) zoom = 1;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        // Snap back to pixel-perfect when the wheel lands just off 1x.
        if (Math.Abs(Math.Round(zoom, 2) - 1) < 0.04) zoom = 1;
        return zoom;
    }

    public static double StepZoom(double zoom, bool zoomIn) =>
        ClampZoom(zoom * (zoomIn ? 1.1 : 1 / 1.1));

    public static double StepOpacity(double opacity, bool increase)
    {
        if (double.IsNaN(opacity) || double.IsInfinity(opacity)) opacity = 1;
        return Math.Clamp(opacity + (increase ? 0.1 : -0.1), MinOpacity, MaxOpacity);
    }

    public static PinRect ZoomAt(PinRect bounds, double zoom, double anchorX, double anchorY, int imageWidth, int imageHeight)
    {
        zoom = ClampZoom(zoom);
        double rx = bounds.Width <= 0 ? 0.5 : anchorX / bounds.Width;
        double ry = bounds.Height <= 0 ? 0.5 : anchorY / bounds.Height;
        int w = Math.Max(1, (int)Math.Round(Math.Max(1, imageWidth) * zoom)) + Border * 2;
        int h = Math.Max(1, (int)Math.Round(Math.Max(1, imageHeight) * zoom)) + Border * 2;
        int screenX = bounds.X + (int)anchorX;
        int screenY = bounds.Y + (int)anchorY;
        return new PinRect(screenX - (int)(rx * w), screenY - (int)(ry * h), w, h);
    }

    public static PinRect AtCursor(int cursorX, int cursorY, int imageWidth, int imageHeight)
    {
        int w = Math.Max(1, imageWidth) + Border * 2;
        int h = Math.Max(1, imageHeight) + Border * 2;
        return new PinRect(cursorX - w / 2, cursorY - h / 2, w, h);
    }

    public static PinRect Centered(int areaX, int areaY, int areaWidth, int areaHeight, int imageWidth, int imageHeight)
    {
        int w = Math.Max(1, imageWidth) + Border * 2;
        int h = Math.Max(1, imageHeight) + Border * 2;
        return new PinRect(areaX + (areaWidth - w) / 2, areaY + (areaHeight - h) / 2, w, h);
    }
}
