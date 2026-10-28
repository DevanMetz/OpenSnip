namespace OpenSnip.Tests;

public class CliTests
{
    [Fact]
    public void Empty_args_do_nothing()
    {
        var options = Cli.Parse(Array.Empty<string>());
        Assert.Equal(LaunchAction.None, options.Action);
        Assert.False(options.ShowHelp);
        Assert.False(options.Diagnostics);
        Assert.Null(options.CaptureFile);
    }

    [Fact]
    public void Capture_requires_a_file_and_cannot_be_combined()
    {
        Assert.Contains("--capture", Assert.Throws<CliException>(() => Cli.Parse(new[] { "--capture" })).Message);
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--capture", "shot.png", "--snip" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--snip", "--repeat" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--diagnostics", "--settings" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--nope" }));
    }

    [Fact]
    public void Single_actions_parse()
    {
        Assert.Equal(LaunchAction.Snip, Cli.Parse(new[] { "--snip" }).Action);
        Assert.Equal(LaunchAction.SnipDelay, Cli.Parse(new[] { "--snip-delay" }).Action);
        Assert.Equal(LaunchAction.Repeat, Cli.Parse(new[] { "--repeat" }).Action);
        Assert.Equal(LaunchAction.Settings, Cli.Parse(new[] { "--settings" }).Action);

        var help = Cli.Parse(new[] { "-h" });
        Assert.True(help.ShowHelp);

        var diag = Cli.Parse(new[] { "--diagnostics", "out.txt" });
        Assert.True(diag.Diagnostics);
        Assert.Equal("out.txt", diag.DiagnosticsFile);
        Assert.Equal(LaunchAction.None, diag.Action);

        Assert.Equal("shot.png", Cli.Parse(new[] { "--capture", "shot.png" }).CaptureFile);
    }
}
