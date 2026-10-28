namespace OpenSnip.Tests;

public class PinLayoutTests
{
    static readonly PinRect Origin = new(0, 0, 202, 102);
    const double Aspect = 2;

    [Fact]
    public void Hit_test_uses_a_larger_corner_target_than_the_edge()
    {
        Assert.Equal(PinEdge.None, PinLayout.HitTest(100, 50, 200, 100, 1));
        Assert.Equal(PinEdge.Left, PinLayout.HitTest(3, 50, 200, 100, 1));
        Assert.Equal(PinEdge.Right, PinLayout.HitTest(196, 50, 200, 100, 1));
        Assert.Equal(PinEdge.Top, PinLayout.HitTest(100, 2, 200, 100, 1));
        Assert.Equal(PinEdge.Bottom, PinLayout.HitTest(100, 97, 200, 100, 1));

        Assert.Equal(PinEdge.Top | PinEdge.Left, PinLayout.HitTest(3, 10, 200, 100, 1));
        Assert.Equal(PinEdge.Top | PinEdge.Right, PinLayout.HitTest(190, 5, 200, 100, 1));
        Assert.Equal(PinEdge.Bottom | PinEdge.Left, PinLayout.HitTest(0, 90, 200, 100, 1));
        Assert.Equal(PinEdge.Bottom | PinEdge.Right, PinLayout.HitTest(199, 99, 200, 100, 1));
    }

    [Fact]
    public void Grip_grows_with_scale_and_stays_usable_on_a_small_pin()
    {
        Assert.Equal(10, PinLayout.Grip(200, 100, 1));
        Assert.Equal(20, PinLayout.Grip(400, 400, 2));
        Assert.Equal(4, PinLayout.Grip(10, 10, 1));
    }

    [Fact]
    public void Resize_from_the_right_keeps_the_left_and_the_aspect()
    {
        var next = PinLayout.Resize(Origin, PinEdge.Right, 20, 0, Aspect, keepAspect: true);
        Assert.Equal(new PinRect(0, 0, 222, 112), next);
    }

    [Fact]
    public void Resize_from_the_bottom_lets_height_lead()
    {
        var next = PinLayout.Resize(Origin, PinEdge.Bottom, 0, 10, Aspect, keepAspect: true);
        Assert.Equal(new PinRect(0, 0, 222, 112), next);
    }

    [Fact]
    public void Resize_from_the_left_anchors_the_right_edge()
    {
        var next = PinLayout.Resize(Origin, PinEdge.Left, 30, 0, Aspect, keepAspect: true);
        Assert.Equal(202, next.Right);
        Assert.Equal(next.Width - PinLayout.Border * 2, (next.Height - PinLayout.Border * 2) * 2);
    }

    [Fact]
    public void Corner_resize_keeps_the_opposite_corner_fixed()
    {
        var next = PinLayout.Resize(Origin, PinEdge.Top | PinEdge.Left, -10, -10, Aspect, keepAspect: true);
        Assert.Equal(Origin.Right, next.Right);
        Assert.Equal(Origin.Bottom, next.Bottom);
        Assert.Equal(2, (next.Width - PinLayout.Border * 2) / (double)(next.Height - PinLayout.Border * 2), 2);
    }

    [Fact]
    public void Shift_resize_does_not_force_the_aspect()
    {
        var next = PinLayout.Resize(Origin, PinEdge.Right, 20, 0, Aspect, keepAspect: false);
        Assert.Equal(new PinRect(0, 0, 222, 102), next);
    }

    [Fact]
    public void Resize_does_not_shrink_below_the_minimum()
    {
        var next = PinLayout.Resize(Origin, PinEdge.Right, -500, 0, Aspect, keepAspect: true);
        Assert.True(next.Width - PinLayout.Border * 2 >= PinLayout.MinContent);
        Assert.True(next.Height - PinLayout.Border * 2 >= 1);
        Assert.Equal(0, next.X);
    }

    [Fact]
    public void Zoom_keeps_the_anchor_pixel_still()
    {
        const double anchorX = 101;
        const double anchorY = 51;
        var next = PinLayout.ZoomAt(Origin, 2, anchorX, anchorY, 200, 100);
        Assert.Equal(402, next.Width);
        Assert.Equal(202, next.Height);
        Assert.Equal(Origin.X + (int)anchorX, next.X + (int)(anchorX / Origin.Width * next.Width));
        Assert.Equal(Origin.Y + (int)anchorY, next.Y + (int)(anchorY / Origin.Height * next.Height));
    }

    [Fact]
    public void Zoom_snaps_near_actual_size_and_clamps()
    {
        Assert.Equal(1, PinLayout.ClampZoom(1.03));
        Assert.Equal(1, PinLayout.ClampZoom(0.97));
        Assert.Equal(1.04, PinLayout.ClampZoom(1.04), 5);
        Assert.Equal(0.1, PinLayout.ClampZoom(0.01));
        Assert.Equal(8, PinLayout.ClampZoom(20));
        Assert.Equal(1, PinLayout.ClampZoom(double.NaN));
    }

    [Fact]
    public void Wheel_steps_zoom_and_opacity()
    {
        Assert.Equal(1.1, PinLayout.StepZoom(1, zoomIn: true), 5);
        Assert.Equal(1, PinLayout.StepOpacity(1, increase: true), 5);
        Assert.Equal(0.9, PinLayout.StepOpacity(1, increase: false), 5);
        Assert.Equal(0.2, PinLayout.StepOpacity(0.2, increase: false), 5);
    }

    [Fact]
    public void Placement_centers_on_the_cursor_or_the_work_area()
    {
        Assert.Equal(new PinRect(94, 69, 12, 22), PinLayout.AtCursor(100, 80, 10, 20));
        Assert.Equal(new PinRect(54, 49, 12, 22), PinLayout.Centered(10, 20, 100, 80, 10, 20));
    }
}
