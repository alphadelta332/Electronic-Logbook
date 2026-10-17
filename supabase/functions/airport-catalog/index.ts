import { CsvParseStream } from "jsr:@std/csv@1.0.6/parse-stream";
import { airacCycle, airportFromRow, mergeAirports, validateAirport } from "./catalog.mjs";

const bucket = "airport-catalog";
const source = "https://davidmegginson.github.io/ourairports-data/airports.csv";
const maxBytes = 32 * 1024 * 1024;

function bounded(response: Response, limit = maxBytes) {
    if (!response.ok || !response.body) throw new Error("Airport request failed");
    let total = 0;
    return response.body.pipeThrough(new TransformStream({
        transform(chunk, controller) {
            total += chunk.byteLength;
            if (total > limit) throw new Error("Airport response too large");
            controller.enqueue(chunk);
        },
    }));
}

async function bytes(response: Response, limit = maxBytes) {
    return new Uint8Array(await new Response(bounded(response, limit)).arrayBuffer());
}

async function digest(data: Uint8Array) {
    return [...new Uint8Array(await crypto.subtle.digest("SHA-256", data))].map(value => value.toString(16).padStart(2, "0")).join("");
}

Deno.serve(async request => {
    const url = Deno.env.get("SUPABASE_URL")!;
    const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
    const publisherToken = Deno.env.get("AIRPORT_CATALOG_PUBLISH_TOKEN");
    if (request.method !== "POST" || !publisherToken || !/^[a-f0-9]{64}$/.test(publisherToken) ||
        request.headers.get("authorization") !== `Bearer ${publisherToken}`)
        return new Response("Unauthorized", { status: 401 });
    const headers = { authorization: `Bearer ${key}`, apikey: key };
    const objectUrl = `${url}/storage/v1/object/${bucket}/`;
    const get = (path: string) => fetch(objectUrl + path, { headers, signal: AbortSignal.timeout(30000), redirect: "error" });
    const put = async (path: string, body: Uint8Array | string, cache: number, type: string) => {
        const response = await fetch(objectUrl + path, {
            method: "POST", headers: { ...headers, "content-type": type, "cache-control": `max-age=${cache}`, "x-upsert": "true" },
            body, signal: AbortSignal.timeout(30000), redirect: "error",
        });
        if (!response.ok) throw new Error("Airport publication failed");
    };
    try {
        const cycle = airacCycle();
        const manifestResponse = await get("manifest.json");
        let previous = null;
        if (manifestResponse.ok) previous = JSON.parse(new TextDecoder().decode(await bytes(manifestResponse, 4096)));
        else if (manifestResponse.status !== 400 && manifestResponse.status !== 404) throw new Error("Airport manifest unavailable");
        if (previous?.cycle === cycle) return Response.json({ status: "current", cycle, count: previous.count });

        const priorBytes = await bytes(await get(previous?.path ?? "seed.json.gz"), 4 * 1024 * 1024);
        if (previous && await digest(priorBytes) !== previous.sha256) throw new Error("Airport snapshot integrity failed");
        const priorJson = await bytes(new Response(new Blob([priorBytes]).stream().pipeThrough(new DecompressionStream("gzip"))), 8 * 1024 * 1024);
        const existing = JSON.parse(new TextDecoder().decode(priorJson));
        if (!Array.isArray(existing) || existing.length < 5000) throw new Error("Airport seed is invalid");
        existing.forEach(validateAirport);

        const response = await fetch(source, { signal: AbortSignal.timeout(30000), redirect: "error" });
        const rows = bounded(response).pipeThrough(new TextDecoderStream()).pipeThrough(new CsvParseStream({ skipFirstRow: true }));
        const downloaded = new Map();
        let checkedHeaders = false;
        for await (const row of rows) {
            if (!checkedHeaders) {
                if (!["type", "scheduled_service", "icao_code", "gps_code", "ident", "name", "latitude_deg", "longitude_deg"].every(key => key in row))
                    throw new Error("Airport CSV columns changed");
                checkedHeaders = true;
            }
            const airport = airportFromRow(row);
            if (airport && !downloaded.has(airport.i)) downloaded.set(airport.i, airport);
        }
        if (downloaded.size < 5000) throw new Error("Airport download is incomplete");
        const airports = mergeAirports(existing, [...downloaded.values()]);
        const json = new TextEncoder().encode(JSON.stringify(airports));
        if (json.length > 8 * 1024 * 1024) throw new Error("Airport catalogue too large");
        const compressed = new Uint8Array(await new Response(new Blob([json]).stream().pipeThrough(new CompressionStream("gzip"))).arrayBuffer());
        const sha256 = await digest(compressed);
        const path = `snapshots/${sha256}.json.gz`;
        const manifest = { schemaVersion: 1, cycle, publishedAt: new Date().toISOString(), sha256, path, count: airports.length };
        await put(path, compressed, 31536000, "application/gzip");
        await put("manifest.json", JSON.stringify(manifest), 300, "application/json");
        return Response.json({ status: "published", cycle, count: airports.length, sha256 });
    } catch {
        console.error("Airport publication failed; previous catalogue retained.");
        return Response.json({ status: "failed" }, { status: 503 });
    }
});
