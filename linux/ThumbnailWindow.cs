using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Shapes = Avalonia.Controls.Shapes;

namespace OpenSnip;

sealed class ThumbnailWindow : Window
{
    readonly PixelRect? _anchor;
    readonly CapturedFrame _frame;
    readonly DispatcherTimer _timer;
    string _path;
    Point _press;
    bool _pressed;
    bool _dragged;

    public ThumbnailWindow(CapturedFrame frame, string savedPath, int seconds, bool play = false, PixelRect? anchor = null)
    {
        _frame = frame;
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
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) =>
        {
            if (!_dragged) _timer.Start();
        };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
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
        _pressed = true;
        _dragged = false;
        _press = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    void OnMoved(object sender, PointerEventArgs e)
    {
        if (!_pressed || _dragged) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _press.X) < 6 && Math.Abs(point.Y - _press.Y) < 6) return;
        _pressed = false;
        _dragged = true;
        _timer.Stop();
        StartDrag(e);
    }

    void OnReleased(object sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        e.Pointer.Capture(null);
        if (!_pressed) return;
        _pressed = false;
        try { App.Instance?.OpenEditor(_frame); }
        catch (Exception ex) { Log.Error(ex); }
        Close();
        e.Handled = true;
    }

    void StartDrag(PointerEventArgs e)
    {
        try
        {
            string path = EnsureDragFile();
            var file = StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult();
            if (file == null)
            {
                AbandonDrag();
                return;
            }
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(file));
            _ = FinishDragAsync(e, data);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            AbandonDrag();
        }
    }

    void AbandonDrag()
    {
        _dragged = false;
        _timer.Start();
    }

    async Task FinishDragAsync(PointerEventArgs e, DataTransfer data)
    {
        try
        {
            var result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
            if (result != DragDropEffects.None) Close();
            else _timer.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _timer.Start();
        }
    }

    string EnsureDragFile()
    {
        if (!string.IsNullOrEmpty(_path) && File.Exists(_path)) return _path;
        string path = CapturedFrame.UniquePath(Path.Combine(Path.GetTempPath(), "OpenSnip"), "png");
        _frame.Save(path, "png");
        _path = path;
        return path;
    }
}
