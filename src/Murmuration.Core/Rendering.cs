using System.Numerics;
using System.Reflection;

namespace Murmuration;

public static class ShaderSources
{
    public static string Render { get; } = Read("render.wgsl");

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

// Steps the simulation and draws it; the host calls Frame once per display frame.
public sealed class FrameRunner
{
    private const float MaxFrameDelta = 0.1f;

    private readonly Simulation _simulation;
    private readonly IGpuRenderer _renderer;
    private InstanceData[] _instances;
    private double _lastTime = double.NaN;
    private double _fpsWindowStart;
    private int _fpsFrames;

    public FrameRunner(Simulation simulation, IGpuRenderer renderer)
    {
        _simulation = simulation;
        _renderer = renderer;
        _instances = new InstanceData[simulation.Birds.Length];
    }

    public volatile bool Paused;

    // Raised from Frame about twice a second.
    public event Action<double>? FpsUpdated;

    public void Frame(double nowSeconds, int width, int height)
    {
        if (double.IsNaN(_lastTime))
        {
            _lastTime = _fpsWindowStart = nowSeconds;
        }

        double frameDelta = nowSeconds - _lastTime;
        float dt = Math.Min((float)frameDelta, MaxFrameDelta);
        _lastTime = nowSeconds;
        int frameRate = frameDelta > 0 ? (int)(1.0 / frameDelta) : 60;
        if (dt > 0f && !Paused) _simulation.Step(dt, frameRate);

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

        Matrix4x4 viewProj = Camera.ViewProjection((float)width / height);
        _renderer.Render(_instances, in viewProj, width, height);

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
