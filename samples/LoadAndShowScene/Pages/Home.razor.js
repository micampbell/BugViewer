const OPEN_FILE = 'OpenLaunchedFileAsync';
const UPDATE_READY = 'NotifyUpdateAvailableAsync';
const LAUNCH_ERROR = 'ReportLaunchErrorAsync';
let appReference;
let registration;
let pendingFiles = Promise.resolve();
let controllerChange;

export async function initializeApp(reference) {
    appReference = reference;
    if ('launchQueue' in window) {
        window.launchQueue.setConsumer(params => {
            for (const handle of params.files) {
                pendingFiles = pendingFiles.then(async () => {
                    if (!appReference) return;
                    const file = await handle.getFile();
                    const stream = DotNet.createJSStreamReference(file);
                    await appReference.invokeMethodAsync(OPEN_FILE, file.name, file.size, stream);
                }).catch(async error => {
                    console.error('Could not open launched scene:', error);
                    if (appReference) {
                        try { await appReference.invokeMethodAsync(LAUNCH_ERROR, String(error)); }
                        catch (callbackError) { console.error('Could not report launch error:', callbackError); }
                    }
                });
            }
        });
    }

    if ('serviceWorker' in navigator) {
        try {
            registration = await navigator.serviceWorker.register(new URL('service-worker.js', document.baseURI));
            const notifyWaiting = () => {
                if (registration.waiting && navigator.serviceWorker.controller && appReference)
                    appReference.invokeMethodAsync(UPDATE_READY).catch(error => console.error('Update notification failed:', error));
            };
            notifyWaiting();
            registration.addEventListener('updatefound', () => {
                const worker = registration.installing;
                worker?.addEventListener('statechange', () => { if (worker.state === 'installed') notifyWaiting(); });
            });
        } catch (error) { console.error('Offline support could not be registered:', error); }
    }
}

export function applyUpdate() {
    if (!registration?.waiting) return;
    controllerChange = () => window.location.reload();
    navigator.serviceWorker.addEventListener('controllerchange', controllerChange, { once: true });
    registration.waiting.postMessage({ type: 'SKIP_WAITING' });
}

export function disposeApp() {
    appReference = null;
    if (controllerChange) navigator.serviceWorker.removeEventListener('controllerchange', controllerChange);
}

export function download(json) {
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'bugviewer-playback-measurements.json';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
