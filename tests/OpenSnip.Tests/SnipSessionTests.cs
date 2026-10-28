using Avalonia;

namespace OpenSnip.Tests;

public class SnipSessionTests
{
    static SnipSession Session()
    {
        var map = MonitorMap.Create(new[]
        {
            new MonitorLayout(new PixelRect(0, 0, 1920, 1080), 1, true),
            new MonitorLayout(new PixelRect(1920, 0, 3840, 2160), 2, false),
        }, 5760, 2160, true, 0, 0);
        return new SnipSession(map);
    }

    [Fact]
    public void Click_grabs_the_monitor_under_the_pointer()
    {
        var session = Session();
        session.Press(new PixelPoint(4000, 1000));
        session.Move(new PixelPoint(4002, 1001));
        var outcome = session.Release(new PixelPoint(4002, 1001));

        Assert.True(outcome.HasRegion);
        Assert.Equal(new PixelRect(1920, 0, 3840, 2160), outcome.Region);
    }

    [Fact]
    public void Drag_uses_desktop_pixels_and_includes_both_ends()
    {
        var session = Session();
        session.Press(new PixelPoint(100, 80));
        session.Move(new PixelPoint(110, 100));
        var outcome = session.Release(new PixelPoint(110, 100));

        Assert.True(outcome.HasRegion);
        Assert.Equal(new PixelRect(100, 80, 11, 21), outcome.Region);
    }

    [Fact]
    public void Drag_that_never_reaches_two_pixels_does_not_capture()
    {
        var session = Session();
        session.Press(new PixelPoint(0, 0));
        session.Move(new PixelPoint(10, 10));
        session.Move(new PixelPoint(1, 0));
        var outcome = session.Release(new PixelPoint(1, 0));

        Assert.False(outcome.Close);
        Assert.False(session.Dragging);
    }

    [Fact]
    public void Enter_confirms_the_selection_or_the_monitor()
    {
        var session = Session();
        session.Move(new PixelPoint(10, 10));
        var monitor = session.Command(SnipCommand.Enter, false);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), monitor.Region);

        session = Session();
        session.Move(new PixelPoint(2000, 10));
        var marked = session.Command(SnipCommand.Monitor, false);
        Assert.False(marked.Close);
        Assert.Equal(new PixelRect(1920, 0, 3840, 2160), session.Selection);
        var chosen = session.Command(SnipCommand.Enter, false);
        Assert.Equal(session.Selection, chosen.Region);
    }

    [Fact]
    public void All_selects_the_whole_desktop_without_closing()
    {
        var session = Session();
        var outcome = session.Command(SnipCommand.All, false);

        Assert.False(outcome.Close);
        Assert.Equal(new PixelRect(0, 0, 5760, 2160), session.Selection);
    }

    [Fact]
    public void Arrow_nudges_one_desktop_pixel_or_ten_with_shift()
    {
        var session = Session();
        session.Move(new PixelPoint(3840, 1080));
        session.Command(SnipCommand.Right, large: false);
        Assert.Equal(new PixelPoint(3841, 1080), session.Cursor);

        session.Command(SnipCommand.Down, large: true);
        Assert.Equal(new PixelPoint(3841, 1090), session.Cursor);
        Assert.True(session.KeyboardCursor);
    }

    [Fact]
    public void Arrow_moves_an_existing_selection_with_the_cursor()
    {
        var session = Session();
        session.Move(new PixelPoint(100, 100));
        session.Command(SnipCommand.Monitor, false);
        session.Command(SnipCommand.Right, false);

        Assert.Equal(new PixelPoint(101, 100), session.Cursor);
        Assert.Equal(new PixelRect(1, 0, 1920, 1080), session.Selection);
    }

    [Fact]
    public void Escape_cancels()
    {
        var session = Session();
        session.Press(new PixelPoint(10, 10));
        var outcome = session.Command(SnipCommand.Escape, false);

        Assert.True(outcome.Close);
        Assert.True(outcome.Cancel);
    }
}
