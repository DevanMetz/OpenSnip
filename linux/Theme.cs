using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace OpenSnip;

static class AppTheme
{
    public static readonly Color BackColor = Color.Parse("#1C1C20");
    public static readonly Color PanelColor = Color.Parse("#26262C");
    public static readonly Color TextColor = Color.Parse("#EBEBF0");
    public static readonly Color MutedColor = Color.Parse("#9696A0");
    public static readonly Color AccentColor = Color.Parse("#7C5CFF");
    public static readonly Color AccentHoverColor = Color.Parse("#8D73FF");
    public static readonly Color BorderColor = Color.Parse("#3A3A46");
    public static readonly Color NavSelectedColor = Color.Parse("#483A8C");

    public static readonly IBrush Back = new SolidColorBrush(BackColor);
    public static readonly IBrush Panel = new SolidColorBrush(PanelColor);
    public static readonly IBrush Text = new SolidColorBrush(TextColor);
    public static readonly IBrush Muted = new SolidColorBrush(MutedColor);
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush Border = new SolidColorBrush(BorderColor);
    public static readonly IBrush NavSelected = new SolidColorBrush(NavSelectedColor);

    public static void Apply(Application app)
    {
        app.RequestedThemeVariant = ThemeVariant.Dark;
        app.Resources["SystemAccentColor"] = AccentColor;
        app.Resources["SystemAccentColorDark1"] = Color.Parse("#6347D9");
        app.Resources["SystemAccentColorDark2"] = Color.Parse("#4E37AD");
        app.Resources["SystemAccentColorDark3"] = Color.Parse("#392780");
        app.Resources["SystemAccentColorLight1"] = Color.Parse("#9479FF");
        app.Resources["SystemAccentColorLight2"] = Color.Parse("#AB96FF");
        app.Resources["SystemAccentColorLight3"] = Color.Parse("#C3B4FF");

        app.Styles.Add(new FluentTheme());

        app.Styles.Add(new Style(x => x.OfType<Window>())
        {
            Setters =
            {
                new Setter(Window.BackgroundProperty, Back),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.ForegroundProperty, Text),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, Panel),
                new Setter(Button.ForegroundProperty, Text),
                new Setter(Button.BorderBrushProperty, Border),
                new Setter(Button.BorderThicknessProperty, new Thickness(1)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(Button.PaddingProperty, new Thickness(12, 6)),
                new Setter(Button.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Center),
                new Setter(Button.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(CheckBox.ForegroundProperty, Text),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.BackgroundProperty, Panel),
                new Setter(TextBox.ForegroundProperty, Text),
                new Setter(TextBox.BorderBrushProperty, Border),
                new Setter(TextBox.SelectionBrushProperty, Accent),
                new Setter(TextBox.CaretBrushProperty, Text),
                new Setter(TextBox.PaddingProperty, new Thickness(9, 6)),
                new Setter(TextBox.CornerRadiusProperty, new CornerRadius(6)),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(ComboBox.BackgroundProperty, Panel),
                new Setter(ComboBox.ForegroundProperty, Text),
                new Setter(ComboBox.BorderBrushProperty, Border),
                new Setter(ComboBox.PaddingProperty, new Thickness(9, 5)),
                new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(6)),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<ComboBoxItem>())
        {
            Setters =
            {
                new Setter(ComboBoxItem.ForegroundProperty, Text),
            }
        });
        app.Styles.Add(new Style(x => x.OfType<ScrollViewer>())
        {
            Setters =
            {
                new Setter(ScrollViewer.BackgroundProperty, Brushes.Transparent),
            }
        });
    }
}
