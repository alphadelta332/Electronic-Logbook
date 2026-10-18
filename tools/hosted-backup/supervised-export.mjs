import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { createHash, createPublicKey } from 'node:crypto';
import { join, isAbsolute, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { buildSupervisedArchive } from './orchestrate.mjs';

// Owner-operated local export only. This command is deliberately absent from the routine backup CLI.
try {
  assert.equal(process.platform, 'win32', 'Use the trusted Windows owner machine.');
  const { values } = parseArgs({ options: { ca: { type: 'string' }, output: { type: 'string' } } });
  assert.ok(values.ca && isAbsolute(values.ca) && values.output && isAbsolute(values.output));
  assert.ok(values.output.endsWith('.elbbackup') && !existsSync(values.output) && !existsSync(values.output + '.receipt.json'));
  const root = join(process.env.LOCALAPPDATA, 'ElectronicLogbook');
  const metadata = JSON.parse(readFileSync(join(root, 'Supabase/hosted-preview-projects.local.json'))).preview;
  const token = readFileSync(join(root, 'Supabase/access-token.txt'), 'utf8').trim();
  const response = await fetch(`https://api.supabase.com/v1/projects/${metadata.project_ref}`, {
    headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(30000), redirect: 'error'
  });
  assert.ok(response.ok); const project = await response.json();
  assert.ok(project.name === 'FlightLogX Preview' && project.region === 'ap-southeast-2' && project.status === 'ACTIVE_HEALTHY');
  const recipient = readFileSync(join(root, 'HostedBackup/recipient.pem'));
  const recovery = JSON.parse(readFileSync(join(root, 'HostedBackup/recovery-receipt.json')));
  const recipientSha256 = createHash('sha256').update(createPublicKey(recipient).export({ type: 'spki', format: 'der' })).digest('hex');
  assert.ok(recovery.independentReadbackPassed && recovery.recipientSha256 === recipientSha256);
  const connection = { host: 'aws-0-ap-southeast-2.pooler.supabase.com', port: 5432, database: 'postgres',
    user: `postgres.${metadata.project_ref}`, password: metadata.db_password, projectRef: metadata.project_ref,
    sslRootCert: values.ca, pgBin: 'C:/Program Files/PostgreSQL/17/bin' };
  const archive = await buildSupervisedArchive(connection, recipient, resolve(dirname(fileURLToPath(import.meta.url)), '../..'));
  try {
    const receipt = { version: 1, exportedAt: new Date().toISOString(), environment: 'preview', accessMode: 'supervised-owner',
      bytes: archive.length, sha256: createHash('sha256').update(archive).digest('hex'), recipientSha256,
      databaseRestoreVerified: false, managedServiceRecoveryVerified: false };
    writeFileSync(values.output, archive, { flag: 'wx' });
    writeFileSync(values.output + '.receipt.json', JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
    console.log(JSON.stringify(receipt));
  } finally { archive.fill(0); }
} catch {
  console.error('Supervised export failed. Existing output was preserved; no credentials, records or provider response printed. A file without its receipt is not accepted.');
  process.exitCode = 1;
}
