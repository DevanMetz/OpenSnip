using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenSnip;

static class MessageDialog
{
    public static async Task AlertAsync(Window owner, string message)
    {
        await ShowAsync(owner, message, confirm: false);
    }

    public static async Task<bool> ConfirmAsync(Window owner, string message, string accept = "Reset")
    {
        return await ShowAsync(owner, message, confirm: true, accept);
    }

    static async Task<bool> ShowAsync(Window owner, string message, bool confirm, string accept = "OK")
    {
        var result = false;
        var window = new Window
        {
            Title = "OpenSnip",
            Icon = IconLoader.Icon,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = owner == null,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = AppTheme.Back,
            Topmost = owner == null,
        };
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.Text,
            FontSize = 14,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
        };
        if (confirm)
        {
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = confirm ? accept : "OK" };
        ok.Click += (_, _) => { result = true; window.Close(); };
        buttons.Children.Add(ok);

        var content = new StackPanel { Margin = new Thickness(22), Spacing = 0 };
        content.Children.Add(text);
        content.Children.Add(buttons);
        window.Content = content;

        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await closed.Task;
        }
        return result;
    }
}
