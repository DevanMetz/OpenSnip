namespace OpenSnip.Tests;

public class ClipboardImageTests
{
    [Fact]
    public void Image_type_prefers_png_then_the_other_bitmap_types()
    {
        Assert.Null(ClipboardService.ChooseImageType(null));
        Assert.Null(ClipboardService.ChooseImageType(new[] { "text/plain" }));
        Assert.Equal("image/png", ClipboardService.ChooseImageType(new[] { "image/jpeg", "image/png", "text/plain" }));
        Assert.Equal("image/jpeg", ClipboardService.ChooseImageType(new[] { " IMAGE/JPEG ", "image/gif" }));
        Assert.Equal("image/bmp", ClipboardService.ChooseImageType(new[] { "image/gif", "image/bmp", "image/webp" }));
        Assert.Equal("image/webp", ClipboardService.ChooseImageType(new[] { "image/webp" }));
    }

    [Fact]
    public void Decode_bytes_round_trips_a_png()
    {
        var source = new CapturedFrame(2, 1, new byte[]
        {
            10, 20, 30, 255,
            40, 50, 60, 255,
        });
        var decoded = CapturedFrame.DecodeBytes(source.EncodePng());
        Assert.Equal(source.Width, decoded.Width);
        Assert.Equal(source.Height, decoded.Height);
        Assert.Equal(source.Bgra, decoded.Bgra);
    }

    [Fact]
    public void Decode_bytes_rejects_empty_input()
    {
        Assert.Throws<InvalidOperationException>(() => CapturedFrame.DecodeBytes(null));
        Assert.Throws<InvalidOperationException>(() => CapturedFrame.DecodeBytes(Array.Empty<byte>()));
        Assert.Throws<InvalidOperationException>(() => CapturedFrame.DecodeBytes(new byte[] { 1, 2, 3 }));
    }
}
