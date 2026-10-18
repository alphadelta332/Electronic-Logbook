import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { lstatSync, readFileSync, realpathSync } from 'node:fs';
import { resolve, sep } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { exportDatabase, exportSupervisedDatabase, sealArchive } from './archive.mjs';
import { postgres, withDatabaseSnapshot } from './postgres.mjs';
import { inspectSourceAccess } from './source-access.mjs';
import { validateAirport } from '../../supabase/functions/airport-catalog/catalog.mjs';

const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const json = value => Buffer.from(JSON.stringify(value));
const schemas = ['--schema=public', '--schema=auth', '--schema=storage', '--schema=supabase_migrations'];
const sourcePaths = ['supabase/config.toml', 'supabase/migrations', 'supabase/functions', 'supabase/activity-worker',
  'tools/Publish-AirportCatalog.ps1', 'tools/Test-AirportCatalog.ps1',
  'tools/hosted-backup', 'updater/src/ElectronicLogbook.Portable', 'versions.properties'];
const requiredSources = ['supabase/config.toml', 'supabase/functions/airport-catalog/index.ts',
  'supabase/activity-worker/worker.mjs', 'supabase/activity-worker/wrangler.toml', 'tools/Publish-AirportCatalog.ps1',
  'supabase/functions/recovery-envelope/index.ts', 'tools/hosted-backup/cli.mjs',
  'tools/hosted-backup/ReplayCheck/ReplayCheck.csproj',
  'updater/src/ElectronicLogbook.Portable/ElectronicLogbook.Portable.csproj', 'versions.properties'];

