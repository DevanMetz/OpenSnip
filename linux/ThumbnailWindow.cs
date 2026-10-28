using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OpenSnip;

sealed class ThumbnailWindow : Window
{
    readonly CapturedFrame _frame;
    readonly DispatcherTimer _timer;
    string _path;
    Point _press;
    bool _pressed;
    bool _dragged;

    public ThumbnailWindow(CapturedFrame frame, string savedPath, int seconds)
    {
        _frame = frame;
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
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) =>
        {
            if (!_dragged) _timer.Start();
        };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
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
