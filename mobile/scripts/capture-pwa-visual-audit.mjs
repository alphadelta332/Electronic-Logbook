import { createReadStream } from "node:fs";
import { createServer } from "node:http";
import { access, mkdir, stat } from "node:fs/promises";
import { dirname, extname, join, normalize, relative, resolve, isAbsolute } from "node:path";
import { fileURLToPath } from "node:url";
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
    { name: "exchange", path: "/exchange", readySelector: "main .package-exchange-page" }
];

function optionValue(name, fallback) {
    const index = process.argv.indexOf(name);
    return index >= 0 && process.argv[index + 1] ? resolve(process.argv[index + 1]) : fallback;
}

if (process.argv.includes("--help")) {
    console.log("Usage: node scripts/capture-pwa-visual-audit.mjs [--publish-root <path>] [--output-dir <path>]");
    process.exit(0);
}

const publishRoot = optionValue("--publish-root", defaultPublishRoot);
const outputDirectory = optionValue("--output-dir", defaultOutputDirectory);

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

const server = await createStaticServer(publishRoot);
const address = server.address();
const baseUrl = `http://127.0.0.1:${address.port}`;
await mkdir(outputDirectory, { recursive: true });

try {
    const browser = await chromium.launch({ headless: true });
    try {
        for (const colorScheme of colorSchemes) {
            for (const profile of profiles) {
                for (const route of routes) {
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
                    }, profile.fontScale);

                    const errorUi = page.locator("#blazor-error-ui");
                    if (await errorUi.isVisible()) {
                        throw new Error(`Blazor error UI is visible for ${route.name} ${profile.name} ${colorScheme}: ${await errorUi.innerText()}`);
                    }

                    await page.screenshot({
                        path: join(outputDirectory, `${profile.name}-${colorScheme}-${route.name}.png`),
                        fullPage: true
                    });

                    if (route.name === "dashboard") {
                        const dashboardPanels = page.locator("details.dashboard-currency-panel");
                        if (await dashboardPanels.count() !== 2) {
                            throw new Error(`Dashboard currency overview did not render both panels for ${profile.name} ${colorScheme}`);
                        }

                        const vfrPanel = dashboardPanels.filter({ hasText: "VFR" });
                        await vfrPanel.locator("summary").click();
                        if (!await vfrPanel.evaluate(panel => panel.open)) {
                            throw new Error(`Dashboard VFR panel did not expand for ${profile.name} ${colorScheme}`);
                        }

                        await vfrPanel.locator("summary").click();
                        if (await vfrPanel.evaluate(panel => panel.open)) {
                            throw new Error(`Dashboard VFR panel did not collapse for ${profile.name} ${colorScheme}`);
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

console.log(`Captured ${profiles.length * colorSchemes.length * routes.length} route screenshots in ${outputDirectory}`);
