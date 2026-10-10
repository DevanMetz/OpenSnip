using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenSnip;

/// <summary>Borderless, non-activating popup that never steals focus from what you're doing.</summary>
abstract class Popup : Form
{
    protected readonly float s;

    protected Popup(Point near)
    {
        s = Native.ScaleAt(near);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Theme.Back;
    }

    protected override bool ShowWithoutActivation => true;

    protected virtual bool ClickThrough => false;
    protected virtual int CornerPreference => Native.DWMWCP_ROUND;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            if (ClickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetCorners(Handle, CornerPreference);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
    }
}

/// <summary>"Snipping in 3…" pill shown for delayed snips. Click-through.</summary>
sealed class CountdownForm : Popup
{
    public event Action Finished;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly Font font;
    int left;

    public CountdownForm(int seconds) : base(Cursor.Position)
    {
        left = seconds;
        font = new Font("Segoe UI Semibold", 15f * s, GraphicsUnit.Pixel);
        Opacity = 0.92;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size((int)(210 * s), (int)(48 * s));
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(40 * s));
        timer.Tick += Tick;
        timer.Start();
    }

    protected override bool ClickThrough => true;

    async void Tick(object sender, EventArgs e)
    {
        left--;
        if (left > 0) { Invalidate(); return; }
        timer.Stop();
        Hide();
        await Task.Delay(120); // let the compositor drop us before the screen is captured
        Finished?.Invoke();
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Back);
        TextRenderer.DrawText(e.Graphics, $"Snipping in {left}…", font, ClientRectangle, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Dispose();
        font.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>
/// Little preview in the corner after a snip. Click to annotate, drag it into any app to
/// drop the file, right-click to dismiss. Fades away on its own.
/// </summary>
sealed class ThumbnailForm : Popup
{
    readonly Bitmap img;
    readonly string path;
    readonly Rectangle source;
    readonly bool video, copied;
    readonly int lifetimeMs;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    readonly Font font;
    readonly Rectangle imgRect;
    int lifeMs;
    bool hovering, pressed;
    Point pressAt;

    /// <param name="video">True when <paramref name="path"/> is a recording; <paramref name="img"/> is then just a still of it.</param>
    public ThumbnailForm(Bitmap img, string path, Rectangle source, bool copied, int seconds, bool video = false)
        : base(Center(source))
    {
        this.img = img;
        this.path = path;
        this.source = source;
        this.copied = copied;
        this.video = video;
        lifetimeMs = Math.Max(1, seconds) * 1000;
        font = new Font("Segoe UI", 11.5f * s, GraphicsUnit.Pixel);

        float maxW = 280 * s, maxH = 180 * s;
        float k = Math.Min(1f, Math.Min(maxW / img.Width, maxH / img.Height));
        int iw = Math.Max(1, (int)(img.Width * k)), ih = Math.Max(1, (int)(img.Height * k));
        int pad = (int)(6 * s), barH = (int)(26 * s);
        int w = Math.Max(iw + 2 * pad, (int)(200 * s));
        imgRect = new Rectangle((w - iw) / 2, pad, iw, ih);
        Size = new Size(w, ih + 2 * pad + barH);

        var wa = Screen.FromRectangle(source).WorkingArea;
        int m = (int)(16 * s);
        Location = new Point(wa.Right - Width - m, wa.Bottom - Height - m);
        Cursor = Cursors.Hand;

        timer.Tick += Tick;
        timer.Start();
    }

    static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    void Tick(object sender, EventArgs e)
    {
        if (hovering || pressed) { lifeMs = 0; Opacity = 1; return; }
        lifeMs += timer.Interval;
        if (lifeMs < lifetimeMs) return;
        Opacity -= 0.08;
        if (Opacity <= 0.05) Close();
    }

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { pressed = true; pressAt = e.Location; }
        else Close();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!pressed) return;
        var d = SystemInformation.DragSize;
        if (Math.Abs(e.X - pressAt.X) <= d.Width && Math.Abs(e.Y - pressAt.Y) <= d.Height) return;
        pressed = false;
        try
        {
            var data = video ? ImageOps.FileData(path) : ImageOps.DragData(img, path);
            var result = DoDragDrop(data, DragDropEffects.Copy);
            if (result != DragDropEffects.None) Close();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed || e.Button != MouseButtons.Left) return;
        pressed = false;
        if (video)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error(ex); }
        }
        else TrayApp.Instance.OpenEditor((Bitmap)img.Clone(), source);
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(img, imgRect, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
        using (var p = new Pen(Color.FromArgb(70, 255, 255, 255)))
            g.DrawRectangle(p, imgRect.X - 1, imgRect.Y - 1, imgRect.Width + 1, imgRect.Height + 1);
        if (video) DrawPlayBadge(g);

        var bar = new Rectangle(0, imgRect.Bottom + (int)(4 * s), Width, Height - imgRect.Bottom - (int)(4 * s));
        string text = hovering
            ? $"Click to {(video ? "play" : "edit")}  ·  Drag to drop  ·  Right-click to close"
            : video ? (copied ? "✓ Recording saved & copied" : "✓ Recording saved")
            : copied ? "✓ Copied to clipboard" : path != null ? "✓ Saved" : "Click to edit";
        var color = hovering ? Theme.Text : Theme.Good;
        TextRenderer.DrawText(g, text, font, bar, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    void DrawPlayBadge(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = Math.Min(44 * s, Math.Min(imgRect.Width, imgRect.Height) * 0.6f);
        var c = new PointF(imgRect.X + imgRect.Width / 2f, imgRect.Y + imgRect.Height / 2f);
        using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            g.FillEllipse(b, c.X - d / 2, c.Y - d / 2, d, d);
        float t = d * 0.22f;
        g.FillPolygon(Brushes.White, new[]
        {
            new PointF(c.X - t * 0.8f, c.Y - t), new PointF(c.X - t * 0.8f, c.Y + t), new PointF(c.X + t * 1.1f, c.Y),
        });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Dispose();
        font.Dispose();
        img.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>
/// An always-on-top floating copy of an image. Drag to move, drag a corner or edge to resize
/// (Shift to stretch freely), scroll to zoom, Ctrl+scroll for opacity, double-click or Esc
/// to close, right-click for more.
/// </summary>
sealed class PinForm : Form
{
    const int WM_NCHITTEST = 0x84, WM_SIZING = 0x214, HTCLIENT = 1;

    readonly Bitmap img;
    readonly float s;
    float zoom = 1f;
    bool dragging;
    Point grab;

    public PinForm(Bitmap img, Point location)
    {
        this.img = img;
        s = Native.ScaleAt(location);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "OpenSnip pin";
        Location = new Point(location.X - 1, location.Y - 1);
        ClientSize = new Size(img.Width + 2, img.Height + 2);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, (_, _) => ImageOps.CopyToClipboard(img));
        menu.Items.Add("Save as…", null, (_, _) => ImageOps.SaveAs(this, img, TrayApp.Instance.Settings.SaveFolder));
        menu.Items.Add("Edit", null, (_, _) => { TrayApp.Instance.OpenEditor((Bitmap)img.Clone(), null); Close(); });
        menu.Items.Add("Actual size", null, (_, _) => SetZoom(1f, new Point(Width / 2, Height / 2)));        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close", null, (_, _) => Close());
        ContextMenuStrip = menu;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            cp.ClassStyle |= Native.CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetCorners(Handle, Native.DWMWCP_DONOTROUND);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_DPICHANGED:
                m.Result = IntPtr.Zero;
                return;
            case WM_NCHITTEST:
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT) m.Result = (IntPtr)HitTest(m.LParam);
                return;
            case WM_SIZING:
                if ((ModifierKeys & Keys.Shift) == 0) KeepAspect(m.WParam.ToInt32(), m.LParam);
                m.Result = (IntPtr)1;
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Corners and edges act as resize handles; everything else is the image.</summary>
    int HitTest(IntPtr lParam)
    {
        long lp = lParam.ToInt64();
        var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
        int grip = Math.Max(4, Math.Min((int)(10 * s), Math.Min(Width, Height) / 4));
        bool l = p.X < grip, r = p.X >= Width - grip, t = p.Y < grip, b = p.Y >= Height - grip;
        // Corners get a bigger target than edges, they're what people reach for.
        int corner = grip * 2;
        bool cl = p.X < corner, cr = p.X >= Width - corner, ct = p.Y < corner, cb = p.Y >= Height - corner;
        if ((t && cl) || (l && ct)) return 13;  // HTTOPLEFT
        if ((t && cr) || (r && ct)) return 14;  // HTTOPRIGHT
        if ((b && cl) || (l && cb)) return 16;  // HTBOTTOMLEFT
        if ((b && cr) || (r && cb)) return 17;  // HTBOTTOMRIGHT
        if (l) return 10;
        if (r) return 11;
        if (t) return 12;
        if (b) return 15;
        return HTCLIENT;
    }

    /// <summary>Adjusts the rectangle Windows proposes while resizing so the image keeps its proportions.</summary>
    void KeepAspect(int edge, IntPtr lParam)
    {
        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.RECT>(lParam);
        double aspect = img.Width / (double)img.Height;
        int w = Math.Max(16, r.Right - r.Left - 2), h = Math.Max(16, r.Bottom - r.Top - 2);
        if (edge is 3 or 6) w = (int)Math.Round(h * aspect);  // top / bottom edge: height leads
        else h = (int)Math.Round(w / aspect);                   // sides and corners: width leads

        bool left = edge is 1 or 4 or 7, top = edge is 3 or 4 or 5;
        if (left) r.Left = r.Right - (w + 2); else r.Right = r.Left + w + 2;
        if (top) r.Top = r.Bottom - (h + 2); else r.Bottom = r.Top + h + 2;
        System.Runtime.InteropServices.Marshal.StructureToPtr(r, lParam, false);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        zoom = Math.Max(0.01f, (ClientSize.Width - 2) / (float)img.Width);
        Invalidate();
    }

    void SetZoom(float z, Point anchor)
    {
        z = Math.Clamp(z, 0.1f, 8f);
        if (Math.Abs(Math.Round(z, 2) - 1) < 0.04) z = 1f; // snap back to pixel-perfect
        float rx = anchor.X / (float)Width, ry = anchor.Y / (float)Height;
        var screenAnchor = PointToScreen(anchor);
        zoom = z;
        int w = (int)Math.Round(img.Width * zoom) + 2, h = (int)Math.Round(img.Height * zoom) + 2;
        Bounds = new Rectangle(screenAnchor.X - (int)(rx * w), screenAnchor.Y - (int)(ry * h), w, h);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1.0);
        else
            SetZoom(zoom * (e.Delta > 0 ? 1.1f : 1 / 1.1f), e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { dragging = true; grab = e.Location; }
        else if (e.Button == MouseButtons.Middle) Close();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        var p = Cursor.Position;
        Location = new Point(p.X - grab.X, p.Y - grab.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e) => dragging = false;

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Control | Keys.C: ImageOps.CopyToClipboard(img); return true;
            case Keys.Control | Keys.S: ImageOps.SaveAs(this, img, TrayApp.Instance.Settings.SaveFolder); return true;
            case Keys.D1: SetZoom(1f, new Point(Width / 2, Height / 2)); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Accent);
        g.InterpolationMode = zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(img, new Rectangle(1, 1, Width - 2, Height - 2), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        img.Dispose();
        base.OnFormClosed(e);
    }
}
