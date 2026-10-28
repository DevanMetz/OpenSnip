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

    public OverlayWindow(CapturedFrame frame, Settings settings, PixelRect[] windows = null, bool record = false)
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

        _canvas = new CaptureCanvas(frame, settings, union, windows, Complete, record);
        Content = _canvas;
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public PixelRect VirtualBounds => _virtualBounds;

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
    readonly PixelRect[] _windows;
    readonly bool _windowsAlign;
    readonly Action<PixelRect?> _complete;
    readonly Typeface _typeface = new("Inter");
    readonly bool _record;
    readonly IPen _framePen;
    readonly IPen _hairPen;

    bool _pressing;
    bool _dragging;
    bool _hasPointer;
    bool _keyboardCursor;
    IPointer _pointer;
    Point _start;
    Point _end;
    Point _cursor;
    Rect? _selection;
    Rect? _hoverLocal;
    PixelRect? _hoverImage;

    static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    static readonly IBrush ReadoutBrush = new SolidColorBrush(Color.FromArgb(238, 38, 38, 44));

    public CaptureCanvas(CapturedFrame frame, Settings settings, PixelRect virtualBounds, PixelRect[] windows, Action<PixelRect?> complete, bool record = false)
    {
        _frame = frame;
        _bitmap = frame.ToBitmap();
        _settings = settings;
        _virtualBounds = virtualBounds;
        _windows = windows ?? Array.Empty<PixelRect>();
        _windowsAlign = frame.Width == virtualBounds.Width && frame.Height == virtualBounds.Height;
        _complete = complete;
        _record = record;
        var color = record ? AppTheme.Record : AppTheme.Accent;
        _framePen = new Pen(color, 2);
        _hairPen = new Pen(color, 1);
        Focusable = true;
        ClipToBounds = true;
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (!TryMapKey(e.Key, out var key)) return false;
        bool pastSlop = DragPastSlop();
        bool ready = pastSlop && SelectionReady;
        Apply(SnipGesture.FromKey(key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), _pressing, ready, pastSlop));
        return true;
    }

    bool DragPastSlop()
    {
        if (!_pressing) return false;
        return Math.Abs(_end.X - _start.X) > 3 || Math.Abs(_end.Y - _start.Y) > 3;
    }

    static bool TryMapKey(Key key, out SnipKey mapped)
    {
        switch (key)
        {
            case Key.Escape: mapped = SnipKey.Escape; return true;
            case Key.Enter: mapped = SnipKey.Enter; return true;
            case Key.F: mapped = SnipKey.Monitor; return true;
            case Key.A: mapped = SnipKey.All; return true;
            case Key.Left: mapped = SnipKey.Left; return true;
            case Key.Right: mapped = SnipKey.Right; return true;
            case Key.Up: mapped = SnipKey.Up; return true;
            case Key.Down: mapped = SnipKey.Down; return true;
            default: mapped = default; return false;
        }
    }

    bool SelectionReady => _selection is { Width: >= 1, Height: >= 1 };

    void Apply(SnipGesture gesture)
    {
        switch (gesture.Kind)
        {
            case SnipGestureKind.Cancel:
                _complete(null);
                break;
            case SnipGestureKind.ResetDrag:
                ResetDrag();
                break;
            case SnipGestureKind.CompleteSelection:
                if (_selection is { } current) _complete(ToImageRect(current));
                break;
            case SnipGestureKind.CompleteMonitor:
                _complete(ToImageRect(MonitorRect(CurrentCursor())));
                break;
            case SnipGestureKind.CompleteAll:
                _complete(_record ? ToImageRect(MonitorRect(CurrentCursor())) : new PixelRect(0, 0, _frame.Width, _frame.Height));
                break;
            case SnipGestureKind.Nudge:
                Nudge(gesture.Dx, gesture.Dy);
                break;
        }
    }

    void ResetDrag()
    {
        _pressing = false;
        _dragging = false;
        _selection = null;
        try { _pointer?.Capture(null); } catch { }
        _pointer = null;
        InvalidateVisual();
    }

    void Nudge(int dx, int dy)
    {
        if (X11Cursor.TryGet() is { } pointer)
        {
            var screen = SnipGeometry.ClampTo(pointer.X + dx, pointer.Y + dy, _virtualBounds);
            bool warped = X11Cursor.TryWarp(screen.X, screen.Y);
            MoveCursor(ScreenToLocal(screen.X, screen.Y), keyboard: !warped);
            return;
        }

        var delta = SnipGeometry.ControlDelta(dx, dy, Bounds.Width, Bounds.Height, _frame.Width, _frame.Height);
        var origin = _hasPointer ? _cursor : new Point(Bounds.Width / 2, Bounds.Height / 2);
        MoveCursor(origin + new Vector(delta.X, delta.Y), keyboard: true);
    }

    void MoveCursor(Point local, bool keyboard)
    {
        _cursor = ClampPoint(local);
        _hasPointer = true;
        _keyboardCursor = keyboard;
        if (_pressing)
        {
            _end = _cursor;
            var rect = RectFromPoints(_start, _end);
            if (rect.Width >= 3 || rect.Height >= 3) _dragging = true;
            if (_dragging)
            {
                _selection = rect.Width >= 1 && rect.Height >= 1 ? rect : null;
                ClearHover();
            }
        }
        if (!_dragging) UpdateHover(_cursor);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            Apply(SnipGesture.FromRightClick(_pressing));
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _pressing = true;
        _dragging = false;
        _pointer = e.Pointer;
        _start = _end = _cursor = ClampPoint(point);
        _hasPointer = true;
        _keyboardCursor = false;
        _selection = null;
        UpdateHover(_cursor);
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
        _keyboardCursor = false;
        _end = point;
        if (_pressing)
        {
            var rect = RectFromPoints(_start, point);
            if (rect.Width >= 3 || rect.Height >= 3)
            {
                _dragging = true;
                _selection = rect.Width >= 1 && rect.Height >= 1 ? rect : null;
                ClearHover();
            }
        }
        else if (_selection == null)
        {
            _keyboardCursor = false;
            UpdateHover(point);
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressing) return;
        _pressing = false;
        _pointer = null;
        e.Pointer.Capture(null);
        var point = ClampPoint(e.GetPosition(this));
        _cursor = point;
        if (!_dragging)
        {
            _complete(HitAt(point).Image);
            return;
        }
        _dragging = false;
        _complete(ToImageRect(RectFromPoints(_start, point)));
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

    Point CurrentCursor()
    {
        if (_hasPointer) return _cursor;
        if (Bounds.Width > 1 && X11Cursor.TryGet() is { } pointer)
        {
            _cursor = ClampPoint(ScreenToLocal(pointer.X, pointer.Y));
            _hasPointer = true;
            return _cursor;
        }
        if (Bounds.Width > 1) return new Point(Bounds.Width / 2, Bounds.Height / 2);
        return new Point(0, 0);
    }

    Point ScreenToLocal(int screenX, int screenY)
    {
        double sx = Bounds.Width / Math.Max(1, _virtualBounds.Width);
        double sy = Bounds.Height / Math.Max(1, _virtualBounds.Height);
        return new Point((screenX - _virtualBounds.X) * sx, (screenY - _virtualBounds.Y) * sy);
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

    readonly struct ScreenHit
    {
        public PixelRect Image { get; init; }
        public Rect Local { get; init; }
    }

    ScreenHit HitAt(Point local)
    {
        if (_windowsAlign)
        {
            int x = ImageCoord(local.X, Bounds.Width, _frame.Width);
            int y = ImageCoord(local.Y, Bounds.Height, _frame.Height);
            var window = WindowHit.At(_windows, x, y, _frame.Width, _frame.Height);
            if (window is { } rect)
                return new ScreenHit { Image = rect, Local = ImageToLocal(rect) };
        }
        var monitor = MonitorRect(local);
        return new ScreenHit { Image = ToImageRect(monitor), Local = monitor };
    }

    void UpdateHover(Point local)
    {
        var hit = HitAt(local);
        _hoverImage = hit.Image;
        _hoverLocal = hit.Local;
    }

    void ClearHover()
    {
        _hoverImage = null;
        _hoverLocal = null;
    }

    Rect ImageToLocal(PixelRect rect)
    {
        double sx = Bounds.Width / Math.Max(1, _frame.Width);
        double sy = Bounds.Height / Math.Max(1, _frame.Height);
        return new Rect(rect.X * sx, rect.Y * sy, rect.Width * sx, rect.Height * sy);
    }

    static int ImageCoord(double local, double localSpan, int imageSpan)
    {
        if (localSpan <= 0 || imageSpan <= 0) return 0;
        int value = (int)Math.Floor(local * imageSpan / localSpan);
        if (value < 0) return 0;
        if (value >= imageSpan) return imageSpan - 1;
        return value;
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
        if (!_hasPointer) CurrentCursor();
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawImage(_bitmap, bounds);

        Rect? readout = null;
        var active = _selection ?? _hoverLocal;
        if (active is { } selection)
        {
            var top = new Rect(0, 0, Bounds.Width, Math.Max(0, selection.Y));
            var bottom = new Rect(0, Math.Max(0, selection.Bottom), Bounds.Width, Math.Max(0, Bounds.Height - selection.Bottom));
            var left = new Rect(0, Math.Max(0, selection.Y), Math.Max(0, selection.X), Math.Max(0, selection.Height));
            var right = new Rect(Math.Max(0, selection.Right), Math.Max(0, selection.Y), Math.Max(0, Bounds.Width - selection.Right), Math.Max(0, selection.Height));
            context.FillRectangle(DimBrush, top);
            context.FillRectangle(DimBrush, bottom);
            context.FillRectangle(DimBrush, left);
            context.FillRectangle(DimBrush, right);
            context.DrawRectangle(null, _framePen, selection);
            readout = DrawReadout(context, selection, _selection == null ? _hoverImage : null);
        }
        else
        {
            context.FillRectangle(DimBrush, bounds);
        }

        if (_hasPointer)
        {
            if (_keyboardCursor)
            {
                context.DrawLine(_hairPen, new Point(_cursor.X - 8, _cursor.Y), new Point(_cursor.X + 8, _cursor.Y));
                context.DrawLine(_hairPen, new Point(_cursor.X, _cursor.Y - 8), new Point(_cursor.X, _cursor.Y + 8));
            }
            if (_settings.ShowMagnifier) DrawMagnifier(context, readout);
        }
    }

    Rect DrawReadout(DrawingContext context, Rect selection, PixelRect? exact)
    {
        int width;
        int height;
        if (exact is { } pixels)
        {
            width = Math.Max(1, pixels.Width);
            height = Math.Max(1, pixels.Height);
        }
        else
        {
            double sx = _frame.Width / Math.Max(1, Bounds.Width);
            double sy = _frame.Height / Math.Max(1, Bounds.Height);
            width = Math.Max(1, (int)Math.Round(selection.Width * sx));
            height = Math.Max(1, (int)Math.Round(selection.Height * sy));
        }
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
        const double labelHeight = 22;
        const int sourceSize = 24;

        int imageX = ImageCoord(_cursor.X, Bounds.Width, _frame.Width);
        int imageY = ImageCoord(_cursor.Y, Bounds.Height, _frame.Height);
        var label = new FormattedText(_frame.ColorLabel(imageX, imageY), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, 11, AppTheme.Muted);

        double square = inner + pad * 2;
        double boxW = Math.Max(square, label.Width + 16);
        var box = PlaceMagnifier(boxW, square + labelHeight, readout);
        double squareX = box.X + (boxW - square) / 2;
        var dest = new Rect(squareX + pad, box.Y + pad, inner, inner);

        context.DrawRectangle(ReadoutBrush, _framePen, new Rect(box.X, box.Y, boxW, square + labelHeight), 7, 7);

        // Keep the source centered on the cursor. Clamping it used to slide the
        // crosshair off the real pixel once the cursor reached the screen edge.
        int left = imageX - sourceSize / 2;
        int top = imageY - sourceSize / 2;
        int srcL = Math.Max(0, left);
        int srcT = Math.Max(0, top);
        int srcR = Math.Min(_frame.Width, left + sourceSize);
        int srcB = Math.Min(_frame.Height, top + sourceSize);
        double scale = inner / sourceSize;
        if (srcR > srcL && srcB > srcT)
        {
            var source = new Rect(srcL, srcT, srcR - srcL, srcB - srcT);
            var destPart = new Rect(
                dest.X + (srcL - left) * scale,
                dest.Y + (srcT - top) * scale,
                (srcR - srcL) * scale,
                (srcB - srcT) * scale);
            using (context.PushClip(dest))
                context.DrawImage(_bitmap, source, destPart);
        }

        double cx = dest.X + (imageX - left + 0.5) * scale;
        double cy = dest.Y + (imageY - top + 0.5) * scale;
        context.DrawLine(_hairPen, new Point(cx - 7, cy), new Point(cx + 7, cy));
        context.DrawLine(_hairPen, new Point(cx, cy - 7), new Point(cx, cy + 7));

        context.DrawText(label, new Point(box.X + (boxW - label.Width) / 2, box.Y + square + 2));
    }
}
