// Kaon alerts: pop-up message, chime, phone vibration, browser notification.
// Shared by the customer order tracker and the owner order board.
(function () {
    var KEY = 'kaon.alerts';
    var ctx = null;
    var listeners = [];
    var flashTimer = null;
    var baseTitle = document.title;

    function audio() {
        try {
            if (!ctx) {
                var A = window.AudioContext || window.webkitAudioContext;
                if (A) ctx = new A();
            }
            if (ctx && ctx.state === 'suspended') ctx.resume();
        } catch (e) { }
        return ctx;
    }

    // One bell strike: the note plus two softer overtones, ringing out
    function bell(c, t, freq, vol) {
        [[1, 1], [2.01, 0.4], [3.02, 0.15]].forEach(function (p) {
            var o = c.createOscillator(), g = c.createGain();
            o.type = 'sine';
            o.frequency.value = freq * p[0];
            g.gain.setValueAtTime(0.0001, t);
            g.gain.exponentialRampToValueAtTime(vol * p[1], t + 0.01);
            g.gain.exponentialRampToValueAtTime(0.0001, t + 1.1);
            o.connect(g); g.connect(c.destination);
            o.start(t); o.stop(t + 1.2);
        });
    }

    // New-order sound: a clear "ding-dong" bell, played twice so it is heard over a busy kitchen
    function chime() {
        var c = audio();
        if (!c || c.state !== 'running') return;
        var now = c.currentTime;
        [0, 1.5].forEach(function (offset) {
            bell(c, now + offset, 1046.5, 0.5);        // ding
            bell(c, now + offset + 0.35, 783.99, 0.5); // dong
        });
    }

    function enabled() {
        try { return localStorage.getItem(KEY) === 'on'; } catch (e) { return false; }
    }

    function permission() {
        return ('Notification' in window) ? Notification.permission : 'unsupported';
    }

    function changed() { listeners.forEach(function (f) { f(); }); }

    function toast(message, kind) {
        var host = document.getElementById('toasts');
        if (!host) return;
        var t = document.createElement('div');
        t.className = 'toast ' + (kind || '');
        t.setAttribute('role', 'status');
        var dot = document.createElement('span'); dot.className = 'toast-dot';
        var msg = document.createElement('span'); msg.textContent = message;
        t.appendChild(dot); t.appendChild(msg);
        t.addEventListener('click', function () { t.remove(); });
        host.appendChild(t);
        setTimeout(function () { t.classList.add('out'); setTimeout(function () { t.remove(); }, 300); }, 9000);
    }

    // Blink the tab title while the page is in the background
    function flashTitle(text) {
        if (!document.hidden) return;
        var on = false;
        clearInterval(flashTimer);
        flashTimer = setInterval(function () { document.title = on ? baseTitle : text; on = !on; }, 1000);
    }
    function stopFlash() { clearInterval(flashTimer); document.title = baseTitle; }
    window.addEventListener('focus', stopFlash);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) stopFlash(); });

    function notify(title, body, opts) {
        opts = opts || {};
        toast(body || title, opts.kind);
        if (enabled()) chime();
        if (navigator.vibrate) { try { navigator.vibrate([200, 100, 200]); } catch (e) { } }
        if (enabled() && permission() === 'granted') {
            try { new Notification(title, { body: body, tag: opts.tag, icon: '/img/logo-icon.svg' }); } catch (e) { }
        }
        flashTitle(title);
    }

    // Must be called from a click, so the browser lets us play sound and ask for permission
    function enable() {
        try { localStorage.setItem(KEY, 'on'); } catch (e) { }
        audio(); chime();
        if (permission() === 'default') {
            Notification.requestPermission().then(changed);
        }
        changed();
    }

    function disable() {
        try { localStorage.setItem(KEY, 'off'); } catch (e) { }
        changed();
    }

    window.kaonAlerts = {
        enable: enable, disable: disable, enabled: enabled, notify: notify,
        toast: toast, chime: chime, permission: permission,
        onChange: function (f) { listeners.push(f); }
    };
})();

// Copy text. The modern clipboard only works on https pages, so fall back to the old way (http, older browsers).
window.kaonCopy = function (text) {
    function old() {
        var t = document.createElement('textarea');
        t.value = text;
        t.setAttribute('readonly', '');
        t.style.position = 'fixed';
        t.style.top = '0';
        t.style.opacity = '0';
        document.body.appendChild(t);
        t.focus();
        t.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        document.body.removeChild(t);
        return ok;
    }
    if (navigator.clipboard && window.isSecureContext) {
        return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return old(); });
    }
    return Promise.resolve(old());
};

// Copy buttons (account numbers, links)
document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-copy]');
    if (!b || b.dataset.copyHandled) return;
    var label = b.textContent;
    window.kaonCopy(b.getAttribute('data-copy')).then(function (ok) {
        b.textContent = ok ? 'Copied' : 'Press Ctrl+C';
        setTimeout(function () { b.textContent = label; }, 2000);
    });
});
