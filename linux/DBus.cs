using System.Collections.Concurrent;
using Tmds.DBus.Protocol;

namespace OpenSnip;

sealed record PortalResponse(string Path, uint Code, Dictionary<string, VariantValue> Results);

static class DBus
{
    static string SessionAddress
    {
        get
        {
            try { return Address.Session; }
            catch { return null; }
        }
    }

    public static bool SessionAvailable => !string.IsNullOrWhiteSpace(SessionAddress);

    internal static async Task<Connection> ConnectAsync()
    {
        if (!SessionAvailable) throw new InvalidOperationException("No session bus");
        var connection = new Connection(SessionAddress);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            return connection;
        }
        catch
        {
            try { connection.Dispose(); } catch { }
            throw;
        }
    }

    public static async Task<bool> NameHasOwnerAsync(string name)
    {
        Connection connection = null;
        try
        {
            connection = await ConnectAsync();
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus",
                "org.freedesktop.DBus", "NameHasOwner", "s", MessageFlags.None);
            writer.WriteString(name);
            return await connection.CallMethodAsync(writer.CreateMessage(),
                (Message message, object state) => message.GetBodyReader().ReadBool(), null)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            if (ex is not InvalidOperationException) Log.Error(ex);
            return false;
        }
        finally
        {
            try { connection?.Dispose(); } catch { }
        }
    }

    public static Task<bool> TrayHostAvailableAsync() => NameHasOwnerAsync("org.kde.StatusNotifierWatcher");
    public static Task<bool> PortalAvailableAsync() => NameHasOwnerAsync("org.freedesktop.portal.Desktop");

    /// <summary>Captures the desktop through org.freedesktop.portal.Screenshot.</summary>
    public static async Task<string> PortalScreenshotAsync()
    {
        Connection connection = null;
        try
        {
            connection = await ConnectAsync();
            var responses = new ConcurrentDictionary<string, PortalResponse>();
            using var signal = new SemaphoreSlim(0);
            var errors = new ConcurrentQueue<Exception>();

            using var subscription = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.portal.Request",
                    Member = "Response",
                },
                (Message message, object state) =>
                {
                    var reader = message.GetBodyReader();
                    uint code = reader.ReadUInt32();
                    var results = reader.ReadDictionaryOfStringToVariantValue();
                    return new PortalResponse(message.PathAsString, code, results);
                },
                (Exception error, PortalResponse value, object readerState, object handlerState) =>
                {
                    if (error != null)
                    {
                        errors.Enqueue(error);
                        signal.Release();
                        return;
                    }
                    if (value != null)
                    {
                        responses[value.Path] = value;
                        signal.Release();
                    }
                },
                ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false);

            string token = $"opensnip_{Environment.ProcessId}_{DateTime.UtcNow.Ticks}";
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                "org.freedesktop.portal.Desktop",
                "/org/freedesktop/portal/desktop",
                "org.freedesktop.portal.Screenshot",
                "Screenshot",
                "sa{sv}",
                MessageFlags.None);
            writer.WriteString("");
            writer.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = VariantValue.String(token),
                ["interactive"] = VariantValue.Bool(false),
            });

            string requestPath = await connection.CallMethodAsync(
                writer.CreateMessage(),
                (Message message, object state) => message.GetBodyReader().ReadObjectPathAsString(),
                null).WaitAsync(TimeSpan.FromSeconds(10));

            var deadline = DateTime.UtcNow.AddSeconds(20);
            PortalResponse response = null;
            while (DateTime.UtcNow < deadline)
            {
                if (responses.TryGetValue(requestPath, out response)) break;
                int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                await signal.WaitAsync(remaining);
                if (errors.TryDequeue(out var error)) throw error;
            }
            if (response == null || response.Code != 0)
                throw new InvalidOperationException(response == null ? "Portal screenshot timed out" : $"Portal screenshot failed ({response.Code})");

            if (!response.Results.TryGetValue("uri", out var uriValue))
                throw new InvalidOperationException("Portal screenshot returned no URI");
            string uri = uriValue.GetString();
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
                throw new InvalidOperationException("Portal screenshot returned an invalid URI");
            return parsed.LocalPath;
        }
        finally
        {
            try { connection?.Dispose(); } catch { }
        }
    }
}
