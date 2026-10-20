const VERTEX_STRIDE = 24;
const INSTANCE_STRIDE = 36;

let device, context, canvas, format, pipeline, bindGroup;
let uniformBuffer, birdBuffer, groundBuffer, instanceBuffer, groundInstanceBuffer;
let depthTexture, depthView, birdVertexCount, maxInstances;

export async function initDevice(canvasId) {
    if (!navigator.gpu) throw new Error('WebGPU is not available in this browser.');
    const adapter = await navigator.gpu.requestAdapter();
    device = await adapter.requestDevice();

    canvas = document.getElementById(canvasId);
    context = canvas.getContext('webgpu');
    format = navigator.gpu.getPreferredCanvasFormat();
    context.configure({ device, format, alphaMode: 'opaque' });
}

// The byte views passed in are .NET MemoryViews over WASM memory, valid only during the call, so they are copied immediately.
export function createResources(shaderSource, birdBytes, groundBytes, groundInstanceBytes, maxInstanceCount) {
    maxInstances = maxInstanceCount;
    birdVertexCount = birdBytes.byteLength / VERTEX_STRIDE;

    uniformBuffer = device.createBuffer({ size: 64, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    birdBuffer = uploaded(birdBytes, GPUBufferUsage.VERTEX);
    groundBuffer = uploaded(groundBytes, GPUBufferUsage.VERTEX);
    groundInstanceBuffer = uploaded(groundInstanceBytes, GPUBufferUsage.VERTEX);
    instanceBuffer = device.createBuffer({ size: maxInstances * INSTANCE_STRIDE, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST });

    const module = device.createShaderModule({ code: shaderSource });
    pipeline = device.createRenderPipeline({
        layout: 'auto',
        vertex: {
            module,
            entryPoint: 'vs',
            buffers: [
                {
                    arrayStride: VERTEX_STRIDE,
                    attributes: [
                        { shaderLocation: 0, offset: 0, format: 'float32x3' },
                        { shaderLocation: 1, offset: 12, format: 'float32x3' },
                    ],
                },
                {
                    arrayStride: INSTANCE_STRIDE,
                    stepMode: 'instance',
                    attributes: [
                        { shaderLocation: 2, offset: 0, format: 'float32x3' },
                        { shaderLocation: 3, offset: 12, format: 'float32x3' },
                        { shaderLocation: 4, offset: 24, format: 'float32x3' },
                    ],
                },
            ],
        },
        fragment: { module, entryPoint: 'fs', targets: [{ format }] },
        primitive: { topology: 'triangle-list', cullMode: 'none' },
        depthStencil: { format: 'depth32float', depthWriteEnabled: true, depthCompare: 'less' },
    });
    bindGroup = device.createBindGroup({
        layout: pipeline.getBindGroupLayout(0),
        entries: [{ binding: 0, resource: { buffer: uniformBuffer } }],
    });
}

function uploaded(view, usage) {
    const bytes = view.slice();
    const buffer = device.createBuffer({ size: bytes.byteLength, usage: usage | GPUBufferUsage.COPY_DST });
    device.queue.writeBuffer(buffer, 0, bytes);
    return buffer;
}

// Matches the canvas backing store to its CSS size in physical pixels.
export function syncSize() {
    const dpr = window.devicePixelRatio || 1;
    const w = Math.max(1, Math.floor(canvas.clientWidth * dpr));
    const h = Math.max(1, Math.floor(canvas.clientHeight * dpr));
    if (canvas.width !== w || canvas.height !== h) {
        canvas.width = w;
        canvas.height = h;
    }
}

export function getWidth() { return canvas.width; }
export function getHeight() { return canvas.height; }

export function render(instanceBytes, viewProjBytes, instanceCount) {
    if (instanceCount > maxInstances) throw new Error(`At most ${maxInstances} birds are supported.`);

    if (!depthTexture || depthTexture.width !== canvas.width || depthTexture.height !== canvas.height) {
        depthTexture?.destroy();
        depthTexture = device.createTexture({
            size: [canvas.width, canvas.height],
            format: 'depth32float',
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        });
        depthView = depthTexture.createView();
    }

    device.queue.writeBuffer(uniformBuffer, 0, viewProjBytes.slice());
    device.queue.writeBuffer(instanceBuffer, 0, instanceBytes.slice());

    const encoder = device.createCommandEncoder();
    const pass = encoder.beginRenderPass({
        colorAttachments: [{
            view: context.getCurrentTexture().createView(),
            clearValue: { r: 0, g: 0, b: 0, a: 1 },
            loadOp: 'clear',
            storeOp: 'store',
        }],
        depthStencilAttachment: { view: depthView, depthClearValue: 1, depthLoadOp: 'clear', depthStoreOp: 'store' },
    });
    pass.setPipeline(pipeline);
    pass.setBindGroup(0, bindGroup);

    pass.setVertexBuffer(0, groundBuffer);
    pass.setVertexBuffer(1, groundInstanceBuffer);
    pass.draw(6, 1);

    pass.setVertexBuffer(0, birdBuffer);
    pass.setVertexBuffer(1, instanceBuffer);
    pass.draw(birdVertexCount, instanceCount);
    pass.end();
    device.queue.submit([encoder.finish()]);
}

export function requestFrame(callback) {
    requestAnimationFrame(callback);
}
