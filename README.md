# OpenSnip

A fast, no-nonsense screenshot and screen recording tool for Windows. Built because the built-in Snipping Tool kept getting in the way.

Press **Win + Shift + S** (or Print Screen), drag, done: it's on your clipboard and saved to disk. Press **Win + Shift + R** to record a video of any part of the screen, with system sound and your mic. No delay, no toast notifications, no extra windows.

## Why it's better than Snipping Tool

- **Instant.** The screen freezes the moment you press the shortcut, so menus and tooltips stay put while you select.
- **Same muscle memory.** Win + Shift + S and Win + Shift + R open OpenSnip instead of the Snipping Tool. Prefer something else? Set any shortcut you like, which helps on keyboards without a Print Screen key.
- **Pixel-perfect on any monitor setup.** Mixed DPIs and 4K + 1440p side by side work fine, with no blurry or offset captures.
- **Click a window to grab just that window**, or drag any region. A magnifier with pixel coordinates and color helps you line up the edges.
- **Copies to the clipboard as real PNG**, so pasting into Discord, Slack, browsers and docs keeps full quality.
- **Auto-save** to the folder of your choice as PNG or JPG, optionally sorted into monthly folders.
- **Small preview in the corner** that disappears by itself. Click it to annotate, or drag it straight into any app as a file.
- **Annotate in one click:** arrows, boxes, pen, highlighter, text, and pixelate-to-redact. Every edit is copied to the clipboard automatically.
- **Pin snips on top of everything.** Resize them from any corner or edge, zoom, and fade them out.
- **Screen recording** to MP4 (H.264, hardware accelerated) with system sound and microphone, pause, mic mute, and controls that never show up in the video.
- **Repeat the last region** with one key, which is handy for before/after shots.
- Small (about 1.5 MB), runs in the tray, and needs no installer.

## Shortcuts

### Anywhere

| Keys | Action |
|---|---|
| `Win` + `Shift` + `S` | Snip |
| `Win` + `Shift` + `R` | Start / stop recording |
| `PrtSc` | Snip |
| `Shift` + `PrtSc` | Snip after a delay (3 s by default, so you can open menus) |
| `Ctrl` + `PrtSc` | Re-capture the last region |
| Click the tray icon | Snip (or stop recording) |

Change any of these in **Settings → Shortcuts**: click **Change** and press any combo. You can also give "snip after delay" and "repeat last region" their own shortcuts, or turn off the Print Screen key entirely. `Alt+PrtSc` and `Win+PrtSc` still do what Windows normally does.

### While selecting (snip or record)

| Input | Action |
|---|---|
| Drag | Select a region |
| Click | Grab the highlighted window (or the whole monitor on the desktop) |
| `F` | Whole monitor under the cursor |
| `A` | All monitors (snips only) |
| `Enter` | Grab what's highlighted |
| Arrow keys | Nudge the cursor 1 px (`Shift` for 10 px) |
| `Esc` / right-click | Cancel the current drag, or cancel |

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

- Drag to move.
- Drag any corner or edge to resize. The image keeps its proportions; hold `Shift` to stretch freely.
- Scroll to zoom, or `Ctrl` + scroll to change opacity. Press `1` to go back to actual size.
- Double-click or `Esc` to close, and right-click for more options.

## Screen recording

Press **Win + Shift + R** (or choose **Record video** from the tray menu), then pick an area, a window or a whole monitor the same way you would for a screenshot. After an optional 3-2-1 countdown, a red outline marks what's being recorded, and a small bar lets you **pause**, **mute your mic**, **stop**, or **throw the recording away**. The outline and bar are hidden from the video itself.

When you stop, the MP4 is saved to `Videos\OpenSnip`, and the file is copied to the clipboard so you can paste it straight into Discord, Slack or a folder. A preview pops up that plays the video when clicked.

Options in **Settings → Recording**: system sound, microphone (and which one), 30 or 60 fps, quality, mouse cursor, and the countdown. The microphone is only opened when you turn it on.

## Settings

Right-click the tray icon and choose **Settings…**. There's a page for each area:

- **Capture:** copy to clipboard, what happens after a snip (preview, editor or nothing), how long the preview stays, the magnifier, and the delayed-snip length.
- **Saving:** auto-save, the screenshot and recording folders, PNG or JPG, and monthly subfolders.
- **Recording:** sound, microphone, frame rate, quality, cursor, and countdown.
- **Shortcuts:** every global shortcut, plus the Print Screen key.
- **General:** start with Windows, open the settings folder, and reset to defaults.

Changes apply immediately.

## Install

1. Download `OpenSnip.exe` from [Releases](https://github.com/DevanMetz/OpenSnip/releases) and put it anywhere.
   - `OpenSnip.exe` is small but needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0). If it's missing, Windows will offer to install it on first launch.
   - `OpenSnip-standalone.exe` is larger but needs nothing installed.
2. Optionally, build it yourself (see below).
3. Run it. It lives in the system tray. Turn on **Settings → General → Start OpenSnip when Windows starts** if you want it on every boot.

**Requirements:** Windows 10 (version 2004 or later) or Windows 11, 64-bit. Screen recording also needs the [Microsoft Visual C++ Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe), which most PCs already have.

### The Windows Snipping Tool still opens?

OpenSnip intercepts its shortcuts before Windows sees them, so this normally just works. If Print Screen still opens the Snipping Tool on your machine, turn off **Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture"**. If something else grabs one of the shortcuts first, pick a different one in **Settings → Shortcuts**.

### Something not working?

Run `OpenSnip.exe --diagnostics` to write a report (displays, audio devices, and whether the recorder can start) to `%APPDATA%\OpenSnip\diagnostics.txt`, and attach it to an [issue](https://github.com/DevanMetz/OpenSnip/issues) along with `%APPDATA%\OpenSnip\error.log` if there is one.

## Build from source

```bash
git clone https://github.com/DevanMetz/OpenSnip
cd OpenSnip/src
dotnet publish -c Release -p:PublishSingleFile=true -o ../dist
```

Requires the .NET 9 SDK on Windows. The result is `dist/OpenSnip.exe`. The project targets x64 because the recording library is native.

For a fully standalone exe that doesn't need the runtime installed (much larger), add `--self-contained true`.

## Where things live

- Screenshots: `Pictures\OpenSnip` by default
- Recordings: `Videos\OpenSnip` by default
- Settings: `%APPDATA%\OpenSnip\settings.json`
- Crash log, if anything goes wrong: `%APPDATA%\OpenSnip\error.log`

## Contributing

Issues and pull requests are welcome. The code is plain WinForms + GDI+; recording uses [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib).

| File | What it does |
|---|---|
| `src/TrayApp.cs` | Tray icon, global shortcuts, snip and record flow |
| `src/CaptureOverlay.cs` | Frozen-screen selection overlay (snips and recordings) |
| `src/EditorForm.cs` / `src/Shapes.cs` | Annotation editor and shapes |
| `src/Recording.cs` | Recording session, red outline, and control bar |
| `src/SettingsForm.cs` | The Settings window |
| `src/SmallWindows.cs` | Preview thumbnail, countdown, pinned images |
| `src/Hotkeys.cs` | Shortcut parsing and matching |
| `src/KeyboardHook.cs` | Low-level keyboard hook (on its own thread) |
| `src/Imaging.cs` | Screen capture, window detection, clipboard, saving, icon |

## License

[MIT](LICENSE). Screen recording is powered by [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) (MIT).
