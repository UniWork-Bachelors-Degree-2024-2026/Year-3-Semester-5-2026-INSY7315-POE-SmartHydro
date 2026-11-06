(function (root) {
    'use strict';
    var names = ['LoRa + BLE', 'Online Wi-Fi', 'SoftAP Setup'];
    function describe(state, now) {
        var active = 'Connection: waiting for the controller to report.';
        if (state.reportedMode === 0) active = 'Connection: LoRa radio (normal)';
        if (state.reportedMode === 1) active = state.reportedModeReason === 1
            ? 'Connection: Wi-Fi (switched automatically while LoRa recovers)'
            : 'Connection: Wi-Fi (chosen by you)';
        if (state.reportedMode === 2) active = 'Connection: setup hotspot (192.168.4.1)';
        var remaining = state.requestedAt ? Math.max(0, Math.ceil((Date.parse(state.requestedAt) + 180000 - now) / 1000)) : 0;
        var waiting = state.status === 'waiting' && remaining > 0;
        var pending = waiting ? 'Waiting for the controller to confirm: ' + names[state.requestedMode] + '. ' + remaining + 's remaining' :
            state.status === 'rejected' ? 'The controller did not accept the mode change.' :
            state.status === 'unconfirmed' || (state.status === 'waiting' && !remaining)
                ? 'Mode change not confirmed. Check the controller. The last mode it reported is shown above.' : '';
        function ago(value) {
            return value ? Math.max(0, Math.floor((now - Date.parse(value)) / 1000)) + 's ago' : 'not seen yet';
        }
        var gateway = 'Gateway last seen: ' + ago(state.lastGatewaySeenAt);
        var node = (state.transport === 'wifi' ? 'Wi-Fi: controller last seen: ' : 'Controller last seen: ') + ago(state.lastNodeSeenAt);
        var fresh = !!state.lastNodeSeenAt && now - Date.parse(state.lastNodeSeenAt) <= 120000;
        return { active: active, pending: pending, waiting: waiting, remaining: remaining, link: node + ' \u00b7 ' + gateway, fresh: fresh };
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { describe: describe };
    if (!root.document) return;
    var latest, host, refreshing = false, requesting = false, requestNotice = '', requestBaseline, requestNoticeUntil = 0;
    async function fetchStatus(url, options, timeoutMs, readJson) {
        if (!root.AbortController) return fetch(url, options);
        var controller = new root.AbortController();
        var timer = root.setTimeout(function () { controller.abort(); }, timeoutMs);
        try {
            var response = await fetch(url, Object.assign({}, options, { signal: controller.signal }));
            if (readJson && response.ok && !response.redirected) {
                var data = await response.json();
                return { ok: true, redirected: false, json: function () { return Promise.resolve(data); } };
            }
            return response;
        }
        finally { root.clearTimeout(timer); }
    }
    function render() {
        if (!host) return;
        if (requestNotice && requestNoticeUntil && Date.now() >= requestNoticeUntil) {
            requestNotice = 'No reply from the controller. Check the connection before trying again.';
            requestNoticeUntil = 0;
        }
        if (!latest) {
            if (requestNotice) host.querySelector('[data-mode-pending]').textContent = requestNotice;
            return;
        }
        var now = Date.now() + (latest.clockOffset || 0), text = describe(latest, now);
        var activeLabel = host.querySelector('[data-mode-active]');
        if (activeLabel.textContent !== text.active) activeLabel.textContent = text.active;
        host.querySelector('[data-mode-pending]').textContent = requestNotice || text.pending;
        var announcement = host.querySelector('[data-mode-announcement]');
        var pendingNotice = (requestNotice || text.pending).replace(/\. \d+s remaining$/, '');
        if (announcement && announcement.textContent !== pendingNotice) announcement.textContent = pendingNotice;
        host.querySelector('[data-mode-link]').textContent = text.link;
        var warning = host.querySelector('[data-mode-warning]');
        var warningText = text.fresh ? '' : 'No recent update from the controller. Showing the last known readings.';
        if (warning && warning.textContent !== warningText) warning.textContent = warningText;
        host.classList.toggle('is-pending', text.waiting);
        host.classList.toggle('is-stale', !text.fresh);
        document.querySelectorAll('.sh-mode-form button[data-mode]').forEach(function (button) {
            var active = Number(button.dataset.mode) === latest.reportedMode;
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', String(active));
            button.classList.toggle('is-pending', text.waiting && Number(button.dataset.mode) === latest.requestedMode);
            button.setAttribute('aria-busy', String(requesting || (text.waiting && Number(button.dataset.mode) === latest.requestedMode)));
        });
        var pill = document.getElementById('sh-live-connection-pill');
        var label = document.getElementById('sh-live-connection-text');
        var hint = document.getElementById('sh-offline-hint');
        if (pill) pill.classList.toggle('is-offline', !text.fresh);
        if (label) label.textContent = text.fresh ? (latest.transport === 'wifi' ? 'Wi-Fi' : 'LoRa') : 'No recent report';
        if (hint) hint.style.display = text.fresh ? 'none' : '';
    }
    root.shReceiveModeState = function (state) {
        if (!state || !host || !root.matchesCurrentDeviceMac(state.deviceMac)) return;
        if (latest && Date.parse(state.serverUtc) < Date.parse(latest.serverUtc)) return;
        if (state.requestedAt && state.requestedAt !== requestBaseline) requestNotice = '';
        state.clockOffset = Date.parse(state.serverUtc) - Date.now();
        root.shServerClockOffset = state.clockOffset;
        latest = state; render();
        if (state.relayState && root.shReceiveRelayState) root.shReceiveRelayState(state.relayState);
    };
    async function refresh() {
        if (!host || refreshing) return;
        refreshing = true;
        try {
            var response = await fetchStatus(host.dataset.modeStateUrl, { credentials: 'same-origin', cache: 'no-store' }, 10000, true);
            if (!response.ok || response.redirected) throw new Error();
            root.shReceiveModeState(await response.json());
        } catch (_) {
            if (!latest) host.querySelector('[data-mode-pending]').textContent = requestNotice || 'Mode status is unavailable. Retrying...';
        } finally { refreshing = false; }
    }
    document.addEventListener('DOMContentLoaded', function () {
        host = document.querySelector('[data-mode-state-url]');
        if (!host) return;
        document.querySelectorAll('.sh-mode-form').forEach(function (form) {
            form.addEventListener('submit', async function (event) {
                event.preventDefault();
                if (requesting) return;
                requesting = true;
                requestBaseline = latest && latest.requestedAt;
                requestNotice = 'Sending mode request...';
                requestNoticeUntil = Date.now() + 180000;
                var button = form.querySelector('button'), token = form.querySelector('input[name="__RequestVerificationToken"]');
                button.classList.add('is-pending'); button.setAttribute('aria-busy', 'true');
                document.querySelectorAll('.sh-mode-form button').forEach(function (item) { item.disabled = true; });
                host.querySelector('[data-mode-pending]').textContent = 'Sending mode request...';
                try {
                    var body = new URLSearchParams(new FormData(form));
                    var response = await fetchStatus(form.action, { method: 'POST', credentials: 'same-origin',
                        headers: { 'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': token.value }, body: body }, 20000);
                    if (!response.ok || response.redirected) throw new Error();
                    requestNotice = latest && latest.requestedAt && latest.requestedAt !== requestBaseline
                        ? '' : 'Mode request queued. Waiting for controller status...';
                    await refresh();
                } catch (_) {
                    requestNotice = 'We could not confirm the request. Checking before you try again.';
                    await refresh();
                } finally {
                    requesting = false;
                    button.classList.remove('is-pending'); button.setAttribute('aria-busy', 'false');
                    document.querySelectorAll('.sh-mode-form button').forEach(function (item) { item.disabled = false; });
                    render();
                }
            });
        });
        refresh();
        root.setInterval(refresh, 5000);
        root.setInterval(render, 1000);
    });
})(typeof window === 'undefined' ? globalThis : window);
