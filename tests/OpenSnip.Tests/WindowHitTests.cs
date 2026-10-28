using Avalonia;

namespace OpenSnip.Tests;

public class WindowHitTests
{
    static PixelRect R(int x, int y, int w, int h) => new(x, y, w, h);

    [Fact]
    public void Front_window_wins_when_they_overlap()
    {
        var windows = new[]
        {
            R(10, 10, 40, 40),
            R(0, 0, 100, 100),
        };

        var hit = WindowHit.At(windows, 20, 20, 200, 200);

        Assert.Equal(R(10, 10, 40, 40), hit);
    }

    [Fact]
    public void Miss_and_edges_do_not_claim_a_window()
    {
        var windows = new[] { R(10, 10, 20, 30) };

        Assert.Null(WindowHit.At(windows, 9, 15, 200, 200));
        Assert.Null(WindowHit.At(windows, 30, 15, 200, 200));
        Assert.Null(WindowHit.At(windows, 15, 40, 200, 200));
        Assert.Equal(R(10, 10, 20, 30), WindowHit.At(windows, 29, 39, 200, 200));
        Assert.Null(WindowHit.At(windows, 200, 10, 200, 200));
        Assert.Null(WindowHit.At(windows, -1, 10, 200, 200));
        Assert.Null(WindowHit.At(null, 10, 10, 200, 200));
    }

    [Fact]
    public void Offscreen_windows_are_clipped_before_the_hit_test()
    {
        var windows = new[] { R(-10, -4, 30, 24) };

        Assert.Equal(R(0, 0, 20, 20), WindowHit.Clip(windows[0], 100, 100));
        Assert.Equal(R(0, 0, 20, 20), WindowHit.At(windows, 1, 1, 100, 100));
        Assert.Equal(default, WindowHit.Clip(R(50, 50, 10, 10), 40, 40));
        Assert.Null(WindowHit.At(windows, 25, 1, 100, 100));
    }
}
