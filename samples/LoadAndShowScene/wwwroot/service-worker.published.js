self.importScripts('./service-worker-assets.js');

const cachePrefix = 'bugviewer-offline-';
const cacheName = cachePrefix + self.assetsManifest.version;
const scopeUrl = new URL(self.registration.scope);
const assets = self.assetsManifest.assets.filter(asset =>
    !asset.url.startsWith('scenes/') &&
    !/^service-worker\.js$/.test(asset.url) &&
    /\.(dll|pdb|wasm|html|js|json|css|woff2?|png|jpe?g|gif|ico|blat|dat|webmanifest)(\?.*)?$/.test(asset.url));
const assetUrls = new Set(assets.map(asset => new URL(asset.url, scopeUrl).href));

self.addEventListener('install', event => event.waitUntil((async () => {
    const cache = await caches.open(cacheName);
    await cache.addAll(assets.map(asset => new Request(new URL(asset.url, scopeUrl),
        { integrity: asset.hash, cache: 'no-cache' })));
})()));

self.addEventListener('activate', event => event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.filter(key => key.startsWith(cachePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
})()));

self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') return;
    const url = new URL(event.request.url);
    if (url.origin !== scopeUrl.origin || !url.pathname.startsWith(scopeUrl.pathname)) return;
    const isAppNavigation = event.request.mode === 'navigate' && !assetUrls.has(url.href);
    const cacheKey = isAppNavigation ? new URL('index.html', scopeUrl) : event.request;
    event.respondWith((async () => {
        const cached = await (await caches.open(cacheName)).match(cacheKey);
        return cached || fetch(event.request);
    })());
});
