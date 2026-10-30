import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

const chrome = process.env.CHROME_PATH ?? (process.platform === 'win32'
    ? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' : 'google-chrome');
const baseUrl = process.env.BUGVIEWER_URL ?? 'http://localhost:5220';
const requestedSamples = Number(process.env.BUGVIEWER_TEST_SAMPLES ?? 4);
assert.ok(requestedSamples === 1 || requestedSamples === 4, 'Test sample count must be 1 or 4');
const directory = await mkdtemp(path.join(os.tmpdir(), 'bugviewer-transparency-'));
const profile = path.join(directory, 'profile');
const browser = spawn(chrome, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    '--enable-unsafe-webgpu', '--disable-gpu-sandbox', '--no-first-run', '--no-default-browser-check', 'about:blank'],
    { stdio: 'ignore' });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(label, action, timeout = 30000) {
    const deadline = Date.now() + timeout;
    let last;
    while (Date.now() < deadline) {
        try { last = await action(); if (last) return last; } catch (error) { last = error; }
        await delay(100);
    }
    throw new Error(`Timed out waiting for ${label}: ${last instanceof Error ? last.message : JSON.stringify(last)}`);
}
class Protocol {
    constructor(socket) {
        this.socket = socket;
        this.next = 0;
        this.pending = new Map();
        socket.addEventListener('message', event => {
            const message = JSON.parse(event.data);
            if (!message.id || !this.pending.has(message.id)) return;
            const { resolve, reject } = this.pending.get(message.id);
            this.pending.delete(message.id);
            message.error ? reject(new Error(message.error.message)) : resolve(message.result);
        });
        socket.addEventListener('close', () => {
            for (const { reject } of this.pending.values()) reject(new Error('Browser protocol connection closed'));
            this.pending.clear();
        });
    }
    static async connect(url) {
        const socket = new WebSocket(url);
        await new Promise((resolve, reject) => {
            socket.addEventListener('open', resolve, { once: true });
            socket.addEventListener('error', reject, { once: true });
        });
        return new Protocol(socket);
    }
    send(method, params = {}) {
        if (this.socket.readyState !== WebSocket.OPEN)
            return Promise.reject(new Error('Browser protocol connection is not open'));
        const id = ++this.next;
        return new Promise((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.socket.send(JSON.stringify({ id, method, params }));
        });
    }
    async evaluate(expression) {
        const result = await this.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
        return result.result.value;
    }
}

