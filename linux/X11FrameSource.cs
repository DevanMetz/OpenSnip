using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenSnip;

/// <summary>
/// One BGRA frame of an X11 rectangle. Windows in <c>exclude</c> are painted out:
/// the root image includes them, so those rectangles are rebuilt from the wallpaper
/// and the windows underneath. That is what keeps the outline and the control bar
/// out of the file while they stay on screen.
/// </summary>
sealed class X11FrameSource : IDisposable
{
    const string X11 = "libX11";
    const int ZPixmap = 2;
    const ulong AllPlanes = ~0UL;
    const int IsViewable = 2;
    const int InputOutput = 1;

    readonly int _x, _y, _width, _height;
    IntPtr _display;
    IntPtr _root;
    IntPtr _shmImage;
    IntPtr _shmAddress;
    int _shmId = -1;
    bool _shmReady;
    IntPtr _wallpaper;
    bool _wallpaperChecked;
    byte[] _row = Array.Empty<byte>();
    bool _disposed;
    static int _sawError;
    static readonly XErrorHandler ErrorDelegate = OnError;

    public int Width => _width;
    public int Height => _height;

    public X11FrameSource(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Invalid frame size");
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _display = XOpenDisplay(null);
        if (_display == IntPtr.Zero) throw new InvalidOperationException("Cannot open the X11 display.");
        XSetErrorHandler(ErrorDelegate);
        _root = XDefaultRootWindow(_display);
        TryAttachShm();
    }

    public bool Grab(byte[] destination, IReadOnlyList<long> exclude, bool cursor)
    {
        if (_disposed || destination == null || destination.Length < _width * _height * 4) return false;
        if (!ReadRoot(destination)) return false;
        if (exclude != null && exclude.Count > 0) RemoveChrome(destination, exclude);
        if (cursor) DrawCursor(destination);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachShm();
        if (_display != IntPtr.Zero)
        {
            try { XCloseDisplay(_display); } catch { }
            _display = IntPtr.Zero;
        }
    }

    bool ReadRoot(byte[] destination)
    {
        if (_shmReady && XShmGetImage(_display, _root, _shmImage, _x, _y, AllPlanes))
        {
            var header = Marshal.PtrToStructure<XImageHeader>(_shmImage);
            Convert(header.data == IntPtr.Zero ? _shmAddress : header.data, header, _width, _height, destination, 0, 0);
            return true;
        }

        IntPtr image = XGetImage(_display, _root, _x, _y, (uint)_width, (uint)_height, AllPlanes, ZPixmap);
        if (image == IntPtr.Zero) return false;
        try
        {
            var header = Marshal.PtrToStructure<XImageHeader>(image);
            if (header.data == IntPtr.Zero) return false;
            Convert(header.data, header, header.width, header.height, destination, 0, 0);
            return true;
        }
        finally { XDestroyImage(image); }
    }

    void RemoveChrome(byte[] destination, IReadOnlyList<long> exclude)
    {
        var skip = new HashSet<long>();
        var holes = new List<Avalonia.PixelRect>();
        foreach (long id in exclude)
        {
            if (id == 0) continue;
            skip.Add(id);
            if (!TryBounds((IntPtr)id, out var bounds)) continue;
            var hole = RecordGeometry.Intersect(bounds, new Avalonia.PixelRect(_x, _y, _width, _height));
            if (hole.Width > 0 && hole.Height > 0) holes.Add(hole);
        }
        if (holes.Count == 0) return;

        var windows = new List<WindowSlice>();
        Collect(_root, windows, skip);

        foreach (var hole in holes)
        {
            BlitWallpaper(destination, hole);
            foreach (var window in windows)
            {
                var overlap = RecordGeometry.Intersect(window.Interior, hole);
                if (overlap.Width <= 0 || overlap.Height <= 0) continue;
                BlitWindow(destination, window.Id, window.Interior, overlap);
            }
        }
    }

    void Collect(IntPtr window, List<WindowSlice> windows, HashSet<long> skip)
    {
        if (XQueryTree(_display, window, out _, out _, out IntPtr children, out uint count) == 0 || children == IntPtr.Zero)
            return;
        try
        {
            for (int i = 0; i < count; i++)
            {
                var child = Marshal.ReadIntPtr(children, i * IntPtr.Size);
                long id = child.ToInt64();
                if (skip.Contains(id)) continue;
                if (TrySlice(child, out var slice)) windows.Add(slice);
                Collect(child, windows, skip);
            }
        }
        finally { XFree(children); }
    }

