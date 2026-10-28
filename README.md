# OpenSnip

A fast, no-nonsense screenshot and screen recording tool for Windows and Linux. Built because the built-in Snipping Tool kept getting in the way.

Press **Win + Shift + S** (or Print Screen), drag, done: it's on your clipboard and saved to disk. Press **Win + Shift + R** to record a video of any part of the screen, with system sound (and your mic, if you turn it on). No delay, no toast notifications, no extra windows.

## Why it's better than Snipping Tool

- **Instant.** The screen freezes the moment you press the shortcut, so menus and tooltips stay put while you select.
- **Same muscle memory.** Win + Shift + S and Win + Shift + R open OpenSnip instead of the Snipping Tool. Prefer something else? Set any shortcut you like, which helps on keyboards without a Print Screen key.
- **Pixel-perfect on any monitor setup.** Mixed DPIs and 4K + 1440p side by side work fine, with no blurry or offset captures.
- **Click a window to grab just that window**, or drag any region. A magnifier with pixel coordinates and color helps you line up the edges.
- **Copies to the clipboard as real PNG**, so pasting into Discord, Slack, browsers and docs keeps full quality.
- **Auto-save** to the folder of your choice as PNG or JPG, optionally sorted into monthly folders.
- **Small preview in the corner** that disappears by itself. Click it to annotate, or drag it straight into any app as a file.
- **Annotate in one click:** arrows, boxes, pen, highlighter, text, and pixelate-to-redact. Every edit is copied to the clipboard automatically.
- **Pin snips on top of everything.** Resize them from any corner or edge, zoom, and adjust their opacity.
- **Screen recording** to MP4 (H.264, hardware accelerated) with system sound and an optional microphone, pause, mic mute, and controls that never show up in the video.
- **Repeat the last region** with a shortcut, which is handy for before/after shots.
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

Right-click the tray icon for the same actions, plus **Pin image from clipboard**, **Open image in editor…**, links to both folders, **Settings…** and **Exit**.

Change any of the keyboard shortcuts in **Settings → Shortcuts**: click **Change** and press any combo. You can also give "snip after delay" and "repeat last region" their own shortcuts, or turn off the Print Screen key entirely. `Alt+PrtSc` and `Win+PrtSc` still do what Windows normally does.

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

A recording covers one monitor: a selection that crosses monitor edges is clipped to the monitor containing most of it.

When you stop, the MP4 is saved to `Videos\OpenSnip` and copied to the clipboard by default, so you can paste it straight into Discord, Slack or a folder. A preview pops up that plays the video when clicked.

Options in **Settings → Recording**: system sound, microphone (and which one), 30 or 60 fps, quality, mouse cursor, the countdown, and whether to copy the file to the clipboard. The microphone is only opened when you turn it on.

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
   - `OpenSnip.exe` is small but needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0). If it's missing, the app will show a prompt to install it on first launch.
   - `OpenSnip-standalone.exe` is larger but needs nothing installed.
2. Optionally, build it yourself (see below).
3. Run it. It lives in the system tray. Turn on **Settings → General → Start OpenSnip when Windows starts** if you want it on every boot.

**Requirements:** Windows 10 (version 2004 or later) or Windows 11, 64-bit. Screen recording also needs the [Microsoft Visual C++ Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe), which most PCs already have.

### The Windows Snipping Tool still opens?

OpenSnip intercepts its shortcuts before Windows sees them, so this normally just works. If Print Screen still opens the Snipping Tool on your machine, turn off **Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture"** (on Windows 10 that's **Settings → Ease of Access → Keyboard**). If something else grabs one of the shortcuts first, pick a different one in **Settings → Shortcuts**.

### Something not working?

