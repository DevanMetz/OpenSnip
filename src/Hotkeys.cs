namespace OpenSnip;

/// <summary>A key plus the exact set of modifiers that must be held with it.</summary>
readonly record struct Hotkey(Keys Key, bool Win, bool Ctrl, bool Alt, bool Shift)
{
    public bool IsEmpty => Key == Keys.None;
    public bool HasModifier => Win || Ctrl || Alt || Shift;

    /// <summary>Parses the settings-file form, e.g. "Win+Shift+S".</summary>
    public static Hotkey Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        bool win = false, ctrl = false, alt = false, shift = false;
        Keys key = Keys.None;
        foreach (var part in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "win": win = true; break;
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default: if (!Enum.TryParse(part, true, out key)) return default; break;
            }
        }
        return key == Keys.None ? default : new Hotkey(key, win, ctrl, alt, shift);
    }

    public string Serialize() => IsEmpty ? "" : string.Join("+", Parts(Key.ToString()));

    public string Display => IsEmpty ? "None" : string.Join(" + ", Parts(KeyName(Key)));

    IEnumerable<string> Parts(string key)
    {
        if (Win) yield return "Win";
        if (Ctrl) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
        yield return key;
    }

    public static string KeyName(Keys k)
    {
        if (k is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k is >= Keys.NumPad0 and <= Keys.NumPad9) return "Num " + (k - Keys.NumPad0);
        switch (k)
        {
            case Keys.PrintScreen: return "PrtSc";
            case Keys.Return: return "Enter";
            case Keys.Prior: return "PgUp";
            case Keys.Next: return "PgDn";
            case Keys.Back: return "Backspace";
            case Keys.Scroll: return "ScrLk";
            case Keys.Capital: return "CapsLock";
        }
        char c = Native.KeyChar(k);
        return c > ' ' ? char.ToUpperInvariant(c).ToString() : k.ToString();
    }

    public static bool IsModifierKey(Keys k) => k is
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    /// <summary>Keys that are safe as a global shortcut on their own, without breaking normal typing.</summary>
    public static bool OkWithoutModifier(Keys k) =>
        k is >= Keys.F1 and <= Keys.F24 or Keys.PrintScreen or Keys.Pause or Keys.Scroll or Keys.Insert
          or Keys.LaunchApplication1 or Keys.LaunchApplication2 or Keys.LaunchMail or Keys.SelectMedia;
}

/// <summary>Immutable snapshot of the configured shortcuts, swapped atomically for the hook thread.</summary>
sealed class HotkeySet
{
    public Hotkey Snip { get; init; }
    public Hotkey Delay { get; init; }
    public Hotkey Repeat { get; init; }
    public bool PrintScreen { get; init; }

    public static HotkeySet From(Settings s) => new()
    {
        Snip = Hotkey.Parse(s.SnipHotkey),
        Delay = Hotkey.Parse(s.DelayHotkey),
        Repeat = Hotkey.Parse(s.RepeatHotkey),
        PrintScreen = s.UsePrintScreen,
    };

    public bool Contains(Hotkey h) => !h.IsEmpty && (h == Snip || h == Delay || h == Repeat);

    /// <summary>What to show in menus and tooltips for the main snip shortcut.</summary>
    public string SnipDisplay => !Snip.IsEmpty ? Snip.Display : PrintScreen ? "PrtSc" : null;
    public string DelayDisplay => !Delay.IsEmpty ? Delay.Display : PrintScreen ? "Shift + PrtSc" : null;
    public string RepeatDisplay => !Repeat.IsEmpty ? Repeat.Display : PrintScreen ? "Ctrl + PrtSc" : null;
}

/// <summary>Lets you set the global shortcuts by pressing them.</summary>
sealed class ShortcutsForm : Form
{
    readonly TrayApp app;
    readonly float ui;
    readonly Label note;
    readonly List<Row> rows = new();
    Row recording;

    sealed class Row
    {
        public Func<string> Get;
        public Action<string> Set;
        public Label Value;
        public Button Change;
    }

    public ShortcutsForm(TrayApp app)
    {
        this.app = app;
        ui = Native.ScaleAt(Cursor.Position);
        Text = "OpenSnip shortcuts";
        Icon = AppIcon.Get(32);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 13f * ui, GraphicsUnit.Pixel);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;

