using System.Diagnostics;
using System.Drawing.Drawing2D;
using ScreenRecorderLib;

namespace OpenSnip;

static class RecorderDevices
{
    public static IEnumerable<(string name, string id)> Microphones() =>
        Recorder.GetSystemAudioCaptureDevices().Select(d => (d.FriendlyName, d.DeviceName));

    /// <summary>The chosen mic if it's still plugged in, otherwise Windows' default one. Null if there's no mic.</summary>
    public static string Microphone(string preferred)
    {
        var all = Recorder.GetSystemAudioCaptureDevices();
        return all.FirstOrDefault(d => !string.IsNullOrEmpty(preferred) && d.DeviceName == preferred)?.DeviceName
            ?? all.FirstOrDefault(d => d.IsDefaultDevice)?.DeviceName
            ?? all.FirstOrDefault()?.DeviceName;
    }

    /// <summary>
    /// Loads the native recorder. Kept in its own non-inlined method so a missing Visual C++ runtime
    /// surfaces as a catchable exception at the caller instead of breaking whatever method touched it.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void EnsureAvailable() => Recorder.GetDisplays();

    public static string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"OpenSnip {typeof(RecorderDevices).Assembly.GetName().Version}");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")}");
        foreach (var sc in Screen.AllScreens)
            sb.AppendLine($"Screen {sc.DeviceName} {sc.Bounds} scale {Native.ScaleAt(sc.Bounds.Location):P0}{(sc.Primary ? " (primary)" : "")}");
        try
        {
            foreach (var d in Recorder.GetDisplays()) sb.AppendLine($"Recordable display {d.DeviceName} {d.FriendlyName}");
            foreach (var d in Recorder.GetSystemAudioLoopbackDevices()) sb.AppendLine($"Speakers: {d.FriendlyName}{(d.IsDefaultDevice ? " (default)" : "")}");
            foreach (var d in Recorder.GetSystemAudioCaptureDevices()) sb.AppendLine($"Microphone: {d.FriendlyName}{(d.IsDefaultDevice ? " (default)" : "")}");
            using var r = Recorder.CreateRecorder();
            sb.AppendLine("Recorder: OK");
        }
        catch (Exception ex) { sb.AppendLine("Recorder: FAILED " + ex); }
        return sb.ToString();
    }

    /// <summary>Windows' current default playback device, for recording "what you hear".</summary>
    public static string DefaultSpeakers()
    {
        var all = Recorder.GetSystemAudioLoopbackDevices();
        return all.FirstOrDefault(d => d.IsDefaultDevice)?.DeviceName ?? all.FirstOrDefault()?.DeviceName;
    }
}

/// <summary>
/// One screen recording: a red frame around the area, a small control bar, and the encoder.
/// The frame and bar are excluded from capture, so they never show up in the video.
/// </summary>
sealed class RecordingSession
{
    public event Action<string, Bitmap, Rectangle> Saved; // file, preview image, area
    public event Action<string> Failed;
    public event Action Ended;                            // always raised exactly once

    readonly Settings settings;
    readonly Control ui;
    readonly string path;
    readonly Stopwatch clock = new();
    Recorder rec;
    CaptureAudioSource mic;
    RecordingFrame frame;
    RecordingBar bar;
    Bitmap preview;
    bool stopping, discard, ended;

    public Rectangle Area { get; }
    public bool Started => rec != null;
    public bool Paused { get; private set; }
    public bool HasMic => mic != null;
    public bool MicOn { get; private set; }
    public TimeSpan Elapsed => clock.Elapsed;

    public RecordingSession(Rectangle area, Settings settings, Control ui)
    {
        Area = area;
        this.settings = settings;
        this.ui = ui;
        string folder = settings.FolderFor(settings.VideoFolder);
        Directory.CreateDirectory(folder);
        path = ImageOps.UniquePath(folder, $"Recording {DateTime.Now:yyyy-MM-dd HH.mm.ss}", ".mp4");
    }

