// HoneyBee storefront — the only custom JavaScript on the site.
// Deliberately dependency-free and small; Bootstrap's bundle handles the rest.
(function () {
    'use strict';

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
