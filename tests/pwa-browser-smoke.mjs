// Run against a published site at BUGVIEWER_URL (including any subpath).
// File launch is simulated; OS registration and the permission prompt require manual verification.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

const base = process.env.BUGVIEWER_URL ?? 'http://localhost:5220/BugViewer/';
const chrome = process.env.CHROME_PATH ?? (process.platform === 'win32'
    ? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' : 'google-chrome');
const profile = await mkdtemp(path.join(os.tmpdir(), 'bugviewer-pwa-smoke-'));
const browser = spawn(chrome, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    '--enable-unsafe-webgpu', '--disable-gpu-sandbox', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(label, fn, timeout = 60000) {
    const until = Date.now() + timeout;
    let last;
    while (Date.now() < until) {
        try { last = await fn(); if (last) return last; } catch (error) { last = error; }
        await delay(100);
    }
    throw new Error(`Timed out waiting for ${label}: ${last}`);
}
class Protocol {
    constructor(socket) {
        this.socket = socket; this.id = 0; this.pending = new Map();
        socket.addEventListener('message', event => {
            const message = JSON.parse(event.data);
            if (!this.pending.has(message.id)) return;
            const { resolve, reject } = this.pending.get(message.id); this.pending.delete(message.id);
            message.error ? reject(new Error(message.error.message)) : resolve(message.result);
        });
    }
    static async connect(url) {
        const socket = new WebSocket(url);
        await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
        return new Protocol(socket);
    }
    send(method, params = {}) { const id = ++this.id; return new Promise((resolve, reject) => { this.pending.set(id, { resolve, reject }); this.socket.send(JSON.stringify({ id, method, params })); }); }
    async evaluate(expression) {
        const result = await this.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
        return result.result.value;
    }
}
try {
    const portFile = path.join(profile, 'DevToolsActivePort');
    const port = await waitFor('Chrome', async () => existsSync(portFile) && (await readFile(portFile, 'utf8')).split('\n')[0]);
    const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
    const root = await Protocol.connect(version.webSocketDebuggerUrl);
    const target = await root.send('Target.createTarget', { url: 'about:blank' });
    const endpoint = await waitFor('page', async () => (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(item => item.id === target.targetId)?.webSocketDebuggerUrl);
    const page = await Protocol.connect(endpoint);
    await page.send('Page.enable'); await page.send('Runtime.enable'); await page.send('Network.enable');
    page.socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        if (message.method === 'Runtime.exceptionThrown') console.error('Browser exception:', message.params.exceptionDetails);
        if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error')
            console.error('Browser console:', message.params.args.map(item => item.value ?? item.description).join(' '));
    });
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: `
        Object.defineProperty(window, 'launchQueue', { configurable: true, value: {
            setConsumer: consumer => { window.simulateFileLaunch = consumer; }
        }});
    ` });
    await page.send('Page.navigate', { url: base });
    await waitFor('launch consumer', () => page.evaluate('typeof window.simulateFileLaunch === "function"'));
    await page.evaluate(`window.simulateFileLaunch({ files: [{ getFile: async () => {
        const response = await fetch(new URL('scenes/example.bugscene.json', document.baseURI));
        return new File([await response.blob()], 'example.bugscene', { type: 'application/json' });
    }}] })`);
    await waitFor('launched scene', () => page.evaluate("document.querySelector('.step-controls')?.textContent.includes('/ 4')"));
    assert.equal(await page.evaluate("document.querySelector('.scene-file-error')?.textContent || ''"), '');
    const manifest = await page.send('Page.getAppManifest');
    assert.equal(manifest.errors.length, 0, JSON.stringify(manifest.errors));
    const manifestData = JSON.parse(manifest.data);
    assert.deepEqual(manifestData.file_handlers[0].accept['application/json'], ['.bugscene']);
    await page.evaluate(`window.simulateFileLaunch({ files: [{ getFile: async () =>
        new File(['bad json'], 'bad.bugscene', { type: 'application/json' }) }] })`);
    await waitFor('invalid scene error', () => page.evaluate("document.querySelector('.scene-file-error')?.textContent.includes('Could not open')"));
    assert.ok(await page.evaluate("document.querySelector('.step-controls')?.textContent.includes('/ 4')"), 'Invalid launch replaced the existing scene');
    await waitFor('offline worker installation', () => page.evaluate("navigator.serviceWorker.ready.then(r => !!r.active)"));
    await page.send('Page.reload');
    await waitFor('worker control', () => page.evaluate('!!navigator.serviceWorker.controller'));
    await page.send('Network.emulateNetworkConditions', { offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0 });
    await page.send('Page.reload');
    await waitFor('offline viewer', () => page.evaluate("!!document.querySelector('.scene-viewer input[type=file]')"));
    assert.equal(await page.evaluate('document.baseURI'), base);
    await waitFor('offline launch consumer', () => page.evaluate('typeof window.simulateFileLaunch === "function"'));
    const offlineScene = await readFile(new URL('../samples/LoadAndShowScene/wwwroot/scenes/example.bugscene.json', import.meta.url), 'utf8');
    await page.evaluate(`window.simulateFileLaunch({ files: [{ getFile: async () =>
        new File([${JSON.stringify(offlineScene)}], 'offline.bugscene', { type: 'application/json' }) }] })`);
    await waitFor('offline scene', () => page.evaluate("document.querySelector('.step-controls')?.textContent.includes('/ 4')"));
    if (process.env.BUGVIEWER_SCENE_FILE) {
        await page.evaluate("document.body.insertAdjacentHTML('beforeend', '<input id=launch-fixture type=file hidden>')");
        await page.send('DOM.enable');
        const document = await page.send('DOM.getDocument');
        const input = await page.send('DOM.querySelector', { nodeId: document.root.nodeId, selector: '#launch-fixture' });
        await page.send('DOM.setFileInputFiles', { nodeId: input.nodeId, files: [process.env.BUGVIEWER_SCENE_FILE] });
        await page.evaluate(`window.simulateFileLaunch({ files: [{ getFile: async () =>
            document.querySelector('#launch-fixture').files[0] }] })`);
        await waitFor('large scene loading', () => page.evaluate("document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'true'"));
        await waitFor('large scene completion', () => page.evaluate("document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'false'"), 180000);
        assert.equal(await page.evaluate("document.querySelector('.scene-file-error')?.textContent || ''"), '');
        assert.ok(await page.evaluate("!!document.querySelector('.step-controls')"), 'Large launched scene has no step controls');
        console.log('PASS: launched the supplied stepped scene from a local file while offline.');
    }
    console.log('PASS: simulated file launch streams a scene and the published subpath viewer starts offline.');
    page.socket.close(); root.socket.close();
} finally { browser.kill(); }
