using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenSnip;

enum Tool { Arrow, Box, Pen, Highlight, Text, Blur }

/// <summary>An annotation, stored in image pixel coordinates.</summary>
abstract class Shape
{
    public Color Color;
    public float Width;

    public abstract bool IsEmpty { get; }
    public abstract void Draw(Graphics g);
    public virtual void DrawPreview(Graphics g) => Draw(g);

    public virtual void Render(Bitmap target)
    {
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        Draw(g);
    }

    public static PointF Snap45(PointF a, PointF b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new PointF(a.X + (float)(Math.Cos(ang) * len), a.Y + (float)(Math.Sin(ang) * len));
    }

    public static PointF Square(PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, m = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new PointF(a.X + (dx < 0 ? -m : m), a.Y + (dy < 0 ? -m : m));
    }

    public static RectangleF RectOf(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}

abstract class TwoPointShape : Shape
{
    public PointF A, B;
    public override bool IsEmpty => Math.Abs(A.X - B.X) < 3 || Math.Abs(A.Y - B.Y) < 3;
}

sealed class StrokeShape : Shape
{
    public readonly List<PointF> Points = new();
    public bool Highlighter;

    public override bool IsEmpty => Points.Count == 0;

    public override void Draw(Graphics g)
    {
        var c = Highlighter ? Color.FromArgb(110, Color) : Color;
        if (Points.Count == 1 || Points.All(p => p == Points[0]))
        {
            var p = Points[0];
            using var b = new SolidBrush(c);
            if (Highlighter) g.FillRectangle(b, p.X - Width / 2, p.Y - Width / 2, Width, Width);
            else g.FillEllipse(b, p.X - Width / 2, p.Y - Width / 2, Width, Width);
            return;
        }
        var cap = Highlighter ? LineCap.Square : LineCap.Round;
        using var pen = new Pen(c, Width) { StartCap = cap, EndCap = cap, LineJoin = LineJoin.Round };
        using var path = new GraphicsPath();
        path.AddLines(Points.ToArray());
        g.DrawPath(pen, path);
    }
}

sealed class ArrowShape : TwoPointShape
{
    public override bool IsEmpty => Math.Abs(A.X - B.X) + Math.Abs(A.Y - B.Y) < 5;

    public override void Draw(Graphics g)
    {
        float dx = B.X - A.X, dy = B.Y - A.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float ux = dx / len, uy = dy / len;
        float head = Math.Min(len * 0.7f, Width * 3.4f + 8);
        float half = head * 0.55f;
        var basePt = new PointF(B.X - ux * head, B.Y - uy * head);
        var tri = new[]
        {
            B,
            new PointF(basePt.X - uy * half, basePt.Y + ux * half),
            new PointF(basePt.X + uy * half, basePt.Y - ux * half),
        };
        var shaftEnd = new PointF(B.X - ux * head * 0.8f, B.Y - uy * head * 0.8f);

        using (var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            g.DrawLine(pen, A, shaftEnd);
        using (var b = new SolidBrush(Color))
            g.FillPolygon(b, tri);
        using (var soft = new Pen(Color, Math.Max(1f, Width * 0.4f)) { LineJoin = LineJoin.Round })
            g.DrawPolygon(soft, tri);
    }
}

sealed class BoxShape : TwoPointShape
{
    public override void Draw(Graphics g)
    {
        using var pen = new Pen(Color, Width) { LineJoin = LineJoin.Round };
        using var path = ImageOps.RoundedRect(RectOf(A, B), Width);
        g.DrawPath(pen, path);
    }
}

/// <summary>Pixelates a region. Width is the block size.</summary>
sealed class BlurShape : TwoPointShape
{
    public override void Draw(Graphics g) { }

    public override void DrawPreview(Graphics g)
    {
        var r = RectOf(A, B);
        using (var fill = new SolidBrush(Color.FromArgb(110, 120, 120, 130)))
            g.FillRectangle(fill, r);
        using var pen = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    public override void Render(Bitmap target)
    {
        var r = Rectangle.Intersect(Rectangle.Round(RectOf(A, B)), new Rectangle(0, 0, target.Width, target.Height));
        if (r.Width < 2 || r.Height < 2) return;
        int block = Math.Max(4, (int)Width);
        int sw = Math.Max(1, (r.Width + block - 1) / block), sh = Math.Max(1, (r.Height + block - 1) / block);

        using var small = new Bitmap(sw, sh, PixelFormat.Format24bppRgb);
        using (var attrs = new ImageAttributes())
        using (var g = Graphics.FromImage(small))
        {
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(target, new Rectangle(0, 0, sw, sh), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, attrs);
        }
        using (var g = Graphics.FromImage(target))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(small, r, 0, 0, sw, sh, GraphicsUnit.Pixel);
        }
    }
}

/// <summary>Bold text with a contrasting outline so it reads on any background. Width is the font size in px.</summary>
sealed class TextShape : Shape
{
    public PointF P;
    public string Text = "";

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.6;

    public override void Draw(Graphics g)
    {
        using var ff = new FontFamily("Segoe UI");
        using var path = new GraphicsPath();
        path.AddString(Text, ff, (int)FontStyle.Bold, Width, P, StringFormat.GenericTypographic);
        var outline = IsLight(Color) ? Color.FromArgb(210, 0, 0, 0) : Color.FromArgb(235, 255, 255, 255);
        using (var pen = new Pen(outline, Math.Max(2f, Width / 6f)) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, path);
        using var b = new SolidBrush(Color);
        g.FillPath(b, path);
    }
}
