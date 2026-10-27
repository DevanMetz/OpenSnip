using System.Runtime.InteropServices;
using Avalonia;

namespace OpenSnip;

/// <summary>Viewable top-level X11 windows, front to back, in root-window pixels.</summary>
sealed class X11WindowList
{
    public int Width { get; }
    public int Height { get; }
    public PixelRect[] Windows { get; }

    public X11WindowList(int width, int height, PixelRect[] windows)
    {
        Width = width;
        Height = height;
        Windows = windows ?? Array.Empty<PixelRect>();
    }
}

static class X11Windows
{
    const string Lib = "libX11.so.6";
    const int IsViewable = 2;
    const int InputOnly = 2;
    const int Success = 0;
    const int MaxWindows = 4096;

    // Xlib stores format-32 properties as longs. On LP64 that is one pointer per item.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    static readonly XErrorHandler IgnoreErrors = static (_, _) => 0;
    static readonly IntPtr IgnoreErrorsPtr = Marshal.GetFunctionPointerForDelegate(IgnoreErrors);

    public static X11WindowList TrySnapshot()
    {
        if (PlatformInfo.IsWayland || !PlatformInfo.HasDisplay) return null;
        try
        {
            return Snapshot();
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    static X11WindowList Snapshot()
    {
        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return null;

        IntPtr previousHandler = IntPtr.Zero;
        bool handlerSet = false;
        try
        {
            // A window can close between XQueryTree and XGetWindowAttributes.
            // The default handler would abort the process on that BadWindow.
            previousHandler = XSetErrorHandler(IgnoreErrorsPtr);
            handlerSet = true;
            var list = Read(display);
            XSync(display, 0);
            return list;
        }
        finally
        {
            if (handlerSet)
            {
                try { XSetErrorHandler(previousHandler); } catch { }
            }
            try { XCloseDisplay(display); } catch { }
        }
    }

    static X11WindowList Read(IntPtr display)
    {
        IntPtr root = XDefaultRootWindow(display);
        if (root == IntPtr.Zero) return null;
        // A monitor left or above the origin means root pixel (0, 0) is not the
        // top-left of the desktop image. Skip window targets rather than crop the wrong one.
        if (HasNegativeOrigin(display, root)) return null;
        if (XGetGeometry(display, root, out _, out _, out _, out uint width, out uint height, out _, out _) == 0)
            return null;
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue) return null;

        int imageWidth = (int)width;
        int imageHeight = (int)height;
        IntPtr typeAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE", 1);
        IntPtr desktopAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE_DESKTOP", 1);

        if (XQueryTree(display, root, out _, out _, out IntPtr children, out uint count) == 0)
            return new X11WindowList(imageWidth, imageHeight, Array.Empty<PixelRect>());

        try
        {
            if (children == IntPtr.Zero || count == 0)
                return new X11WindowList(imageWidth, imageHeight, Array.Empty<PixelRect>());

            int available = (int)Math.Min(count, MaxWindows);
            int start = (int)count - available;
            var ids = new IntPtr[available];
            Marshal.Copy(IntPtr.Add(children, start * IntPtr.Size), ids, 0, available);

            var windows = new List<PixelRect>(available);
            var attrs = new XWindowAttributes();
            for (int i = ids.Length - 1; i >= 0; i--)
            {
                if (ids[i] == IntPtr.Zero) continue;
                if (XGetWindowAttributes(display, ids[i], ref attrs) == 0) continue;
                if (attrs.map_state != IsViewable || attrs.c_class == InputOnly) continue;
                if (desktopAtom != IntPtr.Zero && HasAtom(display, ids[i], typeAtom, desktopAtom)) continue;

                int border = Math.Max(0, attrs.border_width);
                int w = attrs.width + border * 2;
                int h = attrs.height + border * 2;
                if (w < 8 || h < 8) continue;

                var clipped = WindowHit.Clip(new PixelRect(attrs.x, attrs.y, w, h), imageWidth, imageHeight);
                if (clipped.Width < 8 || clipped.Height < 8) continue;
                windows.Add(clipped);
            }

            return new X11WindowList(imageWidth, imageHeight, windows.ToArray());
        }
        finally
        {
            if (children != IntPtr.Zero)
            {
                try { XFree(children); } catch { }
            }
        }
    }

    static GetMonitors _getMonitors;
    static FreeMonitors _freeMonitors;
    static int _randr; // 0 unknown, 1 ready, -1 unavailable

    static bool HasNegativeOrigin(IntPtr display, IntPtr root)
    {
        if (!EnsureRandr()) return false;
        try
        {
            IntPtr monitors = _getMonitors(display, root, 1, out int count);
            if (monitors == IntPtr.Zero || count < 1) return false;
            try
            {
                // XRRMonitorInfo on LP64: int x at 20, int y at 24, struct size 56.
                const int stride = 56;
                const int xOffset = 20;
                const int yOffset = 24;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(monitors, i * stride);
                    if (Marshal.ReadInt32(item, xOffset) < 0 || Marshal.ReadInt32(item, yOffset) < 0)
                        return true;
                }
                return false;
            }
            finally { _freeMonitors(monitors); }
        }
        catch { return false; }
    }

