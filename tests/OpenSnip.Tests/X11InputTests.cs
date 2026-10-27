using System.Runtime.InteropServices;

namespace OpenSnip.Tests;

public class X11InputTests
{
    const string Lib = "libX11.so.6";
    const ulong CwOverrideRedirect = 512;

    [Fact]
    public void Arrow_nudge_moves_the_x11_pointer_and_back()
    {
        if (!X11Cursor.IsAvailable) return;
        var start = X11Cursor.TryGet();
        if (start == null) return;

        try
        {
            Assert.True(X11Cursor.TryWarp(start.Value.X + 7, start.Value.Y + 3));
            var moved = X11Cursor.TryGet();
            Assert.NotNull(moved);
            Assert.Equal(start.Value.X + 7, moved.Value.X);
            Assert.Equal(start.Value.Y + 3, moved.Value.Y);
        }
        finally
        {
            X11Cursor.TryWarp(start.Value.X, start.Value.Y);
        }
    }

    [Fact]
    public void Empty_input_shape_lets_the_pointer_fall_through()
    {
        if (!X11Cursor.IsAvailable) return;

        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return;

        var start = X11Cursor.TryGet();
        IntPtr back = IntPtr.Zero;
        IntPtr front = IntPtr.Zero;
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            back = CreateWindow(display, root, 80, 90, 40, 30, 0x0000FF);
            front = CreateWindow(display, root, 80, 90, 40, 30, 0x00FF00);
            XMapWindow(display, back);
            XMapWindow(display, front);
            XRaiseWindow(display, front);
            XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, 100, 100);
            XSync(display, 0);

            Assert.Equal(front, ChildAt(display, root, 100, 100));
            Assert.True(X11ClickThrough.TryEnable(front));
            Assert.Equal(0, X11ClickThrough.InputRectangleCount(front));
            Assert.Equal(back, ChildAt(display, root, 100, 100));
        }
        finally
        {
            if (front != IntPtr.Zero) XDestroyWindow(display, front);
            if (back != IntPtr.Zero) XDestroyWindow(display, back);
            XCloseDisplay(display);
            if (start != null) X11Cursor.TryWarp(start.Value.X, start.Value.Y);
        }
    }

    static IntPtr ChildAt(IntPtr display, IntPtr root, int x, int y)
    {
        XQueryPointer(display, root, out _, out IntPtr child, out _, out _, out _, out _, out _);
        if (child == IntPtr.Zero) return IntPtr.Zero;
        // Walk down so a parent that merely contains the point is not the answer.
        while (child != IntPtr.Zero)
        {
            if (XQueryPointer(display, child, out _, out IntPtr nested, out _, out _, out _, out _, out _) == 0)
                break;
            if (nested == IntPtr.Zero) return child;
            child = nested;
        }
        return child;
    }

    static IntPtr CreateWindow(IntPtr display, IntPtr root, int x, int y, uint width, uint height, ulong pixel)
    {
        IntPtr window = XCreateSimpleWindow(display, root, x, y, width, height, 0, 0, pixel);
        var attrs = new XSetWindowAttributes { override_redirect = 1 };
        XChangeWindowAttributes(display, window, CwOverrideRedirect, ref attrs);
        return window;
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    struct XSetWindowAttributes
    {
        [FieldOffset(88)] public int override_redirect;
    }

    [DllImport(Lib)] static extern IntPtr XOpenDisplay(string name);
    [DllImport(Lib)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint border, ulong borderPixel, ulong background);
    [DllImport(Lib)] static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, ulong valueMask, ref XSetWindowAttributes attributes);
    [DllImport(Lib)] static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport(Lib)] static extern int XRaiseWindow(IntPtr display, IntPtr window);
    [DllImport(Lib)] static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport(Lib)] static extern int XWarpPointer(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);
    [DllImport(Lib)] static extern int XSync(IntPtr display, int discard);
    [DllImport(Lib)] static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
}
