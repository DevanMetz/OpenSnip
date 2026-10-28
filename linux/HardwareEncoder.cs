using System.Diagnostics;
using System.Globalization;

namespace OpenSnip;

sealed record EncoderChoice(string Name, string Device = null)
{
    public static readonly EncoderChoice Software = new("libx264");
}

/// <summary>Successful test encodes, rather than ffmpeg's compiled-in encoder list, determine support.</summary>
sealed class HardwareEncoder
{
    public static readonly HardwareEncoder Default = new(TestEncode, RenderNodes);
    readonly Lazy<EncoderChoice> _selected;
    readonly Func<EncoderChoice, EncodeSpec, bool> _probe;
    readonly object _gate = new();
    bool _disabled;
    readonly HashSet<(int Width, int Height, int Framerate, bool HighQuality)> _validated = new();

    public HardwareEncoder(Func<EncoderChoice, EncodeSpec, bool> probe, Func<IEnumerable<string>> renderNodes)
    {
        _probe = probe;
        _selected = new Lazy<EncoderChoice>(() =>
        {
            var tiny = new EncodeSpec { Width = 128, Height = 128, Framerate = 30, HighQuality = true };
            var nvenc = new EncoderChoice("h264_nvenc");
            if (Works(nvenc, tiny)) return nvenc;
            try
            {
                foreach (string node in renderNodes())
                {
                    var vaapi = new EncoderChoice("h264_vaapi", node);
                    if (Works(vaapi, tiny)) return vaapi;
                }
            }
            catch { /* Missing or inaccessible render nodes mean software encoding. */ }
            return EncoderChoice.Software;
        });
    }

    public EncoderChoice Selected
    {
        get { lock (_gate) return _disabled ? EncoderChoice.Software : _selected.Value; }
    }

    public EncoderChoice ForRecording(EncodeSpec spec)
    {
        lock (_gate)
        {
            var encoder = Selected;
            if (encoder == EncoderChoice.Software) return encoder;
            var key = (spec.Width, spec.Height, FfmpegPlan.NormalizeFramerate(spec.Framerate), spec.HighQuality);
            if (_validated.Contains(key)) return encoder;
            if (Works(encoder, spec))
            {
                _validated.Add(key);
                return encoder;
            }
            // A driver/resource/size failure should not delay every subsequent segment.
            _disabled = true;
            return EncoderChoice.Software;
        }
    }

    bool Works(EncoderChoice encoder, EncodeSpec spec)
    {
        try { return _probe(encoder, spec); }
        catch { return false; }
    }

    static IEnumerable<string> RenderNodes() => Directory.Exists("/dev/dri")
        ? Directory.GetFiles("/dev/dri", "renderD*").OrderBy(path => path, StringComparer.Ordinal)
        : Array.Empty<string>();

    public static void AddDeviceArguments(List<string> args, EncoderChoice encoder)
    {
        if (encoder.Name == "h264_vaapi") args.AddRange(new[] { "-vaapi_device", encoder.Device });
    }

    public static void AddVideoArguments(List<string> args, EncoderChoice encoder, bool high)
    {
        string quality = FfmpegPlan.Crf(high).ToString(CultureInfo.InvariantCulture);
        args.AddRange(new[] { "-c:v", encoder.Name });
        switch (encoder.Name)
        {
            case "h264_nvenc":
                args.AddRange(new[] { "-preset", "p4", "-rc", "vbr", "-cq", quality, "-b:v", "0", "-pix_fmt", "yuv420p" });
                break;
            case "h264_vaapi":
                args.AddRange(new[] { "-vf", "format=nv12,hwupload", "-rc_mode", "CQP", "-qp", quality });
                break;
            default:
                args.AddRange(new[] { "-preset", "veryfast", "-crf", quality, "-pix_fmt", "yuv420p" });
                break;
        }
    }

    internal static List<string> ProbeArguments(EncoderChoice encoder, EncodeSpec spec)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        AddDeviceArguments(args, encoder);
        // Exercise the same BGRA conversion/upload path as the recording input.
        args.AddRange(new[] { "-f", "lavfi", "-i",
            FormattableString.Invariant($"color=size={spec.Width}x{spec.Height}:rate={FfmpegPlan.NormalizeFramerate(spec.Framerate)},format=bgra"),
            "-frames:v", "1", "-an" });
        AddVideoArguments(args, encoder, spec.HighQuality);
        args.AddRange(new[] { "-f", "null", "-" });
        return args;
    }

    static bool TestEncode(EncoderChoice encoder, EncodeSpec spec)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true, CreateNoWindow = true,
        };
        foreach (string arg in ProbeArguments(encoder, spec)) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        if (process == null) return false;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(1500))
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(); } catch { }
            return false;
        }
        Task.WhenAll(error, output).GetAwaiter().GetResult();
        return process.ExitCode == 0;
    }
}
