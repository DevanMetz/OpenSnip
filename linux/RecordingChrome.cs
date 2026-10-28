using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenSnip;

/// <summary>
/// Red outline around the recorded area and the pause / mic / stop / discard bar.
/// Both are real windows, and their ids are handed to the grabber so the pixels
/// underneath them are what gets written to the file.
/// </summary>
sealed class RecordingChrome
{
    readonly RecordingSession _session;
    readonly Window _bar;
    readonly StackPanel _controls;
    readonly StackPanel _countdownPanel;
    readonly TextBlock _countdownText;
    readonly TextBlock _dot;
    readonly TextBlock _time;
    readonly Button _pause;
    readonly Button _mic;
    readonly Border _timeHost;
    readonly DispatcherTimer _tick;
    DispatcherTimer _count;
    readonly List<IntPtr> _outlines = new();
    IntPtr _display;
    long[] _ids = Array.Empty<long>();
    int _countdown;
    Action _after;
    bool _countdownArmed;
    bool _drag;
    PixelPoint _dragScreen;
    PixelPoint _dragOrigin;
    bool _closed;

    public RecordingChrome(RecordingSession session)
    {
        _session = session;
        _dot = new TextBlock
        {
            Text = "●",
            FontSize = 11,
            Foreground = AppTheme.Record,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _time = new TextBlock
        {
            Text = "00:00",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            MinWidth = 52,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var timeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        timeRow.Children.Add(_dot);
        timeRow.Children.Add(_time);
        _timeHost = new Border
        {
            Padding = new Thickness(12, 6, 10, 6),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = timeRow,
        };
        _timeHost.PointerPressed += OnDragPressed;
        _timeHost.PointerMoved += OnDragMoved;
        _timeHost.PointerReleased += OnDragReleased;

        _pause = BarButton("Pause", (_, _) => _session.TogglePause());
        _mic = _session.HasMic ? BarButton("Mic on", (_, _) => _session.ToggleMic()) : null;
        if (_mic != null) _mic.Foreground = AppTheme.Good;
        var stop = BarButton("Stop", (_, _) => _session.Stop());
        stop.Foreground = AppTheme.Record;
        var discard = BarButton("✕", async (_, _) => await ConfirmDiscard());
        discard.Foreground = AppTheme.Muted;

        _controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _controls.Children.Add(_timeHost);
        _controls.Children.Add(_pause);
        if (_mic != null) _controls.Children.Add(_mic);
        _controls.Children.Add(stop);
        _controls.Children.Add(discard);

        _countdownText = new TextBlock
        {
            Text = "Recording starts in 3…",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0),
        };
        var cancel = BarButton("Cancel", (_, _) => _session.Discard());
        _countdownPanel = new StackPanel { Orientation = Orientation.Horizontal, IsVisible = false };
        _countdownPanel.Children.Add(_countdownText);
        _countdownPanel.Children.Add(cancel);

        var stack = new StackPanel();
        stack.Children.Add(_controls);
        stack.Children.Add(_countdownPanel);

        _bar = new Window
        {
            Title = "OpenSnip",
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = AppTheme.Back,
            Content = new Border
            {
                Background = AppTheme.Back,
                BorderBrush = AppTheme.Border,
                BorderThickness = new Thickness(1),
                Child = stack,
            },
        };
        _bar.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;
            Place();
            RefreshIds();
        }, DispatcherPriority.Loaded);
        _bar.Closed += (_, _) => _closed = true;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) =>
        {
            if (_closed) return;
            _session.Poll();
            if (_closed) return;
            UpdateTime();
        };
    }

    public void StartCountdown(int seconds, Action then)
    {
        _countdown = Math.Max(1, seconds);
        _after = then;
        _countdownArmed = true;
        _countdownText.Text = $"Recording starts in {_countdown}…";
        _controls.IsVisible = false;
        _countdownPanel.IsVisible = true;
        if (_bar.IsVisible) StartCountTimer();
    }

    public void Show()
    {
        CreateOutlines();
        var work = X11Monitors.WorkArea(_session.Area);
        _bar.Position = RecordGeometry.PlaceBar(_session.Area, work, 360, 48, 10).Position;
        _bar.Show();
        if (_countdownArmed) StartCountTimer();
        _tick.Start();
        RefreshIds();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _tick.Stop();
        _count?.Stop();
        DestroyOutlines();
        try
        {
            if (_bar.IsVisible) _bar.Close();
        }
        catch { }
    }

    public void Refresh()
    {
        if (_closed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }
        _pause.Content = _session.Paused ? "Resume" : "Pause";
        if (_mic != null)
        {
            _mic.Content = _session.MicOn ? "Mic on" : "Mic off";
            _mic.Foreground = _session.MicOn ? AppTheme.Good : AppTheme.Muted;
        }
        UpdateTime();
    }

    /// <summary>Ids captured on the UI thread. The grabber reads this from its own thread.</summary>
    public long[] WindowIds() => Volatile.Read(ref _ids);

    void StartCountTimer()
    {
        if (_count != null) return;
        _count = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _count.Tick += (_, _) =>
        {
            if (_closed) return;
            _countdown--;
            if (_countdown > 0)
            {
                _countdownText.Text = $"Recording starts in {_countdown}…";
                return;
            }
            _count.Stop();
            _countdownArmed = false;
            _countdownPanel.IsVisible = false;
            _controls.IsVisible = true;
            Place();
            RefreshIds();
            var go = _after;
            _after = null;
            go?.Invoke();
        };
        _count.Start();
    }

    void UpdateTime()
    {
        var elapsed = _session.Elapsed;
        _time.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
        bool on = _session.Paused || DateTime.Now.Millisecond < 600;
        _dot.Foreground = on ? (_session.Paused ? AppTheme.Muted : AppTheme.Record) : Brushes.Transparent;
    }

    async Task ConfirmDiscard()
    {
        if (_closed) return;
        bool wasPaused = _session.Paused;
        if (!wasPaused) _session.TogglePause();
        if (_closed) return;
        bool yes = false;
        try
        {
            yes = await MessageDialog.ConfirmAsync(_bar, "Throw away this recording?", "Throw away");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        if (_closed) return;
        if (yes) _session.Discard();
        else if (!wasPaused) _session.TogglePause();
    }

    void Place()
    {
        if (_closed) return;
        double scale = _bar.RenderScaling <= 0 ? 1 : _bar.RenderScaling;
        int width = Math.Max(1, (int)Math.Ceiling(_bar.Bounds.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(_bar.Bounds.Height * scale));
        if (width < 8 || height < 8) return;
        var spot = RecordGeometry.PlaceBar(_session.Area, X11Monitors.WorkArea(_session.Area), width, height, 10);
        _bar.Position = spot.Position;
    }

    void RefreshIds()
    {
        var ids = new List<long>(_outlines.Count + 1);
        foreach (var outline in _outlines)
            if (outline != IntPtr.Zero) ids.Add(outline.ToInt64());
        long bar = XidOf(_bar);
        if (bar != 0) ids.Add(bar);
        Volatile.Write(ref _ids, ids.ToArray());
    }

    static long XidOf(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return 0;
            return handle.Handle.ToInt64();
        }
        catch { return 0; }
    }

    void OnDragPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_timeHost).Properties.IsLeftButtonPressed) return;
        _drag = true;
        _dragScreen = _bar.PointToScreen(e.GetPosition(_bar));
        _dragOrigin = _bar.Position;
        e.Pointer.Capture(_timeHost);
        e.Handled = true;
    }

    void OnDragMoved(object sender, PointerEventArgs e)
    {
        if (!_drag) return;
        var now = _bar.PointToScreen(e.GetPosition(_bar));
        _bar.Position = new PixelPoint(
            _dragOrigin.X + (now.X - _dragScreen.X),
            _dragOrigin.Y + (now.Y - _dragScreen.Y));
        RefreshIds();
    }

    void OnDragReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        e.Pointer.Capture(null);
        RefreshIds();
        e.Handled = true;
    }

    static Button BarButton(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 4, 4, 4),
            MinWidth = 0,
        };
        button.Click += click;
        return button;
    }

    void CreateOutlines()
    {
        try
        {
            _display = XOpenDisplay(null);
            if (_display == IntPtr.Zero) return;
            XSetErrorHandler(IgnoreError);
            int screen = XDefaultScreen(_display);
            IntPtr root = XRootWindow(_display, screen);
            IntPtr pixel = AllocColor(_display, screen, 0xF0F0, 0x4444, 0x3838);
            foreach (var rect in RecordGeometry.Outline(_session.Area, 3))
            {
                var window = XCreateSimpleWindow(_display, root, rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height, 0, pixel, pixel);
                if (window == IntPtr.Zero) continue;
                SetOverrideRedirect(_display, window);
                try { XShapeCombineRectangles(_display, window, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, Unsorted); }
                catch (Exception ex) { Log.Error(ex); }
                XMapWindow(_display, window);
                _outlines.Add(window);
            }
            XFlush(_display);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    void DestroyOutlines()
    {
        if (_display == IntPtr.Zero) return;
        try
        {
            foreach (var window in _outlines)
                try { XDestroyWindow(_display, window); } catch { }
            XFlush(_display);
            XCloseDisplay(_display);
        }
        catch { }
        _outlines.Clear();
        _display = IntPtr.Zero;
    }

    static IntPtr AllocColor(IntPtr display, int screen, ushort red, ushort green, ushort blue)
    {
        var color = new XColor { Red = red, Green = green, Blue = blue, Flags = 7 };
        IntPtr colormap = XDefaultColormap(display, screen);
        if (colormap != IntPtr.Zero && XAllocColor(display, colormap, ref color) != 0)
            return (IntPtr)(long)color.Pixel;
        return (IntPtr)0x00F04438;
    }

    static void SetOverrideRedirect(IntPtr display, IntPtr window)
    {
        var bytes = new byte[112];
        bytes[88] = 1;
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { XChangeWindowAttributes(display, window, 512, pin.AddrOfPinnedObject()); }
        finally { pin.Free(); }
    }

    const int ShapeInput = 2;
    const int ShapeSet = 0;
    const int Unsorted = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct XColor
    {
        public nuint Pixel;
        public ushort Red;
        public ushort Green;
        public ushort Blue;
        public byte Flags;
        public byte Pad;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int XErrorHandler(IntPtr display, IntPtr error);

    static readonly XErrorHandler IgnoreError = (_, _) => 0;

    [DllImport("libX11")] static extern IntPtr XOpenDisplay(string name);
    [DllImport("libX11")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11")] static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport("libX11")] static extern int XDefaultScreen(IntPtr display);
    [DllImport("libX11")] static extern IntPtr XRootWindow(IntPtr display, int screen);
    [DllImport("libX11")] static extern IntPtr XDefaultColormap(IntPtr display, int screen);
    [DllImport("libX11")] static extern int XAllocColor(IntPtr display, IntPtr colormap, ref XColor color);
    [DllImport("libX11")] static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, IntPtr border, IntPtr background);
    [DllImport("libX11")] static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, ulong valueMask, IntPtr attributes);
    [DllImport("libX11")] static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport("libX11")] static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport("libX11")] static extern int XFlush(IntPtr display);
    [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int xOff, int yOff, IntPtr rectangles, int count, int operation, int ordering);
}
