using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenSnip;

static class ClipboardService
{
    static Window _hidden;

    public static async Task<bool> CopyPngAsync(byte[] png, TopLevel topLevel = null)
    {
        if (png == null || png.Length == 0) return false;
        if (await TryExternalAsync(png)) return true;
        try
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null)
            {
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
                clipboard = _hidden.Clipboard;
            }
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
