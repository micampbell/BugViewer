import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

async function harness() {
    const scripts = [], renders = [], purged = [];
    const plotly = {
        react: async (...args) => { renders.push(args); },
        purge: element => purged.push(element)
    };
    const context = vm.createContext({ URL, document: {
        createElement: () => ({ remove() { this.removed = true; } }),
        head: { appendChild: script => scripts.push(script) }
    } });
    const module = new vm.SourceTextModule(await readFile(
        new URL('../BugViewer/Presentation/PlotViewer.razor.js', import.meta.url), 'utf8'), {
        context,
        initializeImportMeta(meta) {
            meta.url = 'https://example.test/app/_content/BugViewer/Presentation/PlotViewer.razor.js';
        }
    });
    await module.link(() => { throw new Error('Unexpected import'); });
    await module.evaluate();
    return { api: module.namespace, scripts, renders, purged,
        loaded() { context.Plotly = plotly; scripts.at(-1).onload(); } };
}

test('2D viewers share one lazy asset load and dispose only their own plot', async () => {
    const h = await harness();
    assert.equal(h.scripts.length, 0);
    const first = {}, second = {};
    const rendering = Promise.all([
        h.api.render(first, { traces: [] }, 'first'),
        h.api.render(second, { traces: [] }, 'second')
    ]);
    assert.equal(h.scripts.length, 1);
    assert.equal(h.scripts[0].src, 'https://example.test/app/_content/BugViewer/lib/plotly/plotly.min.js');
    h.loaded();
    await rendering;
    assert.equal(h.renders.length, 2);
    h.api.dispose(first);
    assert.deepEqual(h.purged, [first]);
});

test('plot asset loading can recover after a failed request', async () => {
    const h = await harness();
    const failed = h.api.render({}, { traces: [] }, 'failed');
    h.scripts[0].onerror();
    await assert.rejects(failed, /Could not load Plotly/);
    assert.equal(h.scripts[0].removed, true);
    const retried = h.api.render({}, { traces: [] }, 'retry');
    assert.equal(h.scripts.length, 2);
    h.loaded();
    await retried;
    assert.equal(h.renders.length, 1);
});

test('restored plot enum values, heatmaps, and empty frames reach Plotly correctly', async () => {
    const h = await harness();
    const element = {};
    const trace = { name: 'area', x: [0, 1], y: [2, 3], type: 4, marker: 7,
        closed: true, color: { r: 255, g: 0, b: 0, a: 255 } };
    const rendering = h.api.render(element, { traces: [trace], heatmap: [[1, 2], [3, 4]] }, 'plot');
    h.loaded();
    await rendering;
    const rendered = h.renders[0][1];
    assert.equal(rendered[0].fill, 'tozeroy');
    assert.equal(rendered[0].marker.symbol, 'star');
    assert.deepEqual(Array.from(rendered[0].x), [0, 1, 0]);
    assert.deepEqual(trace.x, [0, 1]); // Closing a trace must not mutate source data saved later.
    assert.equal(rendered[1].type, 'heatmap');
    await h.api.render(element, { traces: [] }, 'empty');
    assert.equal(h.renders[1][1].length, 0);
});
