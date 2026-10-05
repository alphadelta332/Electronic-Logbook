// Disposable, unscheduled deployment only. Never use this entry point in production.
import probe from "./worker.mjs";

const testEnv = {
  SUPABASE_URL: "https://abcdefghijklmnopqrst.supabase.co",
  SUPABASE_PUBLISHABLE_KEY: "sb_publishable_test",
};
const endpoint = testEnv.SUPABASE_URL + "/rest/v1/rpc/preview_activity_probe";
const body = { status: "ok", database_time: "2026-10-05T00:00:00Z" };
const cases = ["success", "transport", "status", "project", "body", "json", "invalid-target"];

export default {
  async fetch(request, env) {
    if (!env.TEST_TOKEN || request.headers.get("Authorization") !== "Bearer " + env.TEST_TOKEN) {
      return new Response(null, { status: 404 });
    }
    const name = new URL(request.url).pathname.slice(1);
    if (request.method !== "POST" || !cases.includes(name)) {
      return new Response(null, { status: 404 });
    }
    const originalFetch = globalThis.fetch;
    let calls = 0;
    // ponytail: global mock requires sequential test calls; use isolated deployments if parallelized.
    globalThis.fetch = async (...args) => {
      calls++;
      if (name === "invalid-target") return originalFetch(...args);
      if (name === "transport") throw new Error("synthetic private transport detail");
      return {
        status: name === "status" ? 503 : 200,
        url: name === "project" ? "https://wrong-project.invalid" : endpoint,
        json: async () => {
          if (name === "json") throw new Error("synthetic private body detail");
          return name === "body" ? { ...body, unexpected: "must-not-be-logged" } : body;
        },
      };
    };
    try {
      await probe.scheduled({}, testEnv);
      return new Response(null, { status: 204 });
    } finally {
      globalThis.fetch = originalFetch;
      console.log(JSON.stringify({ test_case: name, probe_calls: calls }));
    }
  },
};
