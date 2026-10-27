# Linux parity

Status of each Windows feature on Linux. This file describes the tree it ships with.

`working` matches Windows. `partial` works with a gap named here. `missing` has no Linux path yet. `not possible` cannot exist on Linux; the closest equivalent is named.

## Capture

| Feature | Linux | Files |
|---|---|---|
| Region snip on a frozen screen | working | `linux/Capture.cs`, `linux/OverlayWindow.cs` |
| Click grabs the monitor under the cursor | working | `linux/OverlayWindow.cs` |
| Click grabs the window under the cursor | missing | `linux/OverlayWindow.cs`, `src/CaptureOverlay.cs` |
| `F` captures that monitor now | working | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| `A` captures every monitor now | working | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| Enter confirms the selection, or the monitor when nothing is selected | working | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| Arrow keys nudge 1 px, Shift nudges 10 | partial. On X11 the pointer moves. On Wayland only the overlay cursor moves, because a client cannot warp the Wayland pointer. | `linux/OverlayWindow.cs`, `linux/X11Cursor.cs` |
| Esc and right-click cancel the drag first, then the snip | working | `linux/SnipGesture.cs`, `linux/OverlayWindow.cs` |
| Magnifier with coordinates | working. It opens on the X11 pointer before the mouse moves. | `linux/OverlayWindow.cs` |
| Magnifier color | missing | `linux/OverlayWindow.cs`, `src/CaptureOverlay.cs` |
| Mixed-DPI placement | partial. The overlay uses one scale for the whole desktop. Windows keeps the overlay in physical pixels. | `linux/OverlayWindow.cs` |
| X11 capture | partial. The import is `libX11`, which looks for `libX11.so`. Desktops ship `libX11.so.6`. | `linux/Capture.cs` |
| Wayland capture (portal screenshot) | working | `linux/Capture.cs`, `linux/DBus.cs` |
| Copy PNG to the clipboard | working. `xclip` on X11, `wl-copy` on Wayland, then Avalonia. | `linux/ClipboardService.cs` |
| Auto-save PNG or JPG | working | `linux/CapturedFrame.cs`, `linux/SettingsWindow.cs` |
| Monthly folders | working | `src/Settings.cs` |
| Preview thumbnail | working. It does not take focus, and it opens on the monitor that was snipped. | `linux/ThumbnailWindow.cs`, `linux/SnipGeometry.cs` |
| Click the preview to annotate | partial. Click opens the file with `xdg-open`. | `linux/ThumbnailWindow.cs` |
| Drag the preview into another app | missing | `linux/ThumbnailWindow.cs`, `src/SmallWindows.cs` |
| Annotation editor | missing | `src/EditorForm.cs` |
| Open the editor after a snip | missing. The shared setting exists. Linux follows the preview checkbox. | `src/Settings.cs`, `linux/App.cs`, `linux/SettingsWindow.cs` |
| Pin a snip, including from the clipboard | missing | `src/SmallWindows.cs` |
| Delayed snip | working. The countdown sits at the bottom of the monitor under the pointer, does not take focus, and closes before the shot. On X11 clicks pass through it. On Wayland they do not. | `linux/CountdownWindow.cs`, `linux/X11ClickThrough.cs` |
| Tray snip waits so the menu is not in the shot | working | `linux/App.cs`, `linux/SnipGesture.cs` |
| Repeat the last region | working | `linux/App.cs` |

## Screen recording

| Feature | Linux | Files |
|---|---|---|
| Record a region to MP4 | missing | `src/Recording.cs` |
| Record the window under the cursor | missing | `src/CaptureOverlay.cs` |
| 3-2-1 countdown, outline, and control bar | missing | `src/Recording.cs` |
| System audio and microphone | missing | `src/Recording.cs`, `src/SettingsForm.cs` |
| 30 or 60 fps, quality, cursor | missing | `src/Settings.cs` |
| Copy the MP4 and show a preview | missing | `src/TrayApp.cs`, `src/SmallWindows.cs` |
| `Win+Shift+R` | missing | `src/TrayApp.cs` |

## Shortcuts and settings

| Feature | Linux | Files |
|---|---|---|
| `Win+Shift+S`, delay, repeat, Print Screen | missing. Bind `opensnip --snip`, `--snip-delay`, and `--repeat` in the desktop. | `linux/Cli.cs`, `linux/SettingsWindow.cs` |
| Settings pages for capture, saving, shortcuts, general | partial. There is no Recording page, and Shortcuts lists commands instead of recording a key. | `linux/SettingsWindow.cs` |
| Copy, preview, magnifier, delay | working | `linux/SettingsWindow.cs` |
| Auto-save, folder, PNG or JPG, monthly folders | working | `linux/SettingsWindow.cs` |
| Reset defaults | working | `linux/SettingsWindow.cs` |
| Start on login | working. XDG autostart. | `linux/Autostart.cs` |
| First-run tray balloon | missing. Windows shows a one-time hint. Linux does not add it. | `src/TrayApp.cs` |
| Tray tooltip names the snip shortcut | missing. The tooltip is "OpenSnip". | `linux/App.cs` |
| Diagnostics | working. Capture backends, clipboard tools, portal, and tray host. | `linux/Diagnostics.cs` |
| Second launch forwards snip, delay, repeat, and settings | working | `linux/SingleInstance.cs` |
| `--capture` and `--diagnostics` | working | `linux/Cli.cs`, `linux/CaptureCli.cs` |
