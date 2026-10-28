using Avalonia;

namespace OpenSnip;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ConfigPaths.EnsureConfigHome();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error(e.ExceptionObject as Exception ?? new Exception("Unhandled exception"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception);
            e.SetObserved();
        };

        CliOptions options;
        try { options = Cli.Parse(args); }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run OpenSnip --help for usage.");
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Cli.Usage);
            return 0;
        }
        if (options.Diagnostics)
            return Diagnostics.RunAsync(options.DiagnosticsFile).GetAwaiter().GetResult();
        if (options.CaptureFile != null)
            return CaptureCli.RunAsync(options.CaptureFile).GetAwaiter().GetResult();

        string pinFile = null;
        if (options.Action == LaunchAction.Pin && options.PinFile != null)
        {
            try { pinFile = Cli.ResolvePinPath(options.PinFile); }
            catch (CliException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        string editFile = null;
        if (options.EditFile != null)
        {
            try { editFile = Cli.ResolveEditPath(options.EditFile); }
            catch (CliException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        if (!SingleInstance.TryStart())
        {
            var forwarded = options.Action == LaunchAction.None ? LaunchAction.Snip : options.Action;
            if (SingleInstance.Send(forwarded, forwarded == LaunchAction.Edit ? editFile : pinFile, out string error)) return 0;
            Console.Error.WriteLine("OpenSnip is already running but did not accept the command: " + error);
            return 1;
        }

        App.StartupAction = options.Action;
        App.StartupArgument = options.Action == LaunchAction.Edit ? editFile : pinFile;
        try
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Console.Error.WriteLine("OpenSnip could not start: " + ex.Message);
            return 1;
        }
        finally
        {
            SingleInstance.Stop();
        }
    }
}
