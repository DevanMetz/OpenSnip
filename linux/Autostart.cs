using System.Text;

namespace OpenSnip;

static class Autostart
{
    static string AutostartDir => ConfigPaths.AutostartDir;

    static string FilePath => Path.Combine(AutostartDir, "opensnip.desktop");

    public static bool Enabled
    {
        get
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                foreach (var line in File.ReadAllLines(FilePath))
                    if (line.Trim().Equals("Hidden=true", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
            catch { return false; }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }

            Directory.CreateDirectory(AutostartDir);
            var text = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Type=Application")
                .AppendLine("Name=OpenSnip")
                .AppendLine("Exec=" + PlatformInfo.Quote(PlatformInfo.ExecutablePath))
                .AppendLine("Icon=opensnip")
                .AppendLine("Terminal=false")
                .AppendLine("Categories=Utility;Graphics;")
                .AppendLine("X-GNOME-Autostart-enabled=true")
                .ToString();
            File.WriteAllText(FilePath, text);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            throw;
        }
    }
}
