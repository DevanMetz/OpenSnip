using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace OpenSnip;

sealed class CapturedFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public CapturedFrame(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Invalid image size");
        if (bgra == null || bgra.Length < width * height * 4) throw new ArgumentException("Invalid image data");
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public CapturedFrame Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        width = Math.Clamp(width, 1, Width - x);
        height = Math.Clamp(height, 1, Height - y);
        var data = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(Bgra, ((y + row) * Width + x) * 4, data, row * width * 4, width * 4);
        return new CapturedFrame(width, height, data);
    }

    public WriteableBitmap ToBitmap()
    {
        var bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        int rowBytes = Width * 4;
        for (int y = 0; y < Height; y++)
            Marshal.Copy(Bgra, y * rowBytes, IntPtr.Add(buffer.Address, y * buffer.RowBytes), rowBytes);
        return bitmap;
    }

    public byte[] EncodePng() => Encode(SKEncodedImageFormat.Png, 100);
    public byte[] EncodeJpeg() => Encode(SKEncodedImageFormat.Jpeg, 92);

    public bool TryGetRgb(int x, int y, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        int i = (y * Width + x) * 4;
        b = Bgra[i];
        g = Bgra[i + 1];
        r = Bgra[i + 2];
        return true;
    }

    public string ColorLabel(int x, int y)
    {
        if (!TryGetRgb(x, y, out byte r, out byte g, out byte b)) return $"{x}, {y}";
        return $"{x}, {y}  #{r:X2}{g:X2}{b:X2}";
    }

    byte[] Encode(SKEncodedImageFormat format, int quality)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(Bgra, 0, bitmap.GetPixels(), Bgra.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        if (data == null) throw new InvalidOperationException("Image encoding failed");
        return data.ToArray();
    }

    public void Save(string path, string format)
    {
        var bytes = IsJpeg(format) ? EncodeJpeg() : EncodePng();
        File.WriteAllBytes(path, bytes);
    }

    public static bool IsJpeg(string format)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;
        format = format.Trim();
        if (format.StartsWith('.')) format = format[1..];
        return format.Equals("jpg", StringComparison.OrdinalIgnoreCase)
            || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public static CapturedFrame DecodeFile(string path)
    {
        using var source = SKBitmap.Decode(path) ?? throw new InvalidOperationException("Could not decode the captured image");
        if (source.Width <= 0 || source.Height <= 0) throw new InvalidOperationException("The captured image is empty");
        using var converted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(converted))
            canvas.DrawBitmap(source, 0, 0);
        converted.NotifyPixelsChanged();
        var bytes = new byte[source.Width * source.Height * 4];
        Marshal.Copy(converted.GetPixels(), bytes, 0, bytes.Length);
        return new CapturedFrame(source.Width, source.Height, bytes);
    }

    public static string UniquePath(string folder, string format)
    {
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
        string ext = string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
        string path = Path.Combine(folder, $"Snip {stamp}.{ext}");
        int n = 2;
        while (File.Exists(path)) path = Path.Combine(folder, $"Snip {stamp} ({n++}).{ext}");
        return path;
    }
}
