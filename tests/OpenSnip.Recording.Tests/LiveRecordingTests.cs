using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace OpenSnip.Recording.Tests;

[Collection("x11")]
public class LiveRecordingTests
{
    [Fact]
    public void Grab_PaintsOutAnExcludedWindow()
    {
        if (!HasDisplay()) return;
        using var red = XWin.Open(48, 48, 220, 160, 65535, 0, 0);
        using var blue = XWin.Open(90, 80, 80, 60, 0, 0, 65535);
        Thread.Sleep(150);
        using var source = new X11FrameSource(48, 48, 220, 160);
        var buffer = new byte[220 * 160 * 4];

        Assert.True(source.Grab(buffer, null, false));
        var covered = Pixel(buffer, 220, 70, 50);
        Assert.True(covered.B > 200 && covered.R < 30, $"blue window was {covered}");

        Assert.True(source.Grab(buffer, new[] { blue.Id }, false));
        var rebuilt = Pixel(buffer, 220, 70, 50);
        Assert.True(rebuilt.R > 200 && rebuilt.B < 30, $"excluded window stayed {rebuilt}");

        Assert.True(source.Grab(buffer, new[] { blue.Id }, false));
        var again = Pixel(buffer, 220, 70, 50);
        Assert.True(again.R > 200 && again.B < 30, $"second grab was {again}");

        Assert.True(source.Grab(buffer, new[] { blue.Id }, cursor: true));
    }