const scene = {
    requestId: '7e5ba084-1342-4b33-9b6f-72c588686237', format: 'BugViewer.SceneRequest', version: 1,
    title: 'Transparency regression', heading: '', subtitle: '', kind: 'ThreeDimensional',
    billboards: [], pathBatches: [], pointSets: [], steps: [], stepGroups: [],
    meshes: [{ id: 'triangle', vertices: [[-2,-2,0],[2,-2,0],[2,2,0]], triangles: [[0,1,2]], primitiveSurfaceNormals: [],
        colors: [{ r: 230, g: 230, b: 250, a: 100 }], colorMode: 'PerTriangle' }],
    paths: [{ id: 'line', vertices: [[-3,-2,0.5],[3,0,0.5]], thickness: 0.08,
        color: { r: 255, g: 0, b: 0, a: 255 } }],
    autoResetCamera: 'Never', showMeshBorders: false
};
let browserProtocol;
try {
    const files = [];
    for (const [name, z] of [['front', 0.5], ['behind', -0.5]]) {
        scene.paths[0].vertices.forEach(vertex => { vertex[2] = z; });
        const filename = path.join(directory, `${name}.bugscene.json`);
        await writeFile(filename, JSON.stringify(scene));
        files.push(filename);
    }
    const crossingScene = structuredClone(scene);
    crossingScene.camera = { target: { x: 0, y: 0, z: 0 }, azimuthAngle: 0, polarAngle: 0,
        distance: 10, orthographicSize: 5, isPerspective: false };
    crossingScene.meshes[0].vertices = [[0,-2,-2], [0,2,-2], [0,2,2], [0,-2,2]];
    crossingScene.meshes[0].triangles = [[0,1,2], [0,2,3]];
    crossingScene.meshes[0].colors = Array.from({ length: 2 }, () => ({ r: 230, g: 230, b: 250, a: 100 }));
    crossingScene.paths[0].vertices = [[0.5,-1.5,1], [-0.5,1.5,1]];
    crossingScene.paths[0].thickness = 0.16;
    const crossingFile = path.join(directory, 'crossing.bugscene.json');
    await writeFile(crossingFile, JSON.stringify(crossingScene));
    const opaqueScene = structuredClone(crossingScene);
    opaqueScene.meshes[0].colors.forEach(color => { color.a = 255; });
    const opaqueFile = path.join(directory, 'crossing-opaque.bugscene.json');
    await writeFile(opaqueFile, JSON.stringify(opaqueScene));
    const translucentPath = { id: 'translucent', vertices: [[0.5,-1.5,1.5], [-0.5,1.5,1.5]],
        thickness: 0.16, color: { r: 0, g: 128, b: 255, a: 128 } };
    const mixedScene = structuredClone(crossingScene);
    mixedScene.paths.push(translucentPath);
    const mixedFile = path.join(directory, 'mixed-paths.bugscene.json');
    await writeFile(mixedFile, JSON.stringify(mixedScene));
    const translucentScene = structuredClone(crossingScene);
    translucentScene.paths = [translucentPath];
    const translucentFile = path.join(directory, 'translucent-path.bugscene.json');
    await writeFile(translucentFile, JSON.stringify(translucentScene));
    const noPathScene = structuredClone(crossingScene);
    noPathScene.paths = [];
    const noPathFile = path.join(directory, 'no-path.bugscene.json');
    await writeFile(noPathFile, JSON.stringify(noPathScene));
    const translucentFront = structuredClone(translucentScene);
    translucentFront.paths[0].vertices.forEach(vertex => { vertex[0] = 0.5; });
    const translucentFrontFile = path.join(directory, 'translucent-front.bugscene.json');
    await writeFile(translucentFrontFile, JSON.stringify(translucentFront));
    const translucentBehind = structuredClone(translucentScene);
    translucentBehind.paths[0].vertices.forEach(vertex => { vertex[0] = -0.5; });
    const translucentBehindFile = path.join(directory, 'translucent-behind.bugscene.json');
    await writeFile(translucentBehindFile, JSON.stringify(translucentBehind));
    const overlapping = structuredClone(crossingScene);
    overlapping.paths = [];
    overlapping.meshes = [
        { ...structuredClone(crossingScene.meshes[0]), id: 'red-surface',
            vertices: crossingScene.meshes[0].vertices.map(vertex => [0.3, vertex[1], vertex[2]]),
            colors: Array.from({ length: 2 }, () => ({ r: 255, g: 0, b: 0, a: 128 })) },
        { ...structuredClone(crossingScene.meshes[0]), id: 'green-surface',
            vertices: crossingScene.meshes[0].vertices.map(vertex => [-0.3, vertex[1], vertex[2]]),
            colors: Array.from({ length: 2 }, () => ({ r: 0, g: 255, b: 0, a: 128 })) }
    ];
    const overlappingFile = path.join(directory, 'overlapping.bugscene.json');
    await writeFile(overlappingFile, JSON.stringify(overlapping));
    overlapping.meshes.reverse();
    const reversedFile = path.join(directory, 'overlapping-reversed.bugscene.json');
    await writeFile(reversedFile, JSON.stringify(overlapping));
    const mixedMesh = structuredClone(crossingScene);
    mixedMesh.paths = [];
    mixedMesh.meshes[0].colors[0].a = 255;
    const mixedMeshFile = path.join(directory, 'mixed-mesh.bugscene.json');
    await writeFile(mixedMeshFile, JSON.stringify(mixedMesh));
    const separateMeshes = structuredClone(mixedMesh);
    const originalMesh = separateMeshes.meshes[0];
    separateMeshes.meshes = [
        { ...originalMesh, id: 'opaque-triangle', triangles: [originalMesh.triangles[0]], colors: [originalMesh.colors[0]] },
        { ...originalMesh, id: 'translucent-triangle', triangles: [originalMesh.triangles[1]], colors: [originalMesh.colors[1]] }
    ];
    const separateMeshesFile = path.join(directory, 'separate-meshes.bugscene.json');
    await writeFile(separateMeshesFile, JSON.stringify(separateMeshes));
    const perspectiveScene = structuredClone(crossingScene);
    perspectiveScene.camera.isPerspective = true;
    const perspectiveFile = path.join(directory, 'perspective.bugscene.json');
    await writeFile(perspectiveFile, JSON.stringify(perspectiveScene));
    const portFile = path.join(profile, 'DevToolsActivePort');
    const port = await waitFor('Chrome debugging port', async () =>
        existsSync(portFile) && (await readFile(portFile, 'utf8')).split('\n')[0]);
    const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
    browserProtocol = await Protocol.connect(version.webSocketDebuggerUrl);
    const target = await browserProtocol.send('Target.createTarget', { url: baseUrl });
    const endpoint = await waitFor('page target', async () => (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json())
        .find(item => item.id === target.targetId)?.webSocketDebuggerUrl);
    const page = await Protocol.connect(endpoint);
    await page.send('Runtime.enable');
    await page.send('DOM.enable');
    await page.send('Emulation.setDeviceMetricsOverride', { width: 1000, height: 800, deviceScaleFactor: 1, mobile: false });
    await waitFor('scene viewer', () => page.evaluate("!!document.querySelector('.scene-viewer input[type=file]')"));
    const toolbar = await page.evaluate(`(() => {
        const open = document.querySelector('.scene-file-actions button[data-scene-file-command="open"]');
        const save = document.querySelector('.scene-file-actions button:last-child');
        return { openEnabled: !!open && !open.disabled, saveDisabled: !!save && save.disabled };
    })()`);
    assert.deepEqual(toolbar, { openEnabled: true, saveDisabled: true },
        'The standalone viewer should show an enabled Open button and disabled Save button');
    await page.evaluate(`(() => {
        window.sceneLoadStarts = 0;
        const viewer = document.querySelector('.scene-viewer');
        new MutationObserver(records => {
            for (const record of records) {
                if (record.attributeName === 'aria-busy' && record.oldValue === 'false')
                    window.sceneLoadStarts++;
            }
        }).observe(viewer, { attributes: true, attributeOldValue: true, attributeFilter: ['aria-busy'] });
    })()`);
    async function screenshot(filename) {
        const fixture = JSON.parse(await readFile(filename, 'utf8'));
        const previousStarts = await page.evaluate('window.sceneLoadStarts');
        const document = await page.send('DOM.getDocument', { depth: -1 });
        const input = await page.send('DOM.querySelector', { nodeId: document.root.nodeId, selector: '.scene-viewer input[type=file]' });
        await page.send('DOM.setFileInputFiles', { nodeId: input.nodeId, files: [filename] });
        await waitFor('file loading to start', () => page.evaluate(`window.sceneLoadStarts > ${previousStarts}`));
        await waitFor('file loading to finish', () => page.evaluate("document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'false'"));
        await waitFor('scene geometry to be submitted', () => page.evaluate(`(async () => {
            const entry = performance.getEntriesByType('resource')
                .filter(item => item.name.includes('/_content/BugViewer/js/webgpu-canvas.js')).at(-1);
            if (!entry) return false;
            const renderer = await import(entry.name);
            const frame = renderer.getBenchmarkEnvironment();
            return !frame.presentationFramePending && frame.meshCount === ${fixture.meshes.length}
                && (frame.lineCount > 0) === ${fixture.paths.length > 0};
        })()`));
        await page.evaluate(`(async () => {
            const entry = performance.getEntriesByType('resource')
                .filter(item => item.name.includes('/_content/BugViewer/js/webgpu-canvas.js')).at(-1);
            if (!entry) throw new Error('Renderer module was not loaded');
            const renderer = await import(entry.name);
            await renderer.updateDisplayOptions({ sampleCount: ${requestedSamples} });
            if (renderer.getBenchmarkEnvironment().sampleCount !== ${requestedSamples})
                throw new Error('Renderer did not apply the requested sample count');
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        })()`);
        await delay(200);
        const error = await page.evaluate("document.querySelector('.scene-file-error, .webgpu-error-overlay')?.textContent || ''");
        assert.equal(error, '', error);
        const bounds = await page.evaluate("(() => { const r = document.querySelector('#webgpu-canvas').getBoundingClientRect(); return { x: Math.max(0, Math.floor(r.left)), y: Math.max(0, Math.floor(r.top)), width: Math.floor(r.width), height: Math.floor(r.height) }; })()");
        const image = await page.send('Page.captureScreenshot', { format: 'png' });
        await writeFile(path.join(directory, `${path.basename(filename, '.bugscene.json')}.png`),
            Buffer.from(image.data, 'base64'));
        return await page.evaluate(`(async () => {
            const image = new Image(); image.src = 'data:image/png;base64,${image.data}'; await image.decode();
            const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
            const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0);
            const bounds = ${JSON.stringify(bounds)};
            const pixels = ctx.getImageData(bounds.x, bounds.y, Math.min(bounds.width, canvas.width - bounds.x), Math.min(bounds.height, canvas.height - bounds.y)).data;
            const width = Math.min(bounds.width, canvas.width - bounds.x);
            const height = Math.min(bounds.height, canvas.height - bounds.y);
            let red = 0, faded = 0;
            const halves = [{ red: 0, faded: 0 }, { red: 0, faded: 0 }];
            for (let i = 0; i < pixels.length; i += 4) {
                const isRed = pixels[i] > 220 && pixels[i+1] < 35 && pixels[i+2] < 35;
                const isFaded = pixels[i] > 200 && pixels[i+1] >= 60 && pixels[i+1] < 200 && pixels[i+2] >= 60 && pixels[i+2] < 200 && pixels[i] > pixels[i+1] * 1.25;
                if (isRed) red++;
                if (isFaded) faded++;
                const x = (i / 4) % width, y = Math.floor(i / 4 / width);
                if (y > height * .395 && y < height * .405
                    && (x > width * .36 && x < width * .46 || x > width * .54 && x < width * .64)) {
                    const half = halves[x < width * .5 ? 0 : 1];
                    if (isRed) half.red++;
                    if (isFaded) half.faded++;
                }
            }
            const samples = [.42, .58].map(x => {
                const offset = (Math.floor(height * .35) * width + Math.floor(width * x)) * 4;
                return Array.from(pixels.slice(offset, offset + 3));
            });
            const extraSamples = [.42, .58].map(x => {
                const offset = (Math.floor(height * .60) * width + Math.floor(width * x)) * 4;
                return Array.from(pixels.slice(offset, offset + 3));
            });
            return { red, faded, halves, samples, extraSamples };
        })()`);
    }
    const front = await screenshot(files[0]);
    assert.equal(await page.evaluate("document.querySelector('.scene-file-actions button:last-child').disabled"), false,
        'Save must become available after a scene is opened');
    const behind = await screenshot(files[1]);
    console.log('Front:', front, 'Behind:', behind);
    assert.ok(front.red > 10 && behind.red > 10, 'Expected visible red paths in both scenes');
    assert.ok(behind.faded > front.faded + 10, 'The path behind the transparent triangle should be visibly blended, unlike the path in front');
    const crossing = await screenshot(crossingFile);
    console.log('Crossing:', crossing);
    assert.ok(crossing.halves[0].red > 100 && crossing.halves[0].faded < crossing.halves[0].red / 10,
        'The front half of the crossing path should remain red');
    assert.ok(crossing.halves[1].faded > 100 && crossing.halves[1].red < crossing.halves[1].faded / 10,
        'The behind half of the crossing path should be blended');
    const opaque = await screenshot(opaqueFile);
    console.log('Opaque surface:', opaque);
    assert.ok(opaque.halves[0].red > 100, 'The path in front of an opaque surface should remain visible');
    assert.ok(opaque.halves[1].red < 5 && opaque.halves[1].faded < 5,
        'The path behind an opaque surface should be occluded');
    const mixed = await screenshot(mixedFile);
    const translucent = await screenshot(translucentFile);
    const noPath = await screenshot(noPathFile);
    assert.deepEqual(mixed.samples, translucent.samples,
        'A transparent path must blend the same way when batched with an opaque path');
    for (let i = 0; i < mixed.samples.length; i++) {
        assert.notDeepEqual(mixed.samples[i], noPath.samples[i], 'The transparent path should be visible');
        assert.notDeepEqual(mixed.samples[i], [0, 128, 255], 'The transparent path should remain blended');
    }
    const translucentFrontPixels = await screenshot(translucentFrontFile);
    const translucentBehindPixels = await screenshot(translucentBehindFile);
    console.log('Translucent crossing samples:', translucent.samples,
        'front reference:', translucentFrontPixels.samples, 'behind reference:', translucentBehindPixels.samples);
    const colorDistance = (a, b) => a.reduce((sum, channel, index) => sum + Math.abs(channel - b[index]), 0);
    assert.ok(colorDistance(translucentFrontPixels.samples[0], translucentBehindPixels.samples[0]) >= 3,
        'The translucent path must respond visibly to depth relative to the surface');
    assert.ok(colorDistance(translucent.samples[0], translucentFrontPixels.samples[0])
        < colorDistance(translucent.samples[0], translucentBehindPixels.samples[0]),
    'The front half of the crossing path should resemble the front reference');
    assert.ok(colorDistance(translucent.samples[1], translucentBehindPixels.samples[1])
        < colorDistance(translucent.samples[1], translucentFrontPixels.samples[1]),
    'The behind half of the crossing path should resemble the behind reference');
    const surfaces = await screenshot(overlappingFile);
    const reversedSurfaces = await screenshot(reversedFile);
    for (let i = 0; i < surfaces.samples.length; i++) {
        assert.ok(colorDistance(surfaces.samples[i], reversedSurfaces.samples[i]) <= 3,
            'Reordering translucent surfaces must not visibly change their composite');
    }
    const mixedMeshPixels = await screenshot(mixedMeshFile);
    const separateMeshPixels = await screenshot(separateMeshesFile);
    for (const key of ['samples', 'extraSamples']) {
        for (let i = 0; i < mixedMeshPixels[key].length; i++) {
            assert.ok(colorDistance(mixedMeshPixels[key][i], separateMeshPixels[key][i]) <= 3,
                'Opaque and translucent triangles in one mesh must match separate meshes');
        }
    }
    const perspective = await screenshot(perspectiveFile);
    assert.ok(perspective.red > 100 && perspective.faded > 100,
        'The crossing path must render in perspective projection');
    await screenshot(noPathFile);
    await page.evaluate(`(async () => {
        const entry = performance.getEntriesByType('resource')
            .filter(item => item.name.includes('/_content/BugViewer/js/webgpu-canvas.js')).at(-1);
        const renderer = await import(entry.name);
        await renderer.addTextBillboard({ id: 'opaque-label', text: 'opaque',
            position: [1, -1, 1], backgroundColor: [1, 0, 1, 1],
            textColor: [0, 0, 0, 1], scale: .2 });
        await renderer.addTextBillboard({ id: 'translucent-label', text: 'translucent',
            position: [1, 1, 1], backgroundColor: [0, 1, 1, .5],
            textColor: [0, 0, 0, 1], scale: .2 });
        await renderer.updateDisplayOptions({ sampleCount: ${requestedSamples === 1 ? 4 : 1} });
        await renderer.updateDisplayOptions({ sampleCount: ${requestedSamples} });
    })()`);
    await delay(500);
    const billboardImage = await page.send('Page.captureScreenshot', { format: 'png' });
    const labelColors = await page.evaluate(`(async () => {
        const image = new Image(); image.src = 'data:image/png;base64,${billboardImage.data}'; await image.decode();
        const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
        const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0);
        const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
        let magenta = 0, cyan = 0;
        for (let i = 0; i < pixels.length; i += 4) {
            if (pixels[i] > 220 && pixels[i + 1] < 30 && pixels[i + 2] > 220) magenta++;
            if (pixels[i] < 210 && pixels[i + 1] > 220 && pixels[i + 2] > 220) cyan++;
        }
        return { magenta, cyan };
    })()`);
    assert.ok(labelColors.magenta > 20 && labelColors.cyan > 20,
        'Opaque and translucent billboards must remain visible after changing MSAA');
    if (process.env.BUGVIEWER_PERF_BASELINE) {
        const baseline = await readFile(process.env.BUGVIEWER_PERF_BASELINE, 'utf8');
        const performanceResult = await page.evaluate(`(async () => {
            const entry = performance.getEntriesByType('resource')
                .filter(item => item.name.includes('/_content/BugViewer/js/webgpu-canvas.js')).at(-1);
            const active = await import(entry.name);
            await active.pauseRendering();
            const candidate = await (await fetch(entry.name)).text();
            const dependency = new URL('../ViewCube.razor.js', entry.name).href;
            const identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
            const options = { sampleCount: 4, coordinateThickness: 0, zIsUp: false,
                baseColor: [0,0,0,0], lineColor: [0,0,0,0], lineWidthX: 0, lineWidthY: 0,
                ambient: 1, headlampIntensity: 0, directionalLightIntensity: 0 };
            const segments = new Float32Array(50000 * 12);
            for (let i = 0; i < 50000; i++) {
                const x = (i % 250) / 125 - 1, y = Math.floor(i / 250) / 100 - 1;
                segments.set([x,y,.4, x+.004,y,.4, .1,.5,1,.5, .002,0], i * 12);
            }
            const vertices = new Float32Array(20000 * 9), indices = new Uint32Array(20000 * 3);
            for (let i = 0; i < 20000; i++) {
                const x = (i % 200) / 100 - 1, y = Math.floor(i / 200) / 50 - 1;
                vertices.set([x,y,.6, x+.006,y,.6, x,y+.006,.6], i * 9);
                indices.set([i*3,i*3+1,i*3+2], i * 3);
            }
            async function measure(source) {
                const code = source.replace("'../ViewCube.razor.js'", JSON.stringify(dependency));
                const url = URL.createObjectURL(new Blob([code], { type: 'text/javascript' }));
                const module = await import(url);
                const canvas = document.createElement('canvas');
                canvas.width = 800; canvas.height = 600;
                canvas.style.cssText = 'position:fixed;left:0;top:0;width:800px;height:600px;z-index:9999';
                document.body.appendChild(canvas);
                const errors = [];
                const callback = { invokeMethodAsync(method, ...args) {
                    if (method === 'OnWebGpuError') errors.push(args[0]);
                    return Promise.resolve();
                }};
                try {
                    await module.initGPU_Canvas(callback, canvas, null, null, options, identity, [0,0,2]);
                    await module.writeProjectionMatrix(identity);
                    await module.addMesh({ id:'mesh', vertices, indices, colors:[.5,.5,.5,.5],
                        primitiveSurfaceNormals: [], singleColor:true });
                    await module.addLines({ id:'lines', segments, center:[0,0,.4] });
                    await new Promise(resolve => setTimeout(resolve, 500));
                    const timings = [];
                    for (let i = 0; i < 10; i++) {
                        await module.beginSceneMeasurement();
                        const result = await module.finishSceneMeasurement();
                        if (!result.visibilityStable) throw new Error('Performance tab became hidden');
                        timings.push({ cpu: result.cpuSubmissionMs, gpu: result.gpuCompletionMs });
                    }
                    if (errors.length) throw new Error(errors.join('; '));
                    return { environment: module.getBenchmarkEnvironment(), timings };
                } finally {
                    await module.disposeWebGPU_Canvas();
                    canvas.remove();
                    URL.revokeObjectURL(url);
                }
            }
            const baseline = await measure(${JSON.stringify(baseline)});
            const candidateResult = await measure(candidate);
            await active.resumeRendering();
            return { segments: 50000, triangles: 20000, baseline, candidate: candidateResult };
        })()`);
        console.log('Performance comparison:', JSON.stringify(performanceResult));
    }
    assert.ok(!await page.evaluate("!!document.querySelector('.webgpu-error-overlay')"), 'WebGPU unavailable');
    console.log('PASS: front path stays red while the path behind the triangle is blended.');
    page.socket.close();
} finally {
    if (browserProtocol) {
        try { await browserProtocol.send('Browser.close'); } catch { }
        browserProtocol.socket.close();
    }
    browser.kill();
    console.log(`Browser profile and generated scenes: ${directory}`);
}
