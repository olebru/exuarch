// Small helpers for the machine editor. Pointer coordinates are converted to canvas coordinates in C#.
// Firefox only starts an HTML5 drag when the drag carries data; Blazor handlers keep the real state.
document.addEventListener('dragstart', function (e) {
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
        // Scroll only the closest scrolling container, so outer panels and the page stay where they are.
        let box = element.parentElement;
        while (box && !(box.scrollHeight > box.clientHeight && /(auto|scroll)/.test(getComputedStyle(box).overflowY))) box = box.parentElement;
        if (!box) return;
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

// Package READMEs: links into the app (exuarch:...) go to .NET, web links open in a new tab.
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

// Whether this browser has been here before; the first call remembers that it has, so the guide opens only once.
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

// The workspace in the browser's storage. Every call is safe: private windows, blocked storage or a full quota
// just mean nothing is kept, which set reports by returning false.
window.exuarchStore = {
    get: function (key) {
        try { return localStorage.getItem(key); } catch { return null; }
    },
    set: function (key, value) {
        try { localStorage.setItem(key, value); return true; } catch { return false; }
    },
    // Start over: forget everything ExµArch keeps in the browser, then load the page afresh, so what is only in
    // memory, such as the speed setting, goes too. A phone's "open it here anyway" stays, or the reload would land
    // on the phone page.
    resetAll: function () {
        try {
            Object.keys(localStorage).filter(k => k.startsWith('exuarch.') && k !== 'exuarch.full').forEach(k => localStorage.removeItem(k));
        } catch { }
        location.reload();
    },
};

// Keyboard shortcuts that must not also do the browser's own thing. Attached once per element.
window.exuarchKeys = {
    // The Run view: Space, the right arrow and the letter shortcuts are the view's. On a focused button Space would
    // click it as well (so Run would pause and start again); on the speed slider or a checkbox they would also move
    // or toggle it. Buttons keep quiet, and controls keep the key to themselves.
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
        // A button that is disabled while it has the focus, as Tick is once a run starts and Run is when the machine
        // halts, drops the focus to the page, out of the view. Its keys are then the view's again: the focus goes
        // back to it and the key is passed on. This is decided before the key reaches anything, so a key that itself
        // disables the focused button is not passed on a second time.
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
    // The hardware design: Ctrl or Cmd with D, Z or Y are its duplicate, undo and redo, not the browser's bookmark
    // and history keys.
    editor: function (element) {
        if (!element || element.__exuarchKeys) return;
        element.__exuarchKeys = true;
        element.addEventListener('keydown', e => {
            if ((e.ctrlKey || e.metaKey) && ['d', 'z', 'y'].includes(e.key.toLowerCase())) e.preventDefault();
        });
    },
};

// Light or dark. The choice is kept in the browser; dark is the default (index.html applies it before the page
// paints). The code editors follow: Monaco's theme is global.
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
