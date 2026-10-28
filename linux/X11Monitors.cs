using System.Runtime.InteropServices;
using Avalonia;

namespace OpenSnip;

/// <summary>Monitor rectangles in X11 root coordinates, matching a full-root capture.</summary>
static class X11Monitors
{
    const string X11 = "libX11";

    public static IReadOnlyList<PixelRect> Query()
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero) return Array.Empty<PixelRect>();
            var monitors = QueryXinerama(display);
            if (monitors.Count > 0) return monitors;
            return new[] { Root(display) };
        }
        catch (DllNotFoundException) { return Array.Empty<PixelRect>(); }
        finally
        {
            if (display != IntPtr.Zero) { try { XCloseDisplay(display); } catch { } }
        }
    }

    /// <summary>The work area of the monitor that contains most of <paramref name="area"/>.</summary>
    public static PixelRect WorkArea(PixelRect area)
    {
        var monitors = Query();
        PixelRect monitor = default;
        int best = -1;
        foreach (var candidate in monitors)
        {
            var hit = RecordGeometry.Intersect(area, candidate);
            int overlap = Math.Max(0, hit.Width) * Math.Max(0, hit.Height);
            if (overlap > best)
            {
                best = overlap;
                monitor = candidate;
            }
        }
        if (best <= 0) return area;

        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero) return monitor;
            if (!TryWorkArea(display, out var work)) return monitor;
            var clipped = RecordGeometry.Intersect(work, monitor);
            return clipped.Width > 0 && clipped.Height > 0 ? clipped : monitor;
        }
        catch (DllNotFoundException) { return monitor; }
        finally
        {
            if (display != IntPtr.Zero) { try { XCloseDisplay(display); } catch { } }
        }
    }

    static List<PixelRect> QueryXinerama(IntPtr display)
    {
        var list = new List<PixelRect>();
        try
        {
            if (!XineramaQueryExtension(display, out _, out _) || !XineramaIsActive(display)) return list;
            IntPtr info = XineramaQueryScreens(display, out int count);
            if (info == IntPtr.Zero || count <= 0) return list;
            try
            {
                int size = Marshal.SizeOf<XineramaScreen>();
                for (int i = 0; i < count; i++)
                {
                    var screen = Marshal.PtrToStructure<XineramaScreen>(IntPtr.Add(info, i * size));
                    if (screen.Width > 0 && screen.Height > 0)
                        list.Add(new PixelRect(screen.X, screen.Y, screen.Width, screen.Height));
                }
            }
            finally { XFree(info); }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return list;
    }

    static PixelRect Root(IntPtr display)
    {
        IntPtr root = XDefaultRootWindow(display);
        if (XGetGeometry(display, root, out _, out _, out _, out uint width, out uint height, out _, out _) == 0)
            return default;
        return new PixelRect(0, 0, (int)width, (int)height);
    }

    static bool TryWorkArea(IntPtr display, out PixelRect area)
    {
        area = default;
        IntPtr atom = XInternAtom(display, "_NET_WORKAREA", true);
        if (atom == IntPtr.Zero) return false;
        IntPtr root = XDefaultRootWindow(display);
        int status = XGetWindowProperty(display, root, atom, 0, 4, false, (IntPtr)6, out _, out _, out nuint count, out _, out IntPtr prop);
        if (status != 0 || prop == IntPtr.Zero || count < 4) return false;
        try
        {
            // Format 32 is returned as an array of C longs.
            long x = Marshal.ReadInt64(prop, 0);
            long y = Marshal.ReadInt64(prop, 8);
            long width = Marshal.ReadInt64(prop, 16);
            long height = Marshal.ReadInt64(prop, 24);
            if (width <= 0 || height <= 0) return false;
            area = new PixelRect((int)x, (int)y, (int)width, (int)height);
            return true;
        }
        finally { XFree(prop); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct XineramaScreen
    {
        public int ScreenNumber;
        public short X;
        public short Y;
        public short Width;
        public short Height;
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(string name);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(X11)] static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X11)] static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr atom, long offset, long length, bool delete, IntPtr type, out IntPtr actualType, out int actualFormat, out nuint count, out nuint bytesAfter, out IntPtr prop);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport("libXinerama.so.1")] static extern bool XineramaQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport("libXinerama.so.1")] static extern bool XineramaIsActive(IntPtr display);
    [DllImport("libXinerama.so.1")] static extern IntPtr XineramaQueryScreens(IntPtr display, out int count);
}
