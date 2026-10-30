self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => {
    if (new URL(event.request.url).origin !== self.location.origin) return;
    event.respondWith(onFetch(event));
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /^staticwebapp\.config\.json$/ ];

async function onInstall(event) {
    console.info('Service worker: Install');

    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
    await self.skipWaiting();
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    try {
        return await fetch(event.request);
    } catch (error) {
        if (event.request.method !== 'GET') throw error;
        const cache = await caches.open(cacheName);
        const cached = await cache.match(offlineCacheKey(event.request));
        if (cached) return cached;
        throw error;
    }
}

function offlineCacheKey(request) {
    if (request.mode !== 'navigate') return request;
    const path = new URL(request.url).pathname;
    if (path === '/sharpc' || path === '/sharpc/') return 'sharpc/index.html';
    if (path.endsWith('/phone.html') || path.startsWith('/sharpc/')) return request;
    return 'index.html';
}
