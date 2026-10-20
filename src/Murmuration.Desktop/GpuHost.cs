using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Murmuration.Desktop;

// Hosts a native child window and renders into it with WebGPU on a dedicated thread.
public sealed class GpuHost : NativeControlHost
{
    private readonly Simulation _simulation;
    private IntPtr _hwnd;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _paused;

    public GpuHost(Simulation simulation) => _simulation = simulation;

    public void TogglePause() => _paused = !_paused;

    // Raised on the render thread.
    public event Action<double>? FpsUpdated;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        // TODO: macOS (NSView) and Linux (X11/Wayland) surfaces.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native surface hosting is only implemented for Windows.");

        const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;
        _hwnd = CreateWindowExW(0, "STATIC", "", WS_CHILD | WS_VISIBLE, 0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        _running = true;
        _thread = new Thread(RenderLoop) { IsBackground = true, Name = "Render" };
        _thread.Start();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _running = false;
        _thread?.Join();
        _thread = null;
        DestroyWindow(control.Handle);
    }

    private void RenderLoop()
    {
        using var renderer = new WebGpuDesktopRenderer(_hwnd);
        var runner = new FrameRunner(_simulation, renderer);
        runner.FpsUpdated += fps => FpsUpdated?.Invoke(fps);
        var clock = Stopwatch.StartNew();

        while (_running)
        {
            runner.Paused = _paused;
            GetClientRect(_hwnd, out RECT rect);
            runner.Frame(clock.Elapsed.TotalSeconds, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
