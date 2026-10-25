using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Murmuration.Desktop;

internal static class ScreenSaverWindowCoordinator
{
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dismiss = new DismissController(desktop);
        var first = CreateWindow(new Simulation(count: 100, seed: 1), dismiss, desktop);
        first.Opacity = 0;
        first.Opened += (_, _) =>
        {
            Screen[] screens = first.Screens?.All.ToArray() ?? [];
            if (screens.Length == 0)
                screens = first.Screens?.Primary is { } primary ? [primary] : [];

            if (screens.Length == 0)
            {
                first.WindowState = WindowState.Maximized;
                first.Opacity = 1;
                dismiss.Start();
                return;
            }

            for (int i = 0; i < screens.Length; i++)
            {
                Window window = i == 0
                    ? first
                    : CreateWindow(new Simulation(count: 100, seed: i + 1), dismiss, desktop);
                PlaceOnScreen(window, screens[i]);
                if (i > 0)
                    window.Show();
            }

            first.Opacity = 1;
            dismiss.Start();
        };

        desktop.MainWindow = first;
    }

    private static Window CreateWindow(Simulation simulation, DismissController dismiss, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var host = new GpuHost(simulation);
        var window = new Window
        {
            Title = "Murmuration",
            Width = 1280,
            Height = 720,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = host,
        };

        host.RenderFailed += exception => Dispatcher.UIThread.Post(() =>
        {
            ScreenSaverPlatform.ShowMessage(IntPtr.Zero, exception.Message, "Murmuration render error", error: true);
            dismiss.Close();
        });
        return window;
    }

    private static void PlaceOnScreen(Window window, Screen screen)
    {
        var bounds = screen.Bounds;
        window.Position = new PixelPoint(bounds.X, bounds.Y);
        window.Width = bounds.Width / screen.Scaling;
        window.Height = bounds.Height / screen.Scaling;
    }

    private sealed class DismissController(IClassicDesktopStyleApplicationLifetime desktop)
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly bool[] _keyWasDown = new bool[256];
        private POINT _initialPosition;
        private bool _trackingPointer;
        private bool _closing;

        public void Start()
        {
            _clock.Restart();
            GetCursorPos(out _initialPosition);
            for (int key = 0; key < _keyWasDown.Length; key++)
                _keyWasDown[key] = IsKeyDown(key);
            _timer.Tick += (_, _) => CheckPointerMovement();
            _timer.Start();
        }

        public void Close()
        {
            if (_closing)
                return;

            _closing = true;
            _timer.Stop();
            desktop.Shutdown();
        }

        private void CheckPointerMovement()
        {
            bool inputPressed = CheckForNewKeyPress();
            if (_clock.Elapsed < TimeSpan.FromMilliseconds(750))
                return;

            if (!_trackingPointer)
            {
                if (GetCursorPos(out _initialPosition))
                    _trackingPointer = true;
                if (inputPressed)
                    Close();
                return;
            }

            if (inputPressed)
            {
                Close();
                return;
            }

            if (!GetCursorPos(out POINT current))
                return;
            if (PointerDismissPolicy.ShouldDismiss(_initialPosition.X, _initialPosition.Y, current.X, current.Y, _clock.Elapsed))
                Close();
        }

        private bool CheckForNewKeyPress()
        {
            bool pressed = false;
            for (int key = 0; key < _keyWasDown.Length; key++)
            {
                bool isDown = IsKeyDown(key);
                pressed |= isDown && !_keyWasDown[key];
                _keyWasDown[key] = isDown;
            }
            return pressed;
        }

        private static bool IsKeyDown(int key) => GetAsyncKeyState(key) < 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern short GetAsyncKeyState(int virtualKey);
    }
}

internal static class PointerDismissPolicy
{
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMilliseconds(750);
    private const int MovementThresholdSquared = 25;

    public static bool ShouldDismiss(int startX, int startY, int currentX, int currentY, TimeSpan elapsed)
    {
        if (elapsed < StartupGracePeriod)
            return false;

        long deltaX = currentX - startX;
        long deltaY = currentY - startY;
        return deltaX * deltaX + deltaY * deltaY >= MovementThresholdSquared;
    }
}