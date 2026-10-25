using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Murmuration.Desktop;

public sealed unsafe class WebGpuDesktopRenderer : IGpuRenderer
{
    private const TextureFormat DepthFormat = TextureFormat.Depth32float;

    private readonly WebGPU _wgpu;
    private readonly IntPtr _preloadedNativeLibrary;
    private readonly Instance* _instance;
    private readonly Surface* _surface;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly TextureFormat _format;
    private readonly RenderPipeline* _pipeline;
    private readonly BindGroup* _bindGroup;
    private readonly WgpuBuffer* _uniformBuffer;
    private readonly WgpuBuffer* _birdBuffer;
    private readonly WgpuBuffer* _groundBuffer;
    private readonly WgpuBuffer* _instanceBuffer;
    private readonly WgpuBuffer* _groundInstanceBuffer;
    private readonly uint _birdVertexCount;
    private readonly uint _maxInstances;
    private Texture* _depthTexture;
    private TextureView* _depthView;
    private int _width;
    private int _height;

    public WebGpuDesktopRenderer(IntPtr hwnd)
    {
        _preloadedNativeLibrary = WebGpuNativeLibrary.Load();
        try
        {
            _wgpu = WebGPU.GetApi();
        }
        catch
        {
            NativeLibrary.Free(_preloadedNativeLibrary);
            throw;
        }

        var instanceDesc = new InstanceDescriptor();
        _instance = _wgpu.CreateInstance(&instanceDesc);

        var hwndDesc = new SurfaceDescriptorFromWindowsHWND
        {
            Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromWindowsHwnd },
            Hinstance = (void*)GetModuleHandleW(null),
            Hwnd = (void*)hwnd,
        };
        var surfaceDesc = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&hwndDesc };
        _surface = _wgpu.InstanceCreateSurface(_instance, &surfaceDesc);

        Adapter* adapter = null;
        var adapterOptions = new RequestAdapterOptions { CompatibleSurface = _surface };
        _wgpu.InstanceRequestAdapter(_instance, &adapterOptions,
            new PfnRequestAdapterCallback((status, a, msg, data) => adapter = a), null);

        Device* device = null;
        var deviceDesc = new DeviceDescriptor();
        _wgpu.AdapterRequestDevice(adapter, &deviceDesc,
            new PfnRequestDeviceCallback((status, d, msg, data) => device = d), null);
        _device = device;
        _queue = _wgpu.DeviceGetQueue(_device);
        _format = _wgpu.SurfaceGetPreferredFormat(_surface, adapter);

        Vertex[] bird = Geometry.CreateBird();
        Vertex[] ground = Geometry.CreateGround();
        _birdVertexCount = (uint)bird.Length;
        _maxInstances = 1024;

        _uniformBuffer = CreateBuffer(64, BufferUsage.Uniform | BufferUsage.CopyDst);
        _birdBuffer = CreateBuffer((ulong)(bird.Length * sizeof(Vertex)), BufferUsage.Vertex | BufferUsage.CopyDst);
        _groundBuffer = CreateBuffer((ulong)(ground.Length * sizeof(Vertex)), BufferUsage.Vertex | BufferUsage.CopyDst);
        _instanceBuffer = CreateBuffer(_maxInstances * (ulong)sizeof(InstanceData), BufferUsage.Vertex | BufferUsage.CopyDst);
        _groundInstanceBuffer = CreateBuffer((ulong)sizeof(InstanceData), BufferUsage.Vertex | BufferUsage.CopyDst);

        Write(_birdBuffer, bird);
        Write(_groundBuffer, ground);
        Write(_groundInstanceBuffer, [new InstanceData { Position = Vector3.Zero, Direction = Vector3.UnitX, Up = Vector3.UnitZ }]);

        _pipeline = CreatePipeline();

        var entry = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Size = 64 };
        var groupDesc = new BindGroupDescriptor
        {
            Layout = _wgpu.RenderPipelineGetBindGroupLayout(_pipeline, 0),
            EntryCount = 1,
            Entries = &entry,
        };
        _bindGroup = _wgpu.DeviceCreateBindGroup(_device, &groupDesc);
    }

    private RenderPipeline* CreatePipeline()
    {
        string source = ShaderSources.Render;
        var code = (byte*)SilkMarshal.StringToPtr(source);
        var wgsl = new ShaderModuleWGSLDescriptor { Code = code, Chain = new ChainedStruct { SType = SType.ShaderModuleWgsldescriptor } };
        var moduleDesc = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
        ShaderModule* module = _wgpu.DeviceCreateShaderModule(_device, &moduleDesc);
        SilkMarshal.Free((nint)code);

        var entryVs = (byte*)SilkMarshal.StringToPtr("vs");
        var entryFs = (byte*)SilkMarshal.StringToPtr("fs");

        var vertexAttributes = stackalloc VertexAttribute[2];
        vertexAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        vertexAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        var instanceAttributes = stackalloc VertexAttribute[3];
        instanceAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 2 };
        instanceAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 3 };
        instanceAttributes[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 24, ShaderLocation = 4 };

        var layouts = stackalloc VertexBufferLayout[2];
        layouts[0] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(Vertex), StepMode = VertexStepMode.Vertex, AttributeCount = 2, Attributes = vertexAttributes };
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(InstanceData), StepMode = VertexStepMode.Instance, AttributeCount = 3, Attributes = instanceAttributes };

        var colorTarget = new ColorTargetState { Format = _format, WriteMask = ColorWriteMask.All };
        var fragment = new FragmentState { Module = module, EntryPoint = entryFs, TargetCount = 1, Targets = &colorTarget };
        var stencil = new StencilFaceState { Compare = CompareFunction.Always };
        var depthStencil = new DepthStencilState
        {
            Format = DepthFormat,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.Less,
            StencilFront = stencil,
            StencilBack = stencil,
            StencilReadMask = 0xFFFFFFFF,
            StencilWriteMask = 0xFFFFFFFF,
        };

        var desc = new RenderPipelineDescriptor
        {
            Vertex = new VertexState { Module = module, EntryPoint = entryVs, BufferCount = 2, Buffers = layouts },
            Fragment = &fragment,
            DepthStencil = &depthStencil,
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Multisample = new MultisampleState { Count = 1, Mask = ~0u },
        };
        return _wgpu.DeviceCreateRenderPipeline(_device, &desc);
    }

    private WgpuBuffer* CreateBuffer(ulong size, BufferUsage usage)
    {
        var desc = new BufferDescriptor { Size = size, Usage = usage };
        return _wgpu.DeviceCreateBuffer(_device, &desc);
    }

    private void Write<T>(WgpuBuffer* buffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        fixed (T* p = data)
            _wgpu.QueueWriteBuffer(_queue, buffer, 0, p, (nuint)(data.Length * sizeof(T)));
    }

    private void Resize(int width, int height)
    {
        _width = width;
        _height = height;

        var config = new SurfaceConfiguration
        {
            Device = _device,
            Usage = TextureUsage.RenderAttachment,
            Format = _format,
            Width = (uint)width,
            Height = (uint)height,
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto,
        };
        _wgpu.SurfaceConfigure(_surface, &config);

        ReleaseDepth();
        var depthDesc = new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = DepthFormat,
            Usage = TextureUsage.RenderAttachment,
        };
        _depthTexture = _wgpu.DeviceCreateTexture(_device, &depthDesc);
        _depthView = _wgpu.TextureCreateView(_depthTexture, null);
    }

    private void ReleaseDepth()
    {
        if (_depthView != null) _wgpu.TextureViewRelease(_depthView);
        if (_depthTexture != null) _wgpu.TextureRelease(_depthTexture);
        _depthView = null;
        _depthTexture = null;
    }

    public void Render(ReadOnlySpan<InstanceData> birds, in Matrix4x4 viewProj, int width, int height)
    {
        if (birds.Length > _maxInstances) throw new InvalidOperationException($"At most {_maxInstances} birds are supported.");
        if (_depthView == null || width != _width || height != _height) Resize(width, height);

        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        if (surfaceTexture.Texture == null)
        {
            _width = 0;
            return;
        }
        TextureView* view = _wgpu.TextureCreateView(surfaceTexture.Texture, null);

        Matrix4x4 matrix = viewProj;
        _wgpu.QueueWriteBuffer(_queue, _uniformBuffer, 0, &matrix, 64);
        Write(_instanceBuffer, birds);

        CommandEncoder* encoder = _wgpu.DeviceCreateCommandEncoder(_device, new CommandEncoderDescriptor());

        var color = new RenderPassColorAttachment
        {
            View = view,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.0, G = 0.0, B = 0.0, A = 1 },
        };
        var depth = new RenderPassDepthStencilAttachment
        {
            View = _depthView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1f,
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color, DepthStencilAttachment = &depth };
        RenderPassEncoder* pass = _wgpu.CommandEncoderBeginRenderPass(encoder, &passDesc);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);

        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _groundBuffer, 0, ulong.MaxValue);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _groundInstanceBuffer, 0, ulong.MaxValue);
        _wgpu.RenderPassEncoderDraw(pass, 6, 1, 0, 0);

        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _birdBuffer, 0, ulong.MaxValue);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 1, _instanceBuffer, 0, ulong.MaxValue);
        _wgpu.RenderPassEncoderDraw(pass, _birdVertexCount, (uint)birds.Length, 0, 0);
        _wgpu.RenderPassEncoderEnd(pass);

        CommandBuffer* commands = _wgpu.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
        _wgpu.QueueSubmit(_queue, 1, &commands);
        _wgpu.SurfacePresent(_surface);

        _wgpu.CommandBufferRelease(commands);
        _wgpu.CommandEncoderRelease(encoder);
        _wgpu.TextureViewRelease(view);
        _wgpu.TextureRelease(surfaceTexture.Texture);
    }

    public void Dispose()
    {
        ReleaseDepth();
        _wgpu.BindGroupRelease(_bindGroup);
        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.BufferRelease(_groundInstanceBuffer);
        _wgpu.BufferRelease(_instanceBuffer);
        _wgpu.BufferRelease(_groundBuffer);
        _wgpu.BufferRelease(_birdBuffer);
        _wgpu.BufferRelease(_uniformBuffer);
        _wgpu.QueueRelease(_queue);
        _wgpu.DeviceRelease(_device);
        _wgpu.SurfaceRelease(_surface);
        _wgpu.InstanceRelease(_instance);
        _wgpu.Dispose();
        NativeLibrary.Free(_preloadedNativeLibrary);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
