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


    // ----- Styled dropdown for sizes (replaces the browser's plain grey list) -----
    function enhanceSelect(sel) {
        if (sel.dataset.enhanced) return;
        sel.dataset.enhanced = '1';

        var wrap = document.createElement('div');
        wrap.className = 'dd';
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'dd-btn';
        btn.setAttribute('aria-haspopup', 'listbox');
        btn.setAttribute('aria-expanded', 'false');
        var list = document.createElement('ul');
        list.className = 'dd-list';
        list.setAttribute('role', 'listbox');
        list.tabIndex = -1;
        list.hidden = true;

        var uid = 'dd' + Math.random().toString(36).slice(2, 8);
        var items = [];
        Array.prototype.forEach.call(sel.options, function (o, i) {
            var li = document.createElement('li');
            li.setAttribute('role', 'option');
            li.id = uid + '-' + i;
            li.innerHTML = '<span class="dd-check"></span><span class="dd-label"></span><span class="dd-price"></span>';
            li.children[1].textContent = o.dataset.label || o.textContent;
            li.children[2].textContent = o.dataset.money || '';
            li.addEventListener('click', function () { choose(i); btn.focus(); });
            li.addEventListener('mousemove', function () { setActive(i, false); });
            items.push(li);
            list.appendChild(li);
        });

        var active = sel.selectedIndex;

        function paint() {
            var o = sel.options[sel.selectedIndex];
            btn.innerHTML = '<span class="dd-text"><span class="dd-label"></span><span class="dd-price"></span></span>' +
                '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 9l6 6 6-6"/></svg>';
            btn.querySelector('.dd-label').textContent = o.dataset.label || o.textContent;
            btn.querySelector('.dd-price').textContent = o.dataset.money || '';
            items.forEach(function (li, i) { li.setAttribute('aria-selected', i === sel.selectedIndex ? 'true' : 'false'); });
        }

        function setActive(i, scroll) {
            active = Math.max(0, Math.min(items.length - 1, i));
            items.forEach(function (li, k) { li.classList.toggle('active', k === active); });
            list.setAttribute('aria-activedescendant', items[active].id);
            if (scroll !== false) items[active].scrollIntoView({ block: 'nearest' });
        }

        function place() {
            var r = btn.getBoundingClientRect();
            var h = list.offsetHeight;
            var below = window.innerHeight - r.bottom;
            list.style.left = r.left + 'px';
            list.style.width = r.width + 'px';
            list.style.top = (below >= h + 12 || below >= r.top ? r.bottom + 6 : r.top - h - 6) + 'px';
        }

        function open() {
            if (!list.hidden) return;
            document.body.appendChild(list);
            list.hidden = false;
            btn.setAttribute('aria-expanded', 'true');
            setActive(sel.selectedIndex);
            place();
            list.focus({ preventScroll: true });
            window.addEventListener('resize', place);
            window.addEventListener('scroll', place, true);
        }

        function close() {
            if (list.hidden) return;
            list.hidden = true;
            btn.setAttribute('aria-expanded', 'false');
            wrap.appendChild(list);
            window.removeEventListener('resize', place);
            window.removeEventListener('scroll', place, true);
        }

        function choose(i) {
            sel.selectedIndex = i;
            sel.dispatchEvent(new Event('change', { bubbles: true }));
            paint();
            close();
        }

        btn.addEventListener('click', function () { list.hidden ? open() : close(); });
        btn.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); open(); }
        });
        list.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive(active + 1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(active - 1); }
            else if (e.key === 'Home') { e.preventDefault(); setActive(0); }
            else if (e.key === 'End') { e.preventDefault(); setActive(items.length - 1); }
            else if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); choose(active); btn.focus(); }
            else if (e.key === 'Escape' || e.key === 'Tab') { close(); btn.focus(); }
        });
        document.addEventListener('mousedown', function (e) {
            if (!wrap.contains(e.target) && !list.contains(e.target)) close();
        });

        sel.parentNode.insertBefore(wrap, sel);
        wrap.appendChild(btn);
        wrap.appendChild(list);
        wrap.appendChild(sel);
        sel.classList.add('dd-native');
        sel.tabIndex = -1;
        sel.setAttribute('aria-hidden', 'true');
        paint();
    }

    document.querySelectorAll('select.size-select').forEach(enhanceSelect);

    // ----- Food package cards -----
    function peso(n) {
        return '₱' + n.toLocaleString('en-PH', { maximumFractionDigits: 0 });
    }

    document.querySelectorAll('[data-pkg]').forEach(function (form) {
        var qty = form.querySelector('input[name="qty"]');
        var total = form.querySelector('[data-total]');

        function update() {
            var sel = form.querySelector('select[name="optionId"]');
            var opt = sel ? sel.options[sel.selectedIndex] : null;
            var price = opt ? parseFloat(opt.getAttribute('data-price')) : 0;
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

    // ----- Confirm before adding a dish that's still being prepared -----
    var modal = document.getElementById('confirmModal');
    if (modal) {
        var text = modal.querySelector('#confirmText');
        var pendingForm = null;

        function closeModal() { modal.hidden = true; pendingForm = null; }

        document.querySelectorAll('form[data-ready="preparing"]').forEach(function (form) {
            form.addEventListener('submit', function (e) {
                if (form.dataset.confirmed) return;
                e.preventDefault();
                pendingForm = form;
                text.textContent = (form.dataset.name || 'This dish') +
                    ' is still being prepared. Add it anyway and wait until it’s ready?';
                modal.hidden = false;
            });
        });

        modal.querySelector('[data-confirm-yes]').addEventListener('click', function () {
            if (pendingForm) { pendingForm.dataset.confirmed = '1'; pendingForm.submit(); }
            closeModal();
        });
        modal.querySelector('[data-confirm-no]').addEventListener('click', closeModal);
        modal.addEventListener('click', function (e) { if (e.target === modal) closeModal(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !modal.hidden) closeModal(); });
    }
})();
