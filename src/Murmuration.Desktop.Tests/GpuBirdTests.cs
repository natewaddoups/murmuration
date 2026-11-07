using System.Numerics;
using System.Runtime.InteropServices;
using Murmuration;
using NUnit.Framework;

namespace Murmuration.Desktop.Tests;

public class GpuBirdTests
{
    [Test]
    public void BirdStateRoundTrips()
    {
        var bird = new Bird
        {
            Position = new Vector3(1f, 2f, 3f),
            Orientation = new Quaternion(4f, 5f, 6f, 7f),
            DesiredDirection = new Vector3(8f, 9f, 10f),
            Speed = 11f,
            PreferredSpeed = 12f,
            Mass = 13f,
            Climb = ClimbDirection.Right,
            ClimbStrength = 14f,
        };

        Assert.That(GpuBird.FromBird(bird).ToBird(), Is.EqualTo(bird));
    }

    [Test]
    public void LayoutMatchesGeneratedWgsl()
    {
        Assert.That(Marshal.SizeOf<GpuBird>(), Is.EqualTo(80));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Position)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Orientation)).ToInt32(), Is.EqualTo(16));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DesiredDirection)).ToInt32(), Is.EqualTo(32));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Flight)).ToInt32(), Is.EqualTo(48));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Climb)).ToInt32(), Is.EqualTo(64));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DirectionX)).ToInt32(), Is.EqualTo(68));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DirectionY)).ToInt32(), Is.EqualTo(72));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DirectionZ)).ToInt32(), Is.EqualTo(76));
        Assert.That(GpuBird.WgslDeclaration(), Is.EqualTo(
            "struct GpuBird {\n" +
            "    Position: vec4<f32>,\n" +
            "    Orientation: vec4<f32>,\n" +
            "    DesiredDirection: vec4<f32>,\n" +
            "    Flight: vec4<f32>,\n" +
            "    Climb: u32,\n" +
            "    DirectionX: f32,\n" +
            "    DirectionY: f32,\n" +
            "    DirectionZ: f32,\n" +
            "};\n"));
    }

    [Test]
    public void SimulationShaderIncludesGeneratedBirdLayout()
    {
        Assert.That(ShaderSources.Simulation, Does.StartWith(GpuBird.WgslDeclaration()));
        Assert.That(ShaderSources.Simulation, Does.Contain("@compute @workgroup_size(64)"));
    }
}