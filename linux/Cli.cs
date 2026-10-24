namespace OpenSnip;

enum LaunchAction
{
    None,
    Snip,
    SnipDelay,
    Repeat,
    Settings,
}

sealed class CliOptions
{
    public LaunchAction Action { get; init; }
    public bool ShowHelp { get; init; }
    public bool Diagnostics { get; init; }
    public string DiagnosticsFile { get; init; }
    public string CaptureFile { get; init; }
}

static class Cli
{
    public const string Version = "1.2.0";

    public const string Usage =
        "OpenSnip " + Version + "\n" +
        "\n" +
        "Usage:\n" +
        "  OpenSnip [--snip] [--snip-delay] [--repeat] [--settings]\n" +
        "  OpenSnip --capture <file.png>\n" +
        "  OpenSnip --diagnostics [file]\n" +
        "  OpenSnip --help\n" +
        "\n" +
        "Options:\n" +
        "  --snip              Capture a region now; starts OpenSnip if needed.\n" +
        "  --snip-delay        Capture after the configured delay.\n" +
        "  --repeat            Capture the last region again.\n" +
        "  --settings          Open settings; starts OpenSnip if needed.\n" +
        "  --capture <file>    Capture the full desktop without UI.\n" +
        "  --diagnostics [file]  Print a diagnostic report, optionally write it.\n" +
        "  --help              Show this help.\n";

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0) return new CliOptions { Action = LaunchAction.None };

        LaunchAction action = LaunchAction.None;
        bool help = false, diagnostics = false;
        string diagnosticsFile = null, captureFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--snip":
                    action = MergeAction(action, LaunchAction.Snip);
                    break;
                case "--snip-delay":
                    action = MergeAction(action, LaunchAction.SnipDelay);
                    break;
                case "--repeat":
                    action = MergeAction(action, LaunchAction.Repeat);
                    break;
                case "--settings":
                    action = MergeAction(action, LaunchAction.Settings);
                    break;
                case "--diagnostics":
                    diagnostics = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        diagnosticsFile = args[++i];
                    break;
                case "--capture":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        throw new CliException("--capture needs an output file.");
                    captureFile = args[++i];
                    break;
                default:
                    throw new CliException($"Unknown option: {arg}");
            }
        }

        if (captureFile != null && (diagnostics || action != LaunchAction.None || help))
            throw new CliException("--capture cannot be combined with other commands.");
        if (diagnostics && action != LaunchAction.None)
            throw new CliException("--diagnostics cannot be combined with other commands.");

        return new CliOptions
        {
            Action = action,
            ShowHelp = help,
            Diagnostics = diagnostics,
            DiagnosticsFile = diagnosticsFile,
            CaptureFile = captureFile,
        };
    }

    static LaunchAction MergeAction(LaunchAction current, LaunchAction next)
    {
        if (current != LaunchAction.None && current != next)
            throw new CliException("Only one action can be used at a time.");
        return next;
    }
}

sealed class CliException : Exception
{
    public CliException(string message) : base(message) { }
}
