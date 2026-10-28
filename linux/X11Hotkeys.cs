using System.Runtime.InteropServices;

namespace OpenSnip;

/// <summary>
/// Loads libX11 by its soname. The runtime package ships libX11.so.6; libX11.so
/// exists only in the development package.
/// </summary>
static class X11Native
{
    public static bool Available { get; private set; }

    public static XOpenDisplayFn XOpenDisplay;
    public static XCloseDisplayFn XCloseDisplay;
    public static XDefaultRootWindowFn XDefaultRootWindow;
    public static XGrabKeyFn XGrabKey;
    public static XUngrabKeyFn XUngrabKey;
    public static XGrabKeyboardFn XGrabKeyboard;
    public static XUngrabKeyboardFn XUngrabKeyboard;
    public static XPendingFn XPending;
    public static XNextEventFn XNextEvent;
    public static XFlushFn XFlush;
    public static XSyncFn XSync;
    public static XConnectionNumberFn XConnectionNumber;
    public static XKeysymToKeycodeFn XKeysymToKeycode;
    public static XkbKeycodeToKeysymFn XkbKeycodeToKeysym;
    public static XGetModifierMappingFn XGetModifierMapping;
    public static XFreeModifiermapFn XFreeModifiermap;
    public static XSetErrorHandlerFn XSetErrorHandler;
    public static XkbSetDetectableAutoRepeatFn XkbSetDetectableAutoRepeat;
    static XInitThreadsFn _initThreads;

    static X11Native()
    {
        try { Load(); }
        catch { Available = false; }
    }

    /// <summary>Must run before Avalonia touches Xlib, so a second thread can use its own Display.</summary>
    public static void InitThreads()
    {
        try { _initThreads?.Invoke(); }
        catch { }
    }

    static void Load()
    {
        IntPtr lib = IntPtr.Zero;
        foreach (var name in new[] { "libX11.so.6", "libX11.so", "libX11" })
        {
            if (NativeLibrary.TryLoad(name, out lib)) break;
        }
        if (lib == IntPtr.Zero) return;

        XOpenDisplay = Req<XOpenDisplayFn>(lib, "XOpenDisplay");
        XCloseDisplay = Req<XCloseDisplayFn>(lib, "XCloseDisplay");
        XDefaultRootWindow = Req<XDefaultRootWindowFn>(lib, "XDefaultRootWindow");
        XGrabKey = Req<XGrabKeyFn>(lib, "XGrabKey");
        XUngrabKey = Req<XUngrabKeyFn>(lib, "XUngrabKey");
        XGrabKeyboard = Req<XGrabKeyboardFn>(lib, "XGrabKeyboard");
        XUngrabKeyboard = Req<XUngrabKeyboardFn>(lib, "XUngrabKeyboard");
        XPending = Req<XPendingFn>(lib, "XPending");
        XNextEvent = Req<XNextEventFn>(lib, "XNextEvent");
        XFlush = Req<XFlushFn>(lib, "XFlush");
        XSync = Req<XSyncFn>(lib, "XSync");
        XConnectionNumber = Req<XConnectionNumberFn>(lib, "XConnectionNumber");
        XKeysymToKeycode = Req<XKeysymToKeycodeFn>(lib, "XKeysymToKeycode");
        XkbKeycodeToKeysym = Req<XkbKeycodeToKeysymFn>(lib, "XkbKeycodeToKeysym");
        XGetModifierMapping = Req<XGetModifierMappingFn>(lib, "XGetModifierMapping");
        XFreeModifiermap = Req<XFreeModifiermapFn>(lib, "XFreeModifiermap");
        XSetErrorHandler = Req<XSetErrorHandlerFn>(lib, "XSetErrorHandler");
        try { XkbSetDetectableAutoRepeat = Opt<XkbSetDetectableAutoRepeatFn>(lib, "XkbSetDetectableAutoRepeat"); }
        catch { XkbSetDetectableAutoRepeat = null; }
        try { _initThreads = Opt<XInitThreadsFn>(lib, "XInitThreads"); }
        catch { _initThreads = null; }
        Available = true;
    }

