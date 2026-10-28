using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace OpenSnip;

sealed class PinWindow : Window
{
    readonly CapturedFrame _frame;
    readonly Settings _settings;
    readonly Image _image;
    readonly Border _chrome;
    readonly Bitmap _bitmap;

    readonly PixelPoint? _cursor;
    PinRect _rect;
    double _zoom = 1;
    Drag _drag = Drag.None;
    PinEdge _edge;
    PixelPoint _pressScreen;
    PinRect _pressRect;
    int _grabX, _grabY;

    enum Drag { None, Move, Resize }

    public PinWindow(CapturedFrame frame, Settings settings, PixelPoint? cursor)
    {
        _frame = frame;
        _settings = settings;
        _bitmap = frame.ToBitmap();
        _image = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.Fill,
        };
        _chrome = new Border
        {
            Background = AppTheme.Accent,
            BorderBrush = AppTheme.Accent,
            BorderThickness = new Thickness(PinLayout.Border),
            Child = _image,
        };

        _cursor = cursor;
        Title = "OpenSnip pin";
        Icon = IconLoader.Icon;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        Content = _chrome;
        ContextMenu = BuildMenu();
        Apply(new PinRect(0, 0, frame.Width + PinLayout.Border * 2, frame.Height + PinLayout.Border * 2), 1);

