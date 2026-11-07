using System.Runtime.InteropServices;
using Murmuration.Desktop;
using NUnit.Framework;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Murmuration.Desktop.Tests;

public unsafe class GpuShaderTests
{
    private static readonly Lazy<IntPtr> NativeLibraryHandle = new(WebGpuNativeLibrary.Load);

    [Test]
    public void DesktopRendererRunsSimulationAndGrowsBuffers()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The desktop WebGPU renderer requires Windows.");

        IntPtr window = CreateWindowExW(0, "STATIC", "", 0x00CF0000, 0, 0, 320, 240,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
        try
        {
            WebGpuDesktopRenderer renderer;
            try { renderer = new WebGpuDesktopRenderer(window); }
            catch (InvalidOperationException exception) when (exception.Message == "No compatible WebGPU adapter is available.")
            {
                Assert.Ignore(exception.Message);
                return;
            }
            using (renderer)
            {
                Assert.That(renderer.SupportsGpuSimulation, Is.True);
                var matrix = System.Numerics.Matrix4x4.Identity;
                renderer.RenderSimulation(0, 100, 0f, 42, in matrix, 320, 240);
                renderer.RenderSimulation(100, 150, 1f / 60f, 42, in matrix, 320, 240);
                renderer.RenderSimulation(150, 150, 1f / 60f, 42, in matrix, 320, 240);
            }
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    [TestCase(0f, false, 66)]
    [TestCase(1f / 60f, false, 66)]
    [TestCase(1f / 60f, true, 66)]
    [TestCase(1f / 60f, false, 400)]
    [TestCase(1f / 60f, false, 1024)]
    public void SimulationShaderMatchesCpuForOneStep(float delta, bool dense, int count)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The native WebGPU package is Windows-specific for this test.");

        IntPtr library = NativeLibraryHandle.Value;
        {
            using var gpu = WebGPU.GetApi();
            var instanceDescriptor = new InstanceDescriptor();
            Instance* instance = gpu.CreateInstance(&instanceDescriptor);
            Adapter* adapter = null;
            var adapterOptions = new RequestAdapterOptions();
            gpu.InstanceRequestAdapter(instance, &adapterOptions,
                new PfnRequestAdapterCallback((status, result, message, data) => adapter = result), null);
            if (adapter == null)
            {
                gpu.InstanceRelease(instance);
                Assert.Ignore("No WebGPU adapter is available.");
            }

            Device* device = null;
            var deviceDescriptor = new DeviceDescriptor();
            gpu.AdapterRequestDevice(adapter, &deviceDescriptor,
                new PfnRequestDeviceCallback((status, result, message, data) => device = result), null);
            Assert.That((nint)device, Is.Not.EqualTo(IntPtr.Zero));

            string? error = null;
            bool completed = false;
            gpu.DevicePushErrorScope(device, ErrorFilter.Validation);
            var code = (byte*)SilkMarshal.StringToPtr(ShaderSources.Simulation);
            var shader = new ShaderModuleWGSLDescriptor
            {
                Code = code,
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
            };
            var moduleDescriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&shader };
            ShaderModule* module = gpu.DeviceCreateShaderModule(device, &moduleDescriptor);
            var entry = (byte*)SilkMarshal.StringToPtr("simulate");
            var pipelineDescriptor = new ComputePipelineDescriptor
            {
                Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = entry },
            };
            ComputePipeline* pipeline = gpu.DeviceCreateComputePipeline(device, &pipelineDescriptor);
            var callback = new PfnErrorCallback((type, message, data) =>
            {
                if (type != ErrorType.NoError)
                    error = Marshal.PtrToStringUTF8((IntPtr)message);
                completed = true;
            });
            gpu.DevicePopErrorScope(device, callback, null);
            var poll = (delegate* unmanaged[Cdecl]<Device*, uint, void*, uint>)NativeLibrary.GetExport(library, "wgpuDevicePoll");
            poll(device, 1, null);

            Assert.That(completed, Is.True, "The WebGPU validation callback did not run.");
            Assert.That(error, Is.Null);
            Assert.That((nint)pipeline, Is.Not.EqualTo(IntPtr.Zero));

            var reference = new Simulation(count, 42);
            var birds = reference.Birds;
            if (dense)
            {
                for (int index = 0; index < birds.Length; index++)
                {
                    birds[index].Position = new System.Numerics.Vector3(index * 0.2f, (index % 3) * 0.1f, 25f);
                    birds[index].Orientation = System.Numerics.Quaternion.Identity;
                    birds[index].DesiredDirection = System.Numerics.Vector3.UnitX;
                }
            }
            GpuBird* input = stackalloc GpuBird[birds.Length];
            for (int index = 0; index < birds.Length; index++)
                input[index] = GpuBird.FromBird(birds[index]);

            var frameDescriptor = new BufferDescriptor { Size = 32, Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
            var stateDescriptor = new BufferDescriptor
            {
                Size = (ulong)(birds.Length * sizeof(GpuBird)),
                Usage = BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc,
            };
            var instanceBufferDescriptor = new BufferDescriptor
            {
                Size = (ulong)(birds.Length * sizeof(InstanceData)),
                Usage = BufferUsage.Storage | BufferUsage.CopySrc,
            };
            var readbackDescriptor = new BufferDescriptor
            {
                Size = instanceBufferDescriptor.Size,
                Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
            };
            GpuBuffer* frame = gpu.DeviceCreateBuffer(device, &frameDescriptor);
            GpuBuffer* stateIn = gpu.DeviceCreateBuffer(device, &stateDescriptor);
            GpuBuffer* stateOut = gpu.DeviceCreateBuffer(device, &stateDescriptor);
            GpuBuffer* instances = gpu.DeviceCreateBuffer(device, &instanceBufferDescriptor);
            GpuBuffer* readback = gpu.DeviceCreateBuffer(device, &readbackDescriptor);
            var stateReadbackDescriptor = new BufferDescriptor { Size = stateDescriptor.Size, Usage = BufferUsage.MapRead | BufferUsage.CopyDst };
            GpuBuffer* stateReadback = gpu.DeviceCreateBuffer(device, &stateReadbackDescriptor);
            Queue* queue = gpu.DeviceGetQueue(device);
            uint* parameters = stackalloc uint[8] { (uint)birds.Length, BitConverter.SingleToUInt32Bits(delta), 0, 0, 42, 0, 0, 0 };
            gpu.QueueWriteBuffer(queue, frame, 0, parameters, 32);
            gpu.QueueWriteBuffer(queue, stateIn, 0, input, (nuint)stateDescriptor.Size);

            BindGroupLayout* layout = gpu.ComputePipelineGetBindGroupLayout(pipeline, 0);
            BindGroupEntry* entries = stackalloc BindGroupEntry[4];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = frame, Size = frameDescriptor.Size };
            entries[1] = new BindGroupEntry { Binding = 1, Buffer = stateIn, Size = stateDescriptor.Size };
            entries[2] = new BindGroupEntry { Binding = 2, Buffer = stateOut, Size = stateDescriptor.Size };
            entries[3] = new BindGroupEntry { Binding = 3, Buffer = instances, Size = instanceBufferDescriptor.Size };
            var groupDescriptor = new BindGroupDescriptor { Layout = layout, EntryCount = 4, Entries = entries };
            BindGroup* group = gpu.DeviceCreateBindGroup(device, &groupDescriptor);
            var encoderDescriptor = new CommandEncoderDescriptor();
            CommandEncoder* encoder = gpu.DeviceCreateCommandEncoder(device, &encoderDescriptor);
            var passDescriptor = new ComputePassDescriptor();
            ComputePassEncoder* pass = gpu.CommandEncoderBeginComputePass(encoder, &passDescriptor);
            gpu.ComputePassEncoderSetPipeline(pass, pipeline);
            gpu.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
            gpu.ComputePassEncoderDispatchWorkgroups(pass, ((uint)birds.Length + 63) / 64, 1, 1);
            gpu.ComputePassEncoderEnd(pass);
            gpu.CommandEncoderCopyBufferToBuffer(encoder, instances, 0, readback, 0, instanceBufferDescriptor.Size);
            gpu.CommandEncoderCopyBufferToBuffer(encoder, stateOut, 0, stateReadback, 0, stateDescriptor.Size);
            var commandDescriptor = new CommandBufferDescriptor();
            CommandBuffer* commands = gpu.CommandEncoderFinish(encoder, &commandDescriptor);
            gpu.QueueSubmit(queue, 1, &commands);

            bool mapped = false;
            bool stateMapped = false;
            var mapCallback = new PfnBufferMapCallback((status, data) => mapped = status == BufferMapAsyncStatus.Success);
            var stateMapCallback = new PfnBufferMapCallback((status, data) => stateMapped = status == BufferMapAsyncStatus.Success);
            gpu.BufferMapAsync(readback, MapMode.Read, 0, (nuint)readbackDescriptor.Size, mapCallback, null);
            gpu.BufferMapAsync(stateReadback, MapMode.Read, 0, (nuint)stateReadbackDescriptor.Size, stateMapCallback, null);
            poll(device, 1, null);
            Assert.That(mapped, Is.True, "GPU instance buffer could not be mapped.");
            Assert.That(stateMapped, Is.True, "GPU state buffer could not be mapped.");
            if (delta > 0) reference.Step(delta, 60);
            var result = (InstanceData*)gpu.BufferGetConstMappedRange(readback, 0, (nuint)readbackDescriptor.Size);
            var state = (GpuBird*)gpu.BufferGetConstMappedRange(stateReadback, 0, (nuint)stateReadbackDescriptor.Size);
            for (int index = 0; index < birds.Length; index++)
            {
                Assert.That(System.Numerics.Vector3.Distance(result[index].Position, birds[index].Position), Is.LessThan(1e-4f), $"Position {index}");
                Assert.That(System.Numerics.Vector3.Distance(result[index].Direction, birds[index].Direction), Is.LessThan(1e-4f), $"Direction {index}");
                Assert.That(System.Numerics.Vector3.Distance(result[index].Up, birds[index].Up), Is.LessThan(1e-4f), $"Up {index}");
                Bird actual = state[index].ToBird();
                Assert.That(System.Numerics.Vector3.Distance(actual.DesiredDirection, birds[index].DesiredDirection), Is.LessThan(1e-4f), $"Desired direction {index}");
                Assert.That(actual.Speed, Is.EqualTo(birds[index].Speed).Within(1e-4f), $"Speed {index}");
            }
            gpu.BufferUnmap(readback);
            gpu.BufferUnmap(stateReadback);
            gpu.CommandBufferRelease(commands);
            gpu.ComputePassEncoderRelease(pass);
            gpu.CommandEncoderRelease(encoder);
            gpu.BindGroupRelease(group);
            gpu.BindGroupLayoutRelease(layout);

            gpu.DevicePushErrorScope(device, ErrorFilter.Validation);
            var initEntry = (byte*)SilkMarshal.StringToPtr("initialize");
            var initDescriptor = new ComputePipelineDescriptor
            {
                Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = initEntry },
            };
            ComputePipeline* initPipeline = gpu.DeviceCreateComputePipeline(device, &initDescriptor);
            completed = false;
            error = null;
            gpu.DevicePopErrorScope(device, callback, null);
            poll(device, 1, null);
            Assert.That(completed, Is.True);
            Assert.That(error, Is.Null);
            BindGroupLayout* initLayout = gpu.ComputePipelineGetBindGroupLayout(initPipeline, 0);
            BindGroupEntry* initEntries = stackalloc BindGroupEntry[2];
            initEntries[0] = new BindGroupEntry { Binding = 0, Buffer = frame, Size = frameDescriptor.Size };
            initEntries[1] = new BindGroupEntry { Binding = 2, Buffer = stateOut, Size = stateDescriptor.Size };
            var initGroupDescriptor = new BindGroupDescriptor { Layout = initLayout, EntryCount = 2, Entries = initEntries };
            BindGroup* initGroup = gpu.DeviceCreateBindGroup(device, &initGroupDescriptor);
            var initialPositions = new System.Numerics.Vector3[birds.Length];
            bool differentSeedChangedPositions = false;
            for (int phase = 0; phase < 4; phase++)
            {
                parameters[2] = phase == 1 ? 1u : 0u;
                parameters[3] = phase == 1 ? 32u : 0u;
                parameters[4] = phase == 3 ? 43u : 42u;
                gpu.QueueWriteBuffer(queue, frame, 0, parameters, 32);
                encoder = gpu.DeviceCreateCommandEncoder(device, &encoderDescriptor);
                pass = gpu.CommandEncoderBeginComputePass(encoder, &passDescriptor);
                gpu.ComputePassEncoderSetPipeline(pass, initPipeline);
                gpu.ComputePassEncoderSetBindGroup(pass, 0, initGroup, 0, null);
                gpu.ComputePassEncoderDispatchWorkgroups(pass, ((uint)birds.Length - parameters[3] + 63) / 64, 1, 1);
                gpu.ComputePassEncoderEnd(pass);
                gpu.CommandEncoderCopyBufferToBuffer(encoder, stateOut, 0, stateReadback, 0, stateDescriptor.Size);
                commands = gpu.CommandEncoderFinish(encoder, &commandDescriptor);
                gpu.QueueSubmit(queue, 1, &commands);
                stateMapped = false;
                gpu.BufferMapAsync(stateReadback, MapMode.Read, 0, (nuint)stateReadbackDescriptor.Size, stateMapCallback, null);
                poll(device, 1, null);
                Assert.That(stateMapped, Is.True);
                state = (GpuBird*)gpu.BufferGetConstMappedRange(stateReadback, 0, (nuint)stateReadbackDescriptor.Size);
                if (phase == 1)
                    for (int index = 0; index < 32; index++)
                        Assert.That(state[index].ToBird().Position, Is.EqualTo(initialPositions[index]));
                for (int index = (int)parameters[3]; index < birds.Length; index++)
                {
                    Bird initialized = state[index].ToBird();
                    Assert.That(initialized.Position.X, Is.InRange(phase == 1 ? 30f : -50f, 50f));
                    Assert.That(initialized.Position.Y, Is.InRange(phase == 1 ? 40f : -50f, 50f));
                    Assert.That(initialized.Position.Z, Is.InRange(phase == 1 ? 0f : 40f, phase == 1 ? 0f : 50f));
                    Assert.That(initialized.Speed, Is.InRange(3f, 3.25f));
                    Assert.That(initialized.Mass, Is.InRange(0.25f, 0.5f));
                    Assert.That(initialized.ClimbStrength, Is.InRange(0.7f, 1.2f));
                    Assert.That(System.Numerics.Vector3.Distance(initialized.Direction, initialized.DesiredDirection), Is.LessThan(1e-4f));
                    Assert.That(initialized.Up.Length(), Is.EqualTo(1f).Within(1e-4f));
                    if (phase == 0) initialPositions[index] = initialized.Position;
                    if (phase == 2) Assert.That(initialized.Position, Is.EqualTo(initialPositions[index]));
                    if (phase == 3 && initialized.Position != initialPositions[index]) differentSeedChangedPositions = true;
                }
                gpu.BufferUnmap(stateReadback);
                gpu.CommandBufferRelease(commands);
                gpu.ComputePassEncoderRelease(pass);
                gpu.CommandEncoderRelease(encoder);
            }
            Assert.That(differentSeedChangedPositions, Is.True);
            gpu.BindGroupRelease(initGroup);
            gpu.BindGroupLayoutRelease(initLayout);
            gpu.ComputePipelineRelease(initPipeline);
            SilkMarshal.Free((nint)initEntry);
            bool benchmark = Environment.GetEnvironmentVariable("MURMURATION_BENCHMARK") == "1" && count >= 400;
            if ((count == 66 && !dense && delta > 0f) || benchmark)
            {
                layout = gpu.ComputePipelineGetBindGroupLayout(pipeline, 0);
                GpuBuffer* current = stateOut;
                GpuBuffer* next = stateIn;
                parameters[1] = BitConverter.SingleToUInt32Bits(1f / 60f);
                parameters[3] = (uint)count;
                groupDescriptor.Layout = layout;
                entries[1].Buffer = stateOut;
                entries[2].Buffer = stateIn;
                BindGroup* forwardGroup = gpu.DeviceCreateBindGroup(device, &groupDescriptor);
                entries[1].Buffer = stateIn;
                entries[2].Buffer = stateOut;
                BindGroup* reverseGroup = gpu.DeviceCreateBindGroup(device, &groupDescriptor);
                var gpuTimer = System.Diagnostics.Stopwatch.StartNew();
                for (uint frameIndex = 0; frameIndex < 120; frameIndex++)
                {
                    parameters[2] = frameIndex;
                    gpu.QueueWriteBuffer(queue, frame, 0, parameters, 32);
                    encoder = gpu.DeviceCreateCommandEncoder(device, &encoderDescriptor);
                    pass = gpu.CommandEncoderBeginComputePass(encoder, &passDescriptor);
                    gpu.ComputePassEncoderSetPipeline(pass, pipeline);
                    gpu.ComputePassEncoderSetBindGroup(pass, 0, current == stateOut ? forwardGroup : reverseGroup, 0, null);
                    gpu.ComputePassEncoderDispatchWorkgroups(pass, ((uint)count + 63) / 64, 1, 1);
                    gpu.ComputePassEncoderEnd(pass);
                    commands = gpu.CommandEncoderFinish(encoder, &commandDescriptor);
                    gpu.QueueSubmit(queue, 1, &commands);
                    gpu.CommandBufferRelease(commands);
                    gpu.ComputePassEncoderRelease(pass);
                    gpu.CommandEncoderRelease(encoder);
                    GpuBuffer* previous = current;
                    current = next;
                    next = previous;
                }
                encoder = gpu.DeviceCreateCommandEncoder(device, &encoderDescriptor);
                gpu.CommandEncoderCopyBufferToBuffer(encoder, current, 0, stateReadback, 0, stateDescriptor.Size);
                commands = gpu.CommandEncoderFinish(encoder, &commandDescriptor);
                gpu.QueueSubmit(queue, 1, &commands);
                stateMapped = false;
                gpu.BufferMapAsync(stateReadback, MapMode.Read, 0, (nuint)stateReadbackDescriptor.Size, stateMapCallback, null);
                poll(device, 1, null);
                gpuTimer.Stop();
                Assert.That(stateMapped, Is.True);
                state = (GpuBird*)gpu.BufferGetConstMappedRange(stateReadback, 0, (nuint)stateReadbackDescriptor.Size);
                for (int index = 0; index < count; index++)
                {
                    Bird actual = state[index].ToBird();
                    Assert.That(float.IsFinite(actual.Position.X) && float.IsFinite(actual.Position.Y) && float.IsFinite(actual.Position.Z), Is.True);
                    Assert.That(actual.Speed, Is.InRange(2.5f, 5f));
                    Assert.That(actual.Orientation.Length(), Is.EqualTo(1f).Within(1e-4f));
                    Assert.That(actual.Direction.Z, Is.InRange(-0.708f, 0.501f));
                }
                gpu.BufferUnmap(stateReadback);
                gpu.CommandBufferRelease(commands);
                gpu.CommandEncoderRelease(encoder);
                gpu.BindGroupRelease(forwardGroup);
                gpu.BindGroupRelease(reverseGroup);
                gpu.BindGroupLayoutRelease(layout);
                if (benchmark)
                {
                    var cpu = new Simulation(count, 43);
                    cpu.Step(1f / 60f, 59);
                    var cpuTimer = System.Diagnostics.Stopwatch.StartNew();
                    for (int frameIndex = 0; frameIndex < 120; frameIndex++)
                        cpu.Step(1f / 60f, 59);
                    cpuTimer.Stop();
                    TestContext.Progress.WriteLine($"{count} birds, 120 frames: GPU dispatch and final readback {gpuTimer.Elapsed.TotalMilliseconds:F1} ms; CPU {cpuTimer.Elapsed.TotalMilliseconds:F1} ms.");
                }
            }
            gpu.QueueRelease(queue);
            gpu.BufferRelease(readback);
            gpu.BufferRelease(stateReadback);
            gpu.BufferRelease(instances);
            gpu.BufferRelease(stateOut);
            gpu.BufferRelease(stateIn);
            gpu.BufferRelease(frame);

            gpu.ComputePipelineRelease(pipeline);
            gpu.ShaderModuleRelease(module);
            SilkMarshal.Free((nint)entry);
            SilkMarshal.Free((nint)code);
            gpu.DeviceRelease(device);
            gpu.AdapterRelease(adapter);
            gpu.InstanceRelease(instance);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
}