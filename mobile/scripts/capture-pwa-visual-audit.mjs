import { createReadStream } from "node:fs";
import { createServer } from "node:http";
import { access, mkdir, stat, writeFile, readFile } from "node:fs/promises";
import { dirname, extname, join, normalize, relative, resolve, isAbsolute } from "node:path";
import { fileURLToPath } from "node:url";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "playwright";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const mobileRoot = resolve(scriptDirectory, "..");
const defaultPublishRoot = join(mobileRoot, "artifacts", "pages", "wwwroot");
const defaultOutputDirectory = join(mobileRoot, "artifacts", "visual-audit");
const mimeTypes = new Map([
    [".css", "text/css"],
    [".html", "text/html"],
    [".js", "application/javascript"],
    [".mjs", "application/javascript"],
    [".json", "application/json"],
    [".png", "image/png"],
    [".svg", "image/svg+xml"],
    [".wasm", "application/wasm"],
    [".webmanifest", "application/manifest+json"],
    [".woff2", "font/woff2"]
]);

const phoneWidths = [
    { name: "390x844", width: 390, height: 844 },
    { name: "320x712", width: 320, height: 712 },
    { name: "360x800", width: 360, height: 800 },
    { name: "412x915", width: 412, height: 915 }
];
const phoneTextScales = [1, 1.25, 1.5, 1.75, 2];
const profiles = [
    ...phoneWidths.flatMap(phone => phoneTextScales.map(fontScale => ({
        name: `phone-${phone.name}-font${fontScale * 100}`,
        width: phone.width,
        height: phone.height,
        fontScale,
        blocking: true
    }))),
    { name: "wide-768", width: 768, height: 1024, fontScale: 1, blocking: false },
    { name: "ipad-landscape-1024x768", width: 1024, height: 768, fontScale: 1, blocking: false }
];
const colorSchemes = ["light", "dark"];
const routes = [
    { name: "dashboard", path: "/", readySelector: "main .dashboard-page" },
    { name: "dashboard-populated", path: "/", readySelector: "main .dashboard-page", state: "populated" },
    { name: "dashboard-large-totals", path: "/", readySelector: "main .dashboard-page", state: "large-totals" },
    { name: "currency", path: "/currency", readySelector: "main .currency-page" },
    { name: "logbook-entries", path: "/flights?view=entries", readySelector: "main .logbook-page" },
    { name: "logbook-entries-populated", path: "/flights?view=entries", readySelector: "main .flight-row-list", state: "populated" },
    { name: "logbook-totals", path: "/flights?view=totals", readySelector: "main .logbook-totals" },
    { name: "logbook-deleted", path: "/flights?view=deleted", readySelector: "main .deleted-entries-view" },
    { name: "new-flight", path: "/flights/new", readySelector: "main .flight-entry-page" },
    { name: "flight-detail", path: ({ entryId }) => `/flights/${entryId}`, readySelector: "main .flight-detail-page", state: "populated" },
    { name: "edit-flight", path: ({ entryId }) => `/flights/${entryId}/edit`, readySelector: "main .flight-entry-page", state: "populated" },
    { name: "deleted-flights", path: "/flights?view=deleted", readySelector: "main .deleted-entry-row", state: "deleted" },
    { name: "deleted-flight-detail", path: ({ entryId }) => `/flights/${entryId}`, readySelector: "main .deleted-detail-hero", state: "deleted" },
    { name: "more", path: "/more", readySelector: "main .more-page" },
    { name: "charts", path: "/charts", readySelector: "main [aria-labelledby=\"charts-heading\"]" },
    { name: "charts-populated", path: "/charts", readySelector: "main #hours-year-heading", state: "analytics" },
    { name: "charts-design", path: "/charts", readySelector: "main #hours-year-heading", state: "analytics-design" },
    { name: "stats-populated", path: "/charts", readySelector: "main #hours-year-heading", state: "analytics", stats: true },
    { name: "routes", path: "/routes", readySelector: "main [aria-labelledby=\"routes-heading\"]" },
    { name: "routes-populated", path: "/routes", readySelector: "main .route-map-canvas[aria-busy=false]", state: "route-map" },
    { name: "exchange", path: "/exchange", readySelector: "main .package-exchange-page" },
    { name: "settings", path: "/settings", readySelector: "main .settings-page" },
    { name: "settings-expanded", path: "/settings", readySelector: "main .settings-page", expanded: true },
    { name: "export", path: "/export", readySelector: "main .workbook-migration-page" },
    { name: "workbook-verification", path: "/advanced/workbook-verification", readySelector: "main .workbook-migration-page" }
];

function optionValue(name, fallback) {
    const index = process.argv.indexOf(name);
    return index >= 0 && process.argv[index + 1] ? resolve(process.argv[index + 1]) : fallback;
}

function optionFilter(name, values, keySelector = value => value) {
    const index = process.argv.indexOf(name);
    if (index < 0 || !process.argv[index + 1]) {
        return values;
    }

    const requested = process.argv[index + 1];
    const selected = values.filter(value => keySelector(value) === requested);
    if (selected.length === 0) {
        throw new Error(`Unknown ${name} value: ${requested}`);
    }

    return selected;
}

if (process.argv.includes("--help")) {
    console.log("Usage: node scripts/capture-pwa-visual-audit.mjs [--publish-root <path>] [--output-dir <path>] [--profile <name>] [--theme <light|dark>] [--route <name>]");
    process.exit(0);
}

const publishRoot = optionValue("--publish-root", defaultPublishRoot);
const outputDirectory = optionValue("--output-dir", defaultOutputDirectory);
const selectedProfiles = optionFilter("--profile", profiles, profile => profile.name);
const selectedColorSchemes = optionFilter("--theme", colorSchemes);
const selectedRoutes = optionFilter("--route", routes, route => route.name);

async function createStaticServer(root) {
    await access(join(root, "index.html"));
    return await new Promise((resolveServer) => {
        const server = createServer(async (request, response) => {
            const requestPath = decodeURIComponent(new URL(request.url, "http://127.0.0.1").pathname);
            const requestedPath = normalize(join(root, requestPath));
            const relativePath = relative(root, requestedPath);
            const insideRoot = relativePath === "" || (relativePath && !relativePath.startsWith("..") && !isAbsolute(relativePath));
            let filePath = insideRoot ? requestedPath : "";

            try {
                const fileStat = filePath ? await stat(filePath).catch(() => null) : null;
                if (!fileStat?.isFile()) {
                    filePath = join(root, "index.html");
                }

                response.writeHead(200, {
                    "Content-Type": mimeTypes.get(extname(filePath)) ?? "application/octet-stream",
                    "Cache-Control": "no-store"
                });
                createReadStream(filePath).pipe(response);
            } catch {
                response.writeHead(404).end();
            }
        });
        server.listen(0, "127.0.0.1", () => resolveServer(server));
    });
}

async function createDashboardFlight(page, baseUrl) {
    await page.goto(`${baseUrl}/flights/new`, { waitUntil: "domcontentloaded" });
    await page.locator("main .flight-entry-page").waitFor({ state: "visible", timeout: 30000 });
    await page.getByLabel("Date", { exact: true }).fill("2026-08-01");
    await page.getByLabel("Type", { exact: true }).fill("B738");
    await page.getByLabel("Registration", { exact: true }).fill("VH-LNG");
    await page.getByLabel("Flight ID", { exact: true }).fill("QF1234");
    await page.getByLabel("PIC", { exact: true }).fill("Self");
    await page.getByLabel("From", { exact: true }).fill("YMMB");
    await page.getByLabel("To", { exact: true }).fill("YSSY");
    await page.getByLabel("To", { exact: true }).press("Escape");
    await page.getByLabel("SE Command Day", { exact: true }).fill("2.0");
    await page.getByRole("button", { name: "Add flight" }).click();
    const reviewDialog = page.getByRole("dialog", { name: "Add this flight?" });
    await reviewDialog.waitFor({ state: "visible", timeout: 30000 });
    await reviewDialog.getByRole("button", { name: /^(Add flight|Save anyway)$/ }).click();
    const lastFlight = page.locator("main .dashboard-last-flight-link");
    await lastFlight.waitFor({ state: "visible", timeout: 30000 });
    const href = await lastFlight.getAttribute("href");
    const entryId = href?.match(/^\/flights\/([^/]+)$/)?.[1];
    if (!entryId) {
        throw new Error(`Saved flight link did not contain an entry ID: ${href}`);
    }

    return entryId;
}

async function clickMapAirport(page, icao, visits) {
    const marker = page.getByRole("button", { name: new RegExp(`${icao}, ${visits} visits?$`) });
    await marker.scrollIntoViewIfNeeded();
    const bounds = await marker.boundingBox();
    await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
}