        Opened += (_, _) =>
        {
            Place();
            Activate();
            Focus();
        };
        Closed += (_, _) => _bitmap.Dispose();
    }

    void Place()
    {
        if (_drag != Drag.None) return;
        var area = WorkArea(_cursor);
        var rect = _cursor is { } point
            ? PinLayout.AtCursor(point.X, point.Y, _frame.Width, _frame.Height)
            : PinLayout.Centered(area.X, area.Y, area.Width, area.Height, _frame.Width, _frame.Height);
        Apply(rect, _zoom);
    }

    PixelRect WorkArea(PixelPoint? cursor)
    {
        Screen screen = null;
        if (cursor is { } point) screen = Screens?.ScreenFromPoint(point);
        screen ??= Screens?.Primary ?? Screens?.All.FirstOrDefault();
        if (screen != null) return screen.WorkingArea;
        return new PixelRect(0, 0, Math.Max(1, _frame.Width + 2), Math.Max(1, _frame.Height + 2));
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Background = AppTheme.Panel };
        menu.Items.Add(Action("Copy", () => _ = CopyAsync()));
        menu.Items.Add(Action("Save as…", () => _ = SaveAsync()));
        menu.Items.Add(Action("Actual size", ActualSize));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("Close", Close));
        return menu;
    }

    static MenuItem Action(string header, Action action)
    {
        var item = new MenuItem { Header = header, Foreground = AppTheme.Text };
        item.Click += (_, _) => action();
        return item;
    }

    void Apply(PinRect rect, double zoom)
    {
        _rect = rect;
        _zoom = zoom;
        double scale = CurrentScale();
        Width = Math.Max(1, rect.Width / scale);
        Height = Math.Max(1, rect.Height / scale);
        Position = new PixelPoint(rect.X, rect.Y);
        _chrome.BorderThickness = new Thickness(PinLayout.Border / scale);
        RenderOptions.SetBitmapInterpolationMode(_image,
            zoom < 1 ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None);
    }

    double CurrentScale()
    {
        var origin = new PixelPoint(_rect.Width == 0 ? Position.X : _rect.X, _rect.Height == 0 ? Position.Y : _rect.Y);
        var screen = Screens?.ScreenFromPoint(origin) ?? Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        double scale = screen?.Scaling ?? 1;
        return scale <= 0 ? 1 : scale;
    }

    PixelPoint PointerScreen(PointerEventArgs e)
    {
        var local = e.GetPosition(this);
        double scale = CurrentScale();
        return new PixelPoint(
            Position.X + (int)Math.Round(local.X * scale),
            Position.Y + (int)Math.Round(local.Y * scale));
    }

    PinEdge Hit(PointerEventArgs e)
    {
        var local = e.GetPosition(this);
        double scale = CurrentScale();
        return PinLayout.HitTest(local.X * scale, local.Y * scale, Math.Max(1, _rect.Width), Math.Max(1, _rect.Height), scale);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed) return;
        Focus();

        if (point.Properties.IsMiddleButtonPressed)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount >= 2)
        {
            Close();
            e.Handled = true;
            return;
        }

        _pressScreen = PointerScreen(e);
        _pressRect = _rect;
        _edge = Hit(e);
        if (_edge == PinEdge.None)
        {
            _drag = Drag.Move;
            _grabX = _pressScreen.X - _rect.X;
            _grabY = _pressScreen.Y - _rect.Y;
        }
        else
        {
            _drag = Drag.Resize;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == Drag.None)
        {
            Cursor = CursorFor(Hit(e));
            return;
        }

        var now = PointerScreen(e);
        if (_drag == Drag.Move)
        {
            Apply(new PinRect(now.X - _grabX, now.Y - _grabY, _rect.Width, _rect.Height), _zoom);
            return;
        }

        bool keep = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var next = PinLayout.Resize(_pressRect, _edge, now.X - _pressScreen.X, now.Y - _pressScreen.Y,
            _frame.Width / (double)Math.Max(1, _frame.Height), keep);
        double zoom = (next.Width - PinLayout.Border * 2) / (double)Math.Max(1, _frame.Width);
        Apply(next, zoom);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _drag = Drag.None;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        bool up = e.Delta.Y > 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Opacity = PinLayout.StepOpacity(Opacity, up);
        }
        else
        {
            var local = e.GetPosition(this);
            double scale = CurrentScale();
            double zoom = PinLayout.StepZoom(_zoom, up);
            var next = PinLayout.ZoomAt(_rect, zoom, local.X * scale, local.Y * scale, _frame.Width, _frame.Height);
            Apply(next, zoom);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool other = e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            || e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.Escape && !ctrl && !other)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (ctrl && !other && e.Key == Key.C)
        {
            _ = CopyAsync();
            e.Handled = true;
            return;
        }
        if (ctrl && !other && e.Key == Key.S)
        {
            _ = SaveAsync();
            e.Handled = true;
            return;
        }
        if (!ctrl && !other && e.Key is Key.D1 or Key.NumPad1)
        {
            ActualSize();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    void ActualSize()
    {
        var next = PinLayout.ZoomAt(_rect, 1, _rect.Width / 2.0, _rect.Height / 2.0, _frame.Width, _frame.Height);
        Apply(next, 1);
    }

    async Task CopyAsync()
    {
        try
        {
            var png = await Task.Run(() => _frame.EncodePng());
            bool copied = await ClipboardService.CopyPngAsync(png, this);
            if (!copied) await MessageDialog.AlertAsync(this, "Could not copy the image.");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            try { await MessageDialog.AlertAsync(this, "Could not copy the image."); } catch { }
        }
    }

    async Task SaveAsync()
    {
        try
        {
            var options = new FilePickerSaveOptions
            {
                SuggestedFileName = $"Snip {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png",
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG image") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                },
            };
            if (!string.IsNullOrEmpty(_settings.SaveFolder) && Directory.Exists(_settings.SaveFolder))
            {
                var folder = await StorageProvider.TryGetFolderFromPathAsync(_settings.SaveFolder);
                if (folder != null) options.SuggestedStartLocation = folder;
            }

            var file = await StorageProvider.SaveFilePickerAsync(options);
            if (file == null) return;
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                await MessageDialog.AlertAsync(this, "Could not save the image.");
                return;
            }

            string ext = Path.GetExtension(path);
            bool jpeg = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            await Task.Run(() => _frame.Save(path, jpeg ? "jpg" : "png"));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            try { await MessageDialog.AlertAsync(this, "Could not save the image: " + ex.Message); } catch { }
        }
    }

    static Cursor CursorFor(PinEdge edge) => edge switch
    {
        PinEdge.Left or PinEdge.Right => SizeWestEast,
        PinEdge.Top or PinEdge.Bottom => SizeNorthSouth,
        PinEdge.Top | PinEdge.Left => TopLeft,
        PinEdge.Bottom | PinEdge.Right => TopLeft,
        PinEdge.Top | PinEdge.Right => TopRight,
        PinEdge.Bottom | PinEdge.Left => TopRight,
        _ => Arrow,
    };

    static readonly Cursor Arrow = new(StandardCursorType.Arrow);
    static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
    static readonly Cursor SizeNorthSouth = new(StandardCursorType.SizeNorthSouth);
    static readonly Cursor TopLeft = new(StandardCursorType.TopLeftCorner);
    static readonly Cursor TopRight = new(StandardCursorType.TopRightCorner);
}
