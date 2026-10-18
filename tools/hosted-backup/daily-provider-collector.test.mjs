import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { collectGithubEvidence, collectSupabaseEvidence, collectProviderEvidence } from './daily-provider-collector.mjs';

const now = Date.parse('2026-10-18T12:00:00Z');
const policy = JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url)));
const config = { version: 1, githubUser: 'alphadelta332', accountId: 'a'.repeat(32), bucket: policy.bucket,
  supabaseOrganization: 'fixture-org', projectRef: 'b'.repeat(20), policy,
  deploymentRevisions: ['airport-catalog', 'recovery-envelope'].map(slug => ({ slug, version: 1, updated_at: 123 })) };
const credentials = { github: 'github-' + 'g'.repeat(30), supabaseProject: 'sbp-' + 's'.repeat(30),
  supabaseOrganization: 'sbp-' + 'o'.repeat(30), cloudflare: 'cf-' + 'c'.repeat(30) };
function fixture(mutate = () => {}) {
  const zero = { objects: 0, payloadSize: 0, metadataSize: 0 };
  const values = {
    account: { login: config.githubUser, plan: { name: 'free' } },
    usage: { user: config.githubUser, timePeriod: { year: 2026, month: 10 }, usageItems: [
      { product: 'Actions', sku: 'actions_linux', grossQuantity: 3, unitType: 'minutes', netAmount: 0 },
      { product: 'Actions', sku: 'actions_windows', grossQuantity: 2, unitType: 'minutes', netAmount: 0 }] },
    organization: { id: config.supabaseOrganization, plan: 'free' },
    functions: config.deploymentRevisions.map(item => ({ ...item, status: 'ACTIVE' })),
    metrics: { standard: { published: { ...zero, objects: 3, payloadSize: 1500, metadataSize: 129 }, uploaded: zero },
      infrequentAccess: { published: zero, uploaded: zero } },
    bucket: { name: config.bucket, storage_class: 'Standard' }, lock: policy.lock, lifecycle: policy.lifecycle,
    managed: { enabled: false }, domains: { domains: [] },
    analytics: { data: { viewer: { accounts: [{ operations: [{ sum: { requests: 62 }, dimensions: { actionType: 'GetObject' } }],
      storage: [{ max: { payloadSize: 1800, metadataSize: 129, uploadCount: 0 },
        dimensions: { bucketName: config.bucket, datetime: '2026-10-18T11:45:00Z' } },
      { max: { payloadSize: 4000, metadataSize: 50, uploadCount: 0 },
        dimensions: { bucketName: 'other-deleted-bucket', datetime: '2026-10-01T12:00:00Z' } }] }] } } }
  };
  mutate(values);
  const calls = [];
  const request = async (url, options) => {
    calls.push({ url, options });
    assert.equal(options.redirect, 'error'); assert.equal(options.cache, 'no-store');
    assert.equal(options.headers.Authorization, 'Bearer ' + credentials[url.includes('github.com') ? 'github'
      : url.includes('/organizations/') ? 'supabaseOrganization' : url.includes('supabase.com') ? 'supabaseProject' : 'cloudflare']);
    assert.ok(Object.values(credentials).every(token => !url.includes(token)));
    let key;
    if (url.includes('github.com')) key = url.endsWith('/user') ? 'account' : 'usage';
    else if (url.endsWith('/functions')) key = 'functions';
    else if (url.includes('/organizations/')) key = 'organization';
    else if (url.endsWith('/graphql')) {
      key = 'analytics'; const body = JSON.parse(options.body);
      assert.equal(body.variables.from, '2026-10-01T00:00:00.000Z');
      assert.ok(!body.query.includes('bucketName:') && !body.query.includes('repository:'));
    } else key = url.endsWith('/domains/custom') ? 'domains' : url.endsWith('/domains/managed') ? 'managed'
      : url.endsWith('/metrics') ? 'metrics' : url.endsWith('/lock') ? 'lock' : url.endsWith('/lifecycle') ? 'lifecycle' : 'bucket';
    const value = values[key];
    return Response.json(['account', 'usage', 'organization', 'functions', 'analytics'].includes(key) ? value : { success: true, errors: [], result: value });
  };
  return { values, calls, request };
}

test('collector isolates provider credentials and records actual observations without inventing a permit or billing freshness', async () => {
  const f = fixture(); const result = await collectProviderEvidence(config, credentials, f.request, now);
  assert.equal(f.calls.length, 11); assert.deepEqual(result.github.actionsMinutes,
    [{ sku: 'actions_linux', grossQuantity: 3 }, { sku: 'actions_windows', grossQuantity: 2 }]);
  assert.equal(result.github.remainingAllowanceProved, false);
  assert.equal(result.r2.currentBytes, 1629); assert.equal(result.r2.sampledPeakBytes, 5979);
  assert.equal(result.r2.currentMeasurementAt, null); assert.equal(result.r2.monthlyBillingCoverageProved, false);
  assert.equal(result.status, 'collected-not-permitted');
  assert.ok(!JSON.stringify(result).includes(credentials.github));
  assert.ok(!Object.hasOwn(result, 'expiresAt')); assert.ok(!Object.hasOwn(result.supabase, 'egress'));
});

