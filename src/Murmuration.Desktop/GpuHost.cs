using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Murmuration.Desktop;

// Hosts a native child window and renders into it with WebGPU on a dedicated thread.
public sealed class GpuHost : NativeControlHost
{
    private readonly Simulation _simulation;
    private IntPtr _hwnd;
    private GpuRenderSession? _session;
    private volatile bool _paused;

    public GpuHost(Simulation simulation) => _simulation = simulation;

    public void TogglePause()
    {
        _paused = !_paused;
        if (_session is not null)
            _session.Paused = _paused;
    }

    // Raised on the render thread.
    public event Action<double>? FpsUpdated;
    public event Action<Exception>? RenderFailed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        // TODO: macOS (NSView) and Linux (X11/Wayland) surfaces.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native surface hosting is only implemented for Windows.");

        const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;
        _hwnd = CreateWindowExW(0, "STATIC", "", WS_CHILD | WS_VISIBLE, 0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Unable to create the GPU child window (Win32 error {Marshal.GetLastWin32Error()}).");

        _session = new GpuRenderSession(_hwnd, _simulation);
        _session.FpsUpdated += fps => FpsUpdated?.Invoke(fps);
        _session.Failed += exception => RenderFailed?.Invoke(exception);
        try
        {
            _session.Start();
        }
        catch
        {
            _session = null;
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            throw;
        }
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _session?.Stop();
        _session = null;
        DestroyWindow(control.Handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
