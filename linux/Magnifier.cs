namespace OpenSnip;

/// <summary>
/// The snip loupe: a 15×15 grid of the frozen frame, centered on the cursor pixel.
/// Cells are sampled from that pixel so the center stays put when the cursor
/// reaches the edge of the frame. A clamped source rectangle would slide it.
/// </summary>
static class Magnifier
{
    public const int Cells = 15;
    public const int Center = 7;

    public readonly record struct Box(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public bool Overlaps(Box other) =>
            X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    }

    public static int ImageCoord(double local, double span, int pixels)
    {
        if (pixels <= 1 || span <= 0) return 0;
        int value = (int)(local * pixels / span);
        if (value < 0) return 0;
        if (value >= pixels) return pixels - 1;
        return value;
    }

    public static bool TryPixel(CapturedFrame frame, int x, int y, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (frame == null || (uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) return false;
        int i = (y * frame.Width + x) * 4;
        b = frame.Bgra[i];
        g = frame.Bgra[i + 1];
        r = frame.Bgra[i + 2];
        return true;
    }

    public static string Label(int x, int y, byte r, byte g, byte b) =>
        $"{x}, {y}   #{r:X2}{g:X2}{b:X2}";

    /// <summary>Packs a <see cref="Cells"/> square of BGRA pixels. Outside the frame stays black.</summary>
    public static void Fill(CapturedFrame frame, int x, int y, byte[] bgra)
    {
        if (frame == null) throw new ArgumentNullException(nameof(frame));
        if (bgra == null || bgra.Length < Cells * Cells * 4)
            throw new ArgumentException("The loupe buffer is too small.", nameof(bgra));

        int count = Cells * Cells * 4;
        for (int i = 0; i < count; i += 4)
        {
            bgra[i] = 0;
            bgra[i + 1] = 0;
            bgra[i + 2] = 0;
            bgra[i + 3] = 255;
        }

        for (int row = 0; row < Cells; row++)
        {
            int sy = y + row - Center;
            if ((uint)sy >= (uint)frame.Height) continue;
            for (int col = 0; col < Cells; col++)
            {
                int sx = x + col - Center;
                if ((uint)sx >= (uint)frame.Width) continue;
                int src = (sy * frame.Width + sx) * 4;
                int dst = (row * Cells + col) * 4;
                bgra[dst] = frame.Bgra[src];
                bgra[dst + 1] = frame.Bgra[src + 1];
                bgra[dst + 2] = frame.Bgra[src + 2];
                bgra[dst + 3] = 255;
            }
        }
    }

    /// <summary>Nearest-neighbor scale of the cell grid into <paramref name="cell"/> screen pixels per cell.</summary>
    public static void Expand(byte[] cells, int cell, byte[] dest)
    {
        if (cell < 1) throw new ArgumentOutOfRangeException(nameof(cell));
        if (cells == null || cells.Length < Cells * Cells * 4)
            throw new ArgumentException("The loupe buffer is too small.", nameof(cells));
        int size = Cells * cell;
        if (dest == null || dest.Length < size * size * 4)
            throw new ArgumentException("The scaled buffer is too small.", nameof(dest));

        int dstStride = size * 4;
        for (int row = 0; row < Cells; row++)
        {
            for (int col = 0; col < Cells; col++)
            {
                int src = (row * Cells + col) * 4;
                byte b = cells[src];
                byte g = cells[src + 1];
                byte r = cells[src + 2];
                byte a = cells[src + 3];
                int row0 = row * cell;
                int col0 = col * cell;
                for (int dy = 0; dy < cell; dy++)
                {
                    int dst = (row0 + dy) * dstStride + col0 * 4;
                    for (int dx = 0; dx < cell; dx++)
                    {
                        dest[dst++] = b;
                        dest[dst++] = g;
                        dest[dst++] = r;
                        dest[dst++] = a;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Puts the loupe beside the cursor, flipping to stay on screen and off <paramref name="avoid"/>.
    /// </summary>
    public static Box Place(double cursorX, double cursorY, double width, double height,
        double boundsWidth, double boundsHeight, Box? avoid)
    {
        const double gap = 22;
        const double margin = 4;

        Box? TryAt(double x, double y)
        {
            if (x < margin || y < margin ||
                x + width > boundsWidth - margin ||
                y + height > boundsHeight - margin)
                return null;
            var rect = new Box(x, y, width, height);
            if (avoid is { } blocked && rect.Overlaps(blocked)) return null;
            return rect;
        }

        double right = cursorX + gap;
        double left = cursorX - width - gap;
        double down = cursorY + gap;
        double up = cursorY - height - gap;

        var found = TryAt(right, down) ?? TryAt(left, down) ?? TryAt(right, up) ?? TryAt(left, up);
        if (found == null && avoid is { } blocked)
        {
            found = TryAt(blocked.Right + gap, down)
                    ?? TryAt(blocked.X - width - gap, down)
                    ?? TryAt(right, blocked.Bottom + gap)
                    ?? TryAt(right, blocked.Y - height - gap)
                    ?? TryAt(left, blocked.Bottom + gap)
                    ?? TryAt(left, blocked.Y - height - gap);
        }
        if (found is { } placed) return placed;

        double fx = right;
        double fy = down;
        if (fx + width > boundsWidth - margin) fx = left;
        if (fy + height > boundsHeight - margin) fy = up;
        fx = Math.Clamp(fx, margin, Math.Max(margin, boundsWidth - width - margin));
        fy = Math.Clamp(fy, margin, Math.Max(margin, boundsHeight - height - margin));
        return new Box(fx, fy, width, height);
    }
}
