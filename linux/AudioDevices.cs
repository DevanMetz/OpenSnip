using System.Diagnostics;
using System.Text;

namespace OpenSnip;

/// <summary>PulseAudio / PipeWire sources used for system sound and the microphone.</summary>
sealed class AudioDevices
{
    public sealed class Device
    {
        public Device(string name, string description)
        {
            Name = name ?? "";
            Description = string.IsNullOrWhiteSpace(description) ? Name : description;
        }

        public string Name { get; }
        public string Description { get; }
    }

    public string SystemSource { get; }
    public IReadOnlyList<Device> Microphones { get; }
    public string DefaultMicrophone { get; }

    public AudioDevices(string systemSource, IReadOnlyList<Device> microphones, string defaultMicrophone)
    {
        SystemSource = string.IsNullOrWhiteSpace(systemSource) ? null : systemSource;
        Microphones = microphones ?? Array.Empty<Device>();
        DefaultMicrophone = string.IsNullOrWhiteSpace(defaultMicrophone) ? null : defaultMicrophone;
    }

    public static AudioDevices Empty { get; } = new(null, Array.Empty<Device>(), null);

    public string ResolveMicrophone(string preferred)
    {
        if (Microphones.Count == 0) return null;
        if (!string.IsNullOrEmpty(preferred))
        {
            foreach (var mic in Microphones)
                if (mic.Name == preferred) return mic.Name;
        }
        if (!string.IsNullOrEmpty(DefaultMicrophone))
        {
            foreach (var mic in Microphones)
                if (mic.Name == DefaultMicrophone) return mic.Name;
        }
        return Microphones[0].Name;
    }

    public static AudioDevices Discover()
    {
        try
        {
            if (!PlatformInfo.CommandExists("pactl")) return Empty;
            string sources = Run("pactl", "list", "sources");
            string sink = FirstLine(Run("pactl", "get-default-sink"));
            string source = FirstLine(Run("pactl", "get-default-source"));
            return ParsePactl(sources, sink, source);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return Empty;
        }
    }

    public static AudioDevices ParsePactl(string text, string defaultSink, string defaultSource)
    {
        text ??= "";
        var devices = text.Contains("Name:", StringComparison.Ordinal)
            ? ParseVerbose(text)
            : ParseShort(text);
        return FromDevices(devices, defaultSink?.Trim(), defaultSource?.Trim());
    }

    static List<Device> ParseVerbose(string text)
    {
        var devices = new List<Device>();
        string name = null;
        string description = null;
        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(name))
                devices.Add(new Device(name.Trim(), description?.Trim()));
            name = null;
            description = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("Source #", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }
            if (line.StartsWith("Name:", StringComparison.Ordinal)) name = line["Name:".Length..].Trim();
            else if (line.StartsWith("Description:", StringComparison.Ordinal)) description = line["Description:".Length..].Trim();
        }
        Flush();
        return devices;
    }

    static List<Device> ParseShort(string text)
    {
        var devices = new List<Device>();
        foreach (var raw in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split('\t');
            if (parts.Length < 2) continue;
            string name = parts[1].Trim();
            if (name.Length == 0 || name.Contains(' ')) continue;
            devices.Add(new Device(name, name));
        }
        return devices;
    }

    static AudioDevices FromDevices(List<Device> devices, string defaultSink, string defaultSource)
    {
        string monitor = null;
        if (!string.IsNullOrEmpty(defaultSink))
        {
            string candidate = defaultSink.EndsWith(".monitor", StringComparison.Ordinal)
                ? defaultSink
                : defaultSink + ".monitor";
            if (devices.Any(d => d.Name == candidate)) monitor = candidate;
        }
        monitor ??= devices.FirstOrDefault(d => d.Name.EndsWith(".monitor", StringComparison.Ordinal))?.Name;

        var mics = devices.Where(d => !d.Name.EndsWith(".monitor", StringComparison.Ordinal)).ToList();
        string defaultMic = null;
        if (!string.IsNullOrEmpty(defaultSource) && mics.Any(m => m.Name == defaultSource))
            defaultMic = defaultSource;
        else if (mics.Count > 0)
            defaultMic = mics[0].Name;
        return new AudioDevices(monitor, mics, defaultMic);
    }

    static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        int cut = text.IndexOfAny(new[] { '\r', '\n' });
        return (cut < 0 ? text : text[..cut]).Trim();
    }

    static string Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        if (process == null) return "";
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(2000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return "";
        }
        process.WaitForExit();
        return output.ToString();
    }
}
