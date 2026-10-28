using System.Runtime.InteropServices;
using SkiaSharp;

namespace OpenSnip;

/// <summary>Annotations in image pixels. Geometry matches the Windows editor.</summary>
enum AnnotateTool { Arrow, Box, Pen, Highlight, Text, Blur }

static class AnnotateMath
{
    public static SKPoint Snap45(SKPoint a, SKPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new SKPoint(a.X + (float)(Math.Cos(ang) * len), a.Y + (float)(Math.Sin(ang) * len));
    }

    public static SKPoint Square(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, m = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new SKPoint(a.X + (dx < 0 ? -m : m), a.Y + (dy < 0 ? -m : m));
    }

    public static bool IsLight(SKColor color) =>
        (0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue) / 255 > 0.6;

    public static SKRect RectOf(SKPoint a, SKPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}

static class PixelBuffer
{
    public static SKBitmap BitmapFrom(int width, int height, byte[] bgra)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        CopyTo(bitmap, bgra);
        return bitmap;
    }

    public static void CopyTo(SKBitmap bitmap, byte[] bgra)
    {
        int tight = bitmap.Width * 4;
        int stride = bitmap.RowBytes;
        IntPtr ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("Could not lock the image");
        if (stride == tight)
            Marshal.Copy(bgra, 0, ptr, tight * bitmap.Height);
        else
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(bgra, y * tight, IntPtr.Add(ptr, y * stride), tight);
        }
        bitmap.NotifyPixelsChanged();
    }

    public static byte[] CopyFrom(SKBitmap bitmap)
    {
        int tight = bitmap.Width * 4;
        int stride = bitmap.RowBytes;
        var bytes = new byte[tight * bitmap.Height];
        IntPtr ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("Could not lock the image");
        if (stride == tight)
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
        else
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(ptr, y * stride), bytes, y * tight, tight);
        }
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        return bytes;
    }
}

sealed class AnnotationDocument
{
    readonly byte[] _base;
    readonly List<Annotation> _shapes = new();
    readonly Stack<Annotation> _redo = new();

    public int Width { get; }
    public int Height { get; }
    public bool CanUndo => _shapes.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public AnnotationDocument(CapturedFrame frame)
    {
        Width = frame.Width;
        Height = frame.Height;
        _base = (byte[])frame.Bgra.Clone();
    }

    public void Commit(Annotation shape)
    {
        _shapes.Add(shape);
        _redo.Clear();
    }

    public bool Undo()
    {
        if (_shapes.Count == 0) return false;
        _redo.Push(_shapes[^1]);
        _shapes.RemoveAt(_shapes.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _shapes.Add(_redo.Pop());
        return true;
    }

    public CapturedFrame Render(Annotation preview = null)
    {
        using var bitmap = PixelBuffer.BitmapFrom(Width, Height, _base);
        foreach (var shape in _shapes)
            shape.Apply(bitmap);
        if (preview != null)
        {
            using var canvas = new SKCanvas(bitmap);
            preview.DrawPreview(canvas);
        }
        return new CapturedFrame(Width, Height, PixelBuffer.CopyFrom(bitmap));
    }
}

abstract class Annotation
{
    public SKColor Color;
    public float Width;

    public abstract bool IsEmpty { get; }

    public virtual void Apply(SKBitmap bitmap)
    {
        using var canvas = new SKCanvas(bitmap);
        Draw(canvas);
    }

    public abstract void Draw(SKCanvas canvas);
    public virtual void DrawPreview(SKCanvas canvas) => Draw(canvas);

    protected SKPaint Fill(SKColor color) => new()
    {
        Color = color,
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    protected SKPaint Stroke(SKColor color, float width, SKStrokeCap cap)
    {
        return new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = cap,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
    }
}

abstract class TwoPointAnnotation : Annotation
{
    public SKPoint A, B;
    public override bool IsEmpty => Math.Abs(A.X - B.X) < 3 || Math.Abs(A.Y - B.Y) < 3;
}

sealed class StrokeAnnotation : Annotation
{
    public readonly List<SKPoint> Points = new();
    public bool Highlighter;

    public override bool IsEmpty => Points.Count == 0;

    public override void Draw(SKCanvas canvas)
    {
        if (Points.Count == 0) return;
        var color = Highlighter ? Color.WithAlpha(110) : Color;
        var cap = Highlighter ? SKStrokeCap.Square : SKStrokeCap.Round;
        if (Points.Count == 1 || Points.TrueForAll(p => p == Points[0]))
        {
            var p = Points[0];
            using var fill = Fill(color);
            if (Highlighter)
                canvas.DrawRect(SKRect.Create(p.X - Width / 2, p.Y - Width / 2, Width, Width), fill);
            else
                canvas.DrawCircle(p.X, p.Y, Width / 2f, fill);
            return;
        }

        using var path = new SKPath();
        path.MoveTo(Points[0]);
        for (int i = 1; i < Points.Count; i++) path.LineTo(Points[i]);
        using var pen = Stroke(color, Width, cap);
        canvas.DrawPath(path, pen);
    }
}

sealed class ArrowAnnotation : TwoPointAnnotation
{
    public override bool IsEmpty => Math.Abs(A.X - B.X) + Math.Abs(A.Y - B.Y) < 5;

