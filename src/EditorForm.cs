using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenSnip;

/// <summary>Annotate a snip. Every change is copied to the clipboard automatically.</summary>
sealed class EditorForm : Form
{
    static readonly Color[] Swatches =
    {
        Color.FromArgb(235, 64, 52),   // red
        Color.FromArgb(255, 214, 10),  // yellow
        Color.FromArgb(52, 199, 89),   // green
        Color.FromArgb(10, 132, 255),  // blue
        Color.White,
        Color.FromArgb(20, 20, 20),    // black
    };

    readonly Bitmap baseImg;
    readonly Rectangle? source;
    readonly float k;  // annotation size multiplier (matches the DPI the snip came from)
    readonly float ui; // UI scale
    readonly List<Shape> shapes = new();
    readonly Stack<Shape> redo = new();
    readonly Dictionary<Tool, Color> toolColors = new() { [Tool.Highlight] = Swatches[1] };

    Bitmap composite, display;
    Rectangle imgRect;
    Tool tool = Tool.Arrow;
    Shape current;
    PointF start;
    bool drawing;
    TextBox textBox;
    PointF textPos;
    float textSize;

    readonly Canvas canvas;
    readonly ToolStrip bar;
    readonly ToolStripLabel status;
    readonly ToolStripButton undoBtn, redoBtn;
    readonly Dictionary<Tool, ToolStripButton> toolButtons = new();
    readonly List<(Color color, ToolStripButton btn)> swatchButtons = new();
    readonly System.Windows.Forms.Timer copyTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 2200 };

    float Zoom => imgRect.Width / (float)baseImg.Width;
    Color CurrentColor => toolColors.TryGetValue(tool, out var c) ? c : Swatches[0];

    sealed class Canvas : Control
    {
        public Canvas() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public EditorForm(Bitmap img, Rectangle? source)
    {
        baseImg = img;
        this.source = source;
        var origin = source.HasValue
            ? new Point(source.Value.X + source.Value.Width / 2, source.Value.Y + source.Value.Height / 2)
            : Cursor.Position;
        ui = Native.ScaleAt(origin);
        k = Math.Max(1f, ui);

        Text = $"OpenSnip — {img.Width} × {img.Height}";
        Icon = AppIcon.Get(32);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        Font = new Font("Segoe UI", 13f * ui, GraphicsUnit.Pixel);

        // ---- toolbar ----
        bar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new DarkRenderer(),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Font = Font,
            Padding = Pad(6, 5, 6, 5),
            ImageScalingSize = new Size((int)(16 * ui), (int)(16 * ui)),
            ShowItemToolTips = true,
        };

        AddTool(Tool.Arrow, "↗ Arrow", "Arrow  (A)\nHold Shift to snap to 45°");
        AddTool(Tool.Box, "▢ Box", "Rectangle  (R)\nHold Shift for a square");
        AddTool(Tool.Pen, "✎ Pen", "Freehand pen  (D)\nHold Shift for a straight line");
        AddTool(Tool.Highlight, "▌Highlight", "Highlighter  (H)\nHold Shift for a straight line");
        AddTool(Tool.Text, "T Text", "Text  (T)\nClick to type · Enter to finish · Shift+Enter for a new line");
        AddTool(Tool.Blur, "▦ Redact", "Pixelate sensitive info  (B)");
        bar.Items.Add(new ToolStripSeparator { Margin = Pad(4, 0, 4, 0) });

        for (int i = 0; i < Swatches.Length; i++)
        {
            var c = Swatches[i];
            var btn = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = SwatchImage(c),
                ImageScaling = ToolStripItemImageScaling.None,
                ToolTipText = $"Color  ({i + 1})",
                Padding = Pad(3, 3, 3, 3),
            };
            btn.Click += (_, _) => SetColor(c);
            bar.Items.Add(btn);
            swatchButtons.Add((c, btn));
        }
        bar.Items.Add(new ToolStripSeparator { Margin = Pad(4, 0, 4, 0) });

        undoBtn = Button("↶ Undo", "Undo  (Ctrl+Z)", Undo);
        redoBtn = Button("↷ Redo", "Redo  (Ctrl+Y)", Redo);

        // Right side (first added = rightmost).
        Button("Pin", "Float this image on top of everything  (P)", Pin).Alignment = ToolStripItemAlignment.Right;
        Button("Save as…", "Save to a file  (Ctrl+S)", Save).Alignment = ToolStripItemAlignment.Right;
        Button("Copy", "Copy to clipboard  (Ctrl+C)", () => CopyNow()).Alignment = ToolStripItemAlignment.Right;
        status = new ToolStripLabel("Edits copy automatically") { Alignment = ToolStripItemAlignment.Right, ForeColor = Theme.Muted };
        bar.Items.Add(status);

        // ---- canvas ----
        canvas = new Canvas { Dock = DockStyle.Fill, BackColor = Theme.Back, Cursor = Cursors.Cross };
        canvas.Paint += PaintCanvas;
        canvas.MouseDown += CanvasDown;
        canvas.MouseMove += CanvasMove;
        canvas.MouseUp += CanvasUp;
        canvas.Resize += (_, _) => Relayout();

        Controls.Add(canvas);
        Controls.Add(bar);

        copyTimer.Tick += (_, _) => CopyNow();
        statusTimer.Tick += (_, _) =>
        {
            statusTimer.Stop();
            status.ForeColor = Theme.Muted;
            status.Text = "Edits copy automatically";
        };

        RebuildComposite();
        SetTool(Tool.Arrow);
        UpdateUndo();

        // ---- size & position ----
        var wa = Screen.FromPoint(origin).WorkingArea;
        int pad = (int)(16 * ui);
        int w = Math.Max(img.Width + 2 * pad, bar.PreferredSize.Width + pad);
        int h = img.Height + 2 * pad + bar.PreferredSize.Height;
        ClientSize = new Size(Math.Min(w, (int)(wa.Width * 0.92)), Math.Min(h, (int)(wa.Height * 0.9)));
        MinimumSize = new Size((int)(420 * ui), (int)(260 * ui));
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
    }

    Padding Pad(int l, int t, int r, int b) =>
        new((int)(l * ui), (int)(t * ui), (int)(r * ui), (int)(b * ui));

    void AddTool(Tool t, string text, string tip)
    {
        var b = new ToolStripButton(text) { ToolTipText = tip, Padding = Pad(6, 3, 6, 3), Margin = Pad(1, 0, 1, 0) };
        b.Click += (_, _) => SetTool(t);
        bar.Items.Add(b);
        toolButtons[t] = b;
    }

    ToolStripButton Button(string text, string tip, Action onClick)
    {
        var b = new ToolStripButton(text) { ToolTipText = tip, Padding = Pad(6, 3, 6, 3), Margin = Pad(1, 0, 1, 0) };
        b.Click += (_, _) => onClick();
        bar.Items.Add(b);
        return b;
    }

    Bitmap SwatchImage(Color c)
    {
        int sz = (int)(16 * ui);
        var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(c)) g.FillEllipse(b, 1, 1, sz - 3, sz - 3);
        using (var p = new Pen(Color.FromArgb(140, 255, 255, 255))) g.DrawEllipse(p, 1, 1, sz - 3, sz - 3);
        return bmp;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        // Keep the image pixel-for-pixel when the window moves between monitors.
        if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
    }

    // ---------- tools & state ----------

    void SetTool(Tool t)
    {
        if (textBox != null) CommitText();
        tool = t;
        foreach (var (key, btn) in toolButtons) btn.Checked = key == t;
        canvas.Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
        foreach (var (c, btn) in swatchButtons) btn.Checked = c == CurrentColor;
        foreach (var (_, btn) in swatchButtons) btn.Enabled = t != Tool.Blur;
    }

    void SetColor(Color c)
    {
        toolColors[tool] = c;
        foreach (var (sc, btn) in swatchButtons) btn.Checked = sc == c;
        if (textBox != null) textBox.ForeColor = c;
    }

    void UpdateUndo()
    {
        undoBtn.Enabled = shapes.Count > 0;
        redoBtn.Enabled = redo.Count > 0;
    }

    void Commit(Shape shape)
    {
        shapes.Add(shape);
        redo.Clear();
        shape.Render(composite);
        RebuildDisplay();
        Changed();
    }

    void Changed()
    {
        UpdateUndo();
        canvas.Invalidate();
        copyTimer.Stop();
        copyTimer.Start();
    }

    void Undo()
    {
        if (textBox != null) { CancelText(); return; }
        if (drawing) { CancelShape(); return; }
        if (shapes.Count == 0) return;
        redo.Push(shapes[^1]);
        shapes.RemoveAt(shapes.Count - 1);
        RebuildComposite();
        Changed();
    }

    void Redo()
    {
        if (redo.Count == 0) return;
        var s = redo.Pop();
        shapes.Add(s);
        s.Render(composite);
        RebuildDisplay();
        Changed();
    }

    void CopyNow()
    {
        if (textBox != null) CommitText();
        copyTimer.Stop();
        ImageOps.CopyToClipboard(composite);
        Flash("✓ Copied");
    }

    void Save()
    {
        if (textBox != null) CommitText();
        if (ImageOps.SaveAs(this, composite, TrayApp.Instance.Settings.SaveFolder) is string path)
            Flash("✓ Saved " + Path.GetFileName(path));
    }

    void Pin()
    {
        if (textBox != null) CommitText();
        var copy = (Bitmap)composite.Clone();
        Point loc;
        if (source.HasValue) loc = source.Value.Location;
        else
        {
            var wa = Screen.FromControl(this).WorkingArea;
            loc = new Point(wa.X + (wa.Width - copy.Width) / 2, wa.Y + (wa.Height - copy.Height) / 2);
        }
        Close();
        TrayApp.Instance.Pin(copy, loc);
    }

    void Flash(string text)
    {
        status.Text = text;
        status.ForeColor = Theme.Good;
        statusTimer.Stop();
        statusTimer.Start();
    }

    // ---------- rendering ----------

    void RebuildComposite()
    {
        var old = composite;
        composite = new Bitmap(baseImg.Width, baseImg.Height, PixelFormat.Format24bppRgb);
        composite.SetResolution(96, 96);
        using (var g = Graphics.FromImage(composite)) ImageOps.Blit(g, baseImg, 0, 0);
        foreach (var s in shapes) s.Render(composite);
        if (display != null && display != old) display.Dispose();
        display = null;
        old?.Dispose();
        RebuildDisplay();
    }

    void RebuildDisplay()
    {
        if (display != null && display != composite) display.Dispose();
        display = null;
        if (imgRect.Width <= 0 || imgRect.Height <= 0) return;
        if (imgRect.Size == composite.Size) { display = composite; return; }

        display = new Bitmap(imgRect.Width, imgRect.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(display);
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(composite, new Rectangle(0, 0, imgRect.Width, imgRect.Height),
            0, 0, composite.Width, composite.Height, GraphicsUnit.Pixel, attrs);
    }

    void Relayout()
    {
        if (composite == null) return; // still constructing
        if (textBox != null) CommitText();
        var area = canvas.ClientRectangle;
        int pad = (int)(16 * ui);
        float z = Math.Min(1f, Math.Min((area.Width - 2f * pad) / baseImg.Width, (area.Height - 2f * pad) / baseImg.Height));
        z = Math.Max(z, 0.02f);
        var size = new Size(Math.Max(1, (int)Math.Round(baseImg.Width * z)), Math.Max(1, (int)Math.Round(baseImg.Height * z)));
        var newRect = new Rectangle((area.Width - size.Width) / 2, (area.Height - size.Height) / 2, size.Width, size.Height);
        bool resized = newRect.Size != imgRect.Size;
        imgRect = newRect;
        if (resized || display == null) RebuildDisplay();
        canvas.Invalidate();
    }

    void PaintCanvas(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back);
        if (display == null) return;

        using (var p = new Pen(Color.FromArgb(50, 255, 255, 255)))
            g.DrawRectangle(p, imgRect.X - 1, imgRect.Y - 1, imgRect.Width + 1, imgRect.Height + 1);
        ImageOps.Blit(g, display, imgRect.X, imgRect.Y);

        if (current == null) return;
        g.SetClip(imgRect);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TranslateTransform(imgRect.X, imgRect.Y);
        g.ScaleTransform(Zoom, Zoom);
        current.DrawPreview(g);
        g.ResetTransform();
        g.ResetClip();
    }

    // ---------- mouse ----------

    PointF ToImage(Point p) => new((p.X - imgRect.X) / Zoom, (p.Y - imgRect.Y) / Zoom);

    void CanvasDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { if (drawing) CancelShape(); return; }
        if (e.Button != MouseButtons.Left) return;
        if (textBox != null) { CommitText(); return; }

        var p = ToImage(e.Location);
        start = p;
        switch (tool)
        {
            case Tool.Text:
                BeginText(e.Location);
                return;
            case Tool.Pen:
            case Tool.Highlight:
                var st = new StrokeShape
                {
                    Highlighter = tool == Tool.Highlight,
                    Color = CurrentColor,
                    Width = (tool == Tool.Highlight ? 18f : 4f) * k,
                };
                st.Points.Add(p);
                current = st;
                break;
            case Tool.Arrow: current = new ArrowShape { A = p, B = p, Color = CurrentColor, Width = 4.5f * k }; break;
            case Tool.Box: current = new BoxShape { A = p, B = p, Color = CurrentColor, Width = 3.5f * k }; break;
            case Tool.Blur: current = new BlurShape { A = p, B = p, Width = 12f * k }; break;
        }
        drawing = true;
        canvas.Invalidate();
    }

    void CanvasMove(object sender, MouseEventArgs e)
    {
        if (!drawing || current == null) return;
        var p = ToImage(e.Location);
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        switch (current)
        {
            case StrokeShape st:
                if (shift)
                {
                    st.Points.RemoveRange(1, st.Points.Count - 1);
                    st.Points.Add(Shape.Snap45(start, p));
                }
                else st.Points.Add(p);
                break;
            case ArrowShape a:
                a.B = shift ? Shape.Snap45(start, p) : p;
                break;
            case TwoPointShape t:
                t.B = shift ? Shape.Square(start, p) : p;
                break;
        }
        canvas.Invalidate();
    }

    void CanvasUp(object sender, MouseEventArgs e)
    {
        if (!drawing || e.Button != MouseButtons.Left) return;
        drawing = false;
        var shape = current;
        current = null;
        if (shape != null && !shape.IsEmpty) Commit(shape);
        canvas.Invalidate();
    }

    void CancelShape()
    {
        drawing = false;
        current = null;
        canvas.Capture = false;
        canvas.Invalidate();
    }

    // ---------- text ----------

    void BeginText(Point at)
    {
        textPos = ToImage(at);
        textSize = 22f * k;
        var color = CurrentColor;
        var tb = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.None,
            Font = new Font("Segoe UI", Math.Max(6f, textSize * Zoom), FontStyle.Bold, GraphicsUnit.Pixel),
            ForeColor = color,
            BackColor = TextShape.IsLight(color) ? Color.FromArgb(40, 40, 46) : Color.FromArgb(250, 250, 250),
            Location = at,
        };
        void Fit()
        {
            var sz = TextRenderer.MeasureText(tb.Text + "MM", tb.Font);
            tb.Size = new Size(sz.Width, sz.Height + 4);
        }
        tb.TextChanged += (_, _) => Fit();
        tb.KeyDown += (_, ke) =>
        {
            if (ke.KeyCode == Keys.Enter && !ke.Shift) { ke.SuppressKeyPress = true; CommitText(); }
        };
        tb.LostFocus += (_, _) => CommitText();
        textBox = tb;
        Fit();
        canvas.Controls.Add(tb);
        tb.Focus();
    }

    void CommitText()
    {
        var tb = textBox;
        if (tb == null) return;
        textBox = null;
        var shape = new TextShape { P = textPos, Text = tb.Text.TrimEnd(), Width = textSize, Color = tb.ForeColor };
        canvas.Controls.Remove(tb);
        tb.Dispose();
        if (!shape.IsEmpty) Commit(shape);
    }

    void CancelText()
    {
        var tb = textBox;
        if (tb == null) return;
        textBox = null;
        canvas.Controls.Remove(tb);
        tb.Dispose();
    }

    // ---------- keyboard ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (textBox != null)
        {
            if (keyData == Keys.Escape) { CancelText(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        switch (keyData)
        {
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
            case Keys.Control | Keys.C: CopyNow(); return true;
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.P: Pin(); return true;
            case Keys.Escape:
                if (drawing) CancelShape(); else Close();
                return true;
            case Keys.A: SetTool(Tool.Arrow); return true;
            case Keys.R: SetTool(Tool.Box); return true;
            case Keys.D: SetTool(Tool.Pen); return true;
            case Keys.H: SetTool(Tool.Highlight); return true;
            case Keys.T: SetTool(Tool.Text); return true;
            case Keys.B: SetTool(Tool.Blur); return true;
        }
        if (keyData >= Keys.D1 && keyData < Keys.D1 + Swatches.Length && tool != Tool.Blur)
        {
            SetColor(Swatches[keyData - Keys.D1]);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (textBox != null) CommitText();
        if (copyTimer.Enabled) CopyNow(); // don't lose the last edit
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        copyTimer.Dispose();
        statusTimer.Dispose();
        if (display != null && display != composite) display.Dispose();
        composite?.Dispose();
        baseImg.Dispose();
        base.OnFormClosed(e);
    }
}

sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item is ToolStripLabel ? e.Item.ForeColor
                    : e.Item.Enabled ? Theme.Text
                    : Color.FromArgb(95, 95, 105);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    sealed class DarkColors : ProfessionalColorTable
    {
        static readonly Color Bar = Theme.Panel;
        static readonly Color Hover = Color.FromArgb(60, 60, 70);
        static readonly Color Press = Color.FromArgb(76, 76, 88);
        static readonly Color Check = Color.FromArgb(72, 58, 140);
        static readonly Color Line = Color.FromArgb(75, 75, 85);

        public override Color ToolStripGradientBegin => Bar;
        public override Color ToolStripGradientMiddle => Bar;
        public override Color ToolStripGradientEnd => Bar;
        public override Color ToolStripBorder => Bar;
        public override Color ToolStripDropDownBackground => Bar;
        public override Color ImageMarginGradientBegin => Bar;
        public override Color ImageMarginGradientMiddle => Bar;
        public override Color ImageMarginGradientEnd => Bar;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientMiddle => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonSelectedBorder => Hover;
        public override Color ButtonPressedHighlight => Press;
        public override Color ButtonPressedGradientBegin => Press;
        public override Color ButtonPressedGradientMiddle => Press;
        public override Color ButtonPressedGradientEnd => Press;
        public override Color ButtonPressedBorder => Press;
        public override Color ButtonCheckedHighlight => Check;
        public override Color ButtonCheckedHighlightBorder => Theme.Accent;
        public override Color ButtonCheckedGradientBegin => Check;
        public override Color ButtonCheckedGradientMiddle => Check;
        public override Color ButtonCheckedGradientEnd => Check;
        public override Color CheckBackground => Check;
        public override Color CheckSelectedBackground => Check;
        public override Color CheckPressedBackground => Check;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Bar;
        public override Color OverflowButtonGradientBegin => Bar;
        public override Color OverflowButtonGradientMiddle => Bar;
        public override Color OverflowButtonGradientEnd => Bar;
    }
}
