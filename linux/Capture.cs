using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenSnip;

interface ICaptureBackend
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<CapturedFrame> CaptureAsync();
}

sealed class CaptureException : Exception
{
    public CaptureException(string message) : base(message) { }
    public CaptureException(string message, Exception inner) : base(message, inner) { }
}

static class CaptureService
{
    public static async Task<CapturedFrame> CaptureAsync()
    {
        var backends = PlatformInfo.IsWayland
            ? new ICaptureBackend[]
            {
                new PortalScreenshotBackend(),
                new ExternalCaptureBackend("grim", new[] { "grim", "-t", "png" }),
                new ExternalCaptureBackend("gnome-screenshot", new[] { "gnome-screenshot", "-f" }),
                new ExternalCaptureBackend("spectacle", new[] { "spectacle", "-b", "-n", "-o" }),
            }
            : new ICaptureBackend[]
            {
                new X11CaptureBackend(),
                new ExternalCaptureBackend("maim", new[] { "maim" }),
                new ExternalCaptureBackend("ImageMagick import", new[] { "import", "-window", "root" }),
                new ExternalCaptureBackend("scrot", new[] { "scrot", "--silent" }),
            };

        var errors = new List<string>();
        foreach (var backend in backends)
        {
            if (!backend.IsAvailable) continue;
            try
            {
                return await backend.CaptureAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                errors.Add(backend.Name);
            }
        }

        string detail = errors.Count == 0
            ? "No capture backend is available for this session."
            : "Tried: " + string.Join(", ", errors) + ".";
        throw new CaptureException("Screen capture failed. " + detail);
    }

    public static async Task<List<string>> AvailableBackendNamesAsync()
    {
        var names = new List<string>();
        if (PlatformInfo.IsWayland)
        {
            if (await DBus.PortalAvailableAsync()) names.Add("xdg-desktop-portal Screenshot");
            if (PlatformInfo.CommandExists("grim")) names.Add("grim");
            if (PlatformInfo.CommandExists("gnome-screenshot")) names.Add("gnome-screenshot");
            if (PlatformInfo.CommandExists("spectacle")) names.Add("spectacle");
        }
        else
        {
            if (PlatformInfo.HasDisplay && PlatformInfo.LibX11Available) names.Add("libX11 (native)");
            if (PlatformInfo.CommandExists("maim")) names.Add("maim");
            if (PlatformInfo.CommandExists("import")) names.Add("ImageMagick import");
            if (PlatformInfo.CommandExists("scrot")) names.Add("scrot");
        }
        return names;
    }
}

sealed class PortalScreenshotBackend : ICaptureBackend
{
    public string Name => "xdg-desktop-portal Screenshot";
    public bool IsAvailable => DBus.SessionAvailable;

