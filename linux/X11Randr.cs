using System.Runtime.InteropServices;
using Avalonia;

namespace OpenSnip;

/// <summary>
/// Active XRandR monitors. The layout is the LP64 <c>XRRMonitorInfo</c> from libXrandr 1.5
/// (56 bytes): Atom name, two Bools, then x, y, width, height at offsets 20, 24, 28 and 32.
/// </summary>
static class X11Randr
{
    const int InfoSize = 56;
    const int OffX = 20;
    const int OffY = 24;
    const int OffWidth = 28;
    const int OffHeight = 32;

    static readonly object Gate = new();
    static IntPtr _lib;
    static bool _tried;

    public static List<PixelRect> Query(IntPtr display, IntPtr root)
    {
        if (display == IntPtr.Zero || root == IntPtr.Zero || IntPtr.Size != 8) return null;
        var lib = Library();
        if (lib == IntPtr.Zero) return null;
        try
        {
            var get = Marshal.GetDelegateForFunctionPointer<GetMonitors>(NativeLibrary.GetExport(lib, "XRRGetMonitors"));
            var free = Marshal.GetDelegateForFunctionPointer<FreeMonitors>(NativeLibrary.GetExport(lib, "XRRFreeMonitors"));
            IntPtr data = get(display, root, 1, out int count);
            if (data == IntPtr.Zero || count <= 0) return null;
            try
            {
                var list = new List<PixelRect>(count);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(data, i * InfoSize);
                    int x = Marshal.ReadInt32(item, OffX);
                    int y = Marshal.ReadInt32(item, OffY);
                    int width = Marshal.ReadInt32(item, OffWidth);
                    int height = Marshal.ReadInt32(item, OffHeight);
                    if (width <= 0 || height <= 0) continue;
                    list.Add(new PixelRect(x, y, width, height));
                }
                return list.Count == 0 ? null : list;
            }
            finally
            {
                free(data);
            }
        }
        catch (EntryPointNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
    }

    public static List<PixelRect> TryMonitors()
    {
        using var pointer = X11Pointer.TryOpen();
        if (pointer == null) return null;
        return Query(pointer.Display, pointer.Root);
    }

    static IntPtr Library()
    {
        lock (Gate)
        {
            if (_tried) return _lib;
            _tried = true;
            foreach (var name in new[] { "libXrandr.so.2", "libXrandr.so", "libXrandr" })
            {
                if (NativeLibrary.TryLoad(name, out var handle))
                {
                    _lib = handle;
                    return _lib;
                }
            }
            return IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr GetMonitors(IntPtr display, IntPtr window, int getActive, out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FreeMonitors(IntPtr monitors);
}
