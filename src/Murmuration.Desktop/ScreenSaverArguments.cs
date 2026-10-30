using System.Globalization;

namespace Murmuration.Desktop;

internal enum StartupMode
{
    Interactive,
    ScreenSaver,
    Preview,
    Configure,
}

internal readonly record struct StartupOptions(StartupMode Mode, IntPtr ParentWindow = default);

internal static class ScreenSaverArguments
{
    public static bool TryParse(string[] args, out StartupOptions options, out string? error)
    {
        options = new StartupOptions(StartupMode.Interactive);
        error = null;

        if (args.Length == 0)
            return true;

        if (args.Length == 1 && string.Equals(args[0], "/s", StringComparison.OrdinalIgnoreCase))
        {
            options = new StartupOptions(StartupMode.ScreenSaver);
            return true;
        }

        if (args.Length == 1 && string.Equals(args[0], "/c", StringComparison.OrdinalIgnoreCase))
        {
            options = new StartupOptions(StartupMode.Configure);
            return true;
        }

        if (args.Length == 2 && string.Equals(args[0], "/p", StringComparison.OrdinalIgnoreCase))
            return TryParseHandle(args[1], StartupMode.Preview, out options, out error);

        if (args.Length == 1 && args[0].StartsWith("/p:", StringComparison.OrdinalIgnoreCase))
            return TryParseHandle(args[0][3..], StartupMode.Preview, out options, out error);

        if (args.Length == 1 && args[0].StartsWith("/c:", StringComparison.OrdinalIgnoreCase))
            return TryParseHandle(args[0][3..], StartupMode.Configure, out options, out error);

        error = "Expected no arguments, /s, /p <HWND>, /p:<HWND>, /c, or /c:<HWND>.";
        return false;
    }

    private static bool TryParseHandle(string value, StartupMode mode, out StartupOptions options, out string? error)
    {
        options = default;
        error = null;

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long handleValue) ||
            handleValue == 0 ||
            (IntPtr.Size == sizeof(int) && handleValue > int.MaxValue))
        {
            error = "The parent window handle must be a non-zero decimal integer.";
            return false;
        }

        options = new StartupOptions(mode, new IntPtr(handleValue));
        return true;
    }
}