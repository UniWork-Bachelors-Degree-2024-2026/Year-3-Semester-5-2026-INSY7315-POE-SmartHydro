const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../../Group_1_SmartHydro_MVC/wwwroot/js/site.js'), 'utf8');
const controls = source.slice(source.indexOf('var pendingControls = {}'), source.indexOf('function focusFirstStructure()'));
const handler = source.slice(source.indexOf("    actuatorConn.on('ReceiveCommandAck'"), source.indexOf("    actuatorConn.on('OnActuatorStateChanged'"));

function page(controlKey = 'growLight') {
    const host = { getAttribute: name => name === 'data-structure-id' ? '7' : '/controls/toggle' };
    const toggle = checked => ({
        setAttribute: () => {}, removeAttribute: () => {}, checked, disabled: false, caption: '', closest: selector => selector === ".sh-toggle" ? null : host,
        getAttribute: name => name === 'data-control-key' ? controlKey : 'Grow light'
    });
    const group = [toggle(true), toggle(false)];
    const timers = new Map(); let nextTimer = 0, reply;
    const handlers = {};
    const context = vm.createContext({
        URLSearchParams, AbortController, console,
        document: { querySelector: () => ({ value: 'csrf-token' }), querySelectorAll: () => group },
        window: {
            shAnnounce: () => {}, location: {},
            setTimeout: (callback, delay) => { callback.delay = delay; timers.set(++nextTimer, callback); return nextTimer; },
            clearTimeout: id => timers.delete(id)
        },
        showControlState: (item, on) => { item.checked = on; },
        showControlCaption: (item, text) => { item.caption = text; },
        matchesCurrentDeviceMac: mac => mac === 'AA:BB:CC:DD:EE:A1',
        controlKeyAliases: controlKey === 'growLight' ? { growLight: ['growLight'] } : {},
        markStructureOnlineNow: () => {},
        actuatorConn: { on: (name, callback) => { handlers[name] = callback; } },
        fetch: () => new Promise(resolve => { reply = resolve; })
    });
    const telemetryMarker = "    actuatorConn.on('OnTelemetryUpdated', function (telemetry) {";
    const telemetryStart = source.indexOf(telemetryMarker) + telemetryMarker.length;
    const telemetryEnd = source.indexOf('        var ph = telemetry.phLevel', telemetryStart);
    const receiver = source.slice(source.indexOf('function receiveActuatorState(state)'), source.indexOf('window.shReceiveRelayState = receiveActuatorState;'));
    vm.runInContext(controls + receiver + handler + '\nfunction reported(telemetry) { ' + source.slice(telemetryStart, telemetryEnd) + ' }', context);
    return {
        context, group, timers,
        send: () => context.sendControl(group[0]),
        ack: value => handlers.ReceiveCommandAck({ deviceMac: 'AA:BB:CC:DD:EE:A1', ...value }),
        respond: async value => {
            reply({ ok: true, json: () => Promise.resolve(value) });
            await new Promise(resolve => setImmediate(resolve));
        }
    };
}

