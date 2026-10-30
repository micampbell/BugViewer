import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

// Execute the actual renderer in an isolated module; only browser/GPU endpoints
// are substituted. The appended hooks are test-only and never ship in the RCL.
async function harness() {
    let fail = false, pending = null, lose, uncapturedError;
    const counters = { pipelines: 0, pipelineDescriptors: [], shaders: 0, layouts: 0,
        buffers: [], textures: [], submissions: 0, draws: [], passes: [] };
    const resource = () => ({ destroy() { this.destroyed = true; }, createView() { return {}; } });
    const device = {
        lost: new Promise(resolve => { lose = resolve; }),
        addEventListener(type, handler) { if (type === 'uncapturederror') uncapturedError = handler; },
        createBuffer({ size }) { const buffer = { size, ...resource(), data: new ArrayBuffer(size),
            getMappedRange() { return this.data; }, unmap() {} }; counters.buffers.push(buffer); return buffer; },
        createBindGroupLayout(descriptor) { counters.layouts++; return { descriptor }; },
        createPipelineLayout: descriptor => ({ descriptor }),
        createBindGroup: descriptor => ({ descriptor }),
        createShaderModule(descriptor) { counters.shaders++; return { descriptor }; },
        async createRenderPipelineAsync(descriptor) {
            counters.pipelines++;
            counters.pipelineDescriptors.push(descriptor);
            if (pending) await pending;
            if (fail) { fail = false; throw new Error('pipeline failed'); }
            return { descriptor };
        },
        createTexture(descriptor) { const texture = { ...resource(), descriptor }; counters.textures.push(texture); return texture; },
        createSampler: () => ({}), destroy() {},
        createCommandEncoder: () => ({ beginRenderPass: descriptor => {
            const pass = counters.passes.length;
            counters.passes.push(descriptor);
            let pipeline, vertexBuffer;
            return new Proxy({
                setPipeline(value) { pipeline = value; },
                setVertexBuffer(slot, value) { if (slot === 0) vertexBuffer = value; },
                draw() { counters.draws.push({ pipeline, vertexBuffer, pass }); },
                drawIndexed() { counters.draws.push({ pipeline, vertexBuffer, pass }); }
            }, { get: (target, key) => target[key] ?? (() => {}) });
        }, end() {}, finish: () => ({}) }),
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
            getMeshPipeline, decodeBinaryScene, renderFrame, initInstancedLinePipeline, initTransparencyComposite, allocateRenderTargets,
            state: () => ({ meshes, lines, textBillboards, cacheSize: meshPipelines.size }),
            settings(samples, color, depth) { sampleCount = samples; colorFormat = color; depthFormat = depth; },
            async initialize() { isDisposing = false; context = { configure() {}, unconfigure() {} }; await initWebGPU(); },
            canvas(width, height) { canvas = { width, height }; },
            renderTarget() { context = { getCurrentTexture: () => ({ createView: () => ({}) }) };
                colorAttachment = {}; renderPassDescriptor = {}; transparencyPassDescriptor = {};
                compositePipeline = { name: 'composite' }; compositeBindGroup = {}; },
            transparentFixtures() {
                viewMatrix.set([1,0,0,0, 0,1,0,0, 0,0,1,0, 3,4,-10,1]);
                gridPipeline = { name: 'opaque grid' }; transparentGridPipeline = { name: 'grid' };
                gridVertexBuffer = { destroy() {} };
                coordinateThickness = 1; coordinateAxes = { pipeline: { name: 'axes' }, opaquePipeline: { name: 'opaque axes' } };
                instancedLinePipeline = { name: 'line' };
                opaqueInstancedLinePipeline = { name: 'opaque line' };
            },
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
        lose: async () => { lose({ message: 'test loss' }); await Promise.resolve(); await Promise.resolve(); },
        uncapturedError: message => uncapturedError({ error: { message } }) };
}
const mesh = (id, singleColor = true, alpha = 1) => ({ id, vertices: [0,0,0, 1,0,0, 0,1,0],
    indices: [0,1,2], colors: singleColor ? [1,0,0,alpha] : [1,0,0,alpha, 0,1,0,alpha, 0,0,1,alpha],
    primitiveSurfaceNormals: [], singleColor });
{
    const { api, counters } = await harness();
    api.testing.renderTarget();
    api.testing.transparentFixtures();
    await api.addMesh(mesh('transparent', true, .5));
    const transparentMesh = api.testing.state().meshes[0];
    transparentMesh.center = [0, 0, -2];
    const state = api.testing.state();
    const farLine = { center: [0, 0, -4], instanceBuffer: { destroy() {} }, instanceCount: 1, hasTransparent: true };
    const nearLine = { center: [0, 0, 2], instanceBuffer: { destroy() {} }, instanceCount: 1, hasTransparent: true };
    state.lines.push(nearLine, farLine);
    const farBillboard = { position: [0, 0, -3], pipeline: {}, vertexBuffer: { destroy() {} }, indexBuffer: { destroy() {} }, indexCount: 6 };
    state.textBillboards.push(farBillboard);
    api.testing.renderFrame();
    assert.equal(counters.passes.length, 3);
    for (const object of [farLine, nearLine, transparentMesh, farBillboard]) {
        const buffer = object.instanceBuffer ?? object.vertexBuffer;
        assert.equal(counters.draws.find(draw => draw.vertexBuffer === buffer).pass, 1);
    }
    assert.equal(counters.draws.at(-1).pipeline.name, 'composite');
    assert.equal(counters.draws.at(-1).pass, 2);
    await api.disposeWebGPU_Canvas();
}
{
    const { api, counters } = await harness();
    const colored = mesh('mixed-mesh', false, 1);
    colored.colors[7] = .5;
    await api.addMesh(colored);
    const item = api.testing.state().meshes[0];
    assert.equal(item.hasOpaque, true);
    assert.equal(item.isTransparent, true);
    assert.equal(item.pipeline.descriptor.depthStencil.depthWriteEnabled, true);
    assert.equal(item.transparentPipeline.descriptor.depthStencil.depthWriteEnabled, false);
    await api.changeMeshColors({ meshId: item.id, colors: [1,0,0,.4, 0,1,0,.4, 0,0,1,.4] });
    assert.equal(item.hasOpaque, false);
    assert.equal(item.isTransparent, true);
    await api.changeMeshColors({ meshId: item.id, colors: [1,0,0,1, 0,1,0,1, 0,0,1,1] });
    assert.equal(item.hasOpaque, true);
    assert.equal(item.isTransparent, false);
    assert.equal(item.transparentPipeline, null);
    await api.changeMeshColors({ meshId: item.id, colors: [1,0,0,0, 0,1,0,0, 0,0,1,0] });
    assert.equal(item.hasOpaque, false);
    assert.equal(item.isTransparent, false);
    await api.addMesh(mesh('uniform'));
    const uniform = api.testing.state().meshes[1];
    await api.changeMeshColor({ index: 1, color: [1,0,0,.5] });
    assert.equal(uniform.hasOpaque, false);
    assert.equal(uniform.transparentPipeline.descriptor.depthStencil.depthWriteEnabled, false);
    await api.changeMeshColor({ index: 1, color: [1,0,0,1] });
    assert.equal(uniform.hasOpaque, true);
    assert.equal(uniform.isTransparent, false);
    assert.ok(counters.pipelines >= 4);
    await api.disposeWebGPU_Canvas();
}
{
    const { api, counters } = await harness();
    await api.testing.initialize();
    api.testing.canvas(640, 480);
    api.testing.allocateRenderTargets(640, 480);
    const first = counters.textures.slice();
    assert.deepEqual(first.map(texture => texture.descriptor.format),
        ['bgra8unorm-srgb', 'depth24plus', 'rgba16float', 'r16float']);
    api.testing.allocateRenderTargets(800, 600);
    assert.ok(first.every(texture => texture.destroyed), 'Resizing must release the old render targets');
    await api.updateDisplayOptions({ sampleCount: 1 });
    const current = counters.textures.slice(-3);
    assert.deepEqual(current.map(texture => texture.descriptor.format),
        ['depth24plus', 'rgba16float', 'r16float']);
    assert.ok(current.every(texture => texture.descriptor.sampleCount === 1));
    await api.updateDisplayOptions({ sampleCount: 4 });
    assert.ok(current.every(texture => texture.destroyed));
    await api.disposeWebGPU_Canvas();
    assert.ok(counters.textures.every(texture => texture.destroyed));
}
{
    const { api, counters } = await harness();
    api.testing.renderTarget();
    api.testing.transparentFixtures();
    const segment = (alpha, fade = 0) => [0,0,0, 1,0,0, 1,0,0,alpha, 1,fade];
    for (const [id, segments] of [
        ['opaque', segment(1)],
        ['transparent', segment(.5)],
        ['faded', segment(1, 1)],
        ['mixed', [...segment(1), ...segment(.5)]],
        ['invisible', segment(0)]
    ]) await api.addLines({ id, center: [0,0,0], segments });
    const byId = Object.fromEntries(api.testing.state().lines.map(line => [line.id, line]));
    assert.deepEqual(['opaque', 'transparent', 'faded', 'mixed', 'invisible']
        .map(id => [byId[id].hasOpaque, byId[id].hasTransparent]),
    [[true, false], [false, true], [true, true], [true, true], [false, false]]);
    api.testing.renderFrame();
    for (const id of Object.keys(byId)) {
        const draws = counters.draws.filter(draw => draw.vertexBuffer === byId[id].instanceBuffer);
        assert.deepEqual(draws.map(draw => draw.pipeline.name), {
            opaque: ['opaque line'], transparent: ['line'], faded: ['opaque line', 'line'],
            mixed: ['opaque line', 'line'], invisible: []
        }[id]);
    }
    await api.disposeWebGPU_Canvas();
}
{
    const { api, counters } = await harness();
    await api.testing.initInstancedLinePipeline();
    assert.equal(counters.pipelines, 2);
    assert.deepEqual(counters.pipelineDescriptors.map(pipeline => [
        pipeline.fragment.entryPoint, pipeline.depthStencil.depthWriteEnabled
    ]), [['transparentFragment', false], ['opaqueFragment', true]]);
    await api.disposeWebGPU_Canvas();
}
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
    const { api, uncapturedError } = await harness();
    await api.testing.initialize();
    await api.beginSceneMeasurement();
    const frame = api.finishSceneMeasurement();
    await Promise.resolve();
    uncapturedError('test validation failure');
    await assert.rejects(frame, /WebGPU error: test validation failure/);
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

