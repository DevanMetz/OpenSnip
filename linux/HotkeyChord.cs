namespace OpenSnip;

/// <summary>What a global shortcut does. Names and priority match the Windows hook.</summary>
enum HotkeyCommand
{
    Snip,
    SnipDelay,
    Repeat,
    Record,
}

/// <summary>
/// One shortcut in the same text form Windows stores, for example "Win+Shift+S".
/// Key names follow System.Windows.Forms.Keys so a settings file means the same thing on both.
/// </summary>
readonly record struct HotkeyChord(string Key, bool Win, bool Ctrl, bool Alt, bool Shift)
{
    public bool IsEmpty => string.IsNullOrEmpty(Key);
    public bool HasModifier => Win || Ctrl || Alt || Shift;

    public static HotkeyChord Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        bool win = false, ctrl = false, alt = false, shift = false;
        string key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "win": win = true; break;
                case "ctrl":
                case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default:
                    key = CanonicalKey(part);
                    if (key == null) return default;
                    break;
            }
        }
        return key == null ? default : new HotkeyChord(key, win, ctrl, alt, shift);
    }

    public string Serialize() => IsEmpty ? "" : string.Join("+", Parts(Key));

    public string Display => IsEmpty ? "None" : string.Join(" + ", Parts(DisplayOf(Key)));

    IEnumerable<string> Parts(string key)
    {
        if (Win) yield return "Win";
        if (Ctrl) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
        yield return key;
    }

    /// <summary>Keys that are safe as a global shortcut on their own, without breaking normal typing.</summary>
    public static bool OkWithoutModifier(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key.Length is >= 2 and <= 3 && key[0] == 'F' &&
            int.TryParse(key.AsSpan(1), out int n) && n is >= 1 and <= 24)
            return true;
        return key is "PrintScreen" or "Pause" or "Scroll" or "Insert";
    }

    public static string CanonicalKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        EnsureTable();
        return Names.TryGetValue(name, out var canonical) ? canonical : null;
    }

    public static string DisplayOf(string key)
    {
        EnsureTable();
        return key != null && Displays.TryGetValue(key, out var display) ? display : key ?? "";
    }

    /// <summary>X keysyms for a canonical key, most likely first. Empty when the key has no X equivalent.</summary>
    public static ulong[] Keysyms(string key)
    {
        EnsureTable();
        return key != null && Syms.TryGetValue(key, out var syms) ? syms : Array.Empty<ulong>();
    }

    /// <summary>Canonical key for an X keysym, or null for modifiers and unknown keys.</summary>
    public static string FromKeysym(ulong keysym)
    {
        EnsureTable();
        return BySym.TryGetValue(keysym, out var key) ? key : null;
    }

    static Dictionary<string, string> Names;
    static Dictionary<string, string> Displays;
    static Dictionary<string, ulong[]> Syms;
    static Dictionary<ulong, string> BySym;

    static void EnsureTable()
    {
        if (Names != null) return;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var displays = new Dictionary<string, string>(StringComparer.Ordinal);
        var syms = new Dictionary<string, ulong[]>(StringComparer.Ordinal);
        var bySym = new Dictionary<ulong, string>();

        void Add(string name, string display, params ulong[] keysyms)
        {
            names[name] = name;
            displays[name] = display;
            syms[name] = keysyms;
            foreach (var sym in keysyms)
                bySym.TryAdd(sym, name);
        }

        void Alias(string alias, string name) => names[alias] = name;

        for (int i = 0; i < 26; i++)
        {
            char upper = (char)('A' + i);
            char lower = (char)('a' + i);
            Add(upper.ToString(), upper.ToString(), (ulong)lower, (ulong)upper);
        }
        for (int i = 0; i < 10; i++)
        {
            Add("D" + i, i.ToString(), (ulong)('0' + i));
            Add("NumPad" + i, "Num " + i, 0xffb0UL + (ulong)i);
        }
        for (int i = 1; i <= 24; i++)
            Add("F" + i, "F" + i, 0xffbeUL + (ulong)(i - 1));

        Add("PrintScreen", "PrtSc", 0xff61);
        Add("Return", "Enter", 0xff0d, 0xff8d);
        Add("Prior", "PgUp", 0xff55);
        Add("Next", "PgDn", 0xff56);
        Add("Back", "Backspace", 0xff08);
        Add("Scroll", "ScrLk", 0xff14);
        Add("Capital", "CapsLock", 0xffe5);
        Add("Space", "Space", 0x20);
        Add("Tab", "Tab", 0xff09);
        Add("Escape", "Escape", 0xff1b);
        Add("Insert", "Insert", 0xff63);
        Add("Delete", "Delete", 0xffff);
        Add("Home", "Home", 0xff50);
        Add("End", "End", 0xff57);
        Add("Left", "Left", 0xff51);
        Add("Up", "Up", 0xff52);
        Add("Right", "Right", 0xff53);
        Add("Down", "Down", 0xff54);
        Add("Pause", "Pause", 0xff13);
        Add("Add", "Add", 0xffab);
        Add("Subtract", "Subtract", 0xffad);
        Add("Multiply", "Multiply", 0xffaa);
        Add("Divide", "Divide", 0xffaf);
        Add("Decimal", "Decimal", 0xffae);
        Add("Apps", "Apps", 0xff67);

        // Same value, different enum name. Keep the name Windows writes.
        Alias("Snapshot", "PrintScreen");
        Alias("Enter", "Return");
        Alias("PageUp", "Prior");
        Alias("PageDown", "Next");
        Alias("CapsLock", "Capital");

        Names = names;
        Displays = displays;
        Syms = syms;
        BySym = bySym;
    }
}

