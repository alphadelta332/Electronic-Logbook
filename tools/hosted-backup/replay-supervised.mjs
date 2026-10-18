import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { readDisposable } from './restore.mjs';

export function buildReplayCheck() {
  const project = fileURLToPath(new URL('./ReplayCheck/ReplayCheck.csproj', import.meta.url));
  const built = spawnSync('dotnet', ['build', project, '--verbosity', 'quiet'], {
    windowsHide: true, timeout: 120000, maxBuffer: 1024 * 1024
  });
  assert.equal(built.status, 0, 'Application replay build failed; diagnostics suppressed.');
}

export function replaySupervised(local, target, recoveryEnvironment) {
  const values = new Map(recoveryEnvironment.toString('utf8').split(/\r?\n/)
    .filter(line => line && !line.startsWith('#')).map(line => {
      const index = line.indexOf('=');
      assert.ok(index > 0, 'Invalid recovery capsule environment.');
      return [line.slice(0, index), line.slice(index + 1)];
    }));
  const kek = values.get('RECOVERY_KEK_BASE64');
  const keyVersionId = values.get('RECOVERY_KEY_VERSION_ID');
  assert.ok(kek && Buffer.from(kek, 'base64').length === 32 && keyVersionId, 'Recovery capsule lacks managed key material.');
  const rows = JSON.parse(readDisposable(local, target, `select coalesce(json_agg(json_build_object(
    'logbookGuid', l.logbook_id,
    'identityCount', (select count(*) from auth.identities i where i.user_id=l.owner_account_id),
    'keyEnvelopes', (select coalesce(json_agg(k),'[]') from public.key_envelopes k where k.logbook_id=l.logbook_id
      and k.recovery_method='managed-service-v1' and k.revoked_at is null and (k.expires_at is null or k.expires_at>now())),
    'operations', (select coalesce(json_agg(o order by o.revision),'[]') from public.operations o where o.logbook_id=l.logbook_id),
    'configurations', (select coalesce(json_agg(c order by c.revision),'[]') from public.configuration_revisions c where c.logbook_id=l.logbook_id)
  ) order by l.logbook_id),'[]') from public.logbooks l
    where l.deleted_at is null and exists(select 1 from public.operations o where o.logbook_id=l.logbook_id);`));
  const common = row => ({ hostedRevision: row.revision, revisionId: { value: row.portable_revision_id },
    deviceId: { value: 'dev_' + row.author_device_id.replaceAll('-', '') }, createdAt: row.client_created_at,
    payloadCiphertext: row.payload_ciphertext, payloadNonce: row.payload_nonce, payloadTag: row.payload_tag, payloadHash: row.payload_hash });
  const logbooks = rows.map(row => {
    assert.equal(row.keyEnvelopes.length, 1, 'Populated logbook lacks one usable managed recovery envelope.');
    return { ...row, keyEnvelope: row.keyEnvelopes[0], operations: row.operations.map(o => ({ ...common(o),
      entryId: { value: o.entry_id || 'ent_unknown' }, schemaVersion: o.operation_format_version + 1,
      parentRevisionIds: o.parent_revision_ids.map(value => ({ value })) })),
    configurations: row.configurations.map(c => ({ ...common(c), schemaVersion: c.configuration_format_version + 1 })) };
  });
  const result = spawnSync('dotnet', [fileURLToPath(new URL('./ReplayCheck/bin/Debug/net10.0/ReplayCheck.dll', import.meta.url)), 'verify-real'], {
    input: JSON.stringify({ kek, keyVersionId, logbooks }), encoding: 'utf8', windowsHide: true, timeout: 60000,
    maxBuffer: 1024 * 1024
  });
  values.clear();
  assert.equal(result.status, 0, 'Recovered-key application replay failed; private diagnostics suppressed.');
  const report = JSON.parse(result.stdout);
  assert.equal(report.status, 'application-replayed');
  return report;
}
