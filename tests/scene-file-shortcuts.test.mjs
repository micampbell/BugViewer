import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

class Element extends EventTarget {
    constructor(parent = null) { super(); this.parent = parent; this.disabled = false; this.clicks = 0; this.dataset = {}; }
    contains(target) { return target === this || !!target?.parent && this.contains(target.parent); }
    closest() { return this.editable ? this : this.parent?.closest(); }
    click() { if (!this.disabled) { this.clicks++; this.dispatchEvent(new Event('click')); } }
}

async function harness() {
    const document = new Element();
    document.body = new Element(document);
    const context = vm.createContext({ document, AbortController });
    const source = await readFile(new URL('../BugViewer/Presentation/SceneRequestViewer.razor.js', import.meta.url), 'utf8');
    const module = new vm.SourceTextModule(source, { context });
    await module.link(() => { throw new Error('Unexpected import'); });
    await module.evaluate();
    function viewer() {
        const root = new Element(document.body);
        root.dataset.sceneShortcuts = 'true';
        const input = new Element(root), open = new Element(root), save = new Element(root);
        const binding = module.namespace.initialize(root, input, open, save);
        return { root, input, open, save, binding };
    }
    function key(key, overrides = {}) {
        const event = new Event('keydown', { cancelable: true });
        for (const [name, value] of Object.entries({ key, ctrlKey: true, target: document.body, ...overrides }))
            Object.defineProperty(event, name, { value });
        document.dispatchEvent(event);
        return event;
    }
    return { viewer, key, document };
}

test('open executes inside the key event, save targets the scene, and repeats are suppressed', async () => {
    const h = await harness();
    const v = h.viewer();
    v.input.value = 'previous.json';
    assert.equal(h.key('o').defaultPrevented, true);
    assert.equal(v.input.clicks, 1); // No async callback may precede the browser file picker.
    assert.equal(v.input.value, ''); // Selecting the same file again must raise change.
    assert.equal(h.key('S', { ctrlKey: false, metaKey: true }).defaultPrevented, true);
    assert.equal(v.save.clicks, 1);
    assert.equal(h.key('s', { repeat: true }).defaultPrevented, true);
    assert.equal(v.save.clicks, 1);
    v.open.disabled = v.save.disabled = true;
    assert.equal(h.key('o').defaultPrevented, true);
    assert.equal(h.key('s').defaultPrevented, true);
    assert.equal(v.input.clicks, 1);
    assert.equal(v.save.clicks, 1);
});

test('shortcuts follow focus or the last interacted viewer and leave outside editors alone', async () => {
    const h = await harness();
    const first = h.viewer(), second = h.viewer();
    assert.equal(h.key('s').defaultPrevented, false); // No active viewer yet.
    first.root.dispatchEvent(new Event('pointerdown'));
    h.key('s');
    assert.equal(first.save.clicks, 1);
    second.root.dispatchEvent(new Event('focusin'));
    h.key('s');
    assert.equal(second.save.clicks, 1);
    h.key('s', { target: first.save });
    assert.equal(first.save.clicks, 2);
    const editor = new Element(h.document.body);
    editor.editable = true;
    assert.equal(h.key('s', { target: editor }).defaultPrevented, false);
    assert.equal(second.save.clicks, 1);
    second.root.dataset.sceneShortcuts = 'false';
    assert.equal(h.key('o', { target: second.root }).defaultPrevented, false);
    second.open.click(); // Disabling keyboard shortcuts does not disable the button.
    assert.equal(second.input.clicks, 1);
});

test('unrelated modifiers are untouched, and disposal removes listeners and active state', async () => {
    const h = await harness();
    const first = h.viewer(), second = h.viewer();
    first.root.dispatchEvent(new Event('focusin'));
    for (const modifiers of [{ ctrlKey: false }, { shiftKey: true }, { altKey: true }, { isComposing: true }])
        assert.equal(h.key('s', modifiers).defaultPrevented, false);
    assert.equal(h.key('p').defaultPrevented, false);
    first.binding.dispose();
    h.key('s');
    assert.equal(first.save.clicks, 0);
    assert.equal(second.save.clicks, 1); // Remaining sole viewer becomes the fallback.
    first.open.click();
    assert.equal(first.input.clicks, 0);
    second.binding.dispose();
    assert.equal(h.key('o').defaultPrevented, false);
    const replacement = h.viewer();
    h.key('o');
    assert.equal(replacement.input.clicks, 1);
    replacement.binding.dispose();
});
