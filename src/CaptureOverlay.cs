using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenSnip;

/// <summary>
/// Full-desktop overlay showing a frozen screenshot. Drag to select, click to grab the
/// window under the cursor, F for the whole monitor, A for every monitor, Esc to cancel.
/// </summary>
sealed class CaptureOverlay : Form
{
    /// <summary>Raised with the cropped image and its rectangle in screen coordinates.</summary>
    public event Action<Bitmap, Rectangle> Captured;

    /// <summary>Record mode: raised with the chosen area (screen coordinates) instead of <see cref="Captured"/>.</summary>
    public event Action<Rectangle> AreaChosen;

    string HintText => record
        ? "Drag the area to record   ·   Click a window   ·   F  full screen   ·   Esc  cancel"
        : "Drag to snip   ·   Click a window   ·   F  full screen   ·   Esc  cancel";
    const int Cells = 15; // magnifier grid size (odd, so there's a center pixel)

    readonly Rectangle vs;            // virtual screen, screen coordinates
    readonly Bitmap bright, dim;      // frozen desktop, client coordinates
    readonly List<Rectangle> windows; // screen coordinates, front to back
    readonly Rectangle[] monitors;    // client coordinates
    readonly float s;
    readonly int bw;
    readonly Font labelFont, hintFont;
    readonly int infoTextWidth;
    readonly bool record, magnifier;
    Color FrameColor => record ? Theme.Record : Theme.Accent2;

    bool mouseDown, dragging, done;
    Point dragStart, cursor;
    Rectangle selection, hover;

    readonly record struct Frame(Rectangle Sel, Rectangle Hint, Rectangle[] Dyn);

