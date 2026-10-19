using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinRT;

namespace Murmuration;

// D3D11 renderer hosted in a SwapChainPanel; steps the simulation at a fixed rate and draws it.
public sealed unsafe class Renderer : IDisposable
{
    private static readonly Guid SwapChainPanelNativeIid = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    private const float FixedStep = 1f / 60f;
    private const int MaxStepsPerFrame = 5;

    private readonly SwapChainPanel _panel;
    private readonly Simulation _simulation;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private ID3D11RenderTargetView _rtv;
    private ID3D11Texture2D _depthTexture;
    private ID3D11DepthStencilView _dsv;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11InputLayout _inputLayout;
    private readonly ID3D11Buffer _constantBuffer;
    private readonly ID3D11Buffer _birdVertexBuffer;
    private readonly ID3D11Buffer _groundVertexBuffer;
    private readonly ID3D11Buffer _instanceBuffer;
    private readonly ID3D11Buffer _groundInstanceBuffer;
    private readonly ID3D11DepthStencilState _depthState;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly InstanceData[] _instances;
    private readonly uint _birdVertexCount;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private float _accumulator;
    private uint _width;
    private uint _height;

    public Renderer(SwapChainPanel panel, Simulation simulation)
    {
        _panel = panel;
        _simulation = simulation;

        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out _device!,
            out _context!).CheckError();

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        _width = (uint)Math.Max(1, (int)(panel.ActualWidth * panel.CompositionScaleX));
        _height = (uint)Math.Max(1, (int)(panel.ActualHeight * panel.CompositionScaleY));

        var desc = new SwapChainDescription1
        {
            Width = _width,
            Height = _height,
            Format = Format.B8G8R8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipSequential,
            Scaling = Scaling.Stretch,
            AlphaMode = AlphaMode.Ignore,
        };
        _swapChain = factory.CreateSwapChainForComposition(_device, desc);

        AttachToPanel();
        _rtv = CreateRenderTargetView();
        (_depthTexture, _dsv) = CreateDepthBuffer();

        (_vertexShader, _pixelShader, _inputLayout) = CreateShaders();

        _constantBuffer = _device.CreateBuffer(new BufferDescription(
            (uint)sizeof(Matrix4x4), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        Vertex[] bird = Geometry.CreateBird();
        _birdVertexCount = (uint)bird.Length;
        _birdVertexBuffer = CreateImmutableBuffer(bird, BindFlags.VertexBuffer);
        _groundVertexBuffer = CreateImmutableBuffer(Geometry.CreateGround(), BindFlags.VertexBuffer);

        _instances = new InstanceData[simulation.Birds.Length];
        _instanceBuffer = _device.CreateBuffer(new BufferDescription(
            (uint)(sizeof(InstanceData) * _instances.Length), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _groundInstanceBuffer = CreateImmutableBuffer(
            [new InstanceData { Position = Vector3.Zero, Direction = Vector3.UnitX, Up = Vector3.UnitZ }],
            BindFlags.VertexBuffer);

        _depthState = _device.CreateDepthStencilState(DepthStencilDescription.Default);
        _rasterizerState = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));

        panel.SizeChanged += OnSizeChanged;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    private void AttachToPanel()
    {
        IntPtr unknown = MarshalInspectable<SwapChainPanel>.FromManaged(_panel);
        try
        {
            Guid iid = SwapChainPanelNativeIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out IntPtr native));
            try
            {
                // ISwapChainPanelNative::SetSwapChain is vtable slot 3.
                var vtbl = *(void***)native;
                var setSwapChain = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtbl[3];
                Marshal.ThrowExceptionForHR(setSwapChain(native, _swapChain.NativePointer));
            }
            finally
            {
                Marshal.Release(native);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private ID3D11RenderTargetView CreateRenderTargetView()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        return _device.CreateRenderTargetView(backBuffer);
    }

    private (ID3D11Texture2D, ID3D11DepthStencilView) CreateDepthBuffer()
    {
        var texture = _device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, _width, _height, 1, 1, BindFlags.DepthStencil));
        return (texture, _device.CreateDepthStencilView(texture));
    }

    private (ID3D11VertexShader, ID3D11PixelShader, ID3D11InputLayout) CreateShaders()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaders.hlsl")!;
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();

        Compiler.Compile(source, "VSMain", "Shaders.hlsl", "vs_5_0", out Blob vsBlob, out Blob? vsErrors);
        if (vsBlob == null) throw new InvalidOperationException(vsErrors?.AsString());
        Compiler.Compile(source, "PSMain", "Shaders.hlsl", "ps_5_0", out Blob psBlob, out Blob? psErrors);
        if (psBlob == null) throw new InvalidOperationException(psErrors?.AsString());

