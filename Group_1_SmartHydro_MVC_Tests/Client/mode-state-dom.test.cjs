const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../Group_1_SmartHydro_MVC/wwwroot/js/mode-state.js'), 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));
async function checkFailure(queuedDespiteFailure) {
    const parts = {}; let ready, submit, posts = 0;
    const attributes = {};
    const button = { dataset: { mode: '1' }, classList: { toggle() {}, add() {}, remove() {} }, setAttribute(key, value) { attributes[key] = value; } };
    const form = { action: '/request', addEventListener(name, callback) { submit = callback; },
        querySelector: selector => selector === 'button' ? button : { value: 'csrf' } };
    const host = { dataset: { modeStateUrl: '/state' }, classList: { toggle() {} },
        querySelector: selector => parts[selector] ||= { textContent: '' } };
    const document = { addEventListener(name, callback) { ready = callback; },
        querySelector: () => host, getElementById: () => null,
        querySelectorAll: selector => selector === '.sh-mode-form' ? [form] : [button] };
    const intervals = [];
    const window = { document, matchesCurrentDeviceMac: () => true, setInterval(callback) { intervals.push(callback); } };
    const baseline = new Date(Date.now() - 60000).toISOString();
    let newest;
    async function fetch(url, options) {
        if (options && options.method === 'POST') { posts++; newest = new Date().toISOString(); return { ok: false }; }
        return { ok: true, json: async () => ({ deviceMac: 'AABBCCDDEE71', reportedMode: 0,
            requestedMode: posts && queuedDespiteFailure ? 1 : 0, requestedAt: posts && queuedDespiteFailure ? newest : baseline,
            status: posts && queuedDespiteFailure ? 'waiting' : 'confirmed', serverUtc: new Date().toISOString() }) };
    }
    vm.runInNewContext(source, { window, document, fetch, Date, URLSearchParams,
        FormData: class { *[Symbol.iterator]() { yield ['target', '1']; } } });
    ready(); await flush();
    await submit({ preventDefault() {} });
    intervals[1]();
    assert.equal(posts, 1, 'A failed status check must not resubmit the command');
    const message = parts['[data-mode-pending]'].textContent;
    if (queuedDespiteFailure) {
        assert.match(message, /Waiting for the controller to confirm: Online Wi-Fi/);
        assert.match(parts['[data-mode-announcement]'].textContent, /Waiting for the controller to confirm/);
        assert.doesNotMatch(parts['[data-mode-announcement]'].textContent, /\d+s$/);
    }
    else assert.match(message, /could not confirm/);
    assert.match(parts['[data-mode-active]'].textContent, /LoRa/);
    assert.equal(button.disabled, false);
    assert.match(parts['[data-mode-warning]'].textContent, /No recent update from the controller/);
    const now = Date.now();
    window.shReceiveModeState({ deviceMac: 'AABBCCDDEE71', reportedMode: 0, status: 'confirmed',
        serverUtc: new Date(now + 1000).toISOString(), lastNodeSeenAt: new Date(now).toISOString(),
        lastGatewaySeenAt: new Date(now).toISOString(), lastGatewayRssi: -41 });
    assert.equal(parts['[data-mode-warning]'].textContent, '', 'A fresh node report clears the stale warning');
    assert.match(parts['[data-mode-link]'].textContent, /Gateway last seen: 1s ago/);
    window.shReceiveModeState({ deviceMac: 'AABBCCDDEE71', reportedMode: 0, status: 'confirmed',
        serverUtc: new Date(now + 2000).toISOString(), lastNodeSeenAt: new Date(now - 180000).toISOString(),
        lastGatewaySeenAt: new Date(now).toISOString() });
    assert.match(parts['[data-mode-warning]'].textContent, /No recent update from the controller/, 'A fresh gateway cannot hide a stale node');
}
async function checkUnreachableStatus(hangPost = false, earlyReceipt = false) {
    const parts = {}, intervals = []; let ready, submit, posts = 0, now = Date.now();
    class Clock extends Date { static now() { return now; } }
    const button = { dataset: { mode: '0' }, classList: { toggle() {}, add() {}, remove() {} }, setAttribute() {} };
    const form = { action: '/request', addEventListener(_, fn) { submit = fn; }, querySelector: key => key === 'button' ? button : { value: 'csrf' } };
    const host = { dataset: { modeStateUrl: '/state' }, classList: { toggle() {} }, querySelector: key => parts[key] ||= { textContent: '' } };
    const document = { addEventListener(_, fn) { ready = fn; }, querySelector: () => host, getElementById: () => null,
        querySelectorAll: key => key === '.sh-mode-form' ? [form] : [button] };
    const window = { document, matchesCurrentDeviceMac: () => true, setInterval(fn) { intervals.push(fn); } };
    const timers = new Map(); let nextTimer = 0;
    if (hangPost) Object.assign(window, { AbortController,
        setTimeout(fn) { timers.set(++nextTimer, fn); return nextTimer; }, clearTimeout(id) { timers.delete(id); } });
    async function fetch(_, options) {
        if (options && options.method === 'POST') {
            posts++;
            if (earlyReceipt) window.shReceiveModeState({ deviceMac: 'AABBCCDDEE71', reportedMode: 0,
                requestedMode: 0, requestedAt: new Date(now).toISOString(), status: 'confirmed',
                serverUtc: new Date(now).toISOString(), lastNodeSeenAt: new Date(now).toISOString() });
            if (hangPost) return new Promise((_, reject) => options.signal.addEventListener('abort', () => reject(new Error('request timed out'))));
            return { ok: true };
        }
        throw new Error('connection lost');
    }
    vm.runInNewContext(source, { window, document, fetch, Date: Clock, URLSearchParams,
        FormData: class { *[Symbol.iterator]() { yield ['target', '0']; } } });
    ready(); await flush();
    const pending = submit({ preventDefault() {} }); await flush();
    if (hangPost) { for (const fire of [...timers.values()]) fire(); }
    await pending;
    assert.equal(button.disabled, false, 'Even a stuck POST must release its busy controls');
    now += 181000; intervals[1]();
    if (earlyReceipt) assert.equal(parts['[data-mode-pending]'].textContent, '', 'A fast controller report must not be overwritten by queued text');
    else assert.match(parts['[data-mode-pending]'].textContent, /No reply from the controller/);
    assert.equal(posts, 1, 'A missing status must never resend a mode command');
}
(async () => { await checkFailure(false); await checkFailure(true); await checkUnreachableStatus(); await checkUnreachableStatus(true); await checkUnreachableStatus(false, true);
    console.log('Mode DOM checks passed: failed POST keeps its message, a queued request is recovered, and neither path resubmits or changes active mode.');
})().catch(error => { console.error(error); process.exitCode = 1; });

(async function checkSingleStatusRequest() {
    let ready, resolveGet, requests = 0;
    const intervals = [], parts = {};
    const host = { dataset: { modeStateUrl: '/state' }, classList: { toggle() {} }, querySelector: key => parts[key] ||= { textContent: '' } };
    const document = { addEventListener(_, callback) { ready = callback; }, querySelector: () => host,
        querySelectorAll: () => [], getElementById: () => null };
    const window = { document, matchesCurrentDeviceMac: () => true, setInterval(callback) { intervals.push(callback); } };
    const fetch = () => { requests++; return new Promise(resolve => { resolveGet = resolve; }); };
    vm.runInNewContext(source, { window, document, fetch, Date });
    ready(); intervals[0](); intervals[0]();
    assert.equal(requests, 1, 'A slow status response must not create overlapping requests');
    resolveGet({ ok: false }); await flush();
    intervals[0](); assert.equal(requests, 2, 'Status polling resumes after a failed response');
    resolveGet({ ok: false }); await flush();
    console.log('Mode polling checks passed: one request in flight, recovery after failure');
})().catch(error => { console.error(error); process.exitCode = 1; });