// Real presentation protocol: staged chunks, atomic activation, cache hits,
// prefetch, bounded eviction, abort, and disposal while waiting for submission.
function presentationFixture(key) {
    const bytes = new Uint8Array(60);
    new Float32Array(bytes.buffer, 0, 9).set([0,0,0, 1,0,0, 0,1,0]);
    new Float32Array(bytes.buffer, 36, 4).set([1,0,0,1]);
    new Uint16Array(bytes.buffer, 52, 3).set([0,1,2]);
    return { bytes, metadata: { meshes: [{ id:key, vertexOffset:0, vertexCount:9,
        colorOffset:36, colorCount:4, normalOffset:52, normalCount:0,
        indexOffset:52, indexCount:3, index32:false, singleColor:true }], lines:[] } };
}
const drain = () => new Promise(resolve => setTimeout(resolve, 0));
async function activate(api, refs, budget = 1024) {
    const pending = api.commitPresentationFrame(refs, [], true, true, null, budget);
    await drain(); api.testing.renderTarget(); api.testing.renderFrame();
    return await pending;
}
{
    const { api, counters } = await harness();
    await api.addMesh(mesh('previous'));
    const previous = api.testing.state().meshes[0];
    const a = [{ key:'a', id:'logical' }], b = [{ key:'b', id:'logical' }];
    assert.deepEqual([...(await api.preparePresentationFrame(a, [])).meshes], ['a']);
    const fixture = presentationFixture('a');
    await api.uploadPresentationChunk(fixture.metadata, 60, 0, fixture.bytes.subarray(0, 32));
    assert.equal(api.testing.state().meshes[0], previous);
    await api.uploadPresentationChunk(null, 60, 32, fixture.bytes.subarray(32));
    assert.equal(api.testing.state().meshes[0], previous, 'staging must not change active geometry');
    const timing = await activate(api, a);
    assert.ok(timing.submittedAtMs > 0);
    assert.equal(timing.visible, true);
    const retainedA = api.testing.state().meshes[0];
    assert.equal(retainedA.id, 'logical');
    assert.equal(previous.vertexBuffer.destroyed, true);
    await api.preparePresentationFrame(b, []);
    const fixtureB = presentationFixture('b');
    await api.uploadPresentationChunk(fixtureB.metadata, 60, 0, fixtureB.bytes);
    await api.finishPresentationPrefetch(1024);
    assert.equal(api.testing.state().meshes[0], retainedA);
    assert.equal((await api.preparePresentationFrame(b, [])).meshes.length, 0);
    await activate(api, b);
    const retainedB = api.testing.state().meshes[0];
    const bufferCount = counters.buffers.length;
    assert.equal((await api.preparePresentationFrame(a, [])).meshes.length, 0);
    await activate(api, a);
    assert.equal(api.testing.state().meshes[0], retainedA);
    assert.equal(counters.buffers.length, bufferCount, 'revisiting a cached step must allocate no GPU buffers');
    await activate(api, a, 0);
    assert.equal(retainedB.vertexBuffer.destroyed, true, 'inactive geometry must be evicted under budget pressure');
    assert.notEqual(retainedA.vertexBuffer.destroyed, true, 'active geometry must stay pinned');
    assert.deepEqual([...(await api.preparePresentationFrame(b, [])).meshes], ['b']);
    await api.uploadPresentationChunk(fixtureB.metadata, 60, 0, fixtureB.bytes);
    await api.abortPresentationFrame();
    assert.equal(api.testing.state().meshes[0], retainedA);
    assert.deepEqual([...(await api.preparePresentationFrame(b, [])).meshes], ['b']);
    await assert.rejects(api.uploadPresentationChunk(null, 60, 32, fixtureB.bytes.subarray(32)), /Out-of-order/);
    await api.abortPresentationFrame();
    const canceled = api.commitPresentationFrame(a, [], true, false, null, 1024);
    await drain(); api.cancelPresentationWait();
    assert.equal((await canceled).submittedAtMs, 0, 'disposal must unblock a hidden-browser submission wait');
    const paused = await api.commitPresentationFrame(a, [], true, false, null, 1024, false);
    assert.equal(paused.visible, false, 'paused viewers must not wait for an animation frame');
    const waiting = api.commitPresentationFrame(a, [], true, false, null, 1024);
    const rejected = assert.rejects(waiting, /disposed/);
    await drain(); await api.disposeWebGPU_Canvas(); await rejected;
    assert.ok(counters.buffers.every(buffer => buffer.destroyed));
}
{
    const { api } = await harness();
    const refs = [{ key:'empty', id:'empty mesh' }];
    await api.preparePresentationFrame(refs, []);
    await api.uploadPresentationChunk({ meshes:[{ id:'empty', vertexOffset:0, vertexCount:0,
        colorOffset:0, colorCount:0, normalOffset:0, normalCount:0,
        indexOffset:0, indexCount:0, index32:false, singleColor:true }], lines:[] }, 0, 0, new Uint8Array());
    await activate(api, refs);
    assert.equal(api.testing.state().meshes[0].indexCount, 0);
    await api.disposeWebGPU_Canvas();
}
console.log('PASS: renderer pipelines, binary data, presentation staging/activation/cache/prefetch/eviction, cancellation, lifecycle, and submission timing.');
