import { createReadStream } from "node:fs";
import { createServer } from "node:http";
import { access, mkdir, stat } from "node:fs/promises";
import { dirname, extname, join, normalize, resolve } from "node:path";
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
            const insideRoot = requestedPath === root || requestedPath.startsWith(`${root}\\`);
            let filePath = insideRoot ? requestedPath : "";

            try {
                if (!filePath || !(await stat(filePath)).isFile()) {
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
                const context = await browser.newContext({
                    colorScheme,
                    viewport: { width: profile.width, height: profile.height }
                });
                const page = await context.newPage();
                await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
                await page.locator("main .dashboard-page").waitFor({ state: "visible", timeout: 30000 });
                await page.evaluate((fontScale) => {
                    document.documentElement.style.fontSize = `${fontScale * 100}%`;
                }, profile.fontScale);

                const errorUi = page.locator("#blazor-error-ui");
                if (await errorUi.isVisible()) {
                    throw new Error(`Blazor error UI is visible for ${profile.name} ${colorScheme}: ${await errorUi.innerText()}`);
                }

                await page.screenshot({
                    path: join(outputDirectory, `${profile.name}-${colorScheme}-dashboard.png`),
                    fullPage: true
                });
                await context.close();
            }
        }
    } finally {
        await browser.close();
    }
} finally {
    await new Promise((resolveClose) => server.close(resolveClose));
}

console.log(`Captured ${profiles.length * colorSchemes.length} dashboard screenshots in ${outputDirectory}`);
