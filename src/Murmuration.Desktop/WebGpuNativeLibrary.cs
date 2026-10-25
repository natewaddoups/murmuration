using System.Runtime.InteropServices;

namespace Murmuration.Desktop;

internal static class WebGpuNativeLibrary
{
    private const string LibraryFileName = "wgpu_native.dll";

    public static IntPtr Load()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, LibraryFileName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", LibraryFileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return NativeLibrary.Load(candidate);
        }

        string processName = Path.GetFileName(Environment.ProcessPath ?? "Murmuration.scr");
        string extractionBase = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR")
            ?? Path.Combine(Path.GetTempPath(), ".net");
        string applicationExtractionDirectory = Path.Combine(extractionBase, processName);

        if (Directory.Exists(applicationExtractionDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(applicationExtractionDirectory)
                .OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                string candidate = Path.Combine(directory, LibraryFileName);
                if (File.Exists(candidate))
                    return NativeLibrary.Load(candidate);
            }
        }

        throw new DllNotFoundException(
            $"Could not find {LibraryFileName} beside the application or in the .NET single-file extraction directory '{applicationExtractionDirectory}'.");
    }
}