using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Murmuration.Desktop;

internal sealed class GpuRenderSession(IntPtr hwnd, Simulation simulation)
{
    private volatile bool _running;
    private Exception? _failure;
    private Thread? _thread;

    public volatile bool Paused;
    public Exception? Failure => Volatile.Read(ref _failure);
    public event Action<double>? FpsUpdated;
    public event Action<Exception>? Failed;

    public void Start()
    {
        _running = true;
        _thread = new Thread(RenderLoop) { IsBackground = true, Name = "Render" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join();
        _thread = null;
    }

    private void RenderLoop()
    {
        try
        {
            using var renderer = new WebGpuDesktopRenderer(hwnd);
            var runner = new FrameRunner(simulation, renderer);
            runner.FpsUpdated += fps => FpsUpdated?.Invoke(fps);
            var clock = Stopwatch.StartNew();

            while (_running)
            {
                runner.Paused = Paused;
                if (!GetClientRect(hwnd, out RECT rect) || rect.Right <= 0 || rect.Bottom <= 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                runner.Frame(clock.Elapsed.TotalSeconds, rect.Right, rect.Bottom);
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, exception);
            Failed?.Invoke(exception);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
}