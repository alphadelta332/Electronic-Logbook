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

const profiles = [
    { name: "360", width: 360, height: 800, fontScale: 1 },
    { name: "pixel8-412x915", width: 412, height: 915, fontScale: 1 },
    { name: "large-text", width: 412, height: 915, fontScale: 1.25 },
    { name: "wide-768", width: 768, height: 1024, fontScale: 1 }
];
const colorSchemes = ["light", "dark"];
const routes = [
    { name: "dashboard", path: "/", readySelector: "main .dashboard-page" },
    { name: "currency", path: "/currency", readySelector: "main .currency-page" },
    { name: "logbook-entries", path: "/flights?view=entries", readySelector: "main .logbook-page" },
    { name: "logbook-totals", path: "/flights?view=totals", readySelector: "main .logbook-totals" },
    { name: "new-flight", path: "/flights/new", readySelector: "main .flight-entry-page" },
    { name: "charts", path: "/charts", readySelector: "main .future-feature-page" },
    { name: "routes", path: "/routes", readySelector: "main .future-feature-page" },
    { name: "exchange", path: "/exchange", readySelector: "main .package-exchange-page" },
    { name: "settings", path: "/settings", readySelector: "main .settings-page" }
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
    await page.locator("main .dashboard-last-flight-link").waitFor({ state: "visible", timeout: 30000 });
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

try {
    const browser = await chromium.launch({ headless: true });
    try {
        for (const colorScheme of selectedColorSchemes) {
            for (const profile of selectedProfiles) {
                for (const route of selectedRoutes) {
                    const context = await browser.newContext({
                        colorScheme,
                        viewport: { width: profile.width, height: profile.height }
                    });
                    const page = await context.newPage();
                    const browserErrors = [];
                    page.on("pageerror", error => browserErrors.push(error.message));
                    page.on("console", message => {
                        if (message.type() === "error") {
                            browserErrors.push(message.text());
                        }
                    });
                    await page.goto(`${baseUrl}${route.path}`, { waitUntil: "domcontentloaded" });
                    await page.locator(route.readySelector).waitFor({ state: "visible", timeout: 30000 });
                    await page.evaluate((fontScale) => {
                        document.documentElement.style.fontSize = `${fontScale * 100}%`;
                        const main = document.querySelector(".app-main");
                        if (main) {
                            main.scrollTop = 0;
                            main.scrollLeft = 0;
                        }
                        window.scrollTo(0, 0);
                    }, profile.fontScale);
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
                        const visibleControls = [...document.querySelectorAll("button, a[href], input, select, textarea")]
                            .filter(element => element.getClientRects().length > 0);
                        const unnamedControls = visibleControls
                            .filter(element => {
                                const labels = "labels" in element ? [...element.labels] : [];
                                return !element.getAttribute("aria-label") &&
                                    !element.getAttribute("aria-labelledby") &&
                                    labels.length === 0 &&
                                    !(element.textContent ?? "").trim() &&
                                    !element.getAttribute("title");
                            })
                            .map(element => element.outerHTML.slice(0, 120));
                        const smallControlTargets = visibleControls
                            .filter(element => {
                                const target = element.matches('input[type="checkbox"], input[type="radio"]')
                                    ? element.closest("label") ?? element
                                    : element;
                                const bounds = target.getBoundingClientRect();
                                return bounds.width < 44 || bounds.height < 44;
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

                        return {
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
                                    return bounds.width < 44 || bounds.height < 44;
                                })
                                .map(link => link.getAttribute("aria-label")),
                            headingCount: document.querySelectorAll("main h1").length,
                            unnamedControls,
                            smallControlTargets
                        };
                    });

                    if (shellLayout.mainHorizontalOverflow ||
                        shellLayout.mainScrollTop !== 0 ||
                        shellLayout.windowScrollY !== 0 ||
                        shellLayout.headingTop < shellLayout.mainTop ||
                        shellLayout.navigationHorizontalOverflow ||
                        shellLayout.navigationLabelOverflow.length > 0 ||
                        shellLayout.smallNavigationTargets.length > 0 ||
                        shellLayout.headingCount !== 1 ||
                        shellLayout.unnamedControls.length > 0 ||
                        shellLayout.smallControlTargets.length > 0) {
                        throw new Error(`Shell accessibility/layout audit failed for ${route.name} ${profile.name} ${colorScheme}: ${JSON.stringify(shellLayout)}`);
                    }

                    await assertAccessible(page, `${route.name} ${profile.name} ${colorScheme}`);

                    const errorUi = page.locator("#blazor-error-ui");
                    if (await errorUi.isVisible()) {
                        throw new Error(`Blazor error UI is visible for ${route.name} ${profile.name} ${colorScheme}: ${await errorUi.innerText()}`);
                    }

                    await page.screenshot({
                        path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}.png`)
                    });

                    if (route.name === "dashboard") {
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

                        await createDashboardFlight(page, baseUrl);
                        await page.evaluate((fontScale) => {
                            document.documentElement.style.fontSize = `${fontScale * 100}%`;
                            window.electronicLogbookNavigation?.scrollMainToTop();
                        }, profile.fontScale);
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

                        await page.screenshot({
                            path: join(outputDirectory, `${profile.name}-${colorScheme}-dashboard-populated.png`)
                        });
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
                    }

                    await page.waitForTimeout(50);
                    if (browserErrors.length > 0) {
                        throw new Error(`Browser errors for ${route.name} ${profile.name} ${colorScheme}: ${browserErrors.join(" | ")}`);
                    }
                    await context.close();
                }
            }
        }
    } finally {
        await browser.close();
    }
} finally {
    await new Promise((resolveClose) => server.close(resolveClose));
}

const populatedDashboardCount = selectedRoutes.some(route => route.name === "dashboard")
    ? selectedProfiles.length * selectedColorSchemes.length
    : 0;
console.log(`Captured ${selectedProfiles.length * selectedColorSchemes.length * selectedRoutes.length} route screenshots plus ${populatedDashboardCount} populated Dashboard screenshots in ${outputDirectory}`);
