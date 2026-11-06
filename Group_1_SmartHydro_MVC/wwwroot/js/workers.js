(function () {
    'use strict';

    var wording = {
        remove: {
            icon: 'person_remove',
            title: 'Remove from site?',
            confirm: 'Yes, remove',
            text: function (email, site) {
                return email + ' will lose access to ' + site + ' straight away. Their account and any other sites they are on are not affected.';
            }
        },
        cancel: {
            icon: 'cancel_schedule_send',
            title: 'Cancel invitation?',
            confirm: 'Yes, cancel it',
            text: function (email, site) {
                return 'The link and access code sent to ' + email + ' for ' + site + ' will stop working.';
            }
        }
    };

    /* Fills the confirm pop-up for the chosen worker or invitation and points its form at the right action before showing it. */
    function openConfirm(button, parts) {
        var kind = wording[button.getAttribute('data-team-kind')] || wording.remove;
        var email = button.getAttribute('data-team-email') || '';
        var site = button.getAttribute('data-team-site') || 'this site';

        parts.form.action = button.getAttribute('data-team-action');
        parts.email.value = email;
        parts.icon.textContent = kind.icon;
        parts.title.textContent = kind.title;
        parts.text.textContent = kind.text(email, site);
        parts.confirm.textContent = kind.confirm;
        parts.confirm.disabled = false;
        parts.modal.show();
    }

    document.addEventListener('DOMContentLoaded', function () {
        var modalElement = document.getElementById('sh-team-modal');
        if (!modalElement || typeof bootstrap === 'undefined') {
            return;
        }

        var parts = {
            modal: bootstrap.Modal.getOrCreateInstance(modalElement),
            form: document.getElementById('sh-team-modal-form'),
            email: document.getElementById('sh-team-modal-email'),
            icon: document.getElementById('sh-team-modal-icon'),
            title: document.getElementById('sh-team-modal-title'),
            text: document.getElementById('sh-team-modal-text'),
            confirm: document.getElementById('sh-team-modal-confirm')
        };

        document.querySelectorAll('[data-team-action]').forEach(function (button) {
            button.addEventListener('click', function () {
                openConfirm(button, parts);
            });
        });

        parts.form.addEventListener('submit', function () {
            parts.confirm.disabled = true;
        });
    });
})();