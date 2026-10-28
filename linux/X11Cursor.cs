using System.Runtime.InteropServices;
using Avalonia;

namespace OpenSnip;

/// <summary>Moves the X11 pointer. Wayland does not allow this, so callers fall back.</summary>
static class X11Cursor
{
    const string Lib = "libX11.so.6";

    public static bool IsAvailable => PlatformInfo.HasDisplay && !PlatformInfo.IsWayland;

    public static (int X, int Y)? TryGet()
    {
        if (!IsAvailable) return null;
        if (!TryOpen(out IntPtr display)) return null;
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            if (root == IntPtr.Zero) return null;
            if (XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out _) == 0)
                return null;
            return (x, y);
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally
        {
            try { XCloseDisplay(display); } catch { }
        }
    }

    public static bool TryWarp(int x, int y)
    {
        if (!IsAvailable) return false;
        if (!TryOpen(out IntPtr display)) return false;
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            if (root == IntPtr.Zero) return false;
            XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, x, y);
            XSync(display, 0);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        finally
        {
            try { XCloseDisplay(display); } catch { }
        }
    }

    public static PixelPoint? TryClampWarp(int x, int y, PixelRect bounds)
    {
        var clamped = SnipGeometry.ClampTo(x, y, bounds);
        if (!TryWarp(clamped.X, clamped.Y)) return null;
        return clamped;
    }

    static bool TryOpen(out IntPtr display)
    {
        display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            return display != IntPtr.Zero;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [DllImport(Lib)] static extern IntPtr XOpenDisplay(string displayName);
    [DllImport(Lib)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport(Lib)] static extern int XWarpPointer(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);
    [DllImport(Lib)] static extern int XSync(IntPtr display, int discard);
}
