using System.Diagnostics;
using Microsoft.Win32;

namespace OpenSnip;

/// <summary>All of OpenSnip's options. Changes apply and save immediately.</summary>
sealed class SettingsForm : Form
{
    readonly TrayApp app;
    readonly Settings s;
    readonly float ui;
    readonly FlowLayoutPanel nav;
    readonly Panel content;
    readonly List<(Button btn, TableLayoutPanel page)> pages = new();
    readonly List<HotkeyRow> hotkeyRows = new();
    readonly Font navFont, navFontBold;
    HotkeyRow listening;
    Label shortcutNote;

    static readonly Color NavSelected = Color.FromArgb(72, 58, 140);

    sealed class HotkeyRow
    {
        public Func<string> Get;
        public Action<string> Set;
        public Label Value;
        public Button Change;
    }

    public SettingsForm(TrayApp app, string startPage = null)
    {
        this.app = app;
        s = app.Settings;
        ui = Native.ScaleAt(Cursor.Position);

        Text = "OpenSnip settings";
        Icon = AppIcon.Get(32);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 13f * ui, GraphicsUnit.Pixel);
        navFont = new Font(Font, FontStyle.Regular);
        navFontBold = new Font(Font, FontStyle.Bold);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Width = (int)(170 * ui),
            BackColor = Theme.Panel,
            Padding = Pad(8, 14, 8, 14),
        };
        content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        Controls.Add(content);
        Controls.Add(nav);

        AddPage("Capture", BuildCapture());
        AddPage("Saving", BuildSaving());
        AddPage("Recording", BuildRecording());
        AddPage("Shortcuts", BuildShortcuts());
        AddPage("General", BuildGeneral());

        var done = MakeButton("Done");
        done.Click += (_, _) => Close();
        AcceptButton = done;

        var max = Size.Empty;
        foreach (var (_, page) in pages)
        {
            var sz = page.GetPreferredSize(Size.Empty);
            max = new Size(Math.Max(max.Width, sz.Width), Math.Max(max.Height, sz.Height));
        }
        int footer = done.PreferredSize.Height + (int)(24 * ui);
        ClientSize = new Size(nav.Width + max.Width, max.Height + footer);
        done.Location = new Point(max.Width - done.PreferredSize.Width - (int)(24 * ui), max.Height + (int)(4 * ui));
        content.Controls.Add(done);

        SelectPage(pages.FirstOrDefault(p => p.btn.Text == startPage).btn ?? pages[0].btn);
    }

    // ---------- pages ----------

    TableLayoutPanel BuildCapture()
    {
        var p = NewPage("Capture");
        Heading(p, "After a snip");
        Full(p, Check("Copy it to the clipboard", () => s.CopyToClipboard, v => s.CopyToClipboard = v));
        Full(p, Radio("Show a small preview in the corner", s.ShowThumbnail && !s.OpenEditorAfterSnip,
            () => { s.ShowThumbnail = true; s.OpenEditorAfterSnip = false; }));
        Full(p, Radio("Open the editor right away", s.OpenEditorAfterSnip,
            () => { s.OpenEditorAfterSnip = true; }));
        Full(p, Radio("Nothing else", !s.ShowThumbnail && !s.OpenEditorAfterSnip,
            () => { s.ShowThumbnail = false; s.OpenEditorAfterSnip = false; }));
        Row(p, "Preview stays for", Combo(new[] { 3, 6, 10, 20 }, n => $"{n} seconds", s.PreviewSeconds, v => s.PreviewSeconds = v));

        Heading(p, "While selecting");
        Full(p, Check("Show the magnifier next to the cursor", () => s.ShowMagnifier, v => s.ShowMagnifier = v));
        Row(p, "Delayed snip waits", Combo(new[] { 1, 3, 5, 10 }, n => $"{n} second{(n == 1 ? "" : "s")}", s.DelaySeconds, v => s.DelaySeconds = v));
        return p;
    }

    TableLayoutPanel BuildSaving()
    {
        var p = NewPage("Saving");
        Heading(p, "Screenshots");
        Full(p, Check("Save every snip automatically", () => s.AutoSave, v => s.AutoSave = v));
        FolderRow(p, "Folder", () => s.SaveFolder, v => s.SaveFolder = v);
        Row(p, "Format", Combo(new[] { "png", "jpg" }, f => f == "png" ? "PNG (best quality)" : "JPG (smaller files)",
            s.SaveFormat, v => s.SaveFormat = v));

        Heading(p, "Recordings");
        FolderRow(p, "Folder", () => s.VideoFolder, v => s.VideoFolder = v);

        Heading(p, "Organizing");
        Full(p, Check("Sort files into monthly subfolders (like 2026-10)", () => s.MonthlyFolders, v => s.MonthlyFolders = v));
        return p;
    }

    TableLayoutPanel BuildRecording()
    {
        var p = NewPage("Recording");
        Heading(p, "Sound");
        Full(p, Check("Record system sound (what you hear)", () => s.RecordSystemAudio, v => s.RecordSystemAudio = v));
        ComboBox micBox = null;
        Full(p, Check("Record my microphone", () => s.RecordMicrophone, v =>
        {
            s.RecordMicrophone = v;
            if (micBox != null) micBox.Enabled = v;
        }));

        var mics = new List<(string name, string id)> { ("Windows default", "") };
        string micError = null;
        try { mics.AddRange(RecorderDevices.Microphones()); }
        catch (Exception ex) { micError = ex.Message; Log.Error(ex); }
        if (!mics.Any(m => m.id == s.MicrophoneDevice)) s.MicrophoneDevice = "";
        micBox = Combo(mics.Select(m => m.id).ToArray(), id => mics.First(m => m.id == id).name,
            s.MicrophoneDevice, v => s.MicrophoneDevice = v);
        micBox.Width = (int)(320 * ui);
        micBox.Enabled = s.RecordMicrophone;
        Row(p, "Microphone", micBox);
        if (micError != null) Full(p, Note("Couldn't list microphones: " + micError));

        Heading(p, "Video");
        Row(p, "Frame rate", Combo(new[] { 30, 60 }, n => $"{n} fps", s.RecordFramerate, v => s.RecordFramerate = v));
        Row(p, "Quality", Combo(new[] { true, false }, hq => hq ? "High" : "Standard (smaller files)",
            s.RecordHighQuality, v => s.RecordHighQuality = v));
        Full(p, Check("Show the mouse cursor", () => s.RecordCursor, v => s.RecordCursor = v));
        Full(p, Check("Count down 3, 2, 1 before recording starts", () => s.RecordCountdown, v => s.RecordCountdown = v));
        Full(p, Check("Copy the video file to the clipboard when done", () => s.CopyRecordingToClipboard, v => s.CopyRecordingToClipboard = v));
        return p;
    }

    TableLayoutPanel BuildShortcuts()
    {
        var p = NewPage("Shortcuts");
        HotkeyRowFor(p, "Snip", () => s.SnipHotkey, v => s.SnipHotkey = v);
        HotkeyRowFor(p, "Snip after delay", () => s.DelayHotkey, v => s.DelayHotkey = v);
        HotkeyRowFor(p, "Repeat last region", () => s.RepeatHotkey, v => s.RepeatHotkey = v);
        HotkeyRowFor(p, "Start / stop recording", () => s.RecordHotkey, v => s.RecordHotkey = v);
        var prt = Check("Also use the Print Screen key", () => s.UsePrintScreen, v => s.UsePrintScreen = v);
        prt.Margin = Pad(0, 14, 0, 4);
        Full(p, prt);
        shortcutNote = Note("");
        Full(p, shortcutNote);
        return p;
    }

    TableLayoutPanel BuildGeneral()
    {
        var p = NewPage("General");
        Full(p, Check("Start OpenSnip when Windows starts", () => Startup.Enabled, Startup.Set));
        if (Environment.ProcessPath?.Contains(@"\AppData\Roaming\Claude\", StringComparison.OrdinalIgnoreCase) == true ||
            Environment.ProcessPath?.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) == true)
            Full(p, Note("Tip: move OpenSnip.exe somewhere permanent before turning this on."));

        Heading(p, "Settings");
        var open = MakeButton("Open settings folder");
        open.Click += (_, _) => { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", $"\"{Settings.Dir}\""); };
        var reset = MakeButton("Reset everything to defaults");
        reset.Click += (_, _) => Reset();
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, BackColor = Theme.Back };
        open.Margin = Pad(0, 4, 8, 4);
        buttons.Controls.Add(open);
        buttons.Controls.Add(reset);
        Full(p, buttons);

        Heading(p, "About");
        var version = typeof(SettingsForm).Assembly.GetName().Version;
        var link = new LinkLabel
        {
            Text = $"OpenSnip {version?.ToString(3)}  ·  github.com/DevanMetz/OpenSnip",
            AutoSize = true,
            LinkColor = Theme.Accent2,
            ActiveLinkColor = Theme.Accent2,
            ForeColor = Theme.Muted,
            Margin = Pad(0, 4, 0, 4),
        };
        link.LinkArea = new LinkArea(link.Text.IndexOf("github", StringComparison.Ordinal), "github.com/DevanMetz/OpenSnip".Length);
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/DevanMetz/OpenSnip") { UseShellExecute = true });
        Full(p, link);
        return p;
    }

    void Reset()
    {
        if (MessageBox.Show(this, "Reset all OpenSnip settings to their defaults?", "OpenSnip",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        var defaults = new Settings { Welcomed = true };
        foreach (var prop in typeof(Settings).GetProperties().Where(pr => pr.CanWrite))
            prop.SetValue(s, prop.GetValue(defaults));
        Saved();
        var page = pages.First(pg => pg.btn.BackColor == NavSelected).btn.Text;
        Close();
        app.ShowSettings(page);
    }

    // ---------- navigation ----------

    void AddPage(string name, TableLayoutPanel page)
    {
        var b = new Button
        {
            Text = name,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Size = new Size(nav.Width - nav.Padding.Horizontal, (int)(38 * ui)),
            Margin = Pad(0, 0, 0, 4),
            Padding = Pad(10, 0, 0, 0),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 68);
        b.Click += (_, _) => SelectPage(b);
        nav.Controls.Add(b);
        page.Location = Point.Empty;
        page.Visible = false;
        content.Controls.Add(page);
        pages.Add((b, page));
    }

    void SelectPage(Button which)
    {
        StopListening();
        foreach (var (btn, page) in pages)
        {
            bool on = btn == which;
            page.Visible = on;
            btn.BackColor = on ? NavSelected : Theme.Panel;
            btn.FlatAppearance.MouseOverBackColor = on ? NavSelected : Color.FromArgb(58, 58, 68);
            btn.Font = on ? navFontBold : navFont;
        }
    }

    // ---------- layout helpers ----------

    Padding Pad(int l, int t, int r, int b) => new((int)(l * ui), (int)(t * ui), (int)(r * ui), (int)(b * ui));

    TableLayoutPanel NewPage(string title)
    {
        var p = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 0,
            Padding = Pad(26, 18, 26, 8),
            BackColor = Theme.Back,
        };
        Full(p, new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22f * ui, GraphicsUnit.Pixel),
            Margin = Pad(0, 0, 0, 6),
        });
        return p;
    }

    static void Add(TableLayoutPanel p, Control c, int col, int span = 1)
    {
        p.Controls.Add(c, col, p.RowCount - 1);
        if (span > 1) p.SetColumnSpan(c, span);
    }

    static void Full(TableLayoutPanel p, Control c)
    {
        p.RowCount++;
        Add(p, c, 0, 4);
    }

    void Row(TableLayoutPanel p, string label, Control main, params Control[] extra)
    {
        p.RowCount++;
        Add(p, new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Pad(0, 6, 18, 6) }, 0);
        Add(p, main, 1, extra.Length == 0 ? 3 : 1);
        for (int i = 0; i < extra.Length; i++) Add(p, extra[i], 2 + i);
    }

    void Heading(TableLayoutPanel p, string text) => Full(p, new Label
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        ForeColor = Theme.Accent2,
        Font = new Font("Segoe UI Semibold", 11.5f * ui, GraphicsUnit.Pixel),
        Margin = Pad(0, 16, 0, 4),
    });

    Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        MaximumSize = new Size((int)(560 * ui), 0),
        Margin = Pad(0, 8, 0, 4),
    };

    void Saved()
    {
        s.Save();
        app.ReloadHotkeys();
    }

    CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Checked = get(), ForeColor = Theme.Text, Margin = Pad(0, 4, 0, 4) };
        c.CheckedChanged += (_, _) =>
        {
            try { set(c.Checked); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "OpenSnip", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Saved();
        };
        return c;
    }

    RadioButton Radio(string text, bool on, Action pick)
    {
        var r = new RadioButton { Text = text, AutoSize = true, Checked = on, ForeColor = Theme.Text, Margin = Pad(18, 3, 0, 3) };
        r.CheckedChanged += (_, _) => { if (r.Checked) { pick(); Saved(); } };
        return r;
    }

    ComboBox Combo<T>(T[] values, Func<T, string> label, T current, Action<T> set)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Width = (int)(230 * ui),
            Margin = Pad(0, 4, 0, 4),
        };
        // Owner-drawn so the box and its list stay dark (the themed combo ignores BackColor).
        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.ItemHeight = (int)(Font.Height * 1.3);
        cb.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool highlight = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var bg = new SolidBrush(highlight ? NavSelected : Theme.Panel))
                e.Graphics.FillRectangle(bg, e.Bounds);
            var r = new Rectangle(e.Bounds.X + (int)(6 * ui), e.Bounds.Y, e.Bounds.Width - (int)(6 * ui), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font, r, cb.Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        };
        foreach (var v in values) cb.Items.Add(label(v));
        int idx = Array.IndexOf(values, current);
        cb.SelectedIndex = idx >= 0 ? idx : 0;
        cb.SelectedIndexChanged += (_, _) => { set(values[cb.SelectedIndex]); Saved(); };
        return cb;
    }

    Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Padding = Pad(10, 3, 10, 3),
            Margin = Pad(8, 4, 0, 4),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 92);
        return b;
    }

    void FolderRow(TableLayoutPanel p, string label, Func<string> get, Action<string> set)
    {
        var box = new TextBox
        {
            Text = get(),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Width = (int)(340 * ui),
            Anchor = AnchorStyles.Left,
            Margin = Pad(0, 6, 0, 6),
        };
        var browse = MakeButton("Browse…");
        var open = MakeButton("Open");
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Choose a folder",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(get()) ? get() : "",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(dlg.SelectedPath);
            box.Text = dlg.SelectedPath;
            Saved();
        };
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(get());
                Process.Start("explorer.exe", $"\"{get()}\"");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "OpenSnip", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Row(p, label, box, browse, open);
    }

    // ---------- shortcut recording ----------

    void HotkeyRowFor(TableLayoutPanel p, string label, Func<string> get, Action<string> set)
    {
        var row = new HotkeyRow { Get = get, Set = set };
        row.Value = new Label
        {
            AutoSize = false,
            Size = new Size((int)(220 * ui), (int)(32 * ui)),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
            Margin = Pad(0, 4, 0, 4),
        };
        row.Change = MakeButton("Change…");
        var clear = MakeButton("Clear");
        row.Change.Click += (_, _) => { if (listening == row) StopListening(); else StartListening(row); };
        clear.Click += (_, _) => { StopListening(); row.Set(""); Saved(); ShowRow(row); };
        hotkeyRows.Add(row);
        Row(p, label, row.Value, row.Change, clear);
        ShowRow(row);
    }

    void ShowRow(HotkeyRow row)
    {
        var h = Hotkey.Parse(row.Get());
        row.Value.Text = h.Display;
        row.Value.ForeColor = h.IsEmpty ? Theme.Muted : Theme.Text;
        row.Change.Text = "Change…";
    }

    void StartListening(HotkeyRow row)
    {
        StopListening();
        listening = row;
        row.Value.Text = "Press keys…";
        row.Value.ForeColor = Theme.Accent2;
        row.Change.Text = "Cancel";
        app.BeginRecording(OnKeyPressed);
    }

    void StopListening()
    {
        app.EndRecording();
        if (listening == null) return;
        ShowRow(listening);
        listening = null;
    }

    void OnKeyPressed(Hotkey h)
    {
        var row = listening;
        if (row == null || IsDisposed) return;
        if (h.Key == Keys.Escape && !h.HasModifier) { StopListening(); return; }
        if (!h.HasModifier && !Hotkey.OkWithoutModifier(h.Key))
        {
            shortcutNote.ForeColor = Theme.Warn;
            shortcutNote.Text = $"\"{h.Display}\" alone would stop that key from typing. Add Ctrl, Alt, Shift or Win.";
            app.BeginRecording(OnKeyPressed); // keep listening
            return;
        }

        // A shortcut can only do one thing: take it away from any other row.
        foreach (var other in hotkeyRows)
            if (other != row && Hotkey.Parse(other.Get()) == h) { other.Set(""); ShowRow(other); }

        row.Set(h.Serialize());
        StopListening();
        shortcutNote.ForeColor = Theme.Good;
        shortcutNote.Text = $"✓ {h.Display} saved.";
        Saved();
    }

    // ---------- window ----------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && listening == null) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopListening();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            navFont?.Dispose();
            navFontBold?.Dispose();
        }
    }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("OpenSnip") is string;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue("OpenSnip", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("OpenSnip", false);
    }
}
