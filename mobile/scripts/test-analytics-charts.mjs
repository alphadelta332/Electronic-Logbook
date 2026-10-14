import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const echarts = require("echarts");
let lastOptions, disposed = false, listeners = 0, observers = 0;
let fontSize = "13px";
const element = { isConnected: true, clientWidth: 390, clientHeight: 240, dataset: {} };
const context = {
    window: {}, Intl, Date, console,
    document: { documentElement: {}, createElement: () => ({ children: [], append(child) { this.children.push(child); } }) },
    getComputedStyle: () => ({ fontSize, getPropertyValue: name => ({ "--app-primary": "#187a46", "--app-text": "#102018", "--app-text-muted": "#5f7168", "--app-border": "#d7e3dc", "--app-surface": "#fff", "--app-bg": "#f7faf7" })[name] }),
    matchMedia: () => ({ matches: true, addEventListener() { listeners++; }, removeEventListener() { listeners--; } }),
    ResizeObserver: class { observe() { observers++; } disconnect() { observers--; } },
    MutationObserver: class { observe() { observers++; } disconnect() { observers--; } },
    echarts: { init: () => ({ resize() {}, setOption(options) { lastOptions = options; }, dispose() { disposed = true; } }) }
};
vm.runInNewContext(readFileSync(new URL("../src/ElectronicLogbook.Mobile/wwwroot/js/analyticsCharts.js", import.meta.url), "utf8"), context);
const charts = context.window.electronicLogbookCharts;
const year = { kind: "year", labels: ["2023", "2024"], series: [{ name: "<img src=x onerror=alert(1)>", values: [0.15, 9], color: "#397e63" }] };
charts.update(element, year);
assert.equal(lastOptions.animation, false);
assert.equal(lastOptions.series[0].stack, "hours");
assert.deepEqual(lastOptions.series[0].data, year.series[0].values);
const tip = lastOptions.tooltip.formatter({ name: "2023", dataIndex: 0, value: .15, seriesName: year.series[0].name });
assert.equal(tip.children[1].textContent, "0.2");
assert.equal(tip.children[2].textContent, "<img src=x onerror=alert(1)> · 0.2");
assert.equal(tip.children[2].innerHTML, undefined);
charts.update(element, { ...year, series: [...year.series, { name: "Dual", values: [1, 2], color: "#63b9bb" }] });
assert.equal(lastOptions.tooltip.formatter({ name: "2024", dataIndex: 1, value: 2, seriesName: "Dual" }).children[1].textContent, "11.0");

charts.update(element, { kind: "time", labels: ["2023-12-31", "2024-01-10"], series: [{ name: "Flying hours", values: [0, 9], color: "" }] });
assert.equal(lastOptions.series[0].data[1][0] - lastOptions.series[0].data[0][0], 10 * 86400000);
assert.equal(lastOptions.series[0].smooth, false);
assert.equal(lastOptions.useUTC, true);
assert.equal(lastOptions.xAxis.axisLabel.formatter(Date.UTC(2024, 0, 10)), "10 Jan");
assert.equal(lastOptions.xAxis.axisLabel.showMaxLabel, false);
assert.equal(lastOptions.grid.right, 33);
const rendered = echarts.init(null, null, { renderer: "svg", ssr: true, width: 390, height: 240 });
rendered.setOption(lastOptions);
assert.match(rendered.renderToSVGString(), /<svg/);
rendered.dispose();

charts.update(element, { kind: "pie", labels: ["Command", "Dual"], series: [{ name: "Command", values: [7], color: "#397e63" }, { name: "Dual", values: [2], color: "#63b9bb" }] });
assert.equal(lastOptions.series[0].data.reduce((sum, point) => sum + point.value, 0), 9);
charts.update(element, { kind: "ranking", labels: ["YSSY"], series: [{ name: "Airports", values: [2], color: "" }], counts: true });
assert.equal(lastOptions.yAxis.inverse, true);
assert.equal(lastOptions.series[0].label.formatter({ value: 2 }), "2");
assert.equal(lastOptions.tooltip.formatter([{ name: "YSSY", value: 2 }]).children[1].textContent, "2 visits");
fontSize = "28px";
charts.update(element, { kind: "ranking", labels: ["VH-ZMV"], series: [{ name: "Registrations", values: [268.9], color: "" }] });
assert.equal(lastOptions.series[0].label.formatter({ value: 268.9 }), "268.9");
assert.equal(lastOptions.grid.right, 107);
assert.throws(() => charts.update(element, { kind: "year", labels: [], series: [{ values: [1] }] }), /must align/);
charts.dispose(element);
assert.equal(disposed, true);
assert.equal(observers, 0);
assert.equal(listeners, 0);
console.log("Chart rendering, exact dates, rounding, safe tooltip text, reduced motion and disposal passed.");
