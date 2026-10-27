import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

class Element extends EventTarget {
    constructor(parent = null) { super(); this.parent = parent; this.disabled = false; this.clicks = 0; this.dataset = {}; }
    contains(target) { return target === this || !!target?.parent && this.contains(target.parent); }
    closest(selector) {
        if (selector.startsWith('[data-scene-file-command'))
            return this.dataset.sceneFileCommand === 'open' ? this : this.parent?.closest(selector);
        return this.editable ? this : this.parent?.closest(selector);
    }
    getAttribute(name) { return this.attributes?.[name] ?? null; }
    querySelector() { return this.dialog?.open ? this.dialog : null; }
    click() {
        if (this.disabled) return;
        this.clicks++;
        const event = new Event('click');
        Object.defineProperty(event, 'target', { value: this });
        for (let node = this; node; node = node.parent) node.dispatchEvent(event);
    }
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
        open.dataset.sceneFileCommand = 'open';
        const binding = module.namespace.initialize(root, input, open, save);
        return { root, input, open, save, binding };
    }
    function key(key, overrides = {}) {
        const event = new Event('keydown', { cancelable: true });
        for (const [name, value] of Object.entries({ key, ctrlKey: true, altKey: true, target: document.body, ...overrides }))
            Object.defineProperty(event, name, { value });
        document.dispatchEvent(event);
        return event;
    }
    return { viewer, key, document, api: module.namespace };
}

test('open executes inside the key event, save targets the scene, and repeats are suppressed', async () => {
    const h = await harness();
    const v = h.viewer();
    v.input.value = 'previous.json';
    assert.equal(h.key('o').defaultPrevented, true);
    assert.equal(v.input.clicks, 1); // No async callback may precede the browser file picker.
    assert.equal(v.input.value, ''); // Selecting the same file again must raise change.
    assert.equal(h.key('S').defaultPrevented, true);
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
    for (const modifiers of [{ ctrlKey: false }, { shiftKey: true }, { altKey: false }, { metaKey: true },
        { isComposing: true }, { getModifierState: name => name === 'AltGraph' }])
        for (const key of ['o', 's'])
            assert.equal(h.key(key, modifiers).defaultPrevented, false);
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

test('Camera open icons added after initialization use the same picker and honor disabled state', async () => {
    const h = await harness(), v = h.viewer();
    const popover = new Element(v.root), icon = new Element(popover), image = new Element(icon);
    icon.dataset.sceneFileCommand = 'open';
    image.click();
    assert.equal(v.input.clicks, 1);
    icon.attributes = { 'aria-disabled': 'true' };
    image.click();
    assert.equal(v.input.clicks, 1);
    v.binding.dispose();
    icon.attributes = {};
    image.click();
    assert.equal(v.input.clicks, 1);
});

test('Save As focuses the filename and prevents file commands while its modal is open', async () => {
    const h = await harness(), v = h.viewer();
    const dialog = { open: false, showModal() { this.open = true; }, close() { this.open = false; } };
    const input = { focus() { this.focused = true; }, select() { this.selected = true; } };
    v.root.dialog = dialog;
    h.api.showSaveAs(dialog, input);
    assert.equal(dialog.open && input.focused && input.selected, true);
    h.key('o'); h.key('s');
    assert.equal(v.input.clicks + v.save.clicks, 0);
    h.api.closeSaveAs(dialog);
    assert.equal(dialog.open, false);
    h.key('s');
    assert.equal(v.save.clicks, 1);
});
