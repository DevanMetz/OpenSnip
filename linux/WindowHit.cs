using Avalonia;

namespace OpenSnip;

/// <summary>Picks the front-most window under a pixel of the frozen capture.</summary>
static class WindowHit
{
    public static PixelRect? At(IReadOnlyList<PixelRect> windows, int x, int y, int imageWidth, int imageHeight)
    {
        if (windows == null || imageWidth < 1 || imageHeight < 1) return null;
        if ((uint)x >= (uint)imageWidth || (uint)y >= (uint)imageHeight) return null;

        for (int i = 0; i < windows.Count; i++)
        {
            var hit = Clip(windows[i], imageWidth, imageHeight);
            if (hit.Width < 1 || hit.Height < 1) continue;
            if (x >= hit.X && y >= hit.Y && x < hit.X + hit.Width && y < hit.Y + hit.Height)
                return hit;
        }
        return null;
    }

    public static PixelRect Clip(PixelRect rect, int imageWidth, int imageHeight)
    {
        if (rect.Width < 1 || rect.Height < 1 || imageWidth < 1 || imageHeight < 1) return default;
        long x1 = Math.Max(0, (long)rect.X);
        long y1 = Math.Max(0, (long)rect.Y);
        long x2 = Math.Min(imageWidth, (long)rect.X + rect.Width);
        long y2 = Math.Min(imageHeight, (long)rect.Y + rect.Height);
        if (x2 <= x1 || y2 <= y1) return default;
        return new PixelRect((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1));
    }
}
