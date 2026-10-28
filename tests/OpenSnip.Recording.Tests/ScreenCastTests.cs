using Avalonia;

namespace OpenSnip.Recording.Tests;

public class ScreenCastTests
{
    [Theory]
    [InlineData(-1920, 0)]
    [InlineData(0, -1080)]
    [InlineData(1920, 100)]
    public void CropUsesMonitorLocalPixels(int x, int y)
    {
        var monitor = new PixelRect(x, y, 1920, 1080);
        Assert.Equal(new PixelRect(100, 50, 800, 600),
            PortalScreenCast.Crop(new PixelRect(x + 100, y + 50, 800, 600), monitor));
    }

    [Theory]
    [InlineData(-1, 0, 100, 100)]
    [InlineData(700, 0, 200, 100)]
    [InlineData(0, 0, 101, 100)]
    [InlineData(0, 0, 14, 100)]
    public void CropRejectsInvalidRegions(int x, int y, int width, int height)
    {
        Assert.Throws<InvalidOperationException>(() => PortalScreenCast.Crop(
            new PixelRect(x, y, width, height), new PixelRect(0, 0, 800, 600)));
    }

    [Fact]
    public void PortalGeometryAccountsForScaleAndRejectsAnotherMonitor()
    {
        var monitor = new MonitorLayout(new PixelRect(0, 0, 3840, 2160), 2, true);
        Assert.True(PortalScreenCast.Matches(new PixelRect(0, 0, 1920, 1080), monitor));
        Assert.False(PortalScreenCast.Matches(new PixelRect(1920, 0, 1920, 1080), monitor));
    }

    [Fact]
    public void CommandScalesBeforeCroppingAndEmitsOnlyRawBgra()
    {
        var args = PortalScreenCast.Arguments(42, 123, new PixelRect(-800, 0, 800, 600),
            new PixelRect(100, 50, 400, 300), 60);
        Assert.Equal(new[]
        {
            "-q", "pipewiresrc", "fd=42", "path=123", "do-timestamp=true", "!",
            "videoconvert", "!", "videoscale", "!",
            "video/x-raw,format=BGRA,width=800,height=600,pixel-aspect-ratio=1/1", "!",
            "videocrop", "left=100", "top=50", "right=300", "bottom=250", "!",
            "videorate", "!", "video/x-raw,format=BGRA,framerate=60/1", "!", "fdsink", "fd=1",
        }, args);
    }
}
