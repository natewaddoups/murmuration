using Microsoft.Win32;

namespace Murmuration.Desktop;

internal static class ScreenSaverSettings
{
    public const int DefaultBirdCount = 100;
    public const int MinBirdCount = 10;
    public const int MaxBirdCount = 1500;

    private const string KeyPath = @"Software\Murmuration";
    private const string ValueName = "InitialBirdCount";

    public static int Normalize(int? count) =>
        count is >= MinBirdCount and <= MaxBirdCount ? count.Value : DefaultBirdCount;

    public static int Load()
    {
        if (!OperatingSystem.IsWindows())
            return DefaultBirdCount;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return Normalize(key?.GetValue(ValueName) as int?);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return DefaultBirdCount;
        }
    }

    public static void Save(int count)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, Math.Clamp(count, MinBirdCount, MaxBirdCount), RegistryValueKind.DWord);
    }
}
