import { spawnSync } from 'node:child_process';
import { join } from 'node:path';

export function postgres(connection, executable, args, input, ownerSetup = false) {
  const { host, port, database, user, password, sslRootCert, projectRef, disposable } = connection;
  if (typeof host !== 'string' || typeof password !== 'string' || !password.length)
    throw new Error('Incomplete database export connection.');
  const localFixture = disposable === true && host === '127.0.0.1' && Number.isInteger(port) && port >= 1024 && port <= 65535 &&
    /^elb_backup_fixture_[a-f0-9]{32}$/.test(database);
  if (!localFixture && !(/^[a-z]{20}$/.test(projectRef) &&
    /^aws-[0-9]+-ap-southeast-2\.pooler\.supabase\.com$/.test(host) &&
    user === `${ownerSetup ? 'postgres' : 'flightlogx_backup'}.${projectRef}` &&
    database === 'postgres' && port === 5432 && typeof sslRootCert === 'string' && sslRootCert.length > 0))
    throw new Error('Unexpected database export target.');
  const executableName = executable + (process.platform === 'win32' ? '.exe' : '');
  const result = spawnSync(connection.pgBin ? join(connection.pgBin, executableName) : executableName, args, {
    windowsHide: true, timeout: 120000, maxBuffer: 64 * 1024 * 1024, input,
    env: { ...process.env, PGHOST: host, PGPORT: String(port), PGDATABASE: database, PGUSER: user,
      PGPASSWORD: password, PGSSLMODE: localFixture ? 'disable' : 'verify-full',
      PGSSLROOTCERT: sslRootCert ?? '', PGAPPNAME: 'flightlogx-backup-export', PGCONNECT_TIMEOUT: '15',
      PGOPTIONS: '-c default_transaction_read_only=on -c statement_timeout=120000' }
  });
  if (result.status !== 0) throw new Error(`Backup ${executable} failed; sensitive diagnostics omitted.`);
  return result.stdout;
}
