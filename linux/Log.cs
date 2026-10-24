namespace OpenSnip;

static class Log
{
    public static void Error(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(ConfigPaths.Dir);
            File.AppendAllText(Path.Combine(ConfigPaths.Dir, "error.log"), $"[{DateTime.Now:u}] {ex}\r\n\r\n");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
