using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Murmuration.Desktop;

public sealed unsafe class WebGpuDesktopRenderer : IGpuSimulationRenderer
{
    private const TextureFormat DepthFormat = TextureFormat.Depth32float;

    private readonly WebGPU _wgpu;
    private readonly IntPtr _preloadedNativeLibrary;
    private readonly Instance* _instance;
    private readonly Surface* _surface;
    private readonly Device* _device;
    private readonly PfnDeviceLostCallback _deviceLostCallback;
    private readonly PfnErrorCallback _uncapturedErrorCallback;
    private string? _gpuFailure;
    private readonly Queue* _queue;
    private readonly TextureFormat _format;
    private readonly RenderPipeline* _pipeline;
    private readonly BindGroup* _bindGroup;
    private readonly WgpuBuffer* _uniformBuffer;
    private readonly WgpuBuffer* _birdBuffer;
    private readonly WgpuBuffer* _groundBuffer;
    private WgpuBuffer* _instanceBuffer;
    private readonly WgpuBuffer* _groundInstanceBuffer;
    private readonly WgpuBuffer* _simulationUniform;
    private readonly ComputePipeline* _computePipeline;
    private readonly ComputePipeline* _initializePipeline;
    private WgpuBuffer* _state0;
    private WgpuBuffer* _state1;
    private BindGroup* _computeBind0;
    private BindGroup* _computeBind1;
    private BindGroup* _initializeBind0;
    private BindGroup* _initializeBind1;
    private uint _capacity;
    private int _stateIndex;
    private uint _simulationStep;
    private readonly uint _birdVertexCount;
    public bool SupportsGpuSimulation => _computePipeline != null && _initializePipeline != null;
    public int MaxBirds { get; }
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
        if (adapter == null)
        {
            _wgpu.SurfaceRelease(_surface);
            _wgpu.InstanceRelease(_instance);
            _wgpu.Dispose();
            NativeLibrary.Free(_preloadedNativeLibrary);
            throw new InvalidOperationException("No compatible WebGPU adapter is available.");
        }

        Device* device = null;
        _deviceLostCallback = new PfnDeviceLostCallback((reason, message, data) =>
            Volatile.Write(ref _gpuFailure, $"WebGPU device lost: {Marshal.PtrToStringUTF8((IntPtr)message) ?? reason.ToString()}"));
        var deviceDesc = new DeviceDescriptor { DeviceLostCallback = _deviceLostCallback };
        _wgpu.AdapterRequestDevice(adapter, &deviceDesc,
            new PfnRequestDeviceCallback((status, d, msg, data) => device = d), null);
        if (device == null)
        {
            _wgpu.AdapterRelease(adapter);
            _wgpu.SurfaceRelease(_surface);
            _wgpu.InstanceRelease(_instance);
            _wgpu.Dispose();
            NativeLibrary.Free(_preloadedNativeLibrary);
            throw new InvalidOperationException("Unable to create a WebGPU device.");
        }
        _device = device;
        _uncapturedErrorCallback = new PfnErrorCallback((type, message, data) =>
            Volatile.Write(ref _gpuFailure, $"WebGPU {type}: {Marshal.PtrToStringUTF8((IntPtr)message)}"));
        _wgpu.DeviceSetUncapturedErrorCallback(_device, _uncapturedErrorCallback, null);
        _queue = _wgpu.DeviceGetQueue(_device);
        _format = _wgpu.SurfaceGetPreferredFormat(_surface, adapter);
        _wgpu.AdapterRelease(adapter);

        Vertex[] bird = Geometry.CreateBird();
        Vertex[] ground = Geometry.CreateGround();
        _birdVertexCount = (uint)bird.Length;
        var limits = new SupportedLimits();
        _wgpu.DeviceGetLimits(_device, &limits);
        MaxBirds = (int)Math.Min(int.MaxValue, Math.Min(limits.Limits.MaxStorageBufferBindingSize / (ulong)sizeof(GpuBird),
            Math.Min(limits.Limits.MaxBufferSize / (ulong)sizeof(GpuBird), (ulong)limits.Limits.MaxComputeWorkgroupsPerDimension * 64)));

        _uniformBuffer = CreateBuffer(64, BufferUsage.Uniform | BufferUsage.CopyDst);
        _birdBuffer = CreateBuffer((ulong)(bird.Length * sizeof(Vertex)), BufferUsage.Vertex | BufferUsage.CopyDst);
        _groundBuffer = CreateBuffer((ulong)(ground.Length * sizeof(Vertex)), BufferUsage.Vertex | BufferUsage.CopyDst);
        _simulationUniform = CreateBuffer(32, BufferUsage.Uniform | BufferUsage.CopyDst);
        _groundInstanceBuffer = CreateBuffer((ulong)sizeof(InstanceData), BufferUsage.Vertex | BufferUsage.CopyDst);

