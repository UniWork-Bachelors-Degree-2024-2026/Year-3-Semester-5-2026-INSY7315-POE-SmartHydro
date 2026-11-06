/* Says a short message to screen reader users through the hidden live region in the layout. */
window.shAnnounce = function (message) {
    var region = document.getElementById('sh-announcer');

    if (!region || !message) {
        return;
    }

    region.textContent = '';
    window.setTimeout(function () {
        region.textContent = message;
    }, 50);
};

/* Hides decorative icon glyphs from screen readers so their ligature names (such as "chevron_right") are not read out.
   An icon that is the only content of an unlabelled button or link is left alone so that control keeps a name. */
function hideDecorativeIcons(root) {
    if (!root || !root.querySelectorAll) {
        return;
    }

    var icons = root.querySelectorAll('.material-symbols-outlined:not([aria-hidden]), i[class*="fa-"]:not([aria-hidden])');

    icons.forEach(function (icon) {
        var parent = icon.parentElement;

        if (parent && (parent.tagName === 'BUTTON' || parent.tagName === 'A')
            && !parent.hasAttribute('aria-label') && !parent.hasAttribute('aria-labelledby')
            && parent.textContent.trim() === icon.textContent.trim()) {
            return;
        }

        icon.setAttribute('aria-hidden', 'true');
    });
}

document.addEventListener('DOMContentLoaded', function () {
    hideDecorativeIcons(document.body);

    if (window.MutationObserver) {
        new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) {
                        hideDecorativeIcons(node.parentElement || node);
                    }
                });
            });
        }).observe(document.body, { childList: true, subtree: true });
    }
});

document.addEventListener('DOMContentLoaded', function () {
    var menuToggle = document.getElementById('sh-menu-toggle');
    var backdrop = document.getElementById('sh-backdrop');
    var body = document.body;

    var sidebar = document.getElementById('sh-sidebar');

    /* Closes the phone menu and marks the menu button as collapsed */
    function closeSidebar() {
        body.classList.remove('sh-sidebar-open');

        if (menuToggle) {
            menuToggle.setAttribute('aria-expanded', 'false');
            menuToggle.setAttribute('aria-label', 'Open menu');
        }
    }

    /* Opens the phone menu and moves keyboard focus to its first link so Tab carries on inside the menu */
    function openSidebar() {
        body.classList.add('sh-sidebar-open');
        menuToggle.setAttribute('aria-expanded', 'true');
        menuToggle.setAttribute('aria-label', 'Close menu');

        if (sidebar) {
            var firstItem = sidebar.querySelector('a[href], button:not([disabled]), [tabindex="0"]');

            if (firstItem) {
                firstItem.focus();
            }
        }
    }

    if (menuToggle) {
        menuToggle.addEventListener('click', function () {
            if (body.classList.contains('sh-sidebar-open')) {
                closeSidebar();
            } else {
                openSidebar();
            }
        });
    }

    if (backdrop) {
        backdrop.addEventListener('click', closeSidebar);
    }

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape' || !body.classList.contains('sh-sidebar-open')) {
            return;
        }

        closeSidebar();

        if (menuToggle) {
            menuToggle.focus();
        }
    });

    var readingsLocked = document.getElementById('sh-readings-locked');
    var readingsModal = document.getElementById('sh-readings-locked-modal');
    var structureList = document.querySelector('.sh-structure-list');

    /* Explains why Readings is greyed out when it is clicked or tapped, since touch screens cannot show the hover hint. */
    function showReadingsLocked() {
        closeSidebar();
        if (window.bootstrap && readingsModal) {
            bootstrap.Modal.getOrCreateInstance(readingsModal).show();
        }
    }

    if (readingsModal && structureList) {
        readingsModal.addEventListener('hidden.bs.modal', focusFirstStructure);
    }

    if (structureList && new URLSearchParams(window.location.search).get('choose') === 'structure') {
        if (window.history.replaceState) {
            window.history.replaceState(null, '', window.location.pathname);
        }
        focusFirstStructure();
    }

    if (readingsLocked) {
        readingsLocked.addEventListener('click', showReadingsLocked);
        readingsLocked.addEventListener('keydown', function (event) {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                showReadingsLocked();
            }
        });
    }

    var controlToggles = document.querySelectorAll('.sh-control-input');

    controlToggles.forEach(function (toggle) {
        if (toggle.matches('[data-stop-dose]')) {
            toggle.addEventListener('click', function () { toggle.checked = false; sendControl(toggle); });
            return;
        }
        toggle.addEventListener('change', function () {
            sendControl(toggle);
        });
    });

    document.querySelectorAll('.sh-dose-btn').forEach(function (button) {
        button.addEventListener('click', function () {
            sendDose(button);
        });
    });

    var doseHost = document.querySelector('[data-dose-url]');
    var firstDoseButton = document.querySelector('.sh-dose-btn');

    if (doseHost && firstDoseButton && doseHost.getAttribute('data-dose-pending') === 'true') {
        watchDoseStatus(doseHost, firstDoseButton.getAttribute('data-dose-type'), 0);
    }

    initActuatorSignalR();
});

/* Shows the pending and last dose under a pH or nutrient page's dose buttons. */
function showDoseStatus(host, data) {
    host.dosePending = !!data.hasPending;
    host.doseOutcomeUnknown = data.deliveryStatus === 'UNCONFIRMED' || data.deliveryStatus === 'UNKNOWN';
    if (data.doseId) host.doseId = data.doseId;
    var pending = host.querySelector('.sh-dose-pending');
    var last = host.querySelector('.sh-dose-last');

    if (pending) {
        pending.textContent = data.pending;
    }

    if (last && data.last != null) {
        last.textContent = data.last;
    }
}

