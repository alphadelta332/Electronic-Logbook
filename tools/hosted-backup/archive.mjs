import { createCipheriv, createDecipheriv, createHash, createPrivateKey, createPublicKey,
  publicEncrypt, privateDecrypt, randomBytes, constants } from 'node:crypto';
import { gzipSync, gunzipSync } from 'node:zlib';
import { postgres } from './postgres.mjs';
import { inspectSourceAccess } from './source-access.mjs';

export const maxArchiveBytes = 50 * 1024 * 1024;
const maxPayloadBytes = 128 * 1024 * 1024;
const magic = Buffer.from('ELBBACK1');
const requiredTables = ['accounts', 'logbooks', 'logbook_memberships', 'devices',
  'operations', 'operation_acks', 'pairing_requests', 'key_envelopes', 'security_events',
  'workbook_migrations', 'configuration_revisions'];
const sha256 = value => createHash('sha256').update(value).digest('hex');

function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

function rsaKey(pem, privatePart = false) {
  const key = privatePart ? createPrivateKey(pem) : createPublicKey(pem);
  requireValue(key.asymmetricKeyType === 'rsa' && key.asymmetricKeyDetails.modulusLength >= 3072 &&
    key.asymmetricKeyDetails.modulusLength <= 8192, 'Backup identity requires RSA with 3072–8192 bits.');
  return key;
}

function validName(name) {
  return typeof name === 'string' && name.length <= 200 && /^[a-zA-Z0-9][a-zA-Z0-9._/-]*$/.test(name) &&
    !name.split('/').some(part => !part || part === '.' || part === '..');
}

export function sealArchive(files, recipient) {
  requireValue(files && Object.getPrototypeOf(files) === Object.prototype, 'Backup files must be a named object.');
  const names = Object.keys(files);
  requireValue(names.length > 0 && names.length <= 1000 && names.every(validName), 'Invalid backup file names.');
  let totalBytes = 0;
  for (const value of Object.values(files)) {
    requireValue(Buffer.isBuffer(value), 'Backup file contents must be bytes.');
    totalBytes += value.length;
  }
  requireValue(totalBytes <= 64 * 1024 * 1024, 'Backup contents exceed the permitted size.');
  const payload = Buffer.from(JSON.stringify({ version: 1, files: names.sort().map(name => ({
    name, sha256: sha256(files[name]), content: files[name].toString('base64')
  })) }));
  requireValue(payload.length <= maxPayloadBytes, 'Backup payload exceeds the permitted size.');
  const compressed = gzipSync(payload, { level: 9 });
  payload.fill(0);
  const key = randomBytes(32);
  try {
    const wrapped = publicEncrypt({ key: rsaKey(recipient), oaepHash: 'sha256', padding: constants.RSA_PKCS1_OAEP_PADDING }, key);
    const size = Buffer.alloc(2); size.writeUInt16BE(wrapped.length);
    const nonce = randomBytes(12);
    const header = Buffer.concat([magic, size, wrapped, nonce]);
    const cipher = createCipheriv('aes-256-gcm', key, nonce);
    cipher.setAAD(header);
    const ciphertext = Buffer.concat([cipher.update(compressed), cipher.final()]);
    const result = Buffer.concat([header, cipher.getAuthTag(), ciphertext]);
    requireValue(result.length <= maxArchiveBytes, 'Encrypted archive exceeds the permitted size.');
    return result;
  } finally { key.fill(0); compressed.fill(0); }
}

