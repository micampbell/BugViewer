const viewers = new Set();
let activeViewer;

function handleKeyDown(event) {
    if (event.defaultPrevented || event.isComposing || event.shiftKey || event.metaKey
        || !event.ctrlKey || !event.altKey || event.getModifierState?.('AltGraph')) return;
    const key = event.key.toLowerCase();
    if (key !== 'o' && key !== 's') return;

    const containingViewer = [...viewers].find(viewer => viewer.contains(event.target));
    // Leave shortcuts in editors outside the viewer to their owning application.
    if (!containingViewer && event.target?.closest?.('input, textarea, select, [contenteditable]:not([contenteditable="false"])')) return;
    const viewer = containingViewer ?? activeViewer ?? (viewers.size === 1 ? [...viewers][0] : null);
    if (!viewer?.enabled || (key === 'o' && !viewer.openEnabled) || (key === 's' && !viewer.saveEnabled)) return;

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

    constructor(container, input, openButton, saveButton) {
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
    }

    contains(element) { return this.#container.contains(element); }
    get enabled() { return this.#container.dataset.sceneShortcuts === 'true'; }
    get openEnabled() { return this.#container.dataset.sceneOpenEnabled !== 'false'; }
    get saveEnabled() { return this.#container.dataset.sceneSaveEnabled !== 'false'; }
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

export function initialize(container, input, openButton, saveButton) {
    return new SceneFileControls(container, input, openButton, saveButton);
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
