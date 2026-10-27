namespace OpenSnip.Tests;

public class X11PointerTests
{
    [Fact]
    public void Query_returns_a_point_when_the_display_opens()
    {
        using var pointer = X11Pointer.TryOpen();
        if (pointer == null) return;
        Assert.NotNull(pointer.Position());
    }

    [Fact]
    public async Task Capture_covers_the_xrandr_bounding_box()
    {
        var monitors = X11Randr.TryMonitors();
        if (monitors == null) return;

        var expected = CaptureRegion.From(monitors, 1, 1);
        CapturedFrame frame;
        try { frame = await new X11CaptureBackend().CaptureAsync(); }
        catch (CaptureException) { return; }

        Assert.True(frame.OriginKnown);
        Assert.Equal(expected.X, frame.OriginX);
        Assert.Equal(expected.Y, frame.OriginY);
        Assert.Equal(expected.Width, frame.Width);
        Assert.Equal(expected.Height, frame.Height);
        Assert.Equal(expected.Width * expected.Height * 4, frame.Bgra.Length);
    }
}