export function openArchive(archive, identity) {
  let key;
  let compressed;
  let payload;
  try {
    requireValue(Buffer.isBuffer(archive) && archive.length <= maxArchiveBytes && archive.length >= 10 + 384 + 12 + 16 + 1,
      'Invalid archive size.');
    requireValue(archive.subarray(0, 8).equals(magic), 'Unsupported archive format.');
    const wrappedSize = archive.readUInt16BE(8);
    requireValue(wrappedSize >= 384 && wrappedSize <= 1024, 'Invalid wrapped key size.');
    const nonceOffset = 10 + wrappedSize;
    const headerSize = nonceOffset + 12;
    requireValue(archive.length > headerSize + 16, 'Truncated archive.');
    key = privateDecrypt({ key: rsaKey(identity, true), oaepHash: 'sha256', padding: constants.RSA_PKCS1_OAEP_PADDING }, archive.subarray(10, nonceOffset));
    requireValue(key.length === 32, 'Invalid data key.');
    const cipher = createDecipheriv('aes-256-gcm', key, archive.subarray(nonceOffset, headerSize));
    cipher.setAAD(archive.subarray(0, headerSize));
    cipher.setAuthTag(archive.subarray(headerSize, headerSize + 16));
    compressed = Buffer.concat([cipher.update(archive.subarray(headerSize + 16)), cipher.final()]);
    payload = gunzipSync(compressed, { maxOutputLength: maxPayloadBytes });
    const document = JSON.parse(payload.toString('utf8'));
    requireValue(document.version === 1 && Array.isArray(document.files) && document.files.length > 0 && document.files.length <= 1000,
      'Invalid archive contents.');
    const files = {};
    let totalBytes = 0;
    for (const file of document.files) {
      requireValue(validName(file.name) && !Object.hasOwn(files, file.name) && typeof file.content === 'string' &&
        /^[a-zA-Z0-9+/]*={0,2}$/.test(file.content) && file.content.length % 4 === 0, 'Invalid archive file.');
      const bytes = Buffer.from(file.content, 'base64');
      requireValue(bytes.toString('base64') === file.content && sha256(bytes) === file.sha256, 'Archive file digest mismatch.');
      totalBytes += bytes.length;
      requireValue(totalBytes <= 64 * 1024 * 1024, 'Archive contents exceed the permitted size.');
      Object.defineProperty(files, file.name, { value: bytes, enumerable: true });
    }
    return files;
  } catch {
    // Do not expose provider responses, decrypted bytes, or key errors in diagnostics.
    throw new Error('Backup archive could not be authenticated and opened.');
  } finally { key?.fill(0); compressed?.fill(0); payload?.fill(0); }
}

export function exportDatabase(connection, snapshot) {
  requireValue(snapshot === undefined || /^[0-9A-Fa-f]+-[0-9A-Fa-f]+-[0-9]+$/.test(snapshot), 'Invalid export snapshot.');
  if (connection.host !== '127.0.0.1' || connection.user === 'flightlogx_backup') inspectSourceAccess(connection);
  // pg_dump uses one REPEATABLE READ, READ ONLY transaction for the data snapshot.
  const dump = postgres(connection, 'pg_dump', ['--no-password', '--format=custom', '--compress=9',
    '--lock-wait-timeout=10000', '--schema=public', '--schema=auth', '--schema=storage', '--schema=supabase_migrations',
    ...(snapshot ? ['--snapshot=' + snapshot] : [])]);
  try {
    const toc = postgres(connection, 'pg_restore', ['--list'], dump).toString('utf8');
    for (const table of requiredTables)
      requireValue(toc.includes(`TABLE DATA public ${table} `), `Backup is missing required table ${table}.`);
    for (const table of ['users', 'identities'])
      requireValue(toc.includes(`TABLE DATA auth ${table} `), `Backup is missing required Auth table ${table}.`);
    requireValue(toc.includes('TABLE DATA supabase_migrations schema_migrations '), 'Backup is missing migration history.');
    const roles = postgres(connection, 'pg_dumpall', ['--no-password', '--roles-only', '--no-role-passwords']);
    return { 'database.dump': dump, 'roles.sql': roles };
  } catch (error) { dump.fill(0); throw error; }
}

export function buildDatabaseArchive(connection, recipient, additionalFiles = {}) {
  const databaseFiles = exportDatabase(connection);
  try {
    requireValue(!Object.keys(additionalFiles).some(name => Object.hasOwn(databaseFiles, name)), 'Additional file replaces required database content.');
    return sealArchive({ ...additionalFiles, ...databaseFiles }, recipient);
  } finally { Object.values(databaseFiles).forEach(bytes => bytes.fill(0)); }
}