Run `OpenSnip.exe --diagnostics` to write a report (displays, audio devices, and whether the recorder can start) to `%APPDATA%\OpenSnip\diagnostics.txt`, and attach it to an [issue](https://github.com/DevanMetz/OpenSnip/issues) along with `%APPDATA%\OpenSnip\error.log` if there is one.

## Linux

The Linux app is a separate native Avalonia build. It covers screenshots, annotation, and pinned images; recording and built-in global shortcuts remain Windows-only.

**What works:** X11 capture through libX11, with `maim`, ImageMagick `import` and `scrot` as fallbacks; Wayland capture through the `xdg-desktop-portal` Screenshot API, with `grim`, `gnome-screenshot` and `spectacle` as fallbacks; region, window, monitor and all-monitor selection with a magnifier; clipboard as PNG; auto-save as PNG or JPG; preview; annotation; pinned images (under the pointer on X11, centered on Wayland); tray icon; and settings.

**Install:** download `OpenSnip-linux-x64.tar.gz` from [Releases](https://github.com/DevanMetz/OpenSnip/releases), then:

```bash
tar -xzf OpenSnip-linux-x64.tar.gz
cd OpenSnip-linux-x64
./install.sh
```

The installer copies the binary to `~/.local/bin/opensnip`, so make sure `~/.local/bin` is on your `PATH`.

**Requirements:** x64 Linux; X11 or Wayland with `xdg-desktop-portal`; `xclip` (X11) or `wl-clipboard` (Wayland) for clipboard support. A tray host is optional. Without one, Settings opens on launch and closing it quits OpenSnip, unless a pin or editor is still open. `opensnip --pin` opens only the pin, and `opensnip --edit file.png` opens only the editor.

### Shortcuts

There are no built-in global shortcuts on Linux; bind your own in GNOME Settings → Keyboard → Custom Shortcuts, or KDE System Settings → Shortcuts. Use `opensnip --snip` for a normal snip, `opensnip --snip-delay` for a delayed one, `opensnip --repeat` to re-capture the last region, `opensnip --pin` to pin the clipboard image, `opensnip --pin file.png` to pin a file, `opensnip --edit file.png` to edit an image, and `opensnip --settings` to open settings.

### Selecting a region

| Input | Action |
|---|---|
| Drag | Select a region |
| Click | Grab the highlighted window, or the monitor under the cursor |
| `F` | Whole monitor under the cursor |
| `A` | All monitors |
| `Enter` | Grab what's selected |
| Arrow keys | Nudge the cursor 1 px (`Shift` for 10 px) |
| `Esc` / right-click | Cancel |

On X11 the outline follows the window under the pointer. On Wayland a click grabs the monitor.

### Annotating

Click the preview to annotate, or drag it to drop the file. The tray command **Open image in editor…** and `opensnip --edit file.png` open an existing image. Settings can open the editor instead of the preview. Keys match the editor table above. Pin (`P`) is Windows-only. Edits are copied to the clipboard.

### Command line

```bash
opensnip --pin                # pin the clipboard image
opensnip --pin file.png       # pin a file
opensnip --capture file.png   # capture the whole desktop without UI
opensnip --edit file.png      # open an image in the editor
opensnip --diagnostics [file] # print (and optionally write) a diagnostic report
opensnip --help
```

### Build from source

From the repository root:

```bash
dotnet publish linux/OpenSnip.Linux.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist/linux
```

### Where things live

- Screenshots: `~/Pictures/OpenSnip` by default
- Settings: `~/.config/OpenSnip/settings.json`
- Crash log: `~/.config/OpenSnip/error.log`

## Build from source

Linux users should see the [Linux](#linux) section above; the Linux app has its own publish command.

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

Issues and pull requests are welcome. The code is plain WinForms + GDI+; recording uses [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib). The Linux app is native Avalonia.

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
| `linux/Program.cs` / `linux/App.cs` | Linux app startup, single instance, tray and snip flow |
| `linux/Capture.cs` / `linux/DBus.cs` | X11, Wayland portal and external capture backends |
| `linux/X11Windows.cs` | Top-level X11 windows for click-to-capture |
| `linux/OverlayWindow.cs` | Linux selection overlay and magnifier |
| `linux/PinWindow.cs` | Linux pinned images |
| `linux/Annotation.cs` | Annotation shapes for the Linux editor |
| `linux/EditorWindow.cs` | Linux annotation editor |
| `linux/SettingsWindow.cs` | Linux Settings window |
| `tests/OpenSnip.Tests` | Linux regression tests (capture, window detection, images, command line) |

## License

[MIT](LICENSE). Screen recording is powered by [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) (MIT).
