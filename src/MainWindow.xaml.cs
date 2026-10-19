using Microsoft.UI.Xaml;

namespace Murmuration;

public sealed partial class MainWindow : Window
{
    private readonly Renderer _renderer;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        _renderer = new Renderer(Panel, new Simulation(count: 400));
        _renderer.FpsUpdated += OnFpsUpdated;
        Closed += (_, _) =>
        {
            _closed = true;
            _renderer.Dispose();
        };
    }

    private void OnFpsUpdated(double fps)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed) Title = $"Murmuration - {fps:F0} FPS";
        });
    }
}
