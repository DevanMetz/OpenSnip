namespace OpenSnip.Hotkeys.Tests;

public class HotkeyTests
{
    [Fact]
    public void ParseUsesTheWindowsTextForm()
    {
        var chord = HotkeyChord.Parse("win+shift+s");
        Assert.Equal("Win+Shift+S", chord.Serialize());
        Assert.Equal("Win + Shift + S", chord.Display);
        Assert.Equal(chord, HotkeyChord.Parse("Shift+Win+S"));
        Assert.Equal("Ctrl+S", HotkeyChord.Parse("control+s").Serialize());
    }

    [Fact]
    public void ParseRejectsUnknownAndModifierOnlyChords()
    {
        Assert.True(HotkeyChord.Parse(null).IsEmpty);
        Assert.True(HotkeyChord.Parse("").IsEmpty);
        Assert.True(HotkeyChord.Parse("Shift").IsEmpty);
        Assert.True(HotkeyChord.Parse("Ctrl+Nope").IsEmpty);
        Assert.True(HotkeyChord.Parse("PrtSc").IsEmpty);
    }

    [Fact]
    public void AliasesCanonicalizeToTheNameWindowsWrites()
    {
        Assert.Equal("PrintScreen", HotkeyChord.Parse("Snapshot").Key);
        Assert.Equal("PrtSc", HotkeyChord.Parse("PrintScreen").Display);
        Assert.Equal("Return", HotkeyChord.Parse("Enter").Key);
        Assert.Equal("Enter", HotkeyChord.Parse("Return").Display);
        Assert.Equal("Prior", HotkeyChord.Parse("PageUp").Key);
        Assert.Equal("PgUp", HotkeyChord.Parse("Prior").Display);
        Assert.Equal("D1", HotkeyChord.Parse("d1").Key);
        Assert.Equal("1", HotkeyChord.Parse("D1").Display);
        Assert.Equal("Num 2", HotkeyChord.Parse("NumPad2").Display);
        Assert.Equal("Backspace", HotkeyChord.Parse("Back").Display);
    }

    [Fact]
    public void BareTypingKeysAreNotSafeShortcuts()
    {
        Assert.True(HotkeyChord.OkWithoutModifier("F5"));
        Assert.True(HotkeyChord.OkWithoutModifier("F24"));
        Assert.True(HotkeyChord.OkWithoutModifier("PrintScreen"));
        Assert.True(HotkeyChord.OkWithoutModifier("Pause"));
        Assert.False(HotkeyChord.OkWithoutModifier("F25"));
        Assert.False(HotkeyChord.OkWithoutModifier("A"));
        Assert.False(HotkeyGrabs.IsSafe(HotkeyChord.Parse("A")));
        Assert.True(HotkeyGrabs.IsSafe(HotkeyChord.Parse("Ctrl+A")));
        Assert.True(HotkeyGrabs.IsSafe(HotkeyChord.Parse("PrintScreen")));
    }

    [Fact]
    public void CustomChordsBeatThePrintScreenFamily()
    {
        var bindings = new HotkeyBindings
        {
            Snip = HotkeyChord.Parse("Ctrl+PrintScreen"),
            Delay = HotkeyChord.Parse("Ctrl+Shift+D"),
            Repeat = HotkeyChord.Parse("Ctrl+Shift+R"),
            UsePrintScreen = true,
        };

        Assert.Equal(HotkeyCommand.Snip, HotkeyBindings.Match(HotkeyChord.Parse("Ctrl+PrintScreen"), bindings));
        Assert.Equal(HotkeyCommand.SnipDelay, HotkeyBindings.Match(HotkeyChord.Parse("Shift+PrintScreen"), bindings));
        Assert.Equal(HotkeyCommand.Repeat, HotkeyBindings.Match(HotkeyChord.Parse("Ctrl+Shift+PrintScreen"), bindings));
        Assert.Equal(HotkeyCommand.Snip, HotkeyBindings.Match(HotkeyChord.Parse("PrintScreen"), bindings));
        Assert.Null(HotkeyBindings.Match(HotkeyChord.Parse("Alt+PrintScreen"), bindings));
        Assert.Null(HotkeyBindings.Match(HotkeyChord.Parse("Win+PrintScreen"), bindings));
        Assert.Equal(HotkeyCommand.SnipDelay, HotkeyBindings.Match(HotkeyChord.Parse("Ctrl+Shift+D"), bindings));
    }

