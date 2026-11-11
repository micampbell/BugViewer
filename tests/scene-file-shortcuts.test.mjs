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
    querySelector(selector) {
        if (selector === 'dialog[open]') return this.dialog?.open ? this.dialog : null;
        const step = /^\[data-scene-step-command="([jkl])"\]$/.exec(selector);
        return step ? this.stepButtons?.[step[1]] ?? null : null;
    }
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
    const window = {};
    const context = vm.createContext({ document, window, AbortController, DotNet: { createJSObjectReference: value => value }, console });
    const source = await readFile(new URL('../BugViewer/Presentation/SceneViewer.razor.js', import.meta.url), 'utf8');
    const module = new vm.SourceTextModule(source, { context });
    await module.link(() => { throw new Error('Unexpected import'); });
    await module.evaluate();
    function viewer(dotNet = {}) {
        const root = new Element(document.body);
        root.dataset.sceneShortcuts = 'true';
        const input = new Element(root), open = new Element(root), save = new Element(root);
        open.dataset.sceneFileCommand = 'open';
        const binding = module.namespace.initialize(root, input, open, save, dotNet);
        return { root, input, open, save, binding };
    }
    function key(key, overrides = {}) {
        const event = new Event('keydown', { cancelable: true });
        for (const [name, value] of Object.entries({ key, ctrlKey: true, altKey: true, target: document.body, ...overrides }))
            Object.defineProperty(event, name, { value });
        document.dispatchEvent(event);
        return event;
    }
    return { viewer, key, document, window, api: module.namespace };
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

test('step shortcuts target the active stepped viewer independently of file shortcuts', async () => {
    const h = await harness(), first = h.viewer(), second = h.viewer();
    const makeSteps = viewer => viewer.root.stepButtons = Object.fromEntries(
        ['j', 'k', 'l'].map(key => [key, new Element(viewer.root)]));
    makeSteps(first);
    makeSteps(second);
    first.root.dataset.sceneShortcuts = 'false';
    assert.equal(h.key('j', { ctrlKey: false, altKey: false }).defaultPrevented, false);
    first.root.dispatchEvent(new Event('focusin'));
    for (const key of ['j', 'K', 'l'])
        assert.equal(h.key(key, { ctrlKey: false, altKey: false }).defaultPrevented, true);
    assert.deepEqual(Object.values(first.root.stepButtons).map(button => button.clicks), [1, 1, 1]);
    h.key('j', { ctrlKey: false, altKey: false, repeat: true });
    h.key('k', { ctrlKey: false, altKey: false, repeat: true });
    assert.equal(first.root.stepButtons.j.clicks, 2);
    assert.equal(first.root.stepButtons.k.clicks, 1);
    second.root.dispatchEvent(new Event('pointerdown'));
    h.key('l', { ctrlKey: false, altKey: false });
    assert.equal(second.root.stepButtons.l.clicks, 1);
    h.key('j', { ctrlKey: false, altKey: false, target: first.root });
    assert.equal(first.root.stepButtons.j.clicks, 3);
    delete second.root.stepButtons;
    assert.equal(h.key('l', { ctrlKey: false, altKey: false }).defaultPrevented, false);
    first.binding.dispose(); second.binding.dispose();
});

