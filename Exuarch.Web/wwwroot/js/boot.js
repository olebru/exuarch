// Runs first, before anything else on the page loads. Kept out of index.html so the Content Security Policy can
// forbid inline scripts.

// A phone gets a short introduction instead (phone.html), before any of the app is downloaded: the app needs a
// big screen, a keyboard and a mouse. A phone is a touch screen whose shorter side is under 600 CSS pixels, so
// tablets still get the app. "Open it here anyway" on that page comes back with ?full, which is remembered.
(function () {
    let full = new URLSearchParams(location.search).has('full');
    if (full) history.replaceState(null, '', location.pathname + location.hash);
    try {
        if (full) localStorage.setItem('exuarch.full', '1');
        else full = localStorage.getItem('exuarch.full') === '1';
    } catch (e) { }
    const phone = Math.min(screen.width, screen.height) < 600 && matchMedia('(pointer: coarse)').matches;
    if (phone && !full) {
        // Nothing below is needed, so none of it is downloaded. Stopping first, because stop() would also
        // cancel a navigation that had already started.
        window.stop();
        location.replace('phone.html');
    }
})();

try { document.documentElement.dataset.theme = localStorage.getItem('exuarch.theme') === 'light' ? 'light' : 'dark'; }
catch (e) { document.documentElement.dataset.theme = 'dark'; }

// The service worker keeps the app working offline. Registered once the page has loaded, so it does not compete
// with the app's own downloads. The browser looks for a new release only now and then by itself, so the page asks
// at every load, and every hour for a tab that stays open, past any copy of the worker's files the browser keeps.
window.addEventListener('load', function () {
    if (!('serviceWorker' in navigator)) return;
    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }).then(function (registration) {
        const check = function () { registration.update().catch(function () { }); };
        check();
        setInterval(check, 60 * 60 * 1000);
    }).catch(function () { });
});

// A page loads the latest release, and keeps running it. When a newer one comes out while the page is open, its
// service worker takes over, and the page says a new version is ready and reloads when asked, rather than cutting
// a running machine or unsaved typing short. Which release is which comes from the service worker's list of the
// release's files: the one the page started with against the one there is now.
function exuarchRelease() {
    return fetch('service-worker-assets.js', { cache: 'no-store' })
        .then(function (response) { return response.ok ? response.text() : ''; })
        .then(function (text) { return (text.match(/"version":\s*"([^"]+)"/) || [])[1] || null; })
        .catch(function () { return null; });
}
if ('serviceWorker' in navigator) {
    const started = exuarchRelease();
    navigator.serviceWorker.addEventListener('controllerchange', function () {
        Promise.all([started, exuarchRelease()]).then(function (releases) {
            if (!releases[0] || !releases[1] || releases[0] === releases[1]) return;
            showUpdateNotice();
        });
    });
}
function showUpdateNotice() {
    if (document.querySelector('.update-notice')) return;
    const notice = document.createElement('div');
    notice.className = 'update-notice';
    notice.setAttribute('role', 'status');
    const text = document.createElement('span');
    text.textContent = 'A new version of ExµArch is ready.';
    const reload = document.createElement('button');
    reload.type = 'button';
    reload.textContent = 'Reload';
    reload.addEventListener('click', function () { location.reload(); });
    const later = document.createElement('button');
    later.type = 'button';
    later.className = 'later';
    later.textContent = 'Later';
    later.addEventListener('click', function () { notice.remove(); });
    notice.append(text, reload, later);
    document.body.appendChild(notice);
}
