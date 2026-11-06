(function () {
    'use strict';

    var root = document.getElementById('sh-live');
    if (!root) {
        return;
    }

    var stage = document.getElementById('sh-live-stage');
    var deviceImg = document.getElementById('sh-device-image');
    var badgeText = document.getElementById('sh-live-badge-text');
    var clock = document.getElementById('sh-live-clock');
    var shutter = document.getElementById('sh-shutter');
    var snapBtn = document.getElementById('sh-snap-now-btn');
    var refreshBtn = document.getElementById('sh-refresh-btn');
    var message = document.getElementById('sh-live-message');
    var messageText = document.getElementById('sh-live-message-text');
    var retry = document.getElementById('sh-live-retry');
    var sensorText = document.getElementById('sh-sensor-text');
    var sensorDot = document.querySelector('.sh-live-sensor-dot');

    var triggerUrl = root.getAttribute('data-trigger-url');
    var latestUrl = root.getAttribute('data-latest-url');
    var targetMac = root.getAttribute('data-target-mac') || '';
    var structureId = root.getAttribute('data-structure-id') || '';
    var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');

    var isCapturing = false;

    function showImage() {
        if (deviceImg) deviceImg.hidden = false;
    }

    function showState(kind, text) {
        stage.setAttribute('data-state', kind);

        if (kind === 'live') {
            badgeText.textContent = 'ONLINE';
            message.hidden = true;
            showImage();
            shutter.disabled = false;
            if (snapBtn) snapBtn.disabled = false;
            return;
        }

        if (kind === 'connecting') {
            badgeText.textContent = 'CAPTURING...';
            message.hidden = false;
            messageText.textContent = text || 'Taking a photo...';
            retry.hidden = true;
            shutter.disabled = true;
            if (snapBtn) snapBtn.disabled = true;
            return;
        }

        badgeText.textContent = 'ERROR';
        message.hidden = false;
        messageText.textContent = text || 'Could not reach the ESP32-CAM.';
        retry.hidden = false;
        shutter.disabled = false;
        if (snapBtn) snapBtn.disabled = false;
    }

    function flash() {
        stage.classList.add('is-flash');
        window.setTimeout(function () {
            stage.classList.remove('is-flash');
        }, 220);
    }

    function triggerCapture() {
        if (isCapturing) return;

        if (!targetMac) {
            showState('error', 'No ESP32-CAM is linked to this structure.');
            return;
        }

        isCapturing = true;

        showState('connecting', 'Waking the ESP32-CAM and taking a photo. This takes a few seconds...');

        var data = new FormData();
        if (tokenInput) {
            data.append('__RequestVerificationToken', tokenInput.value);
        }
        data.append('mac', targetMac);
        if (structureId) {
            data.append('structureId', structureId);
        }
        var flashEl = document.getElementById('sh-flash-toggle');
        if (flashEl && flashEl.checked) {
            data.append('flash', 'true');
        }

        fetch(triggerUrl, { method: 'POST', body: data })
            .then(function (res) {
                if (!res.ok) {
                    throw new Error('Server returned ' + res.status);
                }
                return res.json();
            })
            .then(function (result) {
                if (result && result.success && result.imageUrl) {
                    applyNewPhoto(result);
                    loadHistoryForDate();
                } else {
                    showState('error', (result && result.message) || 'The photo took too long. Check that the ESP32-CAM is on and connected to Wi-Fi.');
                }
            })
            .catch(function () {
                showState('error', 'Could not reach the camera. Check your connection and try again.');
            })
            .finally(function () {
                isCapturing = false;
            });
    }

    var currentDisplayedPhotoId = null;

    function applyNewPhoto(data) {
        if (!deviceImg || !data || !data.imageUrl) return;

        if (data.photoId && currentDisplayedPhotoId === data.photoId) {
            showState('live');
            return;
        }
        currentDisplayedPhotoId = data.photoId || null;

        var url = data.imageUrl;
        var sep = url.indexOf('?') === -1 ? '?' : '&';
        var cacheBustUrl = url + sep + '_t=' + Date.now();

        deviceImg.onload = function () {
            showImage();
            flash();

            if (clock && data.takenAt) {
                clock.textContent = data.takenAt;
            }

            if (data.sensorSummary && sensorText) {
                sensorText.textContent = data.sensorSummary;
            }
            if (sensorDot) {
                sensorDot.classList.toggle('is-online', !!data.sensorsOnline);
            }

            showState('live');
        };

        deviceImg.onerror = function () {
            showState('error', 'The photo was taken but could not be shown.');
        };

        deviceImg.src = cacheBustUrl;
    }

    function refreshLatest() {
        if (!latestUrl || (!targetMac && !structureId)) return;

        var url = latestUrl + '?mac=' + encodeURIComponent(targetMac);
        if (structureId) url += '&structureId=' + encodeURIComponent(structureId);

        fetch(url)
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (result && result.success && result.imageUrl) {
                    applyNewPhoto(result);
                }
            })
            .catch(function () {});
    }

    // Event Listeners
    if (shutter) {
        shutter.addEventListener('click', triggerCapture);
    }

    if (snapBtn) {
        snapBtn.addEventListener('click', triggerCapture);
    }

    if (refreshBtn) {
        refreshBtn.addEventListener('click', function () {
            refreshLatest();
            loadHistoryForDate();
        });
    }

    if (retry) {
        retry.addEventListener('click', triggerCapture);
    }

    // Initialize SignalR Hub Connection for real-time edge event push
    function initSignalR() {
        if (!window.signalR) return;

        try {
            var body = document.body;
            var apiBaseUrl = body.getAttribute('data-api-base-url') || '';
            if (!apiBaseUrl && window.SMARTHYDRO_API_BASE_URL) {
                apiBaseUrl = window.SMARTHYDRO_API_BASE_URL;
            }
            var hubEndpoint = apiBaseUrl ? (apiBaseUrl.replace(/\/+$/, '') + '/hubs/actuators') : '/hubs/actuators';

            var connection = new signalR.HubConnectionBuilder()
                .withUrl(hubEndpoint, { accessTokenFactory: async function () {
                    var response = await fetch('/Realtime/Token', { credentials: 'same-origin', cache: 'no-store' });
                    if (!response.ok) throw new Error('Please sign in again.');
                    return (await response.json()).token;
                } })
                .withAutomaticReconnect([0, 2000, 5000, 10000])
                .configureLogging(signalR.LogLevel.None)
                .build();

            connection.on('PhotoCaptured', function (payload) {
                if (payload) {
                    var capturedMac = (payload.deviceMac || '').toUpperCase().replace(/[:-]/g, '');
                    var currentMac = (targetMac || '').toUpperCase().replace(/[:-]/g, '');
                    if (!currentMac || !capturedMac || capturedMac === currentMac) {
                        if (payload.captureId) {
                            var photoUrl = '/Camera/Photo/' + payload.captureId;
                            applyNewPhoto({
                                success: true,
                                photoId: payload.captureId,
                                imageUrl: photoUrl,
                                takenAt: payload.capturedAt ? new Date(payload.capturedAt).toLocaleString('en-US', {
                                    month: 'short', day: 'numeric', year: 'numeric',
                                    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: true
                                }) : '',
                                sensorsOnline: true
                            });
                        } else {
                            refreshLatest();
                        }
                        loadHistoryForDate();
                    }
                }
            });

            connection.start()
                .then(function () {
                    if (targetMac) {
                        connection.invoke('RegisterDevice', targetMac).catch(function () {});
                    }
                })
                .catch(function (err) {
                    console.warn('[Camera SignalR] Connection failed:', err);
                });
        } catch (e) {
            console.warn('[Camera SignalR] Exception:', e);
        }
    }

    initSignalR();

    // Photos by date
    var byDateUrl = root.getAttribute('data-by-date-url');
    var dateField = document.getElementById('sh-history-date-field');
    var dateInput = document.getElementById('sh-history-date-input');
    var historyGallery = document.getElementById('sh-history-gallery');

    // Turns the hidden input into the same calendar picker the save and upload forms use
    if (dateField && window.ShDateField) {
        window.ShDateField.create(dateField);
    }

    function showHistoryNote(text, isError) {
        var note = document.createElement('p');
        note.className = 'sh-cam-history-note' + (isError ? ' is-error' : '');
        note.textContent = text;
        historyGallery.replaceChildren(note);
    }

    function showOnStage(url, time) {
        if (!deviceImg || !url) return;
        deviceImg.src = url;
        if (clock && time) clock.textContent = time;
        showState('live');
        stage.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function buildThumb(photo) {
        var card = document.createElement('button');
        card.type = 'button';
        card.className = 'sh-cam-thumb';
        card.setAttribute('aria-label', 'Show photo from ' + photo.takenAt);

        var img = document.createElement('img');
        img.src = photo.imageUrl;
        img.alt = '';
        img.loading = 'lazy';

        var time = document.createElement('span');
        time.className = 'sh-cam-thumb-time';
        time.textContent = photo.timeOnly;

        card.appendChild(img);
        card.appendChild(time);
        card.addEventListener('click', function () {
            showOnStage(photo.imageUrl, photo.takenAt);
        });
        return card;
    }

    function loadHistoryForDate() {
        if (!byDateUrl || !dateInput || !historyGallery || (!targetMac && !structureId)) return;
        var selectedDate = dateInput.value;
        if (!selectedDate) return;

        showHistoryNote('Loading photos...');

        var url = byDateUrl + '?mac=' + encodeURIComponent(targetMac) + '&date=' + encodeURIComponent(selectedDate);
        if (structureId) url += '&structureId=' + encodeURIComponent(structureId);

        fetch(url)
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (!result || !result.photos || result.photos.length === 0) {
                    showHistoryNote('No photos on this day.');
                    return;
                }

                historyGallery.replaceChildren.apply(historyGallery, result.photos.map(buildThumb));
            })
            .catch(function () {
                showHistoryNote('Could not load photos. Try again.', true);
            });
    }

    if (dateInput) {
        dateInput.addEventListener('change', loadHistoryForDate);
    }

    loadHistoryForDate();

    // Show the stage as live when the page loaded with a photo already in it
    if (deviceImg && deviceImg.getAttribute('src')) {
        showState('live');
    }
})();
