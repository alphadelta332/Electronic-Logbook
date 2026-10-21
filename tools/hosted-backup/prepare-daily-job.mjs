import assert from 'node:assert/strict';
import { createHash, createPublicKey } from 'node:crypto';
import { reconstructionSources } from './orchestrate.mjs';
import { sealArchive } from './archive.mjs';
import { validateJob } from './daily-backup.mjs';

const hash = value => createHash('sha256').update(value).digest('hex');
const revision = functions => functions.map(({slug, version, updated_at}) => ({slug, version, updated_at})).sort((a,b)=>a.slug.localeCompare(b.slug));

// Owner-side preparation only: no source export, credentials in output, or remote mutation.
export async function prepareDailyJob(input, projectReadToken, request = fetch) {
  assert.match(input.origin, /^https:\/\/[a-z]{20}\.supabase\.co$/);
  assert.match(input.publishableKey, /^sb_publishable_[a-zA-Z0-9_-]+$/);
  assert.match(input.accountId, /^[a-f0-9]{32}$/);
  assert.match(input.permitUrl, /^https:\/\/[a-z0-9.-]+\.workers\.dev\/daily-backup-permit$/);
  assert.match(input.recipient, /^-----BEGIN PUBLIC KEY-----\r?\n/);
  assert.equal(hash(createPublicKey(input.recipient).export({type:'spki',format:'der'})), input.recipientSha256);
  assert.equal(typeof projectReadToken, 'string'); assert.ok(projectReadToken.length>=20 && !/\s/.test(projectReadToken));
  const source = reconstructionSources(input.root), project = new URL(input.origin).hostname.split('.')[0];
  const get = async path => {
    const response = await request(`https://api.supabase.com/v1/projects/${project}/functions${path}`, {
      method:'GET', headers:{Authorization:'Bearer '+projectReadToken}, redirect:'error', signal:AbortSignal.timeout(30000)
    });
    assert.equal(response.status,200,'Deployed-source read failed; diagnostics suppressed.');
    assert.ok(response.body); const chunks=[];let size=0;
    for await(const bytes of response.body) {size+=bytes.length;assert.ok(size<=4*1024*1024,'Deployed-source response exceeds limit.');chunks.push(bytes);}
    assert.ok(size>0);return Buffer.concat(chunks);
  };
  const files = {};
  try {
    const metadata = await get(''), functions = JSON.parse(metadata);
    assert.ok(Array.isArray(functions) && functions.length===2);
    assert.deepEqual(functions.map(item=>item.slug).sort(),['airport-catalog','recovery-envelope']);
    for(const item of functions) assert.ok(item.status==='ACTIVE' && Number.isSafeInteger(item.version) && item.version>0 &&
      Number.isSafeInteger(item.updated_at) && item.updated_at>0);
    files['deployments/metadata.json']=metadata;
    for(const item of functions) files['deployments/'+item.slug+'.bundle']=await get('/'+item.slug+'/body');
    assert.deepEqual(JSON.parse(await get('')),functions,'Deployed function metadata changed during preparation.');
    const current = reconstructionSources(input.root);
    assert.equal(current.revision,source.revision);assert.equal(current.dirty,source.dirty);
    assert.deepEqual(Object.keys(current.files),Object.keys(source.files));
    for(const name of Object.keys(source.files)) assert.deepEqual(current.files[name],source.files[name],'Reconstruction source changed.');
    const protectedSources=sealArchive(files,input.recipient);
    const contents={...source.files,'deployment-sources.elbbackup':protectedSources};
    const job={version:1,repository:'alphadelta332/flightlogx-preview-backups',accountId:input.accountId,
      bucket:'flightlogx-preview-backups',permitUrl:input.permitUrl,deploymentRevisions:revision(functions),
      source:{origin:input.origin,publishableKey:input.publishableKey,recipient:input.recipient,contract:input.contract,
        sources:Object.fromEntries(Object.entries(contents).map(([name,bytes])=>[name,bytes.toString('base64')])),
        sourceManifest:{revision:source.revision,dirty:source.dirty,files:Object.entries(contents).map(([name,bytes])=>({name,sha256:hash(bytes)}))}}};
    validateJob(job,{repository:job.repository,runId:'1',runAttempt:'1',commit:'0'.repeat(40)});
    protectedSources.fill(0);return job;
  } finally {for(const bytes of [...Object.values(files),...Object.values(source.files)]) bytes.fill(0);}
}
