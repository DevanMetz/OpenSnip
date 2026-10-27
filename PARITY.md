# Linux parity

Status of each Windows feature on Linux. This file describes the tree it ships with.

`working` matches Windows. `partial` works, with the gap named here. `missing` has no Linux path yet. `not possible` cannot be added; the closest equivalent is named.

## Selection

| Feature | Linux | Files |
|---|---|---|
| Region snip on a frozen screen | partial. The drag omits the pixel under the pointer at the far edge. Windows includes it. | `linux/OverlayWindow.cs`, `linux/Capture.cs` |
| Click grabs the monitor under the cursor | working | `linux/OverlayWindow.cs` |
| Click grabs the window under the cursor | missing | `src/CaptureOverlay.cs`, `linux/OverlayWindow.cs` |
| `F` captures that monitor now | partial. Linux selects it. Enter or the mouse confirms. | `linux/OverlayWindow.cs` |
| `A` captures every monitor now | partial. Same as `F`. A recording on Windows stays on one monitor. | `linux/OverlayWindow.cs` |
| Enter confirms the selection, or the monitor when nothing is selected | working | `linux/OverlayWindow.cs` |
| Arrow keys nudge 1 px, Shift nudges 10 | partial. Linux moves the overlay cursor and does not move the pointer. | `linux/OverlayWindow.cs` |
| Esc and right-click cancel the drag first, then the snip | partial. Linux cancels the whole snip. | `linux/OverlayWindow.cs` |
| Pixel-grid magnifier, coordinate, and color | working. 15×15 nearest-neighbor cells, the center cell stays on the cursor pixel at the frame edge, and the strip shows a swatch and `#RRGGBB`. Coordinates are pixels in the frozen frame. | `linux/Magnifier.cs`, `linux/OverlayWindow.cs` |
| Magnifier can be turned off | working | `linux/SettingsWindow.cs` |
| Mixed-DPI placement | partial. One window uses one scale for the whole desktop. | `linux/OverlayWindow.cs` |
| Hint text on the overlay | not possible. It is helper text. | `src/CaptureOverlay.cs` |

## Capture and saving

| Feature | Linux | Files |
|---|---|---|
| X11 capture | partial. The import is `libX11`, which looks for `libX11.so`. Desktops ship `libX11.so.6`. `maim`, ImageMagick `import`, and `scrot` are fallbacks. | `linux/Capture.cs` |
| Wayland capture | working. Portal screenshot, then `grim`, `gnome-screenshot`, and `spectacle`. | `linux/Capture.cs`, `linux/DBus.cs` |
| Copy a PNG to the clipboard | working. `xclip` on X11, `wl-copy` on Wayland, then Avalonia. | `linux/ClipboardService.cs` |
| Auto-save PNG or JPG | working. Quality 92 matches Windows. | `linux/CapturedFrame.cs`, `linux/SettingsWindow.cs` |
| `--capture file.jpeg` | partial. Any extension other than `jpg` is written as PNG. | `linux/CapturedFrame.cs`, `linux/CaptureCli.cs` |
| Monthly subfolders | working | `src/Settings.cs` |
| Preview on the monitor that was snipped | partial. The preview sits on the primary monitor and takes focus. | `linux/ThumbnailWindow.cs` |
| Preview stays up while the pointer is over it, then fades | partial. A timer closes it. Hover does not pause it, and it does not fade. | `linux/ThumbnailWindow.cs` |
| Click the preview to annotate | partial. Click opens the file with `xdg-open`. | `linux/ThumbnailWindow.cs` |
| Drag the preview into another app | missing | `src/SmallWindows.cs`, `linux/ThumbnailWindow.cs` |
| Annotation editor | missing | `src/EditorForm.cs`, `src/Shapes.cs` |
| Open the editor after a snip | missing. The shared setting is unused on Linux. | `src/Settings.cs`, `linux/App.cs` |
| Pin a snip, including from the clipboard | missing | `src/SmallWindows.cs` |
| Delayed snip | partial. The countdown is centered on the primary monitor, takes focus, and can appear in the shot. | `linux/CountdownWindow.cs` |
| Tray snip waits so the menu is not in the shot | missing | `linux/App.cs`, `src/TrayApp.cs` |
| Repeat the last region | working | `linux/App.cs` |

## Screen recording

| Feature | Linux | Files |
|---|---|---|
| Record a region to MP4 | missing | `src/Recording.cs` |
| Record the window under the cursor | missing | `src/CaptureOverlay.cs` |
| Countdown, red outline, and control bar | missing | `src/Recording.cs` |
| System audio and microphone | missing | `src/Recording.cs`, `src/SettingsForm.cs` |
| 30 or 60 fps, quality, cursor | missing | `src/Settings.cs` |
| Copy the MP4 and show a preview | missing | `src/TrayApp.cs`, `src/SmallWindows.cs` |
| `Win+Shift+R` | missing | `src/TrayApp.cs` |

## Shortcuts, tray, and commands

| Feature | Linux | Files |
|---|---|---|
| `Win+Shift+S`, delay, repeat, Print Screen | missing. Bind `opensnip --snip`, `--snip-delay`, and `--repeat` in the desktop. | `linux/Cli.cs`, `linux/SettingsWindow.cs` |
| Tray: snip, snip after delay, repeat, screenshots folder, settings, exit | working | `linux/App.cs` |
| Tray click starts a snip | working | `linux/App.cs` |
| Record, pin, open in editor, recordings folder | missing | `src/TrayApp.cs` |
| Tooltip names the snip shortcut | missing. The tooltip is "OpenSnip". | `linux/App.cs` |
| First-run balloon | not possible. It is helper text. The tray menu is the control. | `src/TrayApp.cs` |
| Second launch forwards snip, delay, repeat, and settings | working | `linux/SingleInstance.cs` |
| `--capture` and `--diagnostics` | working | `linux/Cli.cs`, `linux/CaptureCli.cs`, `linux/Diagnostics.cs` |

## Settings

| Setting | Linux | Files |
|---|---|---|
| Copy to the clipboard | working | `linux/SettingsWindow.cs` |
| Show a preview, and how long it stays | working | `linux/SettingsWindow.cs` |
| Open the editor, or do nothing else | partial. Unchecking the preview leaves nothing else. There is no editor option. | `linux/SettingsWindow.cs`, `src/SettingsForm.cs` |
| Magnifier | working | `linux/SettingsWindow.cs` |
| Delayed-snip length | working | `linux/SettingsWindow.cs` |
| Auto-save, screenshot folder, PNG or JPG, monthly folders | working | `linux/SettingsWindow.cs` |
| Recording folder and recording options | missing | `src/SettingsForm.cs` |
| Shortcut editor and Print Screen | missing. The Shortcuts page lists commands. | `linux/SettingsWindow.cs` |
| Start on login | working. XDG autostart. | `linux/Autostart.cs` |
| Open the settings folder | working | `linux/SettingsWindow.cs` |
| Reset to defaults | partial. Resets the options this window shows. Shortcut and recording fields stay as stored. | `linux/SettingsWindow.cs` |
