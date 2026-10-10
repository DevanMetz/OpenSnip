using System.Runtime.InteropServices;

namespace OpenSnip;

/// <summary>
/// Low-level keyboard hook. Runs on its own thread with its own message loop so a busy
/// UI thread can never stall system-wide keyboard input (or get the hook silently removed).
/// </summary>
sealed class KeyboardHook
{
    /// <summary>Called on the hook thread for every key event. Return true to swallow the key.</summary>
    public delegate bool KeyHandler(Keys key, bool down);

    readonly KeyHandler handler;
    Native.LowLevelKeyboardProc proc; // held in a field so the GC can't collect the delegate
    IntPtr hook;

    public bool Installed => hook != IntPtr.Zero;

    KeyboardHook(KeyHandler handler) => this.handler = handler;

    public static KeyboardHook Start(KeyHandler handler, Action<bool> onInstalled)
    {
        var kh = new KeyboardHook(handler);
        var thread = new Thread(() =>
        {
            kh.proc = kh.Callback;
            kh.hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kh.proc, Native.GetModuleHandle(null), 0);
            onInstalled(kh.Installed);
            if (kh.Installed) Application.Run();
        })
        { IsBackground = true, Name = "OpenSnip keyboard hook" };
        thread.Start();
        return kh;
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            try
            {
                if (handler((Keys)k.vkCode, down)) return (IntPtr)1;
            }
            catch (Exception ex) { Log.Error(ex); }
        }
        return Native.CallNextHookEx(hook, nCode, wParam, lParam);
    }
}