    static T Req<T>(IntPtr lib, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));

    static T Opt<T>(IntPtr lib, string name) where T : Delegate => Req<T>(lib, name);

    public delegate IntPtr XOpenDisplayFn(string name);
    public delegate int XCloseDisplayFn(IntPtr display);
    public delegate ulong XDefaultRootWindowFn(IntPtr display);
    public delegate int XGrabKeyFn(IntPtr display, int keycode, uint modifiers, ulong grabWindow, int ownerEvents, int pointerMode, int keyboardMode);
    public delegate int XUngrabKeyFn(IntPtr display, int keycode, uint modifiers, ulong grabWindow);
    public delegate int XGrabKeyboardFn(IntPtr display, ulong grabWindow, int ownerEvents, int pointerMode, int keyboardMode, ulong time);
    public delegate int XUngrabKeyboardFn(IntPtr display, ulong time);
    public delegate int XPendingFn(IntPtr display);
    public delegate int XNextEventFn(IntPtr display, IntPtr eventReturn);
    public delegate int XFlushFn(IntPtr display);
    public delegate int XSyncFn(IntPtr display, int discard);
    public delegate int XConnectionNumberFn(IntPtr display);
    public delegate int XKeysymToKeycodeFn(IntPtr display, ulong keysym);
    public delegate ulong XkbKeycodeToKeysymFn(IntPtr display, int keycode, int group, int level);
    public delegate IntPtr XGetModifierMappingFn(IntPtr display);
    public delegate int XFreeModifiermapFn(IntPtr map);
    public delegate IntPtr XSetErrorHandlerFn(IntPtr handler);
    public delegate int XkbSetDetectableAutoRepeatFn(IntPtr display, int detectable, IntPtr supported);
    delegate int XInitThreadsFn();
}

/// <summary>
/// Global shortcuts on X11. Wayland compositors do not deliver these events.
/// The connection is private to one thread; Avalonia keeps its own.
/// </summary>
sealed class X11HotkeyService : IDisposable
{
    const int KeyPress = 2;
    const int KeyRelease = 3;
    const int GrabModeAsync = 1;
    const int EventBytes = 192;
    const int PollIn = 1;
    const int ONonBlock = 0x800;
    const int OCloExec = 0x80000;

    static X11HotkeyService _active;
    static XErrorFn _errorHandler;
    static IntPtr _previousHandler;

    readonly Action<HotkeyCommand> _onCommand;
    readonly Action<string> _onConflict;
    string _lastConflict = "";
    readonly object _gate = new();
    readonly Queue<Action> _commands = new();
    readonly HashSet<int> _down = new();

    Thread _thread;
    IntPtr _display;
    IntPtr _probeDisplay;
    IntPtr _event;
    int _wakeRead = -1;
    int _wakeWrite = -1;
    int _errorCount;
    int _disposed;
    uint _altMask = 8;
    uint _superMask = 64;
    ulong _root;
    volatile bool _running;
    bool _keyboardGrabbed;
    bool _listening;
    DateTime _listenDeadline;
    HotkeyBindings _bindings;
    Action<HotkeyChord?> _listenCallback;

    X11HotkeyService(HotkeyBindings bindings, Action<HotkeyCommand> onCommand, Action<string> onConflict)
    {
        _onConflict = onConflict;
        _bindings = bindings ?? new HotkeyBindings();
        _onCommand = onCommand ?? (_ => { });
    }

    public static bool IsSupported
    {
        get
        {
            if (PlatformInfo.IsWayland || !PlatformInfo.HasDisplay || !X11Native.Available) return false;
            // A stale DISPLAY still looks present. Open and close before reporting success.
            // Callers use this before the shortcut thread exists, on their own thread.
            X11Native.InitThreads();
            var display = X11Native.XOpenDisplay(null);
            if (display == IntPtr.Zero) return false;
            X11Native.XCloseDisplay(display);
            return true;
        }
    }

    public static X11HotkeyService TryStart(HotkeyBindings bindings, Action<HotkeyCommand> onCommand, Action<string> onConflict = null)
    {
        if (!IsSupported) return null;
        var service = new X11HotkeyService(bindings, onCommand, onConflict);
        if (service.Start()) return service;
        service.Dispose();
        return null;
    }

    public void Update(HotkeyBindings bindings)
    {
        var next = bindings ?? new HotkeyBindings();
        Post(() =>
        {
            _bindings = next;
            ApplyBindings();
        });
    }

