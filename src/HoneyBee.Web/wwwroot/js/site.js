// HoneyBee storefront — the only custom JavaScript on the site.
// Deliberately dependency-free and small; Bootstrap's bundle handles the rest.
(function () {
    'use strict';

    /* -- dialogs: closing ---------------------------------------------- */
    // Every <dialog> on the page closes the same two ways, so this is written
    // once rather than per dialog. Escape is the browser's own doing.
    document.querySelectorAll('dialog').forEach(function (dialog) {
        dialog.addEventListener('click', function (event) {
            // The backdrop belongs to the dialog element, so a click landing on
            // the element itself rather than its panel means "outside".
            if (event.target === dialog) dialog.close();
        });

        dialog.querySelectorAll('[data-close-dialog]').forEach(function (button) {
            button.addEventListener('click', function () { dialog.close(); });
        });
    });

    /* -- add to basket, without losing the page ------------------------ */
    // Adding used to post the form and reload, which on the product list threw
    // the customer back to the top of the page they were browsing. The add now
    // happens in place and says so.
    var addedDialog = document.getElementById('addedDialog');
    var addForms = document.querySelectorAll('form[data-add-to-cart]');

    if (addedDialog && addForms.length && window.fetch) {
        var addedProduct = document.getElementById('addedDialogProduct');
        var cartCount = document.getElementById('cartCount');

        addForms.forEach(function (form) {
            var button = form.querySelector('button[type="submit"]');

            form.addEventListener('submit', function (event) {
                event.preventDefault();
                if (button) button.disabled = true;

                fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'fetch' },
                    credentials: 'same-origin'
                }).then(function (response) {
                    return response.json();
                }).then(function (result) {
                    if (button) button.disabled = false;

                    if (!result.ok) {
                        // Sold out between the page loading and this click. Let
                        // the ordinary post through so the page re-renders with
                        // the item correctly marked unavailable.
                        form.submit();
                        return;
                    }

                    if (addedProduct) addedProduct.textContent = result.product || '';

                    if (cartCount && typeof result.count === 'number') {
                        cartCount.textContent = result.count;
                        cartCount.hidden = result.count === 0;
                    }

                    if (typeof addedDialog.showModal === 'function') {
                        if (!addedDialog.open) addedDialog.showModal();
                    } else {
                        window.alert(result.message);
                    }
                }).catch(function () {
                    // Never leave the button dead: fall back to the plain post.
                    if (button) button.disabled = false;
                    form.submit();
                });
            });
        });
    }

    /* -- checkout, held until the transfer is approved ----------------- */
    // The button posts in place instead of navigating, so a customer who is
    // waiting can press it again and again without losing the page they are on.
    // The server answers with one of four states; the moment the owner approves,
    // the next press comes back "approved" and this hands over to WhatsApp.
    var checkoutForm = document.getElementById('checkoutForm');
    var waitDialog = document.getElementById('waitDialog');

    if (checkoutForm && waitDialog && window.fetch) {
        var submitButton = document.getElementById('checkoutSubmit');
        var dialogText = document.getElementById('waitDialogText');
        var dialogRef = document.getElementById('waitDialogRef');
        var waitingMessage = dialogText ? dialogText.textContent : '';
        var busy = false;

        function showDialog() {
            // <dialog> without showModal() is inert, and older browsers have
            // neither — falling back to alert() beats a button that does nothing.
            if (typeof waitDialog.showModal === 'function') {
                if (!waitDialog.open) waitDialog.showModal();
            } else if (dialogText) {
                window.alert(dialogText.textContent);
            }
        }

        checkoutForm.addEventListener('submit', function (event) {
            // Let the browser do its own required-field checking first.
            if (checkoutForm.checkValidity && !checkoutForm.checkValidity()) return;

            event.preventDefault();
            if (busy) return;
            busy = true;
            if (submitButton) submitButton.disabled = true;

            fetch(checkoutForm.action, {
                method: 'POST',
                body: new FormData(checkoutForm),
                headers: { 'X-Requested-With': 'fetch' },
                credentials: 'same-origin'
            }).then(function (response) {
                return response.json();
            }).then(function (result) {
                if (result.state === 'approved') {
                    // Kept disabled: the page is on its way out, and a second
                    // press here would open WhatsApp twice.
                    window.location.href = result.url;
                    return;
                }

                if (result.state === 'invalid') {
                    // Nothing was saved, so let the ordinary post render the
                    // field errors rather than reinventing them here.
                    checkoutForm.submit();
                    return;
                }

                if (result.message && dialogText) dialogText.textContent = result.message;

                if (dialogRef) {
                    dialogRef.textContent = result.orderNumber || '';
                    dialogRef.hidden = !result.orderNumber;
                }

                showDialog();
                busy = false;
                if (submitButton) submitButton.disabled = false;
            }).catch(function () {
                // A dropped connection must not strand the customer on a dead
                // button; fall back to an ordinary post, which always works.
                busy = false;
                if (submitButton) submitButton.disabled = false;
                if (dialogText) dialogText.textContent = waitingMessage;
                checkoutForm.submit();
            });
        });
    }

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
