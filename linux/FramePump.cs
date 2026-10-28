using System.Diagnostics;
using System.Text;
using Avalonia;

namespace OpenSnip;

/// <summary>
/// Feeds BGRA frames to one ffmpeg process. Stopping closes ffmpeg's input so the MP4 is finalized.
/// Pause is a stop plus a new segment: a live PulseAudio input would keep running if the process stayed up.
/// </summary>
sealed class FramePump : IDisposable
{
    readonly PixelRect _area;
    readonly EncodeSpec _spec;
    readonly Func<long[]> _exclude;
    readonly object _gate = new();
    readonly StringBuilder _stderr = new();
    readonly ManualResetEventSlim _started = new(false);
    Thread _thread;
    Process _process;
    Stream _input;
    byte[] _latest;
    string _error;
    volatile bool _run;
    volatile bool _failed;
    bool _disposed;

    public FramePump(PixelRect area, EncodeSpec spec, Func<long[]> exclude)
    {
        _area = area;
        _spec = spec;
        _exclude = exclude;
    }

    public bool Failed => _failed;
    public string Error => _error;

    public void Start()
    {
        _run = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "OpenSnip recording" };
        _thread.Start();
        if (!_started.Wait(8000))
            throw new InvalidOperationException(_error ?? "ffmpeg did not start.");
        if (_failed)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(_error) ? "ffmpeg did not start." : _error);
    }

    public CapturedFrame CopyLatest()
    {
        lock (_gate)
        {
            if (_latest == null) return null;
            var copy = new byte[_latest.Length];
            Buffer.BlockCopy(_latest, 0, copy, 0, copy.Length);
            return new CapturedFrame(_area.Width, _area.Height, copy);
        }
    }

    public void Stop()
    {
        _run = false;
        if (_thread != null && !_thread.Join(6000))
        {
            try { _process?.Kill(entireProcessTree: true); } catch { }
            _thread.Join(2000);
        }
        WaitForExit();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Stop(); } catch { }
        _started.Dispose();
        try { _process?.Dispose(); } catch { }
    }

    void Run()
    {
        X11FrameSource source = null;
        try
        {
            source = new X11FrameSource(_area.X, _area.Y, _area.Width, _area.Height);
            var psi = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in FfmpegPlan.EncodeArguments(_spec)) psi.ArgumentList.Add(arg);
            _process = Process.Start(psi);
            if (_process == null)
            {
                Fail("ffmpeg did not start.");
                return;
            }
            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (_stderr) _stderr.AppendLine(e.Data);
            };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _input = _process.StandardInput.BaseStream;

            if (_process.WaitForExit(200))
            {
                Fail(Stderr("ffmpeg failed."));
                return;
            }

            var buffer = new byte[_area.Width * _area.Height * 4];
            _started.Set();
            long interval = Stopwatch.Frequency / Math.Max(1, _spec.Framerate);
            long next = Stopwatch.GetTimestamp();
            while (_run)
            {
                long now = Stopwatch.GetTimestamp();
                if (now < next)
                {
                    int wait = (int)((next - now) * 1000 / Stopwatch.Frequency);
                    if (wait > 2) Thread.Sleep(wait - 1);
                    while (_run && Stopwatch.GetTimestamp() < next) { }
                    if (!_run) break;
                }
                next += interval;
                long after = Stopwatch.GetTimestamp();
                if (next < after) next = after + interval;

                long[] exclude = Array.Empty<long>();
                try { exclude = _exclude?.Invoke() ?? exclude; }
                catch { exclude = Array.Empty<long>(); }

                if (!source.Grab(buffer, exclude, _spec.Cursor))
                {
                    Fail("Screen capture failed.");
                    break;
                }
                try
                {
                    _input.Write(buffer, 0, buffer.Length);
                    _input.Flush();
                }
                catch (Exception ex)
                {
                    if (_run) Fail(Stderr(ex.Message));
                    break;
                }
                lock (_gate)
                {
                    _latest ??= new byte[buffer.Length];
                    Buffer.BlockCopy(buffer, 0, _latest, 0, buffer.Length);
                }
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            Log.Error(ex);
        }
        finally
        {
            try { _input?.Close(); } catch { }
            _started.Set();
            try { source?.Dispose(); } catch { }
        }
    }

    void Fail(string message)
    {
        if (string.IsNullOrWhiteSpace(_error)) _error = string.IsNullOrWhiteSpace(message) ? "Recording failed." : message;
        _failed = true;
        _run = false;
    }

    string Stderr(string fallback)
    {
        string text;
        lock (_stderr) text = _stderr.ToString().Trim();
        if (text.Length > 500) text = text[^500..];
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    void WaitForExit()
    {
        var process = _process;
        if (process == null) return;
        try
        {
            if (!process.HasExited && !process.WaitForExit(8000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                process.WaitForExit(2000);
            }
        }
        catch { }
        try
        {
            if (process.HasExited && process.ExitCode != 0 && !_failed)
                Fail(Stderr("ffmpeg failed."));
        }
        catch { }
    }
}
