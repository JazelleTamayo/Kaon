// Customer order tracker: a bar at the bottom of every page, like a delivery app.
// It follows the customer around, updates live, and alerts them when the food is Ready.
(function () {
    var el = document.getElementById('tracker');
    var A = window.kaonAlerts;
    if (!el || !A) return;

    var activeUrl = el.dataset.activeUrl;
    var statusUrl = el.dataset.statusUrl;
    var jsonUrl = el.dataset.jsonUrl;
    var ordersUrl = el.dataset.ordersUrl;
    var hideBar = el.dataset.hide === '1';   // already on the order's own page
    var STORE = 'kaon.orders';

    var orders = [];
    try { orders = JSON.parse(el.dataset.initial || '[]'); } catch (e) { }

    var CLASS = { Pending: 'pending', Preparing: 'preparing', Ready: 'ready', OutForDelivery: 'delivery' };

    function esc(s) { var d = document.createElement('div'); d.textContent = s == null ? '' : s; return d.innerHTML; }

    function icon(o) {
        var p = {
            Pending: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
            Preparing: '<path d="M4 12h16a8 8 0 0 1-16 0z"/><path d="M9 7c-1-1 1-2 0-3M14 7c-1-1 1-2 0-3"/>',
            Ready: '<circle cx="12" cy="12" r="9"/><path d="M8 12.5l3 3 5-6"/>',
            OutForDelivery: '<circle cx="6" cy="16" r="3.5"/><circle cx="18" cy="16" r="3.5"/><path d="M6 16l4-7h5l3 7M10 9H8"/>'
        }[o.status] || '<circle cx="12" cy="12" r="9"/>';
        return '<svg viewBox="0 0 24 24" aria-hidden="true">' + p + '</svg>';
    }

    function alertButton() {
        if (A.enabled() && A.permission() !== 'default') return '';
        return '<button type="button" class="trk-alert" data-alerts>Turn on alerts</button>';
    }

    function render() {
        if (!orders.length || hideBar) {
            el.hidden = true;
            document.body.classList.remove('has-track');
            return;
        }
        var o = orders[0], more = orders.length - 1;
        var dots = o.steps.map(function (l, i) {
            return '<i class="' + (i < o.step ? 'done' : i === o.step ? 'now' : '') + '" title="' + esc(l) + '"></i>';
        }).join('');

        el.className = 'tracker t-' + (CLASS[o.status] || 'pending');
        el.innerHTML =
            '<a class="trk-main" href="' + statusUrl + '/' + o.id + '">' +
                '<span class="trk-ico">' + icon(o) + '</span>' +
                '<span class="trk-text">' +
                    '<strong>' + esc(o.label) + ' · Order #' + o.id + '</strong>' +
                    '<span class="trk-msg">' + esc(o.message) + '</span>' +
                    '<span class="trk-dots" aria-hidden="true">' + dots + '</span>' +
                '</span>' +
                '<span class="trk-go">Track</span>' +
            '</a>' +
            (more > 0 ? '<a class="trk-more" href="' + ordersUrl + '">+' + more + ' more</a>' : '') +
            alertButton();
        el.hidden = false;
        document.body.classList.add('has-track');
    }

    function load() { try { return JSON.parse(localStorage.getItem(STORE) || 'null'); } catch (e) { return null; } }
    function save(map) { try { localStorage.setItem(STORE, JSON.stringify(map)); } catch (e) { } }

    function note(o) {
        switch (o.status) {
            case 'Preparing': return ['Preparing your order', 'Order #' + o.id + ' is being prepared.', ''];
            case 'Ready': return ['Your order is Ready!',
                o.type === 'Pickup' ? 'Order #' + o.id + ' is Ready. Pick it up.' : 'Order #' + o.id + ' is Ready. A rider will pick it up soon.', 'ready'];
            case 'OutForDelivery': return ['On its way!', 'Order #' + o.id + ' is out for delivery.', 'ready'];
            default: return null;
        }
    }

    // Compare with what we saw last time (this page or any other page) and alert on changes
    function process(list) {
        var prev = load(), now = {};
        list.forEach(function (o) { now[o.id] = o.status; });

        if (prev) {
            list.forEach(function (o) {
                if (prev[o.id] && prev[o.id] !== o.status) {
                    var n = note(o);
                    if (n) A.notify(n[0], n[1], { kind: n[2], tag: 'order-' + o.id });
                }
            });
            Object.keys(prev).forEach(function (id) {
                if (id in now) return;
                // It left the active list: finished or cancelled
                fetch(jsonUrl + '/' + id, { credentials: 'same-origin', cache: 'no-store' })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (d) {
                        if (!d) return;
                        if (d.status === 'Completed') A.notify('Order complete', 'Order #' + id + ' is complete. Thank you for ordering with Kaon!', { tag: 'order-' + id });
                        else if (d.status === 'Cancelled') A.notify('Order cancelled', 'Order #' + id + ' was cancelled by the carinderia.', { kind: 'warn', tag: 'order-' + id });
                    }).catch(function () { });
            });
        }

        save(now);
        orders = list;
        render();
    }

    function poll() {
        fetch(activeUrl, { credentials: 'same-origin', cache: 'no-store' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) { if (d && d.orders) process(d.orders); })
            .catch(function () { });
    }

    el.addEventListener('click', function (e) {
        if (e.target.closest('[data-alerts]')) { e.preventDefault(); A.enable(); }
    });
    A.onChange(render);

    render();
    process(orders);              // alert about changes since the last page
    setInterval(poll, 6000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) poll(); });
})();
