using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Murmuration.Desktop;

internal sealed class ConfigureWindow : Window
{
    public ConfigureWindow()
    {
        Title = "Murmuration Screen Saver Settings";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var count = new NumericUpDown
        {
            Minimum = ScreenSaverSettings.MinBirdCount,
            Maximum = ScreenSaverSettings.MaxBirdCount,
            Increment = 10,
            FormatString = "0",
            Value = ScreenSaverSettings.Load(),
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            ScreenSaverSettings.Save((int)(count.Value ?? ScreenSaverSettings.DefaultBirdCount));
            Close();
        };
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"Initial number of birds ({ScreenSaverSettings.MinBirdCount}-{ScreenSaverSettings.MaxBirdCount})" },
                count,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel },
                },
            },
        };
    }
}
