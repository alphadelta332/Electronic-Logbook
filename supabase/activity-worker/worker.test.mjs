import assert from "node:assert/strict";
import worker from "./worker.mjs";
import deploymentTest from "./worker.deployment-test.mjs";

const env = {
  SUPABASE_URL: "https://abcdefghijklmnopqrst.supabase.co",
  SUPABASE_PUBLISHABLE_KEY: "sb_publishable_test",
};
const endpoint = env.SUPABASE_URL + "/rest/v1/rpc/preview_activity_probe";
const success = { status: "ok", database_time: "2026-10-05T01:17:00.123456+00:00" };
const originalFetch = globalThis.fetch;
const originalLog = console.log;
let calls = 0;
let logs = [];

function reply(body = success, status = 200, url = endpoint) {
  return { status, url, json: async () => body };
}

try {
  console.log = (value) => logs.push(value);
  globalThis.fetch = async (url, options) => {
    calls++;
    if (options.redirect === "error") throw new TypeError("Cloudflare rejects redirect:error; use manual and check status");
    assert.equal(url, endpoint);
    assert.equal(options.method, "POST");
    assert.deepEqual(options.headers, { apikey: env.SUPABASE_PUBLISHABLE_KEY, "Content-Type": "application/json" });
    assert.equal(options.body, "{}");
    assert.equal(options.redirect, "manual");
    assert.ok(options.signal instanceof AbortSignal);
    return reply();
  };
  await worker.scheduled({}, env);
  assert.equal(calls, 1);
  assert.deepEqual(logs, [JSON.stringify(success)]);

  for (const invalid of [
    { ...env, SUPABASE_URL: "http://abcdefghijklmnopqrst.supabase.co" },
    { ...env, SUPABASE_URL: env.SUPABASE_URL + "@evil.example" },
    { ...env, SUPABASE_URL: env.SUPABASE_URL + "/" },
    { ...env, SUPABASE_PUBLISHABLE_KEY: "sb_secret_test" },
    { ...env, SUPABASE_PUBLISHABLE_KEY: "eyJservice_role" },
    {},
  ]) {
    await assert.rejects(worker.scheduled({}, invalid), /configuration/);
  }
  assert.equal(calls, 1, "invalid configuration must make no request");

  for (const response of [
    reply(success, 401), reply(success, 201), reply(success, 302),
    reply(success, 200, "https://different-project.supabase.co"),
    reply(null), reply([]), reply({ status: "ok" }),
    reply({ ...success, participant: "must-not-be-logged" }),
    reply({ ...success, status: "failed" }),
    reply({ ...success, database_time: "not a timestamp" }),
    reply({ ...success, database_time: 123 }),
    reply({ ...success, database_time: "2026-99-05T01:17:00Z" }),
    { status: 200, url: endpoint, json: async () => { throw new Error("private response"); } },
  ]) {
    globalThis.fetch = async () => { calls++; return response; };
    const before = calls;
    await assert.rejects(worker.scheduled({}, env), /Activity probe/);
    assert.equal(calls, before + 1, "failure must not retry");
  }
  globalThis.fetch = async () => { calls++; throw new Error("private URL and credential"); };
  const before = calls;
  await assert.rejects(worker.scheduled({}, env), { message: "Activity probe transport failed" });
  assert.equal(calls, before + 1);
  assert.deepEqual(logs, [JSON.stringify(success)], "failures must not log response data");
} finally {
  globalThis.fetch = originalFetch;
  console.log = originalLog;
}
console.log("Activity Worker success, isolation, redaction, and failure checks passed.");

try {
  logs = [];
  console.log = (value) => logs.push(JSON.parse(value));
  globalThis.fetch = async () => { throw new Error("test must not make an external request"); };
  const testFetch = globalThis.fetch;
  const invoke = (name, token = "test-token", method = "POST") => deploymentTest.fetch(
    new Request("https://test.invalid/" + name, { method, headers: { Authorization: "Bearer " + token } }),
    { TEST_TOKEN: "test-token" });
  assert.equal((await invoke("success", "wrong-token")).status, 404);
  assert.equal((await invoke("success", "test-token", "GET")).status, 404);
  assert.equal((await invoke("unknown")).status, 404);
  assert.deepEqual(logs, []);
  assert.equal((await invoke("success")).status, 204);
  for (const [name, message] of [
    ["transport", "Activity probe transport failed"],
    ["status", "Activity probe HTTP status or project mismatch"],
    ["project", "Activity probe HTTP status or project mismatch"],
    ["body", "Activity probe response contract mismatch"],
    ["json", "Activity probe returned invalid JSON"],
    ["invalid-target", "Activity probe transport failed"],
  ]) {
    await assert.rejects(invoke(name), { message });
    assert.equal(globalThis.fetch, testFetch, "test wrapper must restore fetch after failure");
  }
  assert.deepEqual(logs.filter(log => log.test_case),
    ["success", "transport", "status", "project", "body", "json", "invalid-target"].map(test_case => ({ test_case, probe_calls: 1 })));
  assert.equal(logs.filter(log => log.status === "ok").length, 1);
  assert.ok(!JSON.stringify(logs).includes("private"));
  assert.ok(!JSON.stringify(logs).includes("must-not-be-logged"));
} finally {
  globalThis.fetch = originalFetch;
  console.log = originalLog;
}
console.log("Disposable deployment wrapper authorization, outcome, and one-call checks passed.");
