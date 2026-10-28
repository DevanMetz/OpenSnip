namespace OpenSnip.Tests;

public class PinCommandTests
{
    [Fact]
    public void Pin_flag_reads_the_clipboard_when_no_file_is_given()
    {
        var options = Cli.Parse(new[] { "--pin" });
        Assert.Equal(LaunchAction.Pin, options.Action);
        Assert.Null(options.PinFile);
    }

    [Fact]
    public void Pin_flag_keeps_a_file_path()
    {
        var options = Cli.Parse(new[] { "--pin", "shot.png" });
        Assert.Equal(LaunchAction.Pin, options.Action);
        Assert.Equal("shot.png", options.PinFile);
    }

    [Fact]
    public void Pin_cannot_be_combined_with_another_action()
    {
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--pin", "--snip" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--snip", "--pin", "a.png" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--pin", "a.png", "--settings" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--pin", "--diagnostics" }));
    }

    [Fact]
    public void Existing_actions_still_parse()
    {
        Assert.Equal(LaunchAction.Snip, Cli.Parse(new[] { "--snip" }).Action);
        Assert.Equal(LaunchAction.SnipDelay, Cli.Parse(new[] { "--snip-delay" }).Action);
        Assert.Equal(LaunchAction.Repeat, Cli.Parse(new[] { "--repeat" }).Action);
        Assert.Equal(LaunchAction.Settings, Cli.Parse(new[] { "--settings" }).Action);
        Assert.Null(Cli.Parse(new[] { "--snip" }).PinFile);
    }

    [Fact]
    public void Resolve_pin_path_requires_a_real_file()
    {
        string path = Path.GetTempFileName();
        try
        {
            Assert.Equal(Path.GetFullPath(path), Cli.ResolvePinPath(path));
        }
        finally
        {
            File.Delete(path);
        }

        var missing = Path.Combine(Path.GetTempPath(), "opensnip-missing-" + Guid.NewGuid().ToString("N") + ".png");
        var ex = Assert.Throws<CliException>(() => Cli.ResolvePinPath(missing));
        Assert.Contains("not found", ex.Message);
    }

    [Theory]
    [InlineData("pin", "Pin", null)]
    [InlineData("pin /tmp/a b.png", "Pin", "/tmp/a b.png")]
    [InlineData("pin\t/tmp/shot.png", "Pin", "/tmp/shot.png")]
    [InlineData("snip-delay", "SnipDelay", null)]
    [InlineData("settings", "Settings", null)]
    [InlineData("repeat", "Repeat", null)]
    [InlineData("nope", "Snip", null)]
    public void Remote_commands_round_trip(string line, string action, string argument)
    {
        var command = RemoteCommand.Parse(line);
        Assert.Equal(action, command.Action.ToString());
        Assert.Equal(argument, command.Argument);
    }

    [Fact]
    public void Pin_format_rejects_a_path_that_would_break_the_line()
    {
        Assert.Equal("pin", RemoteCommand.Format(LaunchAction.Pin, null));
        Assert.Equal("pin /tmp/shot.png", RemoteCommand.Format(LaunchAction.Pin, "/tmp/shot.png"));
        Assert.Equal("snip", RemoteCommand.Format(LaunchAction.Snip, null));
        Assert.Throws<ArgumentException>(() => RemoteCommand.Format(LaunchAction.Pin, "/tmp/a\nb.png"));
    }
}
