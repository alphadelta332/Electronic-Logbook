import { spawnSync } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { join } from 'node:path';
import { writeFileSync, unlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { openArchive } from './archive.mjs';

function check(condition, message) { if (!condition) throw new Error(message); }

function run(connection, name, args, input) {
  check(connection.host === '127.0.0.1' && [54322, 55432].includes(connection.port) && connection.user === 'postgres' &&
    typeof connection.password === 'string' && connection.password.length > 0,
  'Restore requires the disposable local Supabase endpoint.');
  const executable = name + (process.platform === 'win32' ? '.exe' : '');
  const result = spawnSync(connection.pgBin ? join(connection.pgBin, executable) : executable, args, {
    windowsHide: true, timeout: 120000, maxBuffer: 64 * 1024 * 1024, input,
    env: { ...process.env, PGHOST: connection.host, PGPORT: String(connection.port), PGUSER: connection.user,
      PGPASSWORD: connection.password, PGSSLMODE: 'disable', PGAPPNAME: 'flightlogx-disposable-restore',
      PGOPTIONS: name === 'pg_restore' ? '-c session_replication_role=replica' : '' }
  });
  const defaultSchemaConflict = result.stderr?.toString('utf8').includes('schema "public" already exists');
  if (result.status !== 0) {
    const hint = result.stderr?.toString('utf8').match(/(?:type|schema|role|extension|function|operator class|collation) "[A-Za-z0-9_. -]{1,100}" (?:does not exist|already exists)/)?.[0];
    throw Object.assign(new Error(defaultSchemaConflict ? 'Disposable restore conflicts with the default public schema.' :
      `Disposable ${name} failed; sensitive diagnostics omitted.`), { restoreFailureHint: hint });
  }
  return result.stdout;
}

export function readDisposable(connection, receipt, sql) {
  assertReceipt(connection, receipt);
  const result = run(connection, 'psql', ['-X', '-w', '-q', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-d', receipt.database],
    `BEGIN READ ONLY;\n${sql}\nCOMMIT;`);
  return result.toString('utf8').trim();
}

function assertReceipt(connection, receipt) {
  check(/^elb_backup_fixture_[a-f0-9]{32}$/.test(receipt.database) && /^[a-f0-9]{32}$/.test(receipt.token),
    'Invalid disposable restore receipt.');
  const comment = run(connection, 'psql', ['-X', '-w', '-q', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-d', 'postgres'],
    `BEGIN READ ONLY; SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname='${receipt.database}'; COMMIT;`).toString('utf8').trim();
  check(comment === 'flightlogx-backup-restore:' + receipt.token, 'Disposable restore identity does not match.');
}

export function dropDisposable(connection, receipt) {
  assertReceipt(connection, receipt);
  run(connection, 'psql', ['-X', '-w', '-v', 'ON_ERROR_STOP=1', '-d', 'postgres'], `DROP DATABASE ${receipt.database};`);
}

export function restoreDisposable(archive, identity, expectedSha256, connection) {
  check(typeof expectedSha256 === 'string' && /^[a-f0-9]{64}$/.test(expectedSha256) &&
    createHash('sha256').update(archive).digest('hex') === expectedSha256,
  'Backup does not match the trusted job digest.');
  // Authenticate completely before creating a database; public-key encryption is not source authentication.
  const files = openArchive(archive, identity);
  let receipt;
  let created = false;
  try {
    check(files['database.dump']?.subarray(0, 5).toString() === 'PGDMP' && files['roles.sql']?.length > 0,
      'Backup is missing database or role content.');
    receipt = { database: 'elb_backup_fixture_' + randomUUID().replaceAll('-', ''), token: randomUUID().replaceAll('-', '') };
    run(connection, 'psql', ['-X', '-w', '-v', 'ON_ERROR_STOP=1', '-d', 'postgres'], `CREATE DATABASE ${receipt.database} TEMPLATE template0;`);
    created = true;
    run(connection, 'psql', ['-X', '-w', '-v', 'ON_ERROR_STOP=1', '-d', 'postgres'],
      `COMMENT ON DATABASE ${receipt.database} IS 'flightlogx-backup-restore:${receipt.token}';
       REVOKE ALL ON DATABASE ${receipt.database} FROM PUBLIC;`);
    assertReceipt(connection, receipt);
    const contents = run(connection, 'pg_restore', ['--list'], files['database.dump']).toString('utf8');
    const prerequisites = files['verification.json'] ? JSON.parse(files['verification.json']).extensions ?? [] :
      ['pgcrypto', 'citext', 'uuid-ossp'].map(name => ({ name, schema: 'extensions' }));
    check(Array.isArray(prerequisites) && prerequisites.every(e => ['pgcrypto','citext','uuid-ossp'].includes(e.name) &&
      ['public','extensions'].includes(e.schema)), 'Unreviewed restore extension layout.');
    run(connection, 'psql', ['-X', '-w', '-v', 'ON_ERROR_STOP=1', '-d', receipt.database],
      ['public','auth','storage','supabase_migrations','extensions'].map(schema => `CREATE SCHEMA IF NOT EXISTS ${schema};`).join(' ') +
      prerequisites.map(e => `CREATE EXTENSION "${e.name}" WITH SCHEMA "${e.schema}";`).join(' '));
    // A metadata-only object list lets extensions use their original schemas before table/type restoration.
    const objectList = join(tmpdir(), 'elb-restore-' + randomUUID() + '.toc');
    writeFileSync(objectList, contents.split(/\r?\n/).filter(line =>
      !/^\d+;\s+\d+\s+\d+\s+SCHEMA - (public|auth|storage|supabase_migrations)\s/.test(line)).join('\n'), { flag: 'wx', mode: 0o600 });
    try {
      run(connection, 'pg_restore', ['--no-password', '--exit-on-error', '--single-transaction', '--no-owner', '--no-privileges',
        '--use-list=' + objectList, '--dbname=' + receipt.database], files['database.dump']);
    } finally { unlinkSync(objectList); }
    assertReceipt(connection, receipt);
    return receipt;
  } catch (error) {
    // Retain a failed, identified disposable target for investigation; never delete a guessed target.
    if (created) throw Object.assign(new Error(`Disposable restoration failed in ${receipt.database}: ${error.message}`),
      { restoreReceipt: receipt, restoreFailureHint: error.restoreFailureHint });
    throw error;
  } finally { Object.values(files).forEach(value => value.fill(0)); }
}
