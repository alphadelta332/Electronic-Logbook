import { spawnSync } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { join } from 'node:path';
import { openArchive } from './archive.mjs';

function check(condition, message) { if (!condition) throw new Error(message); }

function run(connection, name, args, input) {
  check(connection.host === '127.0.0.1' && connection.port === 54322 && connection.user === 'postgres' &&
    typeof connection.password === 'string' && connection.password.length > 0,
  'Restore requires the disposable local Supabase endpoint.');
  const executable = name + (process.platform === 'win32' ? '.exe' : '');
  const result = spawnSync(connection.pgBin ? join(connection.pgBin, executable) : executable, args, {
    windowsHide: true, timeout: 120000, maxBuffer: 64 * 1024 * 1024, input,
    env: { ...process.env, PGHOST: connection.host, PGPORT: String(connection.port), PGUSER: connection.user,
      PGPASSWORD: connection.password, PGSSLMODE: 'disable', PGAPPNAME: 'flightlogx-disposable-restore' }
  });
  const defaultSchemaConflict = result.stderr?.toString('utf8').includes('schema "public" already exists');
  check(result.status === 0, defaultSchemaConflict ? 'Disposable restore conflicts with the default public schema.' :
    `Disposable ${name} failed; sensitive diagnostics omitted.`);
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
      `COMMENT ON DATABASE ${receipt.database} IS 'flightlogx-backup-restore:${receipt.token}';`);
    assertReceipt(connection, receipt);
    // Only the default empty schema of our newly created database is removed; no CASCADE.
    run(connection, 'psql', ['-X', '-w', '-v', 'ON_ERROR_STOP=1', '-d', receipt.database], 'DROP SCHEMA public;');
    run(connection, 'pg_restore', ['--no-password', '--exit-on-error', '--single-transaction', '--no-owner', '--no-privileges',
      '--dbname=' + receipt.database], files['database.dump']);
    assertReceipt(connection, receipt);
    return receipt;
  } catch (error) {
    // Retain a failed, identified disposable target for investigation; never delete a guessed target.
    if (created) throw new Error(`Disposable restoration failed in ${receipt.database}: ${error.message}`);
    throw error;
  } finally { Object.values(files).forEach(value => value.fill(0)); }
}
