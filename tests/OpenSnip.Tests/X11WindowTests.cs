using System.Runtime.InteropServices;

namespace OpenSnip.Tests;

public class X11WindowTests
{
    const string Lib = "libX11.so.6";
    const ulong CwOverrideRedirect = 512;

    [Fact]
    public async Task Snapshot_matches_override_redirect_windows_and_the_capture()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))) return;

        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return;

        IntPtr back = IntPtr.Zero;
        IntPtr front = IntPtr.Zero;
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            back = CreateWindow(display, root, 137, 241, 173, 119, 0x0000FF);
            front = CreateWindow(display, root, 160, 260, 80, 50, 0x00FF00);
            XMapWindow(display, back);
            XMapWindow(display, front);
            XRaiseWindow(display, front);
            XSync(display, 0);

            var snap = X11Windows.TrySnapshot();
            Assert.NotNull(snap);
            Assert.Contains(new Avalonia.PixelRect(137, 241, 173, 119), snap.Windows);
            Assert.Contains(new Avalonia.PixelRect(160, 260, 80, 50), snap.Windows);

            var frontHit = WindowHit.At(snap.Windows, 170, 270, snap.Width, snap.Height);
            var backHit = WindowHit.At(snap.Windows, 142, 246, snap.Width, snap.Height);
            Assert.Equal(new Avalonia.PixelRect(160, 260, 80, 50), frontHit);
            Assert.Equal(new Avalonia.PixelRect(137, 241, 173, 119), backHit);

            var frame = await new X11CaptureBackend().CaptureAsync();
            Assert.Equal(snap.Width, frame.Width);
            Assert.Equal(snap.Height, frame.Height);
            AssertPixel(frame, 170, 270, 0, 255, 0);
            AssertPixel(frame, 142, 246, 0, 0, 255);
        }
        finally
        {
            if (front != IntPtr.Zero) XDestroyWindow(display, front);
            if (back != IntPtr.Zero) XDestroyWindow(display, back);
            XCloseDisplay(display);
        }
    }

    static void AssertPixel(CapturedFrame frame, int x, int y, byte r, byte g, byte b)
    {
        Assert.True(frame.TryGetRgb(x, y, out byte ar, out byte ag, out byte ab), $"No pixel at {x},{y}");
        Assert.True(ar == r && ag == g && ab == b, $"Pixel {x},{y} was #{ar:X2}{ag:X2}{ab:X2}, expected #{r:X2}{g:X2}{b:X2}");
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
    [DllImport(Lib)] static extern int XSync(IntPtr display, int discard);
}
