using System.Text.Json;

namespace OpenSnip.Hotkeys.Tests;

public class NotificationTests
{
    const string Options = "Right-click the tray icon to change shortcuts and options.";

    [Fact]
    public void FirstRunIsConsumedOnceAndSurvivesSettingsRoundTrip()
    {
        var settings = JsonSerializer.Deserialize<Settings>("{}");
        Assert.True(Notifications.TakeFirstRun(settings));
        Assert.True(settings.Welcomed);
        Assert.False(Notifications.TakeFirstRun(settings));
        var loaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings));
        Assert.False(Notifications.TakeFirstRun(loaded));
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", true, "Press Ctrl + Shift + S to snip. ")]
    [InlineData("", true, "Press PrtSc to snip. ")]
    [InlineData("", false, "Click the tray icon to snip. ")]
    [InlineData("invalid", false, "Click the tray icon to snip. ")]
    public void X11UsesConfiguredShortcutOrWindowsFallback(string shortcut, bool printScreen, string prefix)
    {
        var settings = new Settings { SnipHotkey = shortcut, UsePrintScreen = printScreen };
        Assert.Equal(prefix + Options, Notifications.BuildMessage(settings, false, "opensnip --snip"));
    }

    [Theory]
    [InlineData("opensnip --snip")]
    [InlineData("\"/home/user/My Apps/OpenSnip\" --snip")]
    public void WaylandUsesTheCommandToBind(string command)
    {
        Assert.Equal($"Bind {command} to snip. " + Options,
            Notifications.BuildMessage(new Settings(), true, command));
    }
}
