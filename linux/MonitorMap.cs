using Avalonia;

namespace OpenSnip;

/// <summary>
/// One monitor in desktop pixels, the same space as Avalonia <c>Screen.Bounds</c>.
/// <paramref name="Scale"/> is that screen's scale (physical pixels per DIP).
/// </summary>
readonly record struct MonitorLayout(PixelRect Bounds, double Scale, bool Primary)
{
    public double ScaleOrOne => Scale > 0 && !double.IsNaN(Scale) && !double.IsInfinity(Scale) ? Scale : 1;
}

/// <summary>Rectangle of the desktop an X11 capture should read, in root coordinates.</summary>
readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public static CaptureRegion From(IReadOnlyList<PixelRect> monitors, int rootWidth, int rootHeight)
    {
        if (rootWidth < 0) rootWidth = 0;
        if (rootHeight < 0) rootHeight = 0;
        if (monitors == null || monitors.Count == 0)
            return new CaptureRegion(0, 0, rootWidth, rootHeight);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool any = false;
        foreach (var monitor in monitors)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0) continue;
            any = true;
            if (monitor.X < minX) minX = monitor.X;
            if (monitor.Y < minY) minY = monitor.Y;
            if (monitor.Right > maxX) maxX = monitor.Right;
            if (monitor.Bottom > maxY) maxY = monitor.Bottom;
        }
        if (!any || maxX <= minX || maxY <= minY)
            return new CaptureRegion(0, 0, rootWidth, rootHeight);
        return new CaptureRegion(minX, minY, maxX - minX, maxY - minY);
    }
}

/// <summary>
/// Maps a captured frame onto the monitors it covers.
/// Each monitor is shown in its own window so a 1x screen beside a 2x screen is not
/// stretched through one scale. A frame whose size matches the bounding box is cropped
/// 1:1 (including monitors that sit at negative root coordinates). Any other size is
/// mapped uniformly across that box, which is what a portal screenshot sometimes is.
/// </summary>
sealed class MonitorMap
{
    readonly List<MonitorLayout> _monitors;
    readonly bool _oneToOne;
    readonly int _originX;
    readonly int _originY;

    MonitorMap(PixelRect desktop, List<MonitorLayout> monitors, int frameWidth, int frameHeight, int originX, int originY, bool oneToOne)
    {
        Desktop = desktop;
        _monitors = monitors;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        _originX = originX;
        _originY = originY;
        _oneToOne = oneToOne;
    }

