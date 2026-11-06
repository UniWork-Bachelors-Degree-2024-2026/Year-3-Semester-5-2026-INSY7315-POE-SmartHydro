(function () {
    'use strict';

    var form = document.getElementById('sh-add-structure-form');
    if (!form) {
        return;
    }

    var isEdit = form.getAttribute('data-mode') === 'edit';

    var macInput = document.getElementById('MacAddress');
    var cameraInput = document.getElementById('CameraMac');
    var nameInput = document.getElementById('Name');
    var typeSelect = document.getElementById('Type');
    var swatches = form.querySelectorAll('input[name="LabelColour"]');

    var previewTile = document.getElementById('sh-preview-tile');
    var previewName = document.getElementById('sh-preview-name');
    var previewBadge = document.getElementById('sh-preview-badge');
    var previewMac = document.getElementById('sh-preview-mac');
    var previewCameraRow = document.getElementById('sh-preview-camera-row');
    var previewCamera = document.getElementById('sh-preview-camera');
    var iconHydroponic = document.getElementById('sh-preview-icon-hydroponic');
    var iconFodder = document.getElementById('sh-preview-icon-fodder');
    var resetButton = document.getElementById('sh-preview-reset');
    var clearModalElement = document.getElementById('sh-clear-modal');
    var clearConfirmButton = document.getElementById('sh-clear-confirm');
    var clearCancelButton = document.getElementById('sh-clear-cancel');
    var clearConfirmed = false;

    var DEFAULT_NAME = 'New Structure';
    var DEFAULT_MAC = 'No MAC address yet';

    function updateName() {
        var value = nameInput.value.trim();
        if (value.length > 0) {
            previewName.textContent = value;
        } else {
            previewName.textContent = DEFAULT_NAME;
        }
    }

    function updateType() {
        var option = typeSelect.options[typeSelect.selectedIndex];
        var isFodder = typeSelect.value === 'Fodder';

        previewBadge.textContent = (option && option.getAttribute('data-badge')) || typeSelect.value;
        iconFodder.hidden = !isFodder;
        iconHydroponic.hidden = isFodder;
    }

    function updateColour() {
        var chosen = form.querySelector('input[name="LabelColour"]:checked');
        if (!chosen) {
            return;
        }

        previewTile.className = previewTile.className
            .replace(/\bsh-label-\S+/g, '')
            .replace(/\s+/g, ' ')
            .trim() + ' sh-label-' + chosen.value;
    }

    /* Turns whatever was typed into upper case hex pairs split by colons, e.g. "a4cf12" -> "A4:CF:12". */
    function formatMac(value) {
        var hex = value.replace(/[^0-9a-fA-F]/g, '').toUpperCase().slice(0, 12);
        var pairs = hex.match(/.{1,2}/g) || [];
        return pairs.join(':');
    }

    function updateMac() {
        var value = macInput.value.trim();
        if (value.length > 0) {
            previewMac.textContent = formatMac(value);
            previewMac.classList.remove('is-empty');
        } else {
            previewMac.textContent = DEFAULT_MAC;
            previewMac.classList.add('is-empty');
        }
    }
    
    function updateCamera() {
        if (!cameraInput || !previewCameraRow) {
            return;
        }

        var value = cameraInput.value.trim();
        previewCamera.textContent = formatMac(value);
        previewCameraRow.hidden = value.length === 0;
    }

    if (cameraInput) {
        cameraInput.addEventListener('input', function () {
            var atEnd = cameraInput.selectionStart === cameraInput.value.length;
            if (atEnd) {
                cameraInput.value = formatMac(cameraInput.value);
            }
            updateCamera();
        });

        cameraInput.addEventListener('blur', function () {
            cameraInput.value = formatMac(cameraInput.value);
            updateCamera();
        });
    }

    macInput.addEventListener('input', function () {
        var atEnd = macInput.selectionStart === macInput.value.length;
        if (atEnd) {
            macInput.value = formatMac(macInput.value);
        }
        updateMac();
    });

    macInput.addEventListener('blur', function () {
        macInput.value = formatMac(macInput.value);
        updateMac();
    });

    nameInput.addEventListener('input', updateName);

    /* Re-checks a field that is showing an error as soon as it changes, so the message clears while typing instead of on blur. */
    function revalidateOnInput(input) {
        input.addEventListener('input', function () {
            if (!window.jQuery || !input.classList.contains('input-validation-error')) {
                return;
            }
            var validator = window.jQuery(form).data('validator');
            if (validator) {
                validator.element(input);
            }
        });
    }

    revalidateOnInput(macInput);
    if (cameraInput) {
        revalidateOnInput(cameraInput);
    }
    revalidateOnInput(nameInput);
    typeSelect.addEventListener('change', updateType);
    for (var i = 0; i < swatches.length; i++) {
        swatches[i].addEventListener('change', updateColour);
    }

    function clearValidation() {
        if (window.jQuery) {
            var validator = window.jQuery(form).data('validator');
            if (validator) {
                validator.resetForm();
            }
        }

        var messages = form.querySelectorAll('.sh-field-error');
        for (var i = 0; i < messages.length; i++) {
            messages[i].textContent = '';
        }

        var invalidInputs = form.querySelectorAll('.input-validation-error');
        for (var j = 0; j < invalidInputs.length; j++) {
            invalidInputs[j].classList.remove('input-validation-error');
        }
    }

    function resetForm() {
        /* In edit mode "reset" means go back to the saved details, so reload the edit page. */
        if (isEdit) {
            window.location.assign(form.getAttribute('action'));
            return;
        }

        macInput.value = '';
        if (cameraInput) {
            cameraInput.value = '';
        }
        nameInput.value = '';
        typeSelect.selectedIndex = 0;

        if (swatches.length > 0) {
            swatches[0].checked = true;
        }

        clearValidation();
        updateName();
        updateType();
        updateColour();
        updateMac();
        updateCamera();

        macInput.focus();
    }

    function getClearModal() {
        if (!clearModalElement || !window.bootstrap) {
            return null;
        }

        return window.bootstrap.Modal.getOrCreateInstance(clearModalElement);
    }

    function askToReset() {
        var clearModal = getClearModal();
        if (clearModal) {
            clearConfirmed = false;
            clearModal.show();
            return;
        }

        var confirmText = 'Are you sure you want to clear the data?';
        if (isEdit) {
            confirmText = 'Are you sure you want to discard your changes?';
        }

        if (window.confirm(confirmText)) {
            resetForm();
        }
    }

    if (resetButton) {
        resetButton.addEventListener('click', askToReset);
    }

    if (clearConfirmButton) {
        clearConfirmButton.addEventListener('click', function () {
            var clearModal = getClearModal();
            clearConfirmed = true;
            if (clearModal) {
                clearModal.hide();
            }
        });
    }

    if (clearModalElement) {
        clearModalElement.addEventListener('shown.bs.modal', function () {
            if (clearCancelButton) {
                clearCancelButton.focus();
            }
        });

        clearModalElement.addEventListener('hidden.bs.modal', function () {
            if (clearConfirmed) {
                clearConfirmed = false;
                resetForm();
                return;
            }

            if (resetButton) {
                resetButton.focus();
            }
        });
    }

    updateName();
    updateType();
    updateColour();
    updateMac();
    updateCamera();
})();