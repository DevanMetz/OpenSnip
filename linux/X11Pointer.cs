using System.Runtime.InteropServices;

namespace OpenSnip;

/// <summary>
/// Reads the X11 pointer. The import uses libX11.so.6 because desktops ship that soname;
/// the unversioned libX11.so symlink is only in the development package.
/// </summary>
static class X11Pointer
{
    public static (int X, int Y)? TryGet(string displayName = null)
    {
        if (!TryOpen(displayName, out var display)) return null;
        try
        {
            var root = Native.XDefaultRootWindow(display);
            if (root == IntPtr.Zero) return null;
            if (Native.XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out _) == 0)
                return null;
            return (x, y);
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally
        {
            try { Native.XCloseDisplay(display); } catch { }
        }
    }

    public static bool TryWarp(string displayName, int x, int y)
    {
        if (!TryOpen(displayName, out var display)) return false;
        try
        {
            var root = Native.XDefaultRootWindow(display);
            if (root == IntPtr.Zero) return false;
            Native.XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, x, y);
            Native.XSync(display, 0);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        finally
        {
            try { Native.XCloseDisplay(display); } catch { }
        }
    }

    static bool TryOpen(string displayName, out IntPtr display)
    {
        display = IntPtr.Zero;
        try
        {
            display = Native.XOpenDisplay(displayName);
            return display != IntPtr.Zero;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    static class Native
    {
        const string Lib = "libX11.so.6";

        [DllImport(Lib)] public static extern IntPtr XOpenDisplay(string displayName);
        [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr display);
        [DllImport(Lib)] public static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(Lib)] public static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
        [DllImport(Lib)] public static extern int XWarpPointer(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);
        [DllImport(Lib)] public static extern int XSync(IntPtr display, int discard);
    }
}
