namespace OpenSnip.Tests;

public class ColorLabelTests
{
    [Fact]
    public void Pixel_color_is_read_as_rgb_from_bgra()
    {
        var data = new byte[2 * 2 * 4];
        int i = 1 * 4;
        data[i] = 0x10;
        data[i + 1] = 0x20;
        data[i + 2] = 0x30;
        data[i + 3] = 255;

        var frame = new CapturedFrame(2, 2, data);

        Assert.True(frame.TryGetRgb(1, 0, out byte r, out byte g, out byte b));
        Assert.Equal(0x30, r);
        Assert.Equal(0x20, g);
        Assert.Equal(0x10, b);
        Assert.Equal("1, 0  #302010", frame.ColorLabel(1, 0));
        Assert.False(frame.TryGetRgb(-1, 0, out _, out _, out _));
        Assert.False(frame.TryGetRgb(2, 0, out _, out _, out _));
        Assert.Equal("2, 0", frame.ColorLabel(2, 0));
    }
}
