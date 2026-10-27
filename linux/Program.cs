using Avalonia;

namespace OpenSnip;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ConfigPaths.EnsureConfigHome();
        X11Native.InitThreads();

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

        if (!SingleInstance.TryStart())
        {
            var forwarded = options.Action == LaunchAction.None ? LaunchAction.Snip : options.Action;
            if (SingleInstance.Send(forwarded, out string error)) return 0;
            Console.Error.WriteLine("OpenSnip is already running but did not accept the command: " + error);
            return 1;
        }

        App.StartupAction = options.Action;
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
