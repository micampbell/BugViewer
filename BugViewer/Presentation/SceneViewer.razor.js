const viewers = new Set();
let activeViewer;

function handleKeyDown(event) {
    if (event.defaultPrevented || event.isComposing || event.shiftKey || event.metaKey
        || event.getModifierState?.('AltGraph')) return;
    const key = event.key.toLowerCase();
    const fileShortcut = event.ctrlKey && event.altKey && (key === 'o' || key === 's');
    const stepShortcut = !event.ctrlKey && !event.altKey && (key === 'j' || key === 'k' || key === 'l');
    if (!fileShortcut && !stepShortcut) return;

    const containingViewer = [...viewers].find(viewer => viewer.contains(event.target));
    // Plain-letter shortcuts must never interfere with typing, including inside the viewer.
    const editor = event.target?.closest?.('input, textarea, select, [contenteditable]:not([contenteditable="false"])');
    if (editor && (stepShortcut || !containingViewer)) return;
    const viewer = containingViewer ?? activeViewer ?? (viewers.size === 1 ? [...viewers][0] : null);
    if (!viewer) return;
    if (stepShortcut) {
        const button = viewer.stepButton(key);
        if (!button) return;
        event.preventDefault();
        event.stopPropagation();
        if (key !== 'k' || !event.repeat) button.click();
        return;
    }
    if (!viewer.enabled || (key === 'o' && !viewer.openEnabled) || (key === 's' && !viewer.saveEnabled)) return;

    event.preventDefault();
    event.stopPropagation();
    if (!event.repeat) viewer.command(key);
}

class SceneFileControls {
    #container;
    #input;
    #openButton;
    #saveButton;
    #events = new AbortController();
    #picking = false;

    constructor(container, input, openButton, saveButton, dotNet) {
        this.#container = container;
        this.#input = input;
        this.#openButton = openButton;
        this.#saveButton = saveButton;
        if (viewers.size === 0) document.addEventListener('keydown', handleKeyDown, true);
        viewers.add(this);
        const options = { signal: this.#events.signal };
        container.addEventListener('pointerdown', () => { activeViewer = this; }, options);
        container.addEventListener('focusin', () => { activeViewer = this; }, options);
        // Delegate so Camera popover buttons created later use the same browser gesture.
        container.addEventListener('click', event => {
            const button = event.target?.closest?.('[data-scene-file-command="open"]');
            if (!button || !container.contains(button) || button.disabled
                || button.getAttribute('aria-disabled') === 'true' || input.disabled) return;
            // Keep the picker in the browser's user gesture, including in Blazor Server.
            // Reset before opening so choosing the same file again raises a change event.
            input.value = '';
            input.click();
        }, { ...options, capture: true });
        container.addEventListener('submit', event => {
            const form = event.target;
            if (!form.matches?.('[data-scene-save-form]')) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            const name = form.querySelector('[data-save-name]')?.value.trim() ?? '';
            const count = Number(form.dataset.stepCount);
            const start = form.querySelector('[data-save-start]')?.value ?? '1';
            const end = form.querySelector('[data-save-end]')?.value ?? '1';
            const stride = form.querySelector('[data-save-stride]')?.value ?? '1';
            const maximumEarlier = form.querySelector('[data-save-maximum-earlier]')?.value ?? '50000';
            const validNumber = value => /^[+-]?\d+$/.test(value.trim()) && Number.isSafeInteger(Number(value));
            const invalid = !name || /[<>:"/\\|?*\x00-\x1f\x7f-\x9f]/.test(name) || name.endsWith('.') ||
                (count > 0 && (![start, end, stride].every(validNumber) || Number(start) < 1 ||
                    Number(end) < Number(start) || Number(end) > count || Number(stride) < 1 || Number(stride) > 2147483647 ||
                    !validNumber(maximumEarlier) || Number(maximumEarlier) < 0 || Number(maximumEarlier) > 2147483647));
            if (this.#picking) return;
            // .NET owns validation and the fallback download; no native picker is shown for invalid input.
            if (invalid || typeof window.showSaveFilePicker !== 'function') {
                dotNet.invokeMethodAsync('ConfirmPickedSceneSaveAsync', name, start, end, stride, null, maximumEarlier)
                    .catch(error => console.error('Could not start scene save:', error));
                return;
            }
            this.#picking = true;
            const suggestedName = sceneFileName(name);
            // Invoke synchronously from the submit gesture, including for Blazor Server.
            let picker;
            try {
                picker = window.showSaveFilePicker({ suggestedName, id: 'bugviewer-scene',
                    types: [{ description: 'BugViewer scene', accept: { 'application/json': ['.bugscene'] } }],
                    excludeAcceptAllOption: true });
            } catch (error) {
                this.#picking = false;
                dotNet.invokeMethodAsync('ReportSceneSaveError', error.message || String(error))
                    .catch(callbackError => console.error('Could not report save error:', callbackError));
                return;
            }
            picker.then(async handle => {
                    if (!handle.name.toLowerCase().endsWith('.bugscene')) {
                        await dotNet.invokeMethodAsync('ReportSceneSaveError', 'Choose a file name ending in .bugscene.');
                        return;
                    }
                    await dotNet.invokeMethodAsync('ConfirmPickedSceneSaveAsync', handle.name, start, end, stride,
                        DotNet.createJSObjectReference(handle), maximumEarlier);
                })
                .catch(async error => {
                    if (error.name === 'AbortError') return;
                    try { await dotNet.invokeMethodAsync('ReportSceneSaveError', error.message || String(error)); }
                    catch (callbackError) { console.error('Could not report save error:', callbackError); }
                })
                .finally(() => { this.#picking = false; });
        }, { ...options, capture: true });
    }

    contains(element) { return this.#container.contains(element); }
    get enabled() { return this.#container.dataset.sceneShortcuts === 'true'; }
    get openEnabled() { return this.#container.dataset.sceneOpenEnabled !== 'false'; }
    get saveEnabled() { return this.#container.dataset.sceneSaveEnabled !== 'false'; }
    stepButton(key) {
        if (this.#container.querySelector('dialog[open]')) return null;
        return this.#container.querySelector(`[data-scene-step-command="${key}"]`);
    }
    command(key) {
        if (this.#container.querySelector('dialog[open]')) return;
        (key === 'o' ? this.#openButton : this.#saveButton).click();
    }

    dispose() {
        this.#events.abort();
        viewers.delete(this);
        if (activeViewer === this) activeViewer = undefined;
        if (viewers.size === 0) document.removeEventListener('keydown', handleKeyDown, true);
    }
}

export function initialize(container, input, openButton, saveButton, dotNet) {
    return new SceneFileControls(container, input, openButton, saveButton, dotNet);
}

function sceneFileName(name) {
    if (name.toLowerCase().endsWith('.bugscene')) return name;
    if (name.toLowerCase().endsWith('.json')) name = name.slice(0, -5);
    return name.toLowerCase().endsWith('.bugscene') ? name : name + '.bugscene';
}

export function showSaveAs(dialog, input) {
    if (!dialog.open) dialog.showModal();
    input.focus();
    input.select();
}

export function closeSaveAs(dialog) { dialog.close(); }

export async function downloadScene(name, stream) {
    const buffer = await stream.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    try {
        link.click();
    } finally {
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
}

export async function writeSceneFile(handle, stream) {
    const writable = await handle.createWritable();
    try {
        await writable.write(await stream.arrayBuffer());
        await writable.close();
    } catch (error) {
        try { await writable.abort(); } catch { /* Preserve the original error. */ }
        throw error;
    }
}
