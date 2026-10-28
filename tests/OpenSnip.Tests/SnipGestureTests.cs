namespace OpenSnip.Tests;

public class SnipGestureTests
{
    [Fact]
    public void Monitor_key_captures_immediately()
    {
        var gesture = SnipGesture.FromKey(SnipKey.Monitor, shift: false, pointerDown: false, selectionReady: false);
        Assert.Equal(SnipGestureKind.CompleteMonitor, gesture.Kind);

        var midDrag = SnipGesture.FromKey(SnipKey.Monitor, shift: false, pointerDown: true, selectionReady: true);
        Assert.Equal(SnipGestureKind.CompleteMonitor, midDrag.Kind);
    }

    [Fact]
    public void All_key_captures_every_monitor_immediately()
    {
        var gesture = SnipGesture.FromKey(SnipKey.All, shift: true, pointerDown: true, selectionReady: true);
        Assert.Equal(SnipGestureKind.CompleteAll, gesture.Kind);
    }

    [Fact]
    public void Escape_and_right_click_cancel_the_drag_before_the_snip()
    {
        Assert.Equal(SnipGestureKind.ResetDrag, SnipGesture.FromKey(SnipKey.Escape, false, pointerDown: true, false).Kind);
        Assert.Equal(SnipGestureKind.Cancel, SnipGesture.FromKey(SnipKey.Escape, false, pointerDown: false, false).Kind);
        Assert.Equal(SnipGestureKind.ResetDrag, SnipGesture.FromRightClick(pointerDown: true).Kind);
        Assert.Equal(SnipGestureKind.Cancel, SnipGesture.FromRightClick(pointerDown: false).Kind);
    }

    [Fact]
    public void Enter_confirms_the_selection_or_the_monitor()
    {
        Assert.Equal(SnipGestureKind.CompleteSelection, SnipGesture.FromKey(SnipKey.Enter, false, pointerDown: true, selectionReady: true).Kind);
        Assert.Equal(SnipGestureKind.CompleteMonitor, SnipGesture.FromKey(SnipKey.Enter, false, pointerDown: true, selectionReady: false).Kind);
        Assert.Equal(SnipGestureKind.None, SnipGesture.FromKey(SnipKey.Enter, false, pointerDown: true, selectionReady: false, dragPastSlop: true).Kind);
        Assert.Equal(SnipGestureKind.CompleteMonitor, SnipGesture.FromKey(SnipKey.Enter, false, pointerDown: false, selectionReady: false).Kind);
        Assert.Equal(SnipGestureKind.CompleteSelection, SnipGesture.FromKey(SnipKey.Enter, false, pointerDown: false, selectionReady: true).Kind);
    }

    [Fact]
    public void Arrows_nudge_one_pixel_or_ten_with_shift()
    {
        var one = SnipGesture.FromKey(SnipKey.Left, shift: false, false, false);
        Assert.Equal(SnipGestureKind.Nudge, one.Kind);
        Assert.Equal(-1, one.Dx);
        Assert.Equal(0, one.Dy);

        var ten = SnipGesture.FromKey(SnipKey.Down, shift: true, true, true);
        Assert.Equal(0, ten.Dx);
        Assert.Equal(10, ten.Dy);

        Assert.Equal(10, SnipGesture.FromKey(SnipKey.Right, true, false, false).Dx);
        Assert.Equal(-10, SnipGesture.FromKey(SnipKey.Up, true, false, false).Dy);
    }

    [Fact]
    public void Tray_and_countdown_waits_match_windows()
    {
        Assert.Equal(250, SnipTiming.MenuSettleMs);
        Assert.Equal(120, SnipTiming.CountdownClearMs);
        Assert.Equal(100, SnipTiming.ThumbnailClearMs);
    }
}
