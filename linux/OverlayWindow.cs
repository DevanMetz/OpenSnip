using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenSnip;

static class OverlayWindow
{
    public static PixelRect ScreenRect(CapturedFrame frame, PixelRect image)
    {
        var map = MonitorMap.Create(OverlaySession.ReadLayouts(frame), frame.Width, frame.Height, frame.OriginKnown, frame.OriginX, frame.OriginY);
        if (map.OneToOne)
        {
            int x = frame.OriginKnown ? frame.OriginX : map.Desktop.X;
            int y = frame.OriginKnown ? frame.OriginY : map.Desktop.Y;
            return new PixelRect(image.X + x, image.Y + y, image.Width, image.Height);
        }
        return SnipGeometry.ToScreen(image, map.Desktop, frame.Width, frame.Height);
    }

    public static Task<PixelRect?> SelectAsync(CapturedFrame frame, Settings settings, PixelRect[] windows = null, bool record = false)
    {
        var session = new OverlaySession(frame, settings, windows, record);
        return session.Run();
    }
}

sealed class OverlaySession
{
    readonly Settings _settings;
    readonly CapturedFrame _frame;
    readonly PixelRect[] _targets;
    readonly bool _record;
    readonly MonitorMap _map;
    readonly SnipSession _snip;
    readonly X11Pointer _pointer;
    readonly WriteableBitmap _bitmap;
    readonly List<MonitorOverlayWindow> _windows = new();
    readonly TaskCompletionSource<PixelRect?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _done;

    public OverlaySession(CapturedFrame frame, Settings settings, PixelRect[] windows, bool record)
    {
        _settings = settings;
        _frame = frame;
        _targets = windows;
        _record = record;
        _pointer = X11Pointer.TryOpen();
        _map = MonitorMap.Create(ReadLayouts(frame), frame.Width, frame.Height, frame.OriginKnown, frame.OriginX, frame.OriginY);
        _snip = new SnipSession(_map);
        _bitmap = frame.ToBitmap();
    }

    public MonitorMap Map => _map;
    public SnipSession Snip => _snip;
    public Settings Settings => _settings;
    public WriteableBitmap Bitmap => _bitmap;
    public CapturedFrame Frame => _frame;
    public PixelRect? Hover(PixelPoint point)
    {
        if (!_map.TryToImage(new PixelRect(point.X, point.Y, 1, 1), out var image)) return null;
        var hit = WindowHit.At(_targets, image.X, image.Y, _frame.Width, _frame.Height);
        if (hit is not { } rect || !_map.OneToOne) return null;
        return new PixelRect(rect.X + point.X - image.X, rect.Y + point.Y - image.Y, rect.Width, rect.Height);
    }
    public SnipOutcome Release(PixelPoint point)
    {
        var hover = Hover(point);
        var outcome = _snip.Release(point);
        if (hover is { } window && outcome.HasRegion && outcome.Region == _map.MonitorRectAt(point))
            return SnipOutcome.Chosen(window);
        return outcome;
    }

