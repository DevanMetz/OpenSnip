using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OpenSnip;

sealed class App : Application
{
    public static LaunchAction StartupAction { get; set; }
    public static string StartupArgument { get; set; }
    public static App Instance { get; private set; }

    public Settings Settings { get; }

    IClassicDesktopStyleApplicationLifetime _desktop;
    TrayIcon _tray;
    NativeMenuItem _delayItem;
    NativeMenuItem _recordItem;
    SettingsWindow _settingsWindow;
    ThumbnailWindow _thumbnail;
    RecordingSession _recording;
    X11HotkeyService _hotkeys;
    readonly List<PinWindow> _pins = new();
    readonly List<EditorWindow> _editors = new();
    PixelRect? _lastRegion;
    PixelRect? _lastScreen;
    bool _trayAvailable;
    bool _busy;
    bool _exiting;
    bool _started;
    int _pinJobs;

    public App()
    {
        Settings = Settings.Load();
        SettingsDefaults.ApplyLinux(Settings);
    }

    public override void Initialize()
    {
        Instance = this;
        AppTheme.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SingleInstance.SetHandler(command => Dispatcher.UIThread.Post(() => HandleCommand(command)));
            _ = StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }

    async Task StartAsync()
    {
        try
        {
            await StartAsyncCore();
        }
        catch (Exception ex)
        {
            // Startup must not take the process down when the desktop services
            // (tray host, session bus, portal, clipboard tools) are unavailable.
            Log.Error(ex);
        }
    }

    async Task StartAsyncCore()
    {
        try
        {
            _hotkeys = X11HotkeyService.TryStart(HotkeyBindings.From(Settings), OnHotkey, message =>
                Dispatcher.UIThread.Post(() => _ = ShowErrorAsync(message)));
        }
        catch (Exception ex) { Log.Error(ex); }

        try { _trayAvailable = await DBus.TrayHostAvailableAsync(); }
        catch (Exception ex) { Log.Error(ex); _trayAvailable = false; }

        if (_trayAvailable)
        {
            try { CreateTray(); }
            catch (Exception ex)
            {
                Log.Error(ex);
                try { _tray?.Dispose(); } catch { }
                _tray = null;
                _trayAvailable = false;
            }
        }

        var action = StartupAction;
        var argument = StartupArgument;
        StartupAction = LaunchAction.None;
        StartupArgument = null;
        // A pin or editor is the window for this launch. Settings is only the home window
        // when nothing else is on screen, because closing it quits a tray-less app.
        if (!_trayAvailable && action != LaunchAction.Pin && action != LaunchAction.Edit) ShowSettings();

        switch (action)
        {
            case LaunchAction.Snip:
                StartSnip();
                break;
            case LaunchAction.SnipDelay:
                StartSnip(Settings.DelaySeconds);
                break;
            case LaunchAction.Repeat:
                RepeatLast();
                break;
            case LaunchAction.Settings:
                ShowSettings();
                break;
            case LaunchAction.Record:
                ToggleRecording();
                break;
            case LaunchAction.Pin:
                _ = PinAsync(argument);
                break;
            case LaunchAction.Edit:
                OpenEditFile(argument);
                if (!_trayAvailable && _editors.Count == 0) ShowSettings();
                break;
        }

        _started = true;
        QuitIfIdle();
    }

