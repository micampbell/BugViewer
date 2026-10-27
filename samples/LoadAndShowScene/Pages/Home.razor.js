export function download(json) {
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'bugviewer-playback-measurements.json';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
