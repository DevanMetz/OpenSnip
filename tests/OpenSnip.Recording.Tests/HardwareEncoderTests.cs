namespace OpenSnip.Recording.Tests;

public class HardwareEncoderTests
{
    [Fact]
    public void Selection_PrefersNvenc_AndCachesProbe()
    {
        int calls = 0;
        var selector = new HardwareEncoder((encoder, _) => { calls++; return true; },
            () => throw new InvalidOperationException());
        Assert.Equal("h264_nvenc", selector.Selected.Name);
        Assert.Equal(selector.Selected, selector.Selected);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Selection_TriesEachRenderNode_AndCachesSoftware()
    {
        var attempted = new List<EncoderChoice>();
        var selector = new HardwareEncoder((encoder, _) =>
        {
            attempted.Add(encoder);
            return encoder.Device == "/dev/dri/renderD129";
        }, () => new[] { "/dev/dri/renderD128", "/dev/dri/renderD129" });
        Assert.Equal(new EncoderChoice("h264_vaapi", "/dev/dri/renderD129"), selector.Selected);
        Assert.Equal(3, attempted.Count);
        var failed = new HardwareEncoder((_, _) => throw new IOException(), () => Array.Empty<string>());
        Assert.Equal(EncoderChoice.Software, failed.Selected);
        Assert.Equal(EncoderChoice.Software, failed.Selected);
    }

    [Fact]
    public void RecordingValidation_FallsBackAndDisablesHardwareAfterFailure()
    {
        int calls = 0;
        var selector = new HardwareEncoder((_, spec) => { calls++; return spec.Width == 128; },
            () => Array.Empty<string>());
        var spec = new EncodeSpec { Width = 1920, Height = 1080 };
        Assert.Equal(EncoderChoice.Software, selector.ForRecording(spec));
        Assert.Equal(EncoderChoice.Software, selector.ForRecording(spec));
        Assert.Equal(EncoderChoice.Software, selector.Selected);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void RecordingValidation_CachesEachSizeFramerateAndQuality()
    {
        int calls = 0;
        var selector = new HardwareEncoder((_, _) => { calls++; return true; },
            () => Array.Empty<string>());
        var spec = new EncodeSpec { Width = 1920, Height = 1080, Framerate = 30 };
        Assert.Equal("h264_nvenc", selector.ForRecording(spec).Name);
        selector.ForRecording(spec);
        Assert.Equal(2, calls); // Discovery and the first recording configuration.
        spec.HighQuality = true;
        selector.ForRecording(spec);
        spec.Width = 1280;
        selector.ForRecording(spec);
        spec.Framerate = 60;
        selector.ForRecording(spec);
        Assert.Equal(5, calls);
    }

    [Fact]
    public void Selection_FallsBackWhenRenderNodesAreInaccessible()
    {
        var selector = new HardwareEncoder((_, _) => false,
            () => throw new UnauthorizedAccessException());
        Assert.Equal(EncoderChoice.Software, selector.Selected);
    }

    [Theory]
    [InlineData(true, "18")]
    [InlineData(false, "28")]
    public void Arguments_MapQualityAndUploadOnlyForVaapi(bool high, string quality)
    {
        var spec = new EncodeSpec { Width = 1920, Height = 1080, Framerate = 60,
            HighQuality = high, SystemSource = "monitor", MicSource = "mic", Output = "clip.mp4" };
        var nvenc = FfmpegPlan.EncodeArguments(spec, new EncoderChoice("h264_nvenc"));
        Assert.Equal(quality, nvenc[nvenc.IndexOf("-cq") + 1]);
        Assert.Contains("yuv420p", nvenc);
        Assert.DoesNotContain("-crf", nvenc);
        Assert.DoesNotContain("hwupload", nvenc);
        var encoder = new EncoderChoice("h264_vaapi", "/dev/dri/renderD129");
        var vaapi = FfmpegPlan.EncodeArguments(spec, encoder);
        Assert.Equal(quality, vaapi[vaapi.IndexOf("-qp") + 1]);
        Assert.Equal("/dev/dri/renderD129", vaapi[vaapi.IndexOf("-vaapi_device") + 1]);
        Assert.True(vaapi.IndexOf("-vaapi_device") < vaapi.IndexOf("-i"));
        Assert.Contains("format=nv12,hwupload", vaapi);
        Assert.Contains("-filter_complex", vaapi);
        Assert.DoesNotContain("-pix_fmt", vaapi);
        Assert.Equal("clip.mp4", vaapi[^1]);
        var probe = HardwareEncoder.ProbeArguments(encoder, spec);
        Assert.Contains("color=size=1920x1080:rate=60,format=bgra", probe);
        Assert.Equal(quality, probe[probe.IndexOf("-qp") + 1]);
        Assert.Contains("format=nv12,hwupload", probe);
    }
}
