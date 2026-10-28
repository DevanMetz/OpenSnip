using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;

namespace OpenSnip;

sealed class EditorWindow : Window
{
    static readonly SKColor[] Swatches =
    {
        new(235, 64, 52),
        new(255, 214, 10),
        new(52, 199, 89),
        new(10, 132, 255),
        new(255, 255, 255),
        new(20, 20, 20),
    };

    static readonly FontFamily EditorFont = new("DejaVu Sans, Liberation Sans, Noto Sans");
    static readonly IBrush LightPaper = new SolidColorBrush(Color.FromRgb(250, 250, 250));
    static readonly IBrush DarkPaper = new SolidColorBrush(Color.FromRgb(40, 40, 46));

    readonly Settings _settings;
    readonly AnnotationDocument _doc;
    readonly AnnotateCanvas _canvas;
    readonly Dictionary<AnnotateTool, SKColor> _colors = new()
    {
        [AnnotateTool.Highlight] = Swatches[1],
    };
    readonly Dictionary<AnnotateTool, Button> _toolButtons = new();
    readonly List<(SKColor Color, Border Dot)> _swatches = new();
    readonly DispatcherTimer _copyTimer;
    readonly Button _undo;
    readonly Button _redo;
    readonly Button _copy;

    AnnotateTool _tool = AnnotateTool.Arrow;
    Annotation _current;
    SKPoint _start;
    bool _drawing;
    float _scale = 1;
    TextBox _textBox;
    SKPoint _textOrigin;
    float _textSize;
    SKColor _textColor;
    bool _droppingText;
    bool _inCopy;
    DispatcherTimer _copiedTimer;

    SKColor CurrentColor => _colors.TryGetValue(_tool, out var color) ? color : Swatches[0];

    public EditorWindow(CapturedFrame frame, Settings settings)
    {
        _settings = settings;
        _doc = new AnnotationDocument(frame);
        _canvas = new AnnotateCanvas(this, frame.Width, frame.Height);

        Title = $"OpenSnip — {frame.Width} × {frame.Height}";
        Icon = IconLoader.Icon;
        Background = AppTheme.Back;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 480;
        MinHeight = 320;
        Width = Math.Clamp(frame.Width + 64, 720, 1200);
        Height = Math.Clamp(frame.Height + 168, 480, 900);

        _undo = ActionButton("Undo", (_, _) => Undo());
        _redo = ActionButton("Redo", (_, _) => Redo());
        _copy = ActionButton("Copy", (_, _) => _ = CopyAsync(reportFailure: true));
        _copy.MinWidth = 76;
        var save = ActionButton("Save", (_, _) => _ = SaveAsync());

        var tools = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        tools.Children.Add(ToolButton("Arrow", AnnotateTool.Arrow));
        tools.Children.Add(ToolButton("Box", AnnotateTool.Box));
        tools.Children.Add(ToolButton("Pen", AnnotateTool.Pen));
        tools.Children.Add(ToolButton("Highlight", AnnotateTool.Highlight));
        tools.Children.Add(ToolButton("Text", AnnotateTool.Text));
        tools.Children.Add(ToolButton("Redact", AnnotateTool.Blur));
        tools.Children.Add(_undo);
        tools.Children.Add(_redo);

        var colors = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        for (int i = 0; i < Swatches.Length; i++)
            colors.Children.Add(Swatch(Swatches[i]));
        colors.Children.Add(_copy);
        colors.Children.Add(save);

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(tools);
        stack.Children.Add(colors);
        var bar = new Border
        {
            Background = AppTheme.Panel,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 2),
            Child = stack,
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(bar);
        Grid.SetRow(_canvas, 1);
        root.Children.Add(_canvas);
        Content = root;

        _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _copyTimer.Tick += (_, _) => _ = CopyAsync(reportFailure: false);

        AddHandler(KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) =>
        {
            if (_textBox != null) CommitText();
        };
        Closing += (_, _) =>
        {
            if (_textBox != null) CommitText();
            if (_copyTimer.IsEnabled) _ = CopyAsync(reportFailure: false);
        };
        Closed += (_, _) => _copyTimer.Stop();

        SetTool(AnnotateTool.Arrow);
        _canvas.ShowFrame(_doc.Render());
        UpdateChrome();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        double scaling = Screens?.Primary?.Scaling ?? 0;
        if (scaling <= 0) scaling = Screens?.ScreenFromVisual(this)?.Scaling ?? 1;
        _scale = (float)Math.Max(1, scaling);

        var screen = Screens?.Primary;
        if (screen != null && screen.Scaling > 0)
        {
            double maxW = screen.WorkingArea.Width / screen.Scaling * 0.94;
            double maxH = screen.WorkingArea.Height / screen.Scaling * 0.90;
            if (Width > maxW) Width = Math.Max(MinWidth, maxW);
            if (Height > maxH) Height = Math.Max(MinHeight, maxH);
        }
        _canvas.Focus();
    }