    /// <summary>Takes the keyboard until <see cref="EndListen"/>, Escape is handled by the caller, or the timeout.</summary>
    public bool BeginListen(Action<HotkeyChord?> onChord)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                ReleaseKeyboard();
                _listenCallback = onChord;
                _listening = true;
                _down.Clear();
                _listenDeadline = DateTime.UtcNow.AddSeconds(20);
                int result = X11Native.XGrabKeyboard(_display, _root, 0, GrabModeAsync, GrabModeAsync, 0);
                X11Native.XFlush(_display);
                _keyboardGrabbed = result == 0;
                if (!_keyboardGrabbed)
                {
                    _listening = false;
                    _listenCallback = null;
                }
                done.TrySetResult(_keyboardGrabbed);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _listening = false;
                _listenCallback = null;
                done.TrySetResult(false);
            }
        });
        return done.Task.Wait(TimeSpan.FromSeconds(2)) && done.Task.Result;
    }

    public void EndListen() => Post(() => CancelListen(invoke: false));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _running = false;
        Wake();
        if (_thread != null && Thread.CurrentThread != _thread)
            _thread.Join(1000);
    }

    internal int KeycodeFor(string key)
    {
        int code = 0;
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => done.TrySetResult(LookupKeycode(key)));
        if (done.Task.Wait(TimeSpan.FromSeconds(2))) code = done.Task.Result;
        return code;
    }

    internal uint MaskFor(HotkeyChord chord) => HotkeyGrabs.ModifierMask(chord, _altMask, _superMask);

    internal bool ForeignGrabSucceeds(int keycode, uint modifiers)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => done.TrySetResult(ProbeGrab(keycode, modifiers)));
        return done.Task.Wait(TimeSpan.FromSeconds(2)) && done.Task.Result;
    }

    /// <summary>True when this process can grab the combination. Call only while no service is running.</summary>
    internal static bool ProbeGrabSucceeds(int keycode, uint modifiers)
    {
        if (!X11Native.Available || _active != null) return false;
        var display = X11Native.XOpenDisplay(null);
        if (display == IntPtr.Zero) return false;
        try
        {
            _probeErrors = 0;
            _probeDisplayStatic = display;
            InstallHandler();
            int before = _probeErrors;
            var root = X11Native.XDefaultRootWindow(display);
            X11Native.XGrabKey(display, keycode, modifiers, root, 0, GrabModeAsync, GrabModeAsync);
            X11Native.XSync(display, 0);
            bool ok = _probeErrors == before;
            if (ok)
            {
                X11Native.XUngrabKey(display, keycode, modifiers, root);
                X11Native.XFlush(display);
            }
            return ok;
        }
        finally
        {
            X11Native.XCloseDisplay(display);
            _probeDisplayStatic = IntPtr.Zero;
            if (_active == null) RestoreHandler();
        }
    }

    static int _probeErrors;
    static IntPtr _probeDisplayStatic;

    bool Start()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _running = true;
        _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "OpenSnip shortcuts" };
        _thread.Start();
        if (!ready.Task.Wait(TimeSpan.FromSeconds(2))) return false;
        return ready.Task.Result;
    }

    void Loop(TaskCompletionSource<bool> ready)
    {
        try
        {
            if (!OpenDisplay())
            {
                ready.TrySetResult(false);
                return;
            }
            ApplyBindings();
            ready.TrySetResult(true);
            while (_running)
            {
                while (_running && X11Native.XPending(_display) > 0)
                    DispatchEvent();
                if (!_running) break;
                Poll();
                DrainWake();
                DrainCommands();
                if (_listening && DateTime.UtcNow >= _listenDeadline)
                    CancelListen(invoke: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ready.TrySetResult(false);
        }
        finally
        {
            CloseDisplay();
        }
    }

    bool OpenDisplay()
    {
        _display = X11Native.XOpenDisplay(null);
        if (_display == IntPtr.Zero) return false;
        _event = Marshal.AllocHGlobal(EventBytes);
        _root = X11Native.XDefaultRootWindow(_display);
        // Claim the slot first so CloseDisplay restores Avalonia's handler if setup throws.
        _active = this;
        InstallHandler();
        OpenWakePipe();
        try { X11Native.XkbSetDetectableAutoRepeat?.Invoke(_display, 1, IntPtr.Zero); }
        catch { }
        ReadModifierMasks();
        return true;
    }

    void CloseDisplay()
    {
        try
        {
            if (_display != IntPtr.Zero)
            {
                ReleaseKeyboard();
                X11Native.XUngrabKey(_display, 0, HotkeyGrabs.AnyModifier, _root);
                X11Native.XFlush(_display);
                X11Native.XCloseDisplay(_display);
            }
        }
        catch { }
        _display = IntPtr.Zero;
        if (_event != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_event);
            _event = IntPtr.Zero;
        }
        CloseWakePipe();
        if (ReferenceEquals(_active, this))
        {
            _active = null;
            RestoreHandler();
        }
    }

    void ApplyBindings()
    {
        if (_display == IntPtr.Zero) return;
        // A release can be lost across an ungrab. Forget held keys so the next press still fires.
        _down.Clear();
        X11Native.XUngrabKey(_display, 0, HotkeyGrabs.AnyModifier, _root);
        X11Native.XSync(_display, 0);
        var grabs = HotkeyGrabs.Plan(_bindings, LookupKeycode, _altMask, _superMask);
        var conflicts = new HashSet<HotkeyCommand>();
        foreach (var grab in grabs)
        {
            int before = _errorCount;
            X11Native.XGrabKey(_display, grab.Keycode, grab.Modifiers, _root, 0, GrabModeAsync, GrabModeAsync);
            X11Native.XSync(_display, 0);
            if (_errorCount != before)
            {
                conflicts.Add(grab.Command);
            }
        }
        string conflict = string.Join(", ", conflicts.OrderBy(command => command).Select(command => command switch
        {
            HotkeyCommand.Snip => "Snip",
            HotkeyCommand.SnipDelay => "Snip after delay",
            HotkeyCommand.Repeat => "Repeat last region",
            HotkeyCommand.Record => "Start / stop recording",
            _ => command.ToString(),
        }));
        if (conflict != _lastConflict && conflict.Length > 0)
            _onConflict?.Invoke($"Shortcut unavailable: {conflict}. Change it in Settings → Shortcuts or release it in your desktop settings.");
        _lastConflict = conflict;
    }

    int LookupKeycode(string key)
    {
        if (_display == IntPtr.Zero || string.IsNullOrEmpty(key)) return 0;
        foreach (var sym in HotkeyChord.Keysyms(key))
        {
            int code = X11Native.XKeysymToKeycode(_display, sym);
            if (code != 0) return code;
        }
        return 0;
    }

    void ReadModifierMasks()
    {
        var map = X11Native.XGetModifierMapping(_display);
        if (map == IntPtr.Zero) return;
        try
        {
            int max = Marshal.ReadInt32(map);
            var keys = Marshal.ReadIntPtr(map, IntPtr.Size);
            if (max <= 0 || keys == IntPtr.Zero) return;
            uint alt = 0, super = 0;
            for (int mod = 0; mod < 8; mod++)
            {
                for (int i = 0; i < max; i++)
                {
                    int code = Marshal.ReadByte(keys, mod * max + i);
                    if (code == 0) continue;
                    ulong sym = X11Native.XkbKeycodeToKeysym(_display, code, 0, 0);
                    if (sym is 0xffe9 or 0xffea or 0xffe7 or 0xffe8) alt |= 1u << mod;
                    if (sym is 0xffeb or 0xffec) super |= 1u << mod;
                }
            }
            if (alt != 0) _altMask = alt;
            if (super != 0) _superMask = super;
        }
        finally
        {
            X11Native.XFreeModifiermap(map);
        }
    }

    void DispatchEvent()
    {
        X11Native.XNextEvent(_display, _event);
        int type = Marshal.ReadInt32(_event);
        if (type != KeyPress && type != KeyRelease) return;
        int keycode = Marshal.ReadInt32(_event, 84);
        uint state = (uint)Marshal.ReadInt32(_event, 80);
        if (keycode == 0) return;

        if (type == KeyRelease)
        {
            _down.Remove(keycode);
            return;
        }
        if (!_down.Add(keycode)) return;

        var chord = ChordFrom(state, keycode);
        if (chord == null) return;
        if (_listening)
        {
            DeliverListen(chord.Value);
            return;
        }

        var command = HotkeyBindings.Match(chord.Value, _bindings);
        if (command == null) return;
        try { _onCommand(command.Value); }
        catch (Exception ex) { Log.Error(ex); }
    }

    void DeliverListen(HotkeyChord chord)
    {
        // Stay grabbed so a rejected key (a bare letter) can be followed by another try.
        // The caller ends the grab on accept, cancel, or close.
        var callback = _listenCallback;
        if (callback == null) return;
        try { callback(chord); }
        catch (Exception ex) { Log.Error(ex); }
    }

    HotkeyChord? ChordFrom(uint state, int keycode)
    {
        string key = KeyFromCode(keycode, 0) ?? KeyFromCode(keycode, 1);
        if (key == null) return null;
        return new HotkeyChord(
            key,
            (_superMask != 0) && (state & _superMask) != 0,
            (state & HotkeyGrabs.ControlMask) != 0,
            (_altMask != 0) && (state & _altMask) != 0,
            (state & HotkeyGrabs.ShiftMask) != 0);
    }

    string KeyFromCode(int keycode, int level)
    {
        ulong sym = X11Native.XkbKeycodeToKeysym(_display, keycode, 0, level);
        return HotkeyChord.FromKeysym(sym);
    }

    void CancelListen(bool invoke)
    {
        bool was = _listening || _keyboardGrabbed;
        _listening = false;
        ReleaseKeyboard();
        var callback = _listenCallback;
        _listenCallback = null;
        if (!invoke || !was || callback == null) return;
        try { callback(null); }
        catch (Exception ex) { Log.Error(ex); }
    }

    void ReleaseKeyboard()
    {
        if (!_keyboardGrabbed || _display == IntPtr.Zero) return;
        _keyboardGrabbed = false;
        _down.Clear();
        try
        {
            X11Native.XUngrabKeyboard(_display, 0);
            X11Native.XFlush(_display);
        }
        catch { }
    }

    bool ProbeGrab(int keycode, uint modifiers)
    {
        var other = X11Native.XOpenDisplay(null);
        if (other == IntPtr.Zero) return false;
        try
        {
            _probeDisplay = other;
            int before = _errorCount;
            var root = X11Native.XDefaultRootWindow(other);
            X11Native.XGrabKey(other, keycode, modifiers, root, 0, GrabModeAsync, GrabModeAsync);
            X11Native.XSync(other, 0);
            bool ok = _errorCount == before;
            if (ok)
            {
                X11Native.XUngrabKey(other, keycode, modifiers, root);
                X11Native.XFlush(other);
            }
            return ok;
        }
        finally
        {
            _probeDisplay = IntPtr.Zero;
            X11Native.XCloseDisplay(other);
        }
    }

    void Post(Action command)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_gate) _commands.Enqueue(command);
        Wake();
    }

    void DrainCommands()
    {
        while (true)
        {
            Action command;
            lock (_gate)
            {
                if (_commands.Count == 0) return;
                command = _commands.Dequeue();
            }
            try { command(); }
            catch (Exception ex) { Log.Error(ex); }
        }
    }

    void Poll()
    {
        int timeout = _wakeRead >= 0 ? -1 : 50;
        if (_listening)
        {
            int remaining = (int)Math.Max(1, (_listenDeadline - DateTime.UtcNow).TotalMilliseconds);
            timeout = timeout < 0 ? remaining : Math.Min(timeout, remaining);
        }

        var fds = new PollFd[2];
        fds[0].fd = X11Native.XConnectionNumber(_display);
        fds[0].events = PollIn;
        uint count = 1;
        if (_wakeRead >= 0)
        {
            fds[1].fd = _wakeRead;
            fds[1].events = PollIn;
            count = 2;
        }
        int rc = poll(fds, count, timeout);
        if (rc < 0 && Marshal.GetLastPInvokeError() != 4) Thread.Sleep(5); // EINTR retries via the outer loop
    }

    void OpenWakePipe()
    {
        var fds = new int[2];
        if (pipe2(fds, ONonBlock | OCloExec) != 0) return;
        _wakeRead = fds[0];
        _wakeWrite = fds[1];
    }

    void CloseWakePipe()
    {
        if (_wakeRead >= 0) close(_wakeRead);
        if (_wakeWrite >= 0) close(_wakeWrite);
        _wakeRead = -1;
        _wakeWrite = -1;
    }

    void Wake()
    {
        if (_wakeWrite < 0) return;
        var one = new byte[] { 1 };
        write(_wakeWrite, one, 1);
    }

    void DrainWake()
    {
        if (_wakeRead < 0) return;
        var buf = new byte[32];
        while (read(_wakeRead, buf, (nuint)buf.Length) > 0) { }
    }

    static void InstallHandler()
    {
        if (_errorHandler != null) return;
        _errorHandler = OnError;
        // Keep the previous handler as a raw pointer. Avalonia's handler is a
        // different delegate type, and wrapping that pointer throws.
        _previousHandler = X11Native.XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(_errorHandler));
    }

    static void RestoreHandler()
    {
        if (_errorHandler == null) return;
        X11Native.XSetErrorHandler(_previousHandler);
        _errorHandler = null;
        _previousHandler = IntPtr.Zero;
    }

    static unsafe int OnError(IntPtr display, IntPtr error)
    {
        var active = _active;
        if (active != null && (display == active._display || display == active._probeDisplay))
        {
            Interlocked.Increment(ref active._errorCount);
            return 0;
        }
        if (display != IntPtr.Zero && display == _probeDisplayStatic)
        {
            Interlocked.Increment(ref _probeErrors);
            return 0;
        }
        if (_previousHandler != IntPtr.Zero)
        {
            var previous = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)_previousHandler;
            return previous(display, error);
        }
        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int XErrorFn(IntPtr display, IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc", SetLastError = true)]
    static extern int poll(PollFd[] fds, uint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    static extern int pipe2(int[] pipefd, int flags);

    [DllImport("libc", SetLastError = true)]
    static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    static extern long read(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    static extern long write(int fd, byte[] buffer, nuint count);
}
