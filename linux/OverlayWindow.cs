using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace OpenSnip;

sealed class OverlayWindow : Window
{
    readonly TaskCompletionSource<PixelRect?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly PixelRect _virtualBounds;
    readonly CaptureCanvas _canvas;

    public OverlayWindow(CapturedFrame frame, Settings settings)
    {
        var screens = Screens.All;
        PixelRect union = screens.Count > 0 ? screens[0].Bounds : new PixelRect(0, 0, frame.Width, frame.Height);
        foreach (var screen in screens) union = union.Union(screen.Bounds);
        _virtualBounds = union;

        var scale = Screens.ScreenFromPoint(union.Position)?.Scaling
                    ?? Screens.Primary?.Scaling
                    ?? 1;

        Title = "OpenSnip";
        Icon = IconLoader.Icon;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        Position = union.Position;
        Width = Math.Max(1, union.Width / scale);
        Height = Math.Max(1, union.Height / scale);

        _canvas = new CaptureCanvas(frame, settings, union, Complete);
        Content = _canvas;
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public Task<PixelRect?> SelectAsync()
    {
        Show();
        Activate();
        Focus();
        _canvas.Focus();
        return _result.Task;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _canvas.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_canvas.HandleKey(e))
        {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    void Complete(PixelRect? selection)
    {
        if (_result.TrySetResult(selection)) Close();
    }
}

sealed class CaptureCanvas : Control
{
    readonly CapturedFrame _frame;
    readonly Bitmap _bitmap;
    readonly Settings _settings;
    readonly PixelRect _virtualBounds;
    readonly Action<PixelRect?> _complete;
    readonly Typeface _typeface = new("Inter");

    bool _dragging;
    bool _hasPointer;
    bool _keyboardCursor;
    Point _start;
    Point _end;
    Point _cursor;
    Rect? _selection;

    static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    static readonly IBrush ReadoutBrush = new SolidColorBrush(Color.FromArgb(238, 38, 38, 44));
    static readonly IPen AccentPen = new Pen(AppTheme.Accent, 2);
    static readonly IPen CrosshairPen = new Pen(AppTheme.Accent, 1);

    public CaptureCanvas(CapturedFrame frame, Settings settings, PixelRect virtualBounds, Action<PixelRect?> complete)
    {
        _frame = frame;
        _bitmap = frame.ToBitmap();
        _settings = settings;
        _virtualBounds = virtualBounds;
        _complete = complete;
        Focusable = true;
        ClipToBounds = true;
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _complete(null);
                return true;
            case Key.Enter:
                if (_selection is { } current) _complete(ToImageRect(current));
                else _complete(MonitorAt(CurrentCursor()));
                return true;
            case Key.A:
                _selection = new Rect(0, 0, Bounds.Width, Bounds.Height);
                _keyboardCursor = false;
                InvalidateVisual();
                return true;
            case Key.F:
                _selection = MonitorRect(CurrentCursor());
                _keyboardCursor = false;
                InvalidateVisual();
                return true;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                Nudge(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return true;
        }
        return false;
    }

    void Nudge(Key key, bool large)
    {
        double step = large ? 10 : 1;
        var delta = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            _ => new Vector(0, step),
        };
        if (!_hasPointer)
        {
            _cursor = new Point(Bounds.Width / 2, Bounds.Height / 2);
            _hasPointer = true;
        }
        _cursor = ClampPoint(_cursor + delta);
        if (_selection is { } current)
            _selection = ClampRect(current.Translate(delta));
        _keyboardCursor = true;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _complete(null);
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        _start = _end = _cursor = ClampPoint(point);
        _hasPointer = true;
        _keyboardCursor = false;
        _selection = null;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = ClampPoint(e.GetPosition(this));
        _cursor = point;
        _hasPointer = true;
        if (_dragging)
        {
            _end = point;
            var rect = RectFromPoints(_start, _end);
            _selection = rect.Width >= 1 && rect.Height >= 1 ? rect : null;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        var point = ClampPoint(e.GetPosition(this));
        _cursor = point;
        var rect = RectFromPoints(_start, point);
        if (rect.Width < 3 && rect.Height < 3)
        {
            _complete(MonitorAt(point));
            return;
        }
        _complete(ToImageRect(rect));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    Rect RectFromPoints(Point a, Point b)
    {
        double left = Math.Clamp(Math.Min(a.X, b.X), 0, Math.Max(0, Bounds.Width));
        double top = Math.Clamp(Math.Min(a.Y, b.Y), 0, Math.Max(0, Bounds.Height));
        double right = Math.Clamp(Math.Max(a.X, b.X), 0, Math.Max(0, Bounds.Width));
        double bottom = Math.Clamp(Math.Max(a.Y, b.Y), 0, Math.Max(0, Bounds.Height));
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    Point ClampPoint(Point point) =>
        new(Math.Clamp(point.X, 0, Math.Max(0, Bounds.Width)), Math.Clamp(point.Y, 0, Math.Max(0, Bounds.Height)));

    Rect ClampRect(Rect rect)
    {
        double x = Math.Clamp(rect.X, 0, Math.Max(0, Bounds.Width - rect.Width));
        double y = Math.Clamp(rect.Y, 0, Math.Max(0, Bounds.Height - rect.Height));
        return new Rect(x, y, rect.Width, rect.Height);
    }

    Point CurrentCursor()
    {
        if (_hasPointer) return _cursor;
        return new Point(Bounds.Width / 2, Bounds.Height / 2);
    }

    Rect MonitorRect(Point local)
    {
        var screens = TopLevel.GetTopLevel(this)?.Screens;
        if (screens == null) return new Rect(0, 0, Bounds.Width, Bounds.Height);
        double physicalX = _virtualBounds.X + local.X * _virtualBounds.Width / Math.Max(1, Bounds.Width);
        double physicalY = _virtualBounds.Y + local.Y * _virtualBounds.Height / Math.Max(1, Bounds.Height);
        var screen = screens.ScreenFromPoint(new PixelPoint((int)physicalX, (int)physicalY))
                     ?? screens.Primary;
        if (screen == null) return new Rect(0, 0, Bounds.Width, Bounds.Height);
        double sx = Bounds.Width / Math.Max(1, _virtualBounds.Width);
        double sy = Bounds.Height / Math.Max(1, _virtualBounds.Height);
        return new Rect(
            (screen.Bounds.X - _virtualBounds.X) * sx,
            (screen.Bounds.Y - _virtualBounds.Y) * sy,
            screen.Bounds.Width * sx,
            screen.Bounds.Height * sy);
    }

    PixelRect MonitorAt(Point local)
    {
        var rect = MonitorRect(local);
        return ToImageRect(rect);
    }

    PixelRect ToImageRect(Rect local)
    {
        double sx = _frame.Width / Math.Max(1, Bounds.Width);
        double sy = _frame.Height / Math.Max(1, Bounds.Height);
        int x = (int)Math.Round(local.X * sx);
        int y = (int)Math.Round(local.Y * sy);
        int width = (int)Math.Round(local.Width * sx);
        int height = (int)Math.Round(local.Height * sy);
        x = Math.Clamp(x, 0, _frame.Width - 1);
        y = Math.Clamp(y, 0, _frame.Height - 1);
        width = Math.Clamp(width, 1, _frame.Width - x);
        height = Math.Clamp(height, 1, _frame.Height - y);
        return new PixelRect(x, y, width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawImage(_bitmap, bounds);

        Rect? readout = null;
        if (_selection is { } selection)
        {
            var top = new Rect(0, 0, Bounds.Width, Math.Max(0, selection.Y));
            var bottom = new Rect(0, Math.Max(0, selection.Bottom), Bounds.Width, Math.Max(0, Bounds.Height - selection.Bottom));
            var left = new Rect(0, Math.Max(0, selection.Y), Math.Max(0, selection.X), Math.Max(0, selection.Height));
            var right = new Rect(Math.Max(0, selection.Right), Math.Max(0, selection.Y), Math.Max(0, Bounds.Width - selection.Right), Math.Max(0, selection.Height));
            context.FillRectangle(DimBrush, top);
            context.FillRectangle(DimBrush, bottom);
            context.FillRectangle(DimBrush, left);
            context.FillRectangle(DimBrush, right);
            context.DrawRectangle(null, AccentPen, selection);
            readout = DrawReadout(context, selection);
        }
        else
        {
            context.FillRectangle(DimBrush, bounds);
        }

        if (_hasPointer)
        {
            if (_keyboardCursor)
            {
                context.DrawLine(CrosshairPen, new Point(_cursor.X - 8, _cursor.Y), new Point(_cursor.X + 8, _cursor.Y));
                context.DrawLine(CrosshairPen, new Point(_cursor.X, _cursor.Y - 8), new Point(_cursor.X, _cursor.Y + 8));
            }
            if (_settings.ShowMagnifier) DrawMagnifier(context, readout);
        }
    }

    Rect DrawReadout(DrawingContext context, Rect selection)
    {
        double sx = _frame.Width / Math.Max(1, Bounds.Width);
        double sy = _frame.Height / Math.Max(1, Bounds.Height);
        int width = Math.Max(1, (int)Math.Round(selection.Width * sx));
        int height = Math.Max(1, (int)Math.Round(selection.Height * sy));
        var text = new FormattedText($"{width} × {height}", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, 13, AppTheme.Text);
        const double padX = 9, padY = 5;
        double boxW = text.Width + padX * 2;
        double boxH = text.Height + padY * 2;

        // Sit just above the selection's top-left corner; if the selection touches
        // the top edge, keep the readout inside its top-left corner instead.
        double x = selection.Left;
        double y = selection.Top - boxH - 8;
        if (y < 4)
        {
            x = selection.Left + 8;
            y = selection.Top + 8;
        }
        x = Math.Clamp(x, 4, Math.Max(4, Bounds.Width - boxW - 4));
        y = Math.Clamp(y, 4, Math.Max(4, Bounds.Height - boxH - 4));
        var box = new Rect(x, y, boxW, boxH);
        context.DrawRectangle(ReadoutBrush, null, box, 5, 5);
        context.DrawText(text, new Point(x + padX, y + padY));
        return box;
    }

    static bool Overlaps(Rect a, Rect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    Rect PlaceMagnifier(double width, double height, Rect? readout)
    {
        const double gap = 22;
        const double margin = 4;

        Rect? TryAt(double x, double y)
        {
            if (x < margin || y < margin ||
                x + width > Bounds.Width - margin ||
                y + height > Bounds.Height - margin)
                return null;
            var rect = new Rect(x, y, width, height);
            if (readout is { } box && Overlaps(rect, box)) return null;
            return rect;
        }

        double right = _cursor.X + gap;
        double left = _cursor.X - width - gap;
        double down = _cursor.Y + gap;
        double up = _cursor.Y - height - gap;

        var found = TryAt(right, down) ?? TryAt(left, down) ?? TryAt(right, up) ?? TryAt(left, up);
        if (found == null && readout is { } r)
        {
            found = TryAt(r.Right + gap, down)
                    ?? TryAt(r.Left - width - gap, down)
                    ?? TryAt(right, r.Bottom + gap)
                    ?? TryAt(right, r.Top - height - gap)
                    ?? TryAt(left, r.Bottom + gap)
                    ?? TryAt(left, r.Top - height - gap);
        }
        if (found is { } placed) return placed;

        // Tiny screen: flip away from the nearest edges and clamp to the bounds.
        double fx = right;
        double fy = down;
        if (fx + width > Bounds.Width - margin) fx = left;
        if (fy + height > Bounds.Height - margin) fy = up;
        fx = Math.Clamp(fx, margin, Math.Max(margin, Bounds.Width - width - margin));
        fy = Math.Clamp(fy, margin, Math.Max(margin, Bounds.Height - height - margin));
        return new Rect(fx, fy, width, height);
    }

    void DrawMagnifier(DrawingContext context, Rect? readout)
    {
        const double inner = 104;
        const double pad = 3;
        const double labelHeight = 20;
        double total = inner + pad * 2;
        var box = PlaceMagnifier(total, total + labelHeight, readout);
        double x = box.X;
        double y = box.Y;

        int imageX = Math.Clamp((int)(_cursor.X * _frame.Width / Math.Max(1, Bounds.Width)), 0, _frame.Width - 1);
        int imageY = Math.Clamp((int)(_cursor.Y * _frame.Height / Math.Max(1, Bounds.Height)), 0, _frame.Height - 1);
        int sourceSize = 24;
        int left = Math.Clamp(imageX - sourceSize / 2, 0, Math.Max(0, _frame.Width - sourceSize));
        int top = Math.Clamp(imageY - sourceSize / 2, 0, Math.Max(0, _frame.Height - sourceSize));
        var source = new Rect(left, top, Math.Min(sourceSize, _frame.Width), Math.Min(sourceSize, _frame.Height));
        var dest = new Rect(x + pad, y + pad, inner, inner);

        context.DrawRectangle(ReadoutBrush, new Pen(AppTheme.Accent, 2), new Rect(x, y, total, total + labelHeight), 7, 7);
        using (context.PushClip(dest))
        {
            context.DrawImage(_bitmap, source, dest);
        }
        var center = dest.Center;
        context.DrawLine(CrosshairPen, new Point(center.X - 7, center.Y), new Point(center.X + 7, center.Y));
        context.DrawLine(CrosshairPen, new Point(center.X, center.Y - 7), new Point(center.X, center.Y + 7));

        var label = new FormattedText($"{imageX}, {imageY}", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, 11, AppTheme.Muted);
        context.DrawText(label, new Point(x + (total - label.Width) / 2, y + total + 2));
    }
}