    bool TrySlice(IntPtr window, out WindowSlice slice)
    {
        slice = default;
        if (!TryAttributes(window, out int klass, out int mapState)) return false;
        if (klass != InputOutput || mapState != IsViewable) return false;
        if (XGetGeometry(_display, window, out _, out _, out _, out uint width, out uint height, out uint border, out _) == 0)
            return false;
        if (width == 0 || height == 0) return false;
        if (!XTranslateCoordinates(_display, window, _root, 0, 0, out int x, out int y, out _)) return false;
        int b = (int)border;
        slice = new WindowSlice
        {
            Id = window,
            Interior = new Avalonia.PixelRect(x, y, (int)width, (int)height),
            Bounds = new Avalonia.PixelRect(x - b, y - b, (int)width + b * 2, (int)height + b * 2),
        };
        return true;
    }

    bool TryBounds(IntPtr window, out Avalonia.PixelRect bounds)
    {
        bounds = default;
        if (!TrySlice(window, out var slice)) return false;
        bounds = slice.Bounds;
        return true;
    }

    bool TryAttributes(IntPtr window, out int klass, out int mapState)
    {
        klass = 0;
        mapState = 0;
        var attributes = new XWindowAttributes();
        if (XGetWindowAttributes(_display, window, ref attributes) == 0) return false;
        klass = attributes.Class;
        mapState = attributes.MapState;
        return true;
    }

    void BlitWallpaper(byte[] destination, Avalonia.PixelRect hole)
    {
        IntPtr pixmap = Wallpaper();
        if (pixmap == IntPtr.Zero) return;
        BlitDrawable(pixmap, hole.X, hole.Y, hole, destination);
    }

    void BlitWindow(byte[] destination, IntPtr window, Avalonia.PixelRect interior, Avalonia.PixelRect overlap)
    {
        _sawError = 0;
        IntPtr pixmap = XCompositeNameWindowPixmap(_display, window);
        XSync(_display, false);
        if (_sawError != 0 || pixmap == IntPtr.Zero) return;
        try
        {
            BlitDrawable(pixmap, overlap.X - interior.X, overlap.Y - interior.Y, overlap, destination);
        }
        finally
        {
            try { XFreePixmap(_display, pixmap); } catch { }
        }
    }

    void BlitDrawable(IntPtr drawable, int srcX, int srcY, Avalonia.PixelRect destRoot, byte[] destination)
    {
        if (srcX < 0 || srcY < 0 || destRoot.Width <= 0 || destRoot.Height <= 0) return;
        _sawError = 0;
        IntPtr image = XGetImage(_display, drawable, srcX, srcY, (uint)destRoot.Width, (uint)destRoot.Height, AllPlanes, ZPixmap);
        XSync(_display, false);
        if (_sawError != 0 || image == IntPtr.Zero) return;
        try
        {
            var header = Marshal.PtrToStructure<XImageHeader>(image);
            if (header.data == IntPtr.Zero) return;
            Convert(header.data, header, header.width, header.height, destination, destRoot.X - _x, destRoot.Y - _y);
        }
        finally { XDestroyImage(image); }
    }

    IntPtr Wallpaper()
    {
        if (_wallpaperChecked) return _wallpaper;
        _wallpaperChecked = true;
        IntPtr atom = XInternAtom(_display, "_XROOTPMAP_ID", true);
        if (atom == IntPtr.Zero) return IntPtr.Zero;
        int status = XGetWindowProperty(_display, _root, atom, 0, 1, false, (IntPtr)20, out _, out _, out nuint count, out _, out IntPtr prop);
        if (status != 0 || prop == IntPtr.Zero || count < 1) return IntPtr.Zero;
        try { _wallpaper = (IntPtr)Marshal.ReadInt64(prop); }
        finally { XFree(prop); }
        return _wallpaper;
    }