    static bool EnsureRandr()
    {
        if (_randr != 0) return _randr > 0;
        _randr = -1;
        try
        {
            if (!NativeLibrary.TryLoad("libXrandr.so.2", out var library)) return false;
            _getMonitors = Marshal.GetDelegateForFunctionPointer<GetMonitors>(NativeLibrary.GetExport(library, "XRRGetMonitors"));
            _freeMonitors = Marshal.GetDelegateForFunctionPointer<FreeMonitors>(NativeLibrary.GetExport(library, "XRRFreeMonitors"));
            _randr = 1;
            return true;
        }
        catch
        {
            _getMonitors = null;
            _freeMonitors = null;
            return false;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr GetMonitors(IntPtr display, IntPtr window, int getActive, out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FreeMonitors(IntPtr monitors);

    static bool HasAtom(IntPtr display, IntPtr window, IntPtr property, IntPtr atom)
    {
        if (property == IntPtr.Zero || atom == IntPtr.Zero) return false;
        IntPtr data = IntPtr.Zero;
        try
        {
            int status = XGetWindowProperty(display, window, property, 0, 32, 0, (IntPtr)4,
                out _, out int format, out ulong nitems, out _, out data);
            if (status != Success || data == IntPtr.Zero || format != 32 || nitems == 0) return false;

            int stride = IntPtr.Size;
            ulong wanted = unchecked((ulong)atom.ToInt64());
            int count = (int)Math.Min(nitems, 32);
            for (int i = 0; i < count; i++)
            {
                long value = stride == 8
                    ? Marshal.ReadInt64(data, i * stride)
                    : Marshal.ReadInt32(data, i * stride);
                if (unchecked((ulong)value) == wanted) return true;
            }
            return false;
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                try { XFree(data); } catch { }
            }
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 136)]
    struct XWindowAttributes
    {
        [FieldOffset(0)] public int x;
        [FieldOffset(4)] public int y;
        [FieldOffset(8)] public int width;
        [FieldOffset(12)] public int height;
        [FieldOffset(16)] public int border_width;
        [FieldOffset(40)] public int c_class;
        [FieldOffset(92)] public int map_state;
    }

    [DllImport(Lib)] static extern IntPtr XOpenDisplay(string displayName);
    [DllImport(Lib)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);
    [DllImport(Lib)] static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr parentReturn, out IntPtr childrenReturn, out uint nchildren);
    [DllImport(Lib)] static extern int XGetWindowAttributes(IntPtr display, IntPtr window, ref XWindowAttributes attributes);
    [DllImport(Lib)] static extern IntPtr XInternAtom(IntPtr display, string name, int onlyIfExists);
    [DllImport(Lib)] static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long longOffset, long longLength, int delete, IntPtr reqType, out IntPtr actualType, out int actualFormat, out ulong nitems, out ulong bytesAfter, out IntPtr prop);
    [DllImport(Lib)] static extern int XFree(IntPtr data);
    [DllImport(Lib)] static extern int XSync(IntPtr display, int discard);
    [DllImport(Lib)] static extern IntPtr XSetErrorHandler(IntPtr handler);
}