    void CreateTray()
    {
        var menu = new NativeMenu();
        menu.Add(Item("Snip", () => StartSnip(0, SnipTiming.MenuSettleMs)));
        _delayItem = Item($"Snip after delay ({Settings.DelaySeconds}s)", () => StartSnip(Settings.DelaySeconds, SnipTiming.MenuSettleMs));
        _recordItem = Item("Record video", () => _ = DelayedRecord());
        menu.Add(_recordItem);
        menu.Add(_delayItem);
        menu.Add(Item("Repeat last region", () => RepeatLast(SnipTiming.MenuSettleMs)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Pin image from clipboard", () => _ = PinAsync(null)));
        menu.Add(Item("Open image in editor…", () => _ = OpenImageAsync()));
        menu.Add(Item("Open screenshots folder", OpenScreenshotsFolder));
        menu.Add(Item("Open recordings folder", OpenRecordingsFolder));
        menu.Add(Item("Settings…", () => ShowSettings()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Exit", ExitApp));

        _tray = new TrayIcon
        {
            Icon = IconLoader.Icon,
            ToolTipText = "OpenSnip",
            Menu = menu,
            IsVisible = true,
        };
        _tray.Clicked += (_, _) =>
        {
            if (_recording != null) ToggleRecording();
            else StartSnip(0, SnipTiming.MenuSettleMs);
        };
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        UpdateTrayText();
    }

    static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    void HandleCommand(RemoteCommand command)
    {
        switch (command.Action)
        {
            case LaunchAction.Snip: StartSnip(); break;
            case LaunchAction.SnipDelay: StartSnip(Settings.DelaySeconds); break;
            case LaunchAction.Repeat: RepeatLast(); break;
            case LaunchAction.Settings: ShowSettings(); break;
            case LaunchAction.Record: ToggleRecording(); break;
            case LaunchAction.Pin: _ = PinAsync(command.Argument); break;
            case LaunchAction.Edit: OpenEditFile(command.Argument); break;
        }
    }

    void UpdateRecordingUi()
    {
        bool on = _recording != null;
        if (_recordItem != null) _recordItem.Header = on ? "Stop recording" : "Record video";
        if (_tray != null) _tray.ToolTipText = on ? "OpenSnip — recording (click to stop)" : "OpenSnip";
    }

    public void SettingsChanged()
    {
        if (_delayItem != null) _delayItem.Header = $"Snip after delay ({Settings.DelaySeconds}s)";
        _hotkeys?.Update(HotkeyBindings.From(Settings));
        UpdateTrayText();
    }

    public bool HasGlobalHotkeys => _hotkeys != null;

    public bool BeginShortcutCapture(Action<HotkeyChord?> onChord) =>
        _hotkeys != null && _hotkeys.BeginListen(onChord);

    public void EndShortcutCapture() => _hotkeys?.EndListen();

    void OnHotkey(HotkeyCommand command)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (command == HotkeyCommand.Record)
            {
                ToggleRecording();
                return;
            }
            if (_busy) return;
            switch (command)
            {
                case HotkeyCommand.Snip: StartSnip(); break;
                case HotkeyCommand.SnipDelay: StartSnip(Settings.DelaySeconds); break;
                case HotkeyCommand.Repeat: RepeatLast(); break;
            }
        });
    }

    void UpdateTrayText()
    {
        if (_tray == null) return;
        var bindings = HotkeyBindings.From(Settings);
        string keys = !bindings.Snip.IsEmpty ? bindings.Snip.Display
            : bindings.UsePrintScreen ? "PrtSc" : null;
        string text = keys != null ? $"OpenSnip — {keys} to snip" : "OpenSnip";
        _tray.ToolTipText = text.Length <= 63 ? text : text[..62] + "…";
    }

    public void SettingsWindowClosed(SettingsWindow window)
    {
        if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
        QuitIfIdle();
    }

    public async Task PinAsync(string path)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => _ = PinAsync(path));
            return;
        }

        _pinJobs++;
        try
        {
            CapturedFrame image;
            if (!string.IsNullOrWhiteSpace(path))
            {
                image = CapturedFrame.DecodeFile(path);
            }
            else
            {
                image = await ClipboardService.ReadImageAsync(_settingsWindow);
                if (image == null)
                {
                    await ShowErrorAsync("There's no image on the clipboard.");
                    return;
                }
            }
            ShowPin(image);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            string message = string.IsNullOrWhiteSpace(path)
                ? "Could not open the image."
                : "Could not open that image: " + ex.Message;
            try { await ShowErrorAsync(message); } catch (Exception dialog) { Log.Error(dialog); }
        }
        finally
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _pinJobs--;
                QuitIfIdle();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _pinJobs--;
                    QuitIfIdle();
                });
            }
        }
    }

    void ShowPin(CapturedFrame frame)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowPin(frame));
            return;
        }

        PixelPoint? at = null;
        if (!PlatformInfo.IsWayland)
        {
            var pointer = X11Pointer.TryGet();
            if (pointer is { } point) at = new PixelPoint(point.X, point.Y);
        }

        var pin = new PinWindow(frame, Settings, at);
        var closed = false;
        pin.Closed += (_, _) =>
        {
            closed = true;
            _pins.Remove(pin);
            QuitIfIdle();
        };
        pin.Show();
        if (closed) return;
        _pins.Add(pin);
        pin.Activate();
    }

    void QuitIfIdle()
    {
        if (!_started || _trayAvailable || _busy || _recording != null || _settingsWindow != null || _pins.Count > 0 || _pinJobs > 0 || _editors.Count > 0) return;
        Dispatcher.UIThread.Post(ExitApp);
    }

    public void ShowSettings(string page = "Capture")
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.ShowPage(page);
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this, page);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenScreenshotsFolder()
    {
        try
        {
            string folder = Settings.FolderFor(Settings.SaveFolder);
            Directory.CreateDirectory(folder);
            PlatformInfo.Open(folder);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _ = ShowErrorAsync("Could not open the screenshots folder: " + ex.Message);
        }
    }

    public void OpenRecordingsFolder()
    {
        try
        {
            string folder = Settings.FolderFor(Settings.VideoFolder);
            Directory.CreateDirectory(folder);
            PlatformInfo.Open(folder);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _ = ShowErrorAsync("Could not open the recordings folder: " + ex.Message);
        }
    }

    async Task DelayedRecord()
    {
        if (_recording != null)
        {
            ToggleRecording();
            return;
        }
        await Task.Delay(200);
        if (_exiting || _recording != null || _busy) return;
        ToggleRecording();
    }

    public void ToggleRecording()
    {
        if (_recording != null)
        {
            try { _recording.Stop(); }
            catch (Exception ex) { Log.Error(ex); }
            return;
        }
        StartRecording();
    }

    public async void StartRecording()
    {
        if (_recording != null || _busy || _exiting) return;
        if (!FfmpegPlan.Available)
        {
            await ShowErrorAsync("Screen recording needs ffmpeg.");
            return;
        }
        if (PlatformInfo.IsWayland || !PlatformInfo.HasDisplay)
        {
            await ShowErrorAsync("Screen recording needs an X11 session.");
            return;
        }

        _busy = true;
        try
        {
            bool hadThumbnail = _thumbnail != null;
            CloseThumbnail();
            if (hadThumbnail) await Task.Delay(100);

            var frame = await CaptureService.CaptureAsync();
            var region = await OverlayWindow.SelectAsync(frame, Settings, WindowsFor(frame), record: true);
            if (region is not { } selected) return;

            var monitors = X11Monitors.Query();
            if (monitors.Count == 0)
                monitors = new[] { new PixelRect(0, 0, frame.Width, frame.Height) };
            if (!RecordGeometry.TryNormalize(selected, monitors, out var area))
            {
                await ShowErrorAsync("That area is too small to record.");
                return;
            }

            var audio = await Task.Run(AudioDevices.Discover);
            string system = Settings.RecordSystemAudio ? audio.SystemSource : null;
            string mic = Settings.RecordMicrophone ? audio.ResolveMicrophone(Settings.MicrophoneDevice) : null;
            var session = new RecordingSession(area, Settings, system, mic);
            session.Saved += (path, preview) => Dispatcher.UIThread.Post(() => _ = OnRecordingSavedAsync(session, path, preview));
            session.Failed += message => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_recording, session) && _recording != null) return;
                _ = ShowErrorAsync(message);
            });
            session.Ended += () => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_recording, session)) _recording = null;
                UpdateRecordingUi();
                QuitIfIdle();
            });
            _recording = session;
            UpdateRecordingUi();
            session.Begin();
        }
        catch (CaptureException ex)
        {
            Log.Error(ex);
            await ShowErrorAsync(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            await ShowErrorAsync("Couldn't start recording: " + ex.Message);
            if (_recording != null)
            {
                try { _recording.Discard(); } catch { }
                _recording = null;
                UpdateRecordingUi();
            }
        }
        finally
        {
            _busy = false;
            QuitIfIdle();
        }
    }

    async Task OnRecordingSavedAsync(RecordingSession session, string path, CapturedFrame preview)
    {
        if (_exiting || (_recording != null && !ReferenceEquals(_recording, session))) return;
        bool copied = false;
        if (Settings.CopyRecordingToClipboard && !string.IsNullOrEmpty(path))
        {
            try { copied = await ClipboardService.CopyFileAsync(path); }
            catch (Exception ex) { Log.Error(ex); }
        }
        if (_exiting) return;
        if ((Settings.ShowThumbnail || !copied) && preview != null)
            ShowThumbnail(preview, path, Math.Max(Settings.PreviewSeconds, 8), play: true);
    }

    public async void StartSnip(int delaySeconds = 0, int settleMs = 0)
    {
        if (_recording != null || _busy) return;
        _busy = true;
        try
        {
            bool hadThumbnail = _thumbnail != null;
            CloseThumbnail();
            await SettleAsync(settleMs, hadThumbnail);
            if (delaySeconds > 0) await CountdownWindow.RunAsync(delaySeconds);

            var frame = await CaptureService.CaptureAsync();
            var region = await OverlayWindow.SelectAsync(frame, Settings, WindowsFor(frame));
            if (region is not { } selected) return;

            _lastRegion = selected;
            _lastScreen = OverlayWindow.ScreenRect(frame, selected);
            var cropped = frame.Crop(selected.X, selected.Y, selected.Width, selected.Height);
            await DeliverAsync(cropped, _lastScreen.Value);
        }
        catch (CaptureException ex)
        {
            Log.Error(ex);
            await ShowErrorAsync(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            await ShowErrorAsync("Screen capture failed: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    public async void RepeatLast(int settleMs = 0)
    {
        if (_recording != null || _busy) return;
        if (_lastRegion is not { } region)
        {
            StartSnip(0, settleMs);
            return;
        }

        _busy = true;
        bool fallback = false;
        try
        {
            bool hadThumbnail = _thumbnail != null;
            CloseThumbnail();
            await SettleAsync(settleMs, hadThumbnail);
            var frame = await CaptureService.CaptureAsync();
            if (region.X < 0 || region.Y < 0 || region.Right > frame.Width || region.Bottom > frame.Height)
            {
                fallback = true;
                return;
            }
            var cropped = frame.Crop(region.X, region.Y, region.Width, region.Height);
            var screen = _lastScreen ?? SnipGeometry.ToScreen(region, new PixelRect(0, 0, frame.Width, frame.Height), frame.Width, frame.Height);
            await DeliverAsync(cropped, screen);
        }
        catch (CaptureException ex)
        {
            Log.Error(ex);
            await ShowErrorAsync(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            await ShowErrorAsync("Screen capture failed: " + ex.Message);
        }
        finally
        {
            _busy = false;
            if (fallback) StartSnip();
        }
    }

    static async Task SettleAsync(int settleMs, bool hadThumbnail)
    {
        // The tray menu and our own preview are still on screen for a moment.
        int wait = settleMs;
        if (hadThumbnail) wait = Math.Max(wait, SnipTiming.ThumbnailClearMs);
        if (wait > 0) await Task.Delay(wait);
    }

    static PixelRect[] WindowsFor(CapturedFrame frame)
    {
        // Rectangles are root-window pixels, so they only match a full-root capture.
        var snap = X11Windows.TrySnapshot();
        if (snap == null || snap.Width != frame.Width || snap.Height != frame.Height) return null;
        return snap.Windows;
    }

    async Task DeliverAsync(CapturedFrame frame, PixelRect screenRect)
    {
        bool copied = false;
        if (Settings.CopyToClipboard)
        {
            try
            {
                var png = frame.EncodePng();
                copied = await ClipboardService.CopyPngAsync(png, _settingsWindow);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        string saved = null;
        if (Settings.AutoSave)
        {
            try
            {
                string folder = Settings.FolderFor(Settings.SaveFolder);
                saved = CapturedFrame.UniquePath(folder, Settings.SaveFormat);
                frame.Save(saved, Settings.SaveFormat);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                await ShowErrorAsync("Could not save the snip: " + ex.Message);
            }
        }

        if (Settings.OpenEditorAfterSnip)
        {
            try { OpenEditor(frame); }
            catch (Exception ex)
            {
                Log.Error(ex);
                ShowThumbnail(frame, saved, anchor: screenRect);
            }
        }
        else if (Settings.ShowThumbnail || (!copied && saved == null))
            ShowThumbnail(frame, saved, anchor: screenRect);
    }

    public void OpenEditor(CapturedFrame frame)
    {
        if (frame == null) return;
        var editor = new EditorWindow(frame, Settings);
        _editors.Add(editor);
        editor.Closed += (_, _) =>
        {
            _editors.Remove(editor);
            QuitIfIdle();
        };
        editor.Show();
        editor.Activate();
    }

    public void OpenEditFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _ = ShowErrorAsync("Could not open that image.");
                return;
            }
            OpenEditor(CapturedFrame.DecodeFile(path));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _ = ShowErrorAsync("Could not open that image: " + ex.Message);
        }
    }

    public async Task OpenImageAsync()
    {
        Window host = null;
        bool temporary = false;
        try
        {
            if (_settingsWindow != null && _settingsWindow.IsVisible)
                host = _settingsWindow;
            else
            {
                host = new Window
                {
                    Width = 1,
                    Height = 1,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    SystemDecorations = SystemDecorations.None,
                    Opacity = 0,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(-10000, -10000),
                };
                host.Show();
                temporary = true;
            }

            IStorageFolder start = null;
            if (!string.IsNullOrWhiteSpace(Settings.SaveFolder) && Directory.Exists(Settings.SaveFolder))
                start = await host.StorageProvider.TryGetFolderFromPathAsync(Settings.SaveFolder);
            var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open image",
                AllowMultiple = false,
                SuggestedStartLocation = start,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" },
                    },
                    FilePickerFileTypes.All,
                },
            });
            if (files == null || files.Count == 0) return;
            string path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                await ShowErrorAsync("Could not open that image.");
                return;
            }
            OpenEditor(CapturedFrame.DecodeFile(path));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            await ShowErrorAsync("Could not open that image: " + ex.Message);
        }
        finally
        {
            if (temporary)
            {
                try { host?.Close(); } catch { }
            }
        }
    }

    void ShowThumbnail(CapturedFrame frame, string path, int? seconds = null, bool play = false, PixelRect? anchor = null)
    {
        CloseThumbnail();
        var thumbnail = new ThumbnailWindow(frame, path, seconds ?? Settings.PreviewSeconds, play, anchor);
        thumbnail.Closed += (_, _) =>
        {
            if (ReferenceEquals(_thumbnail, thumbnail)) _thumbnail = null;
        };
        _thumbnail = thumbnail;
        thumbnail.Show();
    }

    void CloseThumbnail()
    {
        if (_thumbnail == null) return;
        try { _thumbnail.Close(); } catch { }
        _thumbnail = null;
    }

    async Task ShowErrorAsync(string message)
    {
        if (_exiting || string.IsNullOrWhiteSpace(message)) return;
        try
        {
            var owner = _settingsWindow;
            if (owner != null && owner.IsVisible) await MessageDialog.AlertAsync(owner, message);
            else await MessageDialog.AlertAsync(null, message);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        try { _recording?.Stop(); } catch (Exception ex) { Log.Error(ex); }
        _recording = null;
        CloseThumbnail();
        try { _hotkeys?.Dispose(); } catch { }
        _hotkeys = null;
        try { _tray?.Dispose(); } catch { }
        _tray = null;
        SingleInstance.Stop();
        _desktop?.Shutdown();
    }
}
