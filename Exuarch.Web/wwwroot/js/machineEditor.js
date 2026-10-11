document.addEventListener('dragstart', function addDragDataFirefoxRequiresToStartDrag(e) {
    if (e.dataTransfer && e.dataTransfer.types.length === 0) e.dataTransfer.setData('text/plain', '');
});

window.exuarchEditor = {
    rect: function (element) {
        const r = element.getBoundingClientRect();
        return { left: r.left, top: r.top, width: r.width, height: r.height };
    },
    scrollToId: function (id) {
        const element = document.getElementById(id);
        if (!element) return;
        let box = element.parentElement;
        while (box && !/(auto|scroll)/.test(getComputedStyle(box).overflowY)) box = box.parentElement;
        if (!box || box.scrollHeight <= box.clientHeight) return;
        const e = element.getBoundingClientRect(), b = box.getBoundingClientRect();
        if (e.top < b.top) box.scrollTop -= b.top - e.top;
        else if (e.bottom > b.bottom) box.scrollTop += e.bottom - b.bottom;
    },
    focus: function (element) {
        element.focus({ preventScroll: true });
    },
    download: function (fileName, text) {
        const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        URL.revokeObjectURL(url);
    }
};

window.exuarchReadme = {
    scrollTop: function (element) {
        if (element) element.scrollTop = 0;
    },
    attach: function (element, dotnet) {
        element.addEventListener('click', function (e) {
            const link = e.target.closest('a');
            if (!link || !element.contains(link)) return;
            const href = link.getAttribute('href') || '';
            if (href.startsWith('exuarch:')) {
                e.preventDefault();
                dotnet.invokeMethodAsync('Follow', href);
            } else if (/^(https?:|mailto:)/i.test(href)) {
                link.target = '_blank';
                link.rel = 'noopener noreferrer';
            } else if (href === '#') {
                e.preventDefault();
            }
        });
    },
};

window.exuarchWelcome = {
    key: 'exuarch.welcomed',
    seen: function () {
        try {
            if (localStorage.getItem(this.key)) return true;
            localStorage.setItem(this.key, '1');
        } catch { }
        return false;
    },
};

window.exuarchStore = {
    get: function (key) {
        try { return localStorage.getItem(key); } catch { return null; }
    },
    set: function (key, value) {
        try { localStorage.setItem(key, value); return true; } catch { return false; }
    },
    resetAll: function () {
        try {
            Object.keys(localStorage).filter(k => k.startsWith('exuarch.')).forEach(k => localStorage.removeItem(k));
        } catch { }
        location.reload();
    },
};

window.exuarchKeys = {
    isMac: function () {
        const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
        return /mac|iphone|ipad|ipod/i.test(platform);
    },
    runView: function (element) {
        if (!element || element.__exuarchKeys) return;
        element.__exuarchKeys = true;
        const shortcut = e => !e.ctrlKey && !e.metaKey && !e.altKey && [' ', 'ArrowRight', 'r', 'R', 'm', 'M'].includes(e.key);
        const control = t => t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
        const guard = e => {
            if (!shortcut(e)) return;
            if (control(e.target)) e.stopPropagation();
            else if (e.target !== element) e.preventDefault();
        };
        element.addEventListener('keydown', guard);
        element.addEventListener('keyup', guard);
        this.current = element;
        if (this.forwarding) return;
        this.forwarding = true;
        const keys = [' ', 'ArrowRight', 'ArrowLeft', 'ArrowUp', 'ArrowDown', 'Escape', 'r', 'R', 'm', 'M'];
        const forward = e => {
            const view = this.current;
            if (!e.isTrusted || !view || !view.isConnected || document.activeElement !== document.body) return;
            if (e.ctrlKey || e.metaKey || e.altKey || !keys.includes(e.key)) return;
            e.preventDefault();
            e.stopImmediatePropagation();
            view.focus({ preventScroll: true });
            view.dispatchEvent(new KeyboardEvent(e.type, e));
        };
        window.addEventListener('keydown', forward, true);
        window.addEventListener('keyup', forward, true);
    },
    editor: function (element) {
        if (!element || element.__exuarchKeys) return;
        element.__exuarchKeys = true;
        element.addEventListener('keydown', e => {
            if ((e.ctrlKey || e.metaKey) && ['d', 'z', 'y'].includes(e.key.toLowerCase())) e.preventDefault();
        });
    },
};

window.exuarchPan = {
    threshold: 4,
    attach: function (element, grabbable) {
        if (!element || element.__exuarchPan) return;
        element.__exuarchPan = true;
        const threshold = this.threshold;
        let drag = null;
        element.addEventListener('mousedown', e => {
            if (e.button === 1) e.preventDefault();
        });
        element.addEventListener('pointerdown', e => {
            if (e.pointerType === 'touch' || (e.button !== 0 && e.button !== 1)) return;
            if (e.button === 0 && e.target.closest(grabbable)) return;
            drag = { id: e.pointerId, x: e.clientX, y: e.clientY, left: element.scrollLeft, top: element.scrollTop, moved: false };
        });
        element.addEventListener('pointermove', e => {
            if (!drag || e.pointerId !== drag.id) return;
            const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
            if (!drag.moved && Math.hypot(dx, dy) < threshold) return;
            if (!drag.moved) {
                drag.moved = true;
                element.setPointerCapture(e.pointerId);
                element.classList.add('panning');
            }
            element.scrollLeft = drag.left - dx;
            element.scrollTop = drag.top - dy;
        });
        const release = e => {
            if (!drag || e.pointerId !== drag.id) return;
            element.classList.remove('panning');
            drag = null;
        };
        element.addEventListener('pointerup', release);
        element.addEventListener('pointercancel', release);
    },
};

window.exuarchTheme = {
    key: 'exuarch.theme',
    current: function () {
        return document.documentElement.dataset.theme === 'light' ? 'light' : 'dark';
    },
    monaco: function () {
        return this.current() === 'light' ? 'exuarch' : 'exuarch-dark';
    },
    set: function (theme) {
        theme = theme === 'light' ? 'light' : 'dark';
        document.documentElement.dataset.theme = theme;
        try { localStorage.setItem(this.key, theme); } catch { }
        if (window.monaco && window.exuarchAsm && window.exuarchAsm.registered) window.monaco.editor.setTheme(this.monaco());
        return theme;
    },
    toggle: function () {
        return this.set(this.current() === 'light' ? 'dark' : 'light');
    },
};
