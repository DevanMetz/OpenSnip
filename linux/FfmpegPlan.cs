using System.Globalization;
using System.Text;

namespace OpenSnip;

sealed class EncodeSpec
{
    public int Width;
    public int Height;
    public int Framerate;
    public bool HighQuality;
    public string SystemSource;
    public string MicSource;
    public bool MicOn;
    public bool Cursor;
    public string Output;
}

/// <summary>ffmpeg command lines for one segment and for joining a paused recording back together.</summary>
static class FfmpegPlan
{
    public static bool Available => PlatformInfo.CommandExists("ffmpeg");

    public static int NormalizeFramerate(int value)
    {
        if (value < 1) return 30;
        if (value > 60) return 60;
        return value;
    }

    /// <summary>Windows quality 85 / 65, expressed as x264 CRF. Lower is sharper.</summary>
    public static int Crf(bool highQuality) => highQuality ? 18 : 28;

    public static List<string> EncodeArguments(EncodeSpec spec)
    {
        int framerate = NormalizeFramerate(spec.Framerate);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo",
            "-pixel_format", "bgra",
            "-video_size", spec.Width.ToString(CultureInfo.InvariantCulture) + "x" + spec.Height.ToString(CultureInfo.InvariantCulture),
            "-framerate", framerate.ToString(CultureInfo.InvariantCulture),
            // Arrival time, not frame index, so the picture stays in step with PulseAudio.
            "-use_wallclock_as_timestamps", "1",
            "-i", "pipe:0",
        };

        bool system = !string.IsNullOrEmpty(spec.SystemSource);
        bool mic = !string.IsNullOrEmpty(spec.MicSource);
        if (system) AddPulse(args, spec.SystemSource);
        if (mic) AddPulse(args, spec.MicSource);

        if (system && mic)
        {
            string volume = spec.MicOn ? "1" : "0";
            args.Add("-filter_complex");
            args.Add("[1:a]aformat=sample_fmts=fltp:channel_layouts=stereo[sys];" +
                     "[2:a]aformat=sample_fmts=fltp:channel_layouts=stereo,volume=" + volume + "[mic];" +
                     "[sys][mic]amix=inputs=2:duration=longest:dropout_transition=0[aout]");
            args.Add("-map");
            args.Add("0:v:0");
            args.Add("-map");
            args.Add("[aout]");
        }
        else if (mic && !spec.MicOn)
        {
            // Every segment keeps the mic stream so a later concat does not see a different layout.
            args.Add("-filter_complex");
            args.Add("[1:a]volume=0[aout]");
            args.Add("-map");
            args.Add("0:v:0");
            args.Add("-map");
            args.Add("[aout]");
        }

        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add("veryfast");
        args.Add("-crf");
        args.Add(Crf(spec.HighQuality).ToString(CultureInfo.InvariantCulture));
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-movflags");
        args.Add("+faststart");

        if (system || mic)
        {
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add("192k");
        }
        else args.Add("-an");

        args.Add(spec.Output);
        return args;
    }

    static void AddPulse(List<string> args, string source)
    {
        args.Add("-thread_queue_size");
        args.Add("1024");
        args.Add("-f");
        args.Add("pulse");
        args.Add("-i");
        args.Add(source);
    }

    public static List<string> ConcatArguments(string listFile, string output) => new()
    {
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "concat", "-safe", "0", "-i", listFile,
        "-c", "copy", "-movflags", "+faststart",
        output,
    };

    public static string ConcatList(IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            builder.Append("file '");
            builder.Append((file ?? "").Replace("'", "'\\''", StringComparison.Ordinal));
            builder.Append("'\n");
        }
        return builder.ToString();
    }

    public static string NewRecordingPath(string folder)
    {
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, "Recording " + stamp + ".mp4");
        int n = 2;
        while (File.Exists(path))
            path = Path.Combine(folder, "Recording " + stamp + " (" + n++ + ").mp4");
        return path;
    }
}
