using Avalonia;

namespace OpenSnip.Recording.Tests;

public class PlanTests
{
    [Fact]
    public void Normalize_ClipsToTheMonitorWithTheMostOverlap_AndEvensTheSize()
    {
        var monitors = new[]
        {
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(1920, 0, 1280, 1024),
        };
        var selection = new PixelRect(1800, 100, 400, 301);
        Assert.True(RecordGeometry.TryNormalize(selection, monitors, out var area));
        Assert.Equal(new PixelRect(1920, 100, 280, 300), area);
    }

    [Fact]
    public void Normalize_RejectsATinyArea()
    {
        var monitors = new[] { new PixelRect(0, 0, 800, 600) };
        Assert.False(RecordGeometry.TryNormalize(new PixelRect(0, 0, 15, 40), monitors, out _));
        Assert.False(RecordGeometry.TryNormalize(new PixelRect(10, 10, 0, 40), monitors, out _));
    }

    [Fact]
    public void PlaceBar_PrefersBelow_ThenAbove_ThenInside()
    {
        var area = new PixelRect(100, 100, 400, 300);
        var work = new PixelRect(0, 0, 800, 600);
        var below = RecordGeometry.PlaceBar(area, work, 120, 40, 10);
        Assert.Equal(new PixelRect(240, 410, 120, 40), below);

        var shortWork = new PixelRect(0, 0, 800, 420);
        var above = RecordGeometry.PlaceBar(area, shortWork, 120, 40, 10);
        Assert.Equal(new PixelRect(240, 50, 120, 40), above);

        var full = new PixelRect(0, 0, 400, 300);
        var inside = RecordGeometry.PlaceBar(full, full, 120, 40, 10);
        Assert.True(RecordGeometry.Overlaps(inside, full));
        Assert.Equal(250, inside.Y);
    }

    [Fact]
    public void Outline_SitsOnTheInsideEdge()
    {
        var bars = RecordGeometry.Outline(new PixelRect(10, 20, 100, 80), 3);
        Assert.Equal(4, bars.Length);
        Assert.Equal(new PixelRect(10, 20, 100, 3), bars[0]);
        Assert.Equal(new PixelRect(10, 97, 100, 3), bars[1]);
        Assert.Equal(new PixelRect(10, 20, 3, 80), bars[2]);
        Assert.Equal(new PixelRect(107, 20, 3, 80), bars[3]);
        Assert.Empty(RecordGeometry.Outline(new PixelRect(0, 0, 4, 4), 3));
    }

    [Fact]
    public void Pactl_Verbose_PicksTheSinkMonitor_AndKeepsMicrophones()
    {
        string text = """
            Source #0
            	Name: alsa_output.pci.analog-stereo.monitor
            	Description: Monitor of Built-in Audio
            	Properties:
            		device.vendor.name = "Example"
            Source #1
            	Name: alsa_input.pci.analog-stereo
            	Description: Built-in Microphone
            Source #2
            	Name: bluez_source.headset
            	Description: Headset
            """;
        var audio = AudioDevices.ParsePactl(text, "alsa_output.pci.analog-stereo", "alsa_input.pci.analog-stereo");
        Assert.Equal("alsa_output.pci.analog-stereo.monitor", audio.SystemSource);
        Assert.Equal(2, audio.Microphones.Count);
        Assert.Equal("alsa_input.pci.analog-stereo", audio.DefaultMicrophone);
        Assert.Equal("bluez_source.headset", audio.ResolveMicrophone("bluez_source.headset"));
        Assert.Equal("alsa_input.pci.analog-stereo", audio.ResolveMicrophone("missing"));
        Assert.Equal("alsa_input.pci.analog-stereo", audio.ResolveMicrophone(""));
    }