    [Fact]
    public void FramePump_RecordsARedWindow()
    {
        if (!HasDisplay() || !FfmpegPlan.Available) return;
        using var red = XWin.Open(70, 70, 160, 120, 65535, 0, 0);
        Thread.Sleep(100);
        string dir = Path.Combine(Path.GetTempPath(), "opensnip-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string output = Path.Combine(dir, "clip.mp4");
        var pump = new FramePump(new PixelRect(70, 70, 160, 120), new EncodeSpec
        {
            Width = 160,
            Height = 120,
            Framerate = 10,
            HighQuality = true,
            Cursor = false,
            Output = output,
        }, () => Array.Empty<long>());
        try
        {
            pump.Start();
            Thread.Sleep(800);
            pump.Stop();
            Assert.False(pump.Failed, pump.Error);
            Assert.True(new FileInfo(output).Length > 32);
            var rgb = ExtractFrame(output);
            int pixels = rgb.Length / 3;
            int reds = 0;
            for (int i = 0; i < pixels; i++)
            {
                if (rgb[i * 3] > 180 && rgb[i * 3 + 1] < 50 && rgb[i * 3 + 2] < 50) reds++;
            }
            Assert.True(reds > pixels / 2, $"red pixels {reds} of {pixels}");
        }
        finally
        {
            pump.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Concat_JoinsTwoSegments()
    {
        if (!HasDisplay() || !FfmpegPlan.Available) return;
        using var red = XWin.Open(80, 80, 64, 48, 65535, 0, 0);
        string dir = Path.Combine(Path.GetTempPath(), "opensnip-concat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string first = Path.Combine(dir, "0.mp4");
            string second = Path.Combine(dir, "1.mp4");
            RecordClip(new PixelRect(80, 80, 64, 48), first);
            RecordClip(new PixelRect(80, 80, 64, 48), second);
            string list = Path.Combine(dir, "list.txt");
            File.WriteAllText(list, FfmpegPlan.ConcatList(new[] { first, second }));
            string output = Path.Combine(dir, "all.mp4");
            var psi = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var arg in FfmpegPlan.ConcatArguments(list, output)) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi);
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(15000));
            Assert.True(process.ExitCode == 0 && new FileInfo(output).Length > 32, await stderr);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void RecordClip(PixelRect area, string output)
    {
        var pump = new FramePump(area, new EncodeSpec
        {
            Width = area.Width,
            Height = area.Height,
            Framerate = 10,
            HighQuality = false,
            Cursor = false,
            Output = output,
        }, () => Array.Empty<long>());
        try
        {
            pump.Start();
            Thread.Sleep(400);
            pump.Stop();
            Assert.False(pump.Failed, pump.Error);
        }
        finally { pump.Dispose(); }
    }

    [Fact]
    public async Task Session_SavesAClipFromTheControlBar()
    {
        if (!HasDisplay() || !FfmpegPlan.Available) return;
        using var red = XWin.Open(120, 120, 96, 64, 65535, 0, 0);
        Thread.Sleep(80);
        string dir = Path.Combine(Path.GetTempPath(), "opensnip-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => SessionHost.Run(dir, done)) { IsBackground = true, Name = "OpenSnip session test" };
        thread.Start();
        var finished = await Task.WhenAny(done.Task, Task.Delay(20000));
        Assert.Same(done.Task, finished);
        string path = await done.Task;
        try
        {
            Assert.True(new FileInfo(path).Length > 32);
            var rgb = ExtractFrame(path);
            int pixels = rgb.Length / 3;
            int reds = 0;
            for (int i = 0; i < pixels; i++)
                if (rgb[i * 3] > 180 && rgb[i * 3 + 1] < 50 && rgb[i * 3 + 2] < 50) reds++;
            Assert.True(reds > pixels / 2, $"red pixels {reds} of {pixels}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Monitors_Query_ReturnsAScreen()
    {
        if (!HasDisplay()) return;
        var monitors = X11Monitors.Query();
        Assert.NotEmpty(monitors);
        Assert.True(monitors[0].Width >= 16 && monitors[0].Height >= 16);
        var work = X11Monitors.WorkArea(monitors[0]);
        Assert.True(work.Width >= 16 && work.Height >= 16);
    }

    static bool HasDisplay() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

    static (byte B, byte G, byte R) Pixel(byte[] bgra, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2]);
    }

    static byte[] ExtractFrame(string path)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-ss", "0.2", "-i", path, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-" })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        var stderr = process.StandardError.ReadToEndAsync();
        using var memory = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(memory);
        if (!process.WaitForExit(8000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Reading the recording back timed out.");
        }
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || memory.Length < 3)
            throw new InvalidOperationException("Could not read the recording back: " + error);
        return memory.ToArray();
    }

    sealed class XWin : IDisposable
    {
        IntPtr _display;
        public long Id { get; }

        XWin(IntPtr display, long id)
        {
            _display = display;
            Id = id;
        }

        public static XWin Open(int x, int y, int width, int height, ushort red, ushort green, ushort blue)
        {
            var display = XOpenDisplay(null);
            if (display == IntPtr.Zero) throw new InvalidOperationException("Cannot open the X11 display.");
            XSetErrorHandler(Ignore);
            int screen = XDefaultScreen(display);
            var color = new XColor { Red = red, Green = green, Blue = blue, Flags = 7 };
            IntPtr pixel = (IntPtr)0x00FF0000;
            if (XAllocColor(display, XDefaultColormap(display, screen), ref color) != 0)
                pixel = (IntPtr)(long)color.Pixel;
            var window = XCreateSimpleWindow(display, XRootWindow(display, screen), x, y, (uint)width, (uint)height, 0, pixel, pixel);
            var bytes = new byte[112];
            bytes[88] = 1;
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { XChangeWindowAttributes(display, window, 512, pin.AddrOfPinnedObject()); }
            finally { pin.Free(); }
            XMapWindow(display, window);
            XFlush(display);
            XSync(display, false);
            return new XWin(display, window.ToInt64());
        }

        public void Dispose()
        {
            if (_display == IntPtr.Zero) return;
            try { XDestroyWindow(_display, (IntPtr)Id); XFlush(_display); XCloseDisplay(_display); }
            catch { }
            _display = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XColor
        {
            public nuint Pixel;
            public ushort Red, Green, Blue;
            public byte Flags, Pad;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int XErrorHandler(IntPtr display, IntPtr error);
        static readonly XErrorHandler Ignore = (_, _) => 0;

        [DllImport("libX11")] static extern IntPtr XOpenDisplay(string name);
        [DllImport("libX11")] static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11")] static extern IntPtr XSetErrorHandler(XErrorHandler handler);
        [DllImport("libX11")] static extern int XDefaultScreen(IntPtr display);
        [DllImport("libX11")] static extern IntPtr XRootWindow(IntPtr display, int screen);
        [DllImport("libX11")] static extern IntPtr XDefaultColormap(IntPtr display, int screen);
        [DllImport("libX11")] static extern int XAllocColor(IntPtr display, IntPtr colormap, ref XColor color);
        [DllImport("libX11")] static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint border, IntPtr borderPixel, IntPtr background);
        [DllImport("libX11")] static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, ulong mask, IntPtr attributes);
        [DllImport("libX11")] static extern int XMapWindow(IntPtr display, IntPtr window);
        [DllImport("libX11")] static extern int XDestroyWindow(IntPtr display, IntPtr window);
        [DllImport("libX11")] static extern int XFlush(IntPtr display);
        [DllImport("libX11")] static extern int XSync(IntPtr display, bool discard);
    }
}

[CollectionDefinition("x11", DisableParallelization = true)]
public class X11Collection { }

static class SessionHost
{
    public static void Run(string folder, TaskCompletionSource<string> done)
    {
        try
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SessionApp.Start(lifetime, folder, done);
            AppBuilder.Configure<SessionApp>()
                .UsePlatformDetect()
                .SetupWithLifetime(lifetime);
            if (!done.Task.IsCompleted) lifetime.Start(Array.Empty<string>());
            if (!done.Task.IsCompleted)
                done.TrySetException(new InvalidOperationException("The recording window closed without a file."));
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
    }
}

sealed class SessionApp : Application
{
    static ClassicDesktopStyleApplicationLifetime _lifetime;
    static string _folder;
    static TaskCompletionSource<string> _done;

    public static void Start(ClassicDesktopStyleApplicationLifetime lifetime, string folder, TaskCompletionSource<string> done)
    {
        _lifetime = lifetime;
        _folder = folder;
        _done = done;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = new Settings
        {
            VideoFolder = _folder,
            MonthlyFolders = false,
            RecordCountdown = false,
            RecordSystemAudio = false,
            RecordMicrophone = false,
            RecordCursor = false,
            CopyRecordingToClipboard = false,
            ShowThumbnail = false,
            RecordHighQuality = false,
            RecordFramerate = 10,
        };
        var session = new RecordingSession(new PixelRect(120, 120, 96, 64), settings, null, null);
        session.Saved += (path, _) => _done.TrySetResult(path);
        session.Failed += message => _done.TrySetException(new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Recording failed." : message));
        session.Ended += () =>
        {
            if (!_done.Task.IsCompleted) _done.TrySetException(new InvalidOperationException("Recording ended without a file."));
            _lifetime.Shutdown();
        };
        session.Begin();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            session.Stop();
        };
        timer.Start();
        base.OnFrameworkInitializationCompleted();
    }
}