        var s = app.Settings;
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Padding = Pad(18, 16, 18, 8),
            Location = Point.Empty,
        };
        AddRow(grid, "Snip", () => s.SnipHotkey, v => s.SnipHotkey = v);
        AddRow(grid, "Snip after delay", () => s.DelayHotkey, v => s.DelayHotkey = v);
        AddRow(grid, "Repeat last region", () => s.RepeatHotkey, v => s.RepeatHotkey = v);

        var prt = new CheckBox
        {
            Text = "Also use the Print Screen key  (Shift = delay, Ctrl = repeat)",
            Checked = s.UsePrintScreen,
            AutoSize = true,
            ForeColor = Theme.Text,
            Margin = Pad(0, 12, 0, 0),
        };
        prt.CheckedChanged += (_, _) => { s.UsePrintScreen = prt.Checked; Saved(); };
        grid.Controls.Add(prt, 0, rows.Count);
        grid.SetColumnSpan(prt, 4);

        note = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size((int)(520 * ui), 0),
            Margin = Pad(0, 12, 0, 0),
            Text = "Click Change, then press the keys you want. OpenSnip catches shortcuts before Windows does, " +
                   "so Win + Shift + S opens OpenSnip instead of the Snipping Tool.",
        };
        grid.Controls.Add(note, 0, rows.Count + 1);
        grid.SetColumnSpan(note, 4);

        var done = MakeButton("Done");
        done.Anchor = AnchorStyles.Right;
        done.Margin = Pad(0, 16, 0, 8);
        done.Click += (_, _) => Close();
        grid.Controls.Add(done, 0, rows.Count + 2);
        grid.SetColumnSpan(done, 4);
        AcceptButton = done;

        Controls.Add(grid);
    }

    Padding Pad(int l, int t, int r, int b) => new((int)(l * ui), (int)(t * ui), (int)(r * ui), (int)(b * ui));

    Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Padding = Pad(8, 2, 8, 2),
            Margin = Pad(6, 4, 0, 4),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 92);
        return b;
    }

    void AddRow(TableLayoutPanel grid, string label, Func<string> get, Action<string> set)
    {
        var row = new Row { Get = get, Set = set };
        var name = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Pad(0, 4, 24, 4) };
        row.Value = new Label
        {
            AutoSize = false,
            Size = new Size((int)(190 * ui), (int)(30 * ui)),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
            Margin = Pad(0, 4, 0, 4),
        };
        row.Change = MakeButton("Change…");
        var clear = MakeButton("Clear");
        row.Change.Click += (_, _) => { if (recording == row) StopRecording(); else StartRecording(row); };
        clear.Click += (_, _) => { StopRecording(); row.Set(""); Saved(); };

        int r = rows.Count;
        grid.Controls.Add(name, 0, r);
        grid.Controls.Add(row.Value, 1, r);
        grid.Controls.Add(row.Change, 2, r);
        grid.Controls.Add(clear, 3, r);
        rows.Add(row);
        ShowRow(row);
    }

    void ShowRow(Row row)
    {
        var h = Hotkey.Parse(row.Get());
        row.Value.Text = h.Display;
        row.Value.ForeColor = h.IsEmpty ? Theme.Muted : Theme.Text;
        row.Change.Text = "Change…";
    }

    void StartRecording(Row row)
    {
        StopRecording();
        recording = row;
        row.Value.Text = "Press keys…";
        row.Value.ForeColor = Theme.Accent2;
        row.Change.Text = "Cancel";
        app.BeginRecording(OnRecorded);
    }

    void StopRecording()
    {
        app.EndRecording();
        if (recording == null) return;
        ShowRow(recording);
        recording = null;
    }

    void OnRecorded(Hotkey h)
    {
        var row = recording;
        if (row == null) return;
        if (h.Key == Keys.Escape && !h.HasModifier) { StopRecording(); return; }
        if (!h.HasModifier && !Hotkey.OkWithoutModifier(h.Key))
        {
            note.ForeColor = Color.FromArgb(255, 170, 90);
            note.Text = $"\"{h.Display}\" alone would stop that key from typing. Add Ctrl, Alt, Shift or Win.";
            app.BeginRecording(OnRecorded); // keep listening
            return;
        }

        // A shortcut can only do one thing: take it away from any other row.
        foreach (var other in rows)
            if (other != row && Hotkey.Parse(other.Get()) == h) { other.Set(""); ShowRow(other); }

        row.Set(h.Serialize());
        StopRecording();
        note.ForeColor = Theme.Good;
        note.Text = $"✓ {h.Display} saved.";
        Saved();
    }

    void Saved()
    {
        app.Settings.Save();
        app.ReloadHotkeys();
        foreach (var r in rows) if (r != recording) ShowRow(r);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && recording == null) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopRecording();
        base.OnFormClosed(e);
    }
}