    public Task<PixelRect?> Run()
    {
        for (int i = 0; i < _map.Monitors.Count; i++)
            _windows.Add(new MonitorOverlayWindow(this, i));

        int focus = 0;
        for (int i = 0; i < _map.Monitors.Count; i++)
        {
            if (_map.Monitors[i].Primary) focus = i;
        }

        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].ShowActivated = i == focus;
            _windows[i].Show();
        }
        _windows[focus].Activate();
        _windows[focus].FocusCanvas();
        return _result.Task;
    }

    public PixelPoint PointerDesktop(int index, Point local, Size size)
    {
        bool inside = local.X >= 0 && local.Y >= 0 && local.X < size.Width && local.Y < size.Height;
        if (!inside && _pointer != null)
        {
            var root = _pointer.Position();
            if (root is { } point) return point;
        }
        return _map.DesktopPoint(index, local.X, local.Y, size.Width, size.Height);
    }

    public void Apply(SnipOutcome outcome)
    {
        if (_done) return;
        if (!outcome.Close)
        {
            Invalidate();
            return;
        }
        if (outcome.Cancel || !outcome.HasRegion || !_map.TryToImage(outcome.Region, out var image))
            Finish(null);
        else
            Finish(_record ? outcome.Region : image);
    }

    public void WindowClosed() => Finish(null);

    void Invalidate()
    {
        foreach (var window in _windows) window.InvalidateCanvas();
    }

    void Finish(PixelRect? image)
    {
        if (_done) return;
        _done = true;
        _result.TrySetResult(image);
        foreach (var window in _windows.ToArray())
        {
            try { window.Close(); } catch { }
        }
        _pointer?.Dispose();
        var bitmap = _bitmap;
        Dispatcher.UIThread.Post(() =>
        {
            try { bitmap.Dispose(); } catch { }
        }, DispatcherPriority.Background);
    }

    public static List<MonitorLayout> ReadLayouts(CapturedFrame frame)
    {
        var layouts = new List<MonitorLayout>();
        Window probe = null;
        try
        {
            probe = new Window
            {
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Width = 1,
                Height = 1,
                Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            var screens = probe.Screens?.All;
            if (screens != null)
            {
                foreach (var screen in screens)
                {
                    if (screen == null || screen.Bounds.Width <= 0 || screen.Bounds.Height <= 0) continue;
                    layouts.Add(new MonitorLayout(screen.Bounds, screen.Scaling, screen.IsPrimary));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        finally
        {
            try { probe?.Close(); } catch { }
        }

        if (layouts.Count == 0)
        {
            layouts.Add(new MonitorLayout(
                new PixelRect(0, 0, Math.Max(1, frame.Width), Math.Max(1, frame.Height)),
                1,
                true));
        }
        return layouts;
    }
}

sealed class MonitorOverlayWindow : Window
{
    readonly OverlaySession _session;
    readonly MonitorLayout _layout;
    readonly CaptureCanvas _canvas;
    bool _fitting;

    public MonitorOverlayWindow(OverlaySession session, int index)
    {
        _session = session;
        _layout = session.Map.Monitors[index];
        Title = "OpenSnip";
        Icon = IconLoader.Icon;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        UseLayoutRounding = false;
        Position = new PixelPoint(_layout.Bounds.X, _layout.Bounds.Y);
        Width = Math.Max(1, _layout.Bounds.Width / _layout.ScaleOrOne);
        Height = Math.Max(1, _layout.Bounds.Height / _layout.ScaleOrOne);

        _canvas = new CaptureCanvas(session, index);
        Content = _canvas;
        Opened += (_, _) => Fit();
        ScalingChanged += (_, _) => Fit();
        Closed += (_, _) => session.WindowClosed();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    public void FocusCanvas()
    {
        Focus();
        _canvas.Focus();
    }

    public void InvalidateCanvas() => _canvas.InvalidateVisual();

    void OnKey(object sender, KeyEventArgs e)
    {
        SnipCommand? command = e.Key switch
        {
            Key.Escape => SnipCommand.Escape,
            Key.Enter => SnipCommand.Enter,
            Key.F => SnipCommand.Monitor,
            Key.A => SnipCommand.All,
            Key.Left => SnipCommand.Left,
            Key.Right => SnipCommand.Right,
            Key.Up => SnipCommand.Up,
            Key.Down => SnipCommand.Down,
            _ => null,
        };
        if (command == null) return;
        var outcome = _session.Snip.Command(command.Value, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        if (command is SnipCommand.Left or SnipCommand.Right or SnipCommand.Up or SnipCommand.Down && !PlatformInfo.IsWayland)
            X11Pointer.TryWarp(null, _session.Snip.Cursor.X, _session.Snip.Cursor.Y);
        _session.Apply(outcome);
        e.Handled = true;
    }

    void Fit()
    {
        if (_fitting) return;
        _fitting = true;
        try
        {
            double scale = RenderScaling;
            if (scale < 0.01 || double.IsNaN(scale) || double.IsInfinity(scale))
                scale = _layout.ScaleOrOne;
            double width = Math.Max(1, _layout.Bounds.Width / scale);
            double height = Math.Max(1, _layout.Bounds.Height / scale);
            if (double.IsNaN(Width) || Math.Abs(Width - width) > 0.5) Width = width;
            if (double.IsNaN(Height) || Math.Abs(Height - height) > 0.5) Height = height;
            var position = new PixelPoint(_layout.Bounds.X, _layout.Bounds.Y);
            if (Position != position) Position = position;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        finally
        {
            _fitting = false;
        }
    }
}

sealed class CaptureCanvas : Control
{
    readonly OverlaySession _session;
    readonly int _index;
    readonly Typeface _typeface = new("Inter");

    byte[] _loupePixels;
    byte[] _loupeScaled;
    WriteableBitmap _loupe;
    int _loupeCell;

    static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    static readonly IBrush ReadoutBrush = new SolidColorBrush(Color.FromArgb(238, 38, 38, 44));
    static readonly IBrush GridTint = new SolidColorBrush(Color.FromArgb(60, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
    static readonly IPen AccentPen = new Pen(AppTheme.Accent, 2);
    static readonly IPen CrosshairPen = new Pen(AppTheme.Accent, 1);
    static readonly IPen CenterWhite = new Pen(Brushes.White, 1);
    static readonly IPen CenterBlack = new Pen(Brushes.Black, 1);

    public CaptureCanvas(OverlaySession session, int index)
    {
        _session = session;
        _index = index;
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _session.Apply(SnipOutcome.Cancelled());
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        _session.Snip.Press(_session.PointerDesktop(_index, e.GetPosition(this), Bounds.Size));
        _session.Apply(SnipOutcome.Continue());
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _session.Snip.Move(_session.PointerDesktop(_index, e.GetPosition(this), Bounds.Size));
        _session.Apply(SnipOutcome.Continue());
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_session.Snip.Dragging) return;
        var outcome = _session.Release(_session.PointerDesktop(_index, e.GetPosition(this), Bounds.Size));
        _session.Apply(outcome);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var view = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (view.Width <= 0 || view.Height <= 0) return;

        context.FillRectangle(Brushes.Black, view);
        var map = _session.Map;
        if (map.TryPlacement(_index, view.Width, view.Height, out var source, out var dest) && _session.Bitmap != null)
        {
            var src = new Rect(source.X, source.Y, source.Width, source.Height);
            context.DrawImage(_session.Bitmap, src, dest);
        }

        var snip = _session.Snip;
        Rect? readout = null;
        if ((snip.Selection ?? (snip.HasCursor && !snip.Dragging ? _session.Hover(snip.Cursor) : null)) is { } selection)
        {
            var local = map.LocalRect(_index, selection, view.Width, view.Height);
            if (local is { } box && box.Width > 0 && box.Height > 0)
            {
                DimOutside(context, view, box);
                context.DrawRectangle(null, AccentPen, box);
                if (Owns(new PixelPoint(selection.X, selection.Y)))
                    readout = DrawReadout(context, box, selection);
            }
            else
            {
                context.FillRectangle(DimBrush, view);
            }
        }
        else
        {
            context.FillRectangle(DimBrush, view);
        }

        if (!snip.HasCursor || !Owns(snip.Cursor)) return;
        var cursor = map.LocalCenter(_index, snip.Cursor, view.Width, view.Height);
        if (snip.KeyboardCursor)
        {
            context.DrawLine(CrosshairPen, new Point(cursor.X - 8, cursor.Y), new Point(cursor.X + 8, cursor.Y));
            context.DrawLine(CrosshairPen, new Point(cursor.X, cursor.Y - 8), new Point(cursor.X, cursor.Y + 8));
        }
        if (_session.Settings.ShowMagnifier) DrawMagnifier(context, cursor, readout, view);
    }

    bool Owns(PixelPoint point) => _session.Map.IndexAt(point) == _index;

    static void DimOutside(DrawingContext context, Rect view, Rect box)
    {
        var top = new Rect(0, 0, view.Width, Math.Max(0, box.Y));
        var bottom = new Rect(0, Math.Max(0, box.Bottom), view.Width, Math.Max(0, view.Height - box.Bottom));
        var left = new Rect(0, Math.Max(0, box.Y), Math.Max(0, box.X), Math.Max(0, box.Height));
        var right = new Rect(Math.Max(0, box.Right), Math.Max(0, box.Y), Math.Max(0, view.Width - box.Right), Math.Max(0, box.Height));
        if (top.Height > 0) context.FillRectangle(DimBrush, top);
        if (bottom.Height > 0) context.FillRectangle(DimBrush, bottom);
        if (left.Width > 0) context.FillRectangle(DimBrush, left);
        if (right.Width > 0) context.FillRectangle(DimBrush, right);
    }

    Rect DrawReadout(DrawingContext context, Rect box, PixelRect selection)
    {
        string label = _session.Map.TryToImage(selection, out var image)
            ? $"{image.Width} × {image.Height}"
            : $"{selection.Width} × {selection.Height}";
        var text = new FormattedText(label, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, 13, AppTheme.Text);
        const double padX = 9, padY = 5;
        double boxW = text.Width + padX * 2;
        double boxH = text.Height + padY * 2;
        double x = box.Left;
        double y = box.Top - boxH - 8;
        if (y < 4)
        {
            x = box.Left + 8;
            y = box.Top + 8;
        }
        x = Math.Clamp(x, 4, Math.Max(4, Bounds.Width - boxW - 4));
        y = Math.Clamp(y, 4, Math.Max(4, Bounds.Height - boxH - 4));
        var frame = new Rect(x, y, boxW, boxH);
        context.DrawRectangle(ReadoutBrush, null, frame, 5, 5);
        context.DrawText(text, new Point(x + padX, y + padY));
        return frame;
    }

    void DrawMagnifier(DrawingContext context, Point cursor, Rect? readout, Rect view)
    {
        const double pad = 7;
        const double swatch = 10;
        const double infoH = 24;

        if (!_session.Map.TryToImage(new PixelRect(_session.Snip.Cursor.X, _session.Snip.Cursor.Y, 1, 1), out var pixel)) return;
        int imageX = pixel.X;
        int imageY = pixel.Y;
        Magnifier.TryPixel(_session.Frame, imageX, imageY, out byte red, out byte green, out byte blue);
        var label = new FormattedText(Magnifier.Label(_session.Snip.Cursor.X, _session.Snip.Cursor.Y, red, green, blue), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, 12, AppTheme.Text);

        int cell = Math.Max(8, (int)Math.Ceiling((label.Width + swatch + pad * 3) / Magnifier.Cells));
        double grid = Magnifier.Cells * cell;
        double boxW = Math.Max(grid, label.Width + swatch + pad * 3);
        double boxH = grid + infoH;
        Magnifier.Box? avoid = readout is { } block
            ? new Magnifier.Box(block.X, block.Y, block.Width, block.Height)
            : null;
        var placed = Magnifier.Place(cursor.X, cursor.Y, boxW, boxH, view.Width, view.Height, avoid);
        var box = new Rect(placed.X, placed.Y, placed.Width, placed.Height);

        context.FillRectangle(Brushes.Black, box);

        PaintLoupe(context, imageX, imageY, box.X + Math.Round((boxW - grid) / 2), box.Y, grid, cell);

        double infoY = box.Y + grid;
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(red, green, blue)),
            new Rect(box.X + pad, infoY + (infoH - swatch) / 2, swatch, swatch));
        context.DrawText(label, new Point(box.X + pad + swatch + pad, infoY + (infoH - label.Height) / 2));
        context.DrawRectangle(null, AccentPen, box);
    }

    void PaintLoupe(DrawingContext context, int imageX, int imageY, double gridX, double gridY, double grid, int cell)
    {
        _loupePixels ??= new byte[Magnifier.Cells * Magnifier.Cells * 4];
        Magnifier.Fill(_session.Frame, imageX, imageY, _loupePixels);
        int size = Magnifier.Cells * cell;
        if (_loupe == null || _loupeCell != cell)
        {
            _loupe?.Dispose();
            _loupe = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _loupeScaled = new byte[size * size * 4];
            _loupeCell = cell;
        }
        Magnifier.Expand(_loupePixels, cell, _loupeScaled);
        using (var buffer = _loupe.Lock())
        {
            int stride = size * 4;
            for (int row = 0; row < size; row++)
                Marshal.Copy(_loupeScaled, row * stride, IntPtr.Add(buffer.Address, row * buffer.RowBytes), stride);
        }

        // Already nearest-neighbor. Draw 1:1 so the compositor does not blend the cells.
        var dest = new Rect(gridX, gridY, grid, grid);
        context.DrawImage(_loupe, dest);

        double c0 = Magnifier.Center * cell;
        double cellSize = cell;
        context.FillRectangle(GridTint, new Rect(dest.X, dest.Y + c0, c0, cellSize));
        context.FillRectangle(GridTint, new Rect(dest.X + c0 + cellSize, dest.Y + c0, grid - c0 - cellSize, cellSize));
        context.FillRectangle(GridTint, new Rect(dest.X + c0, dest.Y, cellSize, c0));
        context.FillRectangle(GridTint, new Rect(dest.X + c0, dest.Y + c0 + cellSize, cellSize, grid - c0 - cellSize));

        var center = new Rect(dest.X + c0, dest.Y + c0, cellSize, cellSize);
        context.DrawRectangle(null, CenterBlack, new Rect(center.X - 1, center.Y - 1, center.Width + 2, center.Height + 2));
        context.DrawRectangle(null, CenterWhite, center);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loupe?.Dispose();
        _loupe = null;
    }
}
