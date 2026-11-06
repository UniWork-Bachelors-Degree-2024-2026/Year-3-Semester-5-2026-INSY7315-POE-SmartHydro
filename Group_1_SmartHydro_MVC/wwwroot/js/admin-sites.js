(function () {
    'use strict';

    /* Builds the extra line in the pop-up that warns how many structures go with the site. */
    function describeStructures(count) {
        if (count === 1) {
            return 'Its 1 structure will be moved to deleted items too. Restoring the site brings it back.';
        }

        if (count > 1) {
            return 'Its ' + count + ' structures will be moved to deleted items too. Restoring the site brings them back.';
        }

        return '';
    }

    /* Fills the delete pop-up with the chosen site before showing it. */
    function openDeleteModal(button, parts) {
        var name = button.getAttribute('data-delete-site-name') || 'This site';
        var structures = parseInt(button.getAttribute('data-delete-site-structures') || '0', 10);

        parts.id.value = button.getAttribute('data-delete-site-id') || '';
        parts.nameField.value = name;
        parts.name.textContent = name;
        parts.structures.textContent = describeStructures(structures);
        parts.submit.disabled = false;
        parts.modal.show();
    }

    /* Builds the extra line in the purge pop-up that warns how many structures are removed with the site. */
    function describePurgeStructures(count) {
        if (count === 1) {
            return 'Its 1 structure, with its readings and thresholds, is permanently removed too.';
        }

        if (count > 1) {
            return 'Its ' + count + ' structures, with their readings and thresholds, are permanently removed too.';
        }

        return '';
    }

    /* Wires the Purge buttons in the deleted sites list to the "Permanently delete?" pop-up. */
    function setUpPurge() {
        var modalElement = document.getElementById('sh-purge-site-modal');
        if (!modalElement || typeof bootstrap === 'undefined') {
            return;
        }

        var modal = bootstrap.Modal.getOrCreateInstance(modalElement);
        var form = document.getElementById('sh-purge-site-form');
        var idInput = document.getElementById('sh-purge-site-id');
        var nameField = document.getElementById('sh-purge-site-name-field');
        var nameText = document.getElementById('sh-purge-site-name');
        var structuresText = document.getElementById('sh-purge-site-structures');
        var submit = document.getElementById('sh-purge-site-submit');

        document.querySelectorAll('[data-purge-site-id]').forEach(function (button) {
            button.addEventListener('click', function () {
                var name = button.getAttribute('data-purge-site-name') || 'This site';
                var structures = parseInt(button.getAttribute('data-purge-site-structures') || '0', 10);

                idInput.value = button.getAttribute('data-purge-site-id') || '';
                nameField.value = name;
                nameText.textContent = name;
                structuresText.textContent = describePurgeStructures(structures);
                submit.disabled = false;
                modal.show();
            });
        });

        form.addEventListener('submit', function () {
            submit.disabled = true;
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        setUpPurge();

        var modalElement = document.getElementById('sh-delete-site-modal');
        if (!modalElement || typeof bootstrap === 'undefined') {
            return;
        }

        var parts = {
            modal: bootstrap.Modal.getOrCreateInstance(modalElement),
            form: document.getElementById('sh-delete-site-form'),
            id: document.getElementById('sh-delete-site-id'),
            nameField: document.getElementById('sh-delete-site-name-field'),
            name: document.getElementById('sh-delete-site-name'),
            structures: document.getElementById('sh-delete-site-structures'),
            submit: document.getElementById('sh-delete-site-submit')
        };

        document.querySelectorAll('[data-delete-site-id]').forEach(function (button) {
            button.addEventListener('click', function () {
                openDeleteModal(button, parts);
            });
        });

        parts.form.addEventListener('submit', function () {
            parts.submit.disabled = true;
        });
    });
})();
