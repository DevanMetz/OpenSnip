# Linux parity

Each Windows feature and setting, and where Linux stands on this branch.

`working` matches Windows. `partial` works, with the gap named here. `missing` is not implemented. Nothing on this list is `not possible`; where one piece cannot exist, the row says so.

## Selection

| Feature | Linux | Where |
|---|---|---|
| Drag a region in desktop pixels (inclusive edge, 3 px before a drag arms) | working | `linux/SnipSession.cs`, `linux/MonitorMap.cs`, `linux/OverlayWindow.cs` |
| One window per monitor at that screen's scale | working | `linux/OverlayWindow.cs`, `linux/MonitorMap.cs` |
| X11 drag across monitors that use different scales | working | `linux/X11Pointer.cs` reads root pixels once the pointer leaves the window |
| Wayland drag across monitors that use different scales | partial | Wayland cannot report the pointer. The press window's coordinates are extended, which stays exact only when both screens share a scale. `linux/OverlayWindow.cs` |
| X11 capture of outputs left of or above the origin | working | `linux/X11Randr.cs`, `linux/Capture.cs`. If the server rejects that rectangle, the grab falls back to the root window at (0, 0). |
| Click grabs the monitor | working | `linux/SnipSession.cs` |
| Click grabs the window under the cursor | missing | `src/CaptureOverlay.cs` |
| `F` grabs the monitor immediately | partial | Linux selects it. Enter completes the grab. `linux/SnipSession.cs` |
| `A` grabs every monitor immediately | partial | Linux selects the desktop. Enter completes the grab. On Windows a recording stays on one monitor. |
| Enter grabs the selection, or the monitor under the cursor | working | `linux/SnipSession.cs` |
| Esc or right-click during a drag cancels the drag first | partial | Linux cancels the whole snip. `linux/SnipSession.cs`, `linux/OverlayWindow.cs` |
| Arrow keys move the pointer 1 px, Shift moves 10 | partial | Linux moves the overlay cursor in desktop pixels and does not move the OS pointer. Wayland cannot warp the pointer. `linux/SnipSession.cs` |
| Magnifier with desktop coordinates | working | `linux/OverlayWindow.cs` |
| Magnifier color | missing | `src/CaptureOverlay.cs` |
| Hint text on the overlay | missing | Left off. The Windows hint is helper text. |
| Nearest-neighbor frozen frame | working | `linux/OverlayWindow.cs` |

## After a snip

| Feature | Linux | Where |
|---|---|---|
| Copy a PNG to the clipboard | working | `linux/App.cs` |
| Auto-save PNG or JPG | working | `linux/CapturedFrame.cs`. `--capture file.jpeg` writes a PNG; only the format string `jpg` is JPEG. |
| Monthly subfolders | working | `src/Settings.cs` |
| Preview on the monitor that was snipped | partial | The preview sits on the primary monitor. `linux/ThumbnailWindow.cs` |
| Preview click opens the editor | missing | Click opens the file with `xdg-open`. `linux/ThumbnailWindow.cs` |
| Drag the preview out as a file | missing | `src/SmallWindows.cs` |
| Open the editor after a snip | missing | `OpenEditorAfterSnip` is stored in `src/Settings.cs` and unused on Linux. |
| Pin an image | missing | `src/SmallWindows.cs` |
| Repeat the last region | working | `linux/App.cs` |
| Countdown at the bottom of the monitor under the pointer | partial | The countdown is centered on the primary monitor. `linux/CountdownWindow.cs` |

## Tray and commands

| Feature | Linux | Where |
|---|---|---|
| Snip, snip after delay, repeat, screenshots folder, settings, exit | working | `linux/App.cs` |
| Tray click starts a snip | working | `linux/App.cs` |
| Record, pin, editor, recordings folder | missing | `src/TrayApp.cs` |
| Tooltip names the snip shortcut | missing | `src/TrayApp.cs` |
| First-run balloon | missing | Left off. It is helper text. `src/TrayApp.cs` |
| Second instance forwards snip, delay, repeat, and settings | working | `linux/SingleInstance.cs` |
| `--capture` and `--diagnostics` | working | `linux/Cli.cs`, `linux/CaptureCli.cs`, `linux/Diagnostics.cs` |
| Built-in global shortcuts (Win+Shift+S, Win+Shift+R, Print Screen) | missing | Settings lists the commands to bind. `linux/SettingsWindow.cs` |

## Settings

| Setting | Linux | Where |
|---|---|---|
| Copy to the clipboard | working | `linux/SettingsWindow.cs` |
| Show a preview, and how long it stays | working | `linux/SettingsWindow.cs` |
| Open the editor, or do nothing else | missing | `src/SettingsForm.cs` |
| Magnifier | working | `linux/SettingsWindow.cs` |
| Delayed-snip length | working | `linux/SettingsWindow.cs` |
| Auto-save, screenshot folder, PNG or JPG, monthly folders | working | `linux/SettingsWindow.cs` |
| Recording folder | missing | `src/SettingsForm.cs` |
| System audio, microphone, device, frame rate, quality, cursor, countdown, copy the video | missing | `src/Settings.cs`, `src/Recording.cs` |
| Shortcut editor and Print Screen | missing | The Shortcuts page lists commands. `linux/SettingsWindow.cs` |
| Start on login | working | XDG autostart. `linux/Autostart.cs` |
| Open the settings folder, reset to defaults | working | `linux/SettingsWindow.cs` |

## Capture

| Backend | Linux | Where |
|---|---|---|
| X11 through libX11, then `maim`, ImageMagick `import`, and `scrot` | partial | The native grab uses `DllImport("libX11")`, so a system that only has `libX11.so.6` fails that path and uses a fallback. `linux/Capture.cs` |
| Wayland portal, then `grim`, `gnome-screenshot`, and `spectacle` | partial | A portal image whose size is not the desktop bounding box is stretched across that box, so mixed DPI is not pixel-exact. `linux/DBus.cs`, `linux/MonitorMap.cs` |

## Recording

| Feature | Linux | Where |
|---|---|---|
| Region recording, audio, cursor, countdown, red outline, control bar | missing | `src/Recording.cs` |