    public override void Draw(SKCanvas canvas)
    {
        float dx = B.X - A.X, dy = B.Y - A.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float ux = dx / len, uy = dy / len;
        float head = Math.Min(len * 0.7f, Width * 3.4f + 8);
        float half = head * 0.55f;
        var basePt = new SKPoint(B.X - ux * head, B.Y - uy * head);
        var tip = B;
        var left = new SKPoint(basePt.X - uy * half, basePt.Y + ux * half);
        var right = new SKPoint(basePt.X + uy * half, basePt.Y - ux * half);
        var shaftEnd = new SKPoint(B.X - ux * head * 0.8f, B.Y - uy * head * 0.8f);

        using (var pen = Stroke(Color, Width, SKStrokeCap.Round))
        {
            pen.StrokeCap = SKStrokeCap.Round;
            canvas.DrawLine(A, shaftEnd, pen);
        }

        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(left);
        path.LineTo(right);
        path.Close();
        using (var fill = Fill(Color))
            canvas.DrawPath(path, fill);
        using (var soft = Stroke(Color, Math.Max(1f, Width * 0.4f), SKStrokeCap.Round))
            canvas.DrawPath(path, soft);
    }
}

sealed class BoxAnnotation : TwoPointAnnotation
{
    public override void Draw(SKCanvas canvas)
    {
        var rect = AnnotateMath.RectOf(A, B);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var path = RoundRect(rect, Width);
        using var pen = Stroke(Color, Width, SKStrokeCap.Round);
        canvas.DrawPath(path, pen);
    }

    static SKPath RoundRect(SKRect rect, float radius)
    {
        var path = new SKPath();
        float r = Math.Min(Math.Max(0, radius), Math.Min(rect.Width, rect.Height) / 2f);
        if (r <= 0.5f) path.AddRect(rect);
        else path.AddRoundRect(rect, r, r);
        return path;
    }
}

/// <summary>Pixelates a region. Width is the block size, and at least 4.</summary>
sealed class BlurAnnotation : TwoPointAnnotation
{
    public override void Draw(SKCanvas canvas) { }

    public override void DrawPreview(SKCanvas canvas)
    {
        var rect = AnnotateMath.RectOf(A, B);
        using (var fill = Fill(new SKColor(120, 120, 130, 110)))
            canvas.DrawRect(rect, fill);
        using var dash = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0);
        using var pen = Stroke(SKColors.White, 1.5f, SKStrokeCap.Butt);
        pen.PathEffect = dash;
        canvas.DrawRect(rect, pen);
    }

    public override void Apply(SKBitmap bitmap)
    {
        int left = Round(Math.Min(A.X, B.X));
        int top = Round(Math.Min(A.Y, B.Y));
        int right = Round(Math.Max(A.X, B.X));
        int bottom = Round(Math.Max(A.Y, B.Y));
        left = Math.Clamp(left, 0, bitmap.Width);
        top = Math.Clamp(top, 0, bitmap.Height);
        right = Math.Clamp(right, 0, bitmap.Width);
        bottom = Math.Clamp(bottom, 0, bitmap.Height);
        int width = right - left, height = bottom - top;
        if (width < 2 || height < 2) return;

        int block = Math.Max(4, (int)Width);
        int sw = Math.Max(1, (width + block - 1) / block);
        int sh = Math.Max(1, (height + block - 1) / block);
        var source = new SKRect(left, top, right, bottom);
        var smallRect = new SKRect(0, 0, sw, sh);

        using var small = new SKBitmap(new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Opaque));
        small.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(small))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            canvas.DrawBitmap(bitmap, source, smallRect, paint);

        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
            BlendMode = SKBlendMode.Src,
        })
            canvas.DrawBitmap(small, smallRect, source, paint);
    }

    static int Round(float value) => (int)Math.Round(value);
}

sealed class TextAnnotation : Annotation
{
    public SKPoint P;
    public string Text = "";

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public override void Draw(SKCanvas canvas)
    {
        if (IsEmpty) return;
        using var typeface = AnnotationFont.Bold();
        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = Math.Max(1, Width),
            IsAntialias = true,
            TextAlign = SKTextAlign.Left,
            Style = SKPaintStyle.Fill,
        };
        paint.GetFontMetrics(out var metrics);
        float lineHeight = Math.Max(1, metrics.Descent - metrics.Ascent + metrics.Leading);
        float baseline = P.Y - metrics.Ascent;
        var outline = AnnotateMath.IsLight(Color)
            ? new SKColor(0, 0, 0, 210)
            : new SKColor(255, 255, 255, 235);
        float stroke = Math.Max(2f, Width / 6f);
        var lines = Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = stroke;
                paint.StrokeJoin = SKStrokeJoin.Round;
                paint.Color = outline;
                canvas.DrawText(line, P.X, baseline, paint);
                paint.Style = SKPaintStyle.Fill;
                paint.Color = Color;
                canvas.DrawText(line, P.X, baseline, paint);
            }
            baseline += lineHeight;
        }
    }
}

static class AnnotationFont
{
    public static SKTypeface Bold()
    {
        foreach (var name in new[] { "DejaVu Sans", "Liberation Sans", "Noto Sans" })
        {
            var face = SKTypeface.FromFamilyName(name, SKFontStyle.Bold);
            if (face != null) return face;
        }
        return SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
    }
}