    public async Task<CapturedFrame> CaptureAsync()
    {
        string path = null;
        try
        {
            path = await DBus.PortalScreenshotAsync();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new CaptureException("The portal returned no screenshot file.");
            return CapturedFrame.DecodeFile(path);
        }
        finally
        {
            if (!string.IsNullOrEmpty(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}

sealed class ExternalCaptureBackend : ICaptureBackend
{
    readonly string _executable;
    readonly string[] _arguments;

    public ExternalCaptureBackend(string name, string[] command)
    {
        Name = name;
        _executable = command[0];
        _arguments = command.Skip(1).ToArray();
    }

    public string Name { get; }
    public bool IsAvailable => (PlatformInfo.HasDisplay || PlatformInfo.HasWaylandDisplay) && PlatformInfo.CommandExists(_executable);

    public async Task<CapturedFrame> CaptureAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opensnip-capture-{Guid.NewGuid():N}.png");
        try
        {
            var args = _arguments.Concat(new[] { path }).ToArray();
            var result = await PlatformInfo.RunAsync(_executable, args);
            if (result.Code != 0)
            {
                string message = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                throw new CaptureException($"{Name} exited with code {result.Code}" + (string.IsNullOrEmpty(message) ? "." : $": {message}"));
            }
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new CaptureException($"{Name} did not produce an image.");
            return CapturedFrame.DecodeFile(path);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}

sealed class X11CaptureBackend : ICaptureBackend
{
    const string X11 = "libX11";
    const int ZPixmap = 2;
    const ulong AllPlanes = ~0UL;

    public string Name => "libX11 (native)";
    public bool IsAvailable => PlatformInfo.HasDisplay && !PlatformInfo.IsWayland && PlatformInfo.LibX11Available;

    public Task<CapturedFrame> CaptureAsync() => Task.Run(Capture);

    static CapturedFrame Capture()
    {
        IntPtr display = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero) throw new CaptureException("Cannot open the X11 display.");

            IntPtr root = XDefaultRootWindow(display);
            if (XGetGeometry(display, root, out _, out _, out _, out uint width, out uint height, out _, out _) == 0)
                throw new CaptureException("Cannot read the X11 screen size.");
            if (width == 0 || height == 0) throw new CaptureException("The X11 screen is empty.");

            var monitors = X11Randr.Query(display, root);
            var region = CaptureRegion.From(monitors, (int)width, (int)height);
            if (region.Width <= 0 || region.Height <= 0)
                region = new CaptureRegion(0, 0, (int)width, (int)height);

            image = Grab(display, root, region);
            var rootRegion = new CaptureRegion(0, 0, (int)width, (int)height);
            if (!ImageMatches(image, region) && !SameRegion(region, rootRegion))
            {
                if (image != IntPtr.Zero)
                {
                    try { XDestroyImage(image); } catch { }
                    image = IntPtr.Zero;
                }
                region = rootRegion;
                image = Grab(display, root, region);
            }
            if (image == IntPtr.Zero) throw new CaptureException("XGetImage failed.");

            var header = Marshal.PtrToStructure<XImageHeader>(image);
            if (header.width <= 0 || header.height <= 0 || header.data == IntPtr.Zero)
                throw new CaptureException("XGetImage returned an invalid image.");
            if (header.bits_per_pixel < 16 || header.bits_per_pixel > 32)
                throw new CaptureException($"Unsupported X11 image depth ({header.bits_per_pixel} bpp).");

            int w = header.width;
            int h = header.height;
            int bpp = header.bits_per_pixel;
            int bytesPerPixel = (bpp + 7) / 8;
            int stride = Math.Abs(header.bytes_per_line);
            if (stride < w * bytesPerPixel) throw new CaptureException("Unsupported X11 image stride.");

            ulong redMask = (ulong)header.red_mask.ToInt64();
            ulong greenMask = (ulong)header.green_mask.ToInt64();
            ulong blueMask = (ulong)header.blue_mask.ToInt64();
            if (redMask == 0 && greenMask == 0 && blueMask == 0)
            {
                redMask = 0x00FF0000;
                greenMask = 0x0000FF00;
                blueMask = 0x000000FF;
            }

            bool bigEndian = header.byte_order == 1;
            var output = new byte[w * h * 4];
            var row = new byte[stride];

            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(IntPtr.Add(header.data, y * stride), row, 0, stride);
                int dst = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int offset = x * bytesPerPixel;
                    uint pixel = bytesPerPixel switch
                    {
                        4 => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(row.AsSpan(offset, 4)) : BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(offset, 4)),
                        3 => bigEndian
                            ? (uint)((row[offset] << 16) | (row[offset + 1] << 8) | row[offset + 2])
                            : (uint)(row[offset] | (row[offset + 1] << 8) | (row[offset + 2] << 16)),
                        2 => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(offset, 2)) : BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(offset, 2)),
                        _ => 0u,
                    };
                    output[dst++] = Extract(pixel, blueMask);
                    output[dst++] = Extract(pixel, greenMask);
                    output[dst++] = Extract(pixel, redMask);
                    output[dst++] = 255;
                }
            }

            var frame = new CapturedFrame(w, h, output);
            if (header.width == region.Width && header.height == region.Height)
                frame.NoteOrigin(region.X, region.Y);
            return frame;
        }
        catch (DllNotFoundException ex) { throw new CaptureException("libX11 is not available.", ex); }
        catch (EntryPointNotFoundException ex) { throw new CaptureException("libX11 is missing a required function.", ex); }
        finally
        {
            if (image != IntPtr.Zero) { try { XDestroyImage(image); } catch { } }
            if (display != IntPtr.Zero) { try { XCloseDisplay(display); } catch { } }
        }
    }

    static IntPtr Grab(IntPtr display, IntPtr root, CaptureRegion region) =>
        XGetImage(display, root, region.X, region.Y, (uint)region.Width, (uint)region.Height, AllPlanes, ZPixmap);

    static bool ImageMatches(IntPtr image, CaptureRegion region)
    {
        if (image == IntPtr.Zero) return false;
        var header = Marshal.PtrToStructure<XImageHeader>(image);
        return header.width == region.Width && header.height == region.Height && header.data != IntPtr.Zero;
    }

    static bool SameRegion(CaptureRegion a, CaptureRegion b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    static byte Extract(uint pixel, ulong mask)
    {
        if (mask == 0) return 0;
        int shift = BitOperations.TrailingZeroCount(mask);
        ulong max = mask >> shift;
        ulong value = ((ulong)pixel & mask) >> shift;
        return (byte)Math.Round(value * 255.0 / max);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XImageHeader
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public IntPtr red_mask;
        public IntPtr green_mask;
        public IntPtr blue_mask;
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(string displayName);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);
    [DllImport(X11)] static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport(X11)] static extern int XDestroyImage(IntPtr image);
}
