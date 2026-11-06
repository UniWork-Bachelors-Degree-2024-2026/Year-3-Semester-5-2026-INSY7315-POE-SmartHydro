(function () {
    'use strict';

    var SELECTED_DATE_KEY = 'sh-selected-capture-date';

    function pad(number) {
        if (number < 10) {
            return '0' + number;
        }
        return String(number);
    }

    function todayKey() {
        var now = new Date();
        return now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate());
    }

    function selectedDateKey() {
        var stored = '';
        /* Mozilla Developer (2026e) documents window.sessionStorage for keeping a value for the current browser tab only. */
        try {
            stored = window.sessionStorage.getItem(SELECTED_DATE_KEY) || '';
        } catch (error) {
        }
        return stored || todayKey();
    }

    function create(options) {
        var root = options.root;
        var source = options.source || 'device';
        var defaultStructure = options.structure || '';
        var cameraId = options.camera || '';
        var onClosed = options.onClosed || function () { };

        var toastElement = document.getElementById('sh-live-toast');
        var toastText = document.getElementById('sh-live-toast-text');
        var toastLink = document.getElementById('sh-live-toast-link');
        var toastIcon = document.getElementById('sh-live-toast-icon');
        /* Microsoft Learn (2026) explains sending the antiforgery token with JavaScript requests so [ValidateAntiForgeryToken] accepts them. */
        var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');

        var saveModalElement = document.getElementById('sh-save-modal');
        var saveForm = document.getElementById('sh-save-form');
        var savePreview = document.getElementById('sh-save-preview');
        var saveDateFieldEl = document.getElementById('sh-save-date-field');
        var saveDateField = null;
        if (saveDateFieldEl && window.ShDateField) {
            saveDateField = window.ShDateField.create(saveDateFieldEl);
        }
        var saveStructure = document.getElementById('sh-save-structure');
        var savePlant = document.getElementById('sh-save-plant');
        var saveFavourite = document.getElementById('sh-save-favourite');
        var saveError = document.getElementById('sh-save-error');
        var saveSubmit = document.getElementById('sh-save-submit');
        var saveRetake = document.getElementById('sh-save-retake');

        var saving = false;
        var open = false;
        var pendingBlob = null;
        var previewUrl = '';
        var dialogShown = false;
        var closeWhenShown = false;

        function notify(text, isError, showLink) {
            if (!toastElement || !window.bootstrap) {
                return;
            }

            toastText.textContent = text;
            toastLink.hidden = !showLink;
            toastElement.classList.toggle('is-error', isError);
            if (toastIcon) {
                if (isError) {
                    toastIcon.textContent = 'priority_high';
                } else {
                    toastIcon.textContent = 'check';
                }
            }
            bootstrap.Toast.getOrCreateInstance(toastElement).show();
        }

        function showSaveError(text) {
            saveError.textContent = text;
            saveError.hidden = text === '';
        }

        function setSaving(isSaving) {
            saving = isSaving;
            saveSubmit.disabled = isSaving;
            saveRetake.disabled = isSaving;
            if (isSaving) {
                saveSubmit.textContent = 'Saving...';
            } else {
                saveSubmit.textContent = 'Save photo';
            }
        }

        function releasePreview() {
            if (previewUrl !== '') {
                URL.revokeObjectURL(previewUrl);
                previewUrl = '';
            }
            savePreview.removeAttribute('src');
        }

        function chooseDefaultStructure() {
            if (defaultStructure === '') {
                return;
            }

            var match = Array.prototype.some.call(saveStructure.options, function (option) {
                return option.value === defaultStructure;
            });
            if (match) {
                saveStructure.value = defaultStructure;
            }
        }

        function openDialog(blob) {
            pendingBlob = blob;
            open = true;

            releasePreview();
            /* Mozilla Developer (2026d) documents URL.createObjectURL() and URL.revokeObjectURL() for showing a Blob as an image. */
            previewUrl = URL.createObjectURL(blob);
            savePreview.src = previewUrl;

            saveForm.reset();
            showSaveError('');
            setSaving(false);
            chooseDefaultStructure();

            if (saveDateField) {
                saveDateField.refreshMax(todayKey());
                saveDateField.setValue(selectedDateKey());
            }

            bootstrap.Modal.getOrCreateInstance(saveModalElement).show();
        }

        function closeDialog() {
            if (dialogShown) {
                bootstrap.Modal.getOrCreateInstance(saveModalElement).hide();
            } else {
                closeWhenShown = true;
            }
        }

        function send() {
            if (!pendingBlob || saving) {
                return;
            }

            if (saveDateField && saveDateField.getValue() === '') {
                showSaveError('Choose the date this photo was taken.');
                return;
            }

            var takenAt = '';
            if (saveDateField) {
                takenAt = saveDateField.getValue();
            }

            /* Mozilla Developer (2026a) demonstrates building a FormData object with a Blob and sending it with fetch(). */
            var data = new FormData();
            data.append('photo', pendingBlob, 'capture.jpg');
            data.append('source', source);
            data.append('camera', cameraId);
            data.append('structure', saveStructure.value);
            data.append('plant', savePlant.value.trim());
            var favouriteValue = false;
            if (saveFavourite) {
                favouriteValue = saveFavourite.checked;
            }
            data.append('favourite', String(favouriteValue));
            data.append('takenAt', takenAt);
            if (tokenInput) {
                data.append('__RequestVerificationToken', tokenInput.value);
            }

            setSaving(true);
            showSaveError('');

            fetch(root.getAttribute('data-capture-url'), { method: 'POST', body: data, credentials: 'same-origin' })
                .then(function (response) {
                    return response.json().then(
                        function (body) { return { ok: response.ok, body: body }; },
                        function () { return { ok: response.ok, body: null }; }
                    );
                })
                .then(function (result) {
                    setSaving(false);

                    if (result.ok && result.body && result.body.success) {
                        pendingBlob = null;
                        closeDialog();
                        notify('Photo saved to the gallery.', false, true);
                        return;
                    }

                    var text = 'The photo could not be saved. Try again.';
                    if (result.body && result.body.message) {
                        text = result.body.message;
                    }
                    showSaveError(text);
                })
                .catch(function () {
                    setSaving(false);
                    showSaveError('The photo could not be saved. Try again.');
                });
        }

        saveForm.addEventListener('submit', function (event) {
            event.preventDefault();
            send();
        });

        saveModalElement.addEventListener('shown.bs.modal', function () {
            dialogShown = true;

            if (closeWhenShown) {
                closeWhenShown = false;
                bootstrap.Modal.getOrCreateInstance(saveModalElement).hide();
            }
        });

        saveModalElement.addEventListener('hide.bs.modal', function (event) {
            if (saving) {
                event.preventDefault();
            }
        });

        saveModalElement.addEventListener('hidden.bs.modal', function () {
            pendingBlob = null;
            open = false;
            dialogShown = false;
            closeWhenShown = false;
            releasePreview();
            showSaveError('');

            if (saveDateField) {
                saveDateField.close();
            }

            onClosed();
        });

        return {
            open: openDialog,
            isOpen: function () { return open; },
            notify: notify
        };
    }

    window.ShCameraSave = { create: create };
})();

/*
    Reference List:
    Microsoft Learn, 2026. Prevent Cross-Site Request Forgery (XSRF/CSRF) attacks in ASP.NET Core. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery> [Accessed 22 September 2026].
    Mozilla Developer, 2026a. FormData. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/FormData> [Accessed 22 September 2026].
    Mozilla Developer, 2026d. URL: createObjectURL() static method. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL_static> [Accessed 22 September 2026].
    Mozilla Developer, 2026e. Window: sessionStorage property. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/Window/sessionStorage> [Accessed 22 September 2026].
*/