/* Shows a dose message in the full-width banner between the last sync line and the controller connection, then fades it out after a few seconds. */
function showDoseMessage(host, text, isError) {
    var banner = document.getElementById('sh-dose-banner');

    if (!banner) {
        return;
    }

    var close = banner.querySelector('.sh-dose-banner-close');
    window.clearTimeout(banner.hideTimer);
    window.clearTimeout(banner.removeTimer);

    function hideBanner() {
        banner.classList.add('fading');
        banner.removeTimer = window.setTimeout(function () {
            banner.classList.remove('show', 'fading');
        }, 400);
    }

    if (!banner.bound) {
        banner.bound = true;
        close.addEventListener('click', function () {
            window.clearTimeout(banner.hideTimer);
            window.clearTimeout(banner.removeTimer);
            banner.classList.remove('show', 'fading');
        });
    }

    if (!text) {
        banner.classList.remove('show', 'fading');
        return;
    }

    banner.querySelector('.sh-dose-banner-text').textContent = text;
    banner.querySelector('.sh-dose-banner-icon').textContent = isError ? 'error' : 'check_circle';
    banner.classList.toggle('error', !!isError);
    banner.classList.remove('fading');
    banner.classList.add('show');
    banner.hideTimer = window.setTimeout(hideBanner, isError ? 7000 : 5000);
}

function setDoseBusy(host, busy, blocked) {
    host.querySelectorAll('.sh-dose-btn').forEach(function (button) { button.disabled = busy || !!blocked;
        button.classList.toggle('is-pending', busy); button.setAttribute('aria-busy', String(busy)); });
}

function doseStatusMessage(data) {
    var labels = {
        QUEUED: 'Dose queued for the controller.', SENT: 'Sent. Waiting for the controller.',
        RUNNING: 'Pump running.', AWAITING_COMPLETION: 'Started. Waiting for the cycle to finish.',
        COMPLETED: 'Pump cycle completed. The volume is an estimate.', REJECTED: 'The controller refused this dose.',
        CANCELLED: 'Dose cancelled.', FAILED: 'The dose did not complete.',
        TIMED_OUT: '',
        UNCONFIRMED: '',
        UNKNOWN: 'Waiting for dose status.'
    };
    var message = labels[data.deliveryStatus] || '';
    var reasons = { sensor_fault: 'A sensor reading is unavailable or outside the accepted range.',
        command_expired: 'The request arrived too late.', clock_unavailable: 'The controller is waiting for its clock to synchronise.',
        sensor_stale: 'The sensor readings are too old. Waiting for fresh readings.',
        sensor_recovering: 'The sensors are recovering. Waiting for stable readings before dosing.',
        ph_override_expired: 'The controller refused the override under its pH reading policy. Check that it has the updated tower firmware.',
        invalid_override: 'The tower could not verify this manual override. Check that the API and tower device keys match.',
        mixing_lockout: 'The waiting period after the previous dose has not finished. This does not confirm water circulation.', ph_hourly_cap: 'The hourly pH dose limit has been reached.',
        pump_busy: 'Another dosing pump is already running.', pulse_limit: 'The requested pulse exceeds the pump limit.',
        safety_storage: 'The controller could not save safety information or a command receipt. Check the controller.', cutoff_timer: 'The pump cutoff timer could not be armed.',
        dose_count_limit: 'The controller dose-count limit has been reached.', invalid_dose: 'The dose request is not valid.' };
    if (reasons[data.reason]) message += ' ' + reasons[data.reason];
    if (data.reason === 'safety_blocked') message += ' Check the sensor readings and dosing cooldown.';
    return message;
}

