const maps = new WeakMap();
const longitudeDelta = (from, to) => ((to - from + 540) % 360) - 180;
const longitude = value => ((value + 180) % 360 + 360) % 360 - 180;

export function mapRequest(url, baseUri) {
    const target = new URL(url, baseUri), app = new URL(baseUri);
    const provider = target.origin === "https://tiles.openfreemap.org" &&
        (/^\/styles\/(positron|dark)$/.test(target.pathname) || /^\/(planet(?:\/|$)|fonts\/|sprites\/|natural_earth\/)/.test(target.pathname));
    if (target.username || target.password || (target.origin !== app.origin && (!provider || target.search || target.hash)))
        throw new Error("Map request outside the app or approved map provider.");
    return { url: target.href, credentials: "omit", referrerPolicy: "no-referrer" };
}

function backgroundTheme() {
    const theme = document.documentElement.getAttribute("data-elb-theme");
    return theme === "dark" || (theme !== "light" && matchMedia("(prefers-color-scheme: dark)").matches) ? "dark" : "positron";
}

function offlineBackground(state) {
    state.backgroundAbort?.abort();
    clearTimeout(state.backgroundTimeout);
    if (state.backgroundIdle) state.map.off("idle", state.backgroundIdle);
    state.backgroundIdle = null;
    state.background = null;
    state.backgroundApplied = false;
    for (const layer of [...state.map.getStyle().layers].reverse())
        if (layer.id.startsWith("online-")) state.map.removeLayer(layer.id);
    for (const id of Object.keys(state.map.getStyle().sources))
        if (id.startsWith("online-")) state.map.removeSource(id);
    state.map.setGlyphs(null);
    state.map.setSprite(null);
    state.map.getContainer().dataset.background = "offline";
    updateMarkerAccessibility(state);
}

async function onlineBackground(state) {
    if (state.disposed || !navigator.onLine) return;
    const theme = backgroundTheme();
    if (state.background === theme) return;
    offlineBackground(state);
    state.background = theme;
    const center = state.map.getCenter(), zoom = state.map.getZoom();
    const controller = state.backgroundAbort = new AbortController();
    state.backgroundTimeout = setTimeout(() => { if (!state.disposed) offlineBackground(state); }, 12000);
    try {
        const response = await fetch(mapRequest(`https://tiles.openfreemap.org/styles/${theme}`, document.baseURI).url,
            { credentials: "omit", referrerPolicy: "no-referrer", signal: controller.signal, redirect: "error" });
        if (!response.ok) throw new Error("Map provider unavailable.");
        const style = await response.json();
        if (state.disposed || controller.signal.aborted) return;
        // Keep local sources and route layers intact while the optional background loads.
        state.map.setGlyphs(style.glyphs);
        state.map.setSprite(style.sprite);
        for (const [id, source] of Object.entries(style.sources)) state.map.addSource(`online-${id}`, source);
        for (const layer of style.layers) state.map.addLayer({ ...layer, id: `online-${layer.id}`,
            ...(layer.source ? { source: `online-${layer.source}` } : {}) }, "connections");
        state.backgroundApplied = true;
        state.backgroundIdle = () => {
            clearTimeout(state.backgroundTimeout);
            state.map.off("idle", state.backgroundIdle);
            state.backgroundIdle = null;
            state.map.getContainer().dataset.background = "online";
            // Online attribution can be taller; preserve any view the pilot has changed.
            const current = state.map.getCenter();
            if (!state.backgroundLoaded && !state.popup?.isOpen() && current.lng === center.lng && current.lat === center.lat && state.map.getZoom() === zoom)
                showAll(state.map.getContainer());
            state.backgroundLoaded = true;
            updateMarkerAccessibility(state);
            if (state.background !== backgroundTheme()) void onlineBackground(state);
        };
        state.map.on("idle", state.backgroundIdle);
    } catch {
        if (!state.disposed && state.backgroundAbort === controller) offlineBackground(state);
    }
}

export function routeFeatures(data) {
    const airports = new Map(data.markers.map(marker => [marker.icao, marker]));
    return {
        type: "FeatureCollection",
        features: [
            ...data.connections.map(line => {
                const from = airports.get(line.from), to = airports.get(line.to);
                return { type: "Feature", properties: { key: `${line.from}/${line.to}`, count: line.count },
                    geometry: { type: "LineString", coordinates: [[from.longitude, from.latitude], [from.longitude + longitudeDelta(from.longitude, to.longitude), to.latitude]] } };
            })
        ]
    };
}

