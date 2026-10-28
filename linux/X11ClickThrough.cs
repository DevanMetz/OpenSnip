using System.Runtime.InteropServices;

namespace OpenSnip;

/// <summary>
/// Gives an X11 window an empty input shape so clicks fall through to whatever is underneath.
/// The delay countdown has to stay visible without blocking the menu the user is opening.
/// </summary>
static class X11ClickThrough
{
    const string X11 = "libX11.so.6";
    const string Xext = "libXext.so.6";
    const int ShapeSet = 0;
    const int ShapeInput = 2;

    public static bool TryEnable(IntPtr xid)
    {
        if (xid == IntPtr.Zero || !X11Cursor.IsAvailable) return false;
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero) return false;
            if (XShapeQueryExtension(display, out _, out _) == 0) return false;
            XShapeCombineRectangles(display, xid, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, 0);
            XSync(display, 0);
            return InputRectangleCount(display, xid) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
        finally
        {
            if (display != IntPtr.Zero)
            {
                try { XCloseDisplay(display); } catch { }
            }
        }
    }

    public static int? InputRectangleCount(IntPtr xid)
    {
        if (xid == IntPtr.Zero || !X11Cursor.IsAvailable) return null;
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero) return null;
            if (XShapeQueryExtension(display, out _, out _) == 0) return null;
            return InputRectangleCount(display, xid);
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally
        {
            if (display != IntPtr.Zero)
            {
                try { XCloseDisplay(display); } catch { }
            }
        }
    }

    static int InputRectangleCount(IntPtr display, IntPtr xid)
    {
        IntPtr rects = XShapeGetRectangles(display, xid, ShapeInput, out int count, out _);
        if (rects != IntPtr.Zero) XFree(rects);
        return count;
    }

    [DllImport(X11)] static extern IntPtr XOpenDisplay(string displayName);
    [DllImport(X11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] static extern int XSync(IntPtr display, int discard);
    [DllImport(X11)] static extern int XFree(IntPtr data);
    [DllImport(Xext)] static extern int XShapeQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(Xext)] static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int destKind, int xOff, int yOff, IntPtr rectangles, int count, int op, int ordering);
    [DllImport(Xext)] static extern IntPtr XShapeGetRectangles(IntPtr display, IntPtr window, int kind, out int count, out int ordering);
}
