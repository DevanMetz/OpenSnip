using Avalonia;

namespace OpenSnip.Tests;

public class MonitorMapTests
{
    static MonitorLayout Monitor(int x, int y, int width, int height, double scale, bool primary = false) =>
        new(new PixelRect(x, y, width, height), scale, primary);

    static MonitorMap Mixed(bool originKnown = true) => MonitorMap.Create(new[]
    {
        Monitor(0, 0, 1920, 1080, 1, primary: true),
        Monitor(1920, 0, 3840, 2160, 2),
    }, 5760, 2160, originKnown, 0, 0);

    [Fact]
    public void Mixed_scale_center_of_the_second_monitor_is_its_physical_center()
    {
        var map = Mixed();
        // The 2x monitor is 3840x2160 physical pixels, shown in a 1920x1080 DIP window.
        var point = map.DesktopPoint(1, 960, 540, 1920, 1080);

        Assert.Equal(new PixelPoint(3840, 1080), point);
        Assert.True(map.TryToImage(new PixelRect(point.X, point.Y, 1, 1), out var image));
        Assert.Equal(new PixelRect(3840, 1080, 1, 1), image);
    }

    [Fact]
    public void Mixed_scale_window_fraction_stays_on_that_monitor()
    {
        var map = Mixed();
        var left = map.DesktopPoint(0, 0, 0, 1920, 1080);
        var rightEdge = map.DesktopPoint(0, 1919, 1079, 1920, 1080);
        var second = map.DesktopPoint(1, 0, 0, 1920, 1080);

        Assert.Equal(new PixelPoint(0, 0), left);
        Assert.Equal(new PixelPoint(1919, 1079), rightEdge);
        Assert.Equal(new PixelPoint(1920, 0), second);
        Assert.Equal(0, map.IndexAt(rightEdge));
        Assert.Equal(1, map.IndexAt(second));
    }

    [Fact]
    public void Local_center_of_a_pixel_stays_on_that_pixel()
    {
        var map = Mixed();
        var point = map.LocalCenter(1, new PixelPoint(1920, 0), 1920, 1080);
        Assert.Equal(0.5 * 1920 / 3840d, point.X, 3);
        Assert.InRange(point.Y, 0, 1);
    }

    [Fact]
    public void Full_monitor_crop_is_that_monitors_pixels()
    {
        var map = Mixed();
        Assert.True(map.TryToImage(new PixelRect(1920, 0, 3840, 2160), out var image));
        Assert.Equal(new PixelRect(1920, 0, 3840, 2160), image);

        Assert.True(map.TryPlacement(1, 1920, 1080, out var source, out var dest));
        Assert.Equal(image, source);
        Assert.Equal(new Rect(0, 0, 1920, 1080), dest);
    }

    [Fact]
    public void Selection_crossing_the_boundary_splits_per_monitor()
    {
        var map = Mixed();
        var selection = new PixelRect(1800, 100, 400, 50);

        var onFirst = map.LocalRect(0, selection, 1920, 1080);
        var onSecond = map.LocalRect(1, selection, 1920, 1080);

        Assert.NotNull(onFirst);
        Assert.NotNull(onSecond);
        Assert.Equal(120, onFirst.Value.Width, 0);
        Assert.Equal(1920, onFirst.Value.Right, 0);
        Assert.Equal(0, onSecond.Value.X, 0);
        Assert.Equal(280 * 1920 / 3840d, onSecond.Value.Width, 3);
    }

    [Fact]
    public void Negative_origin_capture_lines_up_with_both_monitors()
    {
        var monitors = new[]
        {
            Monitor(-1920, 0, 1920, 1080, 1),
            Monitor(0, 0, 1920, 1080, 1, primary: true),
        };
        var map = MonitorMap.Create(monitors, 3840, 1080, originKnown: true, originX: -1920, originY: 0);

        Assert.True(map.TryToImage(new PixelRect(-1920, 0, 10, 10), out var left));
        Assert.True(map.TryToImage(new PixelRect(0, 0, 10, 10), out var right));
        Assert.Equal(new PixelRect(0, 0, 10, 10), left);
        Assert.Equal(new PixelRect(1920, 0, 10, 10), right);
    }

    [Fact]
    public void Tight_screenshot_without_an_origin_uses_the_bounding_box()
    {
        var monitors = new[]
        {
            Monitor(-1920, 0, 1920, 1080, 1),
            Monitor(0, 0, 1920, 1080, 1, primary: true),
        };
        var map = MonitorMap.Create(monitors, 3840, 1080, originKnown: false, 0, 0);

        Assert.True(map.OneToOne);
        Assert.True(map.TryToImage(new PixelRect(-1910, 5, 4, 6), out var image));
        Assert.Equal(new PixelRect(10, 5, 4, 6), image);
    }

    [Fact]
    public void Differently_sized_frame_is_spread_across_the_desktop()
    {
        var monitors = new[]
        {
            Monitor(-1920, 0, 1920, 1080, 1),
            Monitor(0, 0, 1920, 1080, 1, primary: true),
        };
        var map = MonitorMap.Create(monitors, 1920, 540, originKnown: false, 0, 0);

        Assert.False(map.OneToOne);
        Assert.True(map.TryToImage(new PixelRect(-1920, 0, 1920, 1080), out var left));
        Assert.True(map.TryToImage(new PixelRect(0, 0, 1920, 1080), out var right));
        Assert.Equal(new PixelRect(0, 0, 960, 540), left);
        Assert.Equal(new PixelRect(960, 0, 960, 540), right);
    }

    [Fact]
    public void Capture_region_includes_monitors_above_and_left_of_the_origin()
    {
        var region = CaptureRegion.From(new[]
        {
            new PixelRect(-1920, -1080, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
        }, 1920, 1080);

        Assert.Equal(new CaptureRegion(-1920, -1080, 3840, 2160), region);
    }

    [Fact]
    public void Capture_region_without_monitors_is_the_root_window()
    {
        Assert.Equal(new CaptureRegion(0, 0, 1920, 1080), CaptureRegion.From(null, 1920, 1080));
        Assert.Equal(new CaptureRegion(0, 0, 800, 600), CaptureRegion.From(new[] { new PixelRect(0, 0, 0, 0) }, 800, 600));
    }
}