/* Retry status checks after a dropped connection; never resend the dose itself. */
function watchDoseStatus(host, type, tries, immediate) {
    if (host.doseWatching) return;
    if (!host.doseWatchStarted) host.doseWatchStarted = Date.now();
    if (tries >= 90 || Date.now() - host.doseWatchStarted >= 180000) {
        host.doseOutcomeUnknown = true;
        showDoseMessage(host, 'The earlier dose is still unconfirmed. Refresh its status or review it after checking the equipment.', true);
        setDoseBusy(host, false, true);
        return;
    }
    host.doseWatching = true;
    setDoseBusy(host, !host.doseOutcomeUnknown, host.doseOutcomeUnknown);
    var version = host.doseWatchVersion || 0;
    host.doseTimer = window.setTimeout(function () {
        if (version !== (host.doseWatchVersion || 0)) return;
        var url = host.getAttribute('data-dose-status-url')
            + '?structureId=' + encodeURIComponent(host.getAttribute('data-structure-id'))
            + '&dosingType=' + encodeURIComponent(type);
        if (host.doseId) url += '&doseId=' + encodeURIComponent(host.doseId);
        else if (host.doseRecoveryAfter) url += '&requestedAfter=' + encodeURIComponent(host.doseRecoveryAfter);
        var controller = new AbortController();
        var timeout = window.setTimeout(function () { controller.abort(); }, 10000);
        function retry() {
            if (version !== (host.doseWatchVersion || 0)) return;
            host.doseWatching = false;
            showDoseMessage(host, 'Connection interrupted. Checking dose status again...', true);
            watchDoseStatus(host, type, tries + 1);
        }
        fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin', signal: controller.signal
        })
            .then(function (response) {
                if (response.redirected) { window.location.href = response.url; return null; }
                if (!response.ok) return null;
                return response.json().catch(function () { return null; });
            })
            .then(function (data) {
                if (version !== (host.doseWatchVersion || 0)) return;
                if (!data || !data.ok) { retry(); return; }
                host.doseWatching = false;
                showDoseStatus(host, data);
                // An unresolved outcome is a safety block, not an executing request.
                setDoseBusy(host, data.hasPending && !host.doseOutcomeUnknown, host.doseOutcomeUnknown);
                var error = ['REJECTED', 'FAILED', 'TIMED_OUT', 'UNCONFIRMED', 'CANCELLED'].indexOf(data.deliveryStatus) >= 0;
                var message = doseStatusMessage(data);
                if (message) showDoseMessage(host, message, error);
                if (data.hasPending || host.doseOutcomeUnknown) watchDoseStatus(host, type, tries + 1);
                else setDoseBusy(host, !!host.doseOutcomeUnknown);
            })
            .catch(retry)
            .finally(function () { window.clearTimeout(timeout); });
    }, immediate ? 0 : 2000);
}
/* Sends a pH or nutrient dose to the site, which queues it with the API, then shows the updated pending and last dose. */
function sendDose(button) {
    var host = button.closest('[data-dose-url]');
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var type = button.getAttribute('data-dose-type');

    if (!host || !tokenInput || !type) {
        return;
    }

    if (host.doseSubmitting || host.dosePending || host.doseOutcomeUnknown) return;
    var manualPhOverride = host.getAttribute('data-ph-fault') === 'true';
    if (manualPhOverride) {
        if (!window.confirm('The pH sensor is unavailable, so the current pH is unknown. Confirm one ' +
            (button.getAttribute('aria-label') || type) + ' manual dose? Automatic pH and EC dosing remain paused until valid readings recover.')) return;
    }
    host.doseSubmitting = true;
    window.clearTimeout(host.doseTimer);
    host.doseWatchVersion = (host.doseWatchVersion || 0) + 1;
    host.doseWatchStarted = Date.now();
    host.doseWatching = false;
    host.doseId = null;
    host.doseRecoveryAfter = new Date(Date.now() + (window.shServerClockOffset || 0)).toISOString();
    var buttons = host.querySelectorAll('.sh-dose-btn');
    setDoseBusy(host, true);

    showDoseMessage(host, 'Sending dose...', false);

    var body = new URLSearchParams();
    body.set('structureId', host.getAttribute('data-structure-id'));
    body.set('dosingType', type);
    if (manualPhOverride) body.set('manualPhOverride', 'true');

    var submission = new AbortController();
    var submissionTimeout = window.setTimeout(function () { submission.abort(); }, 15000);
    fetch(host.getAttribute('data-dose-url'), {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': tokenInput.value,
            'Content-Type': 'application/x-www-form-urlencoded'
        },
        body: body.toString(),
        credentials: 'same-origin', signal: submission.signal
    })
        .then(function (response) {
            if (response.redirected) {
                window.location.href = response.url;
                return null;
            }

            return response.json();
        })
        .then(function (data) {
            if (!data) {
                return;
            }

            if (data.ok) {
                showDoseStatus(host, data);
                showDoseMessage(host, doseStatusMessage(data) || 'Dose queued.', false);

                if (data.hasPending) {
                    watchDoseStatus(host, type, 0);
                }

                return;
            }

            if (data.outcomeUnknown) {
                host.dosePending = true;
                host.doseOutcomeUnknown = true;
                showDoseMessage(host, 'The request outcome is unknown. Checking its status; do not submit it again.', true);
                watchDoseStatus(host, type, 0);
                return;
            }
            showDoseMessage(host, data.message || 'The dose could not be sent. Please try again.', true);
            setDoseBusy(host, false, host.doseOutcomeUnknown);
        })
        .catch(function () {
            host.dosePending = true;
            host.doseOutcomeUnknown = true;
            showDoseMessage(host, 'The reply was interrupted. Checking whether the dose was saved; do not submit it again.', true);
            watchDoseStatus(host, type, 0);
        })
        .finally(function () {
            host.doseSubmitting = false;
            window.clearTimeout(submissionTimeout);
            window.setTimeout(function () {
                if (host.doseSubmitting || host.dosePending || host.doseOutcomeUnknown) return;
                setDoseBusy(host, false);
            }, 1200);
        });
}

/* Shows a control's on or off state on its dashboard card or sensor page control card. */
function showControlState(toggle, isOn) {
    toggle.checked = isOn;
    if (toggle.dataset && toggle.dataset.stopDose !== undefined) toggle.setAttribute('data-reported-on', String(isOn));
    var controlCard = toggle.closest('.sh-control-card');
    var reported = controlCard && controlCard.querySelector('[data-pump-state]');
    if (reported) reported.textContent = isOn ? 'Reported running' : 'Reported stopped';

    var card = toggle.closest('.sh-dashboard-card');
    if (card) {
        var stateLabel = card.querySelector('.sh-control-state');
        if (stateLabel) {
            stateLabel.textContent = isOn ? 'ON' : 'OFF';
            stateLabel.classList.toggle('on', isOn);
            stateLabel.classList.toggle('off', !isOn);
        }
    }

    var dot = toggle.closest('.sh-control-card') && toggle.closest('.sh-control-card').querySelector('.sh-control-dot');
    if (dot) {
        dot.classList.toggle('on', isOn);
        dot.classList.toggle('off', !isOn);
    }
}

/* Writes a short message under a control, such as when it was last changed or why a change failed. */
function showControlCaption(toggle, text) {
    var caption = null;
    var card = toggle.closest('.sh-dashboard-card');

    if (card) {
        caption = card.querySelector('.sh-detail-control-caption');
    } else if (toggle.closest('.sh-control-card')) {
        caption = toggle.closest('.sh-control-card').querySelector('.sh-control-card-caption span:nth-child(2)');
    }

    if (caption) {
        caption.textContent = text;
    }
}

var pendingControls = {};
var receivedCommandAcks = {};
var receivedCommandAckOrder = [];
var controlAttemptNumber = 0;
var latestRelayState = null;

function reportedControlValue(state, key) {
    if (!state) return undefined;
    function value(name) { return state[name] !== undefined ? state[name] : state[name.charAt(0).toUpperCase() + name.slice(1)]; }
    if (key === 'ph') return typeof value('phUpPump') === 'boolean' && typeof value('phDownPump') === 'boolean' ? value('phUpPump') || value('phDownPump') : undefined;
    if (key === 'ec') return typeof value('nutrientUpPump') === 'boolean' && typeof value('dilutionDownPump') === 'boolean' ? value('nutrientUpPump') || value('dilutionDownPump') : undefined;
    return value(key);
}

