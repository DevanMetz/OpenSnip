using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSnip;

static class PlatformInfo
{
    public static bool HasDisplay => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));
    public static bool HasWaylandDisplay => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public static bool IsWayland
    {
        get
        {
            if (HasWaylandDisplay) return true;
            var type = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            return string.Equals(type, "wayland", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string SessionType
    {
        get
        {
            if (IsWayland) return "Wayland";
            if (HasDisplay) return "X11";
            return "Unknown";
        }
    }

    public static string SettingsDir => ConfigPaths.Dir;

    public static string Quote(string value) =>
        string.IsNullOrEmpty(value) ? "\"\"" : "\"" + value.Replace("\"", "\\\"") + "\"";

    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && !string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
                return path;
            return Path.Combine(AppContext.BaseDirectory, "OpenSnip");
        }
    }

    public static string Command(string argument) =>
        InstalledOnPath() ? $"opensnip {argument}" : $"{Quote(ExecutablePath)} {argument}";

    static bool InstalledOnPath()
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home)) return false;

            string expected = Path.Combine(home, ".local", "bin", "opensnip");
            if (!SamePath(ExecutablePath, expected)) return false;
            return DirectoryOnPath(Path.GetDirectoryName(expected));
        }
        catch { return false; }
    }

    static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.Ordinal);
        }
        catch { return false; }
    }

    static bool DirectoryOnPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (SamePath(entry, directory)) return true;
        }
        return false;
    }

    public static bool CommandExists(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, name))) return true;
            }
            catch { }
        }
        return false;
    }

    public static bool LibX11Available
    {
        get
        {
            try { return NativeLibrary.TryLoad("libX11.so.6", out _) || NativeLibrary.TryLoad("libX11", out _); }
            catch { return false; }
        }
    }

    public static void Open(string target)
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(target);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    public static async Task<(int Code, string Output)> RunAsync(string file, IEnumerable<string> args, int timeoutMs = 12000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{Path.GetFileName(file)} timed out");
        }

        var output = (await outputTask) + (await errorTask);
        return (process.ExitCode, output);
    }

    [DllImport("libc", EntryPoint = "getuid")]
    static extern uint GetUid();

    public static string UserId
    {
        get
        {
            try { return GetUid().ToString(); }
            catch { return Environment.UserName; }
        }
    }
}
