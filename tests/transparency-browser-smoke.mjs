import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

const chrome = process.env.CHROME_PATH ?? (process.platform === 'win32'
    ? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' : 'google-chrome');
const baseUrl = process.env.BUGVIEWER_URL ?? 'http://localhost:5220';
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
    async function screenshot(filename) {
        const document = await page.send('DOM.getDocument', { depth: -1 });
        const input = await page.send('DOM.querySelector', { nodeId: document.root.nodeId, selector: '.scene-viewer input[type=file]' });
        await page.send('DOM.setFileInputFiles', { nodeId: input.nodeId, files: [filename] });
        await waitFor('file loading to finish', () => page.evaluate("document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'false'"));
        await delay(1000);
        const error = await page.evaluate("document.querySelector('.scene-file-error, .webgpu-error-overlay')?.textContent || ''");
        assert.equal(error, '', error);
        const bounds = await page.evaluate("(() => { const r = document.querySelector('#webgpu-canvas').getBoundingClientRect(); return { x: Math.max(0, Math.floor(r.left)), y: Math.max(0, Math.floor(r.top)), width: Math.floor(r.width), height: Math.floor(r.height) }; })()");
        const image = await page.send('Page.captureScreenshot', { format: 'png' });
        return await page.evaluate(`(async () => {
            const image = new Image(); image.src = 'data:image/png;base64,${image.data}'; await image.decode();
            const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
            const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0);
            const bounds = ${JSON.stringify(bounds)};
            const pixels = ctx.getImageData(bounds.x, bounds.y, Math.min(bounds.width, canvas.width - bounds.x), Math.min(bounds.height, canvas.height - bounds.y)).data;
            let red = 0, faded = 0;
            for (let i = 0; i < pixels.length; i += 4) {
                if (pixels[i] > 220 && pixels[i+1] < 35 && pixels[i+2] < 35) red++;
                if (pixels[i] > 200 && pixels[i+1] >= 60 && pixels[i+1] < 200 && pixels[i+2] >= 60 && pixels[i+2] < 200 && pixels[i] > pixels[i+1] * 1.25) faded++;
            }
            return { red, faded };
        })()`);
    }
    const front = await screenshot(files[0]);
    const behind = await screenshot(files[1]);
    console.log('Front:', front, 'Behind:', behind);
    assert.ok(front.red > 10 && behind.red > 10, 'Expected visible red paths in both scenes');
    assert.ok(behind.faded > front.faded + 10, 'The path behind the transparent triangle should be visibly blended, unlike the path in front');
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
