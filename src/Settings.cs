using System.Text.Json;

namespace OpenSnip;

public class Settings
{
    public bool AutoSave { get; set; } = true;
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OpenSnip");
    public bool ShowThumbnail { get; set; } = true;
    public bool OpenEditorAfterSnip { get; set; } = false;
    public int DelaySeconds { get; set; } = 3;
    public string SnipHotkey { get; set; } = "Win+Shift+S";
    public string DelayHotkey { get; set; } = "";
    public string RepeatHotkey { get; set; } = "";
    public bool UsePrintScreen { get; set; } = true;
    public bool Welcomed { get; set; } = false;

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSnip");
    static string FilePath => Path.Combine(Dir, "settings.json");

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
