namespace OpenSnip;

static class Program
{
    const string MutexName = "OpenSnip.SingleInstance";
    const string SnipEventName = "OpenSnip.SnipNow";

    [STAThread]
    static void Main(string[] args)
    {
        // Build helper: `OpenSnip --write-icon path.ico` renders the app icon to disk.
        if (args.Length == 2 && args[0] == "--write-icon")
        {
            AppIcon.WriteIco(args[1], new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
            return;
        }

        using var mutex = new Mutex(true, MutexName, out bool firstInstance);
        using var snipEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SnipEventName);

        // Launching OpenSnip again while it's running just starts a snip,
        // so a taskbar/Start pin works as a "snip now" button.
        if (!firstInstance)
        {
            snipEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error(e.ExceptionObject as Exception);

        Application.Run(new TrayApp(snipEvent));
    }
}

static class Log
{
    public static void Error(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"[{DateTime.Now:u}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
