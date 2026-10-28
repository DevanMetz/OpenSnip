using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Avalonia;
using Tmds.DBus.Protocol;

namespace OpenSnip;

/// <summary>The connection and PipeWire remote stay alive across pause and mute segments.</summary>
sealed class PortalScreenCast : IDisposable
{
    readonly Connection _connection;
    readonly string _session;
    readonly SafeFileHandle _remote;
    readonly uint _node;
    readonly PixelRect? _logical;
    PixelRect _monitor;
    PixelRect _crop;
    bool _disposed;
    static readonly object SpawnGate = new();

    internal PortalScreenCast(Connection connection, string session, SafeFileHandle remote, uint node, PixelRect? logical)
    {
        _connection = connection;
        _session = session;
        _remote = remote;
        _node = node;
        _logical = logical;
    }

    public static async Task<PortalScreenCast> OpenAsync(bool cursor)
    {
        foreach (string tool in new[] { "gst-launch-1.0", "gst-inspect-1.0" })
            if (!PlatformInfo.CommandExists(tool))
                throw new InvalidOperationException("Wayland recording needs GStreamer and its PipeWire plugin.");
        foreach (string plugin in new[] { "pipewiresrc", "videoconvert", "videoscale", "videocrop", "videorate", "fdsink" })
        {
            using var check = Process.Start(new ProcessStartInfo("gst-inspect-1.0", plugin)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            var stdout = check.StandardOutput.ReadToEndAsync();
            var stderr = check.StandardError.ReadToEndAsync();
            try { await check.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                try { check.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("GStreamer did not respond.");
            }
            await Task.WhenAll(stdout, stderr);
            if (check.ExitCode != 0)
                throw new InvalidOperationException($"Wayland recording needs the GStreamer {plugin} plugin.");
        }
        return await DBus.OpenScreenCastAsync(cursor);
    }

    public void SetRegion(PixelRect area, IReadOnlyList<MonitorLayout> layouts)
    {
        var matches = layouts.Where(m => RecordGeometry.Contains(m.Bounds, area) &&
            (_logical == null ? layouts.Count == 1 : Matches(_logical.Value, m))).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Select the same monitor in the sharing dialog and recording selection.");
        _monitor = matches[0].Bounds;
        _crop = Crop(area, _monitor);
    }

    internal static bool Matches(PixelRect logical, MonitorLayout monitor)
    {
        double scale = monitor.ScaleOrOne;
        return Math.Abs(logical.X - monitor.Bounds.X / scale) <= 2 &&
            Math.Abs(logical.Y - monitor.Bounds.Y / scale) <= 2 &&
            Math.Abs(logical.Width - monitor.Bounds.Width / scale) <= 2 &&
            Math.Abs(logical.Height - monitor.Bounds.Height / scale) <= 2;
    }

    internal static PixelRect Crop(PixelRect area, PixelRect monitor)
    {
        if (!RecordGeometry.Contains(monitor, area) || area.Width < 16 || area.Height < 16 ||
            (area.Width & 1) != 0 || (area.Height & 1) != 0)
            throw new InvalidOperationException("The recording region must fit the shared monitor.");
        return new PixelRect(area.X - monitor.X, area.Y - monitor.Y, area.Width, area.Height);
    }

    internal static string[] Arguments(int fd, uint node, PixelRect monitor, PixelRect crop, int fps) =>
    [
        "-q", "pipewiresrc", $"fd={fd}", $"path={node}", "do-timestamp=true", "!",
        "videoconvert", "!", "videoscale", "!",
        $"video/x-raw,format=BGRA,width={monitor.Width},height={monitor.Height},pixel-aspect-ratio=1/1", "!",
        "videocrop", $"left={crop.X}", $"top={crop.Y}",
        $"right={monitor.Width - crop.Right}", $"bottom={monitor.Height - crop.Bottom}", "!",
        "videorate", "!", $"video/x-raw,format=BGRA,framerate={fps}/1", "!", "fdsink", "fd=1"
    ];

    public Process StartFrames(int fps)
    {
        // dup creates an inheritable descriptor; only expose it while launching this child.
        lock (SpawnGate)
        {
            int fd = dup(_remote.DangerousGetHandle().ToInt32());
            if (fd < 0) throw new InvalidOperationException("Could not open the PipeWire remote.");
            try
            {
                var psi = new ProcessStartInfo("gst-launch-1.0")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (string arg in Arguments(fd, _node, _monitor, _crop, fps)) psi.ArgumentList.Add(arg);
                return Process.Start(psi) ?? throw new InvalidOperationException("GStreamer did not start.");
            }
            finally { close(fd); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _remote.Dispose();
        try { DBus.CloseScreenCastAsync(_connection, _session).GetAwaiter().GetResult(); } catch { }
        _connection.Dispose();
    }

    [DllImport("libc", SetLastError = true)] static extern int dup(int fd);
    [DllImport("libc")] static extern int close(int fd);
}

static partial class DBus
{
    const string Portal = "org.freedesktop.portal.Desktop";
    const string Desktop = "/org/freedesktop/portal/desktop";
    const string ScreenCast = "org.freedesktop.portal.ScreenCast";

    internal static async Task CloseScreenCastAsync(Connection connection, string session)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Portal, session, "org.freedesktop.portal.Session", "Close", "", MessageFlags.None);
        await connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(TimeSpan.FromSeconds(3));
    }

    internal static async Task<PortalScreenCast> OpenScreenCastAsync(bool cursor)
    {
        var connection = await ConnectAsync();
        string session = null;
        SafeFileHandle remote = null;
        try
        {
            var responses = new ConcurrentDictionary<string, PortalResponse>();
            using var signal = new SemaphoreSlim(0);
            using var subscription = await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Sender = Portal, Interface = "org.freedesktop.portal.Request", Member = "Response" },
                (Message message, object state) =>
                {
                    var reader = message.GetBodyReader();
                    return new PortalResponse(message.PathAsString, reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
                },
                (Exception error, PortalResponse value, object readerState, object handlerState) =>
                {
                    if (value != null) { responses[value.Path] = value; signal.Release(); }
                }, ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false);

            async Task<Dictionary<string, VariantValue>> Request(string method, Dictionary<string, VariantValue> options)
            {
                options["handle_token"] = VariantValue.String("opensnip_" + Guid.NewGuid().ToString("N"));
                var writer = connection.GetMessageWriter();
                string signature = method == "CreateSession" ? "a{sv}" : method == "Start" ? "osa{sv}" : "oa{sv}";
                writer.WriteMethodCallHeader(Portal, Desktop, ScreenCast, method, signature, MessageFlags.None);
                if (method != "CreateSession") writer.WriteObjectPath(session);
                if (method == "Start") writer.WriteString("");
                writer.WriteDictionary(options);
                string path = await connection.CallMethodAsync(writer.CreateMessage(),
                    (Message m, object s) => m.GetBodyReader().ReadObjectPathAsString(), null).WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    while (true)
                    {
                        if (responses.TryRemove(path, out var response))
                        {
                            if (response.Code != 0)
                                throw new InvalidOperationException(response.Code == 1 ? "Screen sharing was cancelled." : "Screen sharing failed.");
                            return response.Results;
                        }
                        try { await signal.WaitAsync(timeout.Token); }
                        catch (OperationCanceledException) { throw new InvalidOperationException("Screen sharing timed out."); }
                    }
                }
                catch
                {
                    var close = connection.GetMessageWriter();
                    close.WriteMethodCallHeader(Portal, path, "org.freedesktop.portal.Request", "Close", "", MessageFlags.None);
                    try { await connection.CallMethodAsync(close.CreateMessage()).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
                    throw;
                }
            }

            async Task<uint> Property(string name)
            {
                var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(Portal, Desktop, "org.freedesktop.DBus.Properties", "Get", "ss", MessageFlags.None);
                writer.WriteString(ScreenCast);
                writer.WriteString(name);
                return await connection.CallMethodAsync(writer.CreateMessage(),
                    (Message m, object s) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null).WaitAsync(TimeSpan.FromSeconds(5));
            }

            uint version = await Property("version");
            var created = await Request("CreateSession", new()
            {
                ["session_handle_token"] = VariantValue.String("opensnip_" + Guid.NewGuid().ToString("N")),
            });
            session = created["session_handle"].GetString();
            var options = new Dictionary<string, VariantValue>
            {
                ["types"] = VariantValue.UInt32(1), ["multiple"] = VariantValue.Bool(false),
            };
            if (version >= 2)
            {
                uint modes = await Property("AvailableCursorModes");
                uint mode = cursor && (modes & 2) != 0 ? 2u : 1u;
                if ((modes & mode) == 0) throw new InvalidOperationException("The portal cannot provide the requested cursor mode.");
                options["cursor_mode"] = VariantValue.UInt32(mode);
            }
            string tokenPath = Path.Combine(ConfigPaths.Dir, "screencast-token");
            if (version >= 4)
            {
                options["persist_mode"] = VariantValue.UInt32(2);
                try
                {
                    if (File.Exists(tokenPath)) options["restore_token"] = VariantValue.String(File.ReadAllText(tokenPath).Trim());
                }
                catch (Exception ex) { Log.Error(ex); }
            }
            await Request("SelectSources", options);
            var started = await Request("Start", new());
            try
            {
                if (started.TryGetValue("restore_token", out var token))
                {
                    Directory.CreateDirectory(ConfigPaths.Dir);
                    string temp = tokenPath + ".tmp";
                    File.WriteAllText(temp, token.GetString());
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    File.Move(temp, tokenPath, true);
                }
                else if (File.Exists(tokenPath)) File.Delete(tokenPath);
            }
            catch (Exception ex) { Log.Error(ex); }
            var streams = started["streams"];
            if (streams.Count != 1) throw new InvalidOperationException("Share one monitor to record.");
            var stream = streams.GetItem(0);
            uint node = stream.GetItem(0).GetUInt32();
            var properties = stream.GetItem(1).GetDictionary<string, VariantValue>();
            PixelRect? logical = null;
            if (properties.TryGetValue("position", out var position) && properties.TryGetValue("size", out var size))
                logical = new PixelRect(position.GetItem(0).GetInt32(), position.GetItem(1).GetInt32(),
                    size.GetItem(0).GetInt32(), size.GetItem(1).GetInt32());

            var open = connection.GetMessageWriter();
            open.WriteMethodCallHeader(Portal, Desktop, ScreenCast, "OpenPipeWireRemote", "oa{sv}", MessageFlags.None);
            open.WriteObjectPath(session);
            open.WriteDictionary(new Dictionary<string, VariantValue>());
            remote = await connection.CallMethodAsync(open.CreateMessage(),
                (Message m, object s) => m.GetBodyReader().ReadHandle<SafeFileHandle>(), null).WaitAsync(TimeSpan.FromSeconds(5));
            return new PortalScreenCast(connection, session, remote, node, logical);
        }
        catch
        {
            remote?.Dispose();
            if (session != null) try { await CloseScreenCastAsync(connection, session); } catch { }
            connection.Dispose();
            throw;
        }
    }
}
