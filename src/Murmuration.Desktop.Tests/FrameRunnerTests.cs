using System.Numerics;
using Murmuration;
using NUnit.Framework;

namespace Murmuration.Desktop.Tests;

public class FrameRunnerTests
{
    [Test]
    public void GpuRendererInitializesBirdsOnlyOnStartupAndGrowth()
    {
        var renderer = new FakeGpuRenderer();
        var simulation = new Simulation(10, 42, deferInitialization: true);
        var runner = new FrameRunner(simulation, renderer);
        runner.Frame(0, 800, 600);
        Assert.That(simulation.Birds[0].Orientation, Is.EqualTo(default(Quaternion)));
        Assert.That(renderer.InitializedCounts, Is.EqualTo(new[] { 10 }));
        Assert.That(renderer.Delta, Is.Zero);

        for (int frame = 1; frame <= 125; frame++)
            runner.Frame(frame / 120.0, 800, 600);

        Assert.That(renderer.InitializedCounts.Count(count => count > 0), Is.EqualTo(2));
        Assert.That(renderer.InitializedCounts.Sum(), Is.EqualTo(11));
        Assert.That(renderer.Count, Is.EqualTo(11));
        runner.Paused = true;
        runner.Frame(126 / 120.0, 800, 600);
        Assert.That(renderer.Delta, Is.Zero);
        Assert.That(renderer.InitializedCounts.Last(), Is.Zero);
    }

    [Test]
    public void GpuPopulationGrowthAfterShrinkInitializesNewBirds()
    {
        var renderer = new FakeGpuRenderer();
        var runner = new FrameRunner(new Simulation(16, 42), renderer);
        runner.Frame(0, 800, 600);
        for (int frame = 1; frame <= 3; frame++)
            runner.Frame(frame / 30.0, 800, 600);
        Assert.That(renderer.Count, Is.EqualTo(10));

        for (int frame = 1; frame <= 125; frame++)
            runner.Frame(0.1 + frame / 120.0, 800, 600);

        Assert.That(renderer.Count, Is.EqualTo(11));
        Assert.That(renderer.FirstNewBirds.Last(index => index < renderer.Count), Is.EqualTo(10));
        Assert.That(renderer.InitializedCounts.Sum(), Is.EqualTo(17));
    }

    [Test]
    public void UnavailableComputeUsesCpuSimulation()
    {
        var renderer = new FakeGpuRenderer { SupportsGpuSimulation = false };
        var runner = new FrameRunner(new Simulation(10, 42, deferInitialization: true), renderer);
        runner.Frame(0, 800, 600);
        var initial = renderer.LastPosition;
        runner.Frame(1.0 / 60, 800, 600);

        Assert.That(renderer.CpuFrames, Is.EqualTo(2));
        Assert.That(renderer.LastPosition, Is.Not.EqualTo(initial));
        Assert.That(renderer.InitializedCounts, Is.Empty);
    }

    private sealed class FakeGpuRenderer : IGpuSimulationRenderer
    {
        public bool SupportsGpuSimulation { get; set; } = true;
        public int MaxBirds => 1024;
        public List<int> InitializedCounts { get; } = [];
        public List<int> FirstNewBirds { get; } = [];
        public int Count { get; private set; }
        public float Delta { get; private set; }
        public int CpuFrames { get; private set; }
        public Vector3 LastPosition { get; private set; }

        public void RenderSimulation(int firstNewBird, int count, float dt, uint seed,
            in Matrix4x4 viewProj, int width, int height)
        {
            InitializedCounts.Add(count - firstNewBird);
            FirstNewBirds.Add(firstNewBird);
            Count = count;
            Delta = dt;
        }

        public void Render(ReadOnlySpan<InstanceData> birds, in Matrix4x4 viewProj, int width, int height)
        {
            CpuFrames++;
            LastPosition = birds[0].Position;
        }
        public void Dispose() { }
    }
}