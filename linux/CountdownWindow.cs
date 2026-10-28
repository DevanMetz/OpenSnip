using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenSnip;

sealed class CountdownWindow : Window
{
    readonly TextBlock _number = new()
    {
        FontSize = 56,
        FontWeight = FontWeight.SemiBold,
        Foreground = AppTheme.Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public CountdownWindow(int seconds)
    {
        Title = "OpenSnip";
        Icon = IconLoader.Icon;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Width = 128;
        Height = 128;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = AppTheme.Back;
        Content = new Border
        {
            BorderBrush = AppTheme.Accent,
            BorderThickness = new Thickness(2),
            Background = AppTheme.Panel,
            Child = _number,
        };
        _number.Text = seconds.ToString();
        Place();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Place();
        PassClicksThrough();
        // Avalonia may map the window after OnOpened. Apply the shape again once that settles.
        Dispatcher.UIThread.Post(PassClicksThrough, DispatcherPriority.Background);
    }

    void Place()
    {
        var screen = ScreenUnderPointer();
        if (screen == null) return;
        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        int width = (int)Math.Round(Width * scale);
        int height = (int)Math.Round(Height * scale);
        int margin = (int)Math.Round(40 * scale);
        Position = SnipGeometry.BottomCenter(screen.WorkingArea, width, height, margin);
    }

    Avalonia.Platform.Screen ScreenUnderPointer()
    {
        var screens = Screens?.All;
        if (screens == null || screens.Count == 0) return null;
        if (X11Cursor.TryGet() is { } pointer)
        {
            var hit = Screens.ScreenFromPoint(new PixelPoint(pointer.X, pointer.Y));
            if (hit != null) return hit;
        }
        return Screens.Primary
               ?? screens.FirstOrDefault(candidate => candidate.IsPrimary)
               ?? screens[0];
    }

    void PassClicksThrough()
    {
        if (TryGetPlatformHandle() is { Handle: var xid } && xid != IntPtr.Zero)
            X11ClickThrough.TryEnable(xid);
    }

    public static async Task RunAsync(int seconds)
    {
        if (seconds <= 0) return;
        var window = new CountdownWindow(seconds);
        window.Show();
        try
        {
            for (int left = seconds; left >= 1; left--)
            {
                window._number.Text = left.ToString();
                await Task.Delay(1000);
            }
        }
        finally
        {
            // Hide first so the compositor can drop the window before the screen is captured.
            try { window.Hide(); } catch { }
            await Task.Delay(SnipTiming.CountdownClearMs);
            try { window.Close(); } catch { }
        }
    }
}
