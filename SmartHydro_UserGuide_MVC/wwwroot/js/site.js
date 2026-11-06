document.addEventListener('DOMContentLoaded', function () {
    var menuToggle = document.getElementById('sh-menu-toggle');
    var backdrop = document.getElementById('sh-backdrop');
    var sidebar = document.getElementById('sh-sidebar');
    var body = document.body;

    // Saves the real screen height so the phone menu always reaches the bottom, even after the browser bar slides away.
    function setScreenHeight() {
        var height = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        document.documentElement.style.setProperty('--sh-vh', Math.round(height) + 'px');
    }

    // Closes the phone menu and marks the menu button as collapsed.
    function closeSidebar() {
        body.classList.remove('sh-sidebar-open');

        if (menuToggle) {
            menuToggle.setAttribute('aria-expanded', 'false');
            menuToggle.setAttribute('aria-label', 'Open menu');
        }
    }

    // Opens the phone menu and moves keyboard focus to its first link so Tab carries on inside the menu.
    function openSidebar() {
        setScreenHeight();
        body.classList.add('sh-sidebar-open');
        menuToggle.setAttribute('aria-expanded', 'true');
        menuToggle.setAttribute('aria-label', 'Close menu');

        if (sidebar) {
            var firstItem = sidebar.querySelector('a[href], button:not([disabled])');

            if (firstItem) {
                firstItem.focus();
            }
        }
    }

    // Opens the phone menu when it is closed and closes it when it is open.
    function toggleSidebar() {
        if (body.classList.contains('sh-sidebar-open')) {
            closeSidebar();
        } else {
            openSidebar();
        }
    }

    // Closes the phone menu on Escape and returns focus to the menu button.
    function closeOnEscape(event) {
        if (event.key !== 'Escape' || !body.classList.contains('sh-sidebar-open')) {
            return;
        }

        closeSidebar();

        if (menuToggle) {
            menuToggle.focus();
        }
    }

    if (menuToggle) {
        menuToggle.addEventListener('click', toggleSidebar);
    }

    if (backdrop) {
        backdrop.addEventListener('click', closeSidebar);
    }

    document.addEventListener('keydown', closeOnEscape);

    setScreenHeight();
    window.addEventListener('resize', setScreenHeight);

    if (window.visualViewport) {
        window.visualViewport.addEventListener('resize', setScreenHeight);
    }
});