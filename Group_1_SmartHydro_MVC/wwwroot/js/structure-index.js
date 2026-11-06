document.addEventListener('DOMContentLoaded', function () {
    setUpSelectPrompt();
    setUpDelete();
    setUpPurge();
});

/* Shows the "Select a structure" pop-up when the page was opened with that message, unless the site picker is open. */
function setUpSelectPrompt() {
    var modalElement = document.getElementById('sh-select-structure-modal');
    if (!modalElement) {
        return;
    }

    var sitePicker = document.getElementById('sh-site-modal');
    if (sitePicker && sitePicker.getAttribute('data-required') === 'true') {
        return;
    }

    if (!window.bootstrap) {
        alert(document.getElementById('sh-select-structure-text').textContent);
        return;
    }

    modalElement.addEventListener('hidden.bs.modal', focusFirstStructure);

    new bootstrap.Modal(modalElement).show();
}

/* Opens the "Are you sure?" pop-up for a structure's Delete button, and sends the delete once the user confirms. */
function setUpDelete() {
    var modalElement = document.getElementById('sh-delete-structure-modal');
    if (!modalElement) {
        return;
    }

    var form = document.getElementById('sh-delete-structure-form');
    var idInput = document.getElementById('sh-delete-structure-id');
    var nameText = document.getElementById('sh-delete-structure-name');
    var cancelButton = document.getElementById('sh-delete-structure-cancel');
    var submitButton = document.getElementById('sh-delete-structure-submit');
    var deleteButtons = document.querySelectorAll('.sh-structure-delete[data-structure-id]:not([data-structure-purge])');
    var lastButton = null;

    function resetButtons() {
        submitButton.disabled = false;
        cancelButton.disabled = false;
        submitButton.textContent = 'Yes, delete';
    }

    deleteButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            lastButton = button;
            idInput.value = button.getAttribute('data-structure-id');
            nameText.textContent = button.getAttribute('data-structure-name') || 'This structure';
            resetButtons();

            if (window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(modalElement).show();
                return;
            }

            if (window.confirm(nameText.textContent + ' will be permanently deleted. This can\'t be undone.')) {
                form.submit();
            }
        });
    });

    modalElement.addEventListener('show.bs.modal', function () {
        window.setTimeout(function () {
            var backdrops = document.querySelectorAll('.modal-backdrop');
            if (backdrops.length > 0) {
                backdrops[backdrops.length - 1].classList.add('sh-delete-backdrop');
            }
        }, 0);
    });

    modalElement.addEventListener('shown.bs.modal', function () {
        cancelButton.focus();
    });

    modalElement.addEventListener('hidden.bs.modal', function () {
        resetButtons();
        if (lastButton) {
            lastButton.focus();
        }
    });

    form.addEventListener('submit', function (event) {
        if (idInput.value === '') {
            event.preventDefault();
            return;
        }
        submitButton.disabled = true;
        cancelButton.disabled = true;
        submitButton.textContent = 'Deleting...';
    });
}

/* Opens the "Permanently delete?" pop-up for a deleted structure's Purge button (Admins only),
   and sends the purge once the user confirms. Mirrors setUpDelete above. */
function setUpPurge() {
    var modalElement = document.getElementById('sh-purge-structure-modal');
    if (!modalElement) {
        return;
    }

    var form = document.getElementById('sh-purge-structure-form');
    var idInput = document.getElementById('sh-purge-structure-id');
    var nameText = document.getElementById('sh-purge-structure-name');
    var cancelButton = document.getElementById('sh-purge-structure-cancel');
    var submitButton = document.getElementById('sh-purge-structure-submit');
    var purgeButtons = document.querySelectorAll('.sh-structure-delete[data-structure-purge][data-structure-id]');
    var lastButton = null;

    function resetButtons() {
        submitButton.disabled = false;
        cancelButton.disabled = false;
        submitButton.textContent = 'Yes, permanently delete';
    }

    purgeButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            lastButton = button;
            idInput.value = button.getAttribute('data-structure-id');
            nameText.textContent = button.getAttribute('data-structure-name') || 'This structure';
            resetButtons();

            if (window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(modalElement).show();
                return;
            }

            if (window.confirm(nameText.textContent + ' will be permanently removed. This cannot be restored afterwards.')) {
                form.submit();
            }
        });
    });

    modalElement.addEventListener('show.bs.modal', function () {
        window.setTimeout(function () {
            var backdrops = document.querySelectorAll('.modal-backdrop');
            if (backdrops.length > 0) {
                backdrops[backdrops.length - 1].classList.add('sh-delete-backdrop');
            }
        }, 0);
    });

    modalElement.addEventListener('shown.bs.modal', function () {
        cancelButton.focus();
    });

    modalElement.addEventListener('hidden.bs.modal', function () {
        resetButtons();
        if (lastButton) {
            lastButton.focus();
        }
    });

    form.addEventListener('submit', function (event) {
        if (idInput.value === '') {
            event.preventDefault();
            return;
        }
        submitButton.disabled = true;
        cancelButton.disabled = true;
        submitButton.textContent = 'Permanently deleting...';
    });
}