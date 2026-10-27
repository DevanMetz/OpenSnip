using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace OpenSnip;

/// <summary>One running OpenSnip per user, reached through a Unix domain socket.</summary>
static class SingleInstance
{
    static Socket _listener;
    static string _socketPath;
    static CancellationTokenSource _cts;
    static readonly ConcurrentQueue<LaunchAction> Pending = new();
    static readonly object HandlerLock = new();
    static Action<LaunchAction> _handler;

    public static bool IsFirst { get; private set; }
    public static string SocketPath => _socketPath;

    public static bool TryStart()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string path = BuildSocketPath();
            if (TryConnect(path))
            {
                IsFirst = false;
                _socketPath = path;
                return false;
            }

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    dir = "/tmp";
                if (!string.IsNullOrEmpty(dir) && !IsWritable(dir))
                    throw new IOException($"Cannot write to {dir}");

                try { if (File.Exists(path)) File.Delete(path); } catch { }

                var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(path));
                listener.Listen(8);
                try { if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }

                _listener = listener;
                _socketPath = path;
                IsFirst = true;
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                if (attempt == 0) continue;
                Log.Error(ex);
                return true; // Keep running even if single-instance support is unavailable.
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return true;
            }
        }
        return true;
    }

    static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".opensnip-write-{Environment.ProcessId}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch { return false; }
    }

    static string BuildSocketPath()
    {
        string dir = null;
        try
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(xdg) && Directory.Exists(xdg) && IsWritable(xdg)) dir = xdg;
        }
        catch { }
        dir ??= "/tmp";
        string path = Path.Combine(dir, $"opensnip-{PlatformInfo.UserId}.sock");
        // Unix domain socket paths are limited to ~108 bytes.
        if (Encoding.UTF8.GetByteCount(path) > 100)
            path = Path.Combine("/tmp", $"opensnip-{PlatformInfo.UserId}.sock");
        return path;
    }

    static bool TryConnect(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var candidate = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var cts = new CancellationTokenSource(800);
            candidate.ConnectAsync(new UnixDomainSocketEndPoint(path), cts.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Send(LaunchAction action, out string error)
    {
        error = null;
        string path = _socketPath ?? BuildSocketPath();
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var cts = new CancellationTokenSource(1500);
            socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cts.Token).AsTask().GetAwaiter().GetResult();
            var payload = Encoding.UTF8.GetBytes(CommandName(action) + "\n");
            socket.Send(payload);
            try { socket.Shutdown(SocketShutdown.Send); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string CommandName(LaunchAction action) => action switch
    {
        LaunchAction.SnipDelay => "snip-delay",
        LaunchAction.Repeat => "repeat",
        LaunchAction.Settings => "settings",
        LaunchAction.Record => "record",
        _ => "snip",
    };

    static LaunchAction ParseCommand(string line) => line.Trim() switch
    {
        "snip-delay" => LaunchAction.SnipDelay,
        "repeat" => LaunchAction.Repeat,
        "settings" => LaunchAction.Settings,
        "record" => LaunchAction.Record,
        _ => LaunchAction.Snip,
    };

    static async Task AcceptLoopAsync(CancellationToken token)
    {
        // Keep our own listener reference: Stop() clears the field while the loop
        // is unwinding, and that must not turn into a NullReferenceException.
        var listener = _listener;
        if (listener == null) return;

        while (!token.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }
            catch (SocketException) { continue; }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                Log.Error(ex);
                continue;
            }

            try
            {
                await HandleClientAsync(client, token);
            }
            catch
            {
                // A client can disappear at any point (including while we are
                // reading or disposing). That is normal for this fire-and-forget
                // protocol and must never be logged or stop the accept loop.
            }
            finally
            {
                try { client.Dispose(); } catch { }
            }
        }
    }

    static async Task HandleClientAsync(Socket client, CancellationToken token)
    {
        using var stream = new NetworkStream(client, ownsSocket: false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync(token);
        if (string.IsNullOrWhiteSpace(line)) return;
        // Forwarding is fire-and-forget: the client closes as soon as the command
        // is written, so don't send a reply (a reply would just cause a broken
        // pipe when the client has already gone away).
        Dispatch(ParseCommand(line));
    }

    static void Dispatch(LaunchAction action)
    {
        Action<LaunchAction> handler;
        lock (HandlerLock)
        {
            handler = _handler;
            if (handler == null)
            {
                Pending.Enqueue(action);
                return;
            }
        }
        try { handler(action); } catch (Exception ex) { Log.Error(ex); }
    }

    public static void SetHandler(Action<LaunchAction> handler)
    {
        lock (HandlerLock)
        {
            _handler = handler;
            while (Pending.TryDequeue(out var action)) handler(action);
        }
    }

    public static void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Dispose();
            _listener = null;
            if (!string.IsNullOrEmpty(_socketPath))
            {
                try { File.Delete(_socketPath); } catch { }
            }
        }
        catch { }
    }
}
