using System.Collections.Specialized;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenSnip;

static class ScreenCapture
{
    /// <summary>Grab a rectangle of the desktop (physical pixels, any monitor layout).</summary>
    public static Bitmap Capture(Rectangle r)
    {
        var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
        bmp.SetResolution(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }
}

static class WindowFinder
{
    /// <summary>Visible top-level windows, front to back, as screen rectangles.</summary>
    public static List<Rectangle> Snapshot()
    {
        var list = new List<Rectangle>();
        uint myPid = (uint)Environment.ProcessId;
        var sb = new StringBuilder(256);

        Native.EnumWindows((h, lParam) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == myPid) return true;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;

            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
            if ((ex & Native.WS_EX_LAYERED) != 0 &&
                Native.GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags) &&
                (flags & 2 /* LWA_ALPHA */) != 0 && alpha == 0) return true;

            sb.Clear();
            Native.GetClassName(h, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW") return true; // the desktop: fall back to whole monitor

            Rectangle r;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT rc,
                    Marshal.SizeOf<Native.RECT>()) == 0)
                r = rc.ToRectangle();
            else if (Native.GetWindowRect(h, out rc))
                r = rc.ToRectangle();
            else return true;

            if (r.Width >= 8 && r.Height >= 8) list.Add(r);
            return true;
        }, IntPtr.Zero);

        return list;
    }
}

static class ImageOps
{
    public static byte[] Png(Bitmap b)
    {
        using var ms = new MemoryStream();
        b.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Puts the image on the clipboard as both a bitmap and a lossless PNG. Returns false if the clipboard was busy.</summary>
    public static bool CopyToClipboard(Bitmap b, byte[] png = null)
    {
        png ??= Png(b);
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, b);
            data.SetData("PNG", false, new MemoryStream(png));
            Clipboard.SetDataObject(data, true, 10, 50);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
    }

    public static string NewFileName() => $"Snip {DateTime.Now:yyyy-MM-dd HH.mm.ss}";

    public static string UniquePath(string folder, string name, string ext)
    {
        string path = Path.Combine(folder, name + ext);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name} ({i}){ext}");
        return path;
    }

    /// <summary>Saves a snip into <paramref name="folder"/> as PNG (reusing already-encoded bytes) or JPG.</summary>
    public static string SaveAuto(Bitmap b, byte[] png, string folder, string format)
    {
        Directory.CreateDirectory(folder);
        bool jpg = format == "jpg";
        string path = UniquePath(folder, NewFileName(), jpg ? ".jpg" : ".png");
        if (jpg) Save(b, path);
        else File.WriteAllBytes(path, png ?? Png(b));
        return path;
    }

    public static string SaveTemp(Bitmap b)
    {
        string dir = Path.Combine(Path.GetTempPath(), "OpenSnip");
        Directory.CreateDirectory(dir);
        string path = UniquePath(dir, NewFileName(), ".png");
        b.Save(path, ImageFormat.Png);
        return path;
    }

    public static void Save(Bitmap b, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
            b.Save(path, codec, p);
        }
        else b.Save(path, ImageFormat.Png);
    }

    /// <summary>Shows a Save As dialog. Returns the saved path or null.</summary>
    public static string SaveAs(IWin32Window owner, Bitmap b, string folder)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            FileName = NewFileName() + ".png",
            InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            AddExtension = true,
            DefaultExt = "png",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        try
        {
            Save(b, dlg.FileName);
            return dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Couldn't save the image:\n" + ex.Message, "OpenSnip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    /// <summary>A file on the clipboard / in a drag, the way Explorer does it.</summary>
    public static DataObject FileData(string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        return data;
    }

    public static DataObject DragData(Bitmap b, string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path ?? SaveTemp(b) });
        data.SetData(DataFormats.Bitmap, true, b);
        return data;
    }

    /// <summary>DrawImage at exact pixel size (ignores bitmap DPI metadata).</summary>
    public static void Blit(Graphics g, Image img, int x, int y) =>
        g.DrawImage(img, new Rectangle(x, y, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

static class Theme
{
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color Accent2 = Color.FromArgb(0, 168, 255);
    public static readonly Color Back = Color.FromArgb(28, 28, 32);
    public static readonly Color Panel = Color.FromArgb(38, 38, 44);
    public static readonly Color Text = Color.FromArgb(235, 235, 240);
    public static readonly Color Muted = Color.FromArgb(150, 150, 160);
    public static readonly Color Good = Color.FromArgb(120, 220, 140);
    public static readonly Color Record = Color.FromArgb(240, 68, 56);
    public static readonly Color Warn = Color.FromArgb(255, 170, 90);
}

static class AppIcon
{
    public static Bitmap Render(int size, bool recording = false)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = size / 32f;

        var rect = new RectangleF(1 * s, 1 * s, 30 * s, 30 * s);
        using (var path = ImageOps.RoundedRect(rect, 7 * s))
        using (var brush = new LinearGradientBrush(rect, Theme.Accent, Theme.Accent2, 45f))
            g.FillPath(brush, path);

        // Four crop corners.
        using var pen = new Pen(Color.White, Math.Max(1.6f, 2.8f * s))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float a = 8.5f * s, b = 23.5f * s, l = 5.5f * s;
        g.DrawLines(pen, new[] { new PointF(a, a + l), new PointF(a, a), new PointF(a + l, a) });
        g.DrawLines(pen, new[] { new PointF(b - l, a), new PointF(b, a), new PointF(b, a + l) });
        g.DrawLines(pen, new[] { new PointF(a, b - l), new PointF(a, b), new PointF(a + l, b) });
        g.DrawLines(pen, new[] { new PointF(b - l, b), new PointF(b, b), new PointF(b, b - l) });

        if (recording)
        {
            float d = 15 * s;
            using var ring = new SolidBrush(Color.White);
            using var dot = new SolidBrush(Theme.Record);
            g.FillEllipse(ring, 32 * s - d - 1.5f * s, 32 * s - d - 1.5f * s, d + 3 * s, d + 3 * s);
            g.FillEllipse(dot, 32 * s - d, 32 * s - d, d, d);
        }
        return bmp;
    }

    public static Icon Get(int size, bool recording = false)
    {
        using var b = Render(size, recording);
        IntPtr h = b.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { Native.DestroyIcon(h); }
    }

    public static void WriteIco(string path, int[] sizes)
    {
        var pngs = sizes.Select(sz => { using var b = Render(sz); return ImageOps.Png(b); }).ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }
}
