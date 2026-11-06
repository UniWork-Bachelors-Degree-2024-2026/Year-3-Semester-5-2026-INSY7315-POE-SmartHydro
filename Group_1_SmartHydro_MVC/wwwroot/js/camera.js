(function () {
    'use strict';

    var root = document.getElementById('sh-gallery');
    if (!root) {
        return;
    }

    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    var DAY_MS = 24 * 60 * 60 * 1000;
    var DATE_LABELS = { any: 'Date', today: 'Today', yesterday: 'Yesterday', last7: 'Last 7 days', last30: 'Last 30 days' };

    var searchInput = document.getElementById('sh-search-input');
    var searchClear = document.getElementById('sh-search-clear');
    var clearAll = document.getElementById('sh-clear-all');
    var favouritesChip = document.getElementById('sh-chip-favourites');
    var chips = root.querySelectorAll('.sh-chip[data-facet]');
    var resultsEl = document.getElementById('sh-results');
    var countEl = document.getElementById('sh-result-count');
    var emptyEl = document.getElementById('sh-results-empty');
    var noneEl = document.getElementById('sh-results-none');
    var emptyDefaultText = emptyEl.textContent;

    var monthLabel = document.getElementById('sh-month-label');
    var monthMenu = document.getElementById('sh-month-menu');
    var calPrev = document.getElementById('sh-cal-prev');
    var calNext = document.getElementById('sh-cal-next');
    var calMonth = document.getElementById('sh-cal-month');
    var calSub = document.getElementById('sh-cal-sub');
    var calDays = document.getElementById('sh-cal-days');

    var modalElement = document.getElementById('sh-capture-modal');
    var deleteForm = document.getElementById('sh-delete-form');
    var deleteId = document.getElementById('sh-delete-id');
    var deleteStart = document.getElementById('sh-delete-start');
    var deleteModal = document.getElementById('sh-delete-modal');
    var deleteName = document.getElementById('sh-delete-name');
    var deleteCancel = document.getElementById('sh-delete-cancel');
    var deleteSubmit = document.getElementById('sh-delete-submit');
    var capturePreview = document.getElementById('sh-capture-preview');
    var captureImage = document.getElementById('sh-capture-image');
    var captureBackdrop = document.getElementById('sh-capture-backdrop');
    var captureDownload = document.getElementById('sh-capture-download');
    var captureDivider = document.getElementById('sh-capture-divider');
    var favToggle = document.getElementById('sh-fav-toggle');
    var favToggleLabel = document.getElementById('sh-fav-toggle-label');
    var favDetail = document.getElementById('sh-capture-favourite');
    var favouriteUrl = root.getAttribute('data-favourite-url');
    var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');
    var openCard = null;
    var favSaving = false;
    var toastElement = document.getElementById('sh-toast');
    var toastBody = document.getElementById('sh-toast-body');
    var toastIcon = document.getElementById('sh-toast-icon');

    var addModal = document.getElementById('sh-add-modal');
    var chooseStep = document.getElementById('sh-add-choose');
    var chooseUpload = document.getElementById('sh-choose-upload');
    var uploadForm = document.getElementById('sh-add-upload');
    var backButton = document.getElementById('sh-add-back');
    var fileInput = document.getElementById('sh-photo-input');
    var dropzone = document.getElementById('sh-dropzone');
    var dropzoneTitle = document.getElementById('sh-dropzone-title');
    var dropzonePreview = document.getElementById('sh-dropzone-preview');
    var uploadError = document.getElementById('sh-add-error');
    var uploadSubmit = document.getElementById('sh-add-submit');
    var uploadSubmitText = document.getElementById('sh-add-submit-text');
    var addSubtitle = document.getElementById('sh-add-sub');
    var chooseLive = document.getElementById('sh-choose-live');
    var liveStep = document.getElementById('sh-add-live');
    var liveBack = document.getElementById('sh-add-live-back');

    var MAX_PHOTO_BYTES = 10 * 1024 * 1024;
    var PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
    var DROPZONE_TEXT = 'Choose a photo or drop it here';
    var previewUrl = '';

    var uploadDateFieldEl = document.getElementById('sh-upload-date-field');
    var uploadDateField = null;
    if (uploadDateFieldEl && window.ShDateField) {
        uploadDateField = window.ShDateField.create(uploadDateFieldEl);
    }
    var SELECTED_DATE_KEY = 'sh-selected-capture-date';


    function pad(number) {
        if (number < 10) {
            return '0' + number;
        }
        return String(number);
    }

    function parseDay(key) {
        var parts = key.split('-');
        return Date.UTC(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
    }

    function dayKey(dayNumber) {
        var date = new Date(dayNumber);
        return date.getUTCFullYear() + '-' + pad(date.getUTCMonth() + 1) + '-' + pad(date.getUTCDate());
    }

    function shortDate(key) {
        var date = new Date(parseDay(key));
        return date.getUTCDate() + ' ' + MONTHS[date.getUTCMonth()].substring(0, 3);
    }

    function longDate(key) {
        var date = new Date(parseDay(key));
        return date.getUTCDate() + ' ' + MONTHS[date.getUTCMonth()] + ' ' + date.getUTCFullYear();
    }

    function monthIndex(year, month) {
        return year * 12 + month;
    }

    var todayKey = root.getAttribute('data-today');
    var todayNumber = parseDay(todayKey);
    var todayDate = new Date(todayNumber);
    var todayYear = todayDate.getUTCFullYear();
    var todayMonth = todayDate.getUTCMonth();


    var captures = [];
    var dayCounts = {};

    Array.prototype.forEach.call(resultsEl.querySelectorAll('.sh-capture-card'), function (card) {
        var date = card.getAttribute('data-date');

        captures.push({
            el: card,
            date: date,
            dayNumber: parseDay(date),
            plant: card.getAttribute('data-plant'),
            structure: card.getAttribute('data-structure'),
            camera: card.getAttribute('data-camera'),
            favourite: card.getAttribute('data-favourite') === 'true',
            search: card.getAttribute('data-search')
        });

        dayCounts[date] = (dayCounts[date] || 0) + 1;
    });


    var state = { words: [], phrase: '', usePhrase: false, plant: '', structure: '', camera: '', favourites: false, dateType: 'any', dateValue: '' };
    var view = { year: todayYear, month: todayMonth };

    function matchesDate(capture) {
        var age = (todayNumber - capture.dayNumber) / DAY_MS;

        if (state.dateType === 'today') {
            return age === 0;
        }
        if (state.dateType === 'yesterday') {
            return age === 1;
        }
        if (state.dateType === 'last7') {
            return age >= 0 && age < 7;
        }
        if (state.dateType === 'last30') {
            return age >= 0 && age < 30;
        }
        if (state.dateType === 'day') {
            return capture.date === state.dateValue;
        }
        return true;
    }

    function updateSearchMode() {
        state.phrase = state.words.join(' ');
        state.usePhrase = false;

        if (state.words.length < 2) {
            return;
        }

        for (var i = 0; i < captures.length; i++) {
            if (captures[i].search.indexOf(state.phrase) !== -1) {
                state.usePhrase = true;
                return;
            }
        }
    }

    function matchesSearch(capture) {
        if (state.usePhrase) {
            return capture.search.indexOf(state.phrase) !== -1;
        }

        for (var i = 0; i < state.words.length; i++) {
            if (capture.search.indexOf(state.words[i]) === -1) {
                return false;
            }
        }
        return true;
    }

    function matches(capture) {
        if (!matchesSearch(capture)) {
            return false;
        }
        if (state.plant !== '' && capture.plant !== state.plant) {
            return false;
        }
        if (state.structure !== '' && capture.structure !== state.structure) {
            return false;
        }
        if (state.camera !== '' && capture.camera !== state.camera) {
            return false;
        }
        if (state.favourites && !capture.favourite) {
            return false;
        }
        return matchesDate(capture);
    }

    function facetValue(facet) {
        if (facet === 'date') {
            if (state.dateType === 'day') {
                return '';
            }
            return state.dateType;
        }
        return state[facet];
    }

    function facetLabel(facet, fallback) {
        if (facet === 'date') {
            if (state.dateType === 'day') {
                return shortDate(state.dateValue);
            }
            return DATE_LABELS[state.dateType];
        }
        if (state[facet] !== '') {
            return state[facet];
        }
        return fallback;
    }

    function updateChips() {
        /* Mozilla Developer (2026a) demonstrates Array.prototype.forEach() for iterating over array elements. */
        Array.prototype.forEach.call(chips, function (chip) {
            var facet = chip.getAttribute('data-facet');
            var isSet = facet === 'date' || state[facet] !== '';
            var current = facetValue(facet);

            chip.querySelector('.sh-chip-label').textContent = facetLabel(facet, chip.getAttribute('data-default-label'));
            chip.classList.toggle('is-active', isSet);

            /* Mozilla Developer (2026d) demonstrates NodeList methods for working with collections of DOM nodes. */
            Array.prototype.forEach.call(chip.parentNode.querySelectorAll('.dropdown-item'), function (item) {
                var selected = item.getAttribute('data-value') === current;
                item.classList.toggle('active', selected);
                if (selected) {
                    item.setAttribute('aria-current', 'true');
                } else {
                    item.removeAttribute('aria-current');
                }
            });
        });

        favouritesChip.classList.toggle('is-active', state.favourites);
        favouritesChip.setAttribute('aria-pressed', String(state.favourites));
    }

    function updateCount(visible) {
        var text = visible + ' captures';
        if (visible === 1) {
            text = '1 capture';
        }
        countEl.textContent = text;
    }

    function updateEmptyMessage() {
        if (state.dateType === 'day') {
            emptyEl.textContent = 'No captures on ' + longDate(state.dateValue) + '. Try another day or clear the filters.';
        } else {
            emptyEl.textContent = emptyDefaultText;
        }
    }

    function selectedDateKey() {
        if (state.dateType === 'day' && state.dateValue !== '' && state.dateValue <= todayKey) {
            return state.dateValue;
        }
        return todayKey;
    }

    function storeSelectedDate() {
        /* Mozilla Developer (2026f) documents window.sessionStorage for keeping a value for the current browser tab only. */
        try {
            window.sessionStorage.setItem(SELECTED_DATE_KEY, selectedDateKey());
        } catch (error) {
        }
    }

    function refresh() {
        var visible = 0;

        updateSearchMode();
        storeSelectedDate();

        captures.forEach(function (capture) {
            var show = matches(capture);
            capture.el.hidden = !show;
            if (show) {
                visible++;
            }
        });

        updateCount(visible);
        updateEmptyMessage();
        noneEl.hidden = captures.length !== 0;
        emptyEl.hidden = visible !== 0 || captures.length === 0;
        resultsEl.scrollTop = 0;

        updateChips();
        renderCalendar();
    }

    function setFacet(facet, value) {
        if (facet === 'date') {
            state.dateType = value;
            state.dateValue = '';
        } else {
            state[facet] = value;
        }
        refresh();
    }

    function resetAll() {
        state.words = [];
        state.plant = '';
        state.structure = '';
        state.camera = '';
        state.favourites = false;
        state.dateType = 'any';
        state.dateValue = '';
        searchInput.value = '';
        view.year = todayYear;
        view.month = todayMonth;
        refresh();
    }

    function monthButtonText() {
        if (view.year === todayYear && view.month === todayMonth) {
            return 'This month';
        }
        if (view.year === todayYear) {
            return MONTHS[view.month];
        }
        return MONTHS[view.month] + ' ' + view.year;
    }

    function makeYearButton(step, iconName, label) {
        var button = document.createElement('button');
        var icon = document.createElement('span');

        button.type = 'button';
        button.className = 'sh-year-btn';
        button.setAttribute('data-year-step', String(step));
        button.setAttribute('aria-label', label);

        icon.className = 'material-symbols-outlined';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = iconName;

        button.appendChild(icon);
        return button;
    }

    function buildMonthMenu() {
        var header = document.createElement('li');
        var yearLabel = document.createElement('span');

        monthMenu.textContent = '';

        header.className = 'sh-year-row';
        yearLabel.className = 'sh-year-label';
        yearLabel.textContent = String(view.year);

        header.appendChild(makeYearButton(-1, 'chevron_left', 'Previous year'));
        header.appendChild(yearLabel);
        header.appendChild(makeYearButton(1, 'chevron_right', 'Next year'));
        monthMenu.appendChild(header);

        for (var month = 0; month < 12; month++) {
            var item = document.createElement('li');
            var button = document.createElement('button');

            button.type = 'button';
            button.className = 'dropdown-item';
            button.setAttribute('data-month', String(month));
            button.textContent = MONTHS[month];

            if (month === view.month) {
                button.classList.add('active');
            }

            if (view.year === todayYear && month === todayMonth) {
                var note = document.createElement('span');
                note.className = 'sh-menu-note';
                note.textContent = 'This month';
                button.appendChild(note);
            }

            item.appendChild(button);
            monthMenu.appendChild(item);
        }
    }

    function renderCalendar() {
        var firstOfMonth = new Date(Date.UTC(view.year, view.month, 1));
        var offset = (firstOfMonth.getUTCDay() + 6) % 7; // weeks start on Monday
        var daysInMonth = new Date(Date.UTC(view.year, view.month + 1, 0)).getUTCDate();
        var weeks = Math.ceil((offset + daysInMonth) / 7);
        var startNumber = Date.UTC(view.year, view.month, 1 - offset);
        var daysWithCaptures = 0;

        calDays.textContent = '';

        for (var i = 0; i < weeks * 7; i++) {
            var dayNumber = startNumber + i * DAY_MS;
            var date = new Date(dayNumber);
            var key = dayKey(dayNumber);
            var count = dayCounts[key] || 0;
            var button = document.createElement('button');
            var label = longDate(key);

            button.type = 'button';
            button.className = 'sh-cal-day';
            button.textContent = String(date.getUTCDate());
            button.setAttribute('data-day', key);

            if (date.getUTCMonth() !== view.month) {
                button.classList.add('is-outside');
            } else if (count > 0) {
                daysWithCaptures++;
            }

            if (count > 0) {
                button.classList.add('has-captures');
                if (count === 1) {
                    label = label + ', 1 capture';
                } else {
                    label = label + ', ' + count + ' captures';
                }
            }

            button.setAttribute('aria-label', label);
            button.title = label;

            if (key === todayKey) {
                button.classList.add('is-today');
            }

            if (state.dateType === 'day' && state.dateValue === key) {
                button.classList.add('is-selected');
                button.setAttribute('aria-pressed', 'true');
            }

            calDays.appendChild(button);
        }

        calMonth.textContent = MONTHS[view.month] + ' ' + view.year;

        if (daysWithCaptures === 0) {
            calSub.textContent = 'No days with captures';
        } else if (daysWithCaptures === 1) {
            calSub.textContent = '1 day with captures';
        } else {
            calSub.textContent = daysWithCaptures + ' days with captures';
        }

        monthLabel.textContent = monthButtonText();
        buildMonthMenu();
    }

    function showMonth(index) {
        view.year = Math.floor(index / 12);
        view.month = index % 12;
        renderCalendar();
    }

    function valueOrNotSet(value) {
        if (value === null || value === '') {
            return 'Not set';
        }
        return value;
    }

    function resetDelete() {
        deleteSubmit.disabled = false;
        deleteCancel.disabled = false;
        deleteSubmit.textContent = 'Yes, delete';
    }

    function openCapture(card) {
        var title = card.getAttribute('data-title');
        var imageUrl = card.getAttribute('data-image');
        var downloadUrl = card.getAttribute('data-download');
        var image = captureImage;
        var placeholder = document.getElementById('sh-capture-placeholder');
        openCard = card;
        showFavourite(card.getAttribute('data-favourite') === 'true');

        document.getElementById('sh-capture-modal-title').textContent = title;
        document.getElementById('sh-capture-plant').textContent = valueOrNotSet(card.getAttribute('data-plant'));
        document.getElementById('sh-capture-structure').textContent = valueOrNotSet(card.getAttribute('data-structure'));
        document.getElementById('sh-capture-camera').textContent = valueOrNotSet(card.getAttribute('data-camera'));
        document.getElementById('sh-capture-full').textContent = card.getAttribute('data-full');

        deleteId.value = card.getAttribute('data-id');
        deleteName.textContent = title || 'This photo';
        resetDelete();

        if (imageUrl) {
            capturePreview.classList.add('is-loading', 'has-image');
            image.src = imageUrl;
            image.alt = title;
            image.hidden = false;
            captureBackdrop.src = imageUrl;
            captureBackdrop.hidden = false;
            placeholder.hidden = true;

            /* Mozilla Developer (2026c) documents HTMLImageElement.complete for images that have already loaded (for example from the cache). */
            if (image.complete && image.naturalWidth > 0) {
                capturePreview.classList.remove('is-loading');
            }
        } else {
            capturePreview.classList.remove('is-loading', 'has-image');
            image.removeAttribute('src');
            image.hidden = true;
            captureBackdrop.removeAttribute('src');
            captureBackdrop.hidden = true;
            placeholder.hidden = false;
        }

        if (imageUrl && downloadUrl) {
            captureDownload.href = downloadUrl;
            captureDownload.hidden = false;
        } else {
            captureDownload.removeAttribute('href');
            captureDownload.hidden = true;
        }
        captureDivider.hidden = captureDownload.hidden;

        if (window.bootstrap) {
            bootstrap.Modal.getOrCreateInstance(modalElement).show();
        }
    }

    captureImage.addEventListener('load', function () {
        capturePreview.classList.remove('is-loading');
    });

    captureImage.addEventListener('error', function () {
        capturePreview.classList.remove('is-loading', 'has-image');
        captureImage.hidden = true;
        captureBackdrop.hidden = true;
        captureDownload.hidden = true;
        captureDivider.hidden = true;
        document.getElementById('sh-capture-placeholder').hidden = false;
    });

    function captureFor(card) {
        for (var i = 0; i < captures.length; i++) {
            if (captures[i].el === card) {
                return captures[i];
            }
        }
        return null;
    }

    function showFavourite(isFavourite) {
        favToggle.classList.toggle('is-active', isFavourite);
        favToggle.setAttribute('aria-pressed', String(isFavourite));
        favToggle.querySelector('.material-symbols-outlined').classList.toggle('sh-icon-filled', isFavourite);
        if (isFavourite) {
            favToggleLabel.textContent = 'Favorited';
            favDetail.textContent = 'Yes';
        } else {
            favToggleLabel.textContent = 'Add to favorites';
            favDetail.textContent = 'No';
        }
    }

    /* Keeps the card's star badge, filter data and search words in step with the saved favourite. */
    function applyFavouriteToCard(card, isFavourite) {
        var capture = captureFor(card);
        var thumb = card.querySelector('.sh-capture-thumb');
        var badge = thumb.querySelector('.sh-capture-fav');
        var search = (card.getAttribute('data-search') || '').replace(/\s*favourite favorite/g, '');

        if (isFavourite) {
            search = (search + ' favourite favorite').trim();
        }

        card.setAttribute('data-favourite', String(isFavourite));
        card.setAttribute('data-search', search);

        if (capture) {
            capture.favourite = isFavourite;
            capture.search = search;
        }

        if (isFavourite && !badge) {
            badge = document.createElement('span');
            badge.className = 'sh-capture-fav';
            badge.title = 'Favorite';
            badge.innerHTML = '<span class="material-symbols-outlined sh-icon-filled" aria-hidden="true">star</span>';
            thumb.appendChild(badge);
        } else if (!isFavourite && badge) {
            badge.remove();
        }
    }

    favToggle.addEventListener('click', function () {
        if (!openCard || favSaving || !favouriteUrl) {
            return;
        }

        var card = openCard;
        var wasFavourite = card.getAttribute('data-favourite') === 'true';
        var next = !wasFavourite;
        var data = new FormData();
        data.append('id', card.getAttribute('data-id'));
        data.append('favourite', String(next));
        if (tokenInput) {
            data.append('__RequestVerificationToken', tokenInput.value);
        }

        /* The star changes straight away and is put back if the server does not save it. */
        favSaving = true;
        favToggle.disabled = true;
        showFavourite(next);

        fetch(favouriteUrl, { method: 'POST', body: data, credentials: 'same-origin' })
            .then(function (response) {
                return response.json().then(
                    function (body) { return { ok: response.ok, body: body }; },
                    function () { return { ok: response.ok, body: null }; }
                );
            })
            .then(function (result) {
                if (result.ok && result.body && result.body.success) {
                    applyFavouriteToCard(card, next);
                    refresh();
                    if (next) {
                        showToast('Added to favorites.', 'favourite');
                    } else {
                        showToast('Removed from favorites.', 'unfavourite');
                    }
                    return;
                }
                var message = '';
                if (result.body && result.body.message) {
                    message = result.body.message;
                }
                throw new Error(message);
            })
            .catch(function (error) {
                if (openCard === card) {
                    showFavourite(wasFavourite);
                }
                showToast(error.message || 'The favorite could not be saved. Please try again.', 'error');
            })
            .then(function () {
                favSaving = false;
                favToggle.disabled = false;
            });
    });

    /* The "Are you sure?" popup opens on top of the photo viewer, so the viewer stays open behind it if the user cancels. */
    deleteStart.addEventListener('click', function () {
        if (deleteId.value === '') {
            return;
        }
        resetDelete();
        if (window.bootstrap) {
            bootstrap.Modal.getOrCreateInstance(deleteModal).show();
        }
    });

    deleteModal.addEventListener('show.bs.modal', function () {
        /* Bootstrap adds the backdrop straight after this event, so the newest backdrop is raised above the photo viewer on the next tick. */
        window.setTimeout(function () {
            var backdrops = document.querySelectorAll('.modal-backdrop');
            if (backdrops.length > 1) {
                backdrops[backdrops.length - 1].classList.add('sh-confirm-backdrop');
            }
        }, 0);
    });

    deleteModal.addEventListener('shown.bs.modal', function () {
        deleteCancel.focus();
    });

    deleteModal.addEventListener('hidden.bs.modal', function () {
        resetDelete();
        /* Closing the popup clears Bootstrap's open-modal state, so it is restored while the photo viewer is still showing. */
        if (modalElement.classList.contains('show')) {
            document.body.classList.add('modal-open');
            deleteStart.focus();
        }
    });

    deleteForm.addEventListener('submit', function (event) {
        if (deleteId.value === '') {
            event.preventDefault();
            return;
        }
        deleteSubmit.disabled = true;
        deleteCancel.disabled = true;
        deleteSubmit.textContent = 'Deleting...';
    });

    searchInput.addEventListener('input', function () {
        var text = searchInput.value.toLowerCase().trim();
        state.words = [];
        if (text !== '') {
            state.words = text.split(/\s+/);
        }
        refresh();
    });

    searchClear.addEventListener('click', function () {
        searchInput.value = '';
        state.words = [];
        refresh();
        searchInput.focus();
    });

    clearAll.addEventListener('click', resetAll);

    favouritesChip.addEventListener('click', function () {
        state.favourites = !state.favourites;
        refresh();
    });

    root.addEventListener('click', function (event) {
        var item = event.target.closest('.dropdown-item[data-facet]');
        if (item) {
            setFacet(item.getAttribute('data-facet'), item.getAttribute('data-value'));
        }
    });

    resultsEl.addEventListener('click', function (event) {
        var card = event.target.closest('.sh-capture-card');
        if (card) {
            openCapture(card);
        }
    });

    calDays.addEventListener('click', function (event) {
        var button = event.target.closest('.sh-cal-day');
        if (!button) {
            return;
        }

        var key = button.getAttribute('data-day');

        if (state.dateType === 'day' && state.dateValue === key) {
            setFacet('date', 'any');
            return;
        }

        state.dateType = 'day';
        state.dateValue = key;

        if (button.classList.contains('is-outside')) {
            var date = new Date(parseDay(key));
            view.year = date.getUTCFullYear();
            view.month = date.getUTCMonth();
        }

        refresh();
    });

    calPrev.addEventListener('click', function () {
        showMonth(monthIndex(view.year, view.month) - 1);
    });

    calNext.addEventListener('click', function () {
        showMonth(monthIndex(view.year, view.month) + 1);
    });

    monthMenu.addEventListener('click', function (event) {
        var yearButton = event.target.closest('.sh-year-btn');
        if (yearButton) {
            event.stopPropagation();
            view.year = view.year + parseInt(yearButton.getAttribute('data-year-step'), 10);
            renderCalendar();
            return;
        }

        var item = event.target.closest('.dropdown-item');
        if (item) {
            view.month = parseInt(item.getAttribute('data-month'), 10);
            renderCalendar();
        }
    });


    /* kind is 'success', 'error', 'favourite' or 'unfavourite' and picks the toast's icon and colour. */
    var TOAST_ICONS = { success: 'check', error: 'priority_high', favourite: 'star', unfavourite: 'star' };

    function showToast(text, kind) {
        if (!toastElement || !window.bootstrap) {
            return;
        }

        if (!TOAST_ICONS[kind]) {
            kind = 'success';
        }

        toastBody.textContent = text;
        toastIcon.textContent = TOAST_ICONS[kind];
        toastIcon.classList.toggle('sh-icon-filled', kind === 'favourite');
        toastElement.classList.remove('is-success', 'is-error', 'is-favourite', 'is-unfavourite');
        toastElement.classList.add('is-' + kind);
        bootstrap.Toast.getOrCreateInstance(toastElement).show();
    }

    var flashNote = document.getElementById('sh-flash');
    if (flashNote) {
        showToast(flashNote.getAttribute('data-message'), flashNote.getAttribute('data-kind'));
    }
    function showUploadError(text) {
        uploadError.textContent = text;
        uploadError.hidden = text === '';
    }

    function clearPreview() {
        if (previewUrl !== '') {
            URL.revokeObjectURL(previewUrl);
            previewUrl = '';
        }
        dropzonePreview.removeAttribute('src');
        dropzonePreview.hidden = true;
        dropzone.classList.remove('has-file');
        dropzoneTitle.textContent = DROPZONE_TEXT;
        uploadSubmit.disabled = true;
    }

    function resetUpload() {
        uploadForm.reset();
        clearPreview();
        showUploadError('');
        uploadSubmitText.textContent = 'Upload photo';
    }

    function showStep(step) {
        chooseStep.hidden = step !== 'choose';
        uploadForm.hidden = step !== 'upload';
        if (liveStep) {
            liveStep.hidden = step !== 'live';
        }
        if (step === 'upload') {
            addSubtitle.textContent = 'Choose a photo and add a few details.';
        } else if (step === 'live') {
            addSubtitle.textContent = "Which structure's camera would you like to use?";
        } else {
            addSubtitle.textContent = 'How would you like to add it?';
        }
    }

    function choosePhoto(file) {
        showUploadError('');
        clearPreview();

        if (!file) {
            return;
        }

        if (PHOTO_TYPES.indexOf(file.type) === -1) {
            fileInput.value = '';
            showUploadError('Use a JPEG, PNG or WebP photo.');
            return;
        }

        if (file.size > MAX_PHOTO_BYTES) {
            fileInput.value = '';
            showUploadError('That photo is larger than 10 MB.');
            return;
        }

        /* Mozilla Developer (2026e) documents URL.createObjectURL() and URL.revokeObjectURL() for previewing a local file without uploading it. */
        previewUrl = URL.createObjectURL(file);
        dropzonePreview.src = previewUrl;
        dropzonePreview.hidden = false;
        dropzone.classList.add('has-file');
        dropzoneTitle.textContent = file.name;
        uploadSubmit.disabled = false;
    }

    chooseUpload.addEventListener('click', function () {
        showStep('upload');
        fileInput.focus();
    });

    if (chooseLive && liveStep && liveBack) {
        chooseLive.addEventListener('click', function () {
            showStep('live');
        });

        liveBack.addEventListener('click', function () {
            showStep('choose');
            chooseLive.focus();
        });
    }

    backButton.addEventListener('click', function () {
        resetUpload();
        showStep('choose');
        chooseUpload.focus();
    });

    fileInput.addEventListener('change', function () {
        choosePhoto(fileInput.files[0]);
    });

    /* Mozilla Developer (2026b) documents the HTML Drag and Drop API events and reading dropped files from dataTransfer.files. */
    ['dragenter', 'dragover'].forEach(function (name) {
        dropzone.addEventListener(name, function (event) {
            event.preventDefault();
            dropzone.classList.add('is-over');
        });
    });

    dropzone.addEventListener('dragleave', function () {
        dropzone.classList.remove('is-over');
    });

    dropzone.addEventListener('drop', function (event) {
        event.preventDefault();
        dropzone.classList.remove('is-over');

        var files = event.dataTransfer.files;
        if (!files || files.length === 0) {
            return;
        }

        try {
            fileInput.files = files;
        } catch (error) {
            showUploadError('Dropping is not supported here. Use "Choose a photo" instead.');
            return;
        }
        choosePhoto(fileInput.files[0]);
    });

    uploadForm.addEventListener('submit', function (event) {
        if (fileInput.files.length === 0) {
            event.preventDefault();
            showUploadError('Choose a photo first.');
            return;
        }
        if (uploadDateField && uploadDateField.getValue() === '') {
            event.preventDefault();
            showUploadError('Choose the date this photo was taken.');
            return;
        }
        uploadSubmit.disabled = true;
        uploadSubmitText.textContent = 'Uploading...';
    });

    addModal.addEventListener('show.bs.modal', function () {
        if (uploadDateField) {
            uploadDateField.setValue(selectedDateKey());
        }
    });

    addModal.addEventListener('hidden.bs.modal', function () {
        resetUpload();
        showStep('choose');

        if (uploadDateField) {
            uploadDateField.close();
        }
    });

    /* ---------- Edit photo pop-up: change the photo (upload or camera) and its details ---------- */
    (function () {
        var editUrl = root.getAttribute('data-edit-url');
        var el = document.getElementById('sh-edit-modal');
        var editStart = document.getElementById('sh-edit-start');

        if (!el || !editStart || !editUrl) {
            return;
        }

        var sub = document.getElementById('sh-edit-sub');
        var panels = {
            details: document.getElementById('sh-edit-details'),
            choose: document.getElementById('sh-edit-choose'),
            upload: document.getElementById('sh-edit-upload'),
            camera: document.getElementById('sh-edit-camera')
        };
        var SUBTITLES = {
            details: 'Change the photo or update its details.',
            choose: 'How would you like to change it?',
            upload: 'Choose the new photo.',
            camera: 'Line up the shot, then tap the shutter.'
        };

        var preview = document.getElementById('sh-edit-preview');
        var newBadge = document.getElementById('sh-edit-new-badge');
        var changeButton = document.getElementById('sh-edit-change');
        var changeText = document.getElementById('sh-edit-change-text');
        var dateFieldEl = document.getElementById('sh-edit-date-field');
        var dateField = null;
        if (dateFieldEl && window.ShDateField) {
            dateField = window.ShDateField.create(dateFieldEl);
        }
        var structureSelect = document.getElementById('sh-edit-structure');
        var plantInput = document.getElementById('sh-edit-plant');
        var favouriteInput = document.getElementById('sh-edit-favourite');
        var errorEl = document.getElementById('sh-edit-error');
        var saveButton = document.getElementById('sh-edit-save');
        var saveText = document.getElementById('sh-edit-save-text');
        var cancelButton = document.getElementById('sh-edit-cancel');

        var chooseUploadBtn = document.getElementById('sh-edit-choose-upload');
        var chooseCameraBtn = document.getElementById('sh-edit-choose-camera');
        var chooseBack = document.getElementById('sh-edit-choose-back');

        var fileInput2 = document.getElementById('sh-edit-file');
        var drop = document.getElementById('sh-edit-dropzone');
        var dropTitle = document.getElementById('sh-edit-drop-title');
        var dropPreview = document.getElementById('sh-edit-drop-preview');
        var uploadErr = document.getElementById('sh-edit-upload-error');
        var uploadBack = document.getElementById('sh-edit-upload-back');
        var uploadUse = document.getElementById('sh-edit-upload-use');

        var cam = document.getElementById('sh-edit-cam');
        var camVideo = document.getElementById('sh-edit-cam-video');
        var camCanvas = document.getElementById('sh-edit-cam-canvas');
        var camMessage = document.getElementById('sh-edit-cam-message');
        var camMessageText = document.getElementById('sh-edit-cam-message-text');
        var camRetry = document.getElementById('sh-edit-cam-retry');
        var camShutter = document.getElementById('sh-edit-shutter');
        var camBack = document.getElementById('sh-edit-cam-back');

        var editingCard = null;
        var currentImage = '';
        var pendingFile = null;
        var pendingName = 'photo.jpg';
        var pendingSource = '';
        var pendingUrl = '';
        var chosenFile = null;
        var stream = null;
        var saving = false;

        function showStep(name) {
            Object.keys(panels).forEach(function (key) {
                panels[key].hidden = key !== name;
            });
            sub.textContent = SUBTITLES[name];

            if (name !== 'camera') {
                stopCamera();
            }
            if (dateField && name !== 'details') {
                dateField.close();
            }
        }

        function showError(text) {
            errorEl.textContent = text;
            errorEl.hidden = text === '';
        }

        function showUploadError(text) {
            uploadErr.textContent = text;
            uploadErr.hidden = text === '';
        }

        function releasePending() {
            if (pendingUrl !== '') {
                URL.revokeObjectURL(pendingUrl);
                pendingUrl = '';
            }
            pendingFile = null;
            pendingSource = '';
        }

        function showPreview() {
            if (pendingFile) {
                preview.src = pendingUrl;
                newBadge.hidden = false;
                changeText.textContent = 'Change photo again';
            } else {
                if (currentImage) {
                    preview.src = currentImage;
                } else {
                    preview.removeAttribute('src');
                }
                newBadge.hidden = true;
                changeText.textContent = 'Change photo';
            }
        }

        function setPending(file, name, source) {
            releasePending();
            pendingFile = file;
            pendingName = name;
            pendingSource = source;
            pendingUrl = URL.createObjectURL(file);
            showPreview();
        }

        function setSaving(isSaving) {
            saving = isSaving;
            saveButton.disabled = isSaving;
            cancelButton.disabled = isSaving;
            changeButton.disabled = isSaving;
            if (isSaving) {
                saveText.textContent = 'Saving...';
            } else {
                saveText.textContent = 'Save changes';
            }
        }

        /* ----- upload step ----- */
        function clearChosen() {
            if (dropPreview.src && dropPreview.getAttribute('data-blob') === 'true') {
                URL.revokeObjectURL(dropPreview.src);
            }
            dropPreview.removeAttribute('src');
            dropPreview.removeAttribute('data-blob');
            dropPreview.hidden = true;
            drop.classList.remove('has-file');
            dropTitle.textContent = 'Choose a photo or drop it here';
            uploadUse.disabled = true;
            chosenFile = null;
        }

        function chooseFile(file) {
            showUploadError('');
            clearChosen();

            if (!file) {
                return;
            }
            if (PHOTO_TYPES.indexOf(file.type) === -1) {
                fileInput2.value = '';
                showUploadError('Use a JPEG, PNG or WebP photo.');
                return;
            }
            if (file.size > MAX_PHOTO_BYTES) {
                fileInput2.value = '';
                showUploadError('That photo is larger than 10 MB.');
                return;
            }

            chosenFile = file;
            dropPreview.src = URL.createObjectURL(file);
            dropPreview.setAttribute('data-blob', 'true');
            dropPreview.hidden = false;
            drop.classList.add('has-file');
            dropTitle.textContent = file.name;
            uploadUse.disabled = false;
        }

        fileInput2.addEventListener('change', function () {
            chooseFile(fileInput2.files[0]);
        });

        ['dragenter', 'dragover'].forEach(function (name) {
            drop.addEventListener(name, function (event) {
                event.preventDefault();
                drop.classList.add('is-over');
            });
        });

        drop.addEventListener('dragleave', function () {
            drop.classList.remove('is-over');
        });

        drop.addEventListener('drop', function (event) {
            event.preventDefault();
            drop.classList.remove('is-over');

            var files = event.dataTransfer.files;
            if (!files || files.length === 0) {
                return;
            }
            try {
                fileInput2.files = files;
            } catch (error) {
                showUploadError('Dropping is not supported here. Use "Choose a photo" instead.');
                return;
            }
            chooseFile(fileInput2.files[0]);
        });

        uploadUse.addEventListener('click', function () {
            if (!chosenFile) {
                return;
            }
            setPending(chosenFile, chosenFile.name, 'upload');
            fileInput2.value = '';
            clearChosen();
            showStep('details');
            changeButton.focus();
        });

        /* ----- camera step ----- */
        function stopCamera() {
            if (stream) {
                stream.getTracks().forEach(function (track) {
                    track.stop();
                });
                stream = null;
            }
            camVideo.srcObject = null;
            camShutter.disabled = true;
        }

        function camState(kind, text) {
            if (kind === 'live') {
                camMessage.hidden = true;
                camShutter.disabled = false;
                return;
            }

            camShutter.disabled = true;
            camMessage.hidden = false;

            if (kind === 'connecting') {
                camMessageText.textContent = 'Connecting to the camera...';
                camRetry.hidden = true;
                return;
            }

            camMessageText.textContent = text;
            camRetry.hidden = false;
        }

        function describeCameraError(error) {
            var name = '';
            if (error && error.name) {
                name = error.name;
            }

            if (name === 'NotAllowedError' || name === 'SecurityError' || name === 'PermissionDeniedError') {
                return 'Camera access was blocked. Allow the camera in your browser, then try again.';
            }
            if (name === 'NotFoundError' || name === 'DevicesNotFoundError' || name === 'OverconstrainedError') {
                return 'No camera was found on this device.';
            }
            if (name === 'NotReadableError' || name === 'TrackStartError') {
                return 'The camera is being used by another app. Close it, then try again.';
            }
            return 'The camera could not be started. Try again.';
        }

        function startCamera() {
            stopCamera();

            if (window.isSecureContext === false) {
                camState('error', 'The camera needs a secure connection. Open this page over https.');
                return;
            }
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                camState('error', "This browser can't open a camera. Try a current version of Chrome, Edge, Firefox or Safari.");
                return;
            }

            camState('connecting');

            navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false })
                .then(function (newStream) {
                    /* The user may have gone back or closed the pop-up while the browser was asking for permission. */
                    if (panels.camera.hidden || !el.classList.contains('show')) {
                        newStream.getTracks().forEach(function (track) { track.stop(); });
                        return;
                    }

                    stream = newStream;
                    newStream.getVideoTracks().forEach(function (track) {
                        track.addEventListener('ended', function () {
                            if (stream === newStream) {
                                stopCamera();
                                camState('error', 'The camera was disconnected.');
                            }
                        });
                    });

                    camVideo.srcObject = newStream;
                    var playing = camVideo.play();
                    if (playing && playing.catch) {
                        playing.catch(function () { });
                    }
                })
                .catch(function (error) {
                    camState('error', describeCameraError(error));
                });
        }

        camVideo.addEventListener('playing', function () {
            if (stream) {
                camState('live');
            }
        });

        camShutter.addEventListener('click', function () {
            if (!stream || !camVideo.videoWidth) {
                return;
            }

            camCanvas.width = camVideo.videoWidth;
            camCanvas.height = camVideo.videoHeight;
            camCanvas.getContext('2d').drawImage(camVideo, 0, 0, camCanvas.width, camCanvas.height);

            cam.classList.add('is-flash');
            window.setTimeout(function () {
                cam.classList.remove('is-flash');
            }, 220);

            camCanvas.toBlob(function (blob) {
                if (!blob) {
                    camState('error', 'The photo could not be taken. Try again.');
                    return;
                }
                setPending(blob, 'capture.jpg', 'camera');
                showStep('details');
                changeButton.focus();
            }, 'image/jpeg', 0.92);
        });

        camRetry.addEventListener('click', startCamera);

        /* ----- step navigation ----- */
        changeButton.addEventListener('click', function () {
            showStep('choose');
            chooseUploadBtn.focus();
        });

        chooseUploadBtn.addEventListener('click', function () {
            showUploadError('');
            clearChosen();
            showStep('upload');
            fileInput2.focus();
        });

        chooseCameraBtn.addEventListener('click', function () {
            showStep('camera');
            startCamera();
        });

        chooseBack.addEventListener('click', function () {
            showStep('details');
            changeButton.focus();
        });

        uploadBack.addEventListener('click', function () {
            clearChosen();
            fileInput2.value = '';
            showStep('choose');
            chooseUploadBtn.focus();
        });

        camBack.addEventListener('click', function () {
            showStep('choose');
            chooseCameraBtn.focus();
        });

        /* ----- open, save, close ----- */
        function fillFrom(card) {
            var structure = card.getAttribute('data-structure') || '';

            currentImage = card.getAttribute('data-image') || '';
            releasePending();
            showPreview();
            preview.alt = card.getAttribute('data-title') || 'Current photo';

            /* A structure that no longer exists is still kept so saving does not silently remove it. */
            if (structure !== '') {
                var found = false;
                Array.prototype.forEach.call(structureSelect.options, function (option) {
                    if (option.value === structure) {
                        found = true;
                    }
                });
                if (!found) {
                    var extra = document.createElement('option');
                    extra.value = structure;
                    extra.textContent = structure;
                    extra.setAttribute('data-extra', 'true');
                    structureSelect.appendChild(extra);
                }
            }
            structureSelect.value = structure;
            plantInput.value = card.getAttribute('data-plant') || '';
            favouriteInput.checked = card.getAttribute('data-favourite') === 'true';

            if (dateField) {
                dateField.refreshMax(todayKey);
                dateField.setValue(card.getAttribute('data-date') || todayKey);
            }
        }

        editStart.addEventListener('click', function () {
            if (!openCard || saving) {
                return;
            }
            editingCard = openCard;
            fillFrom(editingCard);
            showError('');
            showUploadError('');
            setSaving(false);
            showStep('details');

            if (window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(el).show();
            }
        });

        panels.details.addEventListener('submit', function (event) {
            event.preventDefault();

            if (!editingCard || saving) {
                return;
            }
            if (dateField && dateField.getValue() === '') {
                showError('Choose the date this photo was taken.');
                return;
            }

            var data = new FormData();
            data.append('id', editingCard.getAttribute('data-id'));
            data.append('structure', structureSelect.value);
            data.append('plant', plantInput.value.trim());
            data.append('favourite', String(favouriteInput.checked));
            var takenAtValue = '';
            if (dateField) {
                takenAtValue = dateField.getValue();
            }
            data.append('takenAt', takenAtValue);
            if (pendingFile) {
                data.append('photo', pendingFile, pendingName);
                data.append('source', pendingSource);
            }
            if (tokenInput) {
                data.append('__RequestVerificationToken', tokenInput.value);
            }

            setSaving(true);
            showError('');

            fetch(editUrl, { method: 'POST', body: data, credentials: 'same-origin' })
                .then(function (response) {
                    return response.json().then(
                        function (body) { return { ok: response.ok, body: body }; },
                        function () { return { ok: response.ok, body: null }; }
                    );
                })
                .then(function (result) {
                    if (result.ok && result.body && result.body.success) {
                        /* The gallery reloads so the card, filters and calendar all show the saved changes. */
                        saveText.textContent = 'Saved';
                        window.location.reload();
                        return;
                    }
                    setSaving(false);
                    if (result.body && result.body.message) {
                        showError(result.body.message);
                    } else {
                        showError('The photo could not be saved. Try again.');
                    }
                })
                .catch(function () {
                    setSaving(false);
                    showError('The photo could not be saved. Try again.');
                });
        });

        el.addEventListener('show.bs.modal', function () {
            /* Bootstrap adds the backdrop straight after this event, so the newest one is raised above the photo viewer on the next tick. */
            window.setTimeout(function () {
                var backdrops = document.querySelectorAll('.modal-backdrop');
                if (backdrops.length > 1) {
                    backdrops[backdrops.length - 1].classList.add('sh-confirm-backdrop');
                }
            }, 0);
        });

        el.addEventListener('hide.bs.modal', function (event) {
            if (saving) {
                event.preventDefault();
            }
        });

        el.addEventListener('hidden.bs.modal', function () {
            stopCamera();
            clearChosen();
            fileInput2.value = '';
            releasePending();
            showPreview();
            showError('');
            showUploadError('');
            showStep('details');

            if (dateField) {
                dateField.close();
            }

            Array.prototype.forEach.call(structureSelect.querySelectorAll('option[data-extra="true"]'), function (option) {
                option.remove();
            });

            /* Closing this pop-up clears Bootstrap's open-modal state, so it is restored while the photo viewer is still showing. */
            if (modalElement.classList.contains('show')) {
                document.body.classList.add('modal-open');
                editStart.focus();
            }
        });

        window.addEventListener('pagehide', stopCamera);
    })();

    refresh();
})();

/*
    Reference List:
    Mozilla Developer, 2026a. Array.prototype.forEach(). [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Array/forEach> [Accessed 22 September 2026]. 
    Mozilla Developer, 2026b. HTML Drag and Drop API. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/HTML_Drag_and_Drop_API> [Accessed 22 September 2026].
    Mozilla Developer, 2026c. HTMLImageElement: complete property. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/HTMLImageElement/complete> [Accessed 22 September 2026].
    Mozilla Developer, 2026d. NodeList. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/NodeList> [Accessed 22 September 2026]. 
    Mozilla Developer, 2026e. URL: createObjectURL() static method. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL_static> [Accessed 22 September 2026].
    Mozilla Developer, 2026f. Window: sessionStorage property. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/API/Window/sessionStorage> [Accessed 22 September 2026].
*/