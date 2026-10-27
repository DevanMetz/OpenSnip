using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Shapes = Avalonia.Controls.Shapes;

namespace OpenSnip;

sealed class ThumbnailWindow : Window
{
    readonly string _path;
    readonly DispatcherTimer _timer;

    public ThumbnailWindow(CapturedFrame frame, string savedPath, int seconds, bool play = false)
    {
        _path = savedPath;
        Title = "OpenSnip";
        Icon = IconLoader.Icon;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
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
        var picture = new Grid();
        picture.Children.Add(image);
        if (play) picture.Children.Add(PlayBadge());
        Content = new Border
        {
            Background = AppTheme.Panel,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = picture,
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

    static Control PlayBadge()
    {
        var points = new Points();
        points.Add(new Point(0, 0));
        points.Add(new Point(0, 18));
        points.Add(new Point(16, 9));
        return new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(23),
            Background = new SolidColorBrush(Color.FromArgb(176, 20, 20, 24)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new Shapes.Polygon
            {
                Points = points,
                Fill = Brushes.White,
                Width = 16,
                Height = 18,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
            },
        };
    }

    void PlaceBottomRight()
    {
        var screen = Screens.Primary
                     ?? Screens.All.FirstOrDefault(candidate => candidate.IsPrimary)
                     ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var area = screen.WorkingArea;
        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        Position = new PixelPoint(
            (int)(area.Right - Width * scale - 16 * scale),
            (int)(area.Bottom - Height * scale - 16 * scale));
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