    [Fact]
    public void DefaultSummaryIncludesRecording()
    {
        Assert.Equal(
            "Win + Shift + S, Win + Shift + R, PrtSc, Shift + PrtSc, Ctrl + PrtSc",
            HotkeyBindings.From(new Settings()).Summary);
        Assert.Equal("off", new HotkeyBindings().Summary);
    }

    [Fact]
    public void RecordingUsesConfiguredChordAndLockVariants()
    {
        var settings = new Settings();
        var chord = HotkeyChord.Parse("Win+Shift+R");
        var bindings = HotkeyBindings.From(settings);
        Assert.Equal(chord, bindings.Record);
        Assert.Equal(HotkeyCommand.Record, HotkeyBindings.Match(chord, bindings));
        var grabs = HotkeyGrabs.Plan(bindings, key => key == "R" ? 27 : 0, 8, 64);
        Assert.Equal(HotkeyGrabs.LockExtras.Length, grabs.Count);
        Assert.All(grabs, grab => Assert.Equal(HotkeyCommand.Record, grab.Command));
        Assert.Contains(grabs, grab => grab.Modifiers == (64 | HotkeyGrabs.ShiftMask));

        settings.RecordHotkey = "Ctrl+Alt+F8";
        bindings = HotkeyBindings.From(settings);
        Assert.Null(HotkeyBindings.Match(chord, bindings));
        Assert.Equal(HotkeyCommand.Record, HotkeyBindings.Match(HotkeyChord.Parse(settings.RecordHotkey), bindings));
        settings.RecordHotkey = "";
        Assert.True(HotkeyBindings.From(settings).Record.IsEmpty);
        settings.RecordHotkey = "R";
        Assert.Empty(HotkeyGrabs.Plan(HotkeyBindings.From(settings), key => key == "R" ? 27 : 0, 8, 64));
    }

    [Fact]
    public void DuplicateRecordingChordKeepsExistingPriority()
    {
        var chord = HotkeyChord.Parse("Win+Shift+R");
        var bindings = new HotkeyBindings { Snip = chord, Record = chord };
        var grabs = HotkeyGrabs.Plan(bindings, _ => 27, 8, 64);
        Assert.Equal(HotkeyGrabs.LockExtras.Length, grabs.Count);
        Assert.All(grabs, grab => Assert.Equal(HotkeyCommand.Snip, grab.Command));
    }

    [Fact]
    public void PlanGrabsLockVariantsAndSkipsUnsafeOrUnknownKeys()
    {
        var bindings = new HotkeyBindings
        {
            Snip = HotkeyChord.Parse("Win+Shift+S"),
            Delay = HotkeyChord.Parse("A"),
            UsePrintScreen = true,
        };
        var grabs = HotkeyGrabs.Plan(bindings, key => key switch
        {
            "S" => 39,
            "PrintScreen" => 107,
            _ => 0,
        }, altMask: 8, superMask: 64);

        Assert.Equal(40, grabs.Count);
        Assert.Contains(grabs, g => g.Keycode == 39 && g.Modifiers == (64 | HotkeyGrabs.ShiftMask) && g.Command == HotkeyCommand.Snip);
        Assert.Contains(grabs, g => g.Keycode == 39 && g.Modifiers == (64 | HotkeyGrabs.ShiftMask | HotkeyGrabs.Mod2Mask));
        Assert.Contains(grabs, g => g.Keycode == 107 && g.Modifiers == HotkeyGrabs.ControlMask && g.Command == HotkeyCommand.Repeat);
        Assert.Contains(grabs, g => g.Keycode == 107
            && g.Modifiers == (HotkeyGrabs.ControlMask | HotkeyGrabs.ShiftMask)
            && g.Command == HotkeyCommand.Repeat);
        Assert.DoesNotContain(grabs, g => g.Keycode == 0);

        Assert.Empty(HotkeyGrabs.Plan(
            new HotkeyBindings { Snip = HotkeyChord.Parse("Win+Shift+S"), UsePrintScreen = false },
            _ => 0, 8, 64));
    }
}