function relayReportWarning(state) {
    if (!state) return ' Waiting for a controller report.';
    var at = Date.parse(state.updatedAt || state.UpdatedAt);
    if (!Number.isFinite(at)) return ' Update time is unknown. Check the equipment.';
    return Date.now() + (window.shServerClockOffset || 0) - at > 120000
        ? ' Waiting for a fresh update. Please check the equipment if needed.' : '';
}

function rememberRelayReport(state) {
    if (!state) return false;
    var mac = state.deviceMac || state.DeviceMac;
    if (mac && !matchesCurrentDeviceMac(mac)) return false;
    var sequence = state.reportedSequence !== undefined ? state.reportedSequence : state.ReportedSequence;
    var previous = latestRelayState && (latestRelayState.reportedSequence !== undefined ? latestRelayState.reportedSequence : latestRelayState.ReportedSequence);
    if (Number.isSafeInteger(previous) && (!Number.isSafeInteger(sequence) || sequence < previous)) return false;
    if (!Number.isSafeInteger(previous) && latestRelayState && Date.parse(state.updatedAt || state.UpdatedAt) < Date.parse(latestRelayState.updatedAt || latestRelayState.UpdatedAt)) return false;
    latestRelayState = state;
    Object.keys(pendingControls).forEach(function (key) {
        var value = reportedControlValue(state, key);
        if (typeof value === 'boolean') pendingControls[key].confirmed = value;
    });
    return true;
}

function matchesPendingCommand(pending, ack) {
    return ack && pending.commandId === ack.commandId && (!pending.requestId || pending.requestId === ack.requestId)
        && (ack.clientAttempt === undefined || ack.clientAttempt >= pending.attempt);
}

function controlRefusalReason(reason) {
    var messages = { command_expired: 'The request arrived too late.',
        clock_unavailable: 'The controller is waiting for its clock to synchronise.',
        controller_busy: 'The controller is busy. No change was started.',
        invalid_command: 'The request was not recognised.', invalid_mode: 'The requested mode is not recognised.',
        mode_not_saved: 'The selected mode could not be saved.', mode_start_failed: 'The hotspot could not start.',
        safety_blocked: 'A safety check prevented the change.', sensor_fault: 'A required sensor reading is unavailable.' };
    return messages[reason] || 'Check the controller for details.';
}

/* Sends a toggle change to the site, which passes it to the API. Every toggle for the same control on the page follows the result, and a refused change puts the toggle back. */
function sendControl(toggle) {
    var host = toggle.closest('[data-structure-id]');
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var key = toggle.getAttribute('data-control-key');
    var wanted = toggle.checked;

    if (!host || !tokenInput || !key) {
        toggle.checked = !wanted;
        return;
    }

    if (pendingControls[key]) {
        toggle.checked = !wanted;
        return;
    }
    var previous = toggle.dataset && toggle.dataset.stopDose !== undefined ? toggle.getAttribute('data-reported-on') === 'true' : !wanted;
    var pending = { commandId: null, requestId: null, attempt: ++controlAttemptNumber, awaiting: false, confirmed: previous, wanted: wanted, requestedAt: Date.now() };
    var lastReport = reportedControlValue(latestRelayState, key);
    if (typeof lastReport === 'boolean') pending.confirmed = lastReport;
    pendingControls[key] = pending;

    var group = Array.prototype.filter.call(document.querySelectorAll('.sh-control-input'), function (item) {
        return item.getAttribute('data-control-key') === key;
    });
    var unlocked = group.filter(function (item) {
        return !item.disabled;
    });

    unlocked.forEach(function (item) {
        item.disabled = true;
        item.setAttribute("aria-busy", "true");
        if (item.closest(".sh-toggle")) item.closest(".sh-toggle").classList.add("is-pending");
    });

    function release() {
        if (pendingControls[key] === pending) delete pendingControls[key];
        unlocked.forEach(function (item) { item.disabled = false; item.removeAttribute("aria-busy");
            if (item.closest(".sh-toggle")) item.closest(".sh-toggle").classList.remove("is-pending"); });
    }
    pending.finish = function (ack) {
        if (pendingControls[key] !== pending) return;
        if (ack && !matchesPendingCommand(pending, ack)) return;
        window.clearTimeout(pending.timer);
        window.clearTimeout(pending.pollTimer);
        if (ack && ack.state) rememberRelayReport(ack.state);
        var state = latestRelayState;
        var confirmed = pending.confirmed;
        if (state) {
            var value = reportedControlValue(state, key);
            if (typeof value === 'boolean') confirmed = value;
        }
        var message = !ack ? 'No confirmation received. Check the equipment. Last known state is shown.' :
            ack.status === 'EXPIRED' ? 'Request expired. Last reported state is shown.' :
            ack.status === 'SUPERSEDED' ? 'Replaced by a newer request. Last reported state is shown.' :
            ack.phase === 'cancelled' ? 'The controller cancelled the pump cycle.' :
            ack.ok ? 'Confirmed by controller. Showing its reported state.' : 'The controller refused the change. ' + controlRefusalReason(ack.reason);
        group.forEach(function (item) {
            showControlState(item, confirmed);
            showControlCaption(item, message + relayReportWarning(state));
        });
        window.shAnnounce(message);
        release();
    };
    group.forEach(function (item) {
        showControlState(item, pending.confirmed);
        showControlCaption(item, 'Waiting for controller confirmation...');
    });
    var body = new URLSearchParams();
    body.set('structureId', host.getAttribute('data-structure-id'));
    body.set('control', key);
    body.set('on', wanted ? 'true' : 'false');

    var submission = new AbortController();
    var submissionTimeout = window.setTimeout(function () { submission.abort(); }, 15000);
    fetch(host.getAttribute('data-control-url'), {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': tokenInput.value,
            'Content-Type': 'application/x-www-form-urlencoded'
        },
        body: body.toString(),
        credentials: 'same-origin', signal: submission.signal
    })
        .then(function (response) {
            if (response.redirected) {
                window.location.href = response.url;
                return null;
            }

            return response.json().catch(function () {
                return { ok: false, message: 'The change could not be saved. Please try again.' };
            });
        })
        .then(function (data) {
            if (!data) {
                return;
            }

            if (data.ok) {
                if (data.pending && data.commandId) {
                    pending.awaiting = true;
                    pending.commandId = data.commandId;
                    pending.requestId = data.requestId || null;
                    if (!latestRelayState) pending.confirmed = data.on;
                    group.forEach(function (item) { showControlState(item, pending.confirmed); });
                    pending.timer = window.setTimeout(function () { pending.finish(null); }, 65000);
                    var earlyAck = receivedCommandAcks[data.requestId || data.commandId];
                    if (matchesPendingCommand(pending, earlyAck)) pending.finish(earlyAck);
                    pollControlStatus();
                    return;
                }
                group.forEach(function (item) {
                    showControlState(item, pending.confirmed);
                    showControlCaption(item, 'Request received; controller confirmation is unavailable. Last reported state is shown.');
                });
                return;
            }

            group.forEach(function (item) {
                showControlState(item, pending.confirmed);
                showControlCaption(item, data.message || 'The change could not be saved. Please try again.');
            });
            window.shAnnounce(data.message || 'The change could not be saved. Please try again.');
        })
        .catch(function () {
            group.forEach(function (item) {
                showControlState(item, pending.confirmed);
                showControlCaption(item, 'The reply was interrupted. The request may have been saved; check the controller before sending it again.');
            });
            window.shAnnounce('The request outcome is unknown. Check the controller before sending it again.');
        })
        .finally(function () {
            window.clearTimeout(submissionTimeout);
            if (!pending.awaiting) release();
        });

    function pollControlStatus() {
        if (pendingControls[key] !== pending) return;
        pending.pollTimer = window.setTimeout(async function () {
            if (pendingControls[key] !== pending) return;
            var controller = new AbortController();
            var timeout = window.setTimeout(function () { controller.abort(); }, 8000);
            try {
                var url = '/Controls/CommandStatus?structureId=' + encodeURIComponent(host.getAttribute('data-structure-id'))
                    + '&commandId=' + encodeURIComponent(pending.commandId);
                if (pending.requestId) url += '&requestId=' + encodeURIComponent(pending.requestId);
                var response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
                if (response.ok && !response.redirected) {
                    var result = await response.json();
                    if (matchesPendingCommand(pending, result) && result.terminal) pending.finish(result);
                }
            } catch (_) {
                // A status check can be retried safely; the command itself is never resent here.
            } finally {
                window.clearTimeout(timeout);
                if (pendingControls[key] === pending) pollControlStatus();
            }
        }, 2000);
    }
}