    public void Begin()
    {
        frame = new RecordingFrame(Area);
        frame.Show();
        bar = new RecordingBar(this);
        bar.Show();
        if (settings.RecordCountdown) bar.StartCountdown(3, StartEncoder);
        else StartEncoder();
    }

    void StartEncoder()
    {
        if (stopping) return;
        try
        {
            rec = Recorder.CreateRecorder(BuildOptions());
            rec.OnStatusChanged += (_, e) => Ui(() => OnStatus(e.Status));
            rec.OnRecordingComplete += (_, e) => Ui(() => OnComplete(e.FilePath));
            rec.OnRecordingFailed += (_, e) => Ui(() => OnFailed(e.Error));
            rec.Record(path);
            bar.Invalidate();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            OnFailed(ex.Message);
        }
    }

    RecorderOptions BuildOptions()
    {
        var screen = Screen.FromRectangle(Area);
        var b = screen.Bounds;
        var display = new DisplayRecordingSource(screen.DeviceName)
        {
            RecorderApi = RecorderApi.DesktopDuplication,
            IsCursorCaptureEnabled = settings.RecordCursor,
            SourceRect = new ScreenRect(Area.X - b.X, Area.Y - b.Y, Area.Width, Area.Height),
        };

        var audio = new List<AudioSourceBase>();
        if (settings.RecordSystemAudio && RecorderDevices.DefaultSpeakers() is string speakers)
            audio.Add(new LoopbackAudioSource(speakers) { IsAudioCaptureEnabled = true });
        // The mic is only opened when asked for, so Windows never shows it as in use otherwise.
        if (settings.RecordMicrophone && RecorderDevices.Microphone(settings.MicrophoneDevice) is string micId)
        {
            mic = new CaptureAudioSource(micId) { IsAudioCaptureEnabled = true };
            MicOn = true;
            audio.Add(mic);
        }

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = new List<RecordingSourceBase> { display } },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(Area.Width, Area.Height),
                Stretch = StretchMode.Uniform,
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audio.Count > 0,
                AudioSources = audio,
                Bitrate = AudioBitrate.bitrate_192kbps,
                Channels = AudioChannels.Stereo,
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.Quality,
                    EncoderProfile = H264Profile.High,
                },
                Quality = settings.RecordHighQuality ? 85 : 65,
                Framerate = settings.RecordFramerate,
                IsFixedFramerate = true,
                IsHardwareEncodingEnabled = true,
                IsMp4FastStartEnabled = true, // plays/previews instantly when shared
                IsFragmentedMp4Enabled = false,
            },
        };
    }

    void Ui(Action a)
    {
        try { ui.BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    void OnStatus(RecorderStatus status)
    {
        Paused = status == RecorderStatus.Paused;
        if (status == RecorderStatus.Recording) clock.Start(); else clock.Stop();
        bar?.Invalidate();
    }

    public void TogglePause()
    {
        if (rec == null || stopping) return;
        try
        {
            if (Paused) rec.Resume(); else rec.Pause();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    public void ToggleMic()
    {
        if (rec == null || mic == null || stopping) return;
        MicOn = !MicOn;
        mic.IsAudioCaptureEnabled = MicOn;
        try { rec.GetDynamicOptionsBuilder().SetUpdatedAudioSource(mic).Apply(); }
        catch (Exception ex) { Log.Error(ex); }
        bar?.Invalidate();
    }

    public void Stop()
    {
        if (stopping) return;
        stopping = true;
        clock.Stop();
        if (rec == null) { Finish(); return; } // stopped during the countdown
        if (!discard)
        {
            try { preview = ScreenCapture.Capture(Area); } catch (Exception ex) { Log.Error(ex); }
        }
        bar?.ShowSaving();
        try { rec.Stop(); }
        catch (Exception ex) { OnFailed(ex.Message); }
    }

    public void Discard()
    {
        discard = true;
        Stop();
    }

    void OnComplete(string file)
    {
        if (ended) return;
        if (discard)
        {
            try { File.Delete(file); } catch { }
        }
        else Saved?.Invoke(file, preview, Area);
        preview = null; // ownership passed on
        Finish();
    }

    void OnFailed(string error)
    {
        if (ended) return;
        if (string.IsNullOrWhiteSpace(error)) error = "Unknown error";
        if (path.Length >= 240) // the video writer can't handle very long paths
            error += ". The recordings folder path is very long, so try a shorter one in Settings.";
        if (!discard) Failed?.Invoke(error);
        Finish();
    }

    void Finish()
    {
        if (ended) return;
        ended = true;
        frame?.Close();
        bar?.Close();
        preview?.Dispose();
        var r = rec;
        rec = null;
        if (r != null) Ui(() => { try { r.Dispose(); } catch { } });
        Ended?.Invoke();
    }
}

/// <summary>Red outline just outside the recorded area. Click-through and invisible to capture.</summary>
sealed class RecordingFrame : Popup
{
    readonly int bw;

    public RecordingFrame(Rectangle area) : base(new Point(area.X + area.Width / 2, area.Y + area.Height / 2))
    {
        bw = Math.Max(2, (int)Math.Round(2 * s));
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Bounds = Rectangle.Inflate(area, bw, bw);
    }

    protected override bool ClickThrough => true;
    protected override int CornerPreference => Native.DWMWCP_DONOTROUND;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Magenta);
        using var b = new SolidBrush(Theme.Record);
        g.FillRectangle(b, 0, 0, Width, bw);
        g.FillRectangle(b, 0, Height - bw, Width, bw);
        g.FillRectangle(b, 0, 0, bw, Height);
        g.FillRectangle(b, Width - bw, 0, bw, Height);
    }
}

/// <summary>Floating controls while recording: time, pause, mic, stop, discard.</summary>
sealed class RecordingBar : Popup
{
    readonly RecordingSession session;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 250 };
    readonly Font font, bold;
    readonly List<(string id, Rectangle r)> hits = new();
    int countdown;
    Action afterCountdown;
    bool saving;
    string hot;
    bool dragging;
    Point grab;

    public RecordingBar(RecordingSession session) : base(new Point(session.Area.X + session.Area.Width / 2, session.Area.Bottom))
    {
        this.session = session;
        font = new Font("Segoe UI", 13f * s, GraphicsUnit.Pixel);
        bold = new Font("Segoe UI Semibold", 13f * s, GraphicsUnit.Pixel);
        Size = new Size(Seg("time") + Seg("pause") + (session.HasMic || WillHaveMic ? Seg("mic") : 0) + Seg("stop") + Seg("discard") + Pad * 2,
                        (int)(42 * s));
        Place();
        tick.Tick += (_, _) => Invalidate();
        tick.Start();
    }

    static bool WillHaveMic => TrayApp.Instance?.Settings.RecordMicrophone == true;
    int Pad => (int)(6 * s);

    int Seg(string id)
    {
        string widest = id switch
        {
            "time" => "●  00:00:00",
            "pause" => "▶  Resume",
            "mic" => "Mic off",
            "stop" => "■  Stop",
            _ => "✕",
        };
        return TextRenderer.MeasureText(widest, bold).Width + (int)(22 * s);
    }

    void Place()
    {
        var area = session.Area;
        var wa = Screen.FromRectangle(area).WorkingArea;
        int gap = (int)(10 * s);
        int x = Math.Clamp(area.X + (area.Width - Width) / 2, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        int y = area.Bottom + gap;
        if (y + Height > wa.Bottom) y = area.Top - gap - Height;          // above the area
        if (y < wa.Top) y = area.Bottom - gap - Height;                   // inside (it's invisible to capture)
        Location = new Point(x, y);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle);
    }

    public void StartCountdown(int seconds, Action then)
    {
        countdown = seconds;
        afterCountdown = then;
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) =>
        {
            if (IsDisposed || afterCountdown == null) { t.Dispose(); return; }
            countdown--;
            Invalidate();
            if (countdown > 0) return;
            t.Dispose();
            var go = afterCountdown;
            afterCountdown = null;
            go();
        };
        t.Start();
        Invalidate();
    }

    public void ShowSaving()
    {
        saving = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back);
        hits.Clear();
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        var full = new Rectangle(Pad, 0, Width - Pad * 2, Height);

        if (saving)
        {
            TextRenderer.DrawText(g, "Saving recording…", bold, full, Theme.Text, flags);
            return;
        }
        if (afterCountdown != null)
        {
            int cancelW = Seg("stop");
            var text = new Rectangle(full.X, 0, full.Width - cancelW, Height);
            TextRenderer.DrawText(g, $"Recording starts in {countdown}…", bold, text, Theme.Text, flags);
            Button(g, "cancel", "Cancel", new Rectangle(text.Right, 0, cancelW, Height), Theme.Text);
            return;
        }

        int x = Pad;
        // Elapsed time with a blinking dot (steady when paused).
        var timeR = new Rectangle(x, 0, Seg("time"), Height);
        bool blinkOn = session.Paused || DateTime.Now.Millisecond < 600;
        var t = session.Elapsed;
        string time = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        TextRenderer.DrawText(g, (blinkOn ? "●" : "  ") + "  " + time, bold, timeR,
            session.Paused ? Theme.Muted : Theme.Record, flags);
        hits.Add(("grip", timeR));
        x = timeR.Right;

        Button(g, "pause", session.Paused ? "▶  Resume" : "❚❚  Pause", new Rectangle(x, 0, Seg("pause"), Height), Theme.Text);
        x += Seg("pause");
        if (session.HasMic)
        {
            Button(g, "mic", session.MicOn ? "Mic on" : "Mic off", new Rectangle(x, 0, Seg("mic"), Height),
                session.MicOn ? Theme.Good : Theme.Muted);
            x += Seg("mic");
        }
        Button(g, "stop", "■  Stop", new Rectangle(x, 0, Seg("stop"), Height), Theme.Record);
        x += Seg("stop");
        Button(g, "discard", "✕", new Rectangle(x, 0, Seg("discard"), Height), Theme.Muted);
    }

    void Button(Graphics g, string id, string text, Rectangle r, Color color)
    {
        hits.Add((id, r));
        if (hot == id)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ImageOps.RoundedRect(Rectangle.Inflate(r, -(int)(2 * s), -(int)(5 * s)), 6 * s);
            using var b = new SolidBrush(Color.FromArgb(60, 60, 70));
            g.FillPath(b, path);
        }
        TextRenderer.DrawText(g, text, bold, r, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    string HitAt(Point p) => hits.FirstOrDefault(h => h.r.Contains(p)).id;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            var c = Cursor.Position;
            Location = new Point(c.X - grab.X, c.Y - grab.Y);
            return;
        }
        var h = HitAt(e.Location);
        Cursor = h == "grip" ? Cursors.SizeAll : h != null ? Cursors.Hand : Cursors.Default;
        if (h != hot) { hot = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hot = null;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitAt(e.Location) == "grip") { dragging = true; grab = e.Location; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging) { dragging = false; return; }
        if (e.Button != MouseButtons.Left || saving) return;
        switch (HitAt(e.Location))
        {
            case "cancel": session.Discard(); break;
            case "pause": session.TogglePause(); break;
            case "mic": session.ToggleMic(); break;
            case "stop": session.Stop(); break;
            case "discard": ConfirmDiscard(); break;
        }
    }

    void ConfirmDiscard()
    {
        bool wasPaused = session.Paused;
        if (!wasPaused) session.TogglePause();
        var answer = MessageBox.Show(this, "Throw away this recording?", "OpenSnip",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) session.Discard();
        else if (!wasPaused) session.TogglePause();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Dispose();
        font.Dispose();
        bold.Dispose();
        base.OnFormClosed(e);
    }
}
