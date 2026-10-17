import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join } from 'node:path';
import { pathToFileURL } from 'node:url';

export function saveR2Credentials(file, credentials) {
  if (!isAbsolute(file) || !/^[a-f0-9]{32}$/.test(credentials.accountId) ||
      credentials.bucket !== 'flightlogx-preview-backups') throw new Error('Invalid R2 destination.');
  for (const access of [credentials.upload, credentials.restore]) {
    if (!access || !/^[a-f0-9]{32}$/.test(access.accessKeyId) ||
        !/^[a-f0-9]{64}$/.test(access.secretAccessKey)) throw new Error('Invalid R2 credential format.');
  }
  if (credentials.upload.accessKeyId === credentials.restore.accessKeyId ||
      credentials.upload.secretAccessKey === credentials.restore.secretAccessKey)
    throw new Error('Upload and restore must use different credentials.');
  const saved = { accountId: credentials.accountId, bucket: credentials.bucket,
    upload: { accessKeyId: credentials.upload.accessKeyId, secretAccessKey: credentials.upload.secretAccessKey },
    restore: { accessKeyId: credentials.restore.accessKeyId, secretAccessKey: credentials.restore.secretAccessKey } };
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, JSON.stringify(saved, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    let input = '';
    for await (const chunk of process.stdin) { input += chunk; if (input.length > 4096) throw new Error('Input too large.'); }
    saveR2Credentials(join(process.env.LOCALAPPDATA, 'ElectronicLogbook/HostedBackup/r2-credentials.local.json'), JSON.parse(input));
    console.log('R2 credentials saved locally. Live permission tests are still required.');
  } catch {
    console.error('Credentials were not saved: invalid input or destination already exists. No secrets printed.');
    process.exitCode = 1;
  }
}
