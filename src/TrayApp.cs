using Microsoft.Win32;

namespace OpenSnip;

sealed class TrayApp : ApplicationContext
{
    public static TrayApp Instance { get; private set; }
    public Settings Settings { get; } = Settings.Load();

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static volatile bool overlayOpen;

    readonly NotifyIcon tray;
    readonly Control ui; // marshals work from background threads onto the UI thread
    CaptureOverlay overlay;
    ThumbnailForm thumb;
    CountdownForm countdown;
    Rectangle? lastRegion;
    bool busy;

    public TrayApp(EventWaitHandle snipEvent)
    {
        Instance = this;
        ui = new Control();
        _ = ui.Handle;

        tray = new NotifyIcon
        {
            Icon = AppIcon.Get(Native.SmallIconSize),
            Text = "OpenSnip — press PrtSc to snip",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) StartSnip(0, 250); };

        KeyboardHook.Start(OnKey, ok => Post(() =>
        {
            if (!ok)
                tray.ShowBalloonTip(8000, "OpenSnip",
                    "Couldn't take over the Print Screen key. You can still snip from the tray icon.", ToolTipIcon.Warning);
        }));

        new Thread(() => { while (snipEvent.WaitOne()) Post(() => StartSnip(0, 150)); })
        { IsBackground = true, Name = "OpenSnip second-instance signal" }.Start();

