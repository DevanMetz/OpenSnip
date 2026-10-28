using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenSnip;

/// <summary>
/// Loads libX11 for the capture backend. The runtime package ships libX11.so.6;
/// libX11.so exists only in the development package, and the default import
/// probe does not try the versioned name.
/// </summary>
static class X11Library
{
    public const string ImportName = "libX11";

    static readonly string[] Candidates = { "libX11.so.6", "libX11.so", "libX11" };

    [ModuleInitializer]
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(X11Library).Assembly, Resolve);
    }

    public static bool Available
    {
        get
        {
            try
            {
                foreach (var name in Candidates)
                    if (NativeLibrary.TryLoad(name, out _)) return true;
                return false;
            }
            catch { return false; }
        }
    }

    static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != ImportName) return IntPtr.Zero;
        // Default lookup, not the caller's search path: that path skips the system soname.
        foreach (var name in Candidates)
            if (NativeLibrary.TryLoad(name, out var handle))
                return handle;
        return IntPtr.Zero;
    }
}