export function routeBounds(data) {
    if (!data.markers.length) return null;
    const longitudes = [...new Set(data.markers.map(marker => longitude(marker.longitude)))].sort((a, b) => a - b);
    let gap = -1, west = longitudes[0];
    for (let index = 0; index < longitudes.length; index++) {
        const next = longitudes[(index + 1) % longitudes.length] + (index === longitudes.length - 1 ? 360 : 0);
        if (next - longitudes[index] > gap) { gap = next - longitudes[index]; west = longitude(next); }
    }
    let east = west;
    for (const value of longitudes) east = Math.max(east, value < west ? value + 360 : value);
    const airports = new Map(data.markers.map(marker => [marker.icao, marker]));
    let minimum = west;
    for (const line of data.connections) {
        const from = airports.get(line.from), to = airports.get(line.to);
        const start = longitude(from.longitude) < west ? longitude(from.longitude) + 360 : longitude(from.longitude);
        const end = start + longitudeDelta(from.longitude, to.longitude);
        minimum = Math.min(minimum, end);
        east = Math.max(east, end);
    }
    const latitudes = data.markers.map(marker => Math.max(-85, Math.min(85, marker.latitude)));
    return [[minimum, Math.min(...latitudes)], [Math.min(east, minimum + 360), Math.max(...latitudes)]];
}

function colors() {
    const css = getComputedStyle(document.documentElement);
    const read = name => css.getPropertyValue(name).trim();
    return { water: read("--app-surface"), land: read("--app-surface-alt"), border: read("--app-text-muted"), route: read("--app-primary"), ink: read("--app-text") };
}

function recolor(state) {
    if (state.disposed || !state.map) return;
    const palette = colors();
    for (const [layer, property, value] of [["water", "background-color", palette.water], ["land", "fill-color", palette.land],
        ["borders", "line-color", palette.border], ["connections", "line-color", palette.route]])
        state.map.setPaintProperty(layer, property, value);
    if (state.background && state.background !== backgroundTheme()) void onlineBackground(state);
}

function updateMarkerAccessibility(state) {
    if (state.disposed || !state.map) return;
    const container = state.map.getContainer();
    const mapBounds = container.getBoundingClientRect();
    const covers = [...container.querySelectorAll(".maplibregl-ctrl-group, .maplibregl-ctrl-attrib, .maplibregl-popup-content"),
        ...container.parentElement.querySelectorAll(".route-map-toolbar")].map(element => element.getBoundingClientRect());
    for (const marker of state.markers ?? []) {
        const button = marker.getElement(), bounds = button.getBoundingClientRect();
        const obscured = bounds.left < mapBounds.left || bounds.right > mapBounds.right || bounds.top < mapBounds.top || bounds.bottom > mapBounds.bottom ||
            covers.some(cover => bounds.left < cover.right && bounds.right > cover.left && bounds.top < cover.bottom && bounds.bottom > cover.top);
        button.tabIndex = obscured ? -1 : 0;
        if (obscured) button.setAttribute("aria-hidden", "true");
        else button.removeAttribute("aria-hidden");
    }
}

async function initialize(element, state) {
    const { Map: LibreMap, NavigationControl, Marker, Popup, setWorkerUrl } = await import("./vendor/maplibre-gl.mjs");
    if (state.disposed) return;
    state.Marker = Marker;
    state.Popup = Popup;
    setWorkerUrl(new URL("./vendor/maplibre-gl-worker.mjs", import.meta.url).href);
    const palette = colors();
    state.map = new LibreMap({
        container: element, center: [135, -25], zoom: 1, minZoom: 0, maxZoom: 12,
        attributionControl: { compact: false }, dragRotate: false, pitchWithRotate: false, touchPitch: false,
        renderWorldCopies: true, canvasContextAttributes: { preserveDrawingBuffer: true },
        transformRequest: url => mapRequest(url, document.baseURI),
        style: { version: 8, sources: {
            countries: { type: "geojson", data: new URL("../data/natural-earth-countries.json", import.meta.url).href,
                attribution: 'Made with <a href="https://www.naturalearthdata.com/about/terms-of-use/" target="_blank" rel="noopener noreferrer">Natural Earth</a>' },
            routes: { type: "geojson", data: { type: "FeatureCollection", features: [] } }
        }, layers: [
            { id: "water", type: "background", paint: { "background-color": palette.water } },
            { id: "land", type: "fill", source: "countries", paint: { "fill-color": palette.land } },
            { id: "borders", type: "line", source: "countries", paint: { "line-color": palette.border, "line-width": .6, "line-opacity": .6 } },
            { id: "connections", type: "line", source: "routes", paint: { "line-color": palette.route, "line-width": 2.5, "line-opacity": .8 } }
        ] }
    });
    state.map.touchZoomRotate.disableRotation();
    state.map.addControl(new NavigationControl({ showCompass: false }), "top-right");
    state.map.getCanvas().setAttribute("aria-label", "Route map");
    await new Promise((resolve, reject) => {
        state.cancelLoad = resolve;
        state.map.once("load", resolve);
        state.map.once("error", event => reject(event.error));
    });
    if (state.disposed) return;
    element.dataset.background = "offline";
    state.map.on("error", () => { if (state.background) offlineBackground(state); });
    state.map.on("sourcedataloading", event => {
        if (!state.background || !event.sourceId?.startsWith("online-")) return;
        clearTimeout(state.backgroundTimeout);
        state.backgroundTimeout = setTimeout(() => { if (!state.disposed) offlineBackground(state); }, 12000);
    });
    state.map.on("idle", () => { if (state.backgroundApplied) clearTimeout(state.backgroundTimeout); });
    state.online = () => { void onlineBackground(state); };
    state.offline = () => offlineBackground(state);
    window.addEventListener("online", state.online);
    window.addEventListener("offline", state.offline);
    state.map.on("click", event => {
        let nearest, best = Infinity;
        for (const marker of state.markers ?? []) {
            const position = state.map.project(marker.getLngLat());
            const dx = position.x - event.point.x, dy = position.y - event.point.y;
            const distance = dx * dx + dy * dy;
            if (Math.abs(dx) <= 24 && Math.abs(dy) <= 24 && distance < best) { nearest = marker; best = distance; }
        }
        nearest?.getElement().click();
    });
    state.map.on("moveend", () => updateMarkerAccessibility(state));
    state.resize = new ResizeObserver(() => { state.map.resize(); updateMarkerAccessibility(state); });
    state.resize.observe(element);
    state.theme = new MutationObserver(() => recolor(state));
    state.theme.observe(document.documentElement, { attributes: true, attributeFilter: ["data-elb-theme", "style"] });
    state.media = matchMedia("(prefers-color-scheme: dark)");
    state.themeChanged = () => recolor(state);
    state.media.addEventListener("change", state.themeChanged);
}

