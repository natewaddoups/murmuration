using Microsoft.UI.Xaml;

namespace Murmuration;

public sealed partial class MainWindow : Window
{
    private readonly Renderer _renderer;

    public MainWindow()
    {
        InitializeComponent();
        _renderer = new Renderer(Panel);
        Closed += (_, _) => _renderer.Dispose();
    }
}
