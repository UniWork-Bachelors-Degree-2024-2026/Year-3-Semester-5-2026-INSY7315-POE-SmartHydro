(function() {
    'use strict';

    document.addEventListener('DOMContentLoaded', function() {
        var pickerEl = document.getElementById('sh-site-modal');
        var createEl = document.getElementById('sh-site-create-modal');
        if (!pickerEl || typeof bootstrap === 'undefined') {
            return;
        }

        var picker = bootstrap.Modal.getOrCreateInstance(pickerEl);
        var required = pickerEl.getAttribute('data-required') === 'true';
        var noSites = pickerEl.getAttribute('data-no-sites') === 'true';

        if (!createEl) {
            if (required) {
                picker.show();
            }
            return;
        }

        var create = bootstrap.Modal.getOrCreateInstance(createEl);

        var form = document.getElementById('sh-site-create-form');
        var nameInput = document.getElementById('sh-site-name');
        var submitBtn = document.getElementById('sh-site-create-submit');
        var formError = document.getElementById('sh-site-form-error');
        var openCreateBtn = document.getElementById('sh-site-open-create');
        var backBtn = document.getElementById('sh-site-create-back');
        var closeBtn = document.getElementById('sh-site-create-close');
        var idInput = document.getElementById('sh-site-id');
        var formTitle = document.getElementById('sh-site-create-title');
        var formText = document.getElementById('sh-site-create-text');
        var formIcon = document.getElementById('sh-site-form-icon');
        var submitLabel = document.getElementById('sh-site-submit-label');
        var editButtons = pickerEl.querySelectorAll('.sh-site-edit-btn');
        var isEditing = false;

        var deleteEl = document.getElementById('sh-site-delete-modal');
        var deleteButtons = pickerEl.querySelectorAll('.sh-site-delete-btn');

        /* Hides one modal and shows the other once it has fully closed, so Bootstrap never has two backdrops open at the same time. */
        function swap(from, fromEl, to) {
            fromEl.addEventListener('hidden.bs.modal', function onHidden() {
                fromEl.removeEventListener('hidden.bs.modal', onHidden);
                to.show();
            });
            from.hide();
        }

        function clearErrors() {
            formError.hidden = true;
            formError.textContent = '';
            form.querySelectorAll('.sh-field-error[data-for]').forEach(function(el) {
                el.textContent = '';
            });
            nameInput.classList.remove('input-validation-error');
            nameInput.removeAttribute('aria-invalid');
        }

        function showFieldError(field, message) {
            var target = form.querySelector('.sh-field-error[data-for="' + field + '"]');
            var input = null;
            if (field === 'name') {
                input = nameInput;
            }
            if (target) {
                target.textContent = message;
            }
            if (input) {
                input.classList.add('input-validation-error');
                input.setAttribute('aria-invalid', 'true');
            }
        }

        function setBusy(busy) {
            submitBtn.disabled = busy;
            backBtn.disabled = busy;
            closeBtn.disabled = busy;
        }

        /* Sets the site pop-up up for creating a new site, or for editing the given site, and clears any old values and errors. */
        function prepareForm(site) {
            form.reset();
            clearErrors();
            setBusy(false);

            isEditing = site != null;
            if (isEditing) {
                form.action = form.getAttribute('data-edit-url');
                idInput.value = site.id;
                nameInput.value = site.name;
                formTitle.textContent = 'Edit site';
                formText.textContent = 'Change the name of this site.';
                formIcon.textContent = 'edit_location_alt';
                submitLabel.textContent = 'Save changes';
            } else {
                form.action = form.getAttribute('data-create-url');
                idInput.value = '';
                formTitle.textContent = 'Create a site';
                formText.textContent = 'Give your new site a name. You\'ll be switched to it straight away.';
                formIcon.textContent = 'add_location_alt';
                submitLabel.textContent = 'Create site';

                if (noSites) {
                    formTitle.textContent = 'Add your first site';
                    formText.textContent = 'You need a site before you can use Smart Hydro. Give it a name and you\'ll be switched to it straight away.';
                }
            }
        }

        /* Fills the site pop-up for a new or existing site and swaps to it from the site picker. */
        function openForm(site) {
            prepareForm(site);
            swap(picker, pickerEl, create);
        }

        openCreateBtn.addEventListener('click', function() {
            openForm(null);
        });

        editButtons.forEach(function(button) {
            button.addEventListener('click', function() {
                openForm({
                    id: button.getAttribute('data-site-id'),
                    name: button.getAttribute('data-site-name') || ''
                });
            });
        });

        backBtn.addEventListener('click', function() {
            swap(create, createEl, picker);
        });

        /* Closes the site pop-up. On first run there is no site yet, so it goes back to the site picker instead. */
        function closeCreate() {
            if (required) {
                swap(create, createEl, picker);
                return;
            }
            create.hide();
        }

        closeBtn.addEventListener('click', closeCreate);

        pickerEl.addEventListener('show.bs.modal', function() {
            document.body.classList.remove('sh-sidebar-open');
        });

        createEl.addEventListener('shown.bs.modal', function() {
            nameInput.focus();
        });

        nameInput.addEventListener('input', function() {
            var target = form.querySelector('.sh-field-error[data-for="name"]');
            if (target) {
                target.textContent = '';
            }
            nameInput.classList.remove('input-validation-error');
            nameInput.removeAttribute('aria-invalid');
        });

        form.addEventListener('submit', function(event) {
            event.preventDefault();
            clearErrors();

            if (nameInput.value.trim().length === 0) {
                showFieldError('name', 'Please enter a site name.');
                nameInput.focus();
                return;
            }

            setBusy(true);

            fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin'
            })
                .then(function(response) {
                    return response.json().catch(function() {
                        return { ok: false };
                    });
                })
                .then(function(data) {
                    if (data && data.ok && data.redirect) {
                        window.location.href = data.redirect;
                        return;
                    }

                    if (data && data.ok) {
                        window.location.reload();
                        return;
                    }

                    setBusy(false);

                    if (data && data.errors) {
                        Object.keys(data.errors).forEach(function(field) {
                            showFieldError(field, data.errors[field]);
                        });
                        if (data.errors.name) {
                            nameInput.focus();
                        }
                        return;
                    }

                    if (data && data.message) {
                        formError.textContent = data.message;
                    } else if (isEditing) {
                        formError.textContent = 'Something went wrong saving the site. Please try again.';
                    } else {
                        formError.textContent = 'Something went wrong creating the site. Please try again.';
                    }
                    formError.hidden = false;
                })
                .catch(function() {
                    setBusy(false);
                    formError.textContent = 'Something went wrong. Please try again.';
                    formError.hidden = false;
                });
        });

        if (deleteEl) {
            wireDelete();
        }

        /* Wires each site's Delete button in the site list to a confirm pop-up that deletes the site and everything under it. */
        function wireDelete() {
            var deleteModal = bootstrap.Modal.getOrCreateInstance(deleteEl);
            var deleteForm = document.getElementById('sh-site-delete-form');
            var deleteId = document.getElementById('sh-site-delete-id');
            var deleteTitle = document.getElementById('sh-site-delete-title');
            var deleteText = document.getElementById('sh-site-delete-text');
            var deleteError = document.getElementById('sh-site-delete-error');
            var deleteCancel = document.getElementById('sh-site-delete-cancel');
            var deleteConfirm = document.getElementById('sh-site-delete-confirm');

            function describeConsequences(count) {
                if (count === 1) {
                    return 'Its 1 structure moves to deleted items and its workers lose access.';
                }
                if (count > 1) {
                    return 'Its ' + count + ' structures move to deleted items and its workers lose access.';
                }
                return 'Its workers lose access.';
            }

            function setDeleteBusy(busy) {
                deleteConfirm.disabled = busy;
                deleteCancel.disabled = busy;
            }

            function showDeleteError(message) {
                window.shSiteDeleteInProgress = false;
                setDeleteBusy(false);
                deleteError.textContent = message;
                deleteError.hidden = false;
            }

            deleteButtons.forEach(function(button) {
                button.addEventListener('click', function() {
                    var name = button.getAttribute('data-site-name') || '';
                    var structures = parseInt(button.getAttribute('data-site-structures') || '0', 10) || 0;

                    deleteId.value = button.getAttribute('data-site-id');
                    deleteTitle.textContent = 'Delete ' + name + '?';
                    deleteText.textContent = describeConsequences(structures);
                    deleteError.hidden = true;
                    deleteError.textContent = '';
                    setDeleteBusy(false);

                    swap(picker, pickerEl, deleteModal);
                });
            });

            deleteCancel.addEventListener('click', function() {
                swap(deleteModal, deleteEl, picker);
            });

            deleteForm.addEventListener('submit', function(event) {
                event.preventDefault();
                deleteError.hidden = true;
                setDeleteBusy(true);
                window.shSiteDeleteInProgress = true;

                fetch(deleteForm.action, {
                    method: 'POST',
                    body: new FormData(deleteForm),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin'
                })
                    .then(function(response) {
                        return response.json().catch(function() {
                            return { ok: false };
                        });
                    })
                    .then(function(data) {
                        if (data && data.ok && data.redirect) {
                            window.location.href = data.redirect;
                            return;
                        }

                        if (data && data.ok) {
                            window.location.reload();
                            return;
                        }

                        if (data && data.message) {
                            showDeleteError(data.message);
                        } else {
                            showDeleteError('Something went wrong deleting the site. Please try again.');
                        }
                    })
                    .catch(function() {
                        showDeleteError('Something went wrong. Please try again.');
                    });
            });
        }

        if (required) {
            picker.show();
        }
    });
})();