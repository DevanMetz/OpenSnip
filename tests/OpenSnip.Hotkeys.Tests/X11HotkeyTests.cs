using System.Diagnostics;

namespace OpenSnip.Hotkeys.Tests;

public class X11HotkeyTests
{
    [Fact]
    public async Task GrabbedChordFiresAndPlainKeysStayFree()
    {
        if (!X11HotkeyService.IsSupported || !File.Exists("/usr/bin/xdotool")) return;

        var bindings = new HotkeyBindings
        {
            Snip = HotkeyChord.Parse("Ctrl+Alt+Shift+Z"),
            UsePrintScreen = false,
        };
        int hits = 0;
        var commands = new System.Collections.Concurrent.ConcurrentQueue<HotkeyCommand>();
        var listen = new List<HotkeyChord>();
        var listenDone = new TaskCompletionSource<HotkeyChord>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = X11HotkeyService.TryStart(bindings, command => { commands.Enqueue(command); Interlocked.Increment(ref hits); });
        Assert.NotNull(service);

        int codeZ = service.KeycodeFor("Z");
        int codeA = service.KeycodeFor("A");
        uint mask = service.MaskFor(bindings.Snip);
        Assert.NotEqual(0, codeZ);
        Assert.False(service.ForeignGrabSucceeds(codeZ, mask), $"keycode {codeZ} mask {mask} was not held");
        Assert.False(service.ForeignGrabSucceeds(codeZ, mask | HotkeyGrabs.LockMask));
        Assert.True(service.ForeignGrabSucceeds(codeA, 0));

        Press("ctrl+alt+shift+z");
        Wait(() => Volatile.Read(ref hits) >= 1);
        Assert.Equal(1, Volatile.Read(ref hits));

        Press("z");
        Thread.Sleep(250);
        Assert.Equal(1, Volatile.Read(ref hits));

        Press("ctrl+alt+shift+z");
        Wait(() => Volatile.Read(ref hits) >= 2);
        Assert.Equal(2, Volatile.Read(ref hits));

        Assert.True(service.BeginListen(chord =>
        {
            if (chord == null) return;
            lock (listen) listen.Add(chord.Value);
            if (chord.Value.HasModifier) listenDone.TrySetResult(chord.Value);
        }));
        Press("a");
        Wait(() => { lock (listen) return listen.Count >= 1; });
        Press("ctrl+shift+b");
        var completed = await Task.WhenAny(listenDone.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(listenDone.Task, completed);
        service.EndListen();
        lock (listen)
        {
            Assert.Contains(listen, chord => chord == HotkeyChord.Parse("A"));
            Assert.Contains(listen, chord => chord == HotkeyChord.Parse("Ctrl+Shift+B"));
        }

        // The keys from capture must not stay "down" after the grab ends, or the new shortcut never fires.
        service.Update(new HotkeyBindings
        {
            Record = HotkeyChord.Parse("Ctrl+Shift+B"),
            UsePrintScreen = false,
        });
        Thread.Sleep(200);
        Press("ctrl+shift+b");
        Wait(() => Volatile.Read(ref hits) >= 3);
        Assert.Equal(3, Volatile.Read(ref hits));
        Assert.Equal(HotkeyCommand.Record, commands.Last());
        Press("ctrl+shift+b");
        Wait(() => Volatile.Read(ref hits) >= 4);
        Assert.Equal(HotkeyCommand.Record, commands.Last());
        Press("ctrl+alt+shift+z");
        Thread.Sleep(250);
        Assert.Equal(4, Volatile.Read(ref hits));

        service.Dispose();
        Assert.True(X11HotkeyService.ProbeGrabSucceeds(codeZ, mask));

        // A desktop-owned recording chord reports a conflict without taking down the service.
        var other = X11Native.XOpenDisplay(null);
        Assert.NotEqual(IntPtr.Zero, other);
        try
        {
            X11Native.XGrabKey(other, codeZ, mask, X11Native.XDefaultRootWindow(other), 0, 1, 1);
            X11Native.XSync(other, 0);
            var conflict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var blocked = X11HotkeyService.TryStart(new HotkeyBindings { Record = bindings.Snip },
                _ => { }, message => conflict.TrySetResult(message));
            Assert.NotNull(blocked);
            var reported = await Task.WhenAny(conflict.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(conflict.Task, reported);
            Assert.Contains("Start / stop recording", await conflict.Task);
        }
        finally { X11Native.XCloseDisplay(other); }
    }

    static void Press(string combo)
    {
        var start = new ProcessStartInfo("/usr/bin/xdotool") { UseShellExecute = false };
        start.ArgumentList.Add("key");
        start.ArgumentList.Add(combo);
        using var process = Process.Start(start);
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(3000));
        Assert.Equal(0, process.ExitCode);
    }

    static void Wait(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (ready()) return;
            Thread.Sleep(20);
        }
        Assert.Fail("Timed out waiting for the shortcut.");
    }
}
