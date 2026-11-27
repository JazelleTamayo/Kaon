// Add / edit a dish or food package: the form behaviour, and the pop-up that shows it on the Menu page.
(function () {
    var PKG = 'Food package';

    // ---- the form itself (works on the full page and inside the pop-up) ----
    function init(root, pkgCategory) {
        PKG = pkgCategory || PKG;
        var form = root.querySelector('#menuForm');
        if (!form) return;
        var cat = form.querySelector('#Category');
        var meal = form.querySelector('#mealFields');
        var sizes = form.querySelector('#sizeFields');
        var pkgAvail = form.querySelector('#pkgAvailField');
        var rows = form.querySelector('#sizeRows');

        function addRow() {
            var d = document.createElement('div');
            d.className = 'size-row';
            d.innerHTML =
                '<input class="input" name="Options[0].Label" placeholder="Size or portion, for example Small tray (6-8 people)" aria-label="Size name" />' +
                '<input class="input price-in" name="Options[0].Price" type="number" step="0.01" min="0" placeholder="Price ₱" aria-label="Price" />' +
                '<button type="button" class="btn-ghost danger" data-remove aria-label="Remove size">Remove</button>';
            rows.appendChild(d);
        }

        function sync() {
            var pkg = cat.value === PKG;
            meal.style.display = pkg ? 'none' : '';
            sizes.style.display = pkg ? '' : 'none';
            pkgAvail.style.display = pkg ? '' : 'none';
            if (pkg && !rows.children.length) addRow();
        }

        form.querySelector('#addSize').addEventListener('click', addRow);
        rows.addEventListener('click', function (e) {
            if (e.target.matches('[data-remove]')) e.target.closest('.size-row').remove();
        });
        cat.addEventListener('change', sync);

        // Photo: show the chosen picture straight away
        var pick = form.querySelector('.photo-input');
        var preview = form.querySelector('#photoPreview');
        var empty = form.querySelector('.photo-empty');
        var change = form.querySelector('.photo-change');
        if (pick) pick.addEventListener('change', function () {
            var f = pick.files && pick.files[0];
            if (!f) return;
            preview.src = URL.createObjectURL(f);
            preview.hidden = false;
            if (empty) empty.hidden = true;
            if (change) change.textContent = 'Change photo';
        });

        // Number the size rows 0,1,2... so the server reads them all; a hidden section sends no stale price
        form.addEventListener('submit', function () {
            [].forEach.call(rows.querySelectorAll('.size-row'), function (r, i) {
                [].forEach.call(r.querySelectorAll('input'), function (inp) {
                    inp.name = inp.name.replace(/Options\[\d+\]/, 'Options[' + i + ']');
                });
            });
            if (cat.value === PKG) { var p = form.querySelector('#Price'); if (p) p.value = 0; }
        });

        sync();
    }

    // ---- the pop-up on the Menu page ----
    var open = null;

    function close() {
        if (!open) return;
        open.remove();
        open = null;
        document.body.classList.remove('dlg-open');
        document.removeEventListener('keydown', onKey);
    }
    function onKey(e) { if (e.key === 'Escape') close(); }

    function fill(box, html) {
        var holder = document.createElement('div');
        holder.innerHTML = html;
        var form = holder.querySelector('#menuForm');
        if (!form) { box.querySelector('.dlg-content').innerHTML = '<div class="df-body"><p class="muted">Could not open the form. Please try again.</p></div>'; return; }
        box.querySelector('.dlg-title').textContent = form.getAttribute('data-title') || 'Dish';
        var content = box.querySelector('.dlg-content');
        content.innerHTML = '';
        content.appendChild(form);
        init(content, PKG);
        form.addEventListener('submit', function (e) { e.preventDefault(); submit(box, form); });
        var first = form.querySelector('#Name');
        if (first) first.focus();
    }

    async function submit(box, form) {
        var btn = form.querySelector('button[type=submit]');
        if (btn) { btn.disabled = true; btn.textContent = 'Saving...'; }
        try {
            var res = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { 'X-Modal': '1' } });
            if (res.redirected) {
                // Saved: show the list again, on the tab the dish belongs to
                try { sessionStorage.setItem('menuTab', form.querySelector('#Category').value === PKG ? 'packages' : 'dishes'); } catch (e) { }
                location.reload();
                return;
            }
            fill(box, await res.text());   // something to fix: show the form again with its messages
        } catch (err) {
            if (btn) { btn.disabled = false; btn.textContent = 'Try again'; }
        }
    }

    async function show(url) {
        close();
        var backdrop = document.createElement('div');
        backdrop.className = 'dlg-backdrop';
        backdrop.innerHTML =
            '<div class="dlg" role="dialog" aria-modal="true" aria-labelledby="dlgTitle">' +
            '<div class="dlg-head"><h2 id="dlgTitle" class="dlg-title">Loading...</h2>' +
            '<button type="button" class="dlg-x" data-dlg-close aria-label="Close">&times;</button></div>' +
            '<div class="dlg-content"><div class="df-body"><p class="muted">Loading...</p></div></div></div>';
        document.body.appendChild(backdrop);
        document.body.classList.add('dlg-open');
        open = backdrop;
        document.addEventListener('keydown', onKey);
        backdrop.addEventListener('mousedown', function (e) { if (e.target === backdrop) close(); });
        backdrop.addEventListener('click', function (e) { if (e.target.closest('[data-dlg-close]')) close(); });
        try {
            var res = await fetch(url, { credentials: 'same-origin', headers: { 'X-Modal': '1' } });
            if (!res.ok) throw new Error(res.status);
            fill(backdrop, await res.text());
        } catch (err) {
            // Fall back to the normal page
            location.href = url;
        }
    }

    // Open the pop-up from the Edit / Add links on the Menu page
    function attach(pkgCategory) {
        PKG = pkgCategory || PKG;
        document.addEventListener('click', function (e) {
            var a = e.target.closest('a[href*="/OwnerMenu/Edit"], a[href*="/OwnerMenu/Create"]');
            if (!a || e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1) return;
            e.preventDefault();
            show(a.getAttribute('href'));
        });
    }

    window.kaonMenuForm = { init: init, attach: attach, close: close };
})();
