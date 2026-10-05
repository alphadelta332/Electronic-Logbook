export default {
  async scheduled(_event, env) {
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
