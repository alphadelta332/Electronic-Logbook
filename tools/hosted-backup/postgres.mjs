import { spawn, spawnSync } from 'node:child_process';
import { join } from 'node:path';

function command(connection, executable, ownerSetup = false) {
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
  return { executable: connection.pgBin ? join(connection.pgBin, executableName) : executableName,
    env: { ...process.env, PGHOST: host, PGPORT: String(port), PGDATABASE: database, PGUSER: user,
      PGPASSWORD: password, PGSSLMODE: localFixture ? 'disable' : 'verify-full',
      PGSSLROOTCERT: sslRootCert ?? '', PGAPPNAME: 'flightlogx-backup-export', PGCONNECT_TIMEOUT: '15',
      PGOPTIONS: '-c default_transaction_read_only=on -c statement_timeout=120000 -c idle_in_transaction_session_timeout=180000' } };
}

export function postgres(connection, executable, args, input, ownerSetup = false) {
  const spec = command(connection, executable, ownerSetup);
  const result = spawnSync(spec.executable, args, {
    windowsHide: true, timeout: 120000, maxBuffer: 64 * 1024 * 1024, input, env: spec.env
  });
  if (result.status !== 0) throw new Error(`Backup ${executable} failed; sensitive diagnostics omitted.`);
  return result.stdout;
}

// Keep the exporting transaction alive until pg_dump has imported its snapshot.
export async function withDatabaseSnapshot(connection, sql, action) {
  const spec = command(connection, 'psql');
  const child = spawn(spec.executable, ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1'], {
    windowsHide: true, env: spec.env, stdio: ['pipe', 'pipe', 'ignore']
  });
  child.stdout.setEncoding('utf8');
  let output = '';
  let finished = false;
  const failure = new Error('Backup snapshot session failed; sensitive diagnostics omitted.');
  const closed = new Promise(resolve => child.once('close', resolve));
  try {
    const rows = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { child.kill(); reject(failure); }, 120000);
      const stop = error => { clearTimeout(timer); reject(error); };
      child.once('error', () => stop(failure));
      child.stdin.on('error', () => stop(failure));
      child.once('close', () => { if (!finished) stop(failure); });
      child.stdout.on('data', chunk => {
        output += chunk.toString('utf8');
        if (Buffer.byteLength(output) > 32 * 1024 * 1024) { child.kill(); stop(failure); }
        if (output.endsWith('\nBACKUP_SNAPSHOT_READY\n') || output.endsWith('\r\nBACKUP_SNAPSHOT_READY\r\n')) {
          clearTimeout(timer); finished = true;
          try { resolve(output.trim().split(/\r?\n/).slice(0, -1).map(line => JSON.parse(line))); }
          catch { stop(failure); }
          output = '';
        }
      });
      child.stdin.write(`BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;\n${sql}\n\\echo BACKUP_SNAPSHOT_READY\n`);
    });
    const result = await action(rows);
    child.stdin.end('ROLLBACK;\n');
    const timer = setTimeout(() => child.kill(), 5000);
    const code = await closed;
    clearTimeout(timer);
    if (code !== 0) throw failure;
    return result;
  } finally {
    output = '';
    if (child.exitCode === null) child.kill();
    await closed;
  }
}
