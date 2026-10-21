import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync, createPublicKey } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { prepareDailyJob } from './prepare-daily-job.mjs';
import { openArchive } from './archive.mjs';
import { validateJob } from './daily-backup.mjs';

const identity=generateKeyPairSync('rsa',{modulusLength:3072,publicKeyEncoding:{type:'spki',format:'pem'},privateKeyEncoding:{type:'pkcs8',format:'pem'}});
const hash=value=>createHash('sha256').update(value).digest('hex');
const input={root:fileURLToPath(new URL('../../',import.meta.url)),origin:'https://'+'a'.repeat(20)+'.supabase.co',
  publishableKey:'sb_publishable_synthetic',recipient:identity.publicKey,
  recipientSha256:hash(createPublicKey(identity.publicKey).export({type:'spki',format:'der'})),accountId:'b'.repeat(32),
  permitUrl:'https://synthetic.workers.dev/daily-backup-permit',
  contract:JSON.parse(readFileSync(new URL('./preview-logical-contract.json',import.meta.url)))};
const functions=['airport-catalog','recovery-envelope'].map(slug=>({slug,version:1,updated_at:123,status:'ACTIVE'}));
const token='synthetic-token-value-12345';
function provider(change) {
  const calls=[];
  return {calls,request:async(url,options)=>{
    assert.ok(url.startsWith('https://api.supabase.com/v1/projects/'+'a'.repeat(20)+'/functions'));
    assert.equal(options.method,'GET');assert.equal(options.redirect,'error');assert.equal(options.headers.Authorization,'Bearer '+token);
    calls.push(url);return change?.(calls.length)??(url.endsWith('/body')?new Response('synthetic deployed bytes'):Response.json(functions));
  }};
}
test('prepared job preserves reconstruction hashes and encrypts exact deployed bytes without credentials',async()=>{
  const p=provider(),job=await prepareDailyJob(input,token,p.request);
  assert.equal(p.calls.length,4);assert.deepEqual(job.deploymentRevisions,functions.map(({slug,version,updated_at})=>({slug,version,updated_at})));
  assert.equal(validateJob(job,{repository:job.repository,runId:'1',runAttempt:'1',commit:'0'.repeat(40)}),hash(JSON.stringify(job)));
  const nested=openArchive(Buffer.from(job.source.sources['deployment-sources.elbbackup'],'base64'),identity.privateKey);
  assert.deepEqual(JSON.parse(nested['deployments/metadata.json']),functions);
  assert.equal(nested['deployments/recovery-envelope.bundle'].toString(),'synthetic deployed bytes');
  assert.ok(!JSON.stringify(job).includes(token));
  assert.ok(job.source.sourceManifest.files.some(file=>file.name==='sources/tools/hosted-backup/prepare-daily-job.mjs'));
});
test('invalid recipient/origin/public key rejects before provider reads',async()=>{
  for(const patch of [{recipientSha256:'0'.repeat(64)},{origin:'https://other.invalid'},{publishableKey:'secret'}]) {
    const p=provider();await assert.rejects(prepareDailyJob({...input,...patch},token,p.request));assert.equal(p.calls.length,0);
  }
});
test('denied, extra, duplicate, inactive, oversized and drifting deployments reject without producing a job',async()=>{
  for(const change of [n=>n===1?new Response('denied',{status:403}):undefined,
    n=>n===1?Response.json([...functions,functions[0]]):undefined,
    n=>n===1?Response.json([functions[0],functions[0]]):undefined,
    n=>n===1?Response.json(functions.map(item=>({...item,status:'INACTIVE'}))):undefined,
    n=>n===2?new Response(new Uint8Array(4*1024*1024+1)):undefined,
    n=>n===4?Response.json(functions.map(item=>({...item,version:2}))):undefined]) {
    const p=provider(change);await assert.rejects(prepareDailyJob(input,token,p.request));assert.ok(p.calls.length<=4);
  }
});
