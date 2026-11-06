(function () {
    function wirePasswordToggle(toggleId, inputId) {
        var toggle = document.getElementById(toggleId);
        var input = document.getElementById(inputId);

        if (!toggle || !input) {
            return;
        }

        toggle.addEventListener('click', function () {
            var showing = input.getAttribute('type') === 'text';

            if (showing) {
                input.setAttribute('type', 'password');
                toggle.querySelector('.material-symbols-outlined').textContent = 'visibility';
                toggle.setAttribute('aria-label', 'Show password');
            } else {
                input.setAttribute('type', 'text');
                toggle.querySelector('.material-symbols-outlined').textContent = 'visibility_off';
                toggle.setAttribute('aria-label', 'Hide password');
            }
        });
    }

    function setupAuthAlert() {
        var alertBox = document.getElementById('sh-auth-alert');
        if (!alertBox) {
            return;
        }

        var closeButton = document.getElementById('sh-auth-alert-close');
        var dismissed = false;

        function dismiss() {
            if (dismissed) {
                return;
            }

            dismissed = true;
            alertBox.classList.add('is-hiding');

            window.setTimeout(function () {
                if (alertBox.parentNode) {
                    alertBox.parentNode.removeChild(alertBox);
                }
            }, 300);
        }

        if (closeButton) {
            closeButton.addEventListener('click', dismiss);
        }

        window.setTimeout(dismiss, 6000);
    }

    function setupFieldErrorClearing() {
        ['FarmName', 'FullName', 'Email', 'Password', 'ConfirmPassword'].forEach(function (id) {
            var input = document.getElementById(id);
            if (!input) {
                return;
            }

            input.addEventListener('input', function () {
                input.classList.remove('input-validation-error');

                var message = document.querySelector('[data-valmsg-for="' + id + '"]');
                if (message) {
                    message.textContent = '';
                }
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        wirePasswordToggle('sh-password-toggle', 'Password');
        wirePasswordToggle('sh-confirm-password-toggle', 'ConfirmPassword');
        setupAuthAlert();
        setupFieldErrorClearing();
    });
})();