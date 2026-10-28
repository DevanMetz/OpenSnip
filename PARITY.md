# Linux parity

Status of each Windows feature on Linux. This file describes the tree it ships with.

`working` matches Windows. `partial` works with a gap named here. `missing` has no Linux path yet. `not possible` cannot exist on Linux; the closest equivalent is named.

## Capture

| Feature | Linux | Files |
|---|---|---|
| Region snip on a frozen screen | working | `linux/Capture.cs`, `linux/OverlayWindow.cs` |
| Click grabs the monitor | working | `linux/OverlayWindow.cs` |
| Click grabs the window under the cursor | partial. X11 only | `linux/OverlayWindow.cs`, `src/CaptureOverlay.cs` |
| `F` selects the monitor | partial. Sets the selection; Enter or the mouse confirms. Windows completes immediately. While recording, `F` completes the monitor. | `linux/OverlayWindow.cs` |
| `A` selects all monitors | partial. Same as `F` for a snip. While recording, `A` completes the one monitor under the cursor. | `linux/OverlayWindow.cs` |
| Enter confirms, arrows nudge, Esc and right-click cancel | working | `linux/OverlayWindow.cs` |
| Magnifier with coordinates | working | `linux/OverlayWindow.cs` |
| Magnifier color | working | `linux/OverlayWindow.cs` |
| X11 capture | working | `linux/Capture.cs` |
| Wayland capture (portal screenshot) | working | `linux/Capture.cs`, `linux/DBus.cs` |
| Copy PNG to the clipboard | working. `xclip` on X11, `wl-copy` on Wayland, then Avalonia. | `linux/ClipboardService.cs` |
| Auto-save PNG or JPG | working | `linux/CapturedFrame.cs`, `linux/SettingsWindow.cs` |
| Monthly folders | working | `src/Settings.cs` |
| Preview thumbnail | working | `linux/ThumbnailWindow.cs` |
| Click the preview to annotate | working | `linux/ThumbnailWindow.cs` |
| Drag the preview into another app | working | `linux/ThumbnailWindow.cs`, `src/SmallWindows.cs` |
| Annotation editor | working | `src/EditorForm.cs` |
| Open the editor after a snip | working | `src/Settings.cs` |
| Pin a snip, including from the clipboard | partial. Pin from clipboard or file; editor pin remains Windows-only | `src/SmallWindows.cs` |
| Delayed snip | working | `linux/CountdownWindow.cs`, `linux/App.cs` |
| Repeat the last region | working | `linux/App.cs` |

## Screen recording

| Feature | Linux | Files |
|---|---|---|
| Record a region to MP4 | working on X11. Tray **Record video** and `opensnip --record`. | `linux/App.cs`, `linux/RecordingSession.cs`, `linux/FramePump.cs` |
| Wayland recording | missing. The portal ScreenCast API is not wired. The app reports that recording needs an X11 session. | `linux/App.cs` |
| Record the window under the cursor | working on X11. | `linux/OverlayWindow.cs` |
| One monitor, even dimensions, 16 px minimum | working | `linux/RecordGeometry.cs` |
| 3-2-1 countdown | working | `linux/RecordingChrome.cs` |
| Red outline | working | `linux/RecordingChrome.cs` |
| Pause, microphone mute, stop, discard | working | `linux/RecordingChrome.cs`, `linux/RecordingSession.cs` |
| Outline and bar left out of the video | working on X11. Those rectangles are rebuilt from the wallpaper pixmap and the windows underneath. | `linux/X11FrameSource.cs` |
| System audio | partial. PulseAudio or PipeWire monitor source through `pactl` and `ffmpeg`. Video only when `pactl` or a monitor source is missing. | `linux/AudioDevices.cs`, `linux/FfmpegPlan.cs` |
| Microphone, off until enabled | partial. Same `pactl` dependency. Mute keeps a silent stream so paused segments stay compatible. | `linux/AudioDevices.cs` |
| Hardware H.264 | partial. Software libx264 (CRF 18 high, 28 standard). VAAPI and NVENC are not selected. | `linux/FfmpegPlan.cs` |
| 30 or 60 fps | working | `linux/FfmpegPlan.cs`, `linux/SettingsWindow.cs` |
| Mouse cursor in the video | working. XFixes cursor image. | `linux/X11FrameSource.cs` |
| Copy the MP4 | partial. `text/uri-list` through `xclip` or `wl-copy`. | `linux/ClipboardService.cs`, `linux/FileUri.cs` |
| Preview still that plays on click | working. Play badge; click opens the file. The preview stays at least 8 seconds. | `linux/ThumbnailWindow.cs`, `linux/App.cs` |
| Drag the video preview out | missing | `linux/ThumbnailWindow.cs` |
| `Win+Shift+R` | missing. The tray item and `opensnip --record` start and stop a recording. A global key grab is not built in. | `linux/App.cs`, `linux/Cli.cs` |
| Tray click stops a recording, otherwise snips | working | `linux/App.cs` |
| Snip blocked while recording | working | `linux/App.cs` |
| Open the recordings folder | working | `linux/App.cs` |
| Save under Videos/OpenSnip as `Recording yyyy-MM-dd HH.mm.ss.mp4` | working | `linux/FfmpegPlan.cs`, `linux/SettingsDefaults.cs` |

## Shortcuts and settings

| Feature | Linux | Files |
|---|---|---|
| `Win+Shift+S`, delay, repeat, Print Screen | working on X11. Bind commands in the desktop on Wayland. | `linux/Cli.cs`, `linux/SettingsWindow.cs` |
| Settings pages for capture, saving, recording, shortcuts, general | working. Shortcuts supports X11 key bindings and lists commands on Wayland. | `linux/SettingsWindow.cs` |
| Recording options: system sound, microphone, device, frame rate, quality, cursor, countdown, copy file | working | `linux/SettingsWindow.cs` |
| Reset defaults, including the recording options and video folder | working | `linux/SettingsWindow.cs`, `linux/SettingsDefaults.cs` |
| Start on login | working. XDG autostart. | `linux/Autostart.cs` |
| First-run tray balloon | missing. The tray menu is available as soon as the icon is. | `src/TrayApp.cs`, `linux/App.cs` |
| Diagnostics include capture, clipboard, ffmpeg, and audio devices | working | `linux/Diagnostics.cs` |
| Second launch forwards snip, delay, repeat, record, and settings | working | `linux/SingleInstance.cs` |
