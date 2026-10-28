using System.Diagnostics;

namespace OpenSnip.Tests;

public class X11PointerTests
{
    [Fact]
    public void Query_returns_the_warped_pointer()
    {
        if (!File.Exists("/usr/bin/Xvfb")) return;

        int number = FreeDisplay();
        if (number < 0) return;
        string name = ":" + number;

        using var server = StartXvfb(name);
        try
        {
            Assert.True(WaitForSocket(number, server), "Xvfb did not create a display socket.");
            Assert.True(X11Pointer.TryWarp(name, 123, 45));
            Assert.Equal((123, 45), X11Pointer.TryGet(name));
        }
        finally
        {
            Stop(server, number);
        }
    }

    static Process StartXvfb(string name)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "Xvfb",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(name);
        psi.ArgumentList.Add("-screen");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("640x480x24");
        psi.ArgumentList.Add("-nolisten");
        psi.ArgumentList.Add("tcp");
        psi.ArgumentList.Add("-ac");
        // Without this, Xvfb resets when the warping client disconnects and the pointer jumps back.
        psi.ArgumentList.Add("-noreset");
        return Process.Start(psi);
    }

    static int FreeDisplay()
    {
        for (int n = 80; n < 140; n++)
        {
            if (!File.Exists("/tmp/.X11-unix/X" + n)) return n;
        }
        return -1;
    }

    static bool WaitForSocket(int display, Process server)
    {
        string socket = "/tmp/.X11-unix/X" + display;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(socket)) return true;
            if (server == null || server.HasExited) return false;
            Thread.Sleep(30);
        }
        return File.Exists(socket);
    }

    static void Stop(Process server, int display)
    {
        try { if (server != null && !server.HasExited) server.Kill(entireProcessTree: true); } catch { }
        try
        {
            foreach (var process in Process.GetProcessesByName("Xvfb"))
            {
                try
                {
                    string cmd = File.ReadAllText($"/proc/{process.Id}/cmdline").Replace('\0', ' ');
                    if (cmd.Contains(":" + display + " ") || cmd.EndsWith(":" + display, StringComparison.Ordinal))
                        process.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }
        catch { }
    }
}