    [Fact]
    public void Pactl_Short_UsesTheFirstMonitorWhenTheSinkIsUnknown()
    {
        string text = "0\talsp_output.monitor\tmodule\tRUNNING\n1\talsp_input.mic\tmodule\tRUNNING\n";
        var audio = AudioDevices.ParsePactl(text, "other-sink", "nope");
        Assert.Equal("alsp_output.monitor", audio.SystemSource);
        Assert.Equal("alsp_input.mic", audio.DefaultMicrophone);
        Assert.Single(audio.Microphones);
    }

    [Fact]
    public void Encode_VideoOnly_UsesCrfAndNoAudio()
    {
        var args = FfmpegPlan.EncodeArguments(new EncodeSpec
        {
            Width = 640,
            Height = 480,
            Framerate = 30,
            HighQuality = true,
            Output = "/tmp/out.mp4",
        });
        Assert.Contains("libx264", args);
        Assert.Equal("18", args[args.IndexOf("-crf") + 1]);
        Assert.Contains("-an", args);
        Assert.DoesNotContain("pulse", args);
        Assert.Equal("/tmp/out.mp4", args[^1]);
        Assert.Equal("640x480", args[args.IndexOf("-video_size") + 1]);
    }

    [Fact]
    public void Encode_MutesTheMicWithoutDroppingTheStream()
    {
        var on = FfmpegPlan.EncodeArguments(Spec(micOn: true, high: false, fps: 99));
        var off = FfmpegPlan.EncodeArguments(Spec(micOn: false, high: false, fps: 0));
        string onFilter = on[on.IndexOf("-filter_complex") + 1];
        string offFilter = off[off.IndexOf("-filter_complex") + 1];
        Assert.Contains("volume=1", onFilter);
        Assert.Contains("volume=0", offFilter);
        Assert.Contains("amix=inputs=2", onFilter);
        Assert.Equal("28", off[off.IndexOf("-crf") + 1]);
        Assert.Equal("30", off[off.IndexOf("-framerate") + 1]);
        Assert.Equal("60", on[on.IndexOf("-framerate") + 1]);
        Assert.Contains("192k", on);
    }

    [Fact]
    public void Encode_SystemOnly_MapsThatInput()
    {
        var args = FfmpegPlan.EncodeArguments(new EncodeSpec
        {
            Width = 16,
            Height = 16,
            Framerate = 30,
            HighQuality = true,
            SystemSource = "speakers.monitor",
            Output = "a.mp4",
        });
        Assert.Equal(1, args.Count(arg => arg == "pulse"));
        Assert.Contains("speakers.monitor", args);
        Assert.DoesNotContain("-filter_complex", args);
        Assert.Contains("aac", args);
    }

    [Fact]
    public void ConcatList_QuotesSingleQuotes()
    {
        string list = FfmpegPlan.ConcatList(new[] { "/tmp/a'b.mp4", "/tmp/c.mp4" });
        Assert.Equal("file '/tmp/a'\\''b.mp4'\nfile '/tmp/c.mp4'\n", list);
        var args = FfmpegPlan.ConcatArguments("list.txt", "out.mp4");
        Assert.Contains("concat", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void NewRecordingPath_AvoidsACollision()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opensnip-name-" + Guid.NewGuid().ToString("N"));
        string first = FfmpegPlan.NewRecordingPath(dir);
        Assert.EndsWith(".mp4", first);
        Assert.Contains("Recording ", Path.GetFileName(first));
        File.WriteAllBytes(first, new byte[] { 1 });
        string second = FfmpegPlan.NewRecordingPath(dir);
        Assert.NotEqual(first, second);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void FileUri_PercentEncodes()
    {
        Assert.Equal("file:///tmp/a%20b.mp4", FileUri.FromPath("/tmp/a b.mp4"));
        Assert.Contains("file:///", FileUri.FromPath("/tmp/opensnip.mp4"));
    }

    static EncodeSpec Spec(bool micOn, bool high, int fps) => new()
    {
        Width = 32,
        Height = 32,
        Framerate = fps,
        HighQuality = high,
        SystemSource = "sys.monitor",
        MicSource = "mic",
        MicOn = micOn,
        Output = "out.mp4",
    };
}
