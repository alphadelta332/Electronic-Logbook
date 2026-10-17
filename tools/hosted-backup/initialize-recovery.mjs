import { createPrivateKey, createPublicKey, createHash } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { join, isAbsolute } from 'node:path';
import { pathToFileURL } from 'node:url';
import { sealArchive, openArchive } from './archive.mjs';

export function createRecoverySet({ directory, localDirectory, recipient, encryptedIdentity, passphrase, secretFiles }) {
  if (!isAbsolute(directory) || !isAbsolute(localDirectory) || typeof passphrase !== 'string' || passphrase.length < 20 ||
      !encryptedIdentity.startsWith('-----BEGIN ENCRYPTED PRIVATE KEY-----')) throw new Error('Invalid recovery setup input.');
  const identity = { key: encryptedIdentity, passphrase };
  const derived = createPublicKey(createPrivateKey(identity)).export({ type: 'spki', format: 'der' });
  if (!derived.equals(createPublicKey(recipient).export({ type: 'spki', format: 'der' })))
    throw new Error('Recovery identity does not match recipient.');
  const outputs = [join(directory, 'backup-private.encrypted.pem'), join(directory, 'recipient.pem'),
    join(directory, 'recovery.elbbackup'), join(localDirectory, 'recipient.pem'), join(localDirectory, 'recovery-receipt.json')];
  if (outputs.some(existsSync)) throw new Error('Recovery files already exist; refusing to replace the backup identity.');
  const archive = sealArchive(secretFiles, recipient);
  mkdirSync(directory, { recursive: true }); mkdirSync(localDirectory, { recursive: true });
  writeFileSync(outputs[0], encryptedIdentity, { flag: 'wx' });
  writeFileSync(outputs[1], recipient, { flag: 'wx' });
  writeFileSync(outputs[2], archive, { flag: 'wx' });
  const readback = openArchive(readFileSync(outputs[2]), { key: readFileSync(outputs[0]), passphrase });
  try {
    for (const [name, original] of Object.entries(secretFiles))
      if (!readback[name]?.equals(original)) throw new Error('Recovery file readback differs.');
  } finally { Object.values(readback).forEach(value => value.fill(0)); }
  writeFileSync(outputs[3], recipient, { flag: 'wx' });
  writeFileSync(outputs[4], JSON.stringify({ verifiedAt: new Date().toISOString(),
    recipientSha256: createHash('sha256').update(derived).digest('hex'),
    protectedIdentitySha256: createHash('sha256').update(readFileSync(outputs[0])).digest('hex'),
    recoveryArchiveSha256: createHash('sha256').update(readFileSync(outputs[2])).digest('hex'),
    independentReadbackPassed: true }, null, 2) + '\n', { flag: 'wx' });
  return { status: 'created-and-verified', offlineFiles: 3, recoveryContents: Object.keys(secretFiles).length,
    protectedPrivateIdentity: true, rawPrivateKeyWritten: false };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    let input = ''; for await (const chunk of process.stdin) { input += chunk; if (input.length > 65536) throw new Error('Setup input too large.'); }
    const options = JSON.parse(input);
    const root = join(process.env.LOCALAPPDATA, 'ElectronicLogbook');
    const recoveryPath = join(root, 'Supabase/recovery-envelope/private-pilot.env');
    const secret = readFileSync(recoveryPath);
    try {
      const result = createRecoverySet({ ...options, localDirectory: join(root, 'HostedBackup'),
        secretFiles: { 'preview-recovery.env': secret, 'recovery-instructions.txt': Buffer.from(
          'This recovery archive holds the existing Preview recovery service secrets.\n' +
          'Keep this USB copy disconnected and protect its private identity with the separately retained passphrase.\n' +
          'The public recipient alone is used by backup automation. Never upload the private identity, passphrase or recovery archive.\n' +
          'Restore the database archive and original managed recovery KEK/version together; Google sign-in alone does not decrypt flight data.\n') } });
      console.log(JSON.stringify(result));
    } finally { secret.fill(0); }
  } catch {
    console.error('Recovery setup failed. Existing files were not replaced. No password, key or recovery material was printed.');
    process.exitCode = 1;
  }
}