test('collector rejects account, period, paid/SKU, deployment, privacy, policy and incomplete/stale analytics drift', async t => {
  const cases = {
    'wrong account': v => v.account.login = 'other', 'missing plan': v => delete v.account.plan,
    'changed GitHub plan': v => v.account.plan.name = 'pro',
    'wrong user': v => v.usage.user = 'other', 'wrong month': v => v.usage.timePeriod.month = 9,
    'paid usage': v => v.usage.usageItems[0].netAmount = 0.01, 'unknown runner': v => v.usage.usageItems[0].sku = 'gpu',
    'missing units': v => delete v.usage.usageItems[0].unitType, 'missing organization': v => delete v.organization.id,
    'paid plan': v => v.organization.plan = 'pro', 'revision drift': v => v.functions[0].version++,
    'missing function': v => v.functions.pop(), 'inactive function': v => v.functions[0].status = 'REMOVED',
    'public bucket': v => v.managed.enabled = true, 'custom domain': v => v.domains.domains.push({ enabled: true }),
    'wrong storage class': v => v.bucket.storage_class = 'InfrequentAccess', 'lock drift': v => v.lock = { rules: [] },
    'missing storage class': v => { delete v.bucket.storage_class; v.bucket.storageClass = 'Standard'; },
    'missing storage field': v => delete v.metrics.standard.published.metadataSize,
    'infrequent access usage': v => v.metrics.infrequentAccess = { published: { objects: 1, payloadSize: 10, metadataSize: 0 }, uploaded: { objects: 0, payloadSize: 0, metadataSize: 0 } },
    'GraphQL error': v => v.analytics.errors = [{ message: 'denied' }],
    'empty account': v => v.analytics.data.viewer.accounts = [],
    'truncated operations': v => v.analytics.data.viewer.accounts[0].operations = Array(10000).fill({}),
    'missing history': v => v.analytics.data.viewer.accounts[0].storage = [],
    'future sample': v => v.analytics.data.viewer.accounts[0].storage[0].dimensions.datetime = '2026-10-19T00:00:00Z',
    'stale sample': v => v.analytics.data.viewer.accounts[0].storage[0].dimensions.datetime = '2026-10-18T09:00:00Z',
    'pending upload': v => v.analytics.data.viewer.accounts[0].storage[0].max.uploadCount = 1
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, async () => {
    await assert.rejects(collectProviderEvidence(config, credentials, fixture(mutate).request, now));
  });
});

test('GitHub collection can run independently without other provider credentials and never returns account profile data', async () => {
  const f = fixture(); const result = await collectGithubEvidence(config.githubUser, credentials.github, f.request, now);
  assert.equal(f.calls.length, 2); assert.ok(f.calls.every(call => call.url.startsWith('https://api.github.com/')));
  assert.equal(result.plan, 'free'); assert.equal(result.remainingAllowanceProved, false);
  assert.deepEqual(Object.keys(result).sort(), ['retrievedAt', 'plan', 'periodStart', 'actionsMinutes', 'remainingAllowanceProved'].sort());
});

test('Supabase separates organization and Preview credentials and accepts the documented organization id without a slug field', async () => {
  const f = fixture();
  const result = await collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseOrganization, f.request, now);
  assert.equal(f.calls.length, 2); assert.equal(result.plan, 'free'); assert.deepEqual(result.deploymentRevisions, config.deploymentRevisions);
  await assert.rejects(collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseProject, f.request, now), /separate/);
  await assert.rejects(collectSupabaseEvidence(config, credentials.supabaseProject, undefined, f.request, now));
  const denied = async (url, options) => url.includes('/organizations/') ? new Response('', { status: 403 }) : f.request(url, options);
  await assert.rejects(collectSupabaseEvidence(config, credentials.supabaseProject, credentials.supabaseOrganization, denied, now));
});

test('HTTP denials, redirects, pagination, malformed and oversized bodies do not yield evidence', async t => {
  for (const [name, response] of [
    ['denied', () => new Response('private error', { status: 403 })],
    ['redirect', () => new Response('', { status: 302 })],
    ['pagination', () => Response.json({}, { headers: { link: '<next>' } })],
    ['malformed', () => new Response('private invalid JSON')],
    ['oversized', () => new Response('x'.repeat(1024 * 1024 + 1))]
  ]) await t.test(name, async () => {
    await assert.rejects(collectProviderEvidence(config, credentials, async () => response(), now), error => !error.message.includes('private'));
  });
  await assert.rejects(collectProviderEvidence(config, { ...credentials, github: '' }, fixture().request, now));
});
