using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OpenSnip;

static class ClipboardService
{
    static readonly string[] PreferredImageTypes =
    {
        "image/png", "image/jpeg", "image/jpg", "image/bmp", "image/webp", "image/gif", "image/tiff",
    };

    static Window _hidden;

    public static string ChooseImageType(IEnumerable<string> types)
    {
        if (types == null) return null;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type)) continue;
            found.Add(type.Trim());
        }
        foreach (var preferred in PreferredImageTypes)
            if (found.Contains(preferred)) return preferred;
        return null;
    }

    public static async Task<bool> CopyPngAsync(byte[] png, TopLevel topLevel = null)
    {
        if (png == null || png.Length == 0) return false;
        if (await TryExternalAsync(png)) return true;
        try
        {
            var clipboard = ClipboardOf(topLevel);
            if (clipboard == null) return false;
            var item = new DataTransferItem();
            item.Set(DataFormat.CreateBytesPlatformFormat("image/png"), png);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
    }

    public static async Task<CapturedFrame> ReadImageAsync(TopLevel topLevel = null)
    {
        try
        {
            var bytes = await ReadExternalImageAsync();
            if (bytes != null)
            {
                try { return CapturedFrame.DecodeBytes(bytes); }
                catch (Exception ex) { Log.Error(ex); }
            }
        }
        catch (Exception ex) { Log.Error(ex); }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                return await ReadAvaloniaAsync(topLevel);

            var done = new TaskCompletionSource<CapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try { done.TrySetResult(await ReadAvaloniaAsync(topLevel)); }
                catch (Exception ex) { done.TrySetException(ex); }
            });
            return await done.Task;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    static async Task<CapturedFrame> ReadAvaloniaAsync(TopLevel topLevel)
    {
        var clipboard = ClipboardOf(topLevel);
        if (clipboard == null) return null;

        IAsyncDataTransfer transfer = null;
        try { transfer = await clipboard.TryGetDataAsync(); }
        catch (Exception ex) { Log.Error(ex); }

        if (transfer != null)
        {
            foreach (var mime in PreferredImageTypes)
            {
                foreach (var item in transfer.Items)
                {
                    foreach (var format in item.Formats)
                    {
                        if (!FormatMatches(format, mime)) continue;
                        object raw = null;
                        try { raw = await item.TryGetRawAsync(format); }
                        catch { continue; }
                        var bytes = AsBytes(raw);
                        if (bytes == null) continue;
                        try { return CapturedFrame.DecodeBytes(bytes); }
                        catch (Exception ex) { Log.Error(ex); }
                    }
                }
            }
        }

        var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap != null)
        {
            using (bitmap)
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms);
                return CapturedFrame.DecodeBytes(ms.ToArray());
            }
        }

        var file = await clipboard.TryGetFileAsync();
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return CapturedFrame.DecodeFile(path);
        return null;
    }

    static bool FormatMatches(DataFormat format, string mime)
    {
        if (format == null || string.IsNullOrEmpty(format.Identifier)) return false;
        return string.Equals(format.Identifier, mime, StringComparison.OrdinalIgnoreCase)
            || format.Identifier.EndsWith("/" + mime, StringComparison.OrdinalIgnoreCase);
    }

    static byte[] AsBytes(object data)
    {
        switch (data)
        {
            case byte[] bytes when bytes.Length > 0:
                return bytes;
            case MemoryStream stream when stream.Length > 0:
                return stream.ToArray();
            case Stream stream:
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.Length == 0 ? null : copy.ToArray();
                }
            default:
                return null;
        }
    }

    static async Task<byte[]> ReadExternalImageAsync()
    {
        if (PlatformInfo.IsWayland && PlatformInfo.CommandExists("wl-paste"))
        {
            return await ReadTypedAsync("wl-paste",
                new[] { "--list-types" },
                mime => new[] { "--type", mime });
        }
        if (!PlatformInfo.IsWayland && PlatformInfo.CommandExists("xclip"))
        {
            return await ReadTypedAsync("xclip",
                new[] { "-selection", "clipboard", "-t", "TARGETS", "-o" },
                mime => new[] { "-selection", "clipboard", "-t", mime, "-o" });
        }
        return null;
    }

    static async Task<byte[]> ReadTypedAsync(string command, string[] listArgs, Func<string, string[]> typeArgs)
    {
        var listed = await RunAsync(command, listArgs, 1500);
        if (listed.Code != 0) return null;
        var type = ChooseImageType(listed.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        if (type == null) return null;
        var data = await RunAsync(command, typeArgs(type), 3000);
        if (data.Code != 0 || data.Bytes.Length == 0) return null;
        return data.Bytes;
    }

    readonly record struct ProcessOutput(int Code, byte[] Bytes, string Text);

    static async Task<ProcessOutput> RunAsync(string file, string[] args, int timeoutMs)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        try { if (!process.Start()) return new ProcessOutput(-1, Array.Empty<byte>(), ""); }
        catch { return new ProcessOutput(-1, Array.Empty<byte>(), ""); }

        var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var error = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await copy; } catch { }
            try { await error; } catch { }
            return new ProcessOutput(-1, Array.Empty<byte>(), "");
        }

        try { await copy; } catch { }
        try { await error; } catch { }
        var bytes = stdout.ToArray();
        return new ProcessOutput(process.ExitCode, bytes, System.Text.Encoding.UTF8.GetString(bytes));
    }

    static IClipboard ClipboardOf(TopLevel topLevel)
    {
        var clipboard = topLevel?.Clipboard;
        if (clipboard != null) return clipboard;
        if (_hidden == null)
        {
            _hidden = new Window
            {
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                ShowActivated = false,
                SystemDecorations = SystemDecorations.None,
                Background = Brushes.Transparent,
                Opacity = 0,
                Position = new PixelPoint(-10000, -10000),
            };
            _hidden.Show();
        }
        return _hidden.Clipboard;
    }

    static async Task<bool> TryExternalAsync(byte[] png)
    {
        string command = null;
        string[] args = null;
        if (PlatformInfo.IsWayland && PlatformInfo.CommandExists("wl-copy"))
        {
            command = "wl-copy";
            args = new[] { "--type", "image/png" };
        }
        else if (!PlatformInfo.IsWayland && PlatformInfo.CommandExists("xclip"))
        {
            command = "xclip";
            args = new[] { "-selection", "clipboard", "-t", "image/png", "-i" };
        }
        if (command == null) return false;

        try
        {
            var psi = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.StandardInput.BaseStream.WriteAsync(png);
            process.StandardInput.Close();

            using var cts = new CancellationTokenSource(3000);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Clipboard owners such as xclip and wl-copy stay resident by design.
                return true;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
    }
}
