namespace OpenSnip;

enum SnipKey
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

enum SnipGestureKind
{
    None,
    Cancel,
    ResetDrag,
    CompleteSelection,
    CompleteMonitor,
    CompleteAll,
    Nudge,
}

/// <summary>What a selection key does. Matches the Windows overlay.</summary>
readonly record struct SnipGesture(SnipGestureKind Kind, int Dx, int Dy)
{
    public static SnipGesture FromKey(SnipKey key, bool shift, bool pointerDown, bool selectionReady, bool dragPastSlop = false)
    {
        int step = shift ? 10 : 1;
        switch (key)
        {
            case SnipKey.Escape:
                return pointerDown
                    ? new SnipGesture(SnipGestureKind.ResetDrag, 0, 0)
                    : new SnipGesture(SnipGestureKind.Cancel, 0, 0);
            case SnipKey.Enter:
                // A fresh press still confirms the monitor. Only a drag that moved, and never
                // formed a rectangle, is ignored — that is what the Windows overlay does.
                if (selectionReady) return new SnipGesture(SnipGestureKind.CompleteSelection, 0, 0);
                if (dragPastSlop) return new SnipGesture(SnipGestureKind.None, 0, 0);
                return new SnipGesture(SnipGestureKind.CompleteMonitor, 0, 0);
            case SnipKey.Monitor:
                return new SnipGesture(SnipGestureKind.CompleteMonitor, 0, 0);
            case SnipKey.All:
                return new SnipGesture(SnipGestureKind.CompleteAll, 0, 0);
            case SnipKey.Left:
                return new SnipGesture(SnipGestureKind.Nudge, -step, 0);
            case SnipKey.Right:
                return new SnipGesture(SnipGestureKind.Nudge, step, 0);
            case SnipKey.Up:
                return new SnipGesture(SnipGestureKind.Nudge, 0, -step);
            default:
                return new SnipGesture(SnipGestureKind.Nudge, 0, step);
        }
    }

    public static SnipGesture FromRightClick(bool pointerDown) =>
        pointerDown
            ? new SnipGesture(SnipGestureKind.ResetDrag, 0, 0)
            : new SnipGesture(SnipGestureKind.Cancel, 0, 0);
}

static class SnipTiming
{
    /// <summary>Windows waits this long after a tray click so the menu is gone before the shot.</summary>
    public const int MenuSettleMs = 250;

    /// <summary>Windows hides its own preview, then waits at least this long before capturing.</summary>
    public const int ThumbnailClearMs = 100;

    /// <summary>Windows hides the countdown, then waits this long so it is not in the shot.</summary>
    public const int CountdownClearMs = 120;
}