    Button ToolButton(string label, AnnotateTool tool)
    {
        var button = ActionButton(label, (_, _) => SetTool(tool));
        _toolButtons[tool] = button;
        return button;
    }

    static Button ActionButton(string label, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(10, 4),
        };
        button.Click += click;
        return button;
    }

    Border Swatch(SKColor color)
    {
        var dot = new Border
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 6, 8, 6),
            CornerRadius = new CornerRadius(9),
            Background = Brush(color),
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        dot.PointerPressed += (_, e) =>
        {
            if (_tool == AnnotateTool.Blur) return;
            if (!e.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
            SetColor(color);
            e.Handled = true;
        };
        _swatches.Add((color, dot));
        return dot;
    }

    void SetTool(AnnotateTool tool)
    {
        if (_textBox != null) CommitText();
        _tool = tool;
        _canvas.Cursor = new Cursor(tool == AnnotateTool.Text ? StandardCursorType.Ibeam : StandardCursorType.Cross);
        UpdateChrome();
    }

    void SetColor(SKColor color)
    {
        _colors[_tool] = color;
        _textColor = color;
        if (_textBox != null)
        {
            _textBox.Foreground = Brush(color);
            _textBox.CaretBrush = Brush(color);
            _textBox.Background = AnnotateMath.IsLight(color) ? DarkPaper : LightPaper;
        }
        UpdateChrome();
    }

    void UpdateChrome()
    {
        foreach (var (tool, button) in _toolButtons)
            button.Background = tool == _tool ? AppTheme.NavSelected : AppTheme.Panel;
        bool blur = _tool == AnnotateTool.Blur;
        var current = CurrentColor;
        foreach (var (color, dot) in _swatches)
        {
            bool selected = !blur && color == current;
            dot.BorderBrush = selected ? AppTheme.Accent : AppTheme.Border;
            dot.Opacity = blur ? 0.35 : 1;
        }
        _undo.Opacity = _doc.CanUndo ? 1 : 0.4;
        _redo.Opacity = _doc.CanRedo ? 1 : 0.4;
    }

    void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (_textBox != null) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (ctrl && !alt && !meta && e.Key == Key.Z)
        {
            if (shift) Redo(); else Undo();
            e.Handled = true;
            return;
        }
        if (ctrl && !shift && !alt && !meta && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
            return;
        }
        if (ctrl && !shift && !alt && !meta && e.Key == Key.C)
        {
            _ = CopyAsync(reportFailure: true);
            e.Handled = true;
            return;
        }
        if (ctrl && !shift && !alt && !meta && e.Key == Key.S)
        {
            _ = SaveAsync();
            e.Handled = true;
            return;
        }
        if (ctrl || shift || alt || meta) return;

        switch (e.Key)
        {
            case Key.Escape:
                if (_drawing) CancelShape();
                else Close();
                e.Handled = true;
                return;
            case Key.A: SetTool(AnnotateTool.Arrow); e.Handled = true; return;
            case Key.R: SetTool(AnnotateTool.Box); e.Handled = true; return;
            case Key.D: SetTool(AnnotateTool.Pen); e.Handled = true; return;
            case Key.H: SetTool(AnnotateTool.Highlight); e.Handled = true; return;
            case Key.T: SetTool(AnnotateTool.Text); e.Handled = true; return;
            case Key.B: SetTool(AnnotateTool.Blur); e.Handled = true; return;
        }

        if (_tool == AnnotateTool.Blur) return;
        int index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            _ => -1,
        };
        if (index >= 0)
        {
            SetColor(Swatches[index]);
            e.Handled = true;
        }
    }

    void CanvasPressed(Point point, bool left, bool right)
    {
        // Commit before moving focus. Focusing the canvas would otherwise commit
        // through LostFocus and then this same click would start another shape.
        if (_textBox != null)
        {
            CommitText();
            _canvas.Focus();
            return;
        }
        _canvas.Focus();
        if (right)
        {
            if (_drawing) CancelShape();
            return;
        }
        if (!left) return;

        var image = _canvas.ToImage(point);
        if (_tool == AnnotateTool.Text)
        {
            BeginText(point, image);
            return;
        }

        _start = image;
        _drawing = true;
        var color = CurrentColor;
        float k = _scale;
        _current = _tool switch
        {
            AnnotateTool.Pen => new StrokeAnnotation { Color = color, Width = 4f * k, Points = { image } },
            AnnotateTool.Highlight => new StrokeAnnotation { Highlighter = true, Color = color, Width = 18f * k, Points = { image } },
            AnnotateTool.Arrow => new ArrowAnnotation { A = image, B = image, Color = color, Width = 4.5f * k },
            AnnotateTool.Box => new BoxAnnotation { A = image, B = image, Color = color, Width = 3.5f * k },
            AnnotateTool.Blur => new BlurAnnotation { A = image, B = image, Width = 12f * k },
            _ => null,
        };
    }

    void CanvasMoved(Point point, bool shift)
    {
        if (!_drawing || _current == null) return;
        ApplyPoint(_canvas.ToImage(point), shift);
        _canvas.ShowFrame(_doc.Render(_current));
    }

    void CanvasReleased(Point point, bool shift)
    {
        if (!_drawing) return;
        if (_current != null) ApplyPoint(_canvas.ToImage(point), shift);
        _drawing = false;
        var shape = _current;
        _current = null;
        if (shape != null && !shape.IsEmpty)
        {
            _doc.Commit(shape);
            Present(scheduleCopy: true);
        }
        else Present(scheduleCopy: false);
    }

    void ApplyPoint(SKPoint point, bool shift)
    {
        switch (_current)
        {
            case StrokeAnnotation stroke:
                if (shift)
                {
                    if (stroke.Points.Count > 1)
                        stroke.Points.RemoveRange(1, stroke.Points.Count - 1);
                    stroke.Points.Add(AnnotateMath.Snap45(_start, point));
                }
                else stroke.Points.Add(point);
                break;
            case ArrowAnnotation arrow:
                arrow.B = shift ? AnnotateMath.Snap45(_start, point) : point;
                break;
            case TwoPointAnnotation two:
                two.B = shift ? AnnotateMath.Square(_start, point) : point;
                break;
        }
    }

    void CancelShape()
    {
        _drawing = false;
        _current = null;
        Present(scheduleCopy: false);
    }

    void Undo()
    {
        if (_textBox != null) { CancelText(); return; }
        if (_drawing) { CancelShape(); return; }
        if (_doc.Undo()) Present(scheduleCopy: true);
    }

    void Redo()
    {
        if (_textBox != null) CommitText();
        if (_drawing) CancelShape();
        if (_doc.Redo()) Present(scheduleCopy: true);
    }

    void Present(bool scheduleCopy)
    {
        _canvas.ShowFrame(_doc.Render(_current));
        UpdateChrome();
        if (scheduleCopy) ScheduleCopy();
    }

    void ScheduleCopy()
    {
        _copyTimer.Stop();
        _copyTimer.Start();
    }

    async Task CopyAsync(bool reportFailure)
    {
        if (_inCopy) return;
        _inCopy = true;
        try
        {
            _copyTimer.Stop();
            if (_textBox != null) CommitText();
            _copyTimer.Stop();
            var png = _doc.Render().EncodePng();
            bool ok = await ClipboardService.CopyPngAsync(png, this);
            if (ok)
            {
                if (reportFailure) FlashCopied();
            }
            else if (reportFailure)
                await MessageDialog.AlertAsync(this, "Could not copy the image.");
            else
                Log.Error(new InvalidOperationException("Could not copy the image."));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            if (reportFailure)
                await MessageDialog.AlertAsync(this, "Could not copy the image: " + ex.Message);
        }
        finally
        {
            _inCopy = false;
        }
    }

    void FlashCopied()
    {
        _copiedTimer?.Stop();
        _copy.Content = "Copied";
        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            _copy.Content = "Copy";
        };
        _copiedTimer.Start();
    }

    async Task SaveAsync()
    {
        if (_textBox != null) CommitText();
        try
        {
            IStorageFolder start = null;
            if (!string.IsNullOrWhiteSpace(_settings.SaveFolder) && Directory.Exists(_settings.SaveFolder))
                start = await StorageProvider.TryGetFolderFromPathAsync(_settings.SaveFolder);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save",
                SuggestedStartLocation = start,
                SuggestedFileName = $"Snip {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png",
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                },
            });
            if (file == null) return;
            string path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                await MessageDialog.AlertAsync(this, "Could not save the image.");
                return;
            }
            if (string.IsNullOrEmpty(Path.GetExtension(path))) path += ".png";
            string ext = Path.GetExtension(path);
            string format = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "jpg" : "png";
            _doc.Render().Save(path, format);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            await MessageDialog.AlertAsync(this, "Could not save the image: " + ex.Message);
        }
    }

    void BeginText(Point at, SKPoint image)
    {
        _textOrigin = image;
        _textSize = 22f * _scale;
        _textColor = CurrentColor;
        double zoom = _canvas.Zoom;
        var box = new TextBox
        {
            Text = "",
            FontFamily = EditorFont,
            FontWeight = FontWeight.Bold,
            FontSize = Math.Max(8, _textSize * zoom),
            Foreground = Brush(_textColor),
            CaretBrush = Brush(_textColor),
            Background = AnnotateMath.IsLight(_textColor) ? DarkPaper : LightPaper,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0),
            MinHeight = 0,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
        };
        void Fit()
        {
            string sample = string.IsNullOrEmpty(box.Text) ? " " : box.Text;
            var formatted = new FormattedText(
                sample + "M",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(EditorFont, FontStyle.Normal, FontWeight.Bold),
                box.FontSize,
                box.Foreground);
            box.Width = Math.Max(24, formatted.Width + 8);
            box.Height = Math.Max(box.FontSize + 6, formatted.Height + 4);
        }
        box.TextChanged += (_, _) => Fit();
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelText();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                CommitText();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            if (!_droppingText) CommitText();
        };
        _textBox = box;
        Fit();
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
        _canvas.Children.Add(box);
        box.Focus();
    }

    void CommitText()
    {
        var box = _textBox;
        if (box == null || _droppingText) return;
        _droppingText = true;
        _textBox = null;
        try
        {
            string text = box.Text ?? "";
            _canvas.Children.Remove(box);
            var shape = new TextAnnotation
            {
                P = _textOrigin,
                Text = text.TrimEnd(),
                Width = _textSize,
                Color = _textColor,
            };
            if (!shape.IsEmpty)
            {
                _doc.Commit(shape);
                Present(scheduleCopy: true);
            }
        }
        finally
        {
            _droppingText = false;
        }
    }

    void CancelText()
    {
        var box = _textBox;
        if (box == null) return;
        _droppingText = true;
        _textBox = null;
        _canvas.Children.Remove(box);
        _droppingText = false;
    }

    static SolidColorBrush Brush(SKColor color) => new(Color.FromRgb(color.Red, color.Green, color.Blue));

    sealed class AnnotateCanvas : Canvas
    {
        readonly EditorWindow _owner;
        readonly int _width;
        readonly int _height;
        readonly Image _image;
        readonly Border _frame;
        Bitmap _bitmap;

        public AnnotateCanvas(EditorWindow owner, int width, int height)
        {
            _owner = owner;
            _width = width;
            _height = height;
            Focusable = true;
            ClipToBounds = true;
            Background = AppTheme.Back;
            Cursor = new Cursor(StandardCursorType.Cross);
            _frame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            _image = new Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);
            Children.Add(_frame);
            Children.Add(_image);
            SizeChanged += (_, _) => LayoutImage();
        }

        public double Zoom
        {
            get
            {
                var rect = ImageRect();
                return rect.Width / Math.Max(1, _width);
            }
        }

        public void ShowFrame(CapturedFrame frame)
        {
            var bitmap = frame.ToBitmap();
            var previous = _bitmap;
            _bitmap = bitmap;
            _image.Source = bitmap;
            LayoutImage();
            previous?.Dispose();
        }

        public SKPoint ToImage(Point local)
        {
            var rect = ImageRect();
            double sx = _width / Math.Max(1, rect.Width);
            double sy = _height / Math.Max(1, rect.Height);
            return new SKPoint((float)((local.X - rect.X) * sx), (float)((local.Y - rect.Y) * sy));
        }

        Rect ImageRect()
        {
            const double pad = 16;
            double availW = Math.Max(1, Bounds.Width - pad * 2);
            double availH = Math.Max(1, Bounds.Height - pad * 2);
            double z = Math.Min(1, Math.Min(availW / Math.Max(1, _width), availH / Math.Max(1, _height)));
            z = Math.Max(z, 0.02);
            double w = Math.Max(1, Math.Round(_width * z));
            double h = Math.Max(1, Math.Round(_height * z));
            return new Rect(Math.Round((Bounds.Width - w) / 2), Math.Round((Bounds.Height - h) / 2), w, h);
        }

        void LayoutImage()
        {
            var rect = ImageRect();
            Canvas.SetLeft(_image, rect.X);
            Canvas.SetTop(_image, rect.Y);
            _image.Width = rect.Width;
            _image.Height = rect.Height;
            Canvas.SetLeft(_frame, rect.X - 1);
            Canvas.SetTop(_frame, rect.Y - 1);
            _frame.Width = rect.Width + 2;
            _frame.Height = rect.Height + 2;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!ReferenceEquals(e.Source, this)) return;
            var props = e.GetCurrentPoint(this).Properties;
            bool left = props.IsLeftButtonPressed;
            bool right = props.IsRightButtonPressed;
            _owner.CanvasPressed(e.GetPosition(this), left, right);
            if (left || right)
            {
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_owner._drawing) return;
            _owner.CanvasMoved(e.GetPosition(this), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                e.Pointer.Capture(null);
                return;
            }
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Pointer.Capture(null);
            _owner.CanvasReleased(e.GetPosition(this), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            base.OnDetachedFromVisualTree(e);
        }
    }
}
