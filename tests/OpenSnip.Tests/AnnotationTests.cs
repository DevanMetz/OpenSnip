using SkiaSharp;

namespace OpenSnip.Tests;

public class AnnotationTests
{
    [Fact]
    public void Render_without_shapes_keeps_every_pixel()
    {
        var frame = Pattern(17, 9);
        var back = new AnnotationDocument(frame).Render();
        Assert.Equal(frame.Bgra, back.Bgra);
    }

    [Fact]
    public void Snap45_and_square_match_the_windows_editor()
    {
        var flat = AnnotateMath.Snap45(new SKPoint(0, 0), new SKPoint(3, 1));
        Assert.InRange(flat.X, 3.1, 3.2);
        Assert.Equal(0, flat.Y, 2);

        var diagonal = AnnotateMath.Snap45(new SKPoint(0, 0), new SKPoint(10, 10));
        Assert.Equal(diagonal.X, diagonal.Y, 2);

        var vertical = AnnotateMath.Snap45(new SKPoint(2, 2), new SKPoint(2, 12));
        Assert.Equal(2, vertical.X, 2);
        Assert.Equal(12, vertical.Y, 2);

        var square = AnnotateMath.Square(new SKPoint(0, 0), new SKPoint(3, 1));
        Assert.Equal(new SKPoint(3, 3), square);
        var flipped = AnnotateMath.Square(new SKPoint(5, 5), new SKPoint(-1, 2));
        Assert.Equal(new SKPoint(-1, -1), flipped);
    }

    [Fact]
    public void Empty_shapes_are_rejected_by_the_same_thresholds()
    {
        Assert.True(new ArrowAnnotation { A = new SKPoint(0, 0), B = new SKPoint(2, 2) }.IsEmpty);
        Assert.False(new ArrowAnnotation { A = new SKPoint(0, 0), B = new SKPoint(8, 0) }.IsEmpty);
        Assert.True(new BoxAnnotation { A = new SKPoint(0, 0), B = new SKPoint(10, 2) }.IsEmpty);
        Assert.False(new BoxAnnotation { A = new SKPoint(0, 0), B = new SKPoint(10, 10) }.IsEmpty);
        Assert.True(new StrokeAnnotation().IsEmpty);
        Assert.True(new TextAnnotation { Text = "  \n" }.IsEmpty);
        Assert.False(new TextAnnotation { Text = "A" }.IsEmpty);
    }

    [Fact]
    public void Arrow_box_and_pen_change_only_their_pixels()
    {
        var frame = Solid(120, 80, 255, 255, 255);
        var doc = new AnnotationDocument(frame);
        doc.Commit(new ArrowAnnotation
        {
            A = new SKPoint(8, 30),
            B = new SKPoint(110, 30),
            Color = new SKColor(235, 64, 52),
            Width = 6,
        });
        doc.Commit(new BoxAnnotation
        {
            A = new SKPoint(20, 48),
            B = new SKPoint(90, 72),
            Color = new SKColor(52, 199, 89),
            Width = 4,
        });
        doc.Commit(new StrokeAnnotation
        {
            Points = { new SKPoint(16, 8), new SKPoint(16, 22) },
            Color = new SKColor(20, 20, 20),
            Width = 6,
        });

        var drawn = doc.Render();
        var shaft = Pixel(drawn, 60, 30);
        Assert.True(shaft.R > 180 && shaft.G < 120 && shaft.B < 120);
        var edge = Pixel(drawn, 40, 48);
        Assert.True(edge.G > edge.R && edge.G > 100);
        var inside = Pixel(drawn, 50, 60);
        Assert.Equal((byte)255, inside.R);
        Assert.Equal((byte)255, inside.G);
        var pen = Pixel(drawn, 16, 14);
        Assert.True(pen.R < 80 && pen.G < 80 && pen.B < 80);
        Assert.Equal((byte)255, Pixel(drawn, 119, 79).R);
    }

    [Fact]
    public void Highlighter_blends_instead_of_replacing()
    {
        var frame = Solid(80, 40, 255, 255, 255);
        var doc = new AnnotationDocument(frame);
        doc.Commit(new StrokeAnnotation
        {
            Highlighter = true,
            Points = { new SKPoint(8, 20), new SKPoint(72, 20) },
            Color = new SKColor(255, 214, 10),
            Width = 16,
        });
        var pixel = Pixel(doc.Render(), 40, 20);
        Assert.True(pixel.R > 230);
        Assert.InRange(pixel.B, 80, 220);
        Assert.Equal((byte)255, pixel.A);
    }

