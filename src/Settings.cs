using System.Text.Json;

namespace OpenSnip;

public class Settings
{
    // Screenshots
    public bool CopyToClipboard { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OpenSnip");
    public string SaveFormat { get; set; } = "png"; // png | jpg
    public bool MonthlyFolders { get; set; } = false;
    public bool ShowThumbnail { get; set; } = true;
    public bool OpenEditorAfterSnip { get; set; } = false;
    public int PreviewSeconds { get; set; } = 6;
    public bool ShowMagnifier { get; set; } = true;
    public int DelaySeconds { get; set; } = 3;

    // Shortcuts
    public string SnipHotkey { get; set; } = "Win+Shift+S";
    public string DelayHotkey { get; set; } = "";
    public string RepeatHotkey { get; set; } = "";
    public string RecordHotkey { get; set; } = "Win+Shift+R";
    public bool UsePrintScreen { get; set; } = true;

    // Screen recording
    public string VideoFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "OpenSnip");
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; } = false;
    public string MicrophoneDevice { get; set; } = ""; // "" = Windows default
    public int RecordFramerate { get; set; } = 30;
    public bool RecordHighQuality { get; set; } = true;
    public bool RecordCursor { get; set; } = true;
    public bool RecordCountdown { get; set; } = true;
    public bool CopyRecordingToClipboard { get; set; } = true;
    public bool Welcomed { get; set; } = false;

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSnip");
    static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Folder for a new file, honoring the "monthly subfolders" option.</summary>
    public string FolderFor(string root) =>
        MonthlyFolders ? Path.Combine(root, DateTime.Now.ToString("yyyy-MM")) : root;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) { Log.Error(ex); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}
