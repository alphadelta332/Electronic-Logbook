import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const listeners = {};
const properties = {};
let scrolls = 0;
let navTop = 550;
const control = {
    isConnected: true,
    matches: () => true,
    getBoundingClientRect: () => ({ top: 200, bottom: 248 }),
    closest: selector => selector === ".flight-entry-page"
        ? { querySelector: () => ({ getBoundingClientRect: () => ({ height: 60, bottom: 100 }) }) }
        : selector === ".airport-field" ? {} : null,
    scrollIntoView: () => scrolls++
};
const context = {
    window: {}, localStorage: { getItem: () => null }, innerHeight: 600,
    document: {
        documentElement: { setAttribute() {}, style: { setProperty: (key, value) => properties[key] = value } },
        querySelector: selector => selector === ".app-main" ? { style: { setProperty() {} } }
            : { getBoundingClientRect: () => ({ top: navTop }) },
        addEventListener: (name, handler) => listeners[name] = handler
    },
    visualViewport: { offsetTop: 0, height: 600, addEventListener: (name, handler) => listeners[`viewport-${name}`] = handler },
    requestAnimationFrame: callback => { callback(); return 1; }, cancelAnimationFrame() {}
};
vm.runInNewContext(readFileSync(new URL("../src/ElectronicLogbook.Mobile/wwwroot/js/logbookStore.js", import.meta.url), "utf8"), context);
listeners.focusin({ target: control });
assert.equal(properties["--airport-suggestions-height"], "286px");
context.visualViewport.height = 400;
navTop = 350;
listeners["viewport-resize"]();
assert.equal(properties["--airport-suggestions-height"], "86px");
listeners.focusout({ target: control });
listeners["viewport-resize"]();
assert.equal(scrolls, 2, "Leaving the field must stop keyboard-driven scrolling.");
console.log("Airport dropdown sizing: full screen, keyboard resize and focus cleanup passed.");
const resumed = [];
const subscription = context.window.electronicLogbookAirports.subscribe({
    invokeMethodAsync: name => { resumed.push(name); return Promise.resolve(); }
});
context.document.visibilityState = 'hidden';
listeners.visibilitychange();
assert.equal(resumed.length, 0);
context.document.visibilityState = 'visible';
listeners.visibilitychange();
context.window.electronicLogbookAirports.resumed();
assert.deepEqual(resumed, ['HandleAirportResumeAsync', 'HandleAirportResumeAsync']);
context.window.electronicLogbookAirports.unsubscribe(subscription);
context.window.electronicLogbookAirports.resumed();
assert.equal(resumed.length, 2);
console.log('Airport resume: foreground/native callbacks and unsubscribe passed.');
