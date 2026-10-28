using System.Security;
using Tmds.DBus.Protocol;

namespace OpenSnip;

/// <summary>A best-effort first-run notification; keeps the bus open for its default action.</summary>
sealed class Notifications : IDisposable
{
    const string Service = "org.freedesktop.Notifications";
    const string Path = "/org/freedesktop/Notifications";
    Connection _connection;
    IDisposable _actions;
    uint _id;
    bool _invoked;

    public static bool TakeFirstRun(Settings settings)
    {
        if (settings.Welcomed) return false;
        settings.Welcomed = true;
        return true;
    }

    public static string BuildMessage(Settings settings, bool wayland, string snipCommand)
    {
        var bindings = HotkeyBindings.From(settings);
        string keys = !bindings.Snip.IsEmpty ? bindings.Snip.Display
            : bindings.UsePrintScreen ? "PrtSc" : null;
        return (wayland ? $"Bind {snipCommand} to snip. "
            : keys != null ? $"Press {keys} to snip. " : "Click the tray icon to snip. ") +
            "Right-click the tray icon to change shortcuts and options.";
    }

    public async Task ShowAsync(string body, Action snip)
    {
        try
        {
            _connection = await DBus.ConnectAsync();
            var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Service, Path, Service, "GetCapabilities", "", MessageFlags.None);
            var capabilities = await _connection.CallMethodAsync(writer.CreateMessage(),
                (Message message, object state) => message.GetBodyReader().ReadArrayOfString(), null)
                .WaitAsync(TimeSpan.FromSeconds(3));
            bool actions = capabilities.Contains("actions");
            if (actions)
            {
                _actions = await _connection.AddMatchAsync(
                    new MatchRule { Type = MessageType.Signal, Sender = Service, Path = Path,
                        Interface = Service, Member = "ActionInvoked" },
                    (Message message, object state) =>
                    {
                        var reader = message.GetBodyReader();
                        return (Id: reader.ReadUInt32(), Key: reader.ReadString());
                    },
                    (Exception error, (uint Id, string Key) value, object readerState, object handlerState) =>
                    {
                        if (error == null && value.Id == _id && value.Key == "default" && !_invoked)
                        {
                            _invoked = true;
                            snip();
                        }
                    }, ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false);
            }
            writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Service, Path, Service, "Notify", "susssasa{sv}i", MessageFlags.None);
            writer.WriteString("OpenSnip");
            writer.WriteUInt32(0);
            writer.WriteString("opensnip");
            writer.WriteString("OpenSnip is running");
            writer.WriteString(capabilities.Contains("body-markup") ? SecurityElement.Escape(body) : body);
            writer.WriteArray(actions ? new[] { "default", "Snip" } : Array.Empty<string>());
            writer.WriteDictionary(new Dictionary<string, VariantValue>());
            writer.WriteInt32(8000);
            _id = await _connection.CallMethodAsync(writer.CreateMessage(),
                (Message message, object state) => message.GetBodyReader().ReadUInt32(), null)
                .WaitAsync(TimeSpan.FromSeconds(3));
            if (!actions) Dispose();
        }
        catch { Dispose(); } // No bus/server, or unsupported notifications: startup proceeds silently.
    }

    public void Dispose()
    {
        try { _actions?.Dispose(); } catch { }
        _actions = null;
        try { _connection?.Dispose(); } catch { }
        _connection = null;
    }
}
