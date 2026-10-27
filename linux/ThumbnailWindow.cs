using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace OpenSnip;

sealed class ThumbnailWindow : Window
{
    readonly string _path;
    readonly PixelRect? _anchor;
    readonly DispatcherTimer _timer;

    public ThumbnailWindow(CapturedFrame frame, string savedPath, int seconds, PixelRect? anchor = null)
    {
        _path = savedPath;
        _anchor = anchor;
        Title = "OpenSnip";
        Icon = IconLoader.Icon;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        double maxWidth = 300;
        double maxHeight = 200;
        double ratio = Math.Min(maxWidth / frame.Width, maxHeight / frame.Height);
        double width = Math.Max(80, Math.Round(frame.Width * ratio));
        double height = Math.Max(60, Math.Round(frame.Height * ratio));
        Width = width + 8;
        Height = height + 8;

        IImage source;
        if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
        {
            try { source = new Bitmap(savedPath); }
            catch { source = frame.ToBitmap(); }
        }
        else source = frame.ToBitmap();

        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Border
        {
            Background = AppTheme.Panel,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = image,
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
        _timer.Tick += (_, _) => Close();
        Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(PlaceBottomRight, DispatcherPriority.Background);
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
        PointerPressed += OnPressed;
    }

    void PlaceBottomRight()
    {
        var screen = ScreenForAnchor();
        if (screen == null) return;
        var area = screen.WorkingArea;
        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        Position = new PixelPoint(
            (int)(area.Right - Width * scale - 16 * scale),
            (int)(area.Bottom - Height * scale - 16 * scale));
    }

    Screen ScreenForAnchor()
    {
        var screens = Screens?.All;
        if (screens == null || screens.Count == 0) return Screens?.Primary;
        if (_anchor is not { } anchor) return Screens.Primary ?? screens[0];

        var bounds = new PixelRect[screens.Count];
        for (int i = 0; i < screens.Count; i++) bounds[i] = screens[i].Bounds;
        int index = SnipGeometry.BestScreen(anchor, bounds);
        if (index < 0 || index >= screens.Count) return Screens.Primary ?? screens[0];
        return screens[index];
    }

    void OnPressed(object sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (!string.IsNullOrEmpty(_path) && File.Exists(_path)) PlatformInfo.Open(_path);
        Close();
        e.Handled = true;
    }
}
