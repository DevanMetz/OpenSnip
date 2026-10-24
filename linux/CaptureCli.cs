namespace OpenSnip;

static class CaptureCli
{
    public static async Task<int> RunAsync(string path)
    {
        try
        {
            var frame = await CaptureService.CaptureAsync();
            string full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            frame.Save(full, Path.GetExtension(full).TrimStart('.'));
            Console.WriteLine(full);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
