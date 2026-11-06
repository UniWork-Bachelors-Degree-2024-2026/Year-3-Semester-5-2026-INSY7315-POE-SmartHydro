const assert = require('node:assert/strict');
const { describe } = require('../../Group_1_SmartHydro_MVC/wwwroot/js/mode-state.js');
const now = Date.parse('2026-10-28T10:00:00Z');
const state = { reportedMode: 0, requestedMode: 1, requestedAt: new Date(now - 30000).toISOString(), status: 'waiting',
    lastNodeSeenAt: new Date(now - 4000).toISOString(), lastGatewaySeenAt: new Date(now - 8000).toISOString(), lastGatewayRssi: -68 };
let text = describe(state, now);
assert.match(text.active, /LoRa/); assert.match(text.pending, /Online Wi-Fi/); assert.equal(text.remaining, 150);
assert.match(text.pending, /150s remaining/);
assert.match(text.link, /Gateway last seen: 8s ago/); assert.equal(text.fresh, true);
text = describe(state, now + 180000); assert.equal(text.waiting, false); assert.match(text.pending, /not confirmed/); assert.equal(text.fresh, false);
text = describe({ ...state, reportedMode: 1, reportedModeReason: 1, status: 'confirmed' }, now);
assert.match(text.active, /switched automatically while LoRa recovers/); assert.equal(text.pending, '');
text = describe({ ...state, reportedMode: 1, reportedModeReason: 0, transport: 'wifi', status: 'confirmed' }, now);
assert.match(text.active, /chosen by you/); assert.match(text.link, /Wi-Fi: controller last seen: 4s ago/);
text = describe({ ...state, reportedMode: 2, status: 'confirmed' }, now); assert.match(text.active, /192.168.4.1/);
text = describe({ ...state, status: 'rejected' }, now); assert.match(text.pending, /did not accept/);
text = describe({ status: 'reported' }, now); assert.match(text.active, /waiting/); assert.equal(text.fresh, false);
console.log('Mode display checks passed: pending countdown, timeout, fallback, direct Wi-Fi, SoftAP, rejection and unknown state.');