    void DrawCursor(byte[] destination)
    {
        IntPtr image = XFixesGetCursorImage(_display);
        if (image == IntPtr.Zero) return;
        try
        {
            var cursor = Marshal.PtrToStructure<XFixesCursorImage>(image);
            if (cursor.Pixels == IntPtr.Zero || cursor.Width == 0 || cursor.Height == 0) return;
            int left = cursor.X - cursor.XHot;
            int top = cursor.Y - cursor.YHot;
            for (int row = 0; row < cursor.Height; row++)
            {
                int destY = top + row - _y;
                if (destY < 0 || destY >= _height) continue;
                for (int col = 0; col < cursor.Width; col++)
                {
                    int destX = left + col - _x;
                    if (destX < 0 || destX >= _width) continue;
                    IntPtr pixel = IntPtr.Add(cursor.Pixels, (row * cursor.Width + col) * IntPtr.Size);
                    uint argb = (uint)Marshal.ReadInt32(pixel);
                    byte a = (byte)(argb >> 24);
                    if (a == 0) continue;
                    byte r = (byte)(argb >> 16);
                    byte g = (byte)(argb >> 8);
                    byte b = (byte)argb;
                    int offset = (destY * _width + destX) * 4;
                    if (a == 255)
                    {
                        destination[offset] = b;
                        destination[offset + 1] = g;
                        destination[offset + 2] = r;
                        destination[offset + 3] = 255;
                        continue;
                    }
                    int inv = 255 - a;
                    destination[offset] = (byte)(b + destination[offset] * inv / 255);
                    destination[offset + 1] = (byte)(g + destination[offset + 1] * inv / 255);
                    destination[offset + 2] = (byte)(r + destination[offset + 2] * inv / 255);
                    destination[offset + 3] = 255;
                }
            }
        }
        finally { XFree(image); }
    }

