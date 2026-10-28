# Linux parity

Status of each Windows feature on Linux. This file describes the tree it ships with.

`working` matches Windows. `partial` works with a gap named here. `missing` has no Linux path yet. `not possible` cannot exist on Linux; the closest equivalent is named.

## Capture

| Feature | Linux | Files |
|---|---|---|
| Region snip on a frozen screen | working | `linux/Capture.cs`, `linux/OverlayWindow.cs` |
| Click grabs the monitor | working | `linux/OverlayWindow.cs` |
| Click grabs the window under the cursor | partial. X11 only | `linux/OverlayWindow.cs`, `src/CaptureOverlay.cs` |
| `F` selects the monitor; Enter captures it | partial | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| `A` selects all monitors; Enter captures them | partial | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| Enter confirms the selection or monitor; arrows nudge 1 px, or 10 with Shift | working. X11 warps the pointer; Wayland moves the overlay cursor. | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| Esc and right-click cancel the snip | partial | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| Mixed-DPI placement | working on X11, with one window per monitor. Wayland cross-monitor drags are exact when scales match. | `linux/OverlayWindow.cs`, `linux/MonitorMap.cs`, `linux/X11Pointer.cs` |
| X11 outputs left of or above the origin | working. Capture falls back to the root at (0, 0) if the server rejects the bounding box. | `linux/X11Randr.cs`, `linux/Capture.cs` |
| Pixel-grid magnifier, coordinate, and color | working. 15×15 nearest-neighbor cells stay centered on the cursor pixel at frame edges; the strip shows a swatch and `#RRGGBB`. | `linux/Magnifier.cs`, `linux/OverlayWindow.cs` |
| Magnifier can be turned off | working | `linux/SettingsWindow.cs` |
| X11 capture | working | `linux/Capture.cs` |
| Wayland capture (portal screenshot) | working | `linux/Capture.cs`, `linux/DBus.cs` |
| Copy PNG to the clipboard | working. `xclip` on X11, `wl-copy` on Wayland, then Avalonia. | `linux/ClipboardService.cs` |
| Auto-save PNG or JPG | working | `linux/CapturedFrame.cs`, `linux/SettingsWindow.cs` |
| Monthly folders | working | `src/Settings.cs` |
| Preview thumbnail | working. It does not take focus and opens on the snipped monitor. | `linux/ThumbnailWindow.cs`, `linux/SnipGeometry.cs` |
| Click the preview to annotate | working | `linux/ThumbnailWindow.cs` |
| Drag the preview into another app | working | `linux/ThumbnailWindow.cs`, `src/SmallWindows.cs` |
| Annotation editor | working | `src/EditorForm.cs` |
| Open the editor after a snip | working | `src/Settings.cs` |
| Pin a snip, including from the clipboard | partial. Pin from clipboard or file; editor pin remains Windows-only | `src/SmallWindows.cs` |
| Delayed snip | working. The countdown sits at the bottom of the monitor under the pointer, does not take focus, and closes before capture. X11 clicks pass through it; Wayland clicks do not. | `linux/CountdownWindow.cs`, `linux/X11ClickThrough.cs` |
| Tray snip waits for the menu to close | working | `linux/App.cs`, `linux/SnipGesture.cs` |
| Repeat the last region | working | `linux/App.cs` |

## Screen recording

| Feature | Linux | Files |
|---|---|---|
| Record a region to MP4 | working on X11. Tray **Record video** and `opensnip --record`. | `linux/App.cs`, `linux/RecordingSession.cs`, `linux/FramePump.cs` |
| Wayland recording | partial. ScreenCast portal and GStreamer PipeWire plugin feed cropped frames to ffmpeg; share the selected monitor. Restore tokens persist when supported. Pause, mute, audio and countdown use the existing pipeline. Requires GStreamer tools, PipeWire, video conversion/crop/rate plugins. Monitor geometry must be identifiable; mixed-DPI layouts may be rejected. | `linux/PortalScreenCast.cs`, `linux/FramePump.cs`, `linux/RecordingSession.cs`, `linux/App.cs` |
| Record the window under the cursor | working on X11. | `linux/OverlayWindow.cs` |
| One monitor, even dimensions, 16 px minimum | working | `linux/RecordGeometry.cs` |
| 3-2-1 countdown | working | `linux/RecordingChrome.cs` |
| Red outline | working on X11; Wayland uses Xwayland outlines when available. | `linux/RecordingChrome.cs` |
| Pause, microphone mute, stop, discard | working | `linux/RecordingChrome.cs`, `linux/RecordingSession.cs` |
| Outline and bar left out of the video | working on X11. Those rectangles are rebuilt from the wallpaper pixmap and the windows underneath. Wayland cannot exclude the outline or controls; controls may appear for full-monitor recordings. | `linux/X11FrameSource.cs` |
| System audio | partial. PulseAudio or PipeWire monitor source through `pactl` and `ffmpeg`. Video only when `pactl` or a monitor source is missing. | `linux/AudioDevices.cs`, `linux/FfmpegPlan.cs` |
| Microphone, off until enabled | partial. Same `pactl` dependency. Mute keeps a silent stream so paused segments stay compatible. | `linux/AudioDevices.cs` |
| Hardware H.264 | partial. Software libx264 (CRF 18 high, 28 standard). VAAPI and NVENC are not selected. | `linux/FfmpegPlan.cs` |
| 30 or 60 fps | working | `linux/FfmpegPlan.cs`, `linux/SettingsWindow.cs` |
| Mouse cursor in the video | working on X11 (XFixes); Wayland requests an embedded cursor when the portal supports it. | `linux/X11FrameSource.cs`, `linux/PortalScreenCast.cs` |
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
