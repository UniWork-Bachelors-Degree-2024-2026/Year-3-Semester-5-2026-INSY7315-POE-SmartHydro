(function () {
    var BUSY_RESET_MS = 15000;

    // Says a message through the page's hidden live region, creating one if the page has none
    function announce(message) {
        // The shared announcer also lets the read aloud reader speak the message
        if (typeof window.shAnnounce === 'function') {
            window.shAnnounce(message);
            return;
        }

        var region = document.getElementById('sh-announcer');

        if (!region) {
            region = document.createElement('div');
            region.id = 'sh-announcer';
            region.className = 'visually-hidden';
            region.setAttribute('role', 'status');
            region.setAttribute('aria-live', 'polite');
            region.setAttribute('aria-atomic', 'true');
            document.body.appendChild(region);
        }

        region.textContent = '';
        window.setTimeout(function () {
            region.textContent = message;
        }, 50);
    }

    // Marks a form busy and locks its submit buttons, or frees it again
    function setBusy(form, isBusy) {
        var buttons = form.querySelectorAll('button[type="submit"], input[type="submit"]');

        if (isBusy) {
            form.setAttribute('aria-busy', 'true');
        } else {
            form.removeAttribute('aria-busy');
        }

        buttons.forEach(function (button) {
            button.disabled = isBusy;
        });
    }

    document.addEventListener('submit', function (event) {
        var form = event.target;

        // Forms a script already handled, or that open in another tab, are left alone
        if (event.defaultPrevented || form.target || form.hasAttribute('data-no-busy')) {
            return;
        }

        // The buttons are locked on the next tick so the clicked button's value still goes with the post
        window.setTimeout(function () {
            setBusy(form, true);
            announce('Working, please wait');
        }, 0);

        // Frees the form again if the response never replaces the page
        window.setTimeout(function () {
            setBusy(form, false);
        }, BUSY_RESET_MS);
    });

    // Coming back with the browser's back button must not show a stuck form
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) {
            return;
        }

        document.querySelectorAll('form[aria-busy="true"]').forEach(function (form) {
            setBusy(form, false);
        });
    });
})();
