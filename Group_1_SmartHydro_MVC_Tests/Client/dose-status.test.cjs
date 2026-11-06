const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../../Group_1_SmartHydro_MVC/wwwroot/js/site.js'), 'utf8');
const functions = source.slice(source.indexOf('function showDoseStatus('), source.indexOf('/* Shows a control\'s on or off state'));
const doseId = '11111111-2222-3333-4444-555555555555';
const queued = { ok: true, pending: 'Queued', last: null, hasPending: true, doseId, deliveryStatus: 'QUEUED' };
const flush = () => new Promise(resolve => setImmediate(resolve));

function page(responses) {
    const parts = {};
    ['.sh-dose-pending', '.sh-dose-last', '.sh-dose-message'].forEach(key => {
        parts[key] = { textContent: key === '.sh-dose-last' ? 'Earlier cycle' : '', classList: { toggle: () => {} } };
    });
    const hostAttributes = { 'data-dose-url': '/controls/dose', 'data-dose-status-url': '/controls/status',
        'data-structure-id': '7' };
    const host = {
        querySelector: key => parts[key], querySelectorAll: () => buttons,
        getAttribute: key => hostAttributes[key]
    };
    const buttons = [0, 1].map(() => {
        const classes = new Set(), attributes = {};
        return { classes, attributes,
            classList: { toggle: (name, on) => on ? classes.add(name) : classes.delete(name) },
            setAttribute: (name, value) => { attributes[name] = value; }, disabled: false,
            closest: () => host, getAttribute: () => 'PH_UP' };
    });
    const timers = new Map(), calls = []; let next = 0, now = 1000;
    const context = vm.createContext({
        URLSearchParams, AbortController, performance: { now: () => now },
        Date: class extends Date { static now() { return now; } },
        document: { querySelector: () => ({ value: 'csrf' }) },
        window: {
            location: {}, confirm: () => true, setTimeout: (callback, delay) => { timers.set(++next, { callback, delay }); return next; },
            clearTimeout: id => timers.delete(id)
        },
        fetch: async (url, options) => {
            calls.push({ url, method: options.method || 'GET', body: options.body });
            const result = await responses.shift();
            if (result instanceof Error) throw result;
            assert(result, 'Unexpected extra status request');
            return { ok: result.httpStatus !== 503, json: async () => result };
        }
    });
    vm.runInContext(functions, context);
    // Capture wording independently of the page's banner layout.
    context.showDoseMessage = (_host, text) => { parts['.sh-dose-message'].textContent = text; };
    return {
        context, host, parts, buttons, calls, hostAttributes,
        send: async () => { context.sendDose(buttons[0]); await flush(); },
        tick: async delay => {
            const entry = [...timers].find(([, timer]) => timer.delay === delay);
            assert(entry, `No ${delay} ms timer was scheduled`);
            timers.delete(entry[0]); now += delay; entry[1].callback(); await flush();
        },
        advance: amount => { now += amount; }
    };
}

