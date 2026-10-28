using Avalonia;

namespace OpenSnip;

enum SnipCommand
{
    Escape,
    Enter,
    Monitor,
    All,
    Left,
    Right,
    Up,
    Down,
}

readonly record struct SnipOutcome(bool Close, bool Cancel, bool HasRegion, PixelRect Region)
{
    public static SnipOutcome Continue() => default;
    public static SnipOutcome Cancelled() => new(true, true, false, default);
    public static SnipOutcome Chosen(PixelRect region) => new(true, false, true, region);
}

/// <summary>
/// Selection state in desktop pixels. A movement of more than 3 pixels becomes a drag,
/// matching the Windows overlay; anything smaller grabs the monitor under the pointer.
/// Arrow keys move 1 desktop pixel (10 with Shift), not one DIP.
/// </summary>
sealed class SnipSession
{
    public const int DragThreshold = 3;

    readonly MonitorMap _map;
    bool _dragging;
    bool _armed;
    PixelPoint _dragStart;

    public SnipSession(MonitorMap map) => _map = map;

    public bool Dragging => _dragging;
    public bool HasCursor { get; private set; }
    public bool KeyboardCursor { get; private set; }
    public PixelPoint Cursor { get; private set; }
    public PixelRect? Selection { get; private set; }

    public void Press(PixelPoint point)
    {
        Cursor = _map.Clamp(point);
        HasCursor = true;
        KeyboardCursor = false;
        _dragging = true;
        _armed = false;
        _dragStart = Cursor;
        Selection = null;
    }

    public void Move(PixelPoint point)
    {
        Cursor = _map.Clamp(point);
        HasCursor = true;
        KeyboardCursor = false;
        if (!_dragging) return;
        if (!_armed && Exceeds(_dragStart, Cursor)) _armed = true;
        if (_armed) Selection = _map.RectBetween(_dragStart, Cursor);
    }

    public SnipOutcome Release(PixelPoint point)
    {
        if (!_dragging) return SnipOutcome.Continue();
        _dragging = false;
        Cursor = _map.Clamp(point);
        HasCursor = true;
        KeyboardCursor = false;
        bool armed = _armed;
        _armed = false;
        if (!armed)
        {
            Selection = null;
            return SnipOutcome.Chosen(_map.MonitorRectAt(Cursor));
        }

        var rect = _map.RectBetween(_dragStart, Cursor);
        Selection = null;
        if (rect.Width < 2 || rect.Height < 2) return SnipOutcome.Continue();
        return SnipOutcome.Chosen(rect);
    }

    public SnipOutcome Command(SnipCommand command, bool large)
    {
        switch (command)
        {
            case SnipCommand.Escape:
                return SnipOutcome.Cancelled();
            case SnipCommand.Enter:
                if (Selection is { Width: > 0, Height: > 0 } selected)
                    return SnipOutcome.Chosen(selected);
                return SnipOutcome.Chosen(_map.MonitorRectAt(CurrentCursor()));
            case SnipCommand.Monitor:
                Selection = _map.MonitorRectAt(CurrentCursor());
                KeyboardCursor = false;
                return SnipOutcome.Continue();
            case SnipCommand.All:
                Selection = _map.Desktop;
                KeyboardCursor = false;
                return SnipOutcome.Continue();
            case SnipCommand.Left:
            case SnipCommand.Right:
            case SnipCommand.Up:
            case SnipCommand.Down:
                Nudge(command, large ? 10 : 1);
                return SnipOutcome.Continue();
            default:
                return SnipOutcome.Continue();
        }
    }

    void Nudge(SnipCommand command, int step)
    {
        int dx = command == SnipCommand.Left ? -step : command == SnipCommand.Right ? step : 0;
        int dy = command == SnipCommand.Up ? -step : command == SnipCommand.Down ? step : 0;
        if (!HasCursor)
        {
            Cursor = _map.Center;
            HasCursor = true;
        }
        var next = _map.Clamp(new PixelPoint(Cursor.X + dx, Cursor.Y + dy));
        int appliedX = next.X - Cursor.X;
        int appliedY = next.Y - Cursor.Y;
        Cursor = next;
        KeyboardCursor = true;
        if (_dragging)
        {
            if (!_armed && Exceeds(_dragStart, Cursor)) _armed = true;
            if (_armed) Selection = _map.RectBetween(_dragStart, Cursor);
            return;
        }
        if (Selection is { } selected)
            Selection = _map.TranslateClamped(selected, appliedX, appliedY);
    }

    PixelPoint CurrentCursor() => HasCursor ? Cursor : _map.Center;

    static bool Exceeds(PixelPoint start, PixelPoint point) =>
        Math.Abs(point.X - start.X) > DragThreshold || Math.Abs(point.Y - start.Y) > DragThreshold;
}
