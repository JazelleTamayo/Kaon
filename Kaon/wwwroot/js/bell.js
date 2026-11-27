// The owner's notification bell: shows what needs attention, on every page.
(function () {
    var root = document.querySelector('[data-bell]');
    if (!root) return;

    var btn = root.querySelector('[data-bell-btn]');
    var panel = root.querySelector('[data-bell-panel]');
    var badge = root.querySelector('[data-bell-badge]');
    var list = root.querySelector('[data-bell-list]');

    var url = root.getAttribute('data-bell-url') || '/Owner/Notifications';
    var isOwner = root.getAttribute('data-bell-role') !== 'customer';
    // The owner's Orders page and the customer's order pages already ring and pop up by themselves
    var onOrders = isOwner ? /^\/owner\/?$/i.test(location.pathname) : /^\/order(\/|$)/i.test(location.pathname);
    var KNOWN = 'kaon.bell.known.' + (isOwner ? 'owner' : 'customer');   // what we have already told the owner about
    var SEEN = 'kaon.bell.seen.' + (isOwner ? 'owner' : 'customer');     // what the owner has already looked at in the list

    function load(key) {
        try { var v = localStorage.getItem(key); return v === null ? null : JSON.parse(v); } catch (e) { return null; }
    }
    function save(key, value) {
        try { localStorage.setItem(key, JSON.stringify(value)); } catch (e) { }
    }

    function ago(mins) {
        if (mins < 1) return 'Just now';
        if (mins < 60) return mins + ' min ago';
        if (mins < 1440) return Math.floor(mins / 60) + ' h ago';
        return Math.floor(mins / 1440) + ' d ago';
    }

    var current = [];

    function render(d) {
        var items = d.items || [];
        current = items;
        var seen = load(SEEN) || [];
        var unseen = items.filter(function (i) { return seen.indexOf(i.key) === -1; }).length;

        // Badge: how many things are waiting for the owner
        badge.textContent = d.total > 99 ? '99+' : d.total;
        badge.hidden = !d.total;
        btn.classList.toggle('has-new', unseen > 0);
        btn.setAttribute('aria-label', d.total ? 'Notifications, ' + d.total + ' waiting' : 'Notifications');

        // The list
        list.innerHTML = '';
        if (!items.length) {
            var empty = document.createElement('li');
            empty.className = 'bell-empty';
            empty.textContent = "You're all caught up.";
            list.appendChild(empty);
        }
        items.forEach(function (i) {
            var li = document.createElement('li');
            var a = document.createElement('a');
            a.href = i.url;
            a.className = 'bell-item ' + i.kind + (seen.indexOf(i.key) === -1 ? ' unseen' : '');
            var dot = document.createElement('span'); dot.className = 'bell-dot'; dot.setAttribute('aria-hidden', 'true');
            var text = document.createElement('span'); text.className = 'bell-text';
            var t = document.createElement('strong'); t.textContent = i.title;
            var s = document.createElement('small'); s.textContent = i.detail;
            text.appendChild(t); text.appendChild(s);
            a.appendChild(dot); a.appendChild(text);
            if (i.mins >= 0) { var w = document.createElement('span'); w.className = 'bell-when'; w.textContent = ago(i.mins); a.appendChild(w); }
            li.appendChild(a);
            list.appendChild(li);
        });
        if (d.total > items.length) {
            var more = document.createElement('li');
            more.className = 'bell-more';
            more.textContent = 'and ' + (d.total - items.length) + ' more';
            list.appendChild(more);
        }

        // Tell the owner about anything new (not on the Orders page, which does this itself)
        var known = load(KNOWN);
        var keys = items.map(function (i) { return i.key; });
        if (known !== null && !onOrders && window.kaonAlerts) {
            items.forEach(function (i) {
                if (known.indexOf(i.key) === -1) kaonAlerts.notify(i.title, i.title + ' · ' + i.detail, { kind: i.kind === 'order' ? 'ok' : '', tag: i.key });
            });
        }
        save(KNOWN, keys);
    }

    function poll() {
        fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (!r.ok || (r.headers.get('content-type') || '').indexOf('json') === -1) throw new Error('not json');
                return r.json();
            })
            .then(render)
            .catch(function () { /* signed out or the plan ended: leave the bell as it is */ });
    }

    function close() {
        panel.hidden = true;
        btn.setAttribute('aria-expanded', 'false');
    }
    function open() {
        panel.hidden = false;
        btn.setAttribute('aria-expanded', 'true');
        // Opening the list counts as having seen everything in it
        save(SEEN, current.map(function (i) { return i.key; }));
        btn.classList.remove('has-new');
        [].forEach.call(list.querySelectorAll('.unseen'), function (e) { e.classList.remove('unseen'); });
    }

    btn.addEventListener('click', function (e) {
        e.stopPropagation();
        if (panel.hidden) open(); else close();
    });
    document.addEventListener('click', function (e) { if (!panel.hidden && !root.contains(e.target)) close(); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !panel.hidden) { close(); btn.focus(); } });
    panel.addEventListener('click', function (e) { if (e.target.closest('a')) close(); });

    poll();
    setInterval(poll, 20000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) poll(); });
})();
