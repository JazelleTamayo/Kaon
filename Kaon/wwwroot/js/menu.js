// Customer menu helpers: series filter, and size / quantity / total on food package cards.
(function () {
    // ----- Series filter -----
    var filters = document.querySelector('[data-filters]');
    var grid = document.querySelector('[data-grid]');
    var empty = document.querySelector('[data-empty]');
    if (filters && grid) {
        filters.addEventListener('click', function (e) {
            var btn = e.target.closest('.fchip');
            if (!btn) return;
            filters.querySelectorAll('.fchip').forEach(function (b) { b.classList.toggle('on', b === btn); });
            var series = btn.getAttribute('data-series');
            var shown = 0;
            grid.querySelectorAll('.dish-card').forEach(function (card) {
                var match = series === 'all' || card.getAttribute('data-series') === series;
                card.hidden = !match;
                if (match) shown++;
            });
            if (empty) empty.hidden = shown !== 0;
        });
    }

    // ----- Food package cards -----
    function peso(n) {
        return '₱' + n.toLocaleString('en-PH', { maximumFractionDigits: 0 });
    }

    document.querySelectorAll('[data-pkg]').forEach(function (form) {
        var qty = form.querySelector('input[name="qty"]');
        var total = form.querySelector('[data-total]');

        function update() {
            var checked = form.querySelector('input[name="optionId"]:checked');
            var price = checked ? parseFloat(checked.getAttribute('data-price')) : 0;
            var q = Math.max(1, Math.min(100, parseInt(qty.value, 10) || 1));
            total.textContent = peso(price * q);
        }

        form.addEventListener('change', update);
        qty.addEventListener('input', update);
        form.querySelector('[data-dec]').addEventListener('click', function () {
            qty.value = Math.max(1, (parseInt(qty.value, 10) || 1) - 1); update();
        });
        form.querySelector('[data-inc]').addEventListener('click', function () {
            qty.value = Math.min(100, (parseInt(qty.value, 10) || 1) + 1); update();
        });
        update();
    });
})();