export async function update(element, data) {
    let state = maps.get(element);
    if (!state) {
        state = { revision: 0, disposed: false };
        maps.set(element, state);
        state.ready = initialize(element, state);
    }
    element.setAttribute("aria-busy", "true");
    const revision = ++state.revision;
    try {
        await state.ready;
        if (state.disposed || revision !== state.revision) return;
        state.data = data;
        state.popup?.remove();
        for (const marker of state.markers ?? []) marker.remove();
        state.markers = data.markers.map(airport => {
            const button = document.createElement("button");
            const visits = `${airport.count} ${airport.count === 1 ? "visit" : "visits"}`;
            button.type = "button";
            button.className = "route-airport-marker";
            button.setAttribute("aria-label", `${airport.name}, ${airport.icao}, ${visits}`);
            button.setAttribute("aria-expanded", "false");
            const marker = new state.Marker({ element: button }).setLngLat([airport.longitude, airport.latitude]).addTo(state.map);
            button.addEventListener("click", event => {
                event.stopPropagation();
                state.popup?.remove();
                const content = document.createElement("div");
                content.className = "route-airport-details";
                content.setAttribute("role", "status");
                for (const [tag, text] of [["strong", airport.name], ["span", airport.icao], ["span", visits]]) {
                    const field = document.createElement(tag);
                    field.textContent = text;
                    content.append(field);
                }
                button.setAttribute("aria-expanded", "true");
                state.map.panTo(marker.getLngLat(), { duration: 0 });
                state.popup = new state.Popup({ offset: 32, maxWidth: "min(320px, calc(100vw - 32px))" })
                    .setLngLat(marker.getLngLat()).setDOMContent(content).addTo(state.map);
                state.popup.on("close", () => { button.setAttribute("aria-expanded", "false"); updateMarkerAccessibility(state); });
                updateMarkerAccessibility(state);
            });
            return marker;
        });
        state.map.getSource("routes").setData(routeFeatures(data));
        showAll(element);
        await new Promise(resolve => { state.cancelLoad = resolve; state.map.once("idle", resolve); });
        if (!state.disposed && revision === state.revision) { updateMarkerAccessibility(state); element.setAttribute("aria-busy", "false"); }
        if (!state.disposed && !state.backgroundStarted) { state.backgroundStarted = true; void onlineBackground(state); }
    } catch (error) { dispose(element); throw error; }
}

export function showAll(element) {
    const state = maps.get(element);
    if (!state?.data || state.disposed) return;
    const bounds = routeBounds(state.data);
    const attributionHeight = element.querySelector(".maplibregl-ctrl-attrib")?.getBoundingClientRect().height ?? 0;
    if (bounds) state.map.fitBounds(bounds, { padding: { top: 80, right: 80, bottom: Math.max(80, attributionHeight + 32), left: 80 }, maxZoom: 7, duration: 0 });
}

export function dispose(element) {
    const state = maps.get(element);
    if (!state) return;
    state.disposed = true;
    state.backgroundAbort?.abort();
    clearTimeout(state.backgroundTimeout);
    if (state.online) window.removeEventListener("online", state.online);
    if (state.offline) window.removeEventListener("offline", state.offline);
    state.cancelLoad?.();
    state.popup?.remove();
    for (const marker of state.markers ?? []) marker.remove();
    state.resize?.disconnect();
    state.theme?.disconnect();
    if (state.media) state.media.removeEventListener("change", state.themeChanged);
    state.map?.remove();
    maps.delete(element);
}
