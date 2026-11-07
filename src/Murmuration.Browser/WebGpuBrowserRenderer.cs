using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Murmuration.Browser;

// Calls into webgpu-renderer.js, which owns the GPUDevice and all GPU resources.
[SupportedOSPlatform("browser")]
internal sealed partial class WebGpuBrowserRenderer : IGpuSimulationRenderer
{
    private WebGpuBrowserRenderer(bool supportsGpuSimulation, int maxBirds)
    {
        SupportsGpuSimulation = supportsGpuSimulation;
        MaxBirds = maxBirds;
    }

    public bool SupportsGpuSimulation { get; }
    public int MaxBirds { get; }

    public static async Task<WebGpuBrowserRenderer> CreateAsync(string canvasId)
    {
        await JSHost.ImportAsync("webgpu-renderer", "../webgpu-renderer.js");
        await InitDevice(canvasId);

        Vertex[] bird = Geometry.CreateBird();
        Vertex[] ground = Geometry.CreateGround();
        InstanceData[] groundInstance = [new InstanceData { Position = Vector3.Zero, Direction = Vector3.UnitX, Up = Vector3.UnitZ }];
        CreateResources(ShaderSources.Render, MemoryMarshal.AsBytes(bird.AsSpan()), MemoryMarshal.AsBytes(ground.AsSpan()),
            MemoryMarshal.AsBytes(groundInstance.AsSpan()), Marshal.SizeOf<GpuBird>(), Marshal.SizeOf<InstanceData>());
        bool supportsGpuSimulation = await CreateCompute(ShaderSources.Simulation);
        return new WebGpuBrowserRenderer(supportsGpuSimulation, GetMaxBirds());
    }

    // Matches the canvas to its CSS size and reports the size in physical pixels.
    public (int Width, int Height) SyncSize()
    {
        SyncCanvasSize();
        return (GetWidth(), GetHeight());
    }

    public void Render(ReadOnlySpan<InstanceData> birds, in Matrix4x4 viewProj, int width, int height)
    {
        Span<byte> instanceBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(birds), birds.Length));
        Span<byte> matrixBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in viewProj), 1));
        RenderFrame(instanceBytes, matrixBytes, birds.Length);
    }

    public void RenderSimulation(int firstNewBird, int count, float dt, uint seed,
        in Matrix4x4 viewProj, int width, int height)
    {
        Span<byte> matrixBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in viewProj), 1));
        RenderSimulationFrame(firstNewBird, count, dt, unchecked((int)seed), matrixBytes);
    }

    public void Dispose() => DisposeResources();

    [JSImport("initDevice", "webgpu-renderer")]
    private static partial Task InitDevice(string canvasId);

    [JSImport("createResources", "webgpu-renderer")]
    private static partial void CreateResources(string shaderSource,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> birdBytes,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> groundBytes,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> groundInstanceBytes,
        int birdStride, int instanceStride);

    [JSImport("dispose", "webgpu-renderer")]
    private static partial void DisposeResources();

    [JSImport("createCompute", "webgpu-renderer")]
    private static partial Task<bool> CreateCompute(string simulationSource);

    [JSImport("getMaxBirds", "webgpu-renderer")]
    private static partial int GetMaxBirds();

    [JSImport("syncSize", "webgpu-renderer")]
    private static partial void SyncCanvasSize();

    [JSImport("getWidth", "webgpu-renderer")]
    private static partial int GetWidth();

    [JSImport("getHeight", "webgpu-renderer")]
    private static partial int GetHeight();

    [JSImport("render", "webgpu-renderer")]
    private static partial void RenderFrame(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> instanceBytes,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> viewProjBytes,
        int instanceCount);

    [JSImport("renderSimulation", "webgpu-renderer")]
    private static partial void RenderSimulationFrame(
        int firstNewBird, int count, float dt, int seed,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> viewProjBytes);

    [JSImport("requestFrame", "webgpu-renderer")]
    public static partial void RequestFrame([JSMarshalAs<JSType.Function<JSType.Number>>] Action<double> callback);
}