(async () => {
    // Stored ACK state updates both edges without a separate state event or POST.
    const immediate = page();
    immediate.ack({ commandId: 10, ok: true, state: { deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: true, reportedSequence: 10 } });
    assert(immediate.group.every(item => item.checked));
    immediate.ack({ commandId: 11, ok: true, state: { deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: false, reportedSequence: 11 } });
    assert(immediate.group.every(item => !item.checked));
    immediate.ack({ commandId: 10, ok: true, state: { deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: true, reportedSequence: 10 } });
    assert(immediate.group.every(item => !item.checked), 'An older ON report cannot overwrite newer OFF');
    const queued = { ok: true, on: false, commandId: 42, pending: true };
    let test = page(); test.send(); await test.respond(queued);
    assert.equal(test.group[0].checked, false);
    assert.equal(test.group[0].disabled, true);
    assert.match(test.group[0].caption, /Waiting/);
    test.ack({ commandId: 43, ok: true, state: { growLight: true } });
    assert.equal(test.group[0].disabled, true);
    test.ack({ commandId: 42, ok: true, state: { growLight: true } });
    assert(test.group.every(item => item.checked && !item.disabled));
    assert.match(test.group[0].caption, /Confirmed/);
    test = page(); test.send(); await test.respond(queued);
    [...test.timers.values()].find(callback => callback.delay === 2000)();
    await test.respond({ commandId: 42, terminal: true, status: 'ACKED', ok: true, state: { growLight: true } });
    assert(test.group.every(item => item.checked && !item.disabled), 'Polling must recover a missed push ACK without resubmitting the command');

    test = page(); test.send(); await test.respond(queued);
    [...test.timers.values()].find(callback => callback.delay === 2000)();
    await test.respond({ commandId: 42, terminal: true, status: 'EXPIRED', ok: false, state: { growLight: false } });
    assert(test.group.every(item => !item.checked && !item.disabled));
    assert.match(test.group[0].caption, /expired/);

    test = page(); test.send(); await test.respond(queued);
    test.ack({ commandId: 42, ok: false, reason: 'safety_blocked', state: { growLight: false } });
    assert(test.group.every(item => !item.checked && !item.disabled));
    assert.match(test.group[0].caption, /refused/);

    test = page(); test.send();
    test.ack({ commandId: 42, ok: true, state: { growLight: true } });
    await test.respond(queued);
    assert(test.group.every(item => item.checked && !item.disabled));

    test = page(); test.send(); await test.respond(queued);
    [...test.timers.values()][0]();
    assert(test.group.every(item => !item.checked && !item.disabled));
    assert.match(test.group[0].caption, /No confirmation/);

    test = page(); test.send(); await test.respond(queued);
    test.ack({ deviceMac: 'AA:BB:CC:DD:EE:A2', commandId: 42, ok: true, state: { growLight: true } });
    assert.equal(test.group[0].disabled, true);

    test = page(); test.send();
    for (let id = 65500; id < 65565; id++) test.ack({ commandId: id, ok: true });
    test.ack({ commandId: 1, ok: true, state: { growLight: true } });
    await test.respond({ ...queued, commandId: 1 });
    assert.equal(test.group[0].checked, true);
    assert.equal(test.group[0].disabled, false);
    test = page(); test.send(); await test.respond(queued);
    test.context.reported({ deviceMac: 'AA:BB:CC:DD:EE:A1', relayBitmask: 2, recordedAt: new Date(Date.now() - 5000).toISOString() });
    assert.equal(test.group[0].disabled, true, 'A capture from before the click must not confirm it');
    test.context.reported({ deviceMac: 'AA:BB:CC:DD:EE:A1', relayBitmask: 0, recordedAt: new Date(Date.now() + 1000).toISOString() });
    assert.equal(test.group[0].disabled, true, 'A mismatched GPIO must leave the request pending');
    test.context.reported({ deviceMac: 'AA:BB:CC:DD:EE:A1', relayBitmask: 2, recordedAt: new Date(Date.now() + 1000).toISOString() });
    assert(test.group.every(item => item.disabled), 'Matching telemetry is not a command receipt');

    test = page(); test.send(); await test.respond({ ...queued, requestId: 'new-request' });
    test.ack({ commandId: 42, requestId: 'old-request', ok: true, state: { growLight: true } });
    assert(test.group.every(item => item.disabled), 'A reused CID must match its request identity');
    test.ack({ commandId: 42, requestId: 'new-request', ok: true, state: { growLight: true, reportedSequence: 10 } });
    assert(test.group.every(item => item.checked && !item.disabled));

    test = page(); test.send(); await test.respond(queued);
    test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: true, reportedSequence: 20 });
    assert(test.group.every(item => item.checked && item.disabled), 'Fresh reports must remain visible while a request is pending');
    test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: false, reportedSequence: 10 });
    assert(test.group.every(item => item.checked));
    test.ack({ commandId: 42, ok: true, state: { growLight: false, reportedSequence: 15 } });
    assert(test.group.every(item => item.checked && !item.disabled), 'A delayed ACK must not revert the latest report');

    test = page(); test.send(); await test.respond(queued);
    test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: true, reportedSequence: 20 });
    [...test.timers.values()].find(callback => callback.delay === 65000)();
    assert(test.group.every(item => item.checked && !item.disabled), 'Timeout must retain the latest report');
    assert.match(test.group[0].caption, /unknown/);
    for (const [key, up, down] of [['ph', 'phUpPump', 'phDownPump'], ['ec', 'nutrientUpPump', 'dilutionDownPump']]) {
        test = page(key);
        test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', [up]: true, [down]: false, reportedSequence: 1 });
        assert(test.group.every(item => item.checked));
        test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', [up]: false, reportedSequence: 2 });
        assert(test.group.every(item => item.checked), 'A missing opposing-pump value must not imply the group is off');
        test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', [up]: false, [down]: false, reportedSequence: 3 });
        assert(test.group.every(item => !item.checked), 'A complete reported OFF state updates the group');
    }
    test = page();
    test.context.receiveActuatorState({ deviceMac: 'AA:BB:CC:DD:EE:A1', growLight: true,
        reportedSequence: 1, updatedAt: new Date(Date.now() - 180000).toISOString() });
    assert(test.group.every(item => item.checked), 'A stale report retains its reported state');
    assert.match(test.group[0].caption, /fresh update.*check the equipment/);
    test = page(); test.send(); await test.respond(queued);
    test.ack({ commandId: 42, ok: true, state: { growLight: true, reportedSequence: 1,
        updatedAt: new Date(Date.now() - 180000).toISOString() } });
    assert.match(test.group[0].caption, /fresh update.*check the equipment/, 'A receipt must not hide an old relay report');
    console.log('Command confirmation checks passed: pending, ACK, refusal, early ACK, timeout, device filtering and ID wrap.');
})().catch(error => { console.error(error); process.exitCode = 1; });