        Write(_birdBuffer, bird);
        Write(_groundBuffer, ground);
        Write(_groundInstanceBuffer, [new InstanceData { Position = Vector3.Zero, Direction = Vector3.UnitX, Up = Vector3.UnitZ }]);

        _pipeline = CreatePipeline();
        _computePipeline = CreateComputePipeline("simulate");
        _initializePipeline = CreateComputePipeline("initialize");

        var entry = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Size = 64 };
        var groupDesc = new BindGroupDescriptor
        {
            Layout = _wgpu.RenderPipelineGetBindGroupLayout(_pipeline, 0),
            EntryCount = 1,
            Entries = &entry,
        };
        _bindGroup = _wgpu.DeviceCreateBindGroup(_device, &groupDesc);
    }

    private ComputePipeline* CreateComputePipeline(string entryPoint)
    {
        _wgpu.DevicePushErrorScope(_device, ErrorFilter.Validation);
        var code = (byte*)SilkMarshal.StringToPtr(ShaderSources.Simulation);
        var wgsl = new ShaderModuleWGSLDescriptor { Code = code, Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor } };
        var moduleDesc = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
        ShaderModule* module = _wgpu.DeviceCreateShaderModule(_device, &moduleDesc);
        var entry = (byte*)SilkMarshal.StringToPtr(entryPoint);
        var desc = new ComputePipelineDescriptor { Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = entry } };
        ComputePipeline* pipeline = _wgpu.DeviceCreateComputePipeline(_device, &desc);
        string? error = null;
        bool completed = false;
        var callback = new PfnErrorCallback((type, message, data) =>
        {
            if (type != ErrorType.NoError) error = Marshal.PtrToStringUTF8((IntPtr)message) ?? type.ToString();
            completed = true;
        });
        _wgpu.DevicePopErrorScope(_device, callback, null);
        var poll = (delegate* unmanaged[Cdecl]<Device*, uint, void*, uint>)NativeLibrary.GetExport(_preloadedNativeLibrary, "wgpuDevicePoll");
        poll(_device, 1, null);
        _wgpu.ShaderModuleRelease(module);
        SilkMarshal.Free((nint)entry);
        SilkMarshal.Free((nint)code);
        if (!completed || error != null)
        {
            System.Diagnostics.Debug.WriteLine($"GPU simulation unavailable; using CPU simulation: {error ?? "validation did not complete"}");
            if (pipeline != null) _wgpu.ComputePipelineRelease(pipeline);
            return null;
        }
        return pipeline;
    }

    private void EnsureCapacity(int count)
    {
        if (count > MaxBirds) throw new InvalidOperationException($"At most {MaxBirds} birds are supported by this device.");
        if (count <= _capacity) return;
        uint capacity = (uint)Math.Min(MaxBirds, Math.Max(count, _capacity == 0 ? 128L : _capacity * 2L));
        WgpuBuffer* state0 = CreateBuffer(capacity * (ulong)sizeof(GpuBird), BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst);
        WgpuBuffer* state1 = CreateBuffer(capacity * (ulong)sizeof(GpuBird), BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst);
        WgpuBuffer* instances = CreateBuffer(capacity * (ulong)sizeof(InstanceData), BufferUsage.Storage | BufferUsage.Vertex | BufferUsage.CopyDst);
        if (_capacity > 0)
        {
            CommandEncoder* encoder = _wgpu.DeviceCreateCommandEncoder(_device, new CommandEncoderDescriptor());
            _wgpu.CommandEncoderCopyBufferToBuffer(encoder, _stateIndex == 0 ? _state0 : _state1, 0,
                state0, 0, _capacity * (ulong)sizeof(GpuBird));
            CommandBuffer* commands = _wgpu.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            _wgpu.QueueSubmit(_queue, 1, &commands);
            _wgpu.CommandBufferRelease(commands);
            _wgpu.CommandEncoderRelease(encoder);
            ReleaseSimulationBindGroups();
            _wgpu.BufferRelease(_state0);
            _wgpu.BufferRelease(_state1);
            _wgpu.BufferRelease(_instanceBuffer);
        }
        _state0 = state0;
        _state1 = state1;
        _stateIndex = 0;
        _instanceBuffer = instances;
        _capacity = capacity;
        if (SupportsGpuSimulation)
        {
            BindGroupLayout* computeLayout = _wgpu.ComputePipelineGetBindGroupLayout(_computePipeline, 0);
            BindGroupLayout* initLayout = _wgpu.ComputePipelineGetBindGroupLayout(_initializePipeline, 0);
            _computeBind0 = CreateSimulationBindGroup(computeLayout, _state0, _state1);
            _computeBind1 = CreateSimulationBindGroup(computeLayout, _state1, _state0);
            _initializeBind0 = CreateInitializationBindGroup(initLayout, _state1);
            _initializeBind1 = CreateInitializationBindGroup(initLayout, _state0);
            _wgpu.BindGroupLayoutRelease(computeLayout);
            _wgpu.BindGroupLayoutRelease(initLayout);
        }
    }

    private BindGroup* CreateSimulationBindGroup(BindGroupLayout* layout, WgpuBuffer* input, WgpuBuffer* output)
    {
        var entries = stackalloc BindGroupEntry[4];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = _simulationUniform, Size = 32 };
        entries[1] = new BindGroupEntry { Binding = 1, Buffer = input, Size = _capacity * (ulong)sizeof(GpuBird) };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = output, Size = _capacity * (ulong)sizeof(GpuBird) };
        entries[3] = new BindGroupEntry { Binding = 3, Buffer = _instanceBuffer, Size = _capacity * (ulong)sizeof(InstanceData) };
        var desc = new BindGroupDescriptor { Layout = layout, EntryCount = 4, Entries = entries };
        return _wgpu.DeviceCreateBindGroup(_device, &desc);
    }

    private BindGroup* CreateInitializationBindGroup(BindGroupLayout* layout, WgpuBuffer* output)
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = _simulationUniform, Size = 32 };
        entries[1] = new BindGroupEntry { Binding = 2, Buffer = output, Size = _capacity * (ulong)sizeof(GpuBird) };
        var desc = new BindGroupDescriptor { Layout = layout, EntryCount = 2, Entries = entries };
        return _wgpu.DeviceCreateBindGroup(_device, &desc);
    }

    private void ReleaseSimulationBindGroups()
    {
        if (_computeBind0 != null) _wgpu.BindGroupRelease(_computeBind0);
        if (_computeBind1 != null) _wgpu.BindGroupRelease(_computeBind1);
        if (_initializeBind0 != null) _wgpu.BindGroupRelease(_initializeBind0);
        if (_initializeBind1 != null) _wgpu.BindGroupRelease(_initializeBind1);
        _computeBind0 = _computeBind1 = _initializeBind0 = _initializeBind1 = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SimulationFrame
    {
        public uint Count;
        public float Delta;
        public uint Step;
        public uint FirstNewBird;
        public uint Seed;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }

    private void EncodeSimulation(CommandEncoder* encoder, int firstNewBird, int count, float dt, uint seed)
    {
        WgpuBuffer* input = _stateIndex == 0 ? _state0 : _state1;
        WgpuBuffer* output = _stateIndex == 0 ? _state1 : _state0;
        var frame = new SimulationFrame { Count = (uint)count, Delta = dt, Step = _simulationStep, FirstNewBird = (uint)firstNewBird, Seed = seed };
        _wgpu.QueueWriteBuffer(_queue, _simulationUniform, 0, &frame, (nuint)sizeof(SimulationFrame));
        if (firstNewBird < count)
        {
            ComputePassEncoder* initPass = _wgpu.CommandEncoderBeginComputePass(encoder, new ComputePassDescriptor());
            _wgpu.ComputePassEncoderSetPipeline(initPass, _initializePipeline);
            _wgpu.ComputePassEncoderSetBindGroup(initPass, 0, _stateIndex == 0 ? _initializeBind0 : _initializeBind1, 0, null);
            _wgpu.ComputePassEncoderDispatchWorkgroups(initPass, ((uint)(count - firstNewBird) + 63) / 64, 1, 1);
            _wgpu.ComputePassEncoderEnd(initPass);
            _wgpu.ComputePassEncoderRelease(initPass);
            ulong offset = (ulong)firstNewBird * (ulong)sizeof(GpuBird);
            _wgpu.CommandEncoderCopyBufferToBuffer(encoder, output, offset, input, offset, (ulong)(count - firstNewBird) * (ulong)sizeof(GpuBird));
        }
        ComputePassEncoder* pass = _wgpu.CommandEncoderBeginComputePass(encoder, new ComputePassDescriptor());
        _wgpu.ComputePassEncoderSetPipeline(pass, _computePipeline);
        _wgpu.ComputePassEncoderSetBindGroup(pass, 0, _stateIndex == 0 ? _computeBind0 : _computeBind1, 0, null);
        _wgpu.ComputePassEncoderDispatchWorkgroups(pass, ((uint)count + 63) / 64, 1, 1);
        _wgpu.ComputePassEncoderEnd(pass);
        _wgpu.ComputePassEncoderRelease(pass);
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

        var vertexAttributes = stackalloc VertexAttribute[3];
        vertexAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        vertexAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        vertexAttributes[2] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 24, ShaderLocation = 5 };
        var instanceAttributes = stackalloc VertexAttribute[4];
        instanceAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 2 };
        instanceAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 3 };
        instanceAttributes[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 24, ShaderLocation = 4 };
        instanceAttributes[3] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 36, ShaderLocation = 6 };

        var layouts = stackalloc VertexBufferLayout[2];
        layouts[0] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(Vertex), StepMode = VertexStepMode.Vertex, AttributeCount = 3, Attributes = vertexAttributes };
        layouts[1] = new VertexBufferLayout { ArrayStride = (ulong)sizeof(InstanceData), StepMode = VertexStepMode.Instance, AttributeCount = 4, Attributes = instanceAttributes };

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
        RenderCore(birds, 0, birds.Length, 0f, 0, false, in viewProj, width, height);
    }

    public void RenderSimulation(int firstNewBird, int count, float dt, uint seed,
        in Matrix4x4 viewProj, int width, int height)
    {
        RenderCore(default, firstNewBird, count, dt, seed, true, in viewProj, width, height);
    }

    private void RenderCore(ReadOnlySpan<InstanceData> birds, int firstNewBird,
        int count, float dt, uint seed, bool simulate, in Matrix4x4 viewProj, int width, int height)
    {
        if (Volatile.Read(ref _gpuFailure) is { } failure) throw new InvalidOperationException(failure);
        EnsureCapacity(count);
        if (_depthView == null || width != _width || height != _height) Resize(width, height);

        CommandEncoder* encoder = _wgpu.DeviceCreateCommandEncoder(_device, new CommandEncoderDescriptor());
        if (simulate) EncodeSimulation(encoder, firstNewBird, count, dt, seed);
        else Write(_instanceBuffer, birds);

        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        if (surfaceTexture.Texture == null)
        {
            _width = 0;
            Submit(encoder, simulate, dt);
            return;
        }
        TextureView* view = _wgpu.TextureCreateView(surfaceTexture.Texture, null);

        Matrix4x4 matrix = viewProj;
        _wgpu.QueueWriteBuffer(_queue, _uniformBuffer, 0, &matrix, 64);

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
        _wgpu.RenderPassEncoderDraw(pass, _birdVertexCount, (uint)count, 0, 0);
        _wgpu.RenderPassEncoderEnd(pass);

        Submit(encoder, simulate, dt);
        _wgpu.SurfacePresent(_surface);

        _wgpu.RenderPassEncoderRelease(pass);
        _wgpu.TextureViewRelease(view);
        _wgpu.TextureRelease(surfaceTexture.Texture);
    }

    private void Submit(CommandEncoder* encoder, bool simulate, float dt)
    {
        CommandBuffer* commands = _wgpu.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
        _wgpu.QueueSubmit(_queue, 1, &commands);
        _wgpu.CommandBufferRelease(commands);
        _wgpu.CommandEncoderRelease(encoder);
        if (simulate)
        {
            _stateIndex = 1 - _stateIndex;
            if (dt > 0f) _simulationStep++;
        }
    }

    public void Dispose()
    {
        var poll = (delegate* unmanaged[Cdecl]<Device*, uint, void*, uint>)NativeLibrary.GetExport(_preloadedNativeLibrary, "wgpuDevicePoll");
        poll(_device, 1, null);
        ReleaseDepth();
        ReleaseSimulationBindGroups();
        _wgpu.BindGroupRelease(_bindGroup);
        _wgpu.RenderPipelineRelease(_pipeline);
        if (_computePipeline != null) _wgpu.ComputePipelineRelease(_computePipeline);
        if (_initializePipeline != null) _wgpu.ComputePipelineRelease(_initializePipeline);
        _wgpu.BufferRelease(_simulationUniform);
        _wgpu.BufferRelease(_groundInstanceBuffer);
        if (_instanceBuffer != null) _wgpu.BufferRelease(_instanceBuffer);
        if (_state0 != null) _wgpu.BufferRelease(_state0);
        if (_state1 != null) _wgpu.BufferRelease(_state1);
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
