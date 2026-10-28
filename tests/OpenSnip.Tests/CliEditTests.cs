namespace OpenSnip.Tests;

public class CliEditTests
{
    [Fact]
    public void Edit_flag_sets_the_action_and_path()
    {
        var options = Cli.Parse(new[] { "--edit", "shot.png" });
        Assert.Equal(LaunchAction.Edit, options.Action);
        Assert.Equal("shot.png", options.EditFile);
        Assert.Null(options.CaptureFile);
    }

    [Fact]
    public void Edit_requires_a_file_and_cannot_be_combined()
    {
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--edit" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--edit", "--snip" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--edit", "a.png", "--snip" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--edit", "a.png", "--help" }));
    }

    [Fact]
    public void Resolve_edit_path_requires_a_real_file()
    {
        string path = Path.GetTempFileName();
        try
        {
            string full = Cli.ResolveEditPath(path);
            Assert.Equal(Path.GetFullPath(path), full);
        }
        finally
        {
            File.Delete(path);
        }

        var missing = Path.Combine(Path.GetTempPath(), "opensnip-missing-" + Guid.NewGuid().ToString("N") + ".png");
        var ex = Assert.Throws<CliException>(() => Cli.ResolveEditPath(missing));
        Assert.Contains("not found", ex.Message);
    }

    [Theory]
    [InlineData("Edit", "/tmp/my shot.png")]
    [InlineData("Pin", "/tmp/my shot.png")]
    [InlineData("Pin", null)]
    public void Image_commands_format_and_parse(string actionName, string path)
    {
        var action = Enum.Parse<LaunchAction>(actionName);
        var command = RemoteCommand.Parse(RemoteCommand.Format(action, path));
        Assert.Equal(action, command.Action);
        Assert.Equal(path, command.Argument);
    }

    [Fact]
    public void Pin_and_edit_cannot_be_combined()
    {
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--pin", "--edit", "shot.png" }));
        Assert.Throws<CliException>(() => Cli.Parse(new[] { "--edit", "shot.png", "--pin" }));
    }

    [Theory]
    [InlineData("edit /tmp/my shot.png", "Edit", "/tmp/my shot.png")]
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
}