function focusFirstStructure() {
    var target = document.querySelector('.sh-structure-card .sh-details-link');
    var card = null;
    if (target) {
        card = target.closest('.sh-structure-card');
    } else {
        target = document.querySelector('.sh-structure-add');
        card = target;
    }

    if (!target || !card) {
        return;
    }

    card.scrollIntoView({ block: 'center', behavior: 'smooth' });
    target.focus({ preventScroll: true });
    card.classList.add('is-spotlight');
    window.setTimeout(function () {
        card.classList.remove('is-spotlight');
    }, 1800);
}

/* Message banners (.sh-flash): every green confirmation and red error gets a close X.
   Confirmations also fade out after a few seconds; errors stay until closed.
   Hovering or focusing a confirmation pauses its timer so it can still be read or its Undo button used. */
document.addEventListener('DOMContentLoaded', function () {
    var flashes = document.querySelectorAll('.sh-flash[role="status"]:not(.sh-flash-muted), .sh-flash[role="alert"]:not(.sh-flash-muted)');
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    flashes.forEach(function (flash) {
        var autoFade = flash.getAttribute('role') === 'status';
        var hasAction = flash.querySelector('form, button, a') !== null;
        var delay = hasAction ? 10000 : 6000;
        var timer = null;
        var closing = false;

        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'sh-flash-close';
        close.setAttribute('aria-label', 'Dismiss message');
        close.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">close</span>';
        flash.appendChild(close);

        function dismiss() {
            if (closing) {
                return;
            }

            closing = true;
            window.clearTimeout(timer);

            if (reduceMotion) {
                flash.remove();
                return;
            }

            /* Fade first, then fold the space it took up so the page below slides up smoothly. */
            flash.style.height = flash.offsetHeight + 'px';
            flash.classList.add('is-leaving');

            window.setTimeout(function () {
                flash.classList.add('is-collapsing');
                flash.style.height = '0px';
            }, 250);

            window.setTimeout(function () {
                flash.remove();
            }, 550);
        }

        function start() {
            if (!autoFade) {
                return;
            }

            window.clearTimeout(timer);
            timer = window.setTimeout(dismiss, delay);
        }

        function pause() {
            window.clearTimeout(timer);
        }

        close.addEventListener('click', dismiss);
        flash.addEventListener('mouseenter', pause);
        flash.addEventListener('mouseleave', start);
        flash.addEventListener('focusin', pause);
        flash.addEventListener('focusout', function (event) {
            if (!flash.contains(event.relatedTarget)) {
                start();
            }
        });

        start();
    });
});
/* The "no access" message arrives as ?denied=1 on the redirect. The flag is dropped from the address once the page has loaded, so a refresh does not show the message again. */
(function () {
    var url = new URL(window.location.href);

    if (url.searchParams.get("denied") === "1") {
        url.searchParams.delete("denied");
        window.history.replaceState(null, "", url.pathname + url.search + url.hash);
    }
})();

