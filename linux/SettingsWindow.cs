using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OpenSnip;

sealed class SettingsWindow : Window
{
    readonly App _app;
    readonly Settings _s;
    readonly StackPanel _nav;
    readonly ContentControl _host;
    readonly Dictionary<string, Control> _pages = new();
    readonly Dictionary<string, Border> _navItems = new();
    readonly Dictionary<string, TextBlock> _navLabels = new();
    string _currentPage = "Capture";
    readonly List<Action> _refresh = new();

    CheckBox _copy;
    RadioButton _preview;
    RadioButton _editor;
    RadioButton _nothing;
    ComboBox _previewSeconds;
    bool _suppress;
    CheckBox _magnifier;
    ComboBox _delaySeconds;
    CheckBox _autoSave;
    TextBox _folderBox;
    ComboBox _format;
    CheckBox _monthly;
    CheckBox _startOnLogin;
    TextBox _videoFolderBox;
    CheckBox _systemAudio;
    CheckBox _recordMic;
    ComboBox _micBox;
    ComboBox _fps;
    ComboBox _quality;
    CheckBox _recordCursor;
    CheckBox _recordCountdown;
    CheckBox _copyVideo;
    bool _filling;
    List<MicItem> _mics = new() { new MicItem("", "Default") };
    CheckBox _printScreen;
    TextBlock _shortcutStatus;
    readonly List<ShortcutRow> _shortcuts = new();
    ShortcutRow _listeningRow;
    int _listenGeneration;

