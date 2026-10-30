import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, mkdtemp, readFile, readdir } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

const chrome = process.env.CHROME_PATH ?? (process.platform === 'win32'
    ? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' : 'google-chrome');
const baseUrl = process.env.BUGVIEWER_URL ?? 'http://localhost:5220';
const root = path.resolve(import.meta.dirname, '..');
const directory = await mkdtemp(path.join(os.tmpdir(), 'bugviewer-browser-check-'));
const profile = path.join(directory, 'profile');
const downloads = path.join(directory, 'downloads');
await mkdir(downloads);
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
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
        return result.result.value;
    }
}

let browserProtocol;
try {
    const portFile = path.join(profile, 'DevToolsActivePort');
    const port = await waitFor('Chrome debugging port', async () =>
        existsSync(portFile) && (await readFile(portFile, 'utf8')).split('\n')[0]);
    const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
    browserProtocol = await Protocol.connect(version.webSocketDebuggerUrl);
    await browserProtocol.send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: downloads });
    const target = await browserProtocol.send('Target.createTarget', { url: baseUrl });
    const endpoint = await waitFor('page target', async () => (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json())
        .find(item => item.id === target.targetId)?.webSocketDebuggerUrl);
    const page = await Protocol.connect(endpoint);
    await page.send('Runtime.enable');
    await page.send('DOM.enable');
    page.socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        if (message.method === 'Runtime.exceptionThrown') console.error('Browser exception:', message.params.exceptionDetails);
        if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error')
            console.error('Browser console:', message.params.args.map(item => item.value ?? item.description).join(' '));
    });
    await waitFor('scene viewer', () => page.evaluate("!!document.querySelector('.scene-viewer input[type=file]')"));
    await page.evaluate(`(() => {
        window.fileProgressEvents = [];
        window.cancelNextSave = false;
        const observer = new MutationObserver(() => {
            const overlay = document.querySelector('.scene-file-progress');
            if (!overlay) return;
            const bar = overlay.querySelector('progress');
            const box = overlay.getBoundingClientRect();
            window.fileProgressEvents.push({ text: overlay.textContent, determinate: bar.hasAttribute('value'), x: box.x, y: box.y });
            if (window.cancelNextSave && overlay.textContent.includes('Preparing scene')) {
                window.cancelNextSave = false;
                overlay.querySelector('button')?.click();
            }
        });
        observer.observe(document.querySelector('.scene-viewer'), { childList: true, subtree: true, characterData: true });
    })()`);

    async function openFile(filename, condition) {
        const document = await page.send('DOM.getDocument', { depth: -1 });
        const input = await page.send('DOM.querySelector', { nodeId: document.root.nodeId, selector: '.scene-viewer input[type=file]' });
        assert.ok(input.nodeId, 'Scene file input not found');
        await page.send('DOM.setFileInputFiles', { nodeId: input.nodeId, files: [filename] });
        await waitFor(`scene ${path.basename(filename)}`, () => page.evaluate(condition));
        const error = await page.evaluate("document.querySelector('.scene-file-error')?.textContent || ''");
        assert.equal(error, '', `Scene open failed: ${error}`);
    }

    async function saveRange(name, start, end, stride) {
        await waitFor('viewer to finish loading', () => page.evaluate("document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'false'"));
        await page.evaluate("document.querySelector('.scene-viewer > button:not([data-scene-file-command])').click()");
        await waitFor('Save As dialog', () => page.evaluate("!!document.querySelector('dialog[open] .scene-save-range')"));
        const values = JSON.stringify([name, String(start), String(end), String(stride)]);
        await page.evaluate(`(() => { const dialog = document.querySelector('dialog[open]'); const inputs = dialog.querySelectorAll('input'); const values = ${values}; for (let i = 0; i < values.length; i++) { inputs[i].value = values[i]; inputs[i].dispatchEvent(new Event('input', { bubbles: true })); } })()`);
        await page.evaluate("document.querySelector('dialog[open] button[type=submit]').click()");
        const destination = path.join(downloads, name);
        await waitFor(`download ${name}`, () => existsSync(destination));
        await waitFor('download completion', async () => !(await readdir(downloads)).some(item => item.endsWith('.crdownload')));
        return { filename: destination, scene: JSON.parse(await readFile(destination, 'utf8')) };
    }

    await openFile(path.join(root, 'samples/LoadAndShowScene/wwwroot/scenes/example.bugscene.json'),
        "document.querySelector('.step-controls')?.textContent.includes('/ 4')");
    const gpuError = await page.evaluate("document.querySelector('.webgpu-error-overlay')?.textContent || ''");
    assert.equal(gpuError, '', `WebGPU unavailable: ${gpuError}`);
    await waitFor('opening completion', () => page.evaluate("document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'false'"));
    await page.evaluate("document.querySelector('.scene-viewer > button:not([data-scene-file-command])').click()");
    await waitFor('Save As dialog for cancellation', () => page.evaluate("!!document.querySelector('dialog[open] .scene-save-range')"));
    await page.evaluate("window.cancelNextSave = true; document.querySelector('dialog[open] button[type=submit]').click()");
    await waitFor('save cancellation', () => page.evaluate("window.fileProgressEvents.some(e => e.text.includes('Canceling')) && document.querySelector('.scene-viewer')?.getAttribute('aria-busy') === 'false'"));
    assert.deepEqual(await readdir(downloads), [], 'Canceled save started a download');
    const saved = await saveRange('range-check.bugscene.json', 2, 4, 2);
    const progressEvents = await page.evaluate('window.fileProgressEvents');
    const openProgress = progressEvents.find(event => event.text.includes('Opening scene') && event.determinate);
    const saveProgress = progressEvents.find(event => event.text.includes('Preparing scene') && !event.determinate);
    assert.ok(openProgress && saveProgress, 'Open and save progress were not displayed');
    assert.equal(openProgress.x, saveProgress.x, 'Open and save progress have different horizontal positions');
    assert.equal(openProgress.y, saveProgress.y, 'Open and save progress have different vertical positions');
    console.log('PASS: open and save share an overlay, and saving can be canceled before download.');
    assert.equal(saved.scene.initialStep, 0);
    assert.equal(saved.scene.steps.length, 2);
    assert.equal(saved.scene.steps[0].paths.length, 3, 'Visible history and grouped paths were not saved');
    assert.equal(saved.scene.steps[1].paths.length, 4, 'Final group did not retain visible history');
    assert.equal(saved.scene.steps[0].meshes[0].vertices[0][0], 0, 'Latest group transform was not saved');
    await openFile(saved.filename, "document.querySelector('.step-controls')?.textContent.includes('/ 2') && document.querySelector('.step-current')?.textContent.trim() === '1'");
    assert.equal(await page.evaluate(`['Previous step', 'Next step', 'Play'].every(label =>
        document.querySelector('.step-controls button[aria-label="' + label + '"] svg'))`), true,
    '3D playback buttons must expose named Fluent icons');
    await page.evaluate("document.querySelector('.step-controls button[aria-label=\"Next step\"]').click()");
    await waitFor('second saved step', () => page.evaluate("document.querySelector('.step-current')?.textContent.trim() === '2'"));
    await page.evaluate("document.querySelector('.step-controls button[aria-label=\"Previous step\"]').click()");
    await waitFor('previous saved step', () => page.evaluate("document.querySelector('.step-current')?.textContent.trim() === '1'"));
    await page.evaluate(`(() => {
        const speed = document.querySelector('.step-speed-control select');
        speed.value = '3000';
        speed.dispatchEvent(new Event('change', { bubbles: true }));
        document.querySelector('.step-controls button[aria-label="Play"]').click();
    })()`);
    await waitFor('pause icon', () => page.evaluate("!!document.querySelector('.step-controls button[aria-label=\"Pause\"] svg')"));
    await page.evaluate("document.querySelector('.step-controls button[aria-label=\"Pause\"]').click()");
    await waitFor('play icon', () => page.evaluate("!!document.querySelector('.step-controls button[aria-label=\"Play\"] svg')"));
    assert.equal(await page.evaluate("document.querySelector('.webgpu-error-overlay')?.textContent || ''"), '');
    console.log('PASS: opened a stepped 3D scene, saved a grouped range, and reopened both saved steps.');

    await openFile(path.join(root, 'samples/LoadAndShowScene/wwwroot/scenes/plots.bugscene.json'),
        "!!document.querySelector('.plot-viewer') && document.querySelector('.plot-viewer').parentElement.querySelector('.step-controls')?.textContent.includes('/ 2')");
    assert.equal(await page.evaluate(`['Previous step', 'Next step'].every(label =>
        document.querySelector('.step-controls button[aria-label="' + label + '"] svg'))`), true,
    '2D playback buttons must expose named Fluent icons');
    await page.evaluate("document.querySelector('.step-controls button[aria-label=\"Next step\"]').click()");
    await waitFor('second plot step', () => page.evaluate("document.querySelector('.step-controls')?.textContent.includes('2 / 2')"));
    await page.evaluate("document.querySelector('.step-controls button[aria-label=\"Previous step\"]').click()");
    await waitFor('first plot step', () => page.evaluate("document.querySelector('.step-controls')?.textContent.includes('1 / 2')"));
    const plot = await saveRange('plot-check.bugscene.json', 1, 2, 2);
    assert.equal(plot.scene.steps.length, 1);
    assert.equal(plot.scene.steps[0].plot.traces[0].name, 'overlay only');
    await openFile(plot.filename, "!!document.querySelector('.plot-viewer') && !document.querySelector('.plot-viewer').parentElement.querySelector('.step-controls') && document.querySelector('.plot-viewer').data?.[0]?.name === 'overlay only'");
    console.log('PASS: saved and reopened the final plot of a grouped 2D range.');
    page.socket.close();
} finally {
    if (browserProtocol) {
        try { await browserProtocol.send('Browser.close'); } catch { }
        browserProtocol.socket.close();
    }
    browser.kill();
    console.log(`Browser profile and downloads: ${directory}`);
}
