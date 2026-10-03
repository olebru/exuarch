// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
// Only the site's own files: requests to other sites, such as the fonts and the statistics, go to the network as
// they would without a service worker. Fetched from here they would count as connections, which the Content
// Security Policy keeps to this site and Umami's gateway.
self.addEventListener('fetch', event => {
    if (new URL(event.request.url).origin !== self.location.origin) return;
    event.respondWith(onFetch(event));
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
// Azure Static Web Apps reads staticwebapp.config.json but never serves it, and one asset that fails to download
// fails the whole install.
const offlineAssetsExclude = [ /^service-worker\.js$/, /^staticwebapp\.config\.json$/ ];

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
    // Take over as soon as the new version is cached, instead of waiting until every tab of the site is closed,
    // which a reload is not: otherwise a release reaches nobody who keeps a tab open.
    await self.skipWaiting();
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    // Serve the pages that are already open too; they offer to reload into the new version (see js/boot.js).
    await self.clients.claim();
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        // The introduction for phones is a page of its own, not the app.
        const shouldServeIndexHtml = event.request.mode === 'navigate' && !new URL(event.request.url).pathname.endsWith('/phone.html');

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
