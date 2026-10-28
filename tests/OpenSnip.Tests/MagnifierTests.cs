namespace OpenSnip.Tests;

public class MagnifierTests
{
    [Fact]
    public void CenterCellIsTheCursorPixel()
    {
        var frame = Frame(5, 5,
            (2, 2, 255, 0, 0),
            (1, 2, 0, 255, 0),
            (3, 3, 0, 0, 255));
        var loupe = new byte[Magnifier.Cells * Magnifier.Cells * 4];
        Magnifier.Fill(frame, 2, 2, loupe);

        Assert.Equal((255, 0, 0, 255), At(loupe, Magnifier.Center, Magnifier.Center));
        Assert.Equal((0, 255, 0, 255), At(loupe, Magnifier.Center - 1, Magnifier.Center));
        Assert.Equal((0, 0, 255, 255), At(loupe, Magnifier.Center + 1, Magnifier.Center + 1));
    }

    [Fact]
    public void EdgeDoesNotSlideTheCenterCell()
    {
        var frame = Frame(4, 3, (0, 0, 10, 20, 30), (3, 2, 200, 10, 1));
        var loupe = new byte[Magnifier.Cells * Magnifier.Cells * 4];

        Magnifier.Fill(frame, 0, 0, loupe);
        Assert.Equal((10, 20, 30, 255), At(loupe, Magnifier.Center, Magnifier.Center));
        Assert.Equal((0, 0, 0, 255), At(loupe, Magnifier.Center - 1, Magnifier.Center));
        Assert.Equal((0, 0, 0, 255), At(loupe, Magnifier.Center, Magnifier.Center - 1));

        Magnifier.Fill(frame, 3, 2, loupe);
        Assert.Equal((200, 10, 1, 255), At(loupe, Magnifier.Center, Magnifier.Center));
        Assert.Equal((0, 0, 0, 255), At(loupe, Magnifier.Center + 1, Magnifier.Center));
        Assert.Equal((0, 0, 0, 255), At(loupe, Magnifier.Center, Magnifier.Center + 1));
    }

    [Fact]
    public void ExpandKeepsCellsCrisp()
    {
        var frame = Frame(3, 3, (1, 1, 255, 0, 0), (2, 2, 0, 0, 255));
        var cells = new byte[Magnifier.Cells * Magnifier.Cells * 4];
        Magnifier.Fill(frame, 1, 1, cells);
        const int cell = 3;
        var scaled = new byte[Magnifier.Cells * cell * Magnifier.Cells * cell * 4];
        Magnifier.Expand(cells, cell, scaled);

        int origin = Magnifier.Center * cell;
        for (int dy = 0; dy < cell; dy++)
        for (int dx = 0; dx < cell; dx++)
            Assert.Equal((255, 0, 0, 255), ScaledAt(scaled, cell, origin + dx, origin + dy));

        int neighbor = (Magnifier.Center + 1) * cell;
        Assert.Equal((0, 0, 255, 255), ScaledAt(scaled, cell, neighbor, neighbor));
        Assert.Equal((255, 0, 0, 255), ScaledAt(scaled, cell, origin + cell - 1, origin + cell - 1));
        Assert.NotEqual((255, 0, 0, 255), ScaledAt(scaled, cell, origin + cell, origin));
    }

    [Fact]
    public void LabelMatchesTheWindowsReadout()
    {
        Assert.True(Magnifier.TryPixel(Frame(2, 2, (1, 0, 0x0A, 0xB0, 0xC3)), 1, 0, out byte r, out byte g, out byte b));
        Assert.Equal("1, 0   #0AB0C3", Magnifier.Label(1, 0, r, g, b));
        Assert.False(Magnifier.TryPixel(Frame(2, 2), -1, 0, out _, out _, out _));
    }

    [Theory]
    [InlineData(0, 100, 100, 0)]
    [InlineData(50, 100, 200, 100)]
    [InlineData(99.9, 100, 100, 99)]
    [InlineData(100, 100, 100, 99)]
    [InlineData(-4, 100, 100, 0)]
    [InlineData(10, 0, 100, 0)]
    [InlineData(10, 100, 1, 0)]
    public void ImageCoordMatchesTheFrameMapping(double local, double span, int pixels, int expected)
    {
        Assert.Equal(expected, Magnifier.ImageCoord(local, span, pixels));
    }

    [Fact]
    public void LoupeSitsDownAndRightOfTheCursor()
    {
        var box = Magnifier.Place(10, 10, 120, 140, 800, 600, null);
        Assert.Equal(new Magnifier.Box(32, 32, 120, 140), box);
    }

    [Fact]
    public void LoupeFlipsAtTheBottomRightEdge()
    {
        var box = Magnifier.Place(790, 590, 120, 140, 800, 600, null);
        Assert.Equal(new Magnifier.Box(648, 428, 120, 140), box);
    }

    [Fact]
    public void LoupeMovesOffTheSizeReadout()
    {
        var readout = new Magnifier.Box(122, 122, 120, 140);
        var box = Magnifier.Place(100, 100, 120, 140, 800, 600, readout);
        Assert.False(box.Overlaps(readout));
        Assert.Equal(new Magnifier.Box(264, 122, 120, 140), box);
    }

    [Fact]
    public void LoupeClampsWhenItCannotFit()
    {
        var box = Magnifier.Place(50, 50, 200, 200, 100, 100, null);
        Assert.Equal(new Magnifier.Box(4, 4, 200, 200), box);
    }

    static (byte R, byte G, byte B, byte A) ScaledAt(byte[] bgra, int cell, int x, int y)
    {
        int size = Magnifier.Cells * cell;
        int i = (y * size + x) * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i], bgra[i + 3]);
    }

    static (byte R, byte G, byte B, byte A) At(byte[] bgra, int col, int row)
    {
        int i = (row * Magnifier.Cells + col) * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i], bgra[i + 3]);
    }

    static CapturedFrame Frame(int width, int height, params (int X, int Y, byte R, byte G, byte B)[] pixels)
    {
        var data = new byte[width * height * 4];
        for (int i = 3; i < data.Length; i += 4) data[i] = 255;
        foreach (var pixel in pixels)
        {
            int i = (pixel.Y * width + pixel.X) * 4;
            data[i] = pixel.B;
            data[i + 1] = pixel.G;
            data[i + 2] = pixel.R;
            data[i + 3] = 255;
        }
        return new CapturedFrame(width, height, data);
    }
}
