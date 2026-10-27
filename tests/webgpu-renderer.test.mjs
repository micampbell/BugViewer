import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

// Execute the actual renderer in an isolated module; only browser/GPU endpoints
// are substituted. The appended hooks are test-only and never ship in the RCL.
async function harness() {
    let fail = false, pending = null, lose;
    const counters = { pipelines: 0, shaders: 0, layouts: 0, buffers: [], submissions: 0 };
    const resource = () => ({ destroy() { this.destroyed = true; }, createView() { return {}; } });
    const device = {
        lost: new Promise(resolve => { lose = resolve; }),
        createBuffer({ size }) { const buffer = { ...resource(), data: new ArrayBuffer(size),
            getMappedRange() { return this.data; }, unmap() {} }; counters.buffers.push(buffer); return buffer; },
        createBindGroupLayout(descriptor) { counters.layouts++; return { descriptor }; },
        createPipelineLayout: descriptor => ({ descriptor }),
        createBindGroup: descriptor => ({ descriptor }),
        createShaderModule(descriptor) { counters.shaders++; return { descriptor }; },
        async createRenderPipelineAsync(descriptor) {
            counters.pipelines++;
            if (pending) await pending;
            if (fail) { fail = false; throw new Error('pipeline failed'); }
            return { descriptor };
        },
        createTexture: resource, createSampler: () => ({}), destroy() {},
        createCommandEncoder: () => ({ beginRenderPass: () => new Proxy({}, { get: () => () => {} }), end() {}, finish: () => ({}) }),
        queue: { writeBuffer() {}, copyExternalImageToTexture() {},
            submit() { counters.submissions++; }, onSubmittedWorkDone: async () => {} }
    };
    const context = vm.createContext({ console, performance, setTimeout, clearTimeout,
        setInterval: () => 1, clearInterval() {}, requestAnimationFrame: () => 1, cancelAnimationFrame() {},
        Float32Array, Uint8Array, Uint16Array, Uint32Array, ArrayBuffer,
        GPUBufferUsage: { VERTEX: 1, INDEX: 2, UNIFORM: 4, COPY_DST: 8 },
        GPUShaderStage: { VERTEX: 1, FRAGMENT: 2 },
        GPUTextureUsage: { RENDER_ATTACHMENT: 1, TEXTURE_BINDING: 2, COPY_DST: 4 },
        navigator: { userAgent: 'test', gpu: { requestAdapter: async () => ({ features: new Set(), requestDevice: async () => device }) } },
        document: { createElement: () => ({ width: 0, height: 0, getContext: () => ({ measureText: () => ({ width: 20 }), fillText() {}, fillRect() {} }) }) },
        createImageBitmap: async () => ({ close() {} })
    });
    let source = await readFile(new URL('../BugViewer/wwwroot/js/webgpu-canvas.js', import.meta.url), 'utf8');
    source += `
        export const testing = {
            setDevice(value) { device = value; isDisposing = false; clearMeshPipelineCache();
                frameBindGroupLayout = {}; lightBindGroupLayout = {}; lightUniformBuffer = { destroy() {} }; },
            getMeshPipeline, decodeBinaryScene, renderFrame,
            state: () => ({ meshes, lines, textBillboards, cacheSize: meshPipelines.size }),
            settings(samples, color, depth) { sampleCount = samples; colorFormat = color; depthFormat = depth; },
            async initialize() { isDisposing = false; context = { configure() {}, unconfigure() {} }; await initWebGPU(); },
            renderTarget() { context = { getCurrentTexture: () => ({ createView: () => ({}) }) };
                colorAttachment = {}; renderPassDescriptor = {}; },
            lose: () => device.destroy()
        };`;
    const module = new vm.SourceTextModule(source, { context });
    await module.link(() => new vm.SyntheticModule(['createViewCubeController'], function() {
        this.setExport('createViewCubeController', () => ({ initialize() {}, updateSize() {}, updateOptions() {}, updateOrientation() {}, dispose() {} }));
    }, { context }));
    await module.evaluate();
    const api = module.namespace;
    api.testing.setDevice(device);
    return { api, device, counters, fail: () => { fail = true; },
        hold: () => { let release; pending = new Promise(resolve => { release = () => { pending = null; resolve(); }; }); return release; },
        lose: async () => { lose({ message: 'test loss' }); await Promise.resolve(); await Promise.resolve(); } };
}
const mesh = (id, singleColor = true, alpha = 1) => ({ id, vertices: [0,0,0, 1,0,0, 0,1,0],
    indices: [0,1,2], colors: singleColor ? [1,0,0,alpha] : [1,0,0,alpha, 0,1,0,alpha, 0,0,1,alpha],
    primitiveSurfaceNormals: [], singleColor });
{
    const { api, counters } = await harness();
    await api.addMeshes(Array.from({ length: 512 }, (_, i) => mesh(String(i))));
    assert.equal(counters.pipelines, 1);
    assert.equal(counters.shaders, 1);
    const retained = api.testing.state().meshes[0];
    const removed = api.testing.state().meshes[511];
    assert.equal(retained.pipeline, removed.pipeline);
    assert.notEqual(retained.colorBuffer, removed.colorBuffer);
    assert.equal(retained.bindGroup.descriptor.layout, removed.bindGroup.descriptor.layout);
    await api.removeMeshes(['511']);
    await api.addMesh(mesh('replacement'));
    assert.equal(api.testing.state().meshes[0], retained);
    assert.equal(removed.vertexBuffer.destroyed, true);
    assert.equal(counters.pipelines, 1);
    for (const [single, alpha] of [[true,.5], [false,1], [false,.5]]) await api.addMesh(mesh(`${single}-${alpha}`, single, alpha));
    assert.equal(counters.pipelines, 4);
    assert.equal(counters.shaders, 2);
    assert.equal(api.testing.state().meshes.at(-1).pipeline.descriptor.depthStencil.depthWriteEnabled, false);
    const p1 = api.testing.getMeshPipeline(true, false), p2 = api.testing.getMeshPipeline(true, false);
    assert.equal(p1, p2);
    api.testing.settings(1, 'rgba8unorm', 'depth32float');
    await api.testing.getMeshPipeline(true, false);
    assert.equal(counters.pipelines, 5);
    api.testing.setDevice((await harness()).device);
    assert.equal(api.testing.state().cacheSize, 0);
    await api.disposeWebGPU_Canvas();
    assert.ok(counters.buffers.every(buffer => buffer.destroyed));
}
{
    const { api, counters, fail } = await harness();
    fail();
    await assert.rejects(api.addMesh(mesh('failed')), /pipeline failed/);
    assert.equal(api.testing.state().cacheSize, 0);
    assert.ok(counters.buffers.every(buffer => buffer.destroyed));
    await api.addMesh(mesh('retry'));
    assert.equal(counters.pipelines, 2);
    assert.equal(api.testing.state().meshes.length, 1);
    await api.disposeWebGPU_Canvas();
}
{
    const { api, counters, hold } = await harness();
    const release = hold();
    const upload = api.addMesh(mesh('interrupted'));
    await Promise.resolve(); await Promise.resolve();
    const disposal = api.disposeWebGPU_Canvas();
    release();
    await assert.rejects(upload, /interrupted/);
    await disposal;
    assert.ok(counters.buffers.every(buffer => buffer.destroyed));
    assert.equal(api.testing.state().cacheSize, 0);
}
{
    const { api, fail, lose } = await harness();
    fail();
    await assert.rejects(api.testing.initialize(), /pipeline failed/);
    await api.disposeWebGPU_Canvas();
    await api.testing.initialize();
    await api.addMesh(mesh('recovered'));
    await lose();
    assert.equal(api.testing.state().cacheSize, 0);
    await assert.rejects(api.addMesh(mesh('lost')), /unavailable/);
    await api.disposeWebGPU_Canvas();
}
{
    const { api } = await harness();
    const bytes = new Uint8Array(80);
    new Float32Array(bytes.buffer, 0, 9).set([0,0,0, 1,0,0, 0,1,0]);
    new Float32Array(bytes.buffer, 36, 4).set([1,0,0,.5]);
    new Uint16Array(bytes.buffer, 52, 3).set([0,1,2]);
    const metadata = { meshes: [{ id: 'binary', vertexOffset: 0, vertexCount: 9, colorOffset: 36, colorCount: 4,
        normalOffset: 52, normalCount: 0, indexOffset: 52, indexCount: 3, index32: false, singleColor: true }], lines: [] };
    const decoded = api.testing.decodeBinaryScene(metadata, bytes);
    assert.equal(decoded.meshes[0].vertices.buffer, bytes.buffer);
    assert.deepEqual([...decoded.meshes[0].indices], [0,1,2]);
    await api.addSceneBinary(metadata, bytes);
    assert.equal(api.testing.state().meshes[0].indexFormat, 'uint16');
    assert.equal(api.testing.state().meshes[0].isTransparent, true);
    const unaligned = new Uint8Array(bytes.length + 1);
    unaligned.set(bytes, 1);
    assert.deepEqual([...api.testing.decodeBinaryScene(metadata, unaligned.subarray(1)).meshes[0].vertices],
        [...decoded.meshes[0].vertices]);
    metadata.meshes[0].indexCount = 1000;
    assert.throws(() => api.testing.decodeBinaryScene(metadata, bytes), /Invalid/);
    metadata.meshes[0].indexCount = 3; metadata.meshes[0].vertexOffset = 2;
    assert.throws(() => api.testing.decodeBinaryScene(metadata, bytes), /Invalid/);
    await api.disposeWebGPU_Canvas();
}
{
    const { api, counters } = await harness();
    await api.configureBenchmark(false);
    await api.addMeshes([mesh('a'), mesh('b')]);
    assert.equal(counters.pipelines, 2);
    await api.configureBenchmark(true);
    await api.addLinesBatch([{ id:'line', center:[0,0,0], segments: new Array(12).fill(1) }]);
    const line = api.testing.state().lines[0];
    await api.removeMeshes(['b']);
    assert.equal(api.testing.state().lines[0], line);
    await api.addTextBillboard({ id:'text', text:'test', position:[0,0,0], textColor:[1,1,1,1], backgroundColor:[0,0,0,0], scale:1 });
    assert.equal(api.testing.state().textBillboards.length, 1);
    await api.removeTextBillboard(0);
    await api.beginSceneMeasurement();
    const measurement = api.finishSceneMeasurement();
    await Promise.resolve(); await Promise.resolve();
    api.testing.renderTarget();
    api.testing.renderFrame();
    const timing = await measurement;
    assert.equal(timing.meshCount, 1);
    assert.equal(timing.retainedResources, 2);
    assert.ok(timing.gpuCompletionMs >= timing.cpuSubmissionMs);
    await api.disposeWebGPU_Canvas();
    assert.equal(line.instanceBuffer.destroyed, true);
}
{
    const { api } = await harness();
    await api.beginSceneMeasurement();
    const pending = api.finishSceneMeasurement();
    const rejected = assert.rejects(pending, /disposed/);
    await Promise.resolve(); await Promise.resolve();
    await api.disposeWebGPU_Canvas();
    await rejected;
}
console.log('PASS: pipeline reuse/configuration, pending promises, retries, cleanup, initialization failure, device loss, binary views/bounds, incremental resources, lines/billboards, and submission timing.');
