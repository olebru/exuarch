(function () {
    var summary = {};
    var activeMinutes = {};
    var sent = false;

    function track(name, data) {
        try {
            if (window.umami && typeof window.umami.track === 'function') window.umami.track(name, data || undefined);
        } catch (e) { }
    }

    var lastInput = Date.now();
    function active() { lastInput = Date.now(); activeMinutes[Math.floor(lastInput / 60000)] = true; }
    ['pointerdown', 'keydown', 'wheel', 'click', 'input'].forEach(function (type) { window.addEventListener(type, active, { passive: true, capture: true }); });

    function minutesBucket(n) {
        if (n <= 1) return '0 to 1';
        if (n <= 5) return '1 to 5';
        if (n <= 15) return '5 to 15';
        if (n <= 60) return '15 to 60';
        return '60 and more';
    }

    window.addEventListener('pagehide', function () {
        if (sent) return;
        sent = true;
        var data = { 'active minutes': minutesBucket(Object.keys(activeMinutes).length) };
        for (var key in summary) data[key] = summary[key];
        track('visit', data);
    });

    window.exuarchAnalytics = {
        track: track,
        summary: function (key, value) { summary[key] = value; },
        idleSeconds: function () { return (Date.now() - lastInput) / 1000; }
    };
})();
