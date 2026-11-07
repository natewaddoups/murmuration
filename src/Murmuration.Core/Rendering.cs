using System.Numerics;
using System.Reflection;

namespace Murmuration;

public static class ShaderSources
{
    public static string Render { get; } = Read("render.wgsl");
    public static string Simulation { get; } = GpuBird.WgslDeclaration() + Murmuration.Simulation.WgslConstants + Read("simulation.wgsl");

    private static string Read(string name)
    {
        using var stream = typeof(ShaderSources).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public interface IGpuRenderer : IDisposable
{
    // viewProj is a System.Numerics (row-major) matrix; width and height are in physical pixels.
    void Render(ReadOnlySpan<InstanceData> birds, in Matrix4x4 viewProj, int width, int height);
}

public interface IGpuSimulationRenderer : IGpuRenderer
{
    bool SupportsGpuSimulation { get; }
    int MaxBirds { get; }
    void RenderSimulation(int firstNewBird, int count, float dt, uint seed,
        in Matrix4x4 viewProj, int width, int height);
}

// Steps the simulation and draws it; the host calls Frame once per display frame.
public sealed class FrameRunner
{
    private const float MaxFrameDelta = 0.1f;

    private readonly Simulation _simulation;
    private readonly IGpuRenderer _renderer;
    private InstanceData[] _instances;
    private int _gpuBirdCount;
    private double _lastTime = double.NaN;
    private double _startTime;
    private double _fpsWindowStart;
    private int _fpsFrames;

    public FrameRunner(Simulation simulation, IGpuRenderer renderer)
    {
        _simulation = simulation;
        _renderer = renderer;
        if (renderer is not IGpuSimulationRenderer { SupportsGpuSimulation: true }) simulation.EnsureInitialized();
        _instances = renderer is IGpuSimulationRenderer { SupportsGpuSimulation: true } ? [] : new InstanceData[simulation.Birds.Length];
    }

    public volatile bool Paused;

    // Raised from Frame about twice a second.
    public event Action<double>? FpsUpdated;

    public void Frame(double nowSeconds, int width, int height)
    {
        if (double.IsNaN(_lastTime))
        {
            _lastTime = _fpsWindowStart = _startTime = nowSeconds;
        }

        double frameDelta = nowSeconds - _lastTime;
        float dt = Math.Min((float)frameDelta, MaxFrameDelta);
        _lastTime = nowSeconds;
        int frameRate = frameDelta > 0 ? (int)(1.0 / frameDelta) : 60;
        float simulationDelta = !Paused && dt > 0f ? dt : 0f;
        if (_renderer is IGpuSimulationRenderer { SupportsGpuSimulation: true } gpuSimulation)
        {
            if (simulationDelta > 0f) _simulation.AdjustPopulationSize(frameRate, simulationDelta, gpuSimulation.MaxBirds, initializeNewBirds: false);

            Bird[] gpuBirds = _simulation.Birds;
            int firstNewBird = Math.Min(_gpuBirdCount, gpuBirds.Length);
            Matrix4x4 gpuViewProj = Camera.ViewProjection((float)width / height, nowSeconds - _startTime, in gpuBirds[0]);
            gpuSimulation.RenderSimulation(firstNewBird, gpuBirds.Length, simulationDelta, _simulation.Seed, in gpuViewProj, width, height);
            _gpuBirdCount = gpuBirds.Length;
        }
        else
        {
            if (simulationDelta > 0f) _simulation.Step(simulationDelta, frameRate);

            Bird[] birds = _simulation.Birds;
            if (_instances.Length != birds.Length)
                Array.Resize(ref _instances, birds.Length);
            for (int i = 0; i < birds.Length; i++)
            {
                _instances[i] = new InstanceData
                {
                    Position = birds[i].Position,
                    Direction = birds[i].Direction,
                    Up = birds[i].Up,
                };
            }

            Matrix4x4 viewProj = Camera.ViewProjection((float)width / height, nowSeconds - _startTime, in birds[0]);
            _renderer.Render(_instances, in viewProj, width, height);
        }

        _fpsFrames++;
        double elapsed = nowSeconds - _fpsWindowStart;
        if (elapsed >= 0.5)
        {
            FpsUpdated?.Invoke(_fpsFrames / elapsed);
            _fpsFrames = 0;
            _fpsWindowStart = nowSeconds;
        }
    }
}