    void TryAttachShm()
    {
        try
        {
            if (!XShmQueryExtension(_display)) return;
            int screen = XDefaultScreen(_display);
            var info = new XShmSegmentInfo();
            IntPtr image = XShmCreateImage(_display, XDefaultVisual(_display, screen), (uint)XDefaultDepth(_display, screen),
                ZPixmap, IntPtr.Zero, ref info, (uint)_width, (uint)_height);
            if (image == IntPtr.Zero) return;
            var header = Marshal.PtrToStructure<XImageHeader>(image);
            int bytes = header.bytes_per_line * Math.Max(1, header.height);
            if (bytes <= 0)
            {
                XDestroyImage(image);
                return;
            }
            info.ShmId = shmget(0, (nuint)bytes, 512 | 384);
            if (info.ShmId < 0)
            {
                XDestroyImage(image);
                return;
            }
            info.ShmAddress = shmat(info.ShmId, IntPtr.Zero, 0);
            if (info.ShmAddress == new IntPtr(-1))
            {
                shmctl(info.ShmId, 0, IntPtr.Zero);
                XDestroyImage(image);
                return;
            }
            Marshal.WriteIntPtr(image, 16, info.ShmAddress);
            info.ReadOnly = 0;
            _sawError = 0;
            bool attached = XShmAttach(_display, ref info);
            XSync(_display, false);
            if (!attached || _sawError != 0)
            {
                Marshal.WriteIntPtr(image, 16, IntPtr.Zero);
                XDestroyImage(image);
                shmdt(info.ShmAddress);
                shmctl(info.ShmId, 0, IntPtr.Zero);
                return;
            }
            shmctl(info.ShmId, 0, IntPtr.Zero);
            _shmImage = image;
            _shmAddress = info.ShmAddress;
            _shmId = info.ShmId;
            _shmReady = true;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    void DetachShm()
    {
        if (_shmImage == IntPtr.Zero) return;
        try
        {
            var info = new XShmSegmentInfo { ShmId = _shmId, ShmAddress = _shmAddress };
            XShmDetach(_display, ref info);
            Marshal.WriteIntPtr(_shmImage, 16, IntPtr.Zero);
            XDestroyImage(_shmImage);
            if (_shmAddress != IntPtr.Zero) shmdt(_shmAddress);
        }
        catch { }
        _shmImage = IntPtr.Zero;
        _shmReady = false;
    }

    struct WindowSlice
    {
        public IntPtr Id;
        public Avalonia.PixelRect Interior;
        public Avalonia.PixelRect Bounds;
    }

    [StructLayout(LayoutKind.Explicit, Size = 136)]
    struct XWindowAttributes
    {
        [FieldOffset(40)] public int Class;
        [FieldOffset(92)] public int MapState;
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

    [StructLayout(LayoutKind.Sequential)]
    struct XShmSegmentInfo
    {
        public ulong ShmSeg;
        public int ShmId;
        public IntPtr ShmAddress;
        public int ReadOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XFixesCursorImage
    {
        public short X, Y;
        public ushort Width, Height;
        public ushort XHot, YHot;
        public ulong Serial;
        public IntPtr Pixels;
        public ulong Atom;
        public IntPtr Name;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    static int OnError(IntPtr display, IntPtr errorEvent)
    {
        _sawError = 1;
        return 0;
    }

    void Convert(IntPtr data, XImageHeader header, int sourceWidth, int sourceHeight, byte[] destination, int destX, int destY)
    {
        if (data == IntPtr.Zero || sourceWidth <= 0 || sourceHeight <= 0) return;
        int bpp = header.bits_per_pixel;
        if (bpp < 16 || bpp > 32) return;
        int bytesPerPixel = (bpp + 7) / 8;
        int stride = Math.Abs(header.bytes_per_line);
        if (stride < sourceWidth * bytesPerPixel) return;

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
        if (_row.Length < stride) _row = new byte[stride];
        var row = _row;

        for (int y = 0; y < sourceHeight; y++)
        {
            int destRow = destY + y;
            if (destRow < 0 || destRow >= _height) continue;
            Marshal.Copy(IntPtr.Add(data, y * stride), row, 0, stride);
            for (int x = 0; x < sourceWidth; x++)
            {
                int destCol = destX + x;
                if (destCol < 0 || destCol >= _width) continue;
                int source = x * bytesPerPixel;
                uint pixel = bytesPerPixel switch
                {
                    4 => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(row.AsSpan(source, 4)) : BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(source, 4)),
                    3 => bigEndian
                        ? (uint)((row[source] << 16) | (row[source + 1] << 8) | row[source + 2])
                        : (uint)(row[source] | (row[source + 1] << 8) | (row[source + 2] << 16)),
                    2 => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(source, 2)) : BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(source, 2)),
                    _ => 0u,
                };
                int index = (destRow * _width + destCol) * 4;
                destination[index] = Channel(pixel, blueMask);
                destination[index + 1] = Channel(pixel, greenMask);
                destination[index + 2] = Channel(pixel, redMask);
                destination[index + 3] = 255;
            }
        }
    }

    static byte Channel(uint pixel, ulong mask)
    {
        if (mask == 0) return 0;
        int shift = BitOperations.TrailingZeroCount(mask);
        ulong max = mask >> shift;
        ulong value = ((ulong)pixel & mask) >> shift;
        return (byte)Math.Round(value * 255.0 / max);
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(string name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern int XDefaultScreen(IntPtr display);
    [DllImport(X11)] static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(X11)] static extern IntPtr XDefaultVisual(IntPtr display, int screen);
    [DllImport(X11)] static extern int XSync(IntPtr display, bool discard);
    [DllImport(X11)] static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X11)] static extern int XGetWindowAttributes(IntPtr display, IntPtr window, ref XWindowAttributes attributes);
    [DllImport(X11)] static extern bool XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, out int destX, out int destY, out IntPtr child);
    [DllImport(X11)] static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport(X11)] static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport(X11)] static extern int XDestroyImage(IntPtr image);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport(X11)] static extern int XFreePixmap(IntPtr display, IntPtr pixmap);
    [DllImport(X11)] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(X11)] static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr atom, long offset, long length, bool delete, IntPtr type, out IntPtr actualType, out int actualFormat, out nuint count, out nuint bytesAfter, out IntPtr prop);

    [DllImport("libXext")] static extern bool XShmQueryExtension(IntPtr display);
    [DllImport("libXext")] static extern IntPtr XShmCreateImage(IntPtr display, IntPtr visual, uint depth, int format, IntPtr data, ref XShmSegmentInfo info, uint width, uint height);
    [DllImport("libXext")] static extern bool XShmAttach(IntPtr display, ref XShmSegmentInfo info);
    [DllImport("libXext")] static extern bool XShmGetImage(IntPtr display, IntPtr drawable, IntPtr image, int x, int y, ulong planeMask);
    [DllImport("libXext")] static extern bool XShmDetach(IntPtr display, ref XShmSegmentInfo info);

    [DllImport("libXcomposite.so.1")] static extern IntPtr XCompositeNameWindowPixmap(IntPtr display, IntPtr window);
    [DllImport("libXfixes")] static extern IntPtr XFixesGetCursorImage(IntPtr display);

    [DllImport("libc")] static extern int shmget(int key, nuint size, int flags);
    [DllImport("libc")] static extern IntPtr shmat(int id, IntPtr address, int flags);
    [DllImport("libc")] static extern int shmdt(IntPtr address);
    [DllImport("libc")] static extern int shmctl(int id, int command, IntPtr buffer);
}
