using Avalonia;

namespace OpenSnip;

/// <summary>One recording: outline, control bar, and the encoder. The outline and bar are removed from the frames.</summary>
sealed class RecordingSession
{
    public event Action<string, CapturedFrame> Saved;
    public event Action<string> Failed;
    public event Action Ended;

    readonly Settings _settings;
    readonly string _system;
    readonly string _mic;
    readonly string _output;
    readonly string _temp;
    readonly List<string> _segments = new();
    readonly System.Diagnostics.Stopwatch _clock = new();
    FramePump _pump;
    RecordingChrome _chrome;
    string _current;
    bool _paused;
    bool _stopping;
    bool _discard;
    bool _ended;
    bool _micOn;

    public PixelRect Area { get; }
    public bool Paused => _paused;
    public bool HasMic => _mic != null;
    public bool MicOn => _micOn;
    public TimeSpan Elapsed => _clock.Elapsed;

    public RecordingSession(PixelRect area, Settings settings, string systemSource, string micSource)
    {
        Area = area;
        _settings = settings;
        _system = systemSource;
        _mic = micSource;
        _micOn = micSource != null;
        _output = FfmpegPlan.NewRecordingPath(settings.FolderFor(settings.VideoFolder));
        _temp = Path.Combine(Path.GetTempPath(), "opensnip-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Begin()
    {
        _chrome = new RecordingChrome(this);
        if (_settings.RecordCountdown) _chrome.StartCountdown(3, StartEncoder);
        _chrome.Show();
        if (!_settings.RecordCountdown) StartEncoder();
    }

    public void Poll()
    {
        if (_stopping) return;
        if (_pump != null && _pump.Failed)
            Fail(_pump.Error);
    }

    public void TogglePause()
    {
        if (_pump == null || _stopping) return;
        try
        {
            if (_paused) Resume();
            else Pause();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Fail(ex.Message);
        }
        _chrome?.Refresh();
    }

    public void ToggleMic()
    {
        if (_mic == null || _stopping) return;
        _micOn = !_micOn;
        try
        {
            if (_pump != null && !_paused)
            {
                FinishSegment();
                if (!_stopping) StartEncoder();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Fail(ex.Message);
        }
        _chrome?.Refresh();
    }

    public void Stop() => Finish(false);
    public void Discard() => Finish(true);

    void Pause()
    {
        _paused = true;
        _clock.Stop();
        FinishSegment();
    }

    void Resume()
    {
        _paused = false;
        StartEncoder();
    }

    void StartEncoder()
    {
        if (_stopping) return;
        try
        {
            _current = Path.Combine(_temp, _segments.Count + ".mp4");
            var spec = new EncodeSpec
            {
                Width = Area.Width,
                Height = Area.Height,
                Framerate = FfmpegPlan.NormalizeFramerate(_settings.RecordFramerate),
                HighQuality = _settings.RecordHighQuality,
                SystemSource = _system,
                MicSource = _mic,
                MicOn = _micOn,
                Cursor = _settings.RecordCursor,
                Output = _current,
            };
            var pump = new FramePump(Area, spec, () => _chrome?.WindowIds() ?? Array.Empty<long>());
            pump.Start();
            _pump = pump;
            if (!_clock.IsRunning) _clock.Start();
            _chrome?.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Fail(ex.Message);
        }
    }

    void Finish(bool discard)
    {
        if (_stopping) return;
        _stopping = true;
        _discard = discard;
        _clock.Stop();
        CapturedFrame preview = null;
        try
        {
            if (!discard) preview = _pump?.CopyLatest();
            FinishSegment();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            try { _chrome?.Close(); } catch { }
            _chrome = null;
            if (!discard) Fail(ex.Message);
            else End();
            return;
        }

        try { _chrome?.Close(); } catch { }
        _chrome = null;

        if (discard || _segments.Count == 0)
        {
            DeleteTemp();
            if (!discard && _segments.Count == 0)
            {
                // Stopped during the countdown, or ffmpeg never produced a file.
                if (preview == null) { End(); return; }
            }
            End();
            return;
        }

        try
        {
            string final = Assemble();
            if (final == null)
            {
                Failed?.Invoke("The recording was empty.");
                End();
                return;
            }
            Saved?.Invoke(final, preview);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Failed?.Invoke(ex.Message);
        }
        End();
    }

    void Fail(string message)
    {
        if (_ended) return;
        if (!_stopping)
        {
            _discard = true;
            _stopping = true;
            _clock.Stop();
            try { FinishSegment(); } catch (Exception ex) { Log.Error(ex); }
            try { _chrome?.Close(); } catch { }
            _chrome = null;
            DeleteTemp();
        }
        if (!string.IsNullOrWhiteSpace(message)) Failed?.Invoke(message);
        End();
    }

    void FinishSegment()
    {
        var pump = _pump;
        _pump = null;
        if (pump == null) return;
        try { pump.Stop(); }
        catch (Exception ex) { Log.Error(ex); }
        try
        {
            if (!string.IsNullOrEmpty(_current) && File.Exists(_current) && new FileInfo(_current).Length > 32)
                _segments.Add(_current);
        }
        catch (Exception ex) { Log.Error(ex); }
        try { pump.Dispose(); } catch { }
        if (pump.Failed)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(pump.Error) ? "ffmpeg failed." : pump.Error);
    }

    string Assemble()
    {
        if (_segments.Count == 0) return null;
        if (_segments.Count == 1)
        {
            File.Move(_segments[0], _output, true);
            return _output;
        }
        string list = Path.Combine(_temp, "list.txt");
        File.WriteAllText(list, FfmpegPlan.ConcatList(_segments));
        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var arg in FfmpegPlan.ConcatArguments(list, _output)) psi.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) throw new InvalidOperationException("ffmpeg did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Saving the recording timed out.");
        }
        if (process.ExitCode != 0 || !File.Exists(_output))
        {
            string detail = stderr.GetAwaiter().GetResult().Trim();
            if (detail.Length > 300) detail = detail[^300..];
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? "Could not finish the recording." : detail);
        }
        return _output;
    }

    void DeleteTemp()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); } catch { }
    }

    void End()
    {
        if (_ended) return;
        _ended = true;
        DeleteTemp();
        Ended?.Invoke();
    }
}
