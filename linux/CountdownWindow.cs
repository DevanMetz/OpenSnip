using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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

        var screen = Screens.Primary
                     ?? Screens.All.FirstOrDefault(candidate => candidate.IsPrimary)
                     ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var area = screen.WorkingArea;
            double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
            Position = new PixelPoint(
                (int)(area.X + area.Width / 2.0 - Width * scale / 2),
                (int)(area.Y + area.Height / 2.0 - Height * scale / 2));
        }
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
            window.Close();
        }
        await Task.Delay(80);
    }
}