    public PixelRect Desktop { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public IReadOnlyList<MonitorLayout> Monitors => _monitors;
    public bool OneToOne => _oneToOne;

    public static MonitorMap Create(IReadOnlyList<MonitorLayout> monitors, int frameWidth, int frameHeight, bool originKnown, int originX, int originY)
    {
        var usable = new List<MonitorLayout>();
        if (monitors != null)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0)
                    usable.Add(monitor);
            }
        }
        if (usable.Count == 0)
        {
            usable.Add(new MonitorLayout(
                new PixelRect(0, 0, Math.Max(1, frameWidth), Math.Max(1, frameHeight)),
                1,
                true));
        }

        var desktop = usable[0].Bounds;
        for (int i = 1; i < usable.Count; i++) desktop = desktop.Union(usable[i].Bounds);

        bool oneToOne;
        int frameOriginX;
        int frameOriginY;
        if (originKnown)
        {
            frameOriginX = originX;
            frameOriginY = originY;
            oneToOne = true;
        }
        else if (frameWidth == desktop.Width && frameHeight == desktop.Height)
        {
            // A tight screenshot of every output. Pixel (0, 0) is the top-left of the box,
            // which is not always root (0, 0) when a monitor sits to the left or above.
            frameOriginX = desktop.X;
            frameOriginY = desktop.Y;
            oneToOne = true;
        }
        else
        {
            frameOriginX = desktop.X;
            frameOriginY = desktop.Y;
            oneToOne = false;
        }

        return new MonitorMap(desktop, usable, Math.Max(0, frameWidth), Math.Max(0, frameHeight), frameOriginX, frameOriginY, oneToOne);
    }

    public PixelPoint Clamp(PixelPoint point)
    {
        int maxX = Desktop.Width <= 1 ? Desktop.X : Desktop.Right - 1;
        int maxY = Desktop.Height <= 1 ? Desktop.Y : Desktop.Bottom - 1;
        return new PixelPoint(Math.Clamp(point.X, Desktop.X, maxX), Math.Clamp(point.Y, Desktop.Y, maxY));
    }

    public PixelPoint Center
    {
        get
        {
            int x = Desktop.X + Math.Max(0, Desktop.Width - 1) / 2;
            int y = Desktop.Y + Math.Max(0, Desktop.Height - 1) / 2;
            return new PixelPoint(x, y);
        }
    }

    public int IndexAt(PixelPoint point)
    {
        int best = 0;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < _monitors.Count; i++)
        {
            var bounds = _monitors[i].Bounds;
            if (point.X >= bounds.X && point.Y >= bounds.Y && point.X < bounds.Right && point.Y < bounds.Bottom)
                return i;

            long dx = 0;
            if (point.X < bounds.X) dx = bounds.X - point.X;
            else if (point.X >= bounds.Right) dx = point.X - (bounds.Right - 1);
            long dy = 0;
            if (point.Y < bounds.Y) dy = bounds.Y - point.Y;
            else if (point.Y >= bounds.Bottom) dy = point.Y - (bounds.Bottom - 1);
            long distance = dx + dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    public PixelRect MonitorRectAt(PixelPoint point) => _monitors[IndexAt(point)].Bounds;

    /// <summary>
    /// Desktop pixel under a point in a monitor's window.
    /// <paramref name="dipX"/> is allowed to fall outside the window while a drag is grabbed,
    /// so the result can land on a neighboring monitor. The caller clamps it to the desktop.
    /// </summary>
    public PixelPoint DesktopPoint(int index, double dipX, double dipY, double dipWidth, double dipHeight)
    {
        var bounds = _monitors[index].Bounds;
        double width = dipWidth <= 0 ? 1 : dipWidth;
        double height = dipHeight <= 0 ? 1 : dipHeight;
        int x = bounds.X + (int)Math.Floor(dipX / width * bounds.Width);
        int y = bounds.Y + (int)Math.Floor(dipY / height * bounds.Height);
        return new PixelPoint(x, y);
    }

    public PixelRect RectBetween(PixelPoint a, PixelPoint b)
    {
        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X, b.X) + 1;
        int bottom = Math.Max(a.Y, b.Y) + 1;
        left = Math.Clamp(left, Desktop.X, Desktop.Right);
        top = Math.Clamp(top, Desktop.Y, Desktop.Bottom);
        right = Math.Clamp(right, left, Desktop.Right);
        bottom = Math.Clamp(bottom, top, Desktop.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelRect TranslateClamped(PixelRect rect, int dx, int dy)
    {
        int width = Math.Min(Math.Max(0, rect.Width), Math.Max(0, Desktop.Width));
        int height = Math.Min(Math.Max(0, rect.Height), Math.Max(0, Desktop.Height));
        int x = rect.X + dx;
        int y = rect.Y + dy;
        int maxX = Desktop.Right - width;
        int maxY = Desktop.Bottom - height;
        if (x < Desktop.X) x = Desktop.X;
        if (y < Desktop.Y) y = Desktop.Y;
        if (x > maxX) x = maxX;
        if (y > maxY) y = maxY;
        return new PixelRect(x, y, width, height);
    }

    public bool TryToImage(PixelRect desktop, out PixelRect image)
    {
        image = default;
        if (FrameWidth <= 0 || FrameHeight <= 0) return false;

        if (_oneToOne)
        {
            var covered = Intersect(desktop, new PixelRect(_originX, _originY, FrameWidth, FrameHeight));
            if (covered.Width <= 0 || covered.Height <= 0) return false;
            image = new PixelRect(covered.X - _originX, covered.Y - _originY, covered.Width, covered.Height);
            return true;
        }

        if (Desktop.Width <= 0 || Desktop.Height <= 0) return false;
        var hit = Intersect(desktop, Desktop);
        if (hit.Width <= 0 || hit.Height <= 0) return false;
        double sx = FrameWidth / (double)Desktop.Width;
        double sy = FrameHeight / (double)Desktop.Height;
        int x = (int)Math.Round((hit.X - Desktop.X) * sx);
        int y = (int)Math.Round((hit.Y - Desktop.Y) * sy);
        int right = (int)Math.Round((hit.Right - Desktop.X) * sx);
        int bottom = (int)Math.Round((hit.Bottom - Desktop.Y) * sy);
        x = Math.Clamp(x, 0, FrameWidth);
        y = Math.Clamp(y, 0, FrameHeight);
        right = Math.Clamp(right, 0, FrameWidth);
        bottom = Math.Clamp(bottom, 0, FrameHeight);
        if (right <= x) right = Math.Min(FrameWidth, x + 1);
        if (bottom <= y) bottom = Math.Min(FrameHeight, y + 1);
        if (right <= x || bottom <= y) return false;
        image = new PixelRect(x, y, right - x, bottom - y);
        return true;
    }

    public Rect? LocalRect(int index, PixelRect desktop, double dipWidth, double dipHeight)
    {
        if (index < 0 || index >= _monitors.Count || dipWidth <= 0 || dipHeight <= 0) return null;
        var bounds = _monitors[index].Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var hit = Intersect(desktop, bounds);
        if (hit.Width <= 0 || hit.Height <= 0) return null;
        double x0 = (hit.X - bounds.X) * dipWidth / bounds.Width;
        double y0 = (hit.Y - bounds.Y) * dipHeight / bounds.Height;
        double x1 = (hit.Right - bounds.X) * dipWidth / bounds.Width;
        double y1 = (hit.Bottom - bounds.Y) * dipHeight / bounds.Height;
        return new Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    public bool TryPlacement(int index, double dipWidth, double dipHeight, out PixelRect source, out Rect dest)
    {
        source = default;
        dest = default;
        if (index < 0 || index >= _monitors.Count) return false;
        var bounds = _monitors[index].Bounds;
        if (!TryToImage(bounds, out source)) return false;

        PixelRect covered = _oneToOne
            ? new PixelRect(source.X + _originX, source.Y + _originY, source.Width, source.Height)
            : bounds;
        var local = LocalRect(index, covered, dipWidth, dipHeight);
        if (local == null || source.Width <= 0 || source.Height <= 0) return false;
        dest = local.Value;
        return dest.Width > 0 && dest.Height > 0;
    }

    static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int left = Math.Max(a.X, b.X);
        int top = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top) return default;
        return new PixelRect(left, top, right - left, bottom - top);
    }
}