// -----------------------------------------------------------------------------
// Live controller reports update the page without reloading; end-to-end latency needs measurement.
// -----------------------------------------------------------------------------
var controlKeyAliases = {
    mainWaterPump: ['mainWaterPump', 'pump'],
    growLight: ['growLight', 'light'],
    circulationFan: ['circulationFan', 'fan'],
    extractorFan: ['extractorFan', 'extract'],
    spraySystem: ['spraySystem', 'spray'],
    phUpPump: ['phUpPump', 'phUp'],
    phDownPump: ['phDownPump', 'phDown'],
    nutrientUpPump: ['nutrientUpPump', 'ecUp'],
    dilutionDownPump: ['dilutionDownPump', 'ecDown']
};

function normalizeMacJs(mac) {
    if (!mac) return '';
    return String(mac).toUpperCase().replace(/[^0-9A-F]/g, '');
}

function matchesCurrentDeviceMac(incomingMac) {
    var hostLayout = document.querySelector('.sh-dashboard-layout[data-device-mac], .sh-detail-layout[data-device-mac]');
    if (!hostLayout) return true;
    var pageMac = normalizeMacJs(hostLayout.getAttribute('data-device-mac'));
    var eventMac = normalizeMacJs(incomingMac);
    if (!pageMac || !eventMac) return true;
    return pageMac === eventMac;
}

function phReadingAgeLabel(previous, ageSeconds) {
    if (!previous) return 'No previous reading available';
    var minutes = Math.floor(Math.max(0, ageSeconds) / 60);
    return minutes === 0 ? 'Last reading, less than a minute ago' :
        'Last reading, ' + minutes + (minutes === 1 ? ' minute ago' : ' minutes ago');
}

function refreshPhReadingAges() {
    document.querySelectorAll('[data-ph-reading-metadata="true"]').forEach(function (notice) {
        var stamp = notice.getAttribute('data-ph-recorded-at');
        var age = Number(notice.getAttribute('data-ph-age-seconds'));
        if (!stamp || !Number.isFinite(age) || !Number.isFinite(Date.parse(stamp))) return;
        if (notice.phAgeStarted === undefined) notice.phAgeStarted = performance.now();
        var label = phReadingAgeLabel(true, age + (performance.now() - notice.phAgeStarted) / 1000);
        notice.setAttribute('title', label);
        notice.setAttribute('aria-description', label);
    });
}

function updatePhReadingDom(value, fault, previous) {
    var validPrevious = previous && typeof previous.value === 'number' && Number.isFinite(previous.value) &&
        previous.value >= 0 && previous.value <= 14 && Number.isFinite(Date.parse(previous.recordedAt)) &&
        typeof previous.ageSeconds === 'number' && Number.isFinite(previous.ageSeconds) && previous.ageSeconds >= 0;
    var doseHost = document.querySelector('[data-dose-url]');
    if (doseHost) {
        doseHost.setAttribute('data-ph-fault', String(!!fault && doseHost.getAttribute('data-ph-override-allowed') !== 'false'));
    }
    if (fault) {
        // A retained value must never be appended to the live trend or used as the current measurement.
        updateCardMetricDom('CleanWater', validPrevious ? previous.value.toFixed(1) : 'Waiting for a valid reading',
            validPrevious ? 'pH' : '', !validPrevious, null);
    } else if (typeof value === 'number' && Number.isFinite(value)) {
        updateCardMetricDom('CleanWater', value.toFixed(1), 'pH', false, Number(value.toFixed(1)));
    } else return;
    document.querySelectorAll('[data-ph-reading-metadata="true"]').forEach(function (notice) {
        notice.setAttribute('data-ph-recorded-at', fault && validPrevious ? previous.recordedAt : '');
        notice.setAttribute('data-ph-age-seconds', fault && validPrevious ? String(previous.ageSeconds) : '');
        notice.phAgeStarted = performance.now();
        var label = fault ? phReadingAgeLabel(validPrevious, validPrevious ? previous.ageSeconds : 0) : '';
        notice.setAttribute('title', label);
        notice.setAttribute('aria-description', label);
    });
}

document.addEventListener('DOMContentLoaded', function () {
    refreshPhReadingAges();
    window.setInterval(refreshPhReadingAges, 60000);
});

function updateCardMetricDom(actionName, valueText, unitText, isTextValue, rawNumericValue) {
    var cards = document.querySelectorAll('.sh-dashboard-card[data-card-action="' + actionName + '"]');
    cards.forEach(function (card) {
        var valEl = card.querySelector('.sh-dashboard-value');
        if (!valEl) return;
        valEl.classList.remove('sh-stat-value-empty');
        valEl.classList.toggle('sh-dashboard-value-text', !!isTextValue);
        if (unitText) {
            valEl.innerHTML = '';
            valEl.appendChild(document.createTextNode(valueText + ' '));
            var unitSpan = document.createElement('span');
            unitSpan.className = 'sh-dashboard-unit';
            unitSpan.textContent = unitText;
            valEl.appendChild(unitSpan);
        } else {
            valEl.textContent = valueText;
        }
    });

    var detailLayout = document.querySelector('.sh-detail-layout[data-sensor-action="' + actionName + '"]');
    if (detailLayout) {
        var heroVal = detailLayout.querySelector('.sh-hero-value');
        if (heroVal && (!isTextValue || valueText === 'Sensor Fault' || valueText === 'Waiting for a valid reading')) {
            heroVal.classList.remove('sh-hero-empty');
            heroVal.innerHTML = '';
            heroVal.appendChild(document.createTextNode(valueText));
            if (unitText) {
                heroVal.appendChild(document.createTextNode(' '));
                var heroUnit = document.createElement('span');
                heroUnit.className = 'sh-hero-unit';
                heroUnit.textContent = unitText;
                heroVal.appendChild(heroUnit);
            }
        }
        if (!isTextValue && typeof rawNumericValue === 'number' && typeof window.updateSensorTrendRealtime === 'function') {
            window.updateSensorTrendRealtime(rawNumericValue);
        }
    }
}