        var vs = _device.CreateVertexShader(vsBlob.AsBytes());
        var ps = _device.CreatePixelShader(psBlob.AsBytes());

        InputElementDescription[] elements =
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
            new("COLOR", 0, Format.R32G32B32_Float, 12, 0, InputClassification.PerVertexData, 0),
            new("IPOS", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
            new("IDIR", 0, Format.R32G32B32_Float, 12, 1, InputClassification.PerInstanceData, 1),
            new("IUP", 0, Format.R32G32B32_Float, 24, 1, InputClassification.PerInstanceData, 1),
        ];
        var layout = _device.CreateInputLayout(elements, vsBlob.AsBytes());

        vsBlob.Dispose();
        psBlob.Dispose();
        return (vs, ps, layout);
    }

    private ID3D11Buffer CreateImmutableBuffer<T>(T[] data, BindFlags bindFlags) where T : unmanaged
    {
        fixed (T* p = data)
        {
            var desc = new BufferDescription((uint)(sizeof(T) * data.Length), bindFlags, ResourceUsage.Immutable);
            return _device.CreateBuffer(desc, new SubresourceData((nint)p));
        }
    }

    private void WriteDynamic<T>(ID3D11Buffer buffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        MappedSubresource mapped = _context.Map(buffer, MapMode.WriteDiscard);
        data.CopyTo(new Span<T>((void*)mapped.DataPointer, data.Length));
        _context.Unmap(buffer);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        uint w = (uint)Math.Max(1, (int)(e.NewSize.Width * _panel.CompositionScaleX));
        uint h = (uint)Math.Max(1, (int)(e.NewSize.Height * _panel.CompositionScaleY));
        if (w == _width && h == _height) return;

        _width = w;
        _height = h;
        _context.ClearState();
        _rtv.Dispose();
        _dsv.Dispose();
        _depthTexture.Dispose();
        _swapChain.ResizeBuffers(0, w, h, Format.Unknown, SwapChainFlags.None).CheckError();
        _rtv = CreateRenderTargetView();
        (_depthTexture, _dsv) = CreateDepthBuffer();
    }

    private void OnRendering(object? sender, object e)
    {
        Update();
        Draw();
    }

    private void Update()
    {
        double now = _clock.Elapsed.TotalSeconds;
        _accumulator += (float)(now - _lastTime);
        _lastTime = now;

        int steps = 0;
        while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
        {
            _simulation.Step(FixedStep);
            _accumulator -= FixedStep;
            steps++;
        }
        if (steps == MaxStepsPerFrame) _accumulator = 0;
    }

    private void Draw()
    {
        Bird[] birds = _simulation.Birds;
        for (int i = 0; i < birds.Length; i++)
        {
            _instances[i] = new InstanceData
            {
                Position = birds[i].Position,
                Direction = birds[i].Direction,
                Up = birds[i].Up,
            };
        }
        WriteDynamic<InstanceData>(_instanceBuffer, _instances);

        Matrix4x4 viewProj = Camera.ViewProjection((float)_width / _height);
        WriteDynamic(_constantBuffer, new ReadOnlySpan<Matrix4x4>(in viewProj));

        _context.OMSetRenderTargets(_rtv, _dsv);
        _context.OMSetDepthStencilState(_depthState);
        _context.RSSetState(_rasterizerState);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.ClearRenderTargetView(_rtv, new Color4(0.05f, 0.08f, 0.15f, 1f));
        _context.ClearDepthStencilView(_dsv, DepthStencilClearFlags.Depth, 1f, 0);

        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);

        uint vertexStride = (uint)sizeof(Vertex);
        uint instanceStride = (uint)sizeof(InstanceData);

        _context.IASetVertexBuffer(0, _groundVertexBuffer, vertexStride);
        _context.IASetVertexBuffer(1, _groundInstanceBuffer, instanceStride);
        _context.DrawInstanced(6, 1, 0, 0);

        _context.IASetVertexBuffer(0, _birdVertexBuffer, vertexStride);
        _context.IASetVertexBuffer(1, _instanceBuffer, instanceStride);
        _context.DrawInstanced(_birdVertexCount, (uint)_instances.Length, 0, 0);

        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    public void Dispose()
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        _panel.SizeChanged -= OnSizeChanged;
        _rasterizerState.Dispose();
        _depthState.Dispose();
        _groundInstanceBuffer.Dispose();
        _instanceBuffer.Dispose();
        _groundVertexBuffer.Dispose();
        _birdVertexBuffer.Dispose();
        _constantBuffer.Dispose();
        _inputLayout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _dsv.Dispose();
        _depthTexture.Dispose();
        _rtv.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