async function prepareRouteState(page, baseUrl, state) {
    if (!state) {
        return {};
    }

    const entryId = await createDashboardFlight(page, baseUrl);
    if (state === "route-map") {
        await page.evaluate(async () => {
            const stored = JSON.parse(await window.electronicLogbookStore.load("portable-document"));
            const document = JSON.parse(stored.documentJson);
            const template = document.operations.find(operation => operation.entry);
            const routes = [
                ["YMMB", "", "YSSY", 1], ["YSSY", "YMMB YSSY", "YMMB", 1],
                ["YSSY", "UNKNOWN", "YMML", 2], ["YMMB", "", "YMMB", 3],
                ["NZAA", "", "PHNL", 4], ["UNKNOWN", "OTHER", "UNKNOWN", 5]
            ];
            document.operations = routes.map(([from, via, to, day], index) => {
                const operation = structuredClone(template);
                operation.entryId = `ent_route_${index}`;
                operation.revisionId = `rev_route_${index}`;
                Object.assign(operation.entry, { year: 2024, month: 1, day, type: index === 4 ? "C208" : "C172", reg: index === 4 ? "VH-PAC" : "VH-TEST", from, via, to, seCommandDay: 1 });
                return operation;
            });
            stored.documentJson = JSON.stringify(document);
            await window.electronicLogbookStore.save("portable-document", JSON.stringify(stored));
        });
        await page.evaluate(() => navigator.serviceWorker.ready);
        await page.context().setOffline(true);
    }
    if (state === "large-totals" || state === "analytics" || state === "analytics-design") {
        // Synthetic migrated history, isolated in this audit's disposable browser context.
        await page.evaluate(async (fixture) => {
            const analytics = fixture !== "large-totals";
            const design = fixture === "analytics-design";
            const annualHours = [1, 1, 5, 10, 12, 85, 53, 45, 331, 125, 105, 5];
            const designFields = ["seIcusDay", "seDualDay", "seDualNight", "seCommandDay", "seCommandNight", "meDualDay", "meDualNight"];
            const shares = [.08, .07, .04, .7, .05, .04, .02];
            const stored = JSON.parse(await window.electronicLogbookStore.load("portable-document"));
            const document = JSON.parse(stored.documentJson);
            const template = document.operations.find(operation => operation.entry);
            const today = new Date();
            const hourFields = ["seCommandDay", "meDualDay", "seIcusDay", "copilotDay"];
            document.operations = Array.from({ length: design ? 84 : analytics ? 303 : 1493 }, (_, index) => {
                const operation = structuredClone(template);
                operation.entryId = `ent_audit_${index}`;
                operation.revisionId = `rev_audit_${index}`;
                const date = new Date(today.getFullYear(), today.getMonth(), today.getDate());
                date.setDate(date.getDate() - (index < 1100 ? 366 + index : index < 1465 ? 29 + (index - 1100) % 336 : index - 1465));
                if (analytics) date.setTime(new Date(2011, 0, 1 + index * 14).getTime());
                if (design) date.setTime(new Date(2011 + Math.floor(index / 7), index % 7, 1).getTime());
                Object.assign(operation.entry, {
                    year: date.getFullYear(), month: date.getMonth() + 1, day: date.getDate(),
                    seCommandDay: null,
                    [hourFields[index % hourFields.length]]: analytics ? (index === 0 ? 7.6 : 2.5) : index < 1100 ? (index === 0 ? 11 : 10) : index < 1465 ? 3.4 : 3.7,
                    ifrSim: index === 0 ? 1.2 : null
                });
                if (analytics) Object.assign(operation.entry, {
                    reg: ["VH-UMV", "VH-NMV", "VH-HAM", "VH-YBA", "VH-ZMV"][index % 5],
                    type: ["C208", "C172", "GA8"][index % 3],
                    remarks: "GENERAL HANDLING AEROS STALLS TURNS CLIMBING DESCENDING YSSY YCBR",
                    from: "YSSY", to: "YCBR"
                });
                if (design) {
                    for (const field of hourFields) operation.entry[field] = null;
                    const total = annualHours[Math.floor(index / 7)];
                    const portion = index % 7;
                    operation.entry[designFields[portion]] = portion === 6
                        ? Math.round((total - shares.slice(0, 6).reduce((sum, share) => sum + Math.round(total * share * 100) / 100, 0)) * 100) / 100
                        : Math.round(total * shares[portion] * 100) / 100;
                }
                return operation;
            });
            stored.documentJson = JSON.stringify(document);
            await window.electronicLogbookStore.save("portable-document", JSON.stringify(stored));
        }, state);
    }
    if (state === "deleted") {
        await page.goto(`${baseUrl}/flights/${entryId}`, { waitUntil: "domcontentloaded" });
        await page.locator("main .flight-detail-page").waitFor({ state: "visible", timeout: 30000 });
        await page.getByRole("button", { name: "Delete flight", exact: true }).click();
        const deleteDialog = page.getByRole("dialog", { name: "Delete this flight?" });
        await deleteDialog.waitFor({ state: "visible", timeout: 30000 });
        await deleteDialog.getByRole("button", { name: "Delete flight", exact: true }).click();
        await page.locator("main .logbook-page").waitFor({ state: "visible", timeout: 30000 });
        // Deletion feedback appears after saving; navigation alone can precede persistence.
        await page.getByText("Entry deleted.", { exact: true }).waitFor({ state: "visible" });
    }

    return { entryId };
}

async function applyTextScale(page, fontScale) {
    const sizesBefore = await page.evaluate(() => ({
        inlineRoot: document.documentElement.style.fontSize,
        body: parseFloat(getComputedStyle(document.body).fontSize)
    }));
    const sizesAfter = await page.evaluate((scale) => {
        document.documentElement.style.webkitTextSizeAdjust = `${scale * 100}%`;
        document.documentElement.style.textSizeAdjust = `${scale * 100}%`;
        const main = document.querySelector(".app-main");
        if (main) {
            main.scrollTop = 0;
            main.scrollLeft = 0;
        }
        window.scrollTo(0, 0);
        return {
            inlineRoot: document.documentElement.style.fontSize,
            body: parseFloat(getComputedStyle(document.body).fontSize)
        };
    }, fontScale);

    if (fontScale > 1 &&
        (sizesAfter.inlineRoot !== sizesBefore.inlineRoot ||
            sizesAfter.body < sizesBefore.body * (fontScale - 0.05))) {
        throw new Error(`Text-only scaling was ineffective at ${fontScale * 100}%: ${JSON.stringify({ sizesBefore, sizesAfter })}`);
    }
}

async function assertAccessible(page, contextLabel) {
    const accessibilityResults = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
        .analyze();
    if (accessibilityResults.violations.length > 0) {
        const violations = accessibilityResults.violations.map(violation => ({
            id: violation.id,
            impact: violation.impact,
            help: violation.help,
            targets: violation.nodes.flatMap(node => node.target).slice(0, 8)
        }));
        throw new Error(`WCAG accessibility audit failed for ${contextLabel}: ${JSON.stringify(violations)}`);
    }
}

const server = await createStaticServer(publishRoot);
const address = server.address();
const baseUrl = `http://127.0.0.1:${address.port}`;
await mkdir(outputDirectory, { recursive: true });
const failures = [];
const warnings = [];
const mapNetworkLog = selectedRoutes.some(route => route.state === "route-map") ? join(outputDirectory, "map-chromium-netlog.json") : null;

