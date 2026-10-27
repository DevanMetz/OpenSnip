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
    SettingsWindow _settingsWindow;
    ThumbnailWindow _thumbnail;
    readonly List<EditorWindow> _editors = new();
    PixelRect? _lastRegion;
    bool _trayAvailable;
    bool _busy;

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
        StartupAction = LaunchAction.None;
        // An edit session already has a window. Settings is the home window only
        // when nothing else is on screen, because closing it quits a tray-less app.
        if (!_trayAvailable && action != LaunchAction.Edit) ShowSettings();
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
            case LaunchAction.Edit:
                OpenEditFile(StartupArgument);
                if (!_trayAvailable && _editors.Count == 0) ShowSettings();
                break;
        }
    }

    void CreateTray()
    {
        var menu = new NativeMenu();
        menu.Add(Item("Snip", () => StartSnip()));
        _delayItem = Item($"Snip after delay ({Settings.DelaySeconds}s)", () => StartSnip(Settings.DelaySeconds));
        menu.Add(_delayItem);
        menu.Add(Item("Repeat last region", RepeatLast));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Open image in editor…", () => _ = OpenImageAsync()));
        menu.Add(Item("Open screenshots folder", OpenScreenshotsFolder));
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
        _tray.Clicked += (_, _) => StartSnip();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
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
            case LaunchAction.Edit: OpenEditFile(command.Argument); break;
        }
    }

    public void SettingsChanged()
    {
        if (_delayItem != null) _delayItem.Header = $"Snip after delay ({Settings.DelaySeconds}s)";
    }

    public void SettingsWindowClosed(SettingsWindow window)
    {
        if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
        ExitIfIdle();
    }

    void ExitIfIdle()
    {
        if (_trayAvailable || _settingsWindow != null || _editors.Count > 0) return;
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

    public async void StartSnip(int delaySeconds = 0)
    {
        if (_busy) return;
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
        if (_busy) return;
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

        if (Settings.OpenEditorAfterSnip)
        {
            try { OpenEditor(frame); }
            catch (Exception ex)
            {
                Log.Error(ex);
                ShowThumbnail(frame, saved);
            }
        }
        else if (Settings.ShowThumbnail || (!copied && saved == null))
            ShowThumbnail(frame, saved);
    }

    public void OpenEditor(CapturedFrame frame)
    {
        if (frame == null) return;
        var editor = new EditorWindow(frame, Settings);
        _editors.Add(editor);
        editor.Closed += (_, _) =>
        {
            _editors.Remove(editor);
            ExitIfIdle();
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

    void ShowThumbnail(CapturedFrame frame, string path)
    {
        CloseThumbnail();
        var thumbnail = new ThumbnailWindow(frame, path, Settings.PreviewSeconds);
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
        try { _tray?.Dispose(); } catch { }
        _tray = null;
        SingleInstance.Stop();
        _desktop?.Shutdown();
    }
}
