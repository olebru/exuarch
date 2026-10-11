window.exuarchRunLayout = {
    observe: function (element, dotnet) {
        if (!element || element.__exuarchObserved || typeof ResizeObserver === 'undefined') return;
        element.__exuarchObserved = true;
        var pending = false;
        new ResizeObserver(function () {
            if (pending) return;
            pending = true;
            requestAnimationFrame(function () {
                pending = false;
                if (element.isConnected) dotnet.invokeMethodAsync('SchematicResized', element.clientWidth);
            });
        }).observe(element);
    },

    attach: function (run, dotnet) {
        if (!run || run.__exuarchLayout) return;
        run.__exuarchLayout = true;

        function limits(kind) {
            var main = run.querySelector('.main');
            if (kind === 'left' || kind === 'right') return { min: 220, max: Math.max(220, main.getBoundingClientRect().width - 360) };
            if (kind === 'split') return { min: 15, max: 85 };
            return { min: 120, max: Math.max(120, run.getBoundingClientRect().height - 260) };
        }

        function cssProperty(kind) {
            if (kind === 'split') return '--bottom-split';
            if (kind === 'bottom') return '--bottom-height';
            return '--' + kind + '-width';
        }

        function measure(kind, target, ev) {
            var main = run.querySelector('.main').getBoundingClientRect();
            if (kind === 'left') return ev.clientX - main.left;
            if (kind === 'right') return main.right - ev.clientX;
            if (kind === 'split') {
                var below = target.getBoundingClientRect();
                return (ev.clientX - below.left) / below.width * 100;
            }
            return run.getBoundingClientRect().bottom - ev.clientY;
        }

        run.addEventListener('pointerdown', function (e) {
            var handle = e.target.closest?.('[data-resize]');
            if (!handle || !run.contains(handle) || e.button !== 0) return;
            e.preventDefault();
            var kind = handle.dataset.resize;
            var property = cssProperty(kind);
            var target = kind === 'split' ? handle.parentElement : run;
            var unit = kind === 'split' ? '%' : 'px';
            var range = limits(kind);
            var size = null;
            try { handle.setPointerCapture(e.pointerId); } catch (err) { }
            handle.classList.add('dragging');
            run.classList.add('resizing');

            function move(ev) {
                var value = Math.min(range.max, Math.max(range.min, measure(kind, target, ev)));
                size = kind === 'split' ? Math.round(value * 10) / 10 : Math.round(value);
                target.style.setProperty(property, size + unit);
            }
            function up() {
                handle.removeEventListener('pointermove', move);
                handle.removeEventListener('pointerup', up);
                handle.removeEventListener('pointercancel', up);
                handle.classList.remove('dragging');
                run.classList.remove('resizing');
                if (size !== null) dotnet.invokeMethodAsync('LayoutResized', kind, size);
            }
            handle.addEventListener('pointermove', move);
            handle.addEventListener('pointerup', up);
            handle.addEventListener('pointercancel', up);
        });

        run.addEventListener('dblclick', function (e) {
            var handle = e.target.closest?.('[data-resize]');
            if (handle && run.contains(handle)) dotnet.invokeMethodAsync('LayoutResized', handle.dataset.resize, -1);
        });
    }
};

function exuarchPadding(element) {
    var style = getComputedStyle(element);
    return { left: Number.parseFloat(style.paddingLeft), right: Number.parseFloat(style.paddingRight) };
}

window.exuarchFit = {
    observe: function (wrapper) {
        if (!wrapper || wrapper.__exuarchFit || typeof ResizeObserver === 'undefined') return;
        wrapper.__exuarchFit = true;
        var content = wrapper.firstElementChild;
        var host = wrapper.parentElement;
        while (host?.parentElement) {
            var around = exuarchPadding(host);
            if (around.left + around.right > 0) break;
            host = host.parentElement;
        }
        if (!content || !host) return;

        function fit() {
            if (!wrapper.isConnected) return;
            content.style.zoom = '';
            wrapper.style.marginLeft = wrapper.style.marginRight = '';
            var natural = content.getBoundingClientRect().width;
            var room = wrapper.clientWidth;
            if (natural === 0 || natural <= room) return;
            var padding = exuarchPadding(host);
            wrapper.style.marginLeft = -padding.left + 'px';
            wrapper.style.marginRight = -padding.right + 'px';
            var zoomedBorderRoundingPixel = 1;
            var scale = (room + padding.left + padding.right - zoomedBorderRoundingPixel) / natural;
            if (scale < 1) content.style.zoom = Math.floor(scale * 1000) / 1000;
        }

        var pending = false;
        new ResizeObserver(function () {
            if (pending) return;
            pending = true;
            requestAnimationFrame(function () { pending = false; fit(); });
        }).observe(host);
        document.fonts?.ready.then(fit).catch(function keepFirstMeasureWithoutFonts() { });
        fit();
    }
};