function markStructureOnlineNow() {
    var pill = document.getElementById('sh-live-connection-pill');
    var icon = document.getElementById('sh-live-connection-icon');
    var text = document.getElementById('sh-live-connection-text');
    var hint = document.getElementById('sh-offline-hint');
    var syncLabel = document.getElementById('sh-detail-sync-label');

    if (pill) pill.classList.remove('is-offline');
    if (icon) icon.textContent = 'cloud_done';
    if (text) text.textContent = 'Online System';
    if (hint) hint.style.display = 'none';
    if (syncLabel) syncLabel.textContent = 'Last sync: Just now';
}

function receiveActuatorState(state) {
        if (!state) return;
        var stateMac = state.deviceMac || state.DeviceMac || '';
        if (!matchesCurrentDeviceMac(stateMac)) return;
        if (!rememberRelayReport(state)) return;

        var changedBy = state.changedBy || state.ChangedBy || '';
        var captionText = '';
        var lower = changedBy.toLowerCase();
        if (lower.indexOf('ml') !== -1 || lower.indexOf('machine') !== -1 || lower.indexOf('predict') !== -1) {
            captionText = 'Automatic climate control';
        } else if (lower.indexOf('schedule') !== -1 || lower.indexOf('timer') !== -1) {
            captionText = 'Scheduled control';
        } else if (lower.indexOf('gateway') !== -1 || lower.indexOf('lora') !== -1) {
            captionText = 'Controller report received through LoRa';
        } else if (changedBy && changedBy !== 'System' && changedBy !== 'EdgeFirmware') {
            captionText = lower.indexOf('ble') !== -1 ? 'Local Bluetooth control' :
                lower.indexOf('softap') !== -1 ? 'Local hotspot control' : 'Controller report';
        }

        Object.keys(controlKeyAliases).forEach(function (prop) {
            var val = state[prop];
            if (typeof val === 'undefined') {
                var pascal = prop.charAt(0).toUpperCase() + prop.slice(1);
                val = state[pascal];
            }
            if (typeof val === 'boolean') {
                controlKeyAliases[prop].forEach(function (key) {
                    var toggles = document.querySelectorAll('.sh-control-input[data-control-key="' + key + '"]');
                    toggles.forEach(function (toggle) {
                        showControlState(toggle, val);
                        if (pendingControls[key]) return;
                        var msg = (val ? 'Last reported ON.' : 'Last reported OFF.') + (captionText ? ' ' + captionText + '.' : '');
                        showControlCaption(toggle, msg + relayReportWarning(state));
                    });
                });
            }
        });

        var phUp = state.phUpPump !== undefined ? state.phUpPump : state.PhUpPump;
        var phDown = state.phDownPump !== undefined ? state.phDownPump : state.PhDownPump;
        if (typeof phUp === 'boolean' && typeof phDown === 'boolean') {
            var phOn = !!(phUp || phDown);
            document.querySelectorAll('.sh-control-input[data-control-key="ph"]').forEach(function (toggle) {
                showControlState(toggle, phOn);
                if (!pendingControls.ph) showControlCaption(toggle,
                    (phOn ? 'Last reported ON' : 'Last reported OFF') + relayReportWarning(state));
            });
        }

        var ecUp = state.nutrientUpPump !== undefined ? state.nutrientUpPump : state.NutrientUpPump;
        var ecDown = state.dilutionDownPump !== undefined ? state.dilutionDownPump : state.DilutionDownPump;
        if (typeof ecUp === 'boolean' && typeof ecDown === 'boolean') {
            var ecOn = !!(ecUp || ecDown);
            document.querySelectorAll('.sh-control-input[data-control-key="ec"]').forEach(function (toggle) {
                showControlState(toggle, ecOn);
                if (!pendingControls.ec) showControlCaption(toggle,
                    (ecOn ? 'Last reported ON' : 'Last reported OFF') + relayReportWarning(state));
            });
        }
    }
window.shReceiveRelayState = receiveActuatorState;

function recoverActuatorConnection(connection, register) {
    var retryTimer = null, working = false, failures = 0;
    function scheduleRetry() {
        if (retryTimer !== null) return;
        var delay = Math.min(30000, 1000 * Math.pow(2, Math.min(failures++, 5)));
        retryTimer = window.setTimeout(function () { retryTimer = null; connect(); }, delay);
    }
    async function connect() {
        if (working || (connection.state !== 'Disconnected' && connection.state !== 'Connected')) return;
        working = true;
        try {
            if (connection.state === 'Disconnected') await connection.start();
            await register();
            failures = 0;
        } catch (_) {
            // Recover the subscription only. Never replay a switch or dosing request.
            scheduleRetry();
        } finally { working = false; }
    }
    connection.onreconnected(function () {
        window.clearTimeout(retryTimer); retryTimer = null;
        return connect();
    });
    connection.onclose(scheduleRetry);
    connect();
}