(async () => {
    const cancelled = page([]);
    cancelled.hostAttributes['data-ph-fault'] = 'true';
    cancelled.context.window.confirm = () => false;
    await cancelled.send(); assert.equal(cancelled.calls.length, 0);
    const confirmed = page([queued]);
    confirmed.hostAttributes['data-ph-fault'] = 'true';
    let confirmationText;
    confirmed.context.window.confirm = text => { confirmationText = text; return true; };
    await confirmed.send(); await confirmed.send();
    assert.equal(confirmed.calls.length, 1);
    assert.match(confirmationText, /current pH is unknown/);
    assert.equal(new URLSearchParams(confirmed.calls[0].body).get('manualPhOverride'), 'true');

    // A second handler invocation before the first HTTP reply must not POST again.
    for (const type of ['PH_DOWN', 'DILUTION_DOWN']) {
        let reply;
        const pendingReply = new Promise(resolve => { reply = resolve; });
        const rapid = page([pendingReply, { ...queued, hasPending: false, deliveryStatus: 'REJECTED', reason: 'sensor_fault' }]);
        rapid.buttons[0].getAttribute = () => type;
        rapid.context.sendDose(rapid.buttons[0]);
        rapid.context.sendDose(rapid.buttons[1]);
        assert.equal(rapid.calls.length, 1);
        assert.equal(rapid.host.doseSubmitting, true);
        reply(queued); await flush();
        assert.equal(rapid.host.doseSubmitting, false);
        await rapid.tick(2000);
        assert.equal(rapid.calls.filter(call => call.method === 'POST').length, 1);
        assert.equal(rapid.calls[1].method, 'GET');
        assert(rapid.buttons.every(button => !button.disabled && !button.classes.has('is-pending')));
        assert.match(rapid.parts['.sh-dose-message'].textContent, /sensor reading is unavailable/);
    }
    let test = page([queued, new Error('connection lost'),
        { ...queued, deliveryStatus: 'RUNNING', pending: 'Pump running' },
        { ...queued, hasPending: false, deliveryStatus: 'COMPLETED', last: 'Cycle completed' }]);
    await test.send();
    assert.equal(test.parts['.sh-dose-last'].textContent, 'Earlier cycle');
    assert(test.buttons.every(button => button.disabled));
    await test.tick(1200);
    assert(test.buttons.every(button => button.disabled));
    await test.send();
    assert.equal(test.calls.filter(call => call.method === 'POST').length, 1);
    await test.tick(2000);
    assert.match(test.parts['.sh-dose-message'].textContent, /Connection interrupted/);
    await test.tick(2000);
    assert.equal(test.parts['.sh-dose-message'].textContent, 'Pump running.');
    await test.tick(2000);
    assert(test.buttons.every(button => !button.disabled));
    assert.match(test.parts['.sh-dose-message'].textContent, /volume is an estimate/);
    assert(test.calls.filter(call => call.method === 'GET').every(call => call.url.includes('doseId=' + doseId)));

    for (const status of ['REJECTED', 'CANCELLED', 'TIMED_OUT', 'UNCONFIRMED']) {
        test = page([queued, { ...queued, hasPending: false, deliveryStatus: status, reason: 'safety_blocked' }]);
        await test.send(); await test.tick(2000);
        assert.equal(test.host.dosePending, false);
        assert(test.buttons.every(button => button.disabled === (status === 'UNCONFIRMED')));
        if (status === 'UNCONFIRMED') {
            assert(test.buttons.every(button => !button.classes.has('is-pending')));
            assert(test.buttons.every(button => button.attributes['aria-busy'] === 'false'));
            await test.send();
            assert.equal(test.calls.filter(call => call.method === 'POST').length, 1);
        }
        await test.tick(1200);
        assert(test.buttons.every(button => button.disabled === (status === 'UNCONFIRMED')), 'Delayed submission cleanup must not unlock an unknown dose outcome');
        assert.doesNotMatch(test.parts['.sh-dose-message'].textContent, /cycle completed/);
    }

    test = page([queued, { httpStatus: 503 }]);
    await test.send(); test.advance(180000); await test.tick(2000);
    assert(test.buttons.every(button => button.disabled));
    assert.match(test.parts['.sh-dose-message'].textContent, /earlier dose is still unconfirmed/);
    assert(test.buttons.every(button => !button.classes.has('is-pending')));
    assert.equal(test.calls.filter(call => call.method === 'POST').length, 1);
    assert.doesNotMatch(test.context.doseStatusMessage({ reason: 'mixing_lockout' }), /is still mixing/);
    assert.match(test.context.doseStatusMessage({ reason: 'mixing_lockout' }), /does not confirm water circulation/);
    console.log('Dose status checks passed: single submission, command tracking, terminal outcomes and bounded polling.');

    test = page([new Error('Reply lost'), queued, { ...queued, hasPending: false, deliveryStatus: 'COMPLETED' }]);
    await test.send();
    assert(test.buttons.every(button => button.disabled));
    assert(test.buttons.every(button => !button.classes.has('is-pending')));
    await test.send();
    assert.equal(test.calls.filter(call => call.method === 'POST').length, 1);
    await test.tick(2000);
    assert(test.calls[1].url.includes('requestedAfter='), 'A lost POST reply must not be confused with an older dose');
    await test.tick(2000);
    assert(test.buttons.every(button => !button.disabled));
})().catch(error => { console.error(error); process.exitCode = 1; });
