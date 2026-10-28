using Avalonia;

namespace OpenSnip.Tests;

public class SnipGeometryTests
{
    [Fact]
    public void Image_rect_maps_onto_a_virtual_desktop_whose_origin_is_not_zero()
    {
        var image = new PixelRect(10, 20, 30, 40);
        var desktop = new PixelRect(-1920, -100, 3840, 1200);
        var screen = SnipGeometry.ToScreen(image, desktop, 3840, 1200);
        Assert.Equal(new PixelRect(-1910, -80, 30, 40), screen);

        var back = SnipGeometry.ScreenToImage(screen, desktop, 3840, 1200);
        Assert.Equal(image, back);
    }

    [Fact]
    public void Screen_rect_scales_when_the_capture_and_desktop_differ()
    {
        var desktop = new PixelRect(0, 0, 200, 100);
        var screen = new PixelRect(50, 0, 100, 100);
        var image = SnipGeometry.ScreenToImage(screen, desktop, 100, 50);
        Assert.Equal(new PixelRect(25, 0, 50, 50), image);
    }

    [Fact]
    public void Best_screen_is_the_one_with_the_largest_overlap()
    {
        var screens = new[]
        {
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(1920, 0, 1920, 1080),
        };
        var snip = new PixelRect(1800, 100, 400, 200);
        Assert.Equal(1, SnipGeometry.BestScreen(snip, screens));
    }

    [Fact]
    public void Best_screen_falls_back_to_the_nearest()
    {
        var screens = new[]
        {
            new PixelRect(0, 0, 100, 100),
            new PixelRect(500, 0, 100, 100),
        };
        Assert.Equal(1, SnipGeometry.BestScreen(new PixelRect(420, 10, 20, 20), screens));
        Assert.Equal(-1, SnipGeometry.BestScreen(new PixelRect(0, 0, 1, 1), Array.Empty<PixelRect>()));
    }

    [Fact]
    public void Countdown_sits_at_the_bottom_center_of_the_working_area()
    {
        var area = new PixelRect(0, 40, 1920, 1040);
        var point = SnipGeometry.BottomCenter(area, 128, 128, 40);
        Assert.Equal((1920 - 128) / 2, point.X);
        Assert.Equal(40 + 1040 - 128 - 40, point.Y);
    }

    [Fact]
    public void Countdown_stays_inside_a_short_working_area()
    {
        var point = SnipGeometry.BottomCenter(new PixelRect(10, 20, 80, 30), 128, 128, 40);
        Assert.Equal(10, point.X);
        Assert.Equal(20, point.Y);
    }

    [Fact]
    public void Pointer_nudge_clamps_to_the_desktop()
    {
        var desktop = new PixelRect(-10, 0, 100, 50);
        Assert.Equal(new PixelPoint(89, 0), SnipGeometry.ClampTo(200, -5, desktop));
        Assert.Equal(new PixelPoint(-10, 49), SnipGeometry.ClampTo(-40, 80, desktop));
    }

    [Fact]
    public void One_image_pixel_is_one_control_pixel_when_sizes_match()
    {
        var delta = SnipGeometry.ControlDelta(10, -1, 1920, 1080, 1920, 1080);
        Assert.Equal(10, delta.X);
        Assert.Equal(-1, delta.Y);
    }

    [Fact]
    public void Control_delta_shrinks_when_the_overlay_is_scaled()
    {
        var delta = SnipGeometry.ControlDelta(2, 0, 960, 540, 1920, 1080);
        Assert.Equal(1, delta.X);
    }
}
