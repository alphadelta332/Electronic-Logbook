import { createReadStream } from "node:fs";
import { createServer } from "node:http";
import { access, mkdir, stat } from "node:fs/promises";
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
    [".json", "application/json"],
    [".png", "image/png"],
    [".svg", "image/svg+xml"],
    [".wasm", "application/wasm"],
    [".webmanifest", "application/manifest+json"],
    [".woff2", "font/woff2"]
]);

const phoneWidths = [
    { name: "320x712", width: 320, height: 712 },
    { name: "360x800", width: 360, height: 800 },
    { name: "412x915", width: 412, height: 915 }
];
const phoneTextScales = [1.25, 1.5, 1.75, 2];
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
    { name: "charts", path: "/charts", readySelector: "main [aria-labelledby=\"charts-heading\"]" },
    { name: "routes", path: "/routes", readySelector: "main [aria-labelledby=\"routes-heading\"]" },
    { name: "exchange", path: "/exchange", readySelector: "main .package-exchange-page" },
    { name: "settings", path: "/settings", readySelector: "main .settings-page" },
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
    await page.getByLabel("Reg", { exact: true }).fill("VH-LNG");
    await page.getByLabel("Flight ID", { exact: true }).fill("QF1234");
    await page.getByLabel("PIC", { exact: true }).fill("Self");
    await page.getByLabel("From", { exact: true }).fill("YMMB");
    await page.getByLabel("To", { exact: true }).fill("YSSY");
    await page.getByLabel("SE command day", { exact: true }).fill("2.0");
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

async function prepareRouteState(page, baseUrl, state) {
    if (!state) {
        return {};
    }

    const entryId = await createDashboardFlight(page, baseUrl);
    if (state === "deleted") {
        await page.goto(`${baseUrl}/flights/${entryId}`, { waitUntil: "domcontentloaded" });
        await page.locator("main .flight-detail-page").waitFor({ state: "visible", timeout: 30000 });
        await page.getByRole("button", { name: "Delete flight", exact: true }).click();
        const deleteDialog = page.getByRole("dialog", { name: "Delete this flight?" });
        await deleteDialog.waitFor({ state: "visible", timeout: 30000 });
        await deleteDialog.getByRole("button", { name: "Delete flight", exact: true }).click();
        await page.locator("main .logbook-page").waitFor({ state: "visible", timeout: 30000 });
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

try {
    const browser = await chromium.launch({ headless: true });
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
                            browserErrors.push(message.text());
                        }
                    });
                    try {
                    const routeState = await prepareRouteState(page, baseUrl, route.state);
                    const routePath = typeof route.path === "function" ? route.path(routeState) : route.path;
                    await page.goto(`${baseUrl}${routePath}`, { waitUntil: "domcontentloaded" });
                    await page.locator(route.readySelector).waitFor({ state: "visible", timeout: 30000 });
                    await applyTextScale(page, profile.fontScale);
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
                            return element.getClientRects().length > 0 &&
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

                        return {
                            documentHorizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1 ||
                                document.body.scrollWidth > document.body.clientWidth + 1 ||
                                window.scrollX !== 0,
                            mainHorizontalOverflow: main ? main.scrollWidth > main.clientWidth + 1 : true,
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
                            shellOccludedContent,
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
                        shellLayout.shellOccludedContent.length > 0 ||
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
                                const hours = card.querySelector(".dashboard-last-flight-hours").getBoundingClientRect();
                                const style = getComputedStyle(card);
                                return {
                                    nestedLogbookRow: card.querySelector(".logbook-entry-row") !== null,
                                    cardOverflow: card.scrollWidth > card.clientWidth + 1,
                                    pageOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
                                    fieldsOverlap: body.right > hours.left + 1,
                                    paddingInline: Math.min(parseFloat(style.paddingLeft), parseFloat(style.paddingRight))
                                };
                            });

                            if (lastFlightLayout.nestedLogbookRow ||
                                lastFlightLayout.cardOverflow ||
                                lastFlightLayout.pageOverflow ||
                                lastFlightLayout.fieldsOverlap ||
                                lastFlightLayout.paddingInline < 16) {
                                throw new Error(`Populated Dashboard Last Flight layout failed for ${profile.name} ${colorScheme}: ${JSON.stringify(lastFlightLayout)}`);
                            }

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
                    } else if (route.name === "settings") {
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
