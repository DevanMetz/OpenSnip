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
        Native.SetCorners(Handle, Native.DWMWCP_ROUND);
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
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    readonly Font font;
    readonly Rectangle imgRect;
    int lifeMs;
    bool hovering, pressed;
    Point pressAt;

    const int LifetimeMs = 6000;

    public ThumbnailForm(Bitmap img, string path, Rectangle source) : base(Center(source))
    {
        this.img = img;
        this.path = path;
        this.source = source;
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
        if (lifeMs < LifetimeMs) return;
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
            var result = DoDragDrop(ImageOps.DragData(img, path), DragDropEffects.Copy);
            if (result != DragDropEffects.None) Close();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed || e.Button != MouseButtons.Left) return;
        pressed = false;
        TrayApp.Instance.OpenEditor((Bitmap)img.Clone(), source);
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

        var bar = new Rectangle(0, imgRect.Bottom + (int)(4 * s), Width, Height - imgRect.Bottom - (int)(4 * s));
        string text = hovering ? "Click to edit  ·  Drag to drop  ·  Right-click to close" : "✓ Copied to clipboard";
        var color = hovering ? Theme.Text : Theme.Good;
        TextRenderer.DrawText(g, text, font, bar, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
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
/// An always-on-top floating copy of an image. Drag to move, scroll to zoom,
/// Ctrl+scroll for opacity, double-click or Esc to close, right-click for more.
/// </summary>
sealed class PinForm : Form
{
    readonly Bitmap img;
    float zoom = 1f;
    bool dragging;
    Point grab;

    public PinForm(Bitmap img, Point location)
    {
        this.img = img;
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
        menu.Items.Add("Actual size", null, (_, _) => SetZoom(1f, new Point(Width / 2, Height / 2)));
        menu.Items.Add(new ToolStripSeparator());
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
        if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
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
