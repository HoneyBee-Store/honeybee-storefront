// HoneyBee storefront — the only custom JavaScript on the site.
// Deliberately dependency-free and small; Bootstrap's bundle handles the rest.
(function () {
    'use strict';

    /* -- copy-to-clipboard (the CliQ alias) ---------------------------- */
    // Clipboard access is refused more often than it looks: Safari is strict,
    // several in-app browsers deny it outright, and it needs a secure context.
    // So there are two fallbacks below the API, and the button is hidden
    // entirely if neither can work — better no button than a dead one.
    document.querySelectorAll('[data-copy-target]').forEach(function (button) {
        var source = document.getElementById(button.getAttribute('data-copy-target'));
        if (!source) return;

        if (!navigator.clipboard && !document.queryCommandSupported) {
            button.hidden = true;
            return;
        }

        function selectSource() {
            // Last resort: put the alias on screen as a selection so the phone's
            // own copy menu is one tap away. The customer still gets the value.
            var range = document.createRange();
            range.selectNodeContents(source);
            var selection = window.getSelection();
            selection.removeAllRanges();
            selection.addRange(range);
            return range;
        }

        function confirmCopied() {
            var original = button.innerHTML;
            button.classList.add('is-done');
            button.innerHTML = '<i class="bi bi-check-lg" aria-hidden="true"></i>'
                + (button.getAttribute('data-copied') || '');
            setTimeout(function () {
                button.classList.remove('is-done');
                button.innerHTML = original;
            }, 2000);
        }

        function legacyCopy() {
            selectSource();
            var copied = false;
            try { copied = document.execCommand('copy'); } catch (e) { copied = false; }
            // Leave the text selected when even that fails, so the customer can
            // copy it by hand rather than retype eight letters that must be exact.
            if (copied) {
                window.getSelection().removeAllRanges();
                confirmCopied();
            }
        }

        button.addEventListener('click', function () {
            var text = source.textContent.trim();

            if (!navigator.clipboard) {
                legacyCopy();
                return;
            }

            navigator.clipboard.writeText(text).then(confirmCopied, legacyCopy);
        });
    });

    /* -- mobile menu -------------------------------------------------- */
    var toggle = document.getElementById('navToggle');
    var collapse = document.getElementById('navCollapse');

    if (toggle && collapse) {
        toggle.addEventListener('click', function () {
            var open = collapse.classList.toggle('is-open');
            toggle.setAttribute('aria-expanded', String(open));
            toggle.innerHTML = open
                ? '<i class="bi bi-x-lg" aria-hidden="true"></i>'
                : '<i class="bi bi-list" aria-hidden="true"></i>';
        });

        // Close after choosing a destination, so the menu doesn't cover the
        // section it just scrolled to.
        collapse.addEventListener('click', function (e) {
            if (e.target.closest('a') && collapse.classList.contains('is-open')) {
                collapse.classList.remove('is-open');
                toggle.setAttribute('aria-expanded', 'false');
                toggle.innerHTML = '<i class="bi bi-list" aria-hidden="true"></i>';
            }
        });
    }

    /* -- scroll reveal ------------------------------------------------ */
    var revealables = document.querySelectorAll('.reveal');
    var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (reduced || !('IntersectionObserver' in window) || !revealables.length) {
        return;   // leave the content visible; .js-reveal is never applied
    }

    // Only now does the CSS hide anything. Setting this any earlier would risk
    // a blank page if the code below failed.
    document.documentElement.classList.add('js-reveal');

    // Belt and braces: if the observer somehow never fires, reveal everything
    // after three seconds rather than leaving the page empty.
    var safety = setTimeout(function () {
        revealables.forEach(function (el) { el.classList.add('is-in'); });
    }, 3000);

    {
        var seen = 0;
        var observer = new IntersectionObserver(function (entries, obs) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) return;
                entry.target.classList.add('is-in');
                obs.unobserve(entry.target);
                if (++seen === 1) clearTimeout(safety);
            });
        }, { rootMargin: '0px 0px -40px 0px', threshold: 0.01 });

        revealables.forEach(function (el, i) {
            // A short stagger down each grid reads as one motion rather than
            // a dozen unrelated ones.
            el.style.transitionDelay = Math.min(i % 6, 5) * 60 + 'ms';
            observer.observe(el);
        });
    }
})();
