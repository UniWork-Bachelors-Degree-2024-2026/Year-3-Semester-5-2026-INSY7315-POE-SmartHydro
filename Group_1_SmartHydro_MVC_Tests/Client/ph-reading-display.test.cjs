const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../Group_1_SmartHydro_MVC/wwwroot/js/site.js'), 'utf8');
const attributes = {};
const notice = { textContent: '',
    getAttribute: key => attributes[key], setAttribute: (key, value) => { attributes[key] = value; } };
const calls = []; let elapsed = 0;
const context = vm.createContext({ Date, Number, Math, performance: { now: () => elapsed },
    document: { addEventListener: () => {}, querySelector: () => null, querySelectorAll: () => [notice] }, window: {} });
vm.runInContext(source.slice(source.indexOf('function phReadingAgeLabel('), source.indexOf('function markStructureOnlineNow(')), context);
context.updateCardMetricDom = (...args) => calls.push(args);
const previous = { value: 6.2, recordedAt: '2026-11-02T05:00:00Z', ageSeconds: 300 };
context.updatePhReadingDom(null, true, previous);
assert.equal(calls.at(-1)[1], '6.2'); assert.equal(calls.at(-1)[4], null, 'Retained number cannot enter the live trend');
assert.equal(attributes.title, 'Last reading, 5 minutes ago');
elapsed = 60000; context.refreshPhReadingAges();
assert.equal(attributes.title, 'Last reading, 6 minutes ago');
context.updatePhReadingDom(null, true, { ...previous, ageSeconds: 14400 });
assert.equal(calls.at(-1)[1], '6.2'); assert.equal(attributes.title, 'Last reading, 240 minutes ago');
context.updatePhReadingDom(6.8, false, previous);
assert.equal(calls.at(-1)[1], '6.8'); assert.equal(calls.at(-1)[4], 6.8); assert.equal(attributes.title, '');
elapsed += 60000; context.refreshPhReadingAges(); assert.equal(attributes.title, '');
for (const invalid of [null, { ...previous, value: -1 }, { ...previous, recordedAt: 'invalid' }, { ...previous, ageSeconds: -1 }]) {
    context.updatePhReadingDom(null, true, invalid);
    assert.equal(calls.at(-1)[1], 'Waiting for a valid reading');
    assert.equal(attributes.title, 'No previous reading available');
}
assert.equal(notice.textContent, '', 'Age metadata must not add a visible line or overwrite the pH value');
assert.equal(attributes['aria-description'], attributes.title);
console.log('pH display checks passed: retained value, age, recovery, missing history and no historical trend injection.');
