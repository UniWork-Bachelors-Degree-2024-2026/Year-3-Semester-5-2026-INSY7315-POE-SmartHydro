(function () {
    var storageKey = 'sh-theme';
    var contrastStorageKey = 'sh-contrast';
    var root = document.documentElement;

    //w3schools (2026) demonstrates the set and retrieve localStorage name/value pair.
    function readStoredTheme() {
        try {
            return localStorage.getItem(storageKey);
        } catch (error) {
            return null;
        }
    }

    function storeTheme(theme) {
        try {
            localStorage.setItem(storageKey, theme);
        } catch (error) {
            return;
        }
    }

    function getInitialTheme() {
        var storedTheme = readStoredTheme();

        if (storedTheme === 'dark' || storedTheme === 'light') {
            return storedTheme;
        }

        // Nothing saved yet, so follow the device's dark mode setting
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            return 'dark';
        }

        return 'light';
    }

    function updateToggle(theme) {
        var toggle = document.getElementById('sh-theme-toggle');
        if (!toggle) {
            return;
        }

        var label = 'Switch to dark mode';
        if (theme === 'dark') {
            label = 'Switch to light mode';
        }

        toggle.setAttribute('aria-label', label);
        toggle.setAttribute('title', label);
    }

    function applyTheme(theme) {
        root.setAttribute('data-theme', theme);
        root.setAttribute('data-bs-theme', theme);
        updateToggle(theme);
    }

    applyTheme(getInitialTheme());

    // Reads the saved high contrast choice, falling back to the device's more contrast setting when nothing is saved
    function readStoredContrast() {
        var deviceWantsMore = window.matchMedia && window.matchMedia('(prefers-contrast: more)').matches;

        try {
            var saved = localStorage.getItem(contrastStorageKey);
            if (saved === null) {
                return deviceWantsMore;
            }

            return saved === 'high';
        } catch (error) {
            return deviceWantsMore;
        }
    }

    // Saves the high contrast choice so it stays on across pages and visits
    function storeContrast(isHigh) {
        var value = 'normal';
        if (isHigh) {
            value = 'high';
        }

        try {
            localStorage.setItem(contrastStorageKey, value);
        } catch (error) {
            return;
        }
    }

    // Updates the contrast button's pressed state and wording to match the current mode
    function updateContrastToggle(isHigh) {
        var toggle = document.getElementById('sh-contrast-toggle');
        if (!toggle) {
            return;
        }

        var label = 'Turn on high contrast';
        if (isHigh) {
            label = 'Turn off high contrast';
        }

        toggle.setAttribute('aria-pressed', String(isHigh));
        toggle.setAttribute('aria-label', label);
        toggle.setAttribute('title', label);
    }

    // Switches high contrast on or off by setting data-contrast on the page root
    function applyContrast(isHigh) {
        if (isHigh) {
            root.setAttribute('data-contrast', 'high');
        } else {
            root.removeAttribute('data-contrast');
        }

        updateContrastToggle(isHigh);
    }

    applyContrast(readStoredContrast());

    document.addEventListener('DOMContentLoaded', function () {
        var contrastToggle = document.getElementById('sh-contrast-toggle');
        if (!contrastToggle) {
            return;
        }

        updateContrastToggle(root.getAttribute('data-contrast') === 'high');

        contrastToggle.addEventListener('click', function () {
            var isHigh = root.getAttribute('data-contrast') !== 'high';
            applyContrast(isHigh);
            storeContrast(isHigh);

            if (window.shAnnounce) {
                window.shAnnounce(isHigh ? 'High contrast on' : 'High contrast off');
            }
        });
    });

    document.addEventListener('DOMContentLoaded', function () {
        var toggle = document.getElementById('sh-theme-toggle');
        if (!toggle) {
            return;
        }

        updateToggle(root.getAttribute('data-theme'));

        toggle.addEventListener('click', function () {
            var nextTheme = 'dark';
            if (root.getAttribute('data-theme') === 'dark') {
                nextTheme = 'light';
            }

            applyTheme(nextTheme);
            storeTheme(nextTheme);

            if (window.shAnnounce) {
                window.shAnnounce(nextTheme === 'dark' ? 'Dark mode on' : 'Light mode on');
            }
        });
    });
})();

/*
Reference List: 
w3schools, 2026. Window localStorage. [online] Available at: <https://www.w3schools.com/jsref/prop_win_localstorage.asp> [Accessed 23 September 2026]. 

*/