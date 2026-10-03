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
// at every load, and every hour for a tab that stays open.
window.addEventListener('load', function () {
    if (!('serviceWorker' in navigator)) return;
    navigator.serviceWorker.register('service-worker.js').then(function (registration) {
        const check = function () { registration.update().catch(function () { }); };
        check();
        setInterval(check, 60 * 60 * 1000);
    }).catch(function () { });
});

// A new release's service worker takes over open pages as soon as it has the new version. This page keeps
// running the version it started with, so it says a new one is ready and reloads when asked, rather than
// cutting a running machine or unsaved typing short. A first visit has no worker yet, so its first one taking
// over says nothing.
if ('serviceWorker' in navigator) {
    const updating = !!navigator.serviceWorker.controller;
    navigator.serviceWorker.addEventListener('controllerchange', function () {
        if (!updating || document.querySelector('.update-notice')) return;
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
    });
}
