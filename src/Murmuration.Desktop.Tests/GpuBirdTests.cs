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
            FlapRate = 3.5f,
            FlapPhase = 1.25f,
        };

        Assert.That(GpuBird.FromBird(bird).ToBird(), Is.EqualTo(bird));
    }

    [Test]
    public void LayoutMatchesGeneratedWgsl()
    {
        Assert.That(Marshal.SizeOf<GpuBird>(), Is.EqualTo(96));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Position)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Orientation)).ToInt32(), Is.EqualTo(16));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DesiredDirection)).ToInt32(), Is.EqualTo(32));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Flight)).ToInt32(), Is.EqualTo(48));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Climb)).ToInt32(), Is.EqualTo(64));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DirectionX)).ToInt32(), Is.EqualTo(68));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DirectionY)).ToInt32(), Is.EqualTo(72));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.DirectionZ)).ToInt32(), Is.EqualTo(76));
        Assert.That(Marshal.OffsetOf<GpuBird>(nameof(GpuBird.Flap)).ToInt32(), Is.EqualTo(80));
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
            "    Flap: vec4<f32>,\n" +
            "};\n"));
    }

    [Test]
    public void RenderLayoutsIncludeFlapFields()
    {
        Assert.That(Marshal.SizeOf<InstanceData>(), Is.EqualTo(40));
        Assert.That(Marshal.OffsetOf<InstanceData>(nameof(InstanceData.FlapAngle)).ToInt32(), Is.EqualTo(36));
        Assert.That(Marshal.SizeOf<Vertex>(), Is.EqualTo(28));
        Assert.That(Marshal.OffsetOf<Vertex>(nameof(Vertex.WingSide)).ToInt32(), Is.EqualTo(24));
    }

    [Test]
    public void OnlyWingVerticesHaveWingSide()
    {
        Vertex[] bird = Geometry.CreateBird();

        Assert.That(bird.Count(v => v.WingSide == 1f), Is.EqualTo(12));
        Assert.That(bird.Count(v => v.WingSide == -1f), Is.EqualTo(12));
        Assert.That(bird.Count(v => v.WingSide == 0f), Is.EqualTo(24));
        Assert.That(bird.Where(v => v.WingSide != 0f).All(v => v.WingSide * v.Position.Y >= 0f), Is.True);
    }

    [Test]
    public void SimulationShaderIncludesGeneratedBirdLayout()
    {
        Assert.That(ShaderSources.Simulation, Does.StartWith(GpuBird.WgslDeclaration()));
        Assert.That(ShaderSources.Simulation, Does.Contain("@compute @workgroup_size(64)"));
    }
}