using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Murmuration.Browser;

public class App : Application
{
    public static volatile bool Paused;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var view = new Grid { Background = Brushes.Black, Focusable = true };
            view.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Space) Paused = !Paused;
            };
            view.AttachedToVisualTree += (_, _) => view.Focus();
            singleView.MainView = view;
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class Program
{
    private static async Task Main()
    {
        await AppBuilder.Configure<App>().StartBrowserAppAsync("out");

        var renderer = await WebGpuBrowserRenderer.CreateAsync("gpu");
        var runner = new FrameRunner(new Simulation(count: 100), renderer);

        void OnFrame(double ms)
        {
            runner.Paused = App.Paused;
            var (width, height) = renderer.SyncSize();
            runner.Frame(ms / 1000.0, width, height);
            WebGpuBrowserRenderer.RequestFrame(OnFrame);
        }
        WebGpuBrowserRenderer.RequestFrame(OnFrame);
    }
}