    public SettingsWindow(App app, string startPage = "Capture")
    {
        _app = app;
        _s = app.Settings;
        Title = "OpenSnip settings";
        Icon = IconLoader.Icon;
        Width = 840;
        Height = 620;
        MinWidth = 760;
        MinHeight = 540;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = AppTheme.Back;

        _nav = new StackPanel { Spacing = 3 };
        var navBorder = new Border
        {
            Background = AppTheme.Panel,
            Padding = new Thickness(12, 18),
            Child = _nav,
        };
        Grid.SetColumn(navBorder, 0);

        _host = new ContentControl { Margin = new Thickness(30, 22, 30, 12) };
        Grid.SetRow(_host, 0);

        var done = new Button { Content = "Done", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 88 };
        done.Click += (_, _) => Close();
        var footer = new Border
        {
            Padding = new Thickness(30, 10, 30, 18),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { done },
            },
        };
        Grid.SetRow(footer, 1);

        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        right.Children.Add(_host);
        right.Children.Add(footer);
        Grid.SetColumn(right, 1);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("188,*") };
        root.Children.Add(navBorder);
        root.Children.Add(right);
        Content = root;

        AddPage("Capture", BuildCapture());
        AddPage("Saving", BuildSaving());
        AddPage("Recording", BuildRecording());
        AddPage("Shortcuts", BuildShortcuts());
        AddPage("General", BuildGeneral());
        ShowPage(startPage);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Closed += (_, _) =>
        {
            StopShortcutCapture();
            app.SettingsWindowClosed(this);
        };
    }

    void AddPage(string name, Control page)
    {
        var label = new TextBlock
        {
            Text = name,
            Foreground = AppTheme.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var item = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Child = label,
        };
        item.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
            ShowPage(name);
            e.Handled = true;
        };
        item.PointerEntered += (_, _) =>
        {
            if (_currentPage != name) item.Background = new SolidColorBrush(Color.Parse("#31313A"));
        };
        item.PointerExited += (_, _) =>
        {
            if (_currentPage != name) item.Background = Brushes.Transparent;
        };
        item.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                ShowPage(name);
                e.Handled = true;
            }
        };
        _nav.Children.Add(item);
        _navItems[name] = item;
        _navLabels[name] = label;
        _pages[name] = page;
    }

    public void ShowPage(string name)
    {
        StopShortcutCapture();
        if (!_pages.TryGetValue(name, out var page)) name = "Capture";
        if (!_pages.TryGetValue(name, out page)) return;
        _currentPage = name;
        _host.Content = page;
        foreach (var (key, item) in _navItems)
        {
            bool selected = key == name;
            item.Background = selected ? AppTheme.NavSelected : Brushes.Transparent;
            _navLabels[key].FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            _navLabels[key].Foreground = AppTheme.Text;
        }
    }

    Control BuildCapture()
    {
        var panel = Page("Capture");
        panel.Children.Add(Heading("After a snip"));

        _copy = Check("Copy to the clipboard", _s.CopyToClipboard, v => _s.CopyToClipboard = v);
        panel.Children.Add(_copy);

        _previewSeconds = IntCombo(_s.PreviewSeconds, new[] { 3, 6, 10, 20 },
            n => $"{n} seconds", v => _s.PreviewSeconds = v);
        SetPreviewDelayEnabled(_s.ShowThumbnail && !_s.OpenEditorAfterSnip);

        _preview = Radio("Show a small preview", _s.ShowThumbnail && !_s.OpenEditorAfterSnip, () =>
        {
            _s.ShowThumbnail = true;
            _s.OpenEditorAfterSnip = false;
        });
        _editor = Radio("Open the editor", _s.OpenEditorAfterSnip, () =>
        {
            _s.ShowThumbnail = false;
            _s.OpenEditorAfterSnip = true;
        });
        _nothing = Radio("Nothing else", !_s.ShowThumbnail && !_s.OpenEditorAfterSnip, () =>
        {
            _s.ShowThumbnail = false;
            _s.OpenEditorAfterSnip = false;
        });
        panel.Children.Add(_preview);
        panel.Children.Add(_editor);
        panel.Children.Add(_nothing);
        panel.Children.Add(Row("Preview stays for", _previewSeconds));

        panel.Children.Add(Heading("Selection"));
        _magnifier = Check("Show the magnifier", _s.ShowMagnifier, v => _s.ShowMagnifier = v);
        panel.Children.Add(_magnifier);

        _delaySeconds = IntCombo(_s.DelaySeconds, new[] { 1, 3, 5, 10 },
            n => $"{n} second{(n == 1 ? "" : "s")}", v => _s.DelaySeconds = v);
        panel.Children.Add(Row("Delayed snip waits", _delaySeconds));

        _refresh.Add(() =>
        {
            _suppress = true;
            try
            {
                _copy.IsChecked = _s.CopyToClipboard;
                _preview.IsChecked = _s.ShowThumbnail && !_s.OpenEditorAfterSnip;
                _editor.IsChecked = _s.OpenEditorAfterSnip;
                _nothing.IsChecked = !_s.ShowThumbnail && !_s.OpenEditorAfterSnip;
                SetPreviewDelayEnabled(_s.ShowThumbnail && !_s.OpenEditorAfterSnip);
                _magnifier.IsChecked = _s.ShowMagnifier;
                SetComboValue(_previewSeconds, _s.PreviewSeconds);
                SetComboValue(_delaySeconds, _s.DelaySeconds);
            }
            finally { _suppress = false; }
        });
        return panel;
    }

    Control BuildSaving()
    {
        var panel = Page("Saving");
        panel.Children.Add(Heading("Screenshots"));

        _autoSave = Check("Save every snip automatically", _s.AutoSave, v => _s.AutoSave = v);
        panel.Children.Add(_autoSave);

        _folderBox = new TextBox
        {
            Text = _s.SaveFolder,
            IsReadOnly = true,
            MinWidth = 300,
        };
        var browse = new Button { Content = "Browse…" };
        browse.Click += async (_, _) => await BrowseFolderAsync();
        var open = new Button { Content = "Open" };
        open.Click += (_, _) => OpenFolder();
        var folderControls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        _folderBox.MinWidth = 200;
        folderControls.Children.Add(_folderBox);
        Grid.SetColumn(_folderBox, 0);
        folderControls.Children.Add(browse);
        Grid.SetColumn(browse, 1);
        folderControls.Children.Add(open);
        Grid.SetColumn(open, 2);
        panel.Children.Add(Row("Folder", folderControls));

        _format = new ComboBox
        {
            ItemsSource = new[] { "PNG", "JPG" },
            SelectedIndex = string.Equals(_s.SaveFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            MinWidth = 150,
        };
        _format.SelectionChanged += (_, _) =>
        {
            _s.SaveFormat = _format.SelectedIndex == 1 ? "jpg" : "png";
            Save();
        };
        panel.Children.Add(Row("Format", _format));

        panel.Children.Add(Heading("Recordings"));
        _videoFolderBox = new TextBox { Text = _s.VideoFolder, IsReadOnly = true, MinWidth = 200 };
        var videoBrowse = new Button { Content = "Browse…" };
        videoBrowse.Click += async (_, _) => await BrowseFolderAsync(path => _s.VideoFolder = path, _videoFolderBox);
        var videoOpen = new Button { Content = "Open" };
        videoOpen.Click += (_, _) => OpenChosenFolder(_s.VideoFolder);
        panel.Children.Add(Row("Folder", FolderControls(_videoFolderBox, videoBrowse, videoOpen)));

        panel.Children.Add(Heading("Organizing"));
        _monthly = Check("Sort into monthly subfolders", _s.MonthlyFolders, v => _s.MonthlyFolders = v);
        panel.Children.Add(_monthly);

        _refresh.Add(() =>
        {
            _autoSave.IsChecked = _s.AutoSave;
            _folderBox.Text = _s.SaveFolder;
            _videoFolderBox.Text = _s.VideoFolder;
            _format.SelectedIndex = string.Equals(_s.SaveFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _monthly.IsChecked = _s.MonthlyFolders;
        });
        return panel;
    }

    Control BuildRecording()
    {
        var panel = Page("Recording");
        panel.Children.Add(Heading("Sound"));
        _systemAudio = Check("Record system sound", _s.RecordSystemAudio, v => _s.RecordSystemAudio = v);
        panel.Children.Add(_systemAudio);
        _recordMic = Check("Record my microphone", _s.RecordMicrophone, v =>
        {
            _s.RecordMicrophone = v;
            if (_micBox != null) _micBox.IsEnabled = v;
        });
        panel.Children.Add(_recordMic);

        _micBox = new ComboBox { MinWidth = 220, IsEnabled = _s.RecordMicrophone };
        FillMics();
        _micBox.SelectionChanged += (_, _) =>
        {
            if (_filling || _micBox.SelectedItem is not MicItem item) return;
            _s.MicrophoneDevice = item.Id;
            Save();
        };
        panel.Children.Add(Row("Microphone", _micBox));

        panel.Children.Add(Heading("Video"));
        _fps = IntCombo(_s.RecordFramerate, new[] { 30, 60 }, n => $"{n} fps", v => _s.RecordFramerate = v);
        panel.Children.Add(Row("Frame rate", _fps));

        _quality = new ComboBox
        {
            ItemsSource = new[] { "High", "Standard" },
            SelectedIndex = _s.RecordHighQuality ? 0 : 1,
            MinWidth = 160,
        };
        _quality.SelectionChanged += (_, _) =>
        {
            if (_filling || _quality.SelectedIndex < 0) return;
            _s.RecordHighQuality = _quality.SelectedIndex == 0;
            Save();
        };
        panel.Children.Add(Row("Quality", _quality));

        _recordCursor = Check("Show the mouse cursor", _s.RecordCursor, v => _s.RecordCursor = v);
        panel.Children.Add(_recordCursor);
        _recordCountdown = Check("Count down before recording", _s.RecordCountdown, v => _s.RecordCountdown = v);
        panel.Children.Add(_recordCountdown);
        _copyVideo = Check("Copy the video to the clipboard", _s.CopyRecordingToClipboard, v => _s.CopyRecordingToClipboard = v);
        panel.Children.Add(_copyVideo);

        _refresh.Add(() =>
        {
            _filling = true;
            _systemAudio.IsChecked = _s.RecordSystemAudio;
            _recordMic.IsChecked = _s.RecordMicrophone;
            _micBox.IsEnabled = _s.RecordMicrophone;
            FillMics();
            SetComboValue(_fps, _s.RecordFramerate);
            _quality.SelectedIndex = _s.RecordHighQuality ? 0 : 1;
            _recordCursor.IsChecked = _s.RecordCursor;
            _recordCountdown.IsChecked = _s.RecordCountdown;
            _copyVideo.IsChecked = _s.CopyRecordingToClipboard;
            _filling = false;
        });

        _ = LoadMicrophonesAsync();
        return panel;
    }

    async Task LoadMicrophonesAsync()
    {
        AudioDevices audio;
        try { audio = await Task.Run(AudioDevices.Discover); }
        catch (Exception ex)
        {
            Log.Error(ex);
            return;
        }
        if (!IsVisible && _micBox == null) return;
        var items = new List<MicItem> { new("", "Default") };
        foreach (var mic in audio.Microphones)
            items.Add(new MicItem(mic.Name, mic.Description));
        _mics = items;
        if (!_mics.Any(item => item.Id == _s.MicrophoneDevice)) _s.MicrophoneDevice = "";
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_micBox == null) return;
            _filling = true;
            FillMics();
            _filling = false;
        });
    }

    void FillMics()
    {
        _micBox.ItemsSource = _mics.ToList();
        int index = _mics.FindIndex(item => item.Id == _s.MicrophoneDevice);
        _micBox.SelectedIndex = index >= 0 ? index : 0;
    }

    Control BuildShortcuts()
    {
        var panel = Page("Shortcuts");
        if (_app.HasGlobalHotkeys)
        {
            ShortcutRowFor(panel, "Snip", () => _s.SnipHotkey, v => _s.SnipHotkey = v);
            ShortcutRowFor(panel, "Snip after delay", () => _s.DelayHotkey, v => _s.DelayHotkey = v);
            ShortcutRowFor(panel, "Repeat last region", () => _s.RepeatHotkey, v => _s.RepeatHotkey = v);
            ShortcutRowFor(panel, "Start / stop recording", () => _s.RecordHotkey, v => _s.RecordHotkey = v);
            _printScreen = Check("Also use the Print Screen key", _s.UsePrintScreen, v => _s.UsePrintScreen = v);
            panel.Children.Add(_printScreen);
            _shortcutStatus = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.Parse("#FFAA5A")),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false,
            };
            panel.Children.Add(_shortcutStatus);
            _refresh.Add(() =>
            {
                foreach (var row in _shortcuts) ShowShortcut(row);
                if (_printScreen != null) _printScreen.IsChecked = _s.UsePrintScreen;
            });
        }
        else
        {
            panel.Children.Add(Heading("Commands"));
            panel.Children.Add(CommandRow("Snip", PlatformInfo.Command("--snip")));
            panel.Children.Add(CommandRow("Delayed snip", PlatformInfo.Command("--snip-delay")));
            panel.Children.Add(CommandRow("Repeat last region", PlatformInfo.Command("--repeat")));
            panel.Children.Add(CommandRow("Pin clipboard image", PlatformInfo.Command("--pin")));
            panel.Children.Add(CommandRow("Record video", PlatformInfo.Command("--record")));
        }

        return panel;
    }

    Control BuildGeneral()
    {
        var panel = Page("General");
        panel.Children.Add(Heading("Startup"));
        _startOnLogin = Check("Start OpenSnip on login", Autostart.Enabled, v =>
        {
            try { Autostart.Set(v); }
            catch (Exception ex)
            {
                _startOnLogin.IsChecked = !v;
                _ = MessageDialog.AlertAsync(this, "Could not update autostart: " + ex.Message);
            }
        });
        panel.Children.Add(_startOnLogin);

        panel.Children.Add(Heading("Settings"));
        var open = new Button { Content = "Open settings folder" };
        open.Click += (_, _) => PlatformInfo.Open(PlatformInfo.SettingsDir);
        var reset = new Button { Content = "Reset to defaults" };
        reset.Click += async (_, _) =>
        {
            if (!await MessageDialog.ConfirmAsync(this, "Reset OpenSnip settings to their defaults?")) return;
            ResetToDefaults();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(open);
        buttons.Children.Add(reset);
        panel.Children.Add(buttons);

        _refresh.Add(() => _startOnLogin.IsChecked = Autostart.Enabled);
        return panel;
    }

    Control CommandRow(string label, string command)
    {
        var box = new TextBox
        {
            Text = command,
            IsReadOnly = true,
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = false,
            MaxLines = 1,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        var copy = new Button { Content = "Copy", MinWidth = 70 };
        copy.Click += async (_, _) =>
        {
            try
            {
                await Clipboard.SetTextAsync(command);
                copy.Content = "Copied";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                timer.Tick += (_, _) => { timer.Stop(); copy.Content = "Copy"; };
                timer.Start();
            }
            catch (Exception ex)
            {
                await MessageDialog.AlertAsync(this, "Could not copy: " + ex.Message);
            }
        };
        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        controls.Children.Add(box);
        Grid.SetColumn(box, 0);
        controls.Children.Add(copy);
        Grid.SetColumn(copy, 1);
        return Row(label, controls);
    }

    void ResetToDefaults()
    {
        StopShortcutCapture();
        _s.CopyToClipboard = true;
        _s.AutoSave = true;
        _s.SaveFolder = SettingsDefaults.DefaultSaveFolder;
        _s.SaveFormat = "png";
        _s.MonthlyFolders = false;
        _s.ShowThumbnail = true;
        _s.OpenEditorAfterSnip = false;
        _s.PreviewSeconds = 6;
        _s.ShowMagnifier = true;
        _s.DelaySeconds = 3;
        _s.VideoFolder = SettingsDefaults.DefaultVideoFolder;
        _s.RecordSystemAudio = true;
        _s.RecordMicrophone = false;
        _s.MicrophoneDevice = "";
        _s.RecordFramerate = 30;
        _s.RecordHighQuality = true;
        _s.RecordCursor = true;
        _s.RecordCountdown = true;
        _s.CopyRecordingToClipboard = true;
        _s.SnipHotkey = "Win+Shift+S";
        _s.DelayHotkey = "";
        _s.RepeatHotkey = "";
        _s.RecordHotkey = "Win+Shift+R";
        _s.UsePrintScreen = true;
        Save();
        foreach (var refresh in _refresh) refresh();
    }

    async Task BrowseFolderAsync() => await BrowseFolderAsync(path => _s.SaveFolder = path, _folderBox);

    async Task BrowseFolderAsync(Action<string> set, TextBox box)
    {
        try
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose a folder",
                AllowMultiple = false,
            });
            if (result.Count == 0) return;
            var local = result[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(local)) return;
            set(local);
            box.Text = local;
            Save();
        }
        catch (Exception ex)
        {
            await MessageDialog.AlertAsync(this, "Could not choose a folder: " + ex.Message);
        }
    }

    void OpenFolder() => OpenChosenFolder(_s.SaveFolder);

    void OpenChosenFolder(string root)
    {
        try
        {
            string folder = _s.FolderFor(root);
            Directory.CreateDirectory(folder);
            PlatformInfo.Open(folder);
        }
        catch (Exception ex)
        {
            _ = MessageDialog.AlertAsync(this, "Could not open the folder: " + ex.Message);
        }
    }

    static Grid FolderControls(TextBox box, Button browse, Button open)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        grid.Children.Add(box);
        Grid.SetColumn(box, 0);
        grid.Children.Add(browse);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(open);
        Grid.SetColumn(open, 2);
        return grid;
    }

    sealed class MicItem
    {
        public MicItem(string id, string label)
        {
            Id = id ?? "";
            Label = string.IsNullOrWhiteSpace(label) ? "Default" : label;
        }

        public string Id { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    void Save()
    {
        _s.Save();
        _app.SettingsChanged();
    }

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var check = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 2) };
        check.IsCheckedChanged += (_, _) =>
        {
            if (_suppress) return;
            if (check.IsChecked is bool on)
            {
                set(on);
                Save();
            }
        };
        return check;
    }

    void SetPreviewDelayEnabled(bool enabled)
    {
        if (_previewSeconds == null) return;
        _previewSeconds.IsEnabled = enabled;
        // The fluent theme keeps a disabled combo at full strength, so dim it.
        _previewSeconds.Opacity = enabled ? 1 : 0.4;
    }

    RadioButton Radio(string text, bool value, Action select)
    {
        var radio = new RadioButton
        {
            Content = text,
            GroupName = "AfterSnip",
            IsChecked = value,
            Foreground = AppTheme.Text,
            Margin = new Thickness(0, 1),
        };
        radio.IsCheckedChanged += (_, _) =>
        {
            if (_suppress || radio.IsChecked != true) return;
            select();
            SetPreviewDelayEnabled(ReferenceEquals(radio, _preview));
            Save();
        };
        return radio;
    }

    ComboBox IntCombo(int current, int[] values, Func<int, string> label, Action<int> set)
    {
        var list = values.ToList();
        if (!list.Contains(current)) list.Add(current);
        list.Sort();
        int index = list.IndexOf(current);
        var combo = new ComboBox
        {
            ItemsSource = list.Select(label).ToList(),
            SelectedIndex = index >= 0 ? index : 0,
            MinWidth = 160,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= list.Count) return;
            set(list[combo.SelectedIndex]);
            Save();
        };
        return combo;
    }

    void SetComboValue(ComboBox combo, int value)
    {
        if (combo.ItemsSource is not IEnumerable<string> items) return;
        var list = items.ToList();
        int index = list.FindIndex(item => item.StartsWith(value + " ", StringComparison.Ordinal) || item == value.ToString());
        if (index >= 0) combo.SelectedIndex = index;
    }

    static StackPanel Page(string title)
    {
        var panel = new StackPanel { Spacing = 9 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 25,
            FontWeight = FontWeight.SemiBold,
            Foreground = AppTheme.Text,
            Margin = new Thickness(0, 0, 0, 14),
        });
        return panel;
    }

    static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = AppTheme.Accent,
        Margin = new Thickness(0, 13, 0, 1),
    };

    void ShortcutRowFor(StackPanel panel, string label, Func<string> get, Action<string> set)
    {
        var row = new ShortcutRow { Get = get, Set = set };
        row.Value = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new Border
        {
            Background = AppTheme.Panel,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinWidth = 180,
            MinHeight = 32,
            Padding = new Thickness(10, 4),
            Child = row.Value,
        };
        row.Change = new Button { Content = "Change…", MinWidth = 88 };
        var clear = new Button { Content = "Clear", MinWidth = 72 };
        row.Change.Click += (_, _) =>
        {
            if (_listeningRow == row) StopShortcutCapture();
            else StartShortcutCapture(row);
        };
        clear.Click += (_, _) =>
        {
            StopShortcutCapture();
            row.Set("");
            ShowShortcut(row);
            Save();
        };
        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        controls.Children.Add(box);
        Grid.SetColumn(box, 0);
        controls.Children.Add(row.Change);
        Grid.SetColumn(row.Change, 1);
        controls.Children.Add(clear);
        Grid.SetColumn(clear, 2);
        panel.Children.Add(Row(label, controls));
        _shortcuts.Add(row);
        ShowShortcut(row);
    }

    void ShowShortcut(ShortcutRow row)
    {
        var chord = HotkeyChord.Parse(row.Get());
        bool listening = _listeningRow == row;
        row.Value.Text = listening ? "Press keys…" : chord.Display;
        row.Value.Foreground = listening ? AppTheme.Accent : chord.IsEmpty ? AppTheme.Muted : AppTheme.Text;
        row.Change.Content = listening ? "Cancel" : "Change…";
    }

    void StartShortcutCapture(ShortcutRow row)
    {
        StopShortcutCapture();
        int generation = ++_listenGeneration;
        if (!_app.BeginShortcutCapture(chord =>
            Dispatcher.UIThread.Post(() => OnShortcutCaptured(row, generation, chord))))
        {
            _ = MessageDialog.AlertAsync(this, "Couldn't listen for a shortcut.");
            return;
        }
        _listeningRow = row;
        if (_shortcutStatus != null) _shortcutStatus.IsVisible = false;
        ShowShortcut(row);
    }

    void OnShortcutCaptured(ShortcutRow row, int generation, HotkeyChord? chord)
    {
        if (generation != _listenGeneration) return;
        if (chord == null || (chord.Value.Key == "Escape" && !chord.Value.HasModifier))
        {
            StopShortcutCapture();
            return;
        }
        if (!HotkeyGrabs.IsSafe(chord.Value))
        {
            if (_shortcutStatus != null)
            {
                _shortcutStatus.Text = $"{chord.Value.Display} needs Ctrl, Alt, Shift, or Win.";
                _shortcutStatus.IsVisible = true;
            }
            return;
        }
        foreach (var other in _shortcuts)
        {
            if (ReferenceEquals(other, row)) continue;
            if (HotkeyChord.Parse(other.Get()) == chord.Value)
            {
                other.Set("");
                ShowShortcut(other);
            }
        }
        row.Set(chord.Value.Serialize());
        StopShortcutCapture();
        Save();
    }

    void StopShortcutCapture()
    {
        _listenGeneration++;
        var row = _listeningRow;
        _listeningRow = null;
        _app.EndShortcutCapture();
        if (row != null) ShowShortcut(row);
        if (_shortcutStatus != null) _shortcutStatus.IsVisible = false;
    }

    sealed class ShortcutRow
    {
        public Func<string> Get;
        public Action<string> Set;
        public TextBlock Value;
        public Button Change;
    }

    static Grid Row(string label, Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 2),
        };
        var text = new TextBlock
        {
            Text = label,
            Foreground = AppTheme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(text);
        Grid.SetColumn(text, 0);
        grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        return grid;
    }
}
