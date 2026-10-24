namespace OpenSnip;

static class SettingsDefaults
{
    public static string PicturesFolder
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrWhiteSpace(pictures)) return pictures;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home)) home = "/tmp";
            return Path.Combine(home, "Pictures");
        }
    }

    public static string DefaultSaveFolder => Path.Combine(PicturesFolder, "OpenSnip");

    public static void ApplyLinux(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SaveFolder) || !Path.IsPathRooted(settings.SaveFolder))
            settings.SaveFolder = DefaultSaveFolder;
    }
}