    [Fact]
    public void Redact_pixelates_inside_the_region_only()
    {
        const int size = 40;
        var data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            bool red = x < size / 2;
            data[i] = red ? (byte)0 : (byte)255;
            data[i + 1] = 0;
            data[i + 2] = red ? (byte)255 : (byte)0;
            data[i + 3] = 255;
        }
        var frame = new CapturedFrame(size, size, data);
        var doc = new AnnotationDocument(frame);
        doc.Commit(new BlurAnnotation
        {
            A = new SKPoint(12, 12),
            B = new SKPoint(28, 28),
            Width = 16,
        });
        var drawn = doc.Render();
        var left = Pixel(drawn, 14, 18);
        var right = Pixel(drawn, 25, 18);
        Assert.InRange(Math.Abs(left.R - right.R), 0, 8);
        Assert.InRange(Math.Abs(left.B - right.B), 0, 8);
        Assert.True(left.R > 40 && left.B > 40);
        Assert.Equal((byte)255, Pixel(drawn, 2, 2).R);
        Assert.Equal((byte)0, Pixel(drawn, 2, 2).B);
        Assert.Equal((byte)0, Pixel(drawn, 38, 2).R);
        Assert.Equal((byte)255, Pixel(drawn, 38, 2).B);
    }

    [Fact]
    public void Text_paints_glyphs_and_leaves_the_far_corner()
    {
        var frame = Solid(160, 80, 255, 255, 255);
        var doc = new AnnotationDocument(frame);
        doc.Commit(new TextAnnotation
        {
            P = new SKPoint(8, 10),
            Text = "Ag",
            Width = 42,
            Color = new SKColor(20, 20, 20),
        });
        var drawn = doc.Render();
        Assert.True(CountDark(drawn, 8, 10, 100, 55) > 20);
        Assert.Equal((byte)255, Pixel(drawn, 159, 79).R);
        Assert.Equal((byte)255, Pixel(drawn, 159, 79).A);
    }

    [Fact]
    public void Undo_and_redo_restore_pixels()
    {
        var frame = Solid(100, 70, 255, 255, 255);
        var doc = new AnnotationDocument(frame);
        doc.Commit(ArrowAt(20));
        doc.Commit(ArrowAt(50));
        var both = doc.Render();
        Assert.True(Pixel(both, 60, 20).R > 180);
        Assert.True(Pixel(both, 60, 50).R > 180);

        Assert.True(doc.Undo());
        var one = doc.Render();
        Assert.True(Pixel(one, 60, 20).R > 180);
        Assert.Equal((byte)255, Pixel(one, 60, 50).R);

        Assert.True(doc.Undo());
        Assert.Equal(frame.Bgra, doc.Render().Bgra);
        Assert.False(doc.Undo());

        Assert.True(doc.Redo());
        Assert.True(Pixel(doc.Render(), 60, 20).R > 180);
        Assert.Equal((byte)255, Pixel(doc.Render(), 60, 50).R);
    }

    [Theory]
    [InlineData(255, 255, 255, true)]
    [InlineData(255, 214, 10, true)]
    [InlineData(235, 64, 52, false)]
    [InlineData(20, 20, 20, false)]
    public void Light_colors_get_a_dark_outline(byte r, byte g, byte b, bool light)
    {
        Assert.Equal(light, AnnotateMath.IsLight(new SKColor(r, g, b)));
    }

    static ArrowAnnotation ArrowAt(float y) => new()
    {
        A = new SKPoint(8, y),
        B = new SKPoint(92, y),
        Color = new SKColor(235, 64, 52),
        Width = 6,
    };

    static CapturedFrame Solid(int width, int height, byte b, byte g, byte r)
    {
        var data = new byte[width * height * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = 255;
        }
        return new CapturedFrame(width, height, data);
    }

    static CapturedFrame Pattern(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            data[i] = (byte)(x * 13);
            data[i + 1] = (byte)(y * 17);
            data[i + 2] = (byte)(x + y * 3);
            data[i + 3] = 255;
        }
        return new CapturedFrame(width, height, data);
    }

    static (byte B, byte G, byte R, byte A) Pixel(CapturedFrame frame, int x, int y)
    {
        int i = (y * frame.Width + x) * 4;
        return (frame.Bgra[i], frame.Bgra[i + 1], frame.Bgra[i + 2], frame.Bgra[i + 3]);
    }

    static int CountDark(CapturedFrame frame, int x, int y, int width, int height)
    {
        int count = 0;
        for (int py = y; py < y + height; py++)
        for (int px = x; px < x + width; px++)
        {
            var pixel = Pixel(frame, px, py);
            if (pixel.R < 80 && pixel.G < 80 && pixel.B < 80) count++;
        }
        return count;
    }
}