    public CaptureOverlay(Bitmap shot, Rectangle virtualScreen, List<Rectangle> windows, bool record = false, bool magnifier = true)
    {
        vs = virtualScreen;
        this.windows = windows;
        this.record = record;
        this.magnifier = magnifier;

        int w = shot.Width, h = shot.Height;
        bright = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bright))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            ImageOps.Blit(g, shot, 0, 0);
        }
        shot.Dispose();
        dim = bright.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(dim))
        using (var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            g.FillRectangle(shade, 0, 0, w, h);

        monitors = Screen.AllScreens.Select(sc => ToClient(sc.Bounds)).ToArray();
        cursor = ToClient(Cursor.Position);
        s = Native.ScaleAt(Cursor.Position);
        bw = Math.Max(1, (int)Math.Round(2 * s));
        labelFont = new Font("Segoe UI Semibold", 12f * s, GraphicsUnit.Pixel);
        hintFont = new Font("Segoe UI", 13f * s, GraphicsUnit.Pixel);
        infoTextWidth = TextRenderer.MeasureText(InfoText(new Point(-88888, -88888), Color.White), labelFont,
            Size.Empty, TextFormatFlags.NoPadding).Width;
        hover = HoverAt(cursor);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        Bounds = vs;
        Cursor = Cursors.Cross;
        Text = "OpenSnip";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // One window spans every monitor; never let WinForms rescale it when DPIs differ.
        if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle); // never show up in a recording that's running
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.PlaceTopmost(Handle, vs);
        Native.ForceForeground(Handle);
        Activate();
        Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        bright.Dispose();
        dim.Dispose();
        labelFont.Dispose();
        hintFont.Dispose();
    }

    // ---------- geometry ----------

    Rectangle ToClient(Rectangle r) => new(r.X - vs.X, r.Y - vs.Y, r.Width, r.Height);
    Point ToClient(Point p) => new(p.X - vs.X, p.Y - vs.Y);

    Rectangle ActiveRect => dragging ? selection : hover;

    Rectangle MonitorAt(Point c)
    {
        foreach (var m in monitors) if (m.Contains(c)) return m;
        return monitors.OrderBy(m => Distance(m, c)).First();
    }

    static int Distance(Rectangle r, Point p)
    {
        int dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right));
        int dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
        return dx + dy;
    }

    Rectangle HoverAt(Point c)
    {
        var sp = new Point(c.X + vs.X, c.Y + vs.Y);
        foreach (var w in windows)
        {
            if (!w.Contains(sp)) continue;
            var r = Rectangle.Intersect(w, vs);
            if (r.Width > 0 && r.Height > 0) return ToClient(r);
        }
        return MonitorAt(c);
    }

    static Rectangle FromPoints(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);

    Rectangle Outer(Rectangle r) => Rectangle.Inflate(r, bw + 1, bw + 1);

    static string LabelText(Rectangle r) => $"{r.Width} × {r.Height}";

    Rectangle LabelRect(Rectangle r)
    {
        var sz = TextRenderer.MeasureText(LabelText(r), labelFont, Size.Empty, TextFormatFlags.NoPadding);
        int padX = (int)(8 * s), padY = (int)(4 * s), gap = (int)(6 * s);
        var box = new Rectangle(r.X - bw, 0, sz.Width + 2 * padX, sz.Height + 2 * padY);
        var mon = MonitorAt(r.Location);
        box.Y = r.Y - bw - gap - box.Height;
        if (box.Y < mon.Top) box.Y = r.Y + bw + gap; // no room above: tuck inside
        if (box.Right > mon.Right) box.X = mon.Right - box.Width;
        if (box.X < mon.Left) box.X = mon.Left;
        return box;
    }

    // Cells are sized so the zoomed grid is at least as wide as the info strip under it.
    int Cell => Math.Max(Math.Max(6, (int)Math.Round(8 * s)),
                         (int)Math.Ceiling((infoTextWidth + SwatchSize + InfoPad * 3) / (double)Cells));

    int SwatchSize => (int)(10 * s);
    int InfoPad => (int)(7 * s);

    Rectangle MagnifierRect()
    {
        int size = Cells * Cell, infoH = (int)(24 * s), off = (int)(26 * s);
        int w = Math.Max(size, infoTextWidth + SwatchSize + InfoPad * 3);
        var mon = MonitorAt(cursor);
        int x = cursor.X + off, y = cursor.Y + off;
        if (x + w > mon.Right) x = cursor.X - off - w;
        if (y + size + infoH > mon.Bottom) y = cursor.Y - off - size - infoH;
        return new Rectangle(x, y, w, size + infoH);
    }

    Rectangle HintRect()
    {
        if (mouseDown) return Rectangle.Empty;
        var mon = MonitorAt(cursor);
        var sz = TextRenderer.MeasureText(HintText, hintFont, Size.Empty, TextFormatFlags.NoPadding);
        int padX = (int)(18 * s), padY = (int)(10 * s);
        var box = new Rectangle(0, mon.Y + (int)(28 * s), sz.Width + 2 * padX, sz.Height + 2 * padY);
        box.X = mon.X + (mon.Width - box.Width) / 2;
        if (Rectangle.Inflate(box, (int)(60 * s), (int)(60 * s)).Contains(cursor))
            box.Y = mon.Bottom - box.Height - (int)(80 * s);
        return box;
    }

    // ---------- incremental repaint ----------

    Frame Snapshot()
    {
        var sel = ActiveRect;
        var dyn = new List<Rectangle>();
        if (magnifier) dyn.Add(Rectangle.Inflate(MagnifierRect(), 2, 2));
        if (!sel.IsEmpty) dyn.Add(Rectangle.Inflate(LabelRect(sel), 1, 1));
        var hint = HintRect();
        return new Frame(sel, hint.IsEmpty ? hint : Rectangle.Inflate(hint, 2, 2), dyn.ToArray());
    }

    /// <summary>Repaints only what changed between two frames, in small pieces, so dragging stays smooth on huge desktops.</summary>
    void RepaintDiff(Frame a, Frame b)
    {
        using var region = new Region();
        region.MakeEmpty();
        if (a.Sel != b.Sel)
        {
            if (!a.Sel.IsEmpty) region.Union(Outer(a.Sel));
            if (!b.Sel.IsEmpty) region.Union(Outer(b.Sel));
            var keep = Rectangle.Intersect(a.Sel, b.Sel);
            if (keep.Width > 0 && keep.Height > 0) region.Exclude(keep);
        }
        if (a.Hint != b.Hint)
        {
            if (!a.Hint.IsEmpty) region.Union(a.Hint);
            if (!b.Hint.IsEmpty) region.Union(b.Hint);
        }
        foreach (var r in a.Dyn) region.Union(r);
        foreach (var r in b.Dyn) region.Union(r);

        using var identity = new Matrix();
        foreach (var rf in region.GetRegionScans(identity))
        {
            var r = Rectangle.Round(rf);
            if (r.Width <= 0 || r.Height <= 0) continue;
            Invalidate(r);
            Update();
        }
    }

    // ---------- input ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (done) return;
        var before = Snapshot();
        if (e.Button == MouseButtons.Right)
        {
            if (mouseDown) ResetDrag(); else Cancel();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        mouseDown = true;
        dragging = false;
        dragStart = e.Location;
        RepaintDiff(before, Snapshot());
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (done) return;
        var before = Snapshot();
        cursor = e.Location;
        if (mouseDown)
        {
            if (!dragging && (Math.Abs(e.X - dragStart.X) > 3 || Math.Abs(e.Y - dragStart.Y) > 3))
                dragging = true;
            if (dragging)
                selection = Rectangle.Intersect(FromPoints(dragStart, e.Location), new Rectangle(Point.Empty, ClientSize));
        }
        else hover = HoverAt(cursor);
        RepaintDiff(before, Snapshot());
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (done || e.Button != MouseButtons.Left || !mouseDown) return;
        mouseDown = false;
        if (!dragging) { Complete(hover); return; }
        if (selection.Width >= 2 && selection.Height >= 2) { Complete(selection); return; }
        ResetDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Normally keys arrive via the global hook (HandleKey); this is the fallback.
        HandleKey(e.KeyCode, e.Shift);
        e.Handled = true;
    }

    public void HandleKey(Keys key, bool shift)
    {
        if (done) return;
        int step = shift ? 10 : 1;
        switch (key)
        {
            case Keys.Escape:
                if (mouseDown) ResetDrag(); else Cancel();
                break;
            case Keys.Enter:
                Complete(ActiveRect);
                break;
            case Keys.F:
                Complete(MonitorAt(cursor));
                break;
            case Keys.A:
                Complete(record ? MonitorAt(cursor) : new Rectangle(Point.Empty, ClientSize)); // recordings are one monitor
                break;
            case Keys.Left: Nudge(-step, 0); break;
            case Keys.Right: Nudge(step, 0); break;
            case Keys.Up: Nudge(0, -step); break;
            case Keys.Down: Nudge(0, step); break;
        }
    }

    static void Nudge(int dx, int dy)
    {
        var p = Cursor.Position;
        Cursor.Position = new Point(p.X + dx, p.Y + dy);
    }

    void ResetDrag()
    {
        var before = Snapshot();
        mouseDown = false;
        dragging = false;
        Capture = false;
        hover = HoverAt(cursor);
        RepaintDiff(before, Snapshot());
    }

    void Complete(Rectangle r)
    {
        if (done) return;
        r = Rectangle.Intersect(r, new Rectangle(Point.Empty, bright.Size));
        if (r.Width < 1 || r.Height < 1) return;
        done = true;
        var screenRect = new Rectangle(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height);
        Hide();
        if (record) AreaChosen?.Invoke(screenRect);
        else
        {
            var bmp = bright.Clone(r, PixelFormat.Format24bppRgb);
            bmp.SetResolution(96, 96);
            Captured?.Invoke(bmp, screenRect);
        }
        Close();
    }

    void Cancel()
    {
        done = true;
        Close();
    }

    // ---------- painting ----------

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var clip = Rectangle.Intersect(e.ClipRectangle, new Rectangle(Point.Empty, bright.Size));
        if (clip.Width <= 0 || clip.Height <= 0) return;

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.DrawImage(dim, clip, clip.X, clip.Y, clip.Width, clip.Height, GraphicsUnit.Pixel);

        var sel = ActiveRect;
        if (!sel.IsEmpty)
        {
            var r = Rectangle.Intersect(sel, clip);
            if (r.Width > 0 && r.Height > 0)
                g.DrawImage(bright, r, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel);
        }

        g.CompositingMode = CompositingMode.SourceOver;
        if (!sel.IsEmpty) DrawFrame(g, sel);
        if (!mouseDown) DrawPill(g, HintRect(), HintText, hintFont, Color.FromArgb(225, 22, 22, 26), Theme.Text);
        if (magnifier) DrawMagnifier(g);
    }

    void DrawFrame(Graphics g, Rectangle sel)
    {
        var o = Rectangle.Inflate(sel, bw, bw);
        using (var b = new SolidBrush(FrameColor))
        {
            g.FillRectangle(b, o.X, o.Y, o.Width, bw);
            g.FillRectangle(b, o.X, sel.Bottom, o.Width, bw);
            g.FillRectangle(b, o.X, sel.Y, bw, sel.Height);
            g.FillRectangle(b, sel.Right, sel.Y, bw, sel.Height);
        }
        DrawPill(g, LabelRect(sel), LabelText(sel), labelFont, Color.FromArgb(230, 18, 18, 22), Color.White);
    }

    void DrawPill(Graphics g, Rectangle box, string text, Font font, Color back, Color fore)
    {
        if (box.IsEmpty) return;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = ImageOps.RoundedRect(box, box.Height / 2f))
        using (var b = new SolidBrush(back))
            g.FillPath(b, path);
        g.SmoothingMode = old;
        TextRenderer.DrawText(g, text, font, box, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    static string InfoText(Point screen, Color px) => $"{screen.X}, {screen.Y}   #{px.R:X2}{px.G:X2}{px.B:X2}";

    void DrawMagnifier(Graphics g)
    {
        var r = MagnifierRect();
        int cell = Cell, size = Cells * cell;
        var img = new Rectangle(r.X + (r.Width - size) / 2, r.Y, size, size);

        g.FillRectangle(Brushes.Black, r);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bright, img, new Rectangle(cursor.X - Cells / 2, cursor.Y - Cells / 2, Cells, Cells), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.None;

        int c0 = Cells / 2 * cell;
        using (var cross = new SolidBrush(Color.FromArgb(60, Theme.Accent2)))
        {
            g.FillRectangle(cross, img.X, img.Y + c0, c0, cell);
            g.FillRectangle(cross, img.X + c0 + cell, img.Y + c0, size - c0 - cell, cell);
            g.FillRectangle(cross, img.X + c0, img.Y, cell, c0);
            g.FillRectangle(cross, img.X + c0, img.Y + c0 + cell, cell, size - c0 - cell);
        }
        g.DrawRectangle(Pens.Black, img.X + c0 - 1, img.Y + c0 - 1, cell + 1, cell + 1);
        g.DrawRectangle(Pens.White, img.X + c0, img.Y + c0, cell - 1, cell - 1);

        // Info strip: screen position and pixel color.
        var px = new Rectangle(Point.Empty, bright.Size).Contains(cursor) ? bright.GetPixel(cursor.X, cursor.Y) : Color.Black;
        var info = new Rectangle(r.X, img.Bottom, r.Width, r.Height - size);
        int sw = SwatchSize, pad = InfoPad;
        using (var b = new SolidBrush(px))
            g.FillRectangle(b, info.X + pad, info.Y + (info.Height - sw) / 2, sw, sw);
        string text = InfoText(new Point(cursor.X + vs.X, cursor.Y + vs.Y), px);
        var textBox = new Rectangle(info.X + pad * 2 + sw, info.Y, info.Width - pad * 2 - sw, info.Height);
        TextRenderer.DrawText(g, text, labelFont, textBox, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        using var border = new Pen(Theme.Accent2);
        g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);
    }
}