function initActuatorSignalR() {
    if (typeof signalR === 'undefined') {
        window.setTimeout(initActuatorSignalR, 300);
        return;
    }

    var body = document.body;
    var apiBaseUrl = body.getAttribute('data-api-base-url') || '';
    if (!apiBaseUrl && window.SMARTHYDRO_API_BASE_URL) {
        apiBaseUrl = window.SMARTHYDRO_API_BASE_URL;
    }
    var hubEndpoint = apiBaseUrl ? (apiBaseUrl.replace(/\/+$/, '') + '/hubs/actuators') : '/hubs/actuators';

    var actuatorConn = new signalR.HubConnectionBuilder()
        .withUrl(hubEndpoint, { accessTokenFactory: async function () {
            var response = await fetch('/Realtime/Token', { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) throw new Error('Please sign in again.');
            return (await response.json()).token;
        } })
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .configureLogging(signalR.LogLevel.None)
        .build();

    actuatorConn.on('ReceiveModeState', function (state) {
        if (window.shReceiveModeState) window.shReceiveModeState(state);
    });
    actuatorConn.on('ReceiveCommandAck', function (ack) {
        if (!ack || !matchesCurrentDeviceMac(ack.deviceMac)) return;
        // This is the stored controller report, not the requested switch position.
        // Apply it without waiting for the separate state event or polling round.
        if (ack.state) receiveActuatorState(ack.state);
        ack.clientAttempt = controlAttemptNumber;
        var cacheKey = ack.requestId || ack.commandId;
        if (!receivedCommandAcks[cacheKey]) receivedCommandAckOrder.push(cacheKey);
        receivedCommandAcks[cacheKey] = ack;
        if (receivedCommandAckOrder.length > 64) delete receivedCommandAcks[receivedCommandAckOrder.shift()];
        Object.keys(pendingControls).forEach(function (key) {
            var pending = pendingControls[key];
            if (matchesPendingCommand(pending, ack) && pending.finish) pending.finish(ack);
        });
        if (ack.doseId && (ack.phase === 'completed' || ack.phase === 'cancelled' || ack.ok === false)) {
            var doseHost = document.querySelector('[data-dose-url]');
            var doseButton = doseHost && doseHost.querySelector('.sh-dose-btn');
            if (doseButton && doseHost.doseId === ack.doseId) {
                window.clearTimeout(doseHost.doseTimer);
                doseHost.doseWatchVersion = (doseHost.doseWatchVersion || 0) + 1;
                doseHost.doseWatching = false;
                doseHost.doseWatchStarted = Date.now();
                watchDoseStatus(doseHost, doseButton.getAttribute('data-dose-type'), 0, true);
            }
        }
    });
    actuatorConn.on('OnActuatorStateChanged', receiveActuatorState);

    actuatorConn.on('OnTelemetryUpdated', function (telemetry) {
        if (!telemetry) return;
        var telMac = telemetry.deviceMac || telemetry.DeviceMac || '';
        if (!matchesCurrentDeviceMac(telMac)) return;

        markStructureOnlineNow();

        // A matching value does not identify which command ran. Only its CID/request receipt confirms it.

        var ph = telemetry.phLevel !== undefined ? telemetry.phLevel : telemetry.PhLevel;
        var ec = telemetry.ecMs !== undefined ? telemetry.ecMs : telemetry.EcMs;
        var lux = telemetry.lightLux !== undefined ? telemetry.lightLux : telemetry.LightLux;
        var temp = telemetry.temperatureC !== undefined ? telemetry.temperatureC : telemetry.TemperatureC;
        var hum = telemetry.humidityPercent !== undefined ? telemetry.humidityPercent : telemetry.HumidityPercent;
        var waterOk = telemetry.waterLevelDetected !== undefined ? telemetry.waterLevelDetected : telemetry.WaterLevelDetected;

        var faults = telemetry.sensorFaults || [];
        function sensorFault(value, name, minimum) {
            return value === null || faults.indexOf(name) >= 0 ||
                (typeof value === 'number' && (!Number.isFinite(value) || value < minimum));
        }
        [[ec, 'ec', 0, ['Nutrients']],
         [temp, 'temperature', -40, ['Temperature']], [hum, 'humidity', 0, ['Humidity']],
         [lux, 'light', 0, ['Sunlight', 'Light']]].forEach(function (metric) {
            if (sensorFault(metric[0], metric[1], metric[2])) {
                metric[3].forEach(function (action) {
                    updateCardMetricDom(action, 'Sensor Fault', '', true, null);
                });
            }
        });

        if (typeof waterOk === 'boolean') {
            var waterText = waterOk ? 'Level OK' : 'Low';
            updateCardMetricDom('WaterPump', waterText, '', true, null);
            updateCardMetricDom('SpraySystem', waterText, '', true, null);
        }
        if (ph !== undefined || faults.indexOf('ph') >= 0)
            updatePhReadingDom(ph, sensorFault(ph, 'ph', 0) || ph > 14,
                telemetry.phDisplayFallback || telemetry.PhDisplayFallback);
        if (typeof temp === 'number' && !sensorFault(temp, 'temperature', -40)) {
            var tempNum = Number(temp.toFixed(1));
            updateCardMetricDom('Temperature', temp.toFixed(1), '\u00B0C', false, tempNum);
        }
        if (typeof lux === 'number' && !sensorFault(lux, 'light', 0)) {
            var luxRound = Math.round(lux);
            var luxStr = luxRound.toString();
            updateCardMetricDom('Sunlight', luxStr, '%', false, luxRound);
            updateCardMetricDom('Light', luxStr, '%', false, luxRound);
        }
        if (typeof ec === 'number' && !sensorFault(ec, 'ec', 0)) {
            var usCmRound = Math.round(ec * 1000);
            var usCm = usCmRound.toString();
            updateCardMetricDom('Nutrients', usCm, '\u00B5S/cm', false, usCmRound);
        }
        if (typeof hum === 'number' && !sensorFault(hum, 'humidity', 0)) {
            var humRound = Math.round(hum);
            updateCardMetricDom('Humidity', humRound.toString(), '%', false, humRound);
        }
    });

    function registerCurrentDevice() {
        var layout = document.querySelector('.sh-dashboard-layout[data-device-mac], .sh-detail-layout[data-device-mac]');
        var mac = layout ? layout.getAttribute('data-device-mac') : '';
        if (mac) return actuatorConn.invoke('RegisterDevice', mac);
        return Promise.resolve();
    }
    recoverActuatorConnection(actuatorConn, registerCurrentDevice);
}
