namespace OpenSnip;

/// <summary>
/// Linux-side configuration locations. When HOME is unset .NET returns an empty
/// ApplicationData path, which would make Settings.Dir a relative "OpenSnip"
/// directory in the current working directory. Resolve a safe absolute home and
/// teach the shared settings code about it through XDG_CONFIG_HOME.
/// </summary>
static class ConfigPaths
{
    const string FallbackHome = "/tmp";

    public static string Home
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (IsAbsolute(appData)) return appData;

            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (IsAbsolute(xdg)) return xdg;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!IsAbsolute(home)) home = Environment.GetEnvironmentVariable("HOME");
            if (IsAbsolute(home)) return Path.Combine(home, ".config");

            return FallbackHome;
        }
    }

    public static string Dir => Path.Combine(Home, "OpenSnip");

    public static string AutostartDir => Path.Combine(Home, "autostart");

    /// <summary>Set XDG_CONFIG_HOME when ApplicationData cannot provide an absolute path.</summary>
    public static void EnsureConfigHome()
    {
        try
        {
            if (IsAbsolute(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))) return;
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Home);
        }
        catch
        {
            // Fallbacks are also applied directly by ConfigPaths.Dir.
        }
    }

    static bool IsAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.IsPathRooted(path); }
        catch { return false; }
    }
}
