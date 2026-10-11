(function () {
    const fullInAddress = new URLSearchParams(location.search).has('full');
    if (fullInAddress) history.replaceState(null, '', location.pathname + location.hash);
    const full = fullAppChosenForThisTab(fullInAddress);
    const phoneMaxShortSideCssPixels = 600;
    const phone = Math.min(screen.width, screen.height) < phoneMaxShortSideCssPixels && matchMedia('(pointer: coarse)').matches;
    if (phone && !full) stopDownloadsBeforeOpeningPhonePage();

    function fullAppChosenForThisTab(fullInAddress) {
        try {
            removeFullFlagKeptByEarlierReleases();
            if (fullInAddress) sessionStorage.setItem('exuarch.full', '1');
            return fullInAddress || sessionStorage.getItem('exuarch.full') === '1';
        } catch {
            return fullInAddress;
        }
    }

    function removeFullFlagKeptByEarlierReleases() {
        localStorage.removeItem('exuarch.full');
    }

    function stopDownloadsBeforeOpeningPhonePage() {
        window.stop();
        location.replace('phone.html');
    }
})();

try { document.documentElement.dataset.theme = localStorage.getItem('exuarch.theme') === 'light' ? 'light' : 'dark'; }
catch (e) { document.documentElement.dataset.theme = 'dark'; }

window.addEventListener('load', function () {
    if (!('serviceWorker' in navigator)) return;
    const register = function () {
        exuarchRelease().then(function (release) {
            const addressNamingRelease = 'service-worker.js' + (release ? '?release=' + encodeURIComponent(release) : '');
            return navigator.serviceWorker.register(addressNamingRelease, { updateViaCache: 'none' });
        }).then(function (registration) {
            return registration.update();
        }).catch(function () { });
    };
    register();
    setInterval(register, 60 * 60 * 1000);
});

function exuarchRelease() {
    return fetch('service-worker-assets.js', { cache: 'no-store' })
        .then(function (response) { return response.ok ? response.text() : ''; })
        .then(function (text) { return (/"version":\s*"([^"]+)"/.exec(text) || [])[1] || null; })
        .catch(function () { return null; });
}
if ('serviceWorker' in navigator) {
    const started = exuarchRelease();
    navigator.serviceWorker.addEventListener('controllerchange', function () {
        Promise.all([started, exuarchRelease()]).then(function (releases) {
            if (!releases[0] || !releases[1] || releases[0] === releases[1]) return;
            showUpdateNotice();
        }).catch(function () { });
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