test('step shortcuts leave editors, modifiers and dialogs alone', async () => {
    const h = await harness(), v = h.viewer();
    v.root.stepButtons = { j: new Element(v.root), k: new Element(v.root), l: new Element(v.root) };
    const editor = new Element(v.root); editor.editable = true;
    for (const overrides of [
        { target: editor }, { ctrlKey: true }, { altKey: true }, { metaKey: true },
        { shiftKey: true }, { isComposing: true }, { getModifierState: name => name === 'AltGraph' }
    ]) {
        assert.equal(h.key('j', { ctrlKey: false, altKey: false, ...overrides }).defaultPrevented, false);
    }
    v.root.dialog = { open: true };
    assert.equal(h.key('k', { ctrlKey: false, altKey: false }).defaultPrevented, false);
    assert.deepEqual(Object.values(v.root.stepButtons).map(button => button.clicks), [0, 0, 0]);
    v.binding.dispose();
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

test('a save-only viewer leaves the open shortcut untouched', async () => {
    const h = await harness(), v = h.viewer();
    v.root.dataset.sceneOpenEnabled = 'false';
    v.open.disabled = v.input.disabled = true;
    assert.equal(h.key('o').defaultPrevented, false);
    assert.equal(v.input.clicks, 0);
    assert.equal(h.key('s').defaultPrevented, true);
    assert.equal(v.save.clicks, 1);
    v.binding.dispose();
});

test('native save picker opens synchronously, restricts the extension and passes the selected file', async () => {
    const h = await harness(), calls = [];
    const v = h.viewer({ invokeMethodAsync: async (...args) => calls.push(args) });
    let options;
    h.window.showSaveFilePicker = args => { options = args; return Promise.resolve({ name: 'picked.bugscene' }); };
    const values = { '[data-save-name]': 'old.bugscene.json', '[data-save-start]': '1',
        '[data-save-end]': '4', '[data-save-stride]': '2', '[data-save-maximum-earlier]': '37' };
    const form = { dataset: { stepCount: '4' }, matches: () => true,
        querySelector: selector => ({ value: values[selector] }) };
    const event = new Event('submit', { cancelable: true });
    Object.defineProperty(event, 'target', { value: form });
    v.root.dispatchEvent(event);
    assert.equal(event.defaultPrevented, true);
    assert.equal(options.suggestedName, 'old.bugscene');
    assert.equal(options.excludeAcceptAllOption, true);
    assert.deepEqual(Array.from(options.types[0].accept['application/json']), ['.bugscene']);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(calls[0][0], 'ConfirmPickedSceneSaveAsync');
    assert.deepEqual(calls[0].slice(1, 5), ['picked.bugscene', '1', '4', '2']);
    assert.equal(calls[0][6], '37');
    v.binding.dispose();
});

test('invalid ranges do not open a native picker, and a differently named file is not written', async () => {
    const h = await harness(), calls = [];
    const v = h.viewer({ invokeMethodAsync: async (...args) => calls.push(args) });
    let picks = 0;
    h.window.showSaveFilePicker = () => { picks++; return Promise.resolve({ name: 'wrong.txt' }); };
    const values = { '[data-save-name]': 'part', '[data-save-start]': '0',
        '[data-save-end]': '2', '[data-save-stride]': '1' };
    const form = { dataset: { stepCount: '2' }, matches: () => true,
        querySelector: selector => ({ value: values[selector] }) };
    function submit() {
        const event = new Event('submit', { cancelable: true });
        Object.defineProperty(event, 'target', { value: form });
        v.root.dispatchEvent(event);
        return event;
    }
    assert.equal(submit().defaultPrevented, true);
    assert.equal(picks, 0);
    assert.equal(calls[0][0], 'ConfirmPickedSceneSaveAsync');
    calls.length = 0;
    values['[data-save-start]'] = '1';
    assert.equal(submit().defaultPrevented, true);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(calls[0][0], 'ReportSceneSaveError');
    assert.equal(calls.length, 1);
    v.binding.dispose();
});

test('picker cancellation leaves the dialog alone and unavailable pickers use the download path', async () => {
    const h = await harness(), calls = [];
    const v = h.viewer({ invokeMethodAsync: async (...args) => calls.push(args) });
    const form = { dataset: { stepCount: '0' }, matches: () => true,
        querySelector: selector => selector === '[data-save-name]' ? { value: 'part.json' } : null };
    const submit = () => {
        const event = new Event('submit', { cancelable: true });
        Object.defineProperty(event, 'target', { value: form });
        v.root.dispatchEvent(event);
        return event;
    };
    h.window.showSaveFilePicker = () => Promise.reject({ name: 'AbortError' });
    assert.equal(submit().defaultPrevented, true);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(calls.length, 0);
    delete h.window.showSaveFilePicker;
    assert.equal(submit().defaultPrevented, true);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(calls[0][0], 'ConfirmPickedSceneSaveAsync');
    assert.equal(calls[0][5], null);
    v.binding.dispose();
});

test('writing a selected file closes on success and aborts on failure', async () => {
    const h = await harness();
    const data = new Uint8Array([1, 2, 3]);
    const actions = [];
    const handle = { createWritable: async () => ({
        write: async value => { actions.push(value); },
        close: async () => { actions.push('close'); },
        abort: async () => { actions.push('abort'); }
    }) };
    await h.api.writeSceneFile(handle, { arrayBuffer: async () => data });
    assert.deepEqual(actions, [data, 'close']);
    actions.length = 0;
    await assert.rejects(h.api.writeSceneFile(handle, { arrayBuffer: async () => { throw new Error('read failed'); } }));
    assert.deepEqual(actions, ['abort']);
});
