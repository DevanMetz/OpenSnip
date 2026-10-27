using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace OpenSnip;

sealed class App : Application
{
    public static LaunchAction StartupAction { get; set; }
    public static App Instance { get; private set; }

    public Settings Settings { get; }

    IClassicDesktopStyleApplicationLifetime _desktop;
    TrayIcon _tray;
    NativeMenuItem _delayItem;
    NativeMenuItem _recordItem;
    SettingsWindow _settingsWindow;
    ThumbnailWindow _thumbnail;
    RecordingSession _recording;
    PixelRect? _lastRegion;
    bool _trayAvailable;
    bool _busy;
    bool _exiting;

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
            SingleInstance.SetHandler(action => Dispatcher.UIThread.Post(() => HandleAction(action)));
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

        if (!_trayAvailable) ShowSettings();

        var action = StartupAction;
        StartupAction = LaunchAction.None;
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
        }
    }

    void CreateTray()
    {
        var menu = new NativeMenu();
        menu.Add(Item("Snip", () => StartSnip()));
        _recordItem = Item("Record video", () => _ = DelayedRecord());
        menu.Add(_recordItem);
        _delayItem = Item($"Snip after delay ({Settings.DelaySeconds}s)", () => StartSnip(Settings.DelaySeconds));
        menu.Add(_delayItem);
        menu.Add(Item("Repeat last region", RepeatLast));
        menu.Add(new NativeMenuItemSeparator());
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
            else StartSnip();
        };
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    void HandleAction(LaunchAction action)
    {
        switch (action)
        {
            case LaunchAction.Snip: StartSnip(); break;
            case LaunchAction.SnipDelay: StartSnip(Settings.DelaySeconds); break;
            case LaunchAction.Repeat: RepeatLast(); break;
            case LaunchAction.Settings: ShowSettings(); break;
            case LaunchAction.Record: ToggleRecording(); break;
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
    }

    public void SettingsWindowClosed(SettingsWindow window)
    {
        if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
        if (!_trayAvailable) Dispatcher.UIThread.Post(ExitApp);
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
            var overlay = new OverlayWindow(frame, Settings, record: true);
            var region = await overlay.SelectAsync();
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

    public async void StartSnip(int delaySeconds = 0)
    {
        if (_recording != null || _busy) return;
        _busy = true;
        try
        {
            bool hadThumbnail = _thumbnail != null;
            CloseThumbnail();
            if (hadThumbnail) await Task.Delay(100);
            if (delaySeconds > 0) await CountdownWindow.RunAsync(delaySeconds);

            var frame = await CaptureService.CaptureAsync();
            var overlay = new OverlayWindow(frame, Settings);
            var region = await overlay.SelectAsync();
            if (region is not { } selected) return;

            _lastRegion = selected;
            var cropped = frame.Crop(selected.X, selected.Y, selected.Width, selected.Height);
            await DeliverAsync(cropped);
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

    public async void RepeatLast()
    {
        if (_recording != null || _busy) return;
        if (_lastRegion is not { } region)
        {
            StartSnip();
            return;
        }

        _busy = true;
        bool fallback = false;
        try
        {
            bool hadThumbnail = _thumbnail != null;
            CloseThumbnail();
            if (hadThumbnail) await Task.Delay(100);
            var frame = await CaptureService.CaptureAsync();
            if (region.X < 0 || region.Y < 0 || region.Right > frame.Width || region.Bottom > frame.Height)
            {
                fallback = true;
                return;
            }
            var cropped = frame.Crop(region.X, region.Y, region.Width, region.Height);
            await DeliverAsync(cropped);
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

    async Task DeliverAsync(CapturedFrame frame)
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

        if (Settings.ShowThumbnail || (!copied && saved == null))
            ShowThumbnail(frame, saved);
    }

    void ShowThumbnail(CapturedFrame frame, string path, int? seconds = null, bool play = false)
    {
        CloseThumbnail();
        var thumbnail = new ThumbnailWindow(frame, path, seconds ?? Settings.PreviewSeconds, play);
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
        try { _tray?.Dispose(); } catch { }
        _tray = null;
        SingleInstance.Stop();
        _desktop?.Shutdown();
    }
}
