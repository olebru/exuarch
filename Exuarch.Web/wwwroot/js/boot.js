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

window.exuarchSideResize = {
    prefix: 'exuarch.width.',
    property: function (key) { return '--' + key + '-width'; },
    apply: function (key, width) {
        if (width === null) document.documentElement.style.removeProperty(this.property(key));
        else document.documentElement.style.setProperty(this.property(key), width + 'px');
    },
    restore: function () {
        try {
            for (let i = 0; i < localStorage.length; i++) {
                const name = localStorage.key(i);
                if (!name || !name.startsWith(this.prefix)) continue;
                const width = Number.parseInt(localStorage.getItem(name), 10);
                if (Number.isFinite(width)) this.apply(name.substring(this.prefix.length), width);
            }
        } catch (e) { }
    },
    save: function (key, width) {
        try {
            if (width === null) localStorage.removeItem(this.prefix + key);
            else localStorage.setItem(this.prefix + key, String(width));
        } catch (e) { }
    },
    panelOf: function (handle) {
        if (handle.dataset.target === 'prev') return handle.previousElementSibling;
        if (handle.dataset.target === 'next') return handle.nextElementSibling;
        return handle.parentElement;
    },
    widthAt: function (handle, panel, clientX) {
        const box = panel.getBoundingClientRect();
        const width = handle.dataset.grow === 'left' ? box.right - clientX : clientX - box.left;
        const min = Number.parseInt(handle.dataset.min || '120', 10);
        const max = Math.min(Number.parseInt(handle.dataset.max || '900', 10), window.innerWidth - 80);
        return Math.round(Math.min(max, Math.max(min, width)));
    },
    listen: function () {
        const self = this;
        document.addEventListener('pointerdown', function (e) {
            const handle = e.target.closest && e.target.closest('[data-side-resize]');
            if (!handle || e.button !== 0) return;
            const panel = self.panelOf(handle);
            if (!panel) return;
            e.preventDefault();
            const key = handle.dataset.sideResize;
            let width = null;
            try { handle.setPointerCapture(e.pointerId); } catch (err) { }
            handle.classList.add('dragging');
            document.documentElement.classList.add('side-resizing');
            function move(ev) {
                width = self.widthAt(handle, panel, ev.clientX);
                self.apply(key, width);
            }
            function up() {
                handle.removeEventListener('pointermove', move);
                handle.removeEventListener('pointerup', up);
                handle.removeEventListener('pointercancel', up);
                handle.classList.remove('dragging');
                document.documentElement.classList.remove('side-resizing');
                if (width !== null) self.save(key, width);
            }
            handle.addEventListener('pointermove', move);
            handle.addEventListener('pointerup', up);
            handle.addEventListener('pointercancel', up);
        });
        document.addEventListener('dblclick', function (e) {
            const handle = e.target.closest && e.target.closest('[data-side-resize]');
            if (!handle) return;
            self.apply(handle.dataset.sideResize, null);
            self.save(handle.dataset.sideResize, null);
        });
    },
};
exuarchSideResize.restore();
exuarchSideResize.listen();

try {
    if (!localStorage.getItem('exuarch.uiLevel')) {
        const returningVisitor = Object.keys(localStorage).some(function (key) { return key.startsWith('exuarch.'); });
        localStorage.setItem('exuarch.uiLevel', returningVisitor ? 'everything' : 'essentials');
    }
} catch (e) { }

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
