(function () {
    var themeStorageKey = 'sh-theme';
    var contrastStorageKey = 'sh-contrast';
    var root = document.documentElement;

    // Reads the saved theme from localStorage (w3schools, 2026), returning null when storage is blocked.
    function readStoredTheme() {
        try {
            return localStorage.getItem(themeStorageKey);
        } catch (error) {
            return null;
        }
    }

    // Saves the theme choice so it stays the same across pages and visits.
    function storeTheme(theme) {
        try {
            localStorage.setItem(themeStorageKey, theme);
        } catch (error) {
            return;
        }
    }

    // Picks the starting theme: the saved choice first, then the device's dark mode setting, then light.
    function getInitialTheme() {
        var storedTheme = readStoredTheme();

        if (storedTheme === 'dark' || storedTheme === 'light') {
            return storedTheme;
        }

        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            return 'dark';
        }

        return 'light';
    }

    // Updates the dark mode button's label and tooltip to describe what pressing it will do.
    function updateThemeToggle(theme) {
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

    // Applies the theme to the page root so both the Smart Hydro and Bootstrap colours switch together.
    function applyTheme(theme) {
        root.setAttribute('data-theme', theme);
        root.setAttribute('data-bs-theme', theme);
        updateThemeToggle(theme);
    }

    // Reads the saved high contrast choice, falling back to the device's more contrast setting when nothing is saved.
    function readStoredContrast() {
        var deviceWantsMore = false;
        if (window.matchMedia && window.matchMedia('(prefers-contrast: more)').matches) {
            deviceWantsMore = true;
        }

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

    // Saves the high contrast choice so it stays on across pages and visits.
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

    // Updates the contrast button's pressed state and wording to match the current mode.
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

    // Switches high contrast on or off by setting data-contrast on the page root.
    function applyContrast(isHigh) {
        if (isHigh) {
            root.setAttribute('data-contrast', 'high');
        } else {
            root.removeAttribute('data-contrast');
        }

        updateContrastToggle(isHigh);
    }

    // Flips between light and dark when the theme button is pressed, and remembers the choice.
    function toggleTheme() {
        var nextTheme = 'dark';
        if (root.getAttribute('data-theme') === 'dark') {
            nextTheme = 'light';
        }

        applyTheme(nextTheme);
        storeTheme(nextTheme);
    }

    // Flips high contrast on or off when the contrast button is pressed, and remembers the choice.
    function toggleContrast() {
        var isHigh = root.getAttribute('data-contrast') !== 'high';
        applyContrast(isHigh);
        storeContrast(isHigh);
    }

    // Connects the top bar buttons once the page has loaded and syncs their labels with the applied settings.
    function wireToggles() {
        var themeToggle = document.getElementById('sh-theme-toggle');
        if (themeToggle) {
            updateThemeToggle(root.getAttribute('data-theme'));
            themeToggle.addEventListener('click', toggleTheme);
        }

        var contrastToggle = document.getElementById('sh-contrast-toggle');
        if (contrastToggle) {
            updateContrastToggle(root.getAttribute('data-contrast') === 'high');
            contrastToggle.addEventListener('click', toggleContrast);
        }
    }

    applyTheme(getInitialTheme());
    applyContrast(readStoredContrast());

    document.addEventListener('DOMContentLoaded', wireToggles);
})();
