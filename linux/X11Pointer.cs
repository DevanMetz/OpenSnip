using System.Runtime.InteropServices;
using Avalonia;

namespace OpenSnip;

/// <summary>
/// Root-pixel pointer position on X11. A drag is grabbed by the monitor where it started,
/// and that window's coordinates do not continue linearly onto a monitor with another scale.
/// Wayland does not expose the pointer, so the overlay keeps the event it was given.
/// </summary>
sealed class X11Pointer : IDisposable
{
    readonly IntPtr _display;
    readonly CloseDisplay _close;
    readonly QueryPointer _query;
    bool _closed;

    X11Pointer(IntPtr display, IntPtr root, CloseDisplay close, QueryPointer query)
    {
        _display = display;
        Root = root;
        _close = close;
        _query = query;
    }

    public IntPtr Display => _display;
    public IntPtr Root { get; }

    public static X11Pointer TryOpen()
    {
        if (!PlatformInfo.HasDisplay || PlatformInfo.IsWayland) return null;
        var lib = X11Lib.Handle;
        if (lib == IntPtr.Zero) return null;
        try
        {
            var open = Marshal.GetDelegateForFunctionPointer<OpenDisplay>(NativeLibrary.GetExport(lib, "XOpenDisplay"));
            var close = Marshal.GetDelegateForFunctionPointer<CloseDisplay>(NativeLibrary.GetExport(lib, "XCloseDisplay"));
            var rootOf = Marshal.GetDelegateForFunctionPointer<DefaultRoot>(NativeLibrary.GetExport(lib, "XDefaultRootWindow"));
            var query = Marshal.GetDelegateForFunctionPointer<QueryPointer>(NativeLibrary.GetExport(lib, "XQueryPointer"));
            var display = open(null);
            if (display == IntPtr.Zero) return null;
            var root = rootOf(display);
            if (root == IntPtr.Zero)
            {
                close(display);
                return null;
            }
            return new X11Pointer(display, root, close, query);
        }
        catch (EntryPointNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
    }

    public PixelPoint? Position()
    {
        if (_closed) return null;
        try
        {
            if (_query(_display, Root, out _, out _, out int x, out int y, out _, out _, out _) == 0)
                return null;
            return new PixelPoint(x, y);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        try { _close(_display); } catch { }
    }

    public static (int X, int Y)? TryGet(string displayName = null)
    {
        try
        {
            var display = Native.XOpenDisplay(displayName);
            if (display == IntPtr.Zero) return null;
            try
            {
                var root = Native.XDefaultRootWindow(display);
                return root != IntPtr.Zero && Native.XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out _) != 0 ? (x, y) : null;
            }
            finally { Native.XCloseDisplay(display); }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    public static bool TryWarp(string displayName, int x, int y)
    {
        try
        {
            var display = Native.XOpenDisplay(displayName);
            if (display == IntPtr.Zero) return false;
            try
            {
                var root = Native.XDefaultRootWindow(display);
                if (root == IntPtr.Zero) return false;
                Native.XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, x, y);
                Native.XSync(display, 0);
                return true;
            }
            finally { Native.XCloseDisplay(display); }
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    static class Native
    {
        const string Lib = "libX11.so.6";
        [DllImport(Lib)] public static extern IntPtr XOpenDisplay(string name);
        [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr display);
        [DllImport(Lib)] public static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(Lib)] public static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
        [DllImport(Lib)] public static extern int XWarpPointer(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);
        [DllImport(Lib)] public static extern int XSync(IntPtr display, int discard);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr OpenDisplay(string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int CloseDisplay(IntPtr display);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr DefaultRoot(IntPtr display);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int QueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
}

static class X11Lib
{
    static readonly object Gate = new();
    static IntPtr _handle;
    static bool _tried;

    public static IntPtr Handle
    {
        get
        {
            lock (Gate)
            {
                if (_tried) return _handle;
                _tried = true;
                foreach (var name in new[] { "libX11.so.6", "libX11.so", "libX11" })
                {
                    if (NativeLibrary.TryLoad(name, out var handle))
                    {
                        _handle = handle;
                        return _handle;
                    }
                }
                return IntPtr.Zero;
            }
        }
    }
}