(async function connectionRecoveryChecks() {
    const timers = new Map(); let nextTimer = 0;
    const context = vm.createContext({ window: {
        setTimeout: (callback, delay) => { timers.set(++nextTimer, { callback, delay }); return nextTimer; },
        clearTimeout: id => timers.delete(id)
    } });
    vm.runInContext(source.slice(source.indexOf('function recoverActuatorConnection('), source.indexOf('function initActuatorSignalR()')), context);
    let starts = 0, registrations = 0, failStart = true, failRegister = false;
    const connection = { state: 'Disconnected',
        start: async function () { starts++; if (failStart) throw Error('offline'); this.state = 'Connected'; },
        onclose: function (callback) { this.closed = callback; },
        onreconnected: function (callback) { this.reconnected = callback; }
    };
    const flush = () => new Promise(resolve => setImmediate(resolve));
    const tick = async () => { const [id, timer] = timers.entries().next().value; timers.delete(id); timer.callback(); await flush(); return timer.delay; };
    context.recoverActuatorConnection(connection, async () => { registrations++; if (failRegister) throw Error('subscription failed'); });
    await flush(); assert.equal(starts, 1); assert.equal(timers.size, 1);
    assert.equal(await tick(), 1000); assert.equal(await tick(), 2000);
    failStart = false; await tick(); assert.equal(registrations, 1); assert.equal(timers.size, 0);
    failRegister = true; await connection.reconnected(); await flush();
    const previousStarts = starts; await tick(); assert.equal(starts, previousStarts, 'A subscription failure must not open a second connection');
    failRegister = false; await tick(); assert.equal(timers.size, 0);
    connection.state = 'Disconnected'; connection.closed(); connection.closed(); assert.equal(timers.size, 1, 'Only one retry timer is allowed');
    failStart = true;
    for (let i=0;i<8;i++) assert((await tick())<=30000, 'Retry delay is capped at thirty seconds');
    connection.state = 'Reconnecting'; const before = starts; await tick(); assert.equal(starts, before, 'Do not compete with automatic reconnect');
    failStart = false; connection.state = 'Connected'; await connection.reconnected(); assert.equal(timers.size, 0);
    console.log('Live connection recovery checks passed: initial failure, resubscription, closure, single timer and capped backoff.');
})().catch(error => { console.error(error); process.exitCode = 1; });

(function doseReceiptRefreshChecks() {
    let handler, checks = [];
    const button = { getAttribute: () => 'PH_UP' };
    const host = { doseId: 'dose-1', querySelector: () => button };
    const context = vm.createContext({ controlAttemptNumber: 0, receivedCommandAcks: {}, receivedCommandAckOrder: [], pendingControls: {},
        matchesCurrentDeviceMac: () => true, matchesPendingCommand: () => false,
        actuatorConn: { on: (_, callback) => { handler = callback; } },
        document: { querySelector: () => host }, window: { clearTimeout() {} }, Date,
        watchDoseStatus: (...args) => checks.push(args)
    });
    vm.runInContext(handlerSource(), context);
    handler({ deviceMac: 'AA', commandId: 1, doseId: 'other-dose', ok: true, phase: 'completed' });
    handler({ deviceMac: 'AA', commandId: 1, doseId: 'dose-1', ok: true, phase: 'accepted' });
    assert.equal(checks.length, 0, 'An unrelated or merely accepted dose must not imply completion');
    handler({ deviceMac: 'AA', commandId: 1, doseId: 'dose-1', ok: true, phase: 'completed' });
    assert.equal(checks.length, 1);
    assert.equal(checks[0][3], true, 'A matching completion triggers an immediate read, without the routine two-second wait');
    function handlerSource() { return source.slice(source.indexOf("    actuatorConn.on('ReceiveCommandAck'"), source.indexOf("    actuatorConn.on('OnActuatorStateChanged'")); }
    console.log('Dose receipt refresh checks passed: matching outcome refreshes immediately without resending a dose');
})();
