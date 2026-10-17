import { airacCycle } from '../functions/airport-catalog/catalog.mjs';

export async function publishAirports(env) {
  if (!/^https:\/\/[a-z]{20}\.supabase\.co$/.test(env.SUPABASE_URL) ||
      !/^[a-f0-9]{64}$/.test(env.AIRPORT_CATALOG_PUBLISH_TOKEN)) {
    throw new Error("Airport publisher configuration invalid");
  }
  const endpoint = env.SUPABASE_URL + "/functions/v1/airport-catalog";
  try {
    const response = await fetch(endpoint, {
      method: "POST",
      headers: { Authorization: "Bearer " + env.AIRPORT_CATALOG_PUBLISH_TOKEN, "Content-Type": "application/json" },
      body: "{}", redirect: "manual", signal: AbortSignal.timeout(120000),
    });
    if (response.status !== 200 || response.url !== endpoint) throw new Error();
    const body = await response.json();
    const fields = body?.status === "published" ? "count,cycle,sha256,status" : "count,cycle,status";
    if (!body || Array.isArray(body) || Object.keys(body).sort().join(",") !== fields ||
        !["current", "published"].includes(body.status) || body.cycle !== airacCycle() ||
        !Number.isInteger(body.count) || body.count < 5000 ||
        (body.status === "published" && !/^[a-f0-9]{64}$/.test(body.sha256))) throw new Error();
    console.log(JSON.stringify({ airport_status: body.status, cycle: body.cycle, count: body.count }));
  } catch {
    throw new Error("Airport publisher request failed; previous catalogue retained");
  }
}

export default {
  async scheduled(event, env) {
    // The original four-hour timer remains an independent activity probe.
    if (event.cron && event.cron !== "17 */4 * * *") return publishAirports(env);
    if (!/^https:\/\/[a-z]{20}\.supabase\.co$/.test(env.SUPABASE_URL) ||
        !/^sb_publishable_[A-Za-z0-9_-]+$/.test(env.SUPABASE_PUBLISHABLE_KEY)) {
      throw new Error("Invalid activity probe configuration");
    }

    const endpoint = env.SUPABASE_URL + "/rest/v1/rpc/preview_activity_probe";
    let response;
    try {
      response = await fetch(endpoint, {
        method: "POST",
        headers: { apikey: env.SUPABASE_PUBLISHABLE_KEY, "Content-Type": "application/json" },
        body: "{}",
        redirect: "manual",
        signal: AbortSignal.timeout(20000),
      });
    } catch {
      throw new Error("Activity probe transport failed");
    }
    if (response.status !== 200 || response.url !== endpoint) {
      throw new Error("Activity probe HTTP status or project mismatch");
    }

    let body;
    try {
      body = await response.json();
    } catch {
      throw new Error("Activity probe returned invalid JSON");
    }
    if (!body || Array.isArray(body) || Object.keys(body).sort().join(",") !== "database_time,status" ||
        body.status !== "ok" || typeof body.database_time !== "string" ||
        !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,6})?(Z|[+-]\d{2}:\d{2})$/.test(body.database_time) ||
        !Number.isFinite(Date.parse(body.database_time))) {
      throw new Error("Activity probe response contract mismatch");
    }
    console.log(JSON.stringify({ status: "ok", database_time: body.database_time }));
  },
};
