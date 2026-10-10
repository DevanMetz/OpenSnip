namespace OpenSnip;

/// <summary>A key plus the exact set of modifiers that must be held with it.</summary>
readonly record struct Hotkey(Keys Key, bool Win, bool Ctrl, bool Alt, bool Shift)
{
    public bool IsEmpty => Key == Keys.None;
    public bool HasModifier => Win || Ctrl || Alt || Shift;

    /// <summary>Parses the settings-file form, e.g. "Win+Shift+S".</summary>
    public static Hotkey Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        bool win = false, ctrl = false, alt = false, shift = false;
        Keys key = Keys.None;
        foreach (var part in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "win": win = true; break;
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default: if (!Enum.TryParse(part, true, out key)) return default; break;
            }
        }
        return key == Keys.None ? default : new Hotkey(key, win, ctrl, alt, shift);
    }

    public string Serialize() => IsEmpty ? "" : string.Join("+", Parts(Key.ToString()));

    public string Display => IsEmpty ? "None" : string.Join(" + ", Parts(KeyName(Key)));

    IEnumerable<string> Parts(string key)
    {
        if (Win) yield return "Win";
        if (Ctrl) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
        yield return key;
    }

    public static string KeyName(Keys k)
    {
        if (k is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k is >= Keys.NumPad0 and <= Keys.NumPad9) return "Num " + (k - Keys.NumPad0);
        switch (k)
        {
            case Keys.PrintScreen: return "PrtSc";
            case Keys.Return: return "Enter";
            case Keys.Prior: return "PgUp";
            case Keys.Next: return "PgDn";
            case Keys.Back: return "Backspace";
            case Keys.Scroll: return "ScrLk";
            case Keys.Capital: return "CapsLock";
        }
        char c = Native.KeyChar(k);
        return c > ' ' ? char.ToUpperInvariant(c).ToString() : k.ToString();
    }

    public static bool IsModifierKey(Keys k) => k is
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    /// <summary>Keys that are safe as a global shortcut on their own, without breaking normal typing.</summary>
    public static bool OkWithoutModifier(Keys k) =>
        k is >= Keys.F1 and <= Keys.F24 or Keys.PrintScreen or Keys.Pause or Keys.Scroll or Keys.Insert
          or Keys.LaunchApplication1 or Keys.LaunchApplication2 or Keys.LaunchMail or Keys.SelectMedia;
}

/// <summary>Immutable snapshot of the configured shortcuts, swapped atomically for the hook thread.</summary>
sealed class HotkeySet
{
    public Hotkey Snip { get; init; }
    public Hotkey Delay { get; init; }
    public Hotkey Repeat { get; init; }
    public Hotkey Record { get; init; }
    public bool PrintScreen { get; init; }

    public static HotkeySet From(Settings s) => new()
    {
        Snip = Hotkey.Parse(s.SnipHotkey),
        Delay = Hotkey.Parse(s.DelayHotkey),
        Repeat = Hotkey.Parse(s.RepeatHotkey),
        Record = Hotkey.Parse(s.RecordHotkey),
        PrintScreen = s.UsePrintScreen,
    };

    public bool Contains(Hotkey h) => !h.IsEmpty && (h == Snip || h == Delay || h == Repeat || h == Record);
    public string RecordDisplay => Record.IsEmpty ? null : Record.Display;

    /// <summary>What to show in menus and tooltips for the main snip shortcut.</summary>
    public string SnipDisplay => !Snip.IsEmpty ? Snip.Display : PrintScreen ? "PrtSc" : null;
    public string DelayDisplay => !Delay.IsEmpty ? Delay.Display : PrintScreen ? "Shift + PrtSc" : null;
    public string RepeatDisplay => !Repeat.IsEmpty ? Repeat.Display : PrintScreen ? "Ctrl + PrtSc" : null;
}
