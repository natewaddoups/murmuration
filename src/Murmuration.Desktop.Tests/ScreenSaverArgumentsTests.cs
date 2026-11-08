using Murmuration.Desktop;
using NUnit.Framework;
using Silk.NET.WebGPU;

namespace Murmuration.Desktop.Tests;

public sealed class ScreenSaverArgumentsTests
{
    [Test]
    public void EmptyArgumentsSelectInteractiveMode()
    {
        Assert.That(CommandLineArguments.TryParse([], out var options, out var error), Is.True, error);
        Assert.That(options.Mode, Is.EqualTo(StartupMode.Interactive));
        Assert.That(options.InitialBirdCount, Is.Null);
    }

    [TestCase("/count", "37")]
    [TestCase("/COUNT", "37")]
    [TestCase("/count:37", null)]
    public void CountSwitchParsesPositiveInitialBirdCount(string first, string? second)
    {
        string[] args = second is null ? [first] : [first, second];
        Assert.That(CommandLineArguments.TryParse(args, out var options, out var error), Is.True, error);
        Assert.That(options.Mode, Is.EqualTo(StartupMode.Interactive));
        Assert.That(options.InitialBirdCount, Is.EqualTo(37));
    }

    [TestCase("/count", "0")]
    [TestCase("/count", "-1")]
    [TestCase("/count", "invalid")]
    [TestCase("/count:invalid", null)]
    [TestCase("/count", null)]
    public void MissingOrInvalidCountLeavesInitialBirdCountUnset(string first, string? second)
    {
        string[] args = second is null ? [first] : [first, second];
        Assert.That(CommandLineArguments.TryParse(args, out var options, out var error), Is.True, error);
        Assert.That(options.InitialBirdCount, Is.Null);
    }

    [Test]
    public void CommandLineCountTakesPrecedenceOverEnvironment()
    {
        Assert.That(CommandLineArguments.ResolveInitialBirdCount(250, "80"), Is.EqualTo(250));
    }

    [Test]
    public void EnvironmentCountIsUsedWhenCommandLineCountIsMissingOrInvalid()
    {
        Assert.That(CommandLineArguments.ResolveInitialBirdCount(null, "80"), Is.EqualTo(80));
        Assert.That(CommandLineArguments.ResolveInitialBirdCount(null, "invalid"), Is.EqualTo(100));
    }

    [TestCase("/s")]
    [TestCase("/S")]
    public void SaverSwitchIsCaseInsensitive(string argument)
    {
        Assert.That(CommandLineArguments.TryParse([argument], out var options, out var error), Is.True, error);
        Assert.That(options.Mode, Is.EqualTo(StartupMode.ScreenSaver));
    }

    [TestCase("/p", "123")]
    [TestCase("/p:123", null)]
    public void PreviewSwitchParsesParentHandle(string first, string? second)
    {
        string[] args = second is null ? [first] : [first, second];
        Assert.That(CommandLineArguments.TryParse(args, out var options, out var error), Is.True, error);
        Assert.That(options.Mode, Is.EqualTo(StartupMode.Preview));
        Assert.That(options.ParentWindow, Is.EqualTo(new IntPtr(123)));
    }

    [TestCase("/c")]
    [TestCase("/c:123")]
    public void ConfigureSwitchSelectsInformationalMode(string argument)
    {
        Assert.That(CommandLineArguments.TryParse([argument], out var options, out var error), Is.True, error);
        Assert.That(options.Mode, Is.EqualTo(StartupMode.Configure));
    }

    [TestCase("/p")]
    [TestCase("/p:0")]
    [TestCase("/p:nope")]
    [TestCase("/unknown")]
    public void InvalidArgumentsAreRejected(string argument)
    {
        Assert.That(CommandLineArguments.TryParse([argument], out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void PointerMovementDoesNotDismissDuringStartupGracePeriod()
    {
        Assert.That(PointerDismissPolicy.ShouldDismiss(0, 0, 100, 100, TimeSpan.FromMilliseconds(749)), Is.False);
    }

    [Test]
    public void SmallPointerJitterDoesNotDismiss()
    {
        Assert.That(PointerDismissPolicy.ShouldDismiss(0, 0, 3, 3, TimeSpan.FromSeconds(1)), Is.False);
    }

    [Test]
    public void MeaningfulPointerMovementAfterGraceDismisses()
    {
        Assert.That(PointerDismissPolicy.ShouldDismiss(0, 0, 5, 0, TimeSpan.FromSeconds(1)), Is.True);
    }

    [Test]
    public void PreloadedWebGpuLibraryCanBeOpenedBySilk()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The native WebGPU package is Windows-specific for this test.");

        IntPtr library = WebGpuNativeLibrary.Load();
        try
        {
            using var webGpu = WebGPU.GetApi();
            Assert.That(webGpu, Is.Not.Null);
        }
        finally
        {
            System.Runtime.InteropServices.NativeLibrary.Free(library);
        }
    }
}