        if (!Settings.Welcomed)
        {
            Settings.Welcomed = true;
            Settings.Save();
            tray.ShowBalloonTip(8000, "OpenSnip is running",
                "Press Print Screen to snip. Right-click the tray icon for options.", ToolTipIcon.Info);
        }
    }

    void Post(Action a)
    {
        try { ui.BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    // Runs on the keyboard hook thread. Must be quick; returns true to swallow the key.
    bool OnKey(Keys key, bool down)
    {
        if (overlayOpen)
        {
            switch (key)
            {
                case Keys.Escape: case Keys.Enter: case Keys.F: case Keys.A:
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    if (down)
                    {
                        bool shift = Native.KeyDown(Native.VK_SHIFT);
                        Post(() => overlay?.HandleKey(key, shift));
                    }
                    return true;
                case Keys.PrintScreen:
                    return true;
            }
            return false;
        }

        if (key != Keys.PrintScreen) return false;
        // Leave Alt+PrtSc and Win+PrtSc to Windows.
        if (Native.KeyDown(Native.VK_MENU) || Native.KeyDown(Native.VK_LWIN) || Native.KeyDown(Native.VK_RWIN))
            return false;

        if (!down)
        {
            bool ctrl = Native.KeyDown(Native.VK_CONTROL), shift = Native.KeyDown(Native.VK_SHIFT);
            Post(() =>
            {
                if (ctrl) RepeatLast();
                else if (shift) StartSnip(Settings.DelaySeconds);
                else StartSnip();
            });
        }
        return true;
    }

    // ---------- snipping ----------

    /// <summary>Gets our own transient windows off the screen so they don't end up in the shot.</summary>
    async Task<bool> ClearStage(int waitMs)
    {
        if (busy || overlay != null || countdown != null) return false;
        if (thumb != null) { thumb.Close(); waitMs = Math.Max(waitMs, 90); }
        if (waitMs > 0)
        {
            busy = true;
            try { await Task.Delay(waitMs); } finally { busy = false; }
        }
        return overlay == null && countdown == null;
    }

    public async void StartSnip(int delaySeconds = 0, int waitMs = 0)
    {
        if (!await ClearStage(waitMs)) return;

        if (delaySeconds > 0)
        {
            countdown = new CountdownForm(delaySeconds);
            countdown.Finished += () => { countdown = null; ShowOverlay(); };
            countdown.Show();
            return;
        }
        ShowOverlay();
    }

    void ShowOverlay()
    {
        if (overlay != null) return;
        Bitmap shot;
        List<Rectangle> windows;
        var vs = SystemInformation.VirtualScreen;
        try
        {
            windows = WindowFinder.Snapshot();
            shot = ScreenCapture.Capture(vs);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            tray.ShowBalloonTip(5000, "OpenSnip", "Screen capture failed: " + ex.Message, ToolTipIcon.Error);
            return;
        }

        overlay = new CaptureOverlay(shot, vs, windows);
        overlay.Captured += (bmp, rect) => Post(() => Deliver(bmp, rect));
        overlay.FormClosed += (_, _) => { overlay = null; overlayOpen = false; };
        overlayOpen = true;
        overlay.Show();
    }

    public async void RepeatLast()
    {
        if (lastRegion == null) { StartSnip(); return; }
        if (!await ClearStage(0)) return;
        var r = Rectangle.Intersect(lastRegion.Value, SystemInformation.VirtualScreen);
        if (r.Width < 1 || r.Height < 1) { StartSnip(); return; }
        Deliver(ScreenCapture.Capture(r), r);
    }

    void Deliver(Bitmap bmp, Rectangle screenRect)
    {
        lastRegion = screenRect;
        byte[] png = ImageOps.Png(bmp);
        ImageOps.CopyToClipboard(bmp, png);

        string path = null;
        if (Settings.AutoSave)
        {
            try { path = ImageOps.SaveAuto(png, Settings.SaveFolder); }
            catch (Exception ex)
            {
                Log.Error(ex);
                tray.ShowBalloonTip(5000, "OpenSnip", "Copied, but couldn't auto-save: " + ex.Message, ToolTipIcon.Warning);
            }
        }

        if (Settings.OpenEditorAfterSnip) OpenEditor(bmp, screenRect);
        else if (Settings.ShowThumbnail)
        {
            thumb?.Close();
            var t = new ThumbnailForm(bmp, path, screenRect);
            t.FormClosed += (_, _) => { if (thumb == t) thumb = null; };
            thumb = t;
            t.Show();
        }
        else bmp.Dispose();
    }

    public void OpenEditor(Bitmap bmp, Rectangle? source)
    {
        var ed = new EditorForm(bmp, source);
        ed.Show();
        Native.ForceForeground(ed.Handle);
        ed.Activate();
    }

    public void Pin(Bitmap bmp, Point screenLocation)
    {
        var pin = new PinForm(bmp, screenLocation);
        pin.Show();
        Native.ForceForeground(pin.Handle);
        pin.Activate();
    }

    void PinClipboard()
    {
        if (Clipboard.GetImage() is Bitmap b)
        {
            var bmp = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) ImageOps.Blit(g, b, 0, 0);
            b.Dispose();
            var p = Cursor.Position;
            Pin(bmp, new Point(p.X - bmp.Width / 2, p.Y - bmp.Height / 2));
        }
        else tray.ShowBalloonTip(3000, "OpenSnip", "There's no image on the clipboard.", ToolTipIcon.Info);
    }

    void EditFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            InitialDirectory = Directory.Exists(Settings.SaveFolder) ? Settings.SaveFolder : "",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            using var src = Image.FromFile(dlg.FileName);
            var bmp = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); ImageOps.Blit(g, src, 0, 0); }
            OpenEditor(bmp, null);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't open that image:\n" + ex.Message, "OpenSnip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------- tray menu ----------

    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

        Item(m, "Snip", "PrtSc", () => StartSnip(0, 250)).Font = new Font(m.Font, FontStyle.Bold);
        var delayed = Item(m, "Snip after delay", "Shift+PrtSc", () => StartSnip(Settings.DelaySeconds, 250));
        Item(m, "Repeat last region", "Ctrl+PrtSc", () => { RepeatLastFromMenu(); });
        Item(m, "Pin image from clipboard", null, PinClipboard);
        Item(m, "Open image in editor…", null, EditFile);
        m.Items.Add(new ToolStripSeparator());

        Check(m, "Auto-save snips", () => Settings.AutoSave, v => Settings.AutoSave = v);
        Item(m, "Open snips folder", null, () =>
        {
            Directory.CreateDirectory(Settings.SaveFolder);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{Settings.SaveFolder}\"");
        });
        Item(m, "Change snips folder…", null, ChangeFolder);
        Check(m, "Show preview after snip", () => Settings.ShowThumbnail, v => Settings.ShowThumbnail = v);
        Check(m, "Open editor after every snip", () => Settings.OpenEditorAfterSnip, v => Settings.OpenEditorAfterSnip = v);

        var delayMenu = new ToolStripMenuItem("Delay length");
        foreach (int sec in new[] { 1, 3, 5, 10 })
        {
            var it = new ToolStripMenuItem($"{sec} second{(sec == 1 ? "" : "s")}") { Tag = sec };
            it.Click += (_, _) => { Settings.DelaySeconds = sec; Settings.Save(); };
            delayMenu.DropDownItems.Add(it);
        }
        m.Items.Add(delayMenu);
        Check(m, "Start with Windows", () => StartupEnabled, SetStartup);
        m.Items.Add(new ToolStripSeparator());
        Item(m, "About OpenSnip", null, () =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/DevanMetz/OpenSnip") { UseShellExecute = true }));
        Item(m, "Exit", null, Exit);

        m.Opening += (_, _) =>
        {
            delayed.Text = $"Snip in {Settings.DelaySeconds} second{(Settings.DelaySeconds == 1 ? "" : "s")}";
            foreach (ToolStripMenuItem it in delayMenu.DropDownItems) it.Checked = (int)it.Tag == Settings.DelaySeconds;
            foreach (var it in m.Items.OfType<ToolStripMenuItem>())
                if (it.Tag is Func<bool> get) it.Checked = get();
        };
        return m;
    }

    async void RepeatLastFromMenu()
    {
        await Task.Delay(250); // let the menu disappear first
        RepeatLast();
    }

    static ToolStripMenuItem Item(ContextMenuStrip m, string text, string keys, Action onClick)
    {
        var it = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys };
        it.Click += (_, _) => onClick();
        m.Items.Add(it);
        return it;
    }

    void Check(ContextMenuStrip m, string text, Func<bool> get, Action<bool> set)
    {
        var it = new ToolStripMenuItem(text) { Tag = get };
        it.Click += (_, _) =>
        {
            set(!get());
            Settings.Save();
        };
        m.Items.Add(it);
    }

    void ChangeFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Where should OpenSnip save your snips?",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(Settings.SaveFolder) ? Settings.SaveFolder : "",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            Settings.SaveFolder = dlg.SelectedPath;
            Settings.Save();
        }
    }

    static bool StartupEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("OpenSnip") is string;
        }
    }

    static void SetStartup(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue("OpenSnip", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("OpenSnip", false);
    }

    void Exit()
    {
        tray.Visible = false;
        tray.Dispose();
        Application.Exit();
    }
}
