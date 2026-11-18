// Login and sign-up helpers: show/hide password and a password strength bar.
(function () {
    document.querySelectorAll('[data-eye]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var input = btn.parentElement.querySelector('input');
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.classList.toggle('on', show);
            btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
        });
    });

    var pw = document.querySelector('[data-strength]');
    if (!pw) return;
    var bars = pw.closest('.fld').querySelectorAll('.meter i');
    var label = pw.closest('.fld').querySelector('[data-meter-text]');
    var words = ['', 'Too weak', 'Okay', 'Good', 'Strong'];

    pw.addEventListener('input', function () {
        var v = pw.value, s = 0;
        if (v.length >= 6) s++;
        if (v.length >= 10) s++;
        if (/[A-Z]/.test(v) && /[a-z]/.test(v)) s++;
        if (/\d/.test(v) && /[^A-Za-z0-9]/.test(v)) s++;
        if (!v.length) s = 0;
        bars.forEach(function (b, i) { b.className = i < s ? 's' + s : ''; });
        label.textContent = words[s];
        label.className = 'meter-text' + (s ? ' s' + s : '');
    });
})();