try {
    const browser = await chromium.launch({ headless: true, args: mapNetworkLog ? [`--log-net-log=${mapNetworkLog}`, "--net-log-capture-mode=IncludeSensitive"] : [] });
    try {
        for (const colorScheme of selectedColorSchemes) {
            for (const profile of selectedProfiles) {
                for (const route of selectedRoutes) {
                    const context = await browser.newContext({
                        colorScheme,
                        viewport: { width: profile.width, height: profile.height },
                        isMobile: profile.width < 600,
                        hasTouch: profile.width < 600
                    });
                    const page = await context.newPage();
                    const browserErrors = [];
                    page.on("pageerror", error => browserErrors.push(error.message));
                    page.on("console", message => {
                        if (message.type() === "error") {
                            if (route.state === "route-map" && message.location().url.startsWith("https://tiles.openfreemap.org/") && message.text().startsWith("Failed to load resource")) return;
                            browserErrors.push(message.text());
                        }
                    });
                    try {
                    const routeState = await prepareRouteState(page, baseUrl, route.state);
                    const mapRequests = [];
                    if (route.state === "route-map") page.on("request", request => mapRequests.push({ url: request.url(), method: request.method(), request }));
                    const routePath = typeof route.path === "function" ? route.path(routeState) : route.path;
                    await page.goto(`${baseUrl}${routePath}`, { waitUntil: "domcontentloaded" });
                    await page.locator(route.readySelector).waitFor({ state: "visible", timeout: route.state === "large-totals" ? 90000 : 30000 });
                    if (route.stats) {
                        await page.getByRole("tab", { name: "Stats", exact: true }).click();
                        await page.locator("main .analytics-statistics").first().waitFor({ state: "visible" });
                    }
                    if (route.expanded) {
                        await page.locator("main details").evaluateAll(panels => panels.forEach(panel => panel.open = true));
                    }
                    if (route.state === "large-totals") {
                        const totals = await page.locator(".dashboard-hours-total > strong, .dashboard-hours-total > small strong, .dashboard-hours-period > span").allTextContents();
                        if (JSON.stringify(totals) !== JSON.stringify(["12345.6", "12346.8", "103.6", "1344.6"])) {
                            throw new Error(`Large migrated totals fixture did not materialize correctly: ${JSON.stringify(totals)}`);
                        }
                    }
                    await applyTextScale(page, profile.fontScale);
                    if (route.state === "route-map") {
                        if (await page.locator(".routes-summary, .routes-help, .route-list, .route-selection, .route-flights").count()) throw new Error("Removed map prose or bottom sections returned.");
                        if (await page.locator(".route-airport-marker").count() !== 5) throw new Error("Expected five airport markers.");
                        const required = ["js/routeMap.mjs", "js/vendor/maplibre-gl.mjs", "js/vendor/maplibre-gl-worker.mjs", "css/vendor/maplibre-gl.css", "data/natural-earth-countries.json"];
                        const missing = await page.evaluate(async paths => {
                            const cache = await caches.open((await caches.keys()).find(name => name.startsWith("offline-cache-")));
                            return (await Promise.all(paths.map(async path => await cache.match(new URL(path, document.baseURI)) ? null : path))).filter(Boolean);
                        }, required);
                        if (missing.length) throw new Error(`Map assets absent from offline cache: ${missing.join(", ")}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-overview.png`) });
                        await page.getByRole("button", { name: "Filters", exact: true }).click();
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-filters.png`) });
                        await page.locator("#routes-from").fill("2024-01-04");
                        await page.locator("#routes-to").fill("2024-01-04");
                        await page.locator(".route-map-canvas[aria-busy=false]").waitFor();
                        await page.getByRole("button", { name: "Filters (1)", exact: true }).click();
                        const canvas = page.locator(".maplibregl-canvas");
                        const bounds = await canvas.boundingBox();
                        await canvas.click({ position: { x: bounds.width / 2, y: bounds.height / 2 } });
                        if (await page.locator(".maplibregl-popup").count()) throw new Error("Route lines should not open a selection panel.");
                        await clickMapAirport(page, "NZAA", 1);
                        const popup = page.locator(".route-airport-details");
                        await popup.waitFor();
                        if (await popup.locator("strong").innerText() === "" || JSON.stringify(await popup.locator("span").allTextContents()) !== JSON.stringify(["NZAA", "1 visit"])) throw new Error("Airport popup must contain name, ICAO and visits only.");
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-date-line.png`) });
                        await page.getByRole("button", { name: "Close popup", exact: true }).click();
                        await page.getByRole("button", { name: "Zoom in", exact: true }).click();
                        await page.getByRole("button", { name: "Show all routes", exact: true }).click();
                        await page.getByRole("button", { name: "Clear filters", exact: true }).click();
                        await page.getByRole("button", { name: "Filters", exact: true }).click();
                        await page.locator("#routes-from").fill("2024-01-03");
                        await page.locator("#routes-to").fill("2024-01-03");
                        await page.locator(".route-map-canvas[aria-busy=false]").waitFor();
                        await page.getByRole("button", { name: "Filters (1)", exact: true }).click();
                        const localAirport = page.getByRole("button", { name: /YMMB, 1 visit$/ });
                        await localAirport.focus();
                        await localAirport.press("Enter");
                        await popup.waitFor();
                        if (JSON.stringify(await popup.locator("span").allTextContents()) !== JSON.stringify(["YMMB", "1 visit"])) throw new Error("Keyboard airport selection failed.");
                        await page.getByRole("button", { name: "Clear filters", exact: true }).click();
                        await page.locator(".route-map-canvas[aria-busy=false]").waitFor();
                        await clickMapAirport(page, "YMMB", 3);
                        if (JSON.stringify(await popup.locator("span").allTextContents()) !== JSON.stringify(["YMMB", "3 visits"])) throw new Error("Repeated stops inflated airport visits.");
                        const popupBounds = await page.locator(".maplibregl-popup-content").boundingBox();
                        const mapBounds = await page.locator(".route-map-canvas").boundingBox();
                        if (popupBounds.x < mapBounds.x || popupBounds.x + popupBounds.width > mapBounds.x + mapBounds.width || popupBounds.y < mapBounds.y || popupBounds.y + popupBounds.height > mapBounds.y + mapBounds.height) throw new Error("Airport popup is clipped by the map.");
                        await assertAccessible(page, `offline route map ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-airport-popup.png`) });
                        const external = mapRequests.filter(request => new URL(request.url).origin !== new URL(baseUrl).origin);
                        // Existing font and public airport-catalogue GETs also run on other app screens.
                        const expectedBackgroundRequest = request => {
                            const url = new URL(request.url);
                            return request.method === "GET" && (
                                url.hostname === "fonts.googleapis.com" && url.pathname === "/css2" ||
                                url.hostname === "fonts.gstatic.com" && url.pathname.startsWith("/s/inter/") ||
                                url.hostname.endsWith(".supabase.co") && url.pathname.startsWith("/storage/v1/object/public/airport-catalog/"));
                        };
                        const unexpected = external.filter(request => !expectedBackgroundRequest(request));
                        console.log(`Offline map request inspection: ${mapRequests.length} requests; existing app background requests: ${JSON.stringify(external.map(request => ({ host: new URL(request.url).hostname, path: new URL(request.url).pathname, method: request.method })))}`);
                        if (unexpected.length) throw new Error(`Unexpected external requests: ${JSON.stringify(unexpected.map(request => ({ host: new URL(request.url).hostname, path: new URL(request.url).pathname, method: request.method })))}`);
                        await page.context().setOffline(false);
                        await context.addCookies([{ name: "route_privacy_canary", value: "must-never-be-sent", domain: "tiles.openfreemap.org", path: "/", secure: true, sameSite: "None" }]);
                        await page.goto(`${baseUrl}/routes`);
                        await page.locator(".route-map-canvas[aria-busy=false]").waitFor();
                        await applyTextScale(page, profile.fontScale);
                        await page.locator('.route-map-canvas[data-background="online"]').waitFor({ timeout: 25000 });
                        const attribution = page.locator(".maplibregl-ctrl-attrib");
                        for (const name of ["OpenFreeMap", "OpenMapTiles", "OpenStreetMap"]) {
                            if (!(await attribution.innerText()).includes(name)) throw new Error(`Missing ${name} attribution.`);
                        }
                        await assertAccessible(page, `online route map ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-online.png`) });
                        await clickMapAirport(page, "YMMB", 3);
                        if (JSON.stringify(await page.locator(".route-airport-details span").allTextContents()) !== JSON.stringify(["YMMB", "3 visits"])) throw new Error("Online background changed airport selection.");
                        await page.getByRole("button", { name: "Close popup", exact: true }).click();
                        // Fail a tile after successful startup, then fail the style on a new visit.
                        // Worker tile fetches bypass Playwright routing. Suppress only our offline
                        // listener here so actual failed fetches must trigger the renderer error path.
                        await page.evaluate(() => window.addEventListener("offline", event => event.stopImmediatePropagation(), { capture: true, once: true }));
                        await context.setOffline(true);
                        for (let zoom = 0; zoom < 4; zoom++) {
                            await page.getByRole("button", { name: "Zoom in", exact: true }).click();
                            await page.waitForTimeout(400);
                        }
                        await page.locator('.route-map-canvas[data-background="offline"]').waitFor({ timeout: 20000 });
                        await page.getByRole("button", { name: "Show all routes", exact: true }).click();
                        await clickMapAirport(page, "YMMB", 3);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-provider-failure.png`) });
                        // Restore connectivity without firing the app retry listener, then reject
                        // the main-thread style request to simulate a provider-specific outage.
                        await page.evaluate(() => window.addEventListener("online", event => event.stopImmediatePropagation(), { capture: true, once: true }));
                        await context.setOffline(false);
                        // Inject provider responses at the fetch boundary; the service worker can
                        // otherwise hide request failures from Playwright's page event stream.
                        await page.addInitScript(() => {
                            const originalFetch = window.fetch.bind(window);
                            window.fetch = (url, options) => {
                                if (String(url).startsWith("https://tiles.openfreemap.org/styles/")) {
                                    window.routeMapStyleAttempted = true;
                                    if (localStorage.getItem("route-map-audit-provider-mode") === "stall") return new Promise((resolve, reject) => {
                                        options.signal.addEventListener("abort", () => { window.routeMapStyleAborted = true; reject(new DOMException("Aborted", "AbortError")); });
                                    });
                                    return Promise.resolve(new Response(null, { status: 503 }));
                                }
                                return originalFetch(url, options);
                            };
                        });
                        await page.reload();
                        await page.waitForFunction(() => window.routeMapStyleAttempted);
                        await page.locator(".route-map-canvas[aria-busy=false]").waitFor();
                        await page.waitForFunction(() => document.querySelector(".route-map-canvas")?.dataset.background === "offline");
                        await clickMapAirport(page, "YMMB", 3);
                        await page.getByRole("button", { name: "Close popup", exact: true }).click();
                        if (profile.name === "phone-390x844-font100" && colorScheme === "light") {
                            await page.evaluate(() => localStorage.setItem("route-map-audit-provider-mode", "stall"));
                            const started = Date.now();
                            await page.reload();
                            await page.waitForFunction(() => window.routeMapStyleAborted, null, { timeout: 18000 });
                            if (Date.now() - started < 11000) throw new Error("Stalled provider exercise ended before the timeout.");
                            await page.locator('.route-map-canvas[data-background="offline"]').waitFor();
                            await clickMapAirport(page, "YMMB", 3);
                            await page.getByRole("button", { name: "Close popup", exact: true }).click();
                            console.log("Stalled provider style timed out; offline airport selection passed.");
                        }
                        const providerRequests = [];
                        for (const item of mapRequests.filter(item => new URL(item.url).hostname === "tiles.openfreemap.org")) {
                            if (item.request.failure()) continue;
                            const headers = await item.request.allHeaders();
                            const url = new URL(item.url);
                            if (item.method !== "GET" || item.request.postData() || url.search || url.username || url.password ||
                                ["authorization", "cookie", "apikey", "referer"].some(name => headers[name]) ||
                                /VH-TEST|VH-PAC|ent_route_|rev_route_|YMMB|YSSY|NZAA|PHNL|must-never-be-sent/.test(JSON.stringify({ url: item.url, headers })))
                                throw new Error(`Private data in map provider request: ${url.pathname}`);
                            providerRequests.push({ url: item.url, method: item.method, headers });
                        }
                        await writeFile(join(outputDirectory, `${profile.name}-${colorScheme}-map-network.json`), JSON.stringify(providerRequests, null, 2));
                        console.log(`Provider inspection passed: ${providerRequests.length} GETs; no credentials, cookies, referrer, flight IDs, registrations or airport identifiers. Online detail, live tile failure and startup failure passed.`);
                        await page.getByRole("button", { name: "Filters", exact: true }).click();
                        await page.locator("#routes-from").fill("2024-02-01");
                        await page.locator("#routes-to").fill("2024-01-01");
                        await page.getByRole("alert").filter({ hasText: "Choose a Start date" }).waitFor();
                        await page.getByRole("button", { name: "Clear filters", exact: true }).click();
                        await page.getByRole("button", { name: "Filters", exact: true }).click();
                        await page.locator(".route-map-canvas[aria-busy=false]").waitFor();
                        // The Material ripple temporarily expands the button's scroll dimensions.
                        await page.waitForFunction(() => {
                            const button = document.querySelector(".logbook-filter-button");
                            return button && button.getAnimations({ subtree: true }).length === 0 &&
                                button.scrollWidth <= button.clientWidth + 1 && button.scrollHeight <= button.clientHeight + 1;
                        }, null, { timeout: 2000 });
                        await page.evaluate(async () => {
                            const module = await import("./js/routeMap.mjs");
                            const markers = [
                                { icao: "YMMB", name: "Moorabbin", longitude: 145.1, latitude: -38, count: 9 },
                                { icao: "YSSY", name: "Sydney", longitude: 151.2, latitude: -33.9, count: 1 },
                                { icao: "YBBN", name: "Brisbane", longitude: 153.1, latitude: -27.4, count: 3 },
                                { icao: "YPAD", name: "Adelaide", longitude: 138.5, latitude: -34.9, count: 5 }
                            ];
                            await module.update(document.querySelector(".route-map-canvas"), {
                                markers, connections: markers.slice(1).map(airport => ({ from: "YMMB", to: airport.icao, count: airport.count }))
                            });
                            const appearances = markers.map(airport => {
                                const button = [...document.querySelectorAll(".route-airport-marker")].find(item => item.getAttribute("aria-label").includes(airport.icao));
                                const dot = getComputedStyle(button, "::after"), target = button.getBoundingClientRect();
                                if (target.width !== 48 || target.height !== 48) throw new Error("Frequency styling changed an airport touch target.");
                                return { size: parseFloat(dot.width), color: dot.backgroundColor };
                            });
                            if (JSON.stringify(appearances.map(dot => dot.size)) !== JSON.stringify([24, 12, 15, 18]) ||
                                new Set(appearances.map(dot => dot.color)).size !== 4)
                                throw new Error(`Airport frequency scale failed: ${JSON.stringify(appearances)}`);
                            const order = [...document.querySelectorAll(".route-airport-marker")].map(button => button.getAttribute("aria-label").split(", ")[1]);
                            if (JSON.stringify(order) !== JSON.stringify(["YSSY", "YBBN", "YPAD", "YMMB"])) throw new Error(`Airport stacking order failed: ${order}`);
                        });
                        await assertAccessible(page, `frequency map ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-frequency.png`) });
                        for (const accent of ["Ocean", "Sky", "Indigo", "Violet", "Teal", "Forest"]) {
                            const before = await page.getByRole("button", { name: /YSSY, 1 visit$/ }).evaluate(button => getComputedStyle(button, "::after").backgroundColor);
                            await page.evaluate(accent => window.electronicLogbookUiPreferences.applyTheme(`System|${accent}`), accent);
                            await page.waitForFunction(before => {
                                const button = [...document.querySelectorAll(".route-airport-marker")].find(button => button.getAttribute("aria-label").includes("YSSY"));
                                return getComputedStyle(button, "::after").backgroundColor !== before;
                            }, before);
                            if (accent === "Ocean") await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-routes-ocean.png`) });
                        }
                        await page.evaluate(async () => {
                            const module = await import("./js/routeMap.mjs");
                            await module.update(document.querySelector(".route-map-canvas"), { markers: [
                                { icao: "START", name: "Route start", longitude: 145, latitude: -38, count: 10 },
                                { icao: "BUSY", name: "Busy destination", longitude: 149, latitude: -38, count: 9 },
                                { icao: "QUIET", name: "Quiet destination", longitude: 147, latitude: -38, count: 1 }
                            ], connections: [{ from: "START", to: "BUSY", count: 9 }, { from: "START", to: "QUIET", count: 1 }] });
                            const positions = ["START", "QUIET"].map(icao => {
                                const button = [...document.querySelectorAll(".route-airport-marker")].find(button => button.getAttribute("aria-label").includes(icao));
                                const bounds = button.getBoundingClientRect();
                                return { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
                            });
                            const map = document.querySelector(".maplibregl-canvas"), bounds = map.getBoundingClientRect();
                            const sample = document.createElement("canvas");
                            sample.width = map.width; sample.height = map.height;
                            const context = sample.getContext("2d");
                            context.drawImage(map, 0, 0);
                            const [red, green] = context.getImageData(
                                Math.round(((positions[0].x + positions[1].x) / 2 - bounds.x) * map.width / bounds.width),
                                Math.round(((positions[0].y + positions[1].y) / 2 - bounds.y) * map.height / bounds.height), 1, 1).data;
                            if (red <= green) throw new Error(`Quiet route obscured the busy red route: ${red}, ${green}`);
                            await module.update(document.querySelector(".route-map-canvas"), { markers: [
                                { icao: "BUSY", name: "Busy airport", longitude: 145, latitude: -38, count: 9 },
                                { icao: "QUIET", name: "Quiet airport", longitude: 145, latitude: -38, count: 1 }
                            ], connections: [] });
                        });
                        await clickMapAirport(page, "QUIET", 1);
                        if (JSON.stringify(await page.locator(".route-airport-details span").allTextContents()) !== JSON.stringify(["BUSY", "9 visits"]))
                            throw new Error("Overlapping airport tap selected the hidden dot.");
                        await page.getByRole("button", { name: "Close popup", exact: true }).click();
                    }
                    await page.waitForTimeout(50);
                    await page.evaluate(() => {
                        window.electronicLogbookNavigation?.scrollMainToTop();
                        window.scrollTo(0, 0);
                    });

                    const shellLayout = await page.evaluate(() => {
                        const main = document.querySelector(".app-main");
                        const navigation = document.querySelector(".bottom-nav");
                        const heading = document.querySelector("main h1");
                        const navigationLinks = [...document.querySelectorAll(".bottom-nav a")];
                        const isVisible = element => {
                            const bounds = element.getBoundingClientRect();
                            const style = getComputedStyle(element);
                            return element.checkVisibility() &&
                                element.getClientRects().length > 0 &&
                                bounds.width > 1 &&
                                bounds.height > 1 &&
                                style.visibility !== "hidden";
                        };
                        const describe = element => ({
                            element: element.tagName.toLowerCase(),
                            name: element.getAttribute("aria-label") ??
                                element.getAttribute("title") ??
                                (element.textContent ?? "").trim().slice(0, 60)
                        });
                        const overlaps = (first, second) =>
                            first.left < second.right - 1 &&
                            first.right > second.left + 1 &&
                            first.top < second.bottom - 1 &&
                            first.bottom > second.top + 1;
                        const visibleControls = [...document.querySelectorAll(
                            'button, a[href], input:not([type="hidden"]), select, textarea, [role="button"], [role="checkbox"], [role="radio"], [role="switch"], [role="link"]')]
                            .filter(isVisible);
                        const unnamedControls = visibleControls
                            .filter(element => {
                                const labels = "labels" in element ? [...element.labels] : [];
                                return !element.getAttribute("aria-label") &&
                                    !element.getAttribute("aria-labelledby") &&
                                    labels.length === 0 &&
                                    !(element.textContent ?? "").trim() &&
                                    !element.getAttribute("title") &&
                                    !(element instanceof HTMLInputElement && element.value.trim());
                            })
                            .map(element => element.outerHTML.slice(0, 120));
                        const smallControlTargets = visibleControls
                            .filter(element => {
                                const target = element.matches('input[type="checkbox"], input[type="radio"]')
                                    ? element.closest("label") ?? element
                                    : element;
                                const bounds = target.getBoundingClientRect();
                                return bounds.width < 48 || bounds.height < 48;
                            })
                            .map(element => {
                                const target = element.matches('input[type="checkbox"], input[type="radio"]')
                                    ? element.closest("label") ?? element
                                    : element;
                                const bounds = target.getBoundingClientRect();
                                return {
                                    name: element.getAttribute("aria-label") ??
                                        element.getAttribute("title") ??
                                        (target.textContent ?? "").trim().slice(0, 60) ??
                                        element.tagName,
                                    width: Math.round(bounds.width),
                                    height: Math.round(bounds.height)
                                };
                            });
                        const clippedEssentialText = [...document.querySelectorAll(
                            "main h1, main h2, main h3, main label, main legend, main button, main a[href], main summary, main output, main [role=status], .bottom-nav a > span:last-child")]
                            .filter(isVisible)
                            .filter(element => (element.textContent ?? "").trim())
                            .filter(element => {
                                const style = getComputedStyle(element);
                                const clipsOverflow = ["hidden", "clip"].includes(style.overflowX) ||
                                    ["hidden", "clip"].includes(style.overflowY) ||
                                    style.textOverflow === "ellipsis";
                                return clipsOverflow &&
                                    (element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1);
                            })
                            .map(describe);
                        const overlappingLabels = [...document.querySelectorAll("main label")]
                            .filter(isVisible)
                            .flatMap(label => {
                                const control = label.control;
                                if (!control || !label.contains(control) || !isVisible(control)) {
                                    return [];
                                }

                                const controlBounds = control.getBoundingClientRect();
                                return [...label.childNodes]
                                    .filter(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim())
                                    .flatMap(node => {
                                        const range = document.createRange();
                                        range.selectNodeContents(node);
                                        return [...range.getClientRects()]
                                            .filter(bounds => overlaps(bounds, controlBounds))
                                            .map(() => describe(label));
                                    });
                            });
                        const entryHeaderContent = [...document.querySelectorAll(
                            ".entry-action-header h1, .entry-action-header .entry-draft-status, .entry-action-header button")]
                            .filter(isVisible);
                        const overlappingEntryHeader = entryHeaderContent.flatMap((element, index) =>
                            entryHeaderContent.slice(index + 1)
                                .filter(other => overlaps(element.getBoundingClientRect(), other.getBoundingClientRect()))
                                .map(other => ({ first: describe(element), second: describe(other) })));
                        const overlappingSectionTitles = [...document.querySelectorAll(".section-title-row")]
                            .flatMap(row => {
                                const children = [...row.children].filter(isVisible);
                                return children.flatMap((element, index) => children.slice(index + 1)
                                    .filter(other => overlaps(element.getBoundingClientRect(), other.getBoundingClientRect()))
                                    .map(other => ({ first: describe(element), second: describe(other) })));
                            });
                        const currencyTextLayout = [...document.querySelectorAll(
                            ".currency-overview-item, .currency-category-panel > summary")]
                            .filter(isVisible)
                            .flatMap(container => {
                                const bounds = container.getBoundingClientRect();
                                const text = [...container.querySelectorAll(":scope > strong, :scope > span")]
                                    .filter(isVisible)
                                    .map(element => {
                                        const range = document.createRange();
                                        range.selectNodeContents(element);
                                        return { element, rects: [...range.getClientRects()] };
                                    });
                                return text.flatMap(({ element, rects }, index) => [
                                    ...rects.filter(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1 ||
                                        rect.top < bounds.top - 1 || rect.bottom > bounds.bottom + 1)
                                        .map(() => ({ clipped: describe(element) })),
                                    ...text.slice(index + 1)
                                        .filter(other => rects.some(rect => other.rects.some(otherRect => overlaps(rect, otherRect))))
                                        .map(other => ({ first: describe(element), second: describe(other.element) }))
                                ]);
                            });
                        const dashboardCurrencyRows = [...document.querySelectorAll(".dashboard-currency-overview")]
                            .flatMap(overview => {
                                const items = [...overview.querySelectorAll(".currency-overview-item")];
                                const rows = Object.values(Object.groupBy(items,
                                    item => Math.round(item.getBoundingClientRect().top)));
                                const splitItem = items.some(item => {
                                    const bounds = [...item.children].map(child => child.getBoundingClientRect());
                                    return Math.max(...bounds.map(rect => rect.top)) >= Math.min(...bounds.map(rect => rect.bottom));
                                });
                                return rows.some(row => row.length === 2) || splitItem ? [describe(overview)] : [];
                            });
                        const shellContent = () => main
                            ? [...main.querySelectorAll("h1, h2, h3, p, label, legend, button, a[href], summary, output")].filter(isVisible)
                            : [];
                        const topbarBounds = document.querySelector(".app-topbar")?.getBoundingClientRect();
                        const shellOccludedContent = topbarBounds
                            ? shellContent().filter(element => overlaps(element.getBoundingClientRect(), topbarBounds)).map(describe)
                            : [];
                        const originalMainScrollTop = main?.scrollTop ?? 0;
                        if (main) {
                            main.scrollTop = main.scrollHeight;
                        }
                        const navigationBounds = navigation?.getBoundingClientRect();
                        if (navigationBounds) {
                            shellOccludedContent.push(...shellContent()
                                .filter(element => overlaps(element.getBoundingClientRect(), navigationBounds))
                                .map(describe));
                        }
                        if (!main) {
                            shellOccludedContent.push({ element: "main", name: "missing" });
                        }
                        if (main) {
                            main.scrollTop = originalMainScrollTop;
                        }
                        const switchGeometry = [...document.querySelectorAll(".custom-entry-number-format .mud-switch")]
                            .map(element => {
                                const span = element.querySelector(".mud-switch-span")?.getBoundingClientRect();
                                const input = element.querySelector(".mud-switch-input")?.getBoundingClientRect();
                                const thumb = element.querySelector('[class*="mud-switch-thumb"]')?.getBoundingClientRect();
                                const track = element.querySelector(".mud-switch-track")?.getBoundingClientRect();
                                return {
                                    controlHeight: Math.round(element.getBoundingClientRect().height),
                                    spanHeight: Math.round(span?.height ?? -1),
                                    inputHeight: Math.round(input?.height ?? -1),
                                    thumbTrackOffset: Math.round(Math.abs(
                                        ((thumb?.top ?? 0) + (thumb?.height ?? 0) / 2) -
                                        ((track?.top ?? 0) + (track?.height ?? 0) / 2)))
                                };
                            });

                        const dashboardHourLayout = [...document.querySelectorAll(
                            ".dashboard-hours-total > strong, .dashboard-hours-total > small strong, .dashboard-hours-period > span, .dashboard-experience-legend strong, .dashboard-last-flight-hours strong")]
                            .filter(isVisible)
                            .flatMap(element => {
                                const range = document.createRange();
                                range.selectNodeContents(element);
                                const lines = [...range.getClientRects()];
                                const bounds = range.getBoundingClientRect();
                                const container = element.closest("a, li")?.getBoundingClientRect();
                                const icon = element.parentElement.querySelector(":scope > .mud-icon-root")?.getBoundingClientRect();
                                return lines.length > 1 ||
                                    (container && (bounds.left < container.left - 1 || bounds.right > container.right + 1)) ||
                                    (icon && overlaps(bounds, icon))
                                    ? [describe(element)] : [];
                            });
                        return {
                            documentHorizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1 ||
                                document.body.scrollWidth > document.body.clientWidth + 1 ||
                                window.scrollX !== 0,
                            mainHorizontalOverflow: main ? main.scrollWidth > main.clientWidth + 1 : true,
                            mainOverflowElements: main && main.scrollWidth > main.clientWidth + 1
                                ? [...main.querySelectorAll("*")].filter(isVisible)
                                    .filter(element => element.scrollWidth > element.clientWidth + 1)
                                    .slice(0, 16).map(element => ({
                                        ...describe(element),
                                        className: element.getAttribute("class"),
                                        width: element.clientWidth,
                                        scrollWidth: element.scrollWidth
                                    })) : [],
                            mainScrollTop: main?.scrollTop ?? -1,
                            windowScrollY: window.scrollY,
                            headingTop: heading?.getBoundingClientRect().top ?? -1,
                            mainTop: main?.getBoundingClientRect().top ?? -1,
                            navigationHorizontalOverflow: navigation ? navigation.scrollWidth > navigation.clientWidth + 1 : true,
                            navigationLabelOverflow: navigationLinks
                                .map(link => link.querySelector("span:last-child"))
                                .filter(label => label && label.scrollWidth > label.clientWidth + 1)
                                .map(label => label.textContent?.trim()),
                            smallNavigationTargets: navigationLinks
                                .filter(link => {
                                    const bounds = link.getBoundingClientRect();
                                    return bounds.width < 48 || bounds.height < 48;
                                })
                                .map(link => link.getAttribute("aria-label")),
                            headingCount: document.querySelectorAll("main h1").length,
                            unnamedControls,
                            smallControlTargets,
                            clippedEssentialText,
                            overlappingLabels,
                            overlappingEntryHeader,
                            overlappingSectionTitles,
                            currencyTextLayout,
                            dashboardCurrencyRows,
                            shellOccludedContent,
                            dashboardHourLayout,
                            switchGeometry
                        };
                    });

                    const contractFailures = [];
                    if (shellLayout.documentHorizontalOverflow ||
                        shellLayout.mainHorizontalOverflow ||
                        shellLayout.mainScrollTop !== 0 ||
                        shellLayout.windowScrollY !== 0 ||
                        shellLayout.headingTop < shellLayout.mainTop ||
                        shellLayout.navigationHorizontalOverflow ||
                        shellLayout.navigationLabelOverflow.length > 0 ||
                        shellLayout.smallNavigationTargets.length > 0 ||
                        shellLayout.headingCount !== 1 ||
                        shellLayout.unnamedControls.length > 0 ||
                        shellLayout.smallControlTargets.length > 0 ||
                        shellLayout.clippedEssentialText.length > 0 ||
                        shellLayout.overlappingLabels.length > 0 ||
                        shellLayout.overlappingEntryHeader.length > 0 ||
                        shellLayout.overlappingSectionTitles.length > 0 ||
                        shellLayout.currencyTextLayout.length > 0 ||
                        shellLayout.dashboardCurrencyRows.length > 0 ||
                        shellLayout.shellOccludedContent.length > 0 ||
                        shellLayout.dashboardHourLayout.length > 0 ||
                        shellLayout.switchGeometry.some(switchLayout =>
                            switchLayout.inputHeight > switchLayout.spanHeight + 1 ||
                            switchLayout.thumbTrackOffset > 1)) {
                        contractFailures.push(`Shell accessibility/layout audit failed: ${JSON.stringify(shellLayout)}`);
                    }

                    try {
                        await assertAccessible(page, `${route.name} ${profile.name} ${colorScheme}`);
                    } catch (error) {
                        contractFailures.push(error.message);
                    }

                    if (contractFailures.length > 0) {
                        throw new Error(contractFailures.join(" | "));
                    }

                    const errorUi = page.locator("#blazor-error-ui");
                    if (await errorUi.isVisible()) {
                        throw new Error(`Blazor error UI is visible for ${route.name} ${profile.name} ${colorScheme}: ${await errorUi.innerText()}`);
                    }

                    await page.screenshot({
                        path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}.png`)
                    });
                    if (route.state === "analytics" || route.state === "analytics-design") {
                        if (!route.stats) {
                            await page.locator(".analytics-plot[data-chart-ready=true]").first().waitFor({ state: "visible" });
                        }
                        const tabLayout = await page.locator(".analytics-tabs [role=tablist]").evaluate(element => {
                            const tabs = [...element.querySelectorAll("[role=tab]")].map(tab => tab.getBoundingClientRect());
                            const bar = element.closest(".mud-tabs-tabbar-content").getBoundingClientRect();
                            const outer = element.closest(".mud-tabs-tabbar");
                            const style = getComputedStyle(outer);
                            const availableWidth = outer.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                            return { count: tabs.length, gap: bar.right - tabs.at(-1).right,
                                firstGap: tabs[0].left - bar.left, between: tabs[1].left - tabs[0].right,
                                widths: tabs.map(tab => tab.width), barWidth: bar.width, availableWidth };
                        });
                        if (tabLayout.count !== 2 || Math.abs(tabLayout.barWidth - tabLayout.availableWidth) > 1 || Math.abs(tabLayout.gap) > 1 || Math.abs(tabLayout.firstGap) > 1 ||
                            tabLayout.widths.some(width => Math.abs(width - (tabLayout.barWidth - tabLayout.between) / 2) > 1)) {
                            throw new Error(`Charts/Stats tabs do not evenly fill the bar: ${JSON.stringify(tabLayout)}`);
                        }
                        const sections = page.locator(route.stats ? ".analytics-statistics" : ".analytics-chart");
                        if (route.stats) {
                            await page.locator(".analytics-bases > summary").click();
                            const recent = page.locator(".analytics-base-suggestions");
                            await recent.locator("summary").click();
                            const input = page.locator("#analytics-base-code");
                            if (await input.getAttribute("list") !== null || await page.locator("datalist").count() !== 0) {
                                throw new Error("Base airports still use a native suggestion popup");
                            }
                            await input.focus();
                            await recent.scrollIntoViewIfNeeded();
                            const choices = await recent.locator("button").evaluateAll(buttons => buttons.map(button => ({
                                height: button.getBoundingClientRect().height,
                                position: getComputedStyle(button).position,
                                overflow: button.scrollWidth > button.clientWidth + 1
                            })));
                            if (choices.length === 0 || choices.some(choice => choice.height < 48 || choice.overflow || !["static", "relative"].includes(choice.position))) {
                                throw new Error(`Recent base airports must remain readable touch targets in the page: ${JSON.stringify(choices)}`);
                            }
                            await assertAccessible(page, `base airport choices ${profile.name} ${colorScheme}`);
                            await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-base-airports.png`) });
                            await recent.locator("button").first().click();
                            await page.locator(".analytics-base-list li").waitFor({ state: "visible" });
                            if (await page.locator(".analytics-base-list li").count() !== 1) throw new Error("Recent airport was not saved");
                            await page.locator(".analytics-base-list button").click();
                            await page.locator(".analytics-base-list li").waitFor({ state: "hidden" });
                            await page.locator(".analytics-bases > summary").click();
                        }
                        for (let index = 0; index < await sections.count(); index++) {
                            await sections.nth(index).scrollIntoViewIfNeeded();
                            await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-section${index + 1}.png`) });
                        }
                        if (!route.stats) {
                            if (route.state === "analytics-design") {
                                const year = page.locator("[aria-labelledby=hours-year-heading] .analytics-plot");
                                await year.scrollIntoViewIfNeeded();
                                await year.evaluate(element => {
                                    const chart = window.echarts.getInstanceByDom(element);
                                    const index = chart.getOption().xAxis[0].data.indexOf("2019");
                                    chart.dispatchAction({ type: "showTip", seriesIndex: chart.getOption().series.length - 1, dataIndex: index });
                                });
                                await page.locator("[aria-labelledby=hours-year-heading] .analytics-tooltip strong").waitFor({ state: "visible" });
                                await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-year-tooltip.png`) });
                                await year.evaluate(element => window.echarts.getInstanceByDom(element).dispatchAction({ type: "hideTip" }));
                                await page.evaluate(async () => {
                                    await navigator.serviceWorker.ready;
                                    const cached = await caches.match(new URL("js/vendor/echarts.min.js", document.baseURI));
                                    if (!cached) throw new Error("Chart engine is missing from the offline cache.");
                                });
                                await context.setOffline(true);
                            }
                            const pie = page.locator("[aria-labelledby=hours-category-heading]");
                            await pie.scrollIntoViewIfNeeded();
                            await pie.locator(".analytics-plot").evaluate(element => {
                                window.echarts.getInstanceByDom(element).dispatchAction({ type: "showTip", seriesIndex: 0, dataIndex: 0 });
                            });
                            const tooltip = pie.locator(".analytics-tooltip strong");
                            await tooltip.waitFor({ state: "visible" });
                            if (!/^\d+\.\d$/.test((await tooltip.textContent()).trim())) {
                                throw new Error(`Pie tooltip exposes unformatted hours: ${await tooltip.textContent()}`);
                            }
                            await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-tooltip.png`) });
                            await pie.locator(".analytics-plot").evaluate(element => window.echarts.getInstanceByDom(element).dispatchAction({ type: "hideTip" }));
                        }
                        const filters = page.locator(".analytics-filter-panel");
                        await page.getByRole("button", { name: "Filters", exact: true }).click();
                        await filters.scrollIntoViewIfNeeded();
                        const fieldStyle = await page.locator("#analytics-from").evaluate(element => getComputedStyle(element).backgroundColor);
                        const plotCount = await page.locator(".analytics-plot[data-chart-ready=true]").count();
                        if (fieldStyle === "rgb(255, 255, 255)" && colorScheme === "dark" || plotCount !== 6) {
                            throw new Error(`Analytics charts did not render: date=${fieldStyle}, plots=${plotCount}`);
                        }
                        await assertAccessible(page, `analytics expanded filters ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-filters.png`) });
                        for (const [selector, values] of [
                            [".analytics-type-filter", ["C172", "C208"]],
                            [".analytics-registration-filter", ["VH-UMV", "VH-NMV"]]
                        ]) {
                            await page.locator(`${selector} .mud-input-control-input-container`).click();
                            for (const value of values) {
                                await page.locator(".mud-popover-open").getByText(value, { exact: true }).click();
                            }
                            await page.locator("#charts-heading").click();
                            const summary = await page.locator(".analytics-filter-summary").innerText();
                            if (values.some(value => !summary.includes(value))) {
                                throw new Error(`Multiple selections missing from analytics summary: ${summary}`);
                            }
                        }
                        await assertAccessible(page, `analytics multiple selections ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-multiple-filters.png`) });
                        await page.locator(".analytics-filter-summary button").click();
                        if (route.state === "analytics-design") await context.setOffline(false);
                    }

                    if (route.name.startsWith("dashboard")) {
                        const dashboardOverview = page.locator(".dashboard-currency-overview");
                        if (await dashboardOverview.locator(".currency-overview-item").count() !== 3) {
                            throw new Error(`Dashboard currency snapshot did not render three status totals for ${profile.name} ${colorScheme}`);
                        }

                        const dashboardOverviewText = await dashboardOverview.innerText();
                        await page.goto(`${baseUrl}/currency`, { waitUntil: "domcontentloaded" });
                        const currencyOverview = page.locator("main .currency-overview");
                        await currencyOverview.waitFor({ state: "visible", timeout: 30000 });
                        const currencyOverviewText = await currencyOverview.innerText();

                        if (dashboardOverviewText !== currencyOverviewText) {
                            throw new Error(`Dashboard currency snapshot did not match the Currency header totals for ${profile.name} ${colorScheme}`);
                        }

                        if (route.name === "dashboard-populated") {
                            await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
                            await page.locator("main .dashboard-last-flight-link").waitFor({ state: "visible", timeout: 30000 });
                            await applyTextScale(page, profile.fontScale);
                            await page.waitForTimeout(50);

                            const populatedLastFlight = page.locator(".dashboard-last-flight-link");
                            const lastFlightLayout = await populatedLastFlight.evaluate((card) => {
                                const body = card.querySelector(".dashboard-last-flight-body").getBoundingClientRect();
                                const bodyTextOverflow = [...card.querySelectorAll(
                                    ".dashboard-last-flight-label, time, .relative-date, .dashboard-last-flight-route, .dashboard-last-flight-aircraft")]
                                    .some(element => {
                                        const range = document.createRange();
                                        range.selectNodeContents(element);
                                        return [...range.getClientRects()].some(bounds => bounds.left < body.left - 1 || bounds.right > body.right + 1);
                                    });
                                const hoursElement = card.querySelector(".dashboard-last-flight-hours");
                                const hours = hoursElement.getBoundingClientRect();
                                const style = getComputedStyle(card);
                                const cardBounds = card.getBoundingClientRect();
                                const hoursCenter = (hours.left + parseFloat(getComputedStyle(hoursElement).borderLeftWidth) +
                                    cardBounds.right - parseFloat(style.borderRightWidth)) / 2;
                                const hoursCenterOffsets = [...hoursElement.children].map(element => {
                                    const bounds = element.getBoundingClientRect();
                                    return (bounds.left + bounds.right) / 2 - hoursCenter;
                                });
                                return {
                                    nestedLogbookRow: card.querySelector(".logbook-entry-row") !== null,
                                    cardOverflow: card.scrollWidth > card.clientWidth + 1,
                                    pageOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
                                    fieldsOverlap: body.right > hours.left + 1,
                                    bodyTextOverflow,
                                    hoursCenterOffsets,
                                    paddingInline: Math.min(parseFloat(style.paddingLeft),
                                        parseFloat(style.paddingRight) + parseFloat(getComputedStyle(hoursElement).paddingRight))
                                };
                            });

                            if (lastFlightLayout.nestedLogbookRow ||
                                lastFlightLayout.cardOverflow ||
                                lastFlightLayout.pageOverflow ||
                                lastFlightLayout.fieldsOverlap ||
                                lastFlightLayout.bodyTextOverflow ||
                                lastFlightLayout.hoursCenterOffsets.some(offset => Math.abs(offset) > 1) ||
                                lastFlightLayout.paddingInline < 16) {
                                throw new Error(`Populated Dashboard Last Flight layout failed for ${profile.name} ${colorScheme}: ${JSON.stringify(lastFlightLayout)}`);
                            }

                            await populatedLastFlight.screenshot({
                                path: join(outputDirectory, `${profile.name}-${colorScheme}-last-flight-card.png`)
                            });
                            await assertAccessible(page, `dashboard populated ${profile.name} ${colorScheme}`);
                        }
                    } else if (route.name === "currency") {
                        const currencyOverview = page.locator(".currency-overview");
                        const categoryPanels = page.locator("details.currency-category-panel");
                        const licencePanel = categoryPanels.filter({ hasText: "Licence" }).first();
                        const licenceSummary = licencePanel.locator("summary");
                        const engineSwitch = licencePanel.locator(".currency-licence-engine-switch");
                        const overviewBefore = await currencyOverview.innerText();
                        const nonLicenceBefore = await categoryPanels.evaluateAll(panels =>
                            panels.slice(1).map(panel => panel.textContent));

                        if (!await engineSwitch.isVisible()) {
                            throw new Error(`Currency engine selector was not visible in the expanded Licence panel for ${profile.name} ${colorScheme}`);
                        }
                        await licenceSummary.click();
                        if (await licencePanel.evaluate(panel => panel.open) || await engineSwitch.isVisible()) {
                            throw new Error(`Currency engine selector remained visible when Licence was collapsed for ${profile.name} ${colorScheme}`);
                        }
                        await licenceSummary.click();
                        if (!await licencePanel.evaluate(panel => panel.open) || !await engineSwitch.isVisible()) {
                            throw new Error(`Currency engine selector did not return when Licence was expanded for ${profile.name} ${colorScheme}`);
                        }

                        const multiEngine = page.getByRole("button", { name: "Multi engine" });
                        await multiEngine.hover();
                        await page.mouse.down();
                        const pressedHeaderStyle = await licenceSummary.evaluate(summary => {
                            const style = getComputedStyle(summary);
                            return { filter: style.filter, opacity: style.opacity };
                        });
                        await page.mouse.up();
                        if (pressedHeaderStyle.filter !== "none" || pressedHeaderStyle.opacity !== "1") {
                            throw new Error(`Currency engine selector activated the Licence header styling for ${profile.name} ${colorScheme}`);
                        }
                        await page.waitForTimeout(100);
                        if (await categoryPanels.count() !== 4) {
                            throw new Error(`Currency Multi engine selection hid non-Licence categories for ${profile.name} ${colorScheme}`);
                        }
                        if (await currencyOverview.innerText() !== overviewBefore) {
                            throw new Error(`Currency Multi engine selection changed the overview for ${profile.name} ${colorScheme}`);
                        }

                        const nonLicenceAfter = await categoryPanels.evaluateAll(panels =>
                            panels.slice(1).map(panel => panel.textContent));
                        if (JSON.stringify(nonLicenceAfter) !== JSON.stringify(nonLicenceBefore)) {
                            throw new Error(`Currency Multi engine selection changed a non-Licence category for ${profile.name} ${colorScheme}`);
                        }

                        const singleEngine = page.getByRole("button", { name: "Single engine" });
                        await singleEngine.click();
                        await page.waitForTimeout(100);
                        if (await page.locator("details.currency-category-panel").count() !== 4) {
                            throw new Error(`Currency Single engine switch did not activate for ${profile.name} ${colorScheme}`);
                        }

                        const passengerPanel = page.locator("details.currency-category-panel")
                            .filter({ hasText: "Passenger carrying" });
                        await passengerPanel.locator("summary").click();
                        if (!await passengerPanel.evaluate(panel => panel.open)) {
                            throw new Error(`Currency category did not expand for ${profile.name} ${colorScheme}`);
                        }

                        await assertAccessible(page, `currency interaction ${profile.name} ${colorScheme}`);
                    } else if (route.name === "logbook-totals") {
                        await page.getByRole("button", { name: "Filters", exact: true }).click();
                        await page.locator(".date-range-fields").waitFor({ state: "visible" });
                        await assertAccessible(page, `totals expanded filters ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-filters.png`) });
                    } else if (route.path === "/settings") {
                        if (await page.locator(".accent-option").count() !== 6 || await page.locator('.accent-option[data-accent="rose"], .accent-option[data-accent="plum"]').count())
                            throw new Error("Removed accent choices are still available.");
                        for (const accent of ["Rose", "Plum"]) {
                            const fallback = await page.evaluate(accent => {
                                window.electronicLogbookUiPreferences.save("route-map-audit-preferences", `Dark|${accent}`);
                                return { accent: document.documentElement.dataset.elbAccent, saved: localStorage.getItem("route-map-audit-preferences") };
                            }, accent);
                            if (fallback.accent !== "forest" || fallback.saved !== "Dark|Forest") throw new Error(`Removed accent fallback failed: ${accent}`);
                        }
                        await page.evaluate(() => window.electronicLogbookUiPreferences.applyTheme("System|Forest"));
                        const privacy = page.locator("details.settings-privacy");
                        await privacy.evaluate(element => element.open = true);
                        if (!(await privacy.innerText()).includes("viewed map area") ||
                            await privacy.locator('a[href="https://openfreemap.org/privacy/"]').count() !== 1 ||
                            await privacy.locator('a[href="https://openfreemap.org/tos/"]').count() !== 1) throw new Error("Map privacy disclosure missing.");
                        await privacy.scrollIntoViewIfNeeded();
                        await assertAccessible(page, `map privacy ${profile.name} ${colorScheme}`);
                        await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-settings-privacy.png`) });
                        await privacy.evaluate(element => element.open = false);
                        const appearance = page.getByRole("group", { name: "Appearance", exact: true });
                        const originalTheme = await appearance.locator("button[aria-pressed=true]").innerText();
                        for (const theme of ["Light", "Dark", "System"]) {
                            await appearance.getByRole("button", { name: theme, exact: true }).click();
                            await page.waitForFunction(expected => {
                                const selected = document.querySelectorAll('[aria-label="Appearance"] button[aria-pressed=true]');
                                return selected.length === 1 && selected[0].textContent.trim() === expected;
                            }, theme);
                        }
                        await appearance.getByRole("button", { name: originalTheme.trim(), exact: true }).click();
                        if (route.expanded) {
                            for (const [name, value] of [["Invited account email", "audit@example.invalid"], ["Six-digit sign-in code", "123456"]]) {
                                const input = page.getByLabel(name, { exact: true });
                                await input.fill(value);
                                const labelLayout = await input.evaluate(control => {
                                    const label = control.labels[0];
                                    return {
                                        clipped: label.scrollWidth > label.clientWidth + 1 || label.scrollHeight > label.clientHeight + 1,
                                        overlapsInput: label.getBoundingClientRect().bottom > control.getBoundingClientRect().top + 1
                                    };
                                });
                                if (labelLayout.clipped || labelLayout.overlapsInput) {
                                    throw new Error(`Focused Settings label failed: ${name} ${JSON.stringify(labelLayout)}`);
                                }
                                await input.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-${name.startsWith("Invited") ? "email" : "code"}-input.png`) });
                            }
                            await page.screenshot({ path: join(outputDirectory, `${profile.name}-${colorScheme}-settings-fallback.png`) });
                            await assertAccessible(page, `expanded settings ${profile.name} ${colorScheme}`);
                        }
                        const firstCustomEntry = page.locator(".custom-entry-setting").first();
                        await firstCustomEntry.scrollIntoViewIfNeeded();
                        const numberFormatPill = firstCustomEntry.locator(".custom-entry-number-format-toggle");

                        if (await numberFormatPill.count() > 0) {
                            const wholeNumbers = numberFormatPill.getByRole("checkbox", { name: "Whole numbers" });
                            const decimals = numberFormatPill.getByRole("checkbox", { name: "Decimals" });
                            const target = await wholeNumbers.getAttribute("aria-checked") === "true" ? decimals : wholeNumbers;
                            await page.evaluate(() => {
                                const groups = [...document.querySelectorAll(".custom-entry-number-format-toggle")];
                                window.__numberFormatFrames = [];
                                const deadline = performance.now() + 500;
                                const capture = () => {
                                    window.__numberFormatFrames.push(groups.map(group => [...group.querySelectorAll("button")]
                                        .map(button => {
                                            const style = getComputedStyle(button);
                                            return {
                                                disabled: button.disabled,
                                                backgroundColor: style.backgroundColor,
                                                color: style.color,
                                                opacity: style.opacity
                                            };
                                        })));
                                    if (performance.now() < deadline) {
                                        requestAnimationFrame(capture);
                                    }
                                };
                                capture();
                            });
                            await target.click();
                            await page.waitForTimeout(550);
                            const numberFormatFrames = await page.evaluate(() => window.__numberFormatFrames);
                            const initialUnchangedGroups = JSON.stringify(numberFormatFrames[0].slice(1));
                            const controlsFlashed = numberFormatFrames.some(frame =>
                                frame.some(group => group.some(control => control.disabled)) ||
                                JSON.stringify(frame.slice(1)) !== initialUnchangedGroups);
                            if (await target.getAttribute("aria-checked") !== "true") {
                                throw new Error(`Settings number-format pill did not change selection for ${profile.name} ${colorScheme}`);
                            }
                            if (controlsFlashed) {
                                throw new Error(`Settings number-format pills flashed or unrelated pills changed for ${profile.name} ${colorScheme}`);
                            }
                        } else {
                            const numberFormatSwitch = firstCustomEntry.locator(".mud-switch");
                            const thumb = numberFormatSwitch.locator('[class*="mud-switch-thumb"]');
                            const thumbBefore = await thumb.boundingBox();
                            await numberFormatSwitch.click();
                            await page.waitForTimeout(200);
                            const thumbAfter = await thumb.boundingBox();

                            if (await numberFormatSwitch.locator("input").getAttribute("aria-checked") !== "true" ||
                                !thumbBefore ||
                                !thumbAfter ||
                                thumbAfter.x <= thumbBefore.x + 10) {
                                throw new Error(`Settings number-format switch did not toggle for ${profile.name} ${colorScheme}`);
                            }
                        }

                        await firstCustomEntry.screenshot({
                            path: join(outputDirectory, `${profile.name}-${colorScheme}-settings-custom-entry-toggled.png`)
                        });
                    }

                    await page.waitForTimeout(50);
                    if (browserErrors.length > 0) {
                        throw new Error(`Browser errors for ${route.name} ${profile.name} ${colorScheme}: ${browserErrors.join(" | ")}`);
                    }
                    } catch (error) {
                        const message = `${route.name} ${profile.name} ${colorScheme}: ${error.message}`;
                        (profile.blocking ? failures : warnings).push(message);
                        await page.screenshot({
                            path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}-failed.png`)
                        }).catch(() => {});
                    } finally {
                        await context.close();
                    }
                }
            }
        }
    } finally {
        await browser.close();
    }
    if (mapNetworkLog) {
        const netlog = JSON.parse(await readFile(mapNetworkLog, "utf8"));
        const headerTypes = ["HTTP_TRANSACTION_SEND_REQUEST_HEADERS", "HTTP_TRANSACTION_HTTP2_SEND_REQUEST_HEADERS", "HTTP_TRANSACTION_QUIC_SEND_REQUEST_HEADERS"].map(name => netlog.constants.logEventTypes[name]);
        const sent = netlog.events.filter(event => headerTypes.includes(event.type) &&
            /(?:host:|:authority:) tiles\.openfreemap\.org/i.test((event.params?.headers ?? []).join("\n")));
        if (!sent.length || !sent.some(event => /\.pbf/.test(event.params.headers.join("\n")))) failures.push("Chromium network log did not capture detailed provider tile requests.");
        for (const event of sent) {
            const headers = event.params.headers.join("\n");
            if (!/(?:^|\n)(?::method: GET|GET \S+ HTTP\/)/.test(headers) || /^:path: .*\?/m.test(headers) ||
                /(?:^|\n)(?:authorization|cookie|apikey|referer):|VH-TEST|VH-PAC|ent_route_|rev_route_|YMMB|YSSY|NZAA|PHNL|must-never-be-sent/i.test(headers))
                failures.push("Private data in Chromium map request headers.");
        }
        await writeFile(join(outputDirectory, "map-sent-headers.json"), JSON.stringify(sent, null, 2));
        console.log(`Chromium wire inspection: ${sent.length} provider requests including worker tiles; no account headers, cookies, referrer or flight fixture data.`);
    }
} finally {
    await new Promise((resolveClose) => server.close(resolveClose));
}

const auditCount = selectedProfiles.length * selectedColorSchemes.length * selectedRoutes.length;
console.log(`Audited ${auditCount} route/state combinations in ${outputDirectory}`);
if (warnings.length > 0) {
    console.warn(`Visual audit found ${warnings.length} non-blocking tablet regression warning(s):\n${warnings.join("\n")}`);
}
if (failures.length > 0) {
    console.error(`Visual audit found ${failures.length} failure(s):\n${failures.join("\n")}`);
    process.exitCode = 1;
}
