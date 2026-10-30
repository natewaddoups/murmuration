using System.Runtime.InteropServices;

namespace Murmuration.Desktop;

internal static class ScreenSaverPlatform
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipsiblings = 0x04000000;
    private const uint WsClipchildren = 0x02000000;
    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;

    public static void ShowMessage(IntPtr owner, string message, string title, bool error = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(message);
            return;
        }

        MessageBoxW(owner, message, title, error ? 0x10u : 0x40u);
    }

    public static void RunPreview(IntPtr parent)
    {
        if (!OperatingSystem.IsWindows() || !IsWindow(parent))
        {
            ShowMessage(parent, "The screen-saver preview window is unavailable.", "Murmuration", error: true);
            return;
        }

        if (!GetClientRect(parent, out RECT parentRect) || parentRect.Right <= 0 || parentRect.Bottom <= 0)
            return;

        IntPtr child = CreateWindowExW(0, "STATIC", "", WsChild | WsVisible | WsClipsiblings | WsClipchildren,
            0, 0, parentRect.Right, parentRect.Bottom, parent, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (child == IntPtr.Zero)
        {
            ShowMessage(parent, $"Unable to create the preview surface (Win32 error {Marshal.GetLastWin32Error()}).", "Murmuration", error: true);
            return;
        }

        var session = new GpuRenderSession(child, new Simulation(count: 100, seed: 1));
        try
        {
            session.Start();
            int width = parentRect.Right;
            int height = parentRect.Bottom;
            while (IsWindow(parent) && IsWindow(child) && session.Failure is null)
            {
                if (!PumpMessages())
                    break;
                if (!GetClientRect(parent, out parentRect))
                    break;

                if (parentRect.Right != width || parentRect.Bottom != height)
                {
                    width = parentRect.Right;
                    height = parentRect.Bottom;
                    MoveWindow(child, 0, 0, width, height, true);
                }

                Thread.Sleep(20);
            }

            if (session.Failure is { } failure)
                ShowMessage(parent, failure.Message, "Murmuration preview error", error: true);
        }
        finally
        {
            session.Stop();
            if (IsWindow(child))
                DestroyWindow(child);
        }
    }

    private static bool PumpMessages()
    {
        while (PeekMessageW(out MSG message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.Message == WmQuit)
                return false;
            TranslateMessage(in message);
            DispatchMessageW(in message);
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Point;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessageW(out MSG message, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TranslateMessage(in MSG message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr DispatchMessageW(in MSG message);
}