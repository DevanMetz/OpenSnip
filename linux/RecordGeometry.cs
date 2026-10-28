using Avalonia;

namespace OpenSnip;

/// <summary>
/// Recording regions cover one monitor and H.264 wants even dimensions.
/// The control bar prefers a spot that does not cover the recorded pixels.
/// </summary>
static class RecordGeometry
{
    public const int MinSize = 16;

    public static bool TryNormalize(PixelRect selection, IReadOnlyList<PixelRect> monitors, out PixelRect area)
    {
        area = default;
        if (monitors == null || monitors.Count == 0) return false;
        if (selection.Width <= 0 || selection.Height <= 0) return false;

        int best = -1;
        var monitor = default(PixelRect);
        foreach (var candidate in monitors)
        {
            int overlap = OverlapArea(selection, candidate);
            if (overlap > best)
            {
                best = overlap;
                monitor = candidate;
            }
        }
        if (best <= 0) return false;

        var clipped = Intersect(selection, monitor);
        int width = clipped.Width & ~1;
        int height = clipped.Height & ~1;
        if (width < MinSize || height < MinSize) return false;
        area = new PixelRect(clipped.X, clipped.Y, width, height);
        return true;
    }

    /// <summary>Red bars drawn on the inside edge of <paramref name="area"/> so a full-monitor selection still shows a frame.</summary>
    public static PixelRect[] Outline(PixelRect area, int thickness)
    {
        int t = Math.Max(1, thickness);
        if (area.Width <= t * 2 || area.Height <= t * 2) return Array.Empty<PixelRect>();
        return new[]
        {
            new PixelRect(area.X, area.Y, area.Width, t),
            new PixelRect(area.X, area.Bottom - t, area.Width, t),
            new PixelRect(area.X, area.Y, t, area.Height),
            new PixelRect(area.Right - t, area.Y, t, area.Height),
        };
    }

    /// <summary>
    /// Places the bar just outside the recording when the monitor has room,
    /// then inside it. Outside means a failed window-exclusion still leaves the controls out of the file.
    /// </summary>
    public static PixelRect PlaceBar(PixelRect area, PixelRect work, int barWidth, int barHeight, int gap)
    {
        barWidth = Math.Max(1, barWidth);
        barHeight = Math.Max(1, barHeight);
        gap = Math.Max(0, gap);
        int x = Math.Clamp(area.X + (area.Width - barWidth) / 2, work.X, Math.Max(work.X, work.Right - barWidth));

        PixelRect At(int y) => new(x, y, barWidth, barHeight);
        bool Fits(PixelRect rect) => Contains(work, rect) && !Overlaps(rect, area);

        var below = At(area.Bottom + gap);
        if (Fits(below)) return below;
        var above = At(area.Y - gap - barHeight);
        if (Fits(above)) return above;

        int sideY = Math.Clamp(area.Y + (area.Height - barHeight) / 2, work.Y, Math.Max(work.Y, work.Bottom - barHeight));
        var right = new PixelRect(area.Right + gap, sideY, barWidth, barHeight);
        if (Contains(work, right) && !Overlaps(right, area)) return right;
        var left = new PixelRect(area.X - gap - barWidth, sideY, barWidth, barHeight);
        if (Contains(work, left) && !Overlaps(left, area)) return left;

        int insideY = Math.Clamp(area.Bottom - gap - barHeight, work.Y, Math.Max(work.Y, work.Bottom - barHeight));
        return At(insideY);
    }

    public static bool Overlaps(PixelRect a, PixelRect b) =>
        a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0 &&
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    public static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    public static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int x = Math.Max(a.X, b.X);
        int y = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= x || bottom <= y) return default;
        return new PixelRect(x, y, right - x, bottom - y);
    }

    static int OverlapArea(PixelRect a, PixelRect b)
    {
        var hit = Intersect(a, b);
        return Math.Max(0, hit.Width) * Math.Max(0, hit.Height);
    }
}