/// <summary>The shortcuts this process should honor.</summary>
sealed class HotkeyBindings
{
    public HotkeyChord Snip { get; init; }
    public HotkeyChord Delay { get; init; }
    public HotkeyChord Repeat { get; init; }
    public HotkeyChord Record { get; init; }
    public bool UsePrintScreen { get; init; }

    public static HotkeyBindings From(Settings settings) => new()
    {
        Snip = HotkeyChord.Parse(settings.SnipHotkey),
        Delay = HotkeyChord.Parse(settings.DelayHotkey),
        Repeat = HotkeyChord.Parse(settings.RepeatHotkey),
        Record = HotkeyChord.Parse(settings.RecordHotkey),
        UsePrintScreen = settings.UsePrintScreen,
    };

    /// <summary>
    /// Custom shortcuts win over the Print Screen family. Ctrl+PrtSc repeats, Shift+PrtSc delays,
    /// and Ctrl+Shift+PrtSc repeats, matching the Windows hook. Alt and Win leave Print Screen alone.
    /// </summary>
    public static HotkeyCommand? Match(HotkeyChord chord, HotkeyBindings bindings)
    {
        if (chord.IsEmpty || bindings == null) return null;
        if (!bindings.Snip.IsEmpty && chord == bindings.Snip) return HotkeyCommand.Snip;
        if (!bindings.Delay.IsEmpty && chord == bindings.Delay) return HotkeyCommand.SnipDelay;
        if (!bindings.Repeat.IsEmpty && chord == bindings.Repeat) return HotkeyCommand.Repeat;
        if (!bindings.Record.IsEmpty && chord == bindings.Record) return HotkeyCommand.Record;
        if (bindings.UsePrintScreen && chord.Key == "PrintScreen" && !chord.Alt && !chord.Win)
        {
            if (chord.Ctrl) return HotkeyCommand.Repeat;
            if (chord.Shift) return HotkeyCommand.SnipDelay;
            return HotkeyCommand.Snip;
        }
        return null;
    }

    public string Summary
    {
        get
        {
            var items = new List<string>();
            void Add(HotkeyChord chord)
            {
                if (Match(chord, this) == null) return;
                string text = chord.Display;
                if (!items.Contains(text)) items.Add(text);
            }
            Add(Snip);
            Add(Delay);
            Add(Repeat);
            Add(Record);
            if (UsePrintScreen)
            {
                Add(new HotkeyChord("PrintScreen", false, false, false, false));
                Add(new HotkeyChord("PrintScreen", false, false, false, true));
                Add(new HotkeyChord("PrintScreen", false, true, false, false));
            }
            return items.Count == 0 ? "off" : string.Join(", ", items);
        }
    }
}

/// <summary>Passive grabs for a binding set, including the lock combinations X11 otherwise ignores.</summary>
static class HotkeyGrabs
{
    public const uint ShiftMask = 1;
    public const uint LockMask = 2;
    public const uint ControlMask = 4;
    public const uint Mod2Mask = 16;
    public const uint Mod5Mask = 128;
    public const uint AnyModifier = 32768;

    public static readonly uint[] LockExtras =
    {
        0,
        LockMask,
        Mod2Mask,
        LockMask | Mod2Mask,
        Mod5Mask,
        LockMask | Mod5Mask,
        Mod2Mask | Mod5Mask,
        LockMask | Mod2Mask | Mod5Mask,
    };

    public readonly record struct Grab(int Keycode, uint Modifiers, HotkeyCommand Command);

    public static bool IsSafe(HotkeyChord chord) =>
        !chord.IsEmpty && (chord.HasModifier || HotkeyChord.OkWithoutModifier(chord.Key));

    public static uint ModifierMask(HotkeyChord chord, uint altMask, uint superMask)
    {
        uint mask = 0;
        if (chord.Shift) mask |= ShiftMask;
        if (chord.Ctrl) mask |= ControlMask;
        if (chord.Alt) mask |= altMask;
        if (chord.Win) mask |= superMask;
        return mask;
    }

    public static List<Grab> Plan(HotkeyBindings bindings, Func<string, int> keycode, uint altMask, uint superMask)
    {
        var planned = new List<Grab>();
        if (bindings == null || keycode == null) return planned;

        var chords = new List<HotkeyChord>();
        void Consider(HotkeyChord chord)
        {
            // A bare letter in the settings file must not swallow typing.
            if (!IsSafe(chord) || HotkeyBindings.Match(chord, bindings) == null) return;
            if (chords.Contains(chord)) return;
            chords.Add(chord);
        }

        Consider(bindings.Snip);
        Consider(bindings.Delay);
        Consider(bindings.Repeat);
        Consider(bindings.Record);
        if (bindings.UsePrintScreen)
        {
            Consider(new HotkeyChord("PrintScreen", false, false, false, false));
            Consider(new HotkeyChord("PrintScreen", false, false, false, true));
            Consider(new HotkeyChord("PrintScreen", false, true, false, false));
            Consider(new HotkeyChord("PrintScreen", false, true, false, true));
        }

        var seen = new HashSet<(int Keycode, uint Modifiers)>();
        foreach (var chord in chords)
        {
            int code = keycode(chord.Key);
            if (code == 0) continue;
            var command = HotkeyBindings.Match(chord, bindings).Value;
            uint mask = ModifierMask(chord, altMask, superMask);
            foreach (var extra in LockExtras)
            {
                uint full = mask | extra;
                if (!seen.Add((code, full))) continue;
                planned.Add(new Grab(code, full, command));
            }
        }
        return planned;
    }
}
