using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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
            if (Program.Options.Mode == StartupMode.ScreenSaver)
            {
                ScreenSaverWindowCoordinator.Start(desktop);
            }
            else
            {
                var simulation = new Simulation(count: 100);
                var host = new GpuHost(simulation);
                var window = new Window { Title = "Murmuration", Width = 1280, Height = 720, WindowState = WindowState.Maximized, Content = host };
                host.FpsUpdated += fps => Dispatcher.UIThread.Post(() => window.Title = $"Murmuration - {simulation.Birds.Length} birds - {fps:F0} FPS");
                host.RenderFailed += exception => Dispatcher.UIThread.Post(() =>
                    ScreenSaverPlatform.ShowMessage(IntPtr.Zero, exception.Message, "Murmuration render error", error: true));
                window.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Space) host.TogglePause();
                };
                desktop.MainWindow = window;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    internal static StartupOptions Options { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (!ScreenSaverArguments.TryParse(args, out var options, out string? error))
        {
            ScreenSaverPlatform.ShowMessage(IntPtr.Zero, error!, "Murmuration", error: true);
            return;
        }

        Options = options;
        if (options.Mode == StartupMode.Preview)
        {
            ScreenSaverPlatform.RunPreview(options.ParentWindow);
            return;
        }

        if (options.Mode == StartupMode.Configure)
        {
            ScreenSaverPlatform.ShowMessage(options.ParentWindow, "Murmuration has no configurable options.", "Murmuration Screen Saver");
            return;
        }

        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
}
