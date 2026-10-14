(function () {
    const plots = new WeakMap();
    const hourFormat = new Intl.NumberFormat("en-AU", { minimumFractionDigits: 1, maximumFractionDigits: 1, useGrouping: false });
    const hours = value => hourFormat.format(Number(value));
    const dateLabel = value => new Intl.DateTimeFormat("en-AU", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(value));

    function tooltip(data, params) {
        const points = Array.isArray(params) ? params : [params];
        const box = document.createElement("div");
        box.className = "analytics-tooltip";
        const title = document.createElement("span");
        title.textContent = data.kind === "time" ? dateLabel(points[0].value[0]) : points[0].name;
        box.append(title);
        const total = document.createElement("strong");
        const value = data.kind === "year" ? data.series.reduce((sum, series) => sum + Number(series.values[points[0].dataIndex]), 0)
            : points.reduce((sum, point) => sum + Number(Array.isArray(point.value) ? point.value[1] : point.value), 0);
        total.textContent = data.counts ? `${value} visits` : hours(value);
        box.append(total);
        if (data.kind === "year") {
            for (const point of points.filter(point => Number(point.value) > 0)) {
                const row = document.createElement("div");
                row.textContent = `${point.seriesName} · ${hours(point.value)}`;
                box.append(row);
            }
        }
        return box;
    }

    function options(element, data) {
        const css = getComputedStyle(element);
        const color = name => css.getPropertyValue(name).trim();
        const primary = color("--app-primary");
        const text = color("--app-text-muted");
        const font = Math.max(12, parseFloat(css.fontSize));
        const horizontal = data.kind === "ranking";
        const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
        const label = { color: text, fontSize: font, fontFamily: "Inter, Segoe UI, sans-serif", hideOverlap: true };
        const axis = { axisTick: { show: false }, axisLine: { show: false }, axisLabel: label };
        const result = {
            useUTC: true, animation: !reduced, animationDuration: 300, animationDurationUpdate: 200,
            textStyle: { fontFamily: "Inter, Segoe UI, sans-serif" },
            tooltip: { trigger: data.kind === "pie" || data.kind === "year" ? "item" : "axis", confine: true,
                backgroundColor: color("--app-surface"), borderWidth: 0, padding: 12,
                textStyle: { color: color("--app-text"), fontSize: font },
                extraCssText: "border-radius:12px;box-shadow:0 6px 24px rgba(0,0,0,.14);max-width:90%;",
                formatter: params => tooltip(data, params), axisPointer: { type: "line", lineStyle: { color: text, width: 1 } } },
            grid: { left: horizontal ? Math.min(140, element.clientWidth * .4) : 8, right: horizontal ? 66 : 8, top: 18, bottom: 12, containLabel: !horizontal }
        };
        if (horizontal) {
            const digits = Math.max(...data.series[0].values.map(value => (data.counts ? String(value) : hours(value)).length));
            result.grid.right = Math.max(66, Math.ceil(digits * font * .65 + 16));
        }
        if (data.kind === "pie") {
            result.series = [{ type: "pie", radius: "72%", center: ["50%", "50%"], label: { show: false },
                itemStyle: { borderColor: color("--app-bg"), borderWidth: 3, borderRadius: 4 },
                emphasis: { scaleSize: 5 }, data: data.series.map(series => ({ name: series.name, value: series.values[0], itemStyle: { color: series.color } })) }];
            return result;
        }
        const valuesAxis = { ...axis, type: "value", min: 0, splitNumber: 3,
            splitLine: { show: !horizontal, lineStyle: { color: color("--app-border"), type: "dashed", width: 1 } } };
        const labelsAxis = { ...axis, type: data.kind === "time" ? "time" : "category", data: data.labels,
            boundaryGap: data.kind !== "time", splitLine: { show: false },
            axisLabel: { ...label, interval: Math.max(0, Math.ceil(data.labels.length / Math.max(2, Math.floor(element.clientWidth / (font * 4)))) - 1) } };
        if (data.kind === "time") {
            result.grid.right = Math.ceil(font * 2.5);
            const days = (Date.parse(data.labels.at(-1)) - Date.parse(data.labels[0])) / 86400000;
            labelsAxis.axisLabel.showMaxLabel = false;
            labelsAxis.axisLabel.formatter = value => days >= 730 ? String(new Date(value).getUTCFullYear())
                : new Intl.DateTimeFormat("en-AU", days < 60 ? { day: "numeric", month: "short", timeZone: "UTC" }
                    : { month: "short", year: "2-digit", timeZone: "UTC" }).format(new Date(value));
        }
        if (horizontal) {
            labelsAxis.inverse = true;
            labelsAxis.axisLabel = { ...label, interval: 0, width: result.grid.left - 12, overflow: "truncate" };
        }
        result.xAxis = horizontal ? { ...valuesAxis, axisLabel: { show: false }, splitLine: { show: false } } : labelsAxis;
        result.yAxis = horizontal ? labelsAxis : valuesAxis;
        result.series = data.series.map((series, index) => ({
            name: series.name, type: data.kind === "time" ? "line" : "bar",
            stack: data.kind === "year" ? "hours" : undefined,
            data: data.kind === "time" ? series.values.map((value, point) => [Date.parse(`${data.labels[point]}T00:00:00Z`), value]) : series.values,
            itemStyle: { color: series.color || primary, borderRadius: horizontal ? [0, 4, 4, 0] : index === data.series.length - 1 ? [3, 3, 0, 0] : 0 },
            barMaxWidth: horizontal ? 16 : 28, barCategoryGap: "32%",
            emphasis: { focus: "series" },
            label: horizontal ? { show: true, position: "right", color: color("--app-text"), fontSize: font,
                formatter: params => data.counts ? `${params.value}` : hours(params.value) } : { show: false },
            smooth: false, showSymbol: data.kind === "time" && series.values.length < 20, symbolSize: 5,
            lineStyle: { width: 2.5, color: primary }, areaStyle: data.kind === "time" ? { color: primary, opacity: .08 } : undefined
        }));
        return result;
    }

    window.electronicLogbookCharts = {
        update(element, data) {
            if (!data.labels || data.series.some(series => series.values.length !== (data.kind === "pie" ? 1 : data.labels.length))) throw new Error("Chart labels and values must align.");
            let state = plots.get(element);
            if (!state) {
                state = { data, chart: null };
                state.draw = () => {
                    if (!element.isConnected || !element.clientWidth || !element.clientHeight) return;
                    state.chart ||= echarts.init(element, null, { renderer: "svg" });
                    state.chart.resize();
                    state.chart.setOption(options(element, state.data), { notMerge: true });
                    element.dataset.chartReady = "true";
                };
                state.resize = new ResizeObserver(state.draw);
                state.resize.observe(element);
                state.theme = new MutationObserver(state.draw);
                state.theme.observe(document.documentElement, { attributes: true, attributeFilter: ["data-elb-theme", "style"] });
                state.systemTheme = matchMedia("(prefers-color-scheme: dark)");
                state.systemTheme.addEventListener("change", state.draw);
                plots.set(element, state);
            }
            state.data = data;
            state.draw();
        },
        dispose(element) {
            const state = plots.get(element);
            if (!state) return;
            state.resize.disconnect(); state.theme.disconnect(); state.chart?.dispose(); plots.delete(element);
            state.systemTheme.removeEventListener("change", state.draw);
        }
    };
}());