function git(root, args) {
  const result = spawnSync('git', ['-C', root, ...args], { windowsHide: true, timeout: 10000, maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, 'Reconstruction source inventory failed.');
  return result.stdout.toString('utf8').trim();
}

function sources(root) {
  const names = [...new Set(git(root, ['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', ...sourcePaths])
    .split('\0').filter(Boolean))].sort();
  assert.ok(requiredSources.every(name => names.includes(name)) && names.some(name => /^supabase\/migrations\/\d+.*\.sql$/.test(name)),
    'Required reconstruction sources are missing.');
  const files = {};
  let size = 0;
  for (const name of names) {
    assert.ok(/\.(sql|toml|ts|mjs|json|cs|csproj|ps1|properties)$/.test(name) &&
      !/(^|\/)(bin|obj|node_modules|\.env)(\/|$)|\.local\./.test(name), 'Unexpected reconstruction file; review before inclusion.');
    const path = resolve(root, name);
    const stat = lstatSync(path);
    assert.ok(stat.isFile() && !stat.isSymbolicLink() && realpathSync(path).startsWith(realpathSync(root) + sep) &&
      stat.size <= 2 * 1024 * 1024, 'Unsafe reconstruction source.');
    size += stat.size;
    assert.ok(size <= 24 * 1024 * 1024, 'Reconstruction sources exceed the size limit.');
    files['sources/' + name] = readFileSync(path);
  }
  return { files, revision: git(root, ['rev-parse', 'HEAD']),
    dirty: git(root, ['status', '--porcelain', '--untracked-files=all', '--', ...sourcePaths]).length > 0 };
}

const storageSql = `select jsonb_build_object(
  'buckets',coalesce((select jsonb_agg(to_jsonb(b) order by id) from storage.buckets b),'[]'::jsonb),
  'objects',coalesce((select jsonb_agg(jsonb_build_object('bucket',bucket_id,'name',name,'metadata',metadata)
    order by bucket_id,name) from storage.objects),'[]'::jsonb))`;
const migrationsSql = `select coalesce(jsonb_agg(to_jsonb(m) order by to_jsonb(m)::text),'[]'::jsonb)
  from supabase_migrations.schema_migrations m`;

// Hash every row, including duplicates; revision identities remain inside encryption.
export const tableManifestSql = `set local timezone='UTC'; set local datestyle='ISO, YMD';
select format($query$select json_build_object('table',%L,'rows',count(*),
  'sha256',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,'UTF8')),'hex'),''
    order by encode(sha256(convert_to(to_jsonb(t)::text,'UTF8')),'hex')),''),'UTF8')),'hex'),
  'revisions',case when %L in ('public.operations','public.configuration_revisions') then
    coalesce(jsonb_agg(jsonb_strip_nulls(jsonb_build_object('id',to_jsonb(t)->'id',
      'logbook',to_jsonb(t)->'logbook_id','revision',to_jsonb(t)->'revision',
      'operation',to_jsonb(t)->'operation_id','portableRevision',to_jsonb(t)->'portable_revision_id',
      'payloadHash',to_jsonb(t)->'payload_hash')) order by to_jsonb(t)::text),'[]'::jsonb) else null end)
  from %I.%I t;$query$, n.nspname||'.'||c.relname,n.nspname||'.'||c.relname,n.nspname,c.relname)
  from pg_class c join pg_namespace n on n.oid=c.relnamespace
  where n.nspname in ('public','auth','storage','supabase_migrations') and c.relkind in ('r','p')
    and not c.relispartition order by n.nspname,c.relname
\\gexec`;

function schemaBytes(connection, ownerSetup) {
  return Buffer.from(postgres(connection, 'pg_dump', ['--no-password', '--schema-only', ...schemas], undefined, ownerSetup)
    .toString('utf8').replace(/^\\(?:un)?restrict .*\r?\n/gm, ''));
}

async function publicObject(origin, path, limit) {
  const response = await fetch(`${origin}/storage/v1/object/public/airport-catalog/${path}?backup=${Date.now()}`, {
    headers: { 'cache-control': 'no-cache' }, redirect: 'error', signal: AbortSignal.timeout(30000)
  });
  assert.ok(response.ok && response.body, 'Airport object download failed.');
  const chunks = [];
  let size = 0;
  for await (const chunk of response.body) {
    size += chunk.length;
    assert.ok(size <= limit, 'Airport object exceeds size limit.');
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

async function airportFiles(origin, inventory) {
  assert.ok(inventory.buckets.length === 1 && inventory.buckets[0].id === 'airport-catalog' &&
    inventory.buckets[0].public === true, 'Unexpected Storage bucket; review its backup requirements.');
  assert.ok(inventory.objects.every(o => o.bucket === 'airport-catalog' &&
    /^(manifest\.json|seed\.json\.gz|snapshots\/[a-f0-9]{64}\.json\.gz)$/.test(o.name)),
  'Unexpected Storage object; review its backup requirements.');
  const manifestBytes = await publicObject(origin, 'manifest.json', 4096);
  const manifest = JSON.parse(manifestBytes);
  assert.ok(manifest.schemaVersion === 1 && /^\d{4}-\d{2}-\d{2}$/.test(manifest.cycle) &&
    Number.isFinite(Date.parse(manifest.publishedAt)) && /^[a-f0-9]{64}$/.test(manifest.sha256) &&
    manifest.path === `snapshots/${manifest.sha256}.json.gz` && Number.isInteger(manifest.count) && manifest.count >= 5000,
  'Invalid airport manifest.');
  const files = { 'reference/manifest.json': manifestBytes };
  for (const name of ['manifest.json', manifest.path, 'seed.json.gz']) {
    const object = inventory.objects.find(o => o.name === name);
    assert.ok(object, 'Airport object is absent from the database snapshot.');
    const bytes = name === 'manifest.json' ? manifestBytes : await publicObject(origin, name, 4 * 1024 * 1024);
    // Storage's content ETag binds downloaded bytes to the exported metadata, not a later publication.
    assert.ok(typeof object.metadata?.eTag === 'string' &&
      createHash('md5').update(bytes).digest('hex') === object.metadata.eTag.replaceAll('"', ''),
    'Airport bytes do not match the database snapshot.');
    if (name !== 'manifest.json') {
      const airports = JSON.parse(gunzipSync(bytes, { maxOutputLength: 8 * 1024 * 1024 }));
      assert.ok(Array.isArray(airports) && airports.length >= 5000 &&
        new Set(airports.map(a => validateAirport(a).i)).size === airports.length, 'Invalid airport catalogue.');
      if (name === manifest.path) assert.ok(digest(bytes) === manifest.sha256 && airports.length === manifest.count,
        'Airport catalogue digest/count mismatch.');
      files['reference/' + name] = bytes;
    }
  }
  return files;
}

export { sources as reconstructionSources, airportFiles };

export async function buildHostedArchive(connection, recipient, root) {
  if (connection.host !== '127.0.0.1' || connection.user === 'flightlogx_backup') inspectSourceAccess(connection);
  return buildArchive(connection, recipient, root, false);
}

export async function buildSupervisedArchive(connection, recipient, root) {
  return buildArchive(connection, recipient, root, true);
}

async function buildArchive(connection, recipient, root, supervised) {
  assert.ok(/^[a-z]{20}$/.test(connection.projectRef), 'Missing backup project identity.');
  const origin = connection.disposable === true && connection.host === '127.0.0.1'
    ? connection.fixtureObjectOrigin : `https://${connection.projectRef}.supabase.co`;
  assert.ok(origin === `https://${connection.projectRef}.supabase.co` ||
    (connection.disposable === true && /^http:\/\/127\.0\.0\.1:\d+$/.test(origin)), 'Invalid airport source.');
  const run = (executable, args, input) => postgres(connection, executable, args, input, supervised);
  const source = sources(root);
  let databaseFiles = {};
  let references = {};
  let before;
  let after;
  try {
    before = schemaBytes(connection, supervised);
    return await withDatabaseSnapshot(connection, `select json_build_object('snapshot',pg_export_snapshot(),
      'time',transaction_timestamp(),'serverVersion',current_setting('server_version'),
      'extensions',(select coalesce(json_agg(json_build_object('name',e.extname,'schema',n.nspname) order by e.extname),'[]'::json)
        from pg_extension e join pg_namespace n on n.oid=e.extnamespace where e.extname in ('pgcrypto','citext','uuid-ossp')),
      'migrations',(${migrationsSql}));
      ${storageSql};\n${tableManifestSql}`, async ([snapshot, inventory, ...tables]) => {
      assert.ok(tables.length > 0, 'Empty backup verification manifest.');
      references = await airportFiles(origin, inventory);
      databaseFiles = (supervised ? exportSupervisedDatabase : exportDatabase)(connection, snapshot.snapshot);
      after = schemaBytes(connection, supervised);
      assert.ok(before.equals(after), 'Schema changed during export; retry with stable migrations.');
      const storageAfter = JSON.parse(run('psql', ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1'], storageSql + ';'));
      assert.deepEqual(storageAfter, inventory, 'Storage metadata changed during export; retry.');
      assert.deepEqual(JSON.parse(run('psql', ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1'], migrationsSql + ';')),
        snapshot.migrations, 'Migration history changed during export; retry.');
      const current = sources(root);
      try {
        assert.equal(current.revision, source.revision, 'Source revision changed during export.');
        assert.deepEqual(Object.keys(current.files), Object.keys(source.files), 'Source inventory changed during export.');
        for (const name of Object.keys(source.files)) assert.ok(source.files[name].equals(current.files[name]),
          'Reconstruction source changed during export.');
      } finally { Object.values(current.files).forEach(bytes => bytes.fill(0)); }
      const manifest = { version: 1, accessMode: supervised ? 'supervised-owner' : 'restricted', ...snapshot, tables, storage: inventory, sources: {
        revision: source.revision, dirty: source.dirty,
        files: Object.entries(source.files).map(([name, bytes]) => ({ name, sha256: digest(bytes) })) },
        storageCoverage: 'Current manifest, referenced catalogue and seed bytes; prior content-addressed snapshots are superseded reference data.',
        limitations: 'Roles are sampled separately. Service credentials/Vault are excluded; jobs must be recreated disabled. Managed-service and Google recovery acceptance remains required.' };
      databaseFiles['verification.json'] = json(manifest);
      return sealArchive({ ...source.files, ...references, ...databaseFiles }, recipient);
    }, supervised);
  } finally {
    before?.fill(0); after?.fill(0);
    for (const files of [source.files, databaseFiles, references]) Object.values(files).forEach(bytes => bytes.fill(0));
  }
}
