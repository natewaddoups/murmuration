using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Murmuration.Desktop;

public class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = new GpuHost(new Simulation(count: 600));
            var window = new Window { Title = "Murmuration", Width = 1280, Height = 720, Content = host };
            host.FpsUpdated += fps => Dispatcher.UIThread.Post(() => window.Title = $"Murmuration - {fps:F0} FPS");
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
