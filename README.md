# OpenSnip

A fast, no-nonsense screenshot tool for Windows. Built because the built-in Snipping Tool kept getting in the way.

Press **Win + Shift + S** (or Print Screen), drag, done: it's on your clipboard and saved to disk. No delay, no toast notifications, no extra windows.

## Why it's better than Snipping Tool

- **Instant.** The screen freezes the moment you press the shortcut, so menus and tooltips stay put while you select.
- **Same muscle memory.** Win + Shift + S opens OpenSnip instead of the Snipping Tool. Prefer something else? Set any shortcut you like, which helps on keyboards without a Print Screen key.
- **Pixel-perfect on any monitor setup.** Mixed DPIs and 4K + 1440p side by side work fine, with no blurry or offset captures.
- **Click a window to grab just that window**, or drag any region. A magnifier with pixel coordinates and color helps you line up the edges.
- **Copies to the clipboard as real PNG**, so pasting into Discord, Slack, browsers and docs keeps full quality.
- **Optional auto-save** to `Pictures\OpenSnip` (or anywhere you like).
- **Small preview in the corner** that disappears by itself. Click it to annotate, or drag it straight into any app as a file.
- **Annotate in one click:** arrows, boxes, pen, highlighter, text, and pixelate-to-redact. Every edit is copied to the clipboard automatically.
- **Pin snips on top of everything**, for comparing or for keeping a reference visible while you work.
- **Repeat the last region** with one key, which is handy for before/after shots.
- Tiny (about 260 KB), runs in the tray, and needs no installer.

## Shortcuts

### Anywhere

| Keys | Action |
|---|---|
| `Win` + `Shift` + `S` | Snip |
| `PrtSc` | Snip |
| `Shift` + `PrtSc` | Snip after a delay (3 s by default, so you can open menus) |
| `Ctrl` + `PrtSc` | Re-capture the last region |
| Click the tray icon | Snip |

To change these, right-click the tray icon and choose **Keyboard shortcuts…**, click **Change**, and press any combo. You can also give "snip after delay" and "repeat last region" their own shortcuts, or turn off the Print Screen key entirely. `Alt+PrtSc` and `Win+PrtSc` still do what Windows normally does.

### While snipping

| Input | Action |
|---|---|
| Drag | Select a region |
| Click | Grab the highlighted window (or the whole monitor on the desktop) |
| `F` | Whole monitor under the cursor |
| `A` | All monitors |
| `Enter` | Grab what's highlighted |
| Arrow keys | Nudge the cursor 1 px (`Shift` for 10 px) |
| `Esc` / right-click | Cancel the current drag, or cancel the snip |

### Editor

| Keys | Tool / action |
|---|---|
| `A` | Arrow |
| `R` | Box |
| `D` | Pen |
| `H` | Highlighter |
| `T` | Text (Enter to finish, Shift+Enter for a new line) |
| `B` | Redact (pixelate) |
| `1`–`6` | Colors |
| hold `Shift` | Straight lines, 45° arrows, squares |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+C` | Copy (edits also copy automatically) |
| `Ctrl+S` | Save as PNG or JPEG |
| `P` | Pin on top of everything |
| `Esc` | Close |

### Pinned images

Drag to move, scroll to zoom, `Ctrl` + scroll to change opacity, double-click or `Esc` to close, and right-click for more options.

## Install

1. Download `OpenSnip.exe` from [Releases](https://github.com/DevanMetz/OpenSnip/releases) and put it anywhere.
   - `OpenSnip.exe` is tiny but needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0). If it's missing, Windows will offer to install it on first launch.
   - `OpenSnip-standalone.exe` is larger but needs nothing installed.
2. Optionally, build it yourself (see below).
3. Run it. It lives in the system tray. Right-click the tray icon and tick **Start with Windows** if you want it on every boot.

### The Windows Snipping Tool still opens?

OpenSnip intercepts its shortcuts before Windows sees them, so this normally just works. If Print Screen still opens the Snipping Tool on your machine, turn off **Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture"**. If something else grabs Win + Shift + S first, pick a different shortcut under **Keyboard shortcuts…**.

## Build from source

```bash
git clone https://github.com/DevanMetz/OpenSnip
cd OpenSnip/src
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ../dist
```

Requires the .NET 9 SDK on Windows 10 or 11. The result is `dist/OpenSnip.exe`.

For a fully standalone exe that doesn't need the runtime installed (much larger), use `--self-contained true` instead.

## Where things live

- Snips: `Pictures\OpenSnip` by default (change it from the tray menu)
- Settings: `%APPDATA%\OpenSnip\settings.json`
- Crash log, if anything goes wrong: `%APPDATA%\OpenSnip\error.log`

## Contributing

Issues and pull requests are welcome. The code is plain WinForms + GDI+ with no dependencies:

| File | What it does |
|---|---|
| `src/TrayApp.cs` | Tray icon, global hotkeys, snip flow |
| `src/CaptureOverlay.cs` | Frozen-screen selection overlay |
| `src/EditorForm.cs` / `src/Shapes.cs` | Annotation editor and shapes |
| `src/SmallWindows.cs` | Preview thumbnail, countdown, pinned images |
| `src/KeyboardHook.cs` | Low-level keyboard hook (on its own thread) |
| `src/Hotkeys.cs` | Shortcut parsing and the Keyboard shortcuts window |
| `src/Imaging.cs` | Screen capture, window detection, clipboard, saving, icon |

## License

[MIT](LICENSE)
