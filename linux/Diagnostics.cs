using System.Runtime.InteropServices;
using System.Text;

namespace OpenSnip;

static class Diagnostics
{
    public static async Task<int> RunAsync(string file)
    {
        var report = new StringBuilder();
        report.AppendLine("OpenSnip " + Cli.Version + " diagnostics");
        report.AppendLine("OS: " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")");
        report.AppendLine("Session type: " + PlatformInfo.SessionType);
        report.AppendLine("DISPLAY: " + (PlatformInfo.HasDisplay ? "present" : "absent"));
        report.AppendLine("WAYLAND_DISPLAY: " + (PlatformInfo.HasWaylandDisplay ? "present" : "absent"));
        report.AppendLine("Session bus: " + (DBus.SessionAvailable ? "available" : "unavailable"));

        var backends = await CaptureService.AvailableBackendNamesAsync();
        report.AppendLine("Capture backends: " + (backends.Count == 0 ? "none" : string.Join(", ", backends)));

        var clipboard = new List<string>();
        if (PlatformInfo.CommandExists("wl-copy")) clipboard.Add("wl-copy");
        if (PlatformInfo.CommandExists("xclip")) clipboard.Add("xclip");
        clipboard.Add("Avalonia");
        report.AppendLine("Clipboard: " + string.Join(", ", clipboard));

        bool portal = await DBus.PortalAvailableAsync();
        bool tray = await DBus.TrayHostAvailableAsync();
        report.AppendLine("xdg-desktop-portal: " + (portal ? "available" : "unavailable"));
        report.AppendLine("Tray host: " + (tray ? "available" : "unavailable"));
        report.AppendLine("Settings: " + ConfigPaths.Dir);

        string text = report.ToString();
        Console.Write(text);

        if (!string.IsNullOrWhiteSpace(file))
        {
            try
            {
                string full = Path.GetFullPath(file);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, text);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not write diagnostics: " + ex.Message);
                return 1;
            }
        }
        return 0;
    }
}
