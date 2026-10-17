import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, isAbsolute } from 'node:path';
import { parseArgs } from 'node:util';
import { randomBytes } from 'node:crypto';
import { initializeSourceAccess } from './source-access.mjs';

try {
  const { values } = parseArgs({ options: { ca: { type: 'string' } } });
  if (!values.ca || !isAbsolute(values.ca)) throw new Error('An explicit trusted CA file is required.');
  const root = join(process.env.LOCALAPPDATA, 'ElectronicLogbook');
  const directory = join(root, 'HostedBackup');
  const configPath = join(directory, 'source-connection.local.json');
  const caPath = join(directory, 'source-root.crt');
  if (existsSync(configPath) || existsSync(caPath)) throw new Error('Source setup already exists.');
  const metadata = JSON.parse(readFileSync(join(root, 'Supabase/hosted-preview-projects.local.json'), 'utf8')).preview;
  const password = randomBytes(32).toString('hex');
  const owner = { host: 'aws-0-ap-southeast-2.pooler.supabase.com', port: 5432, database: 'postgres',
    user: `postgres.${metadata.project_ref}`, password: metadata.db_password, projectRef: metadata.project_ref,
    sslRootCert: values.ca, pgBin: process.platform === 'win32' ? 'C:/Program Files/PostgreSQL/17/bin' : undefined };
  initializeSourceAccess(owner, password);
  mkdirSync(directory, { recursive: true });
  writeFileSync(caPath, readFileSync(values.ca), { flag: 'wx' });
  writeFileSync(configPath, JSON.stringify({ ...owner, user: `flightlogx_backup.${owner.projectRef}`, password,
    sslRootCert: caPath }, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  console.log('Restricted source identity provisioned and local configuration saved. Live export checks are still required.');
} catch {
  console.error('Source setup failed; existing credentials were not replaced. A partially provisioned role requires owner inspection. No secrets printed.');
  process.exitCode = 1;
}
