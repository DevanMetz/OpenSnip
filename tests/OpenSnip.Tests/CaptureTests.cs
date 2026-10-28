using System.Diagnostics;

namespace OpenSnip.Tests;

public class CaptureTests
{
    [Fact]
    public void X11_import_loads_the_runtime_library()
    {
        if (!X11Library.Available)
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                Assert.Fail("CI should have libX11.so.6 installed so this regression test can run.");
            return;
        }

        Assert.True(X11CaptureBackend.NativeLibraryLoads());
    }

    [Fact]
    public async Task X11_capture_matches_the_root_window_and_round_trips()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))) return;
        if (!X11Library.Available) return;
        if (!File.Exists("/usr/bin/xwininfo") && !OnPath("xwininfo")) return;

        var (width, height) = RootSize();
        var frame = await new X11CaptureBackend().CaptureAsync();
        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(width * height * 4, frame.Bgra.Length);

        string path = TempFile();
        try
        {
            frame.Save(path, "png");
            var back = CapturedFrame.DecodeFile(path);
            Assert.Equal(frame.Width, back.Width);
            Assert.Equal(frame.Height, back.Height);
            AssertEqualBytes(frame.Bgra, back.Bgra);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Png_round_trip_keeps_odd_dimensions()
    {
        const int width = 13;
        const int height = 7;
        var data = new byte[width * height * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = (byte)(i & 255);
            data[i + 1] = (byte)((i * 3) & 255);
            data[i + 2] = (byte)((i * 7) & 255);
            data[i + 3] = 255;
        }

        var frame = new CapturedFrame(width, height, data);
        string path = TempFile();
        try
        {
            frame.Save(path, "png");
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            var back = CapturedFrame.DecodeFile(path);
            Assert.Equal(width, back.Width);
            Assert.Equal(height, back.Height);
            AssertEqualBytes(data, back.Bgra);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("JPG")]
    [InlineData("jpeg")]
    [InlineData(".jpeg")]
    [InlineData(".JPG")]
    public void Jpeg_formats_write_a_jpeg(string format)
    {
        var frame = new CapturedFrame(2, 2, new byte[]
        {
            255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255,
        });
        string path = TempFile();
        try
        {
            frame.Save(path, format);
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Crop_copies_the_requested_pixels()
    {
        var data = new byte[3 * 2 * 4];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
        var crop = new CapturedFrame(3, 2, data).Crop(1, 1, 2, 1);
        Assert.Equal(2, crop.Width);
        Assert.Equal(1, crop.Height);
        AssertEqualBytes(data[16..24], crop.Bgra);
    }

    static string TempFile() => Path.Combine(Path.GetTempPath(), $"opensnip-test-{Guid.NewGuid():N}.img");

    static void AssertEqualBytes(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] == actual[i]) continue;
            Assert.Fail($"Byte {i} differs: expected {expected[i]}, got {actual[i]}");
        }
    }

    static bool OnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir, name))) return true;
        return false;
    }

    static (int Width, int Height) RootSize()
    {
        using var process = Process.Start(new ProcessStartInfo("xwininfo", "-root")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        string text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return (Field(text, "Width"), Field(text, "Height"));
    }

    static int Field(string text, string label)
    {
        foreach (var line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(label + ":", StringComparison.Ordinal)) continue;
            Assert.True(int.TryParse(trimmed[(label.Length + 1)..].Trim(), out int value));
            return value;
        }
        Assert.Fail($"xwininfo did not report {label}");
        return 0;
    }
}
