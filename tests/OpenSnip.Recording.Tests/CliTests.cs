namespace OpenSnip.Recording.Tests;

public class CliTests
{
    [Fact]
    public void Record_IsItsOwnAction()
    {
        var options = Cli.Parse(new[] { "--record" });
        Assert.Equal(LaunchAction.Record, options.Action);
        Assert.Contains("--record", Cli.Usage);
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--record", "--snip" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--record", "--capture", "a.png" }));
    }

    [Fact]
    public void ApplyLinux_ReplacesAnUnrootedVideoFolder()
    {
        var settings = new Settings
        {
            SaveFolder = "/tmp/snips",
            VideoFolder = "C:\\Users\\x\\Videos\\OpenSnip",
        };
        SettingsDefaults.ApplyLinux(settings);
        Assert.Equal("/tmp/snips", settings.SaveFolder);
        Assert.Equal(SettingsDefaults.DefaultVideoFolder, settings.VideoFolder);
        Assert.True(Path.IsPathRooted(settings.VideoFolder));
    }
}
