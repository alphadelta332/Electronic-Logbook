import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { appendFileSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { captureLogicalArchive } from './logical-client.mjs';
import { maxArchiveBytes } from './archive.mjs';
import { dailySourceEgressBytes } from './daily-controls.mjs';
import { validateCostEvidence, ownerReviewBudget } from './daily-cost-policy.mjs';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const bucket = 'flightlogx-preview-backups';
const repository = 'alphadelta332/flightlogx-preview-backups';
const maximumObjects = 35;
const policy = JSON.parse(readFileSync(new URL('./r2-policy.json', import.meta.url)));

// Reuse the existing R2 check's curl signer: credentials travel on stdin, never argv.
export function r2Request(accountId, access, method, key = '', body) {
  assert.match(accountId, /^[a-f0-9]{32}$/);
  assert.match(access.accessKeyId, /^[a-f0-9]{32}$/);
  assert.match(access.secretAccessKey, /^[a-f0-9]{64}$/);
  assert.ok(method === 'GET' || (method === 'PUT' && key));
  assert.match(key, /^[a-zA-Z0-9/._-]{0,200}$/);
  assert.ok(!key.split('/').some(part => part === '..' || part === '.'));
  assert.ok(method === 'PUT' ? Buffer.isBuffer(body) && body.length <= maxArchiveBytes : body === undefined);
  let directory;
  try {
    const config = [
      `url = "https://${accountId}.r2.cloudflarestorage.com/${bucket}${key ? '/' + key : '?list-type=2&max-keys=1000'}"`,
      `request = "${method}"`, 'aws-sigv4 = "aws:amz:auto:s3"',
      `user = "${access.accessKeyId}:${access.secretAccessKey}"`,
      'silent', 'show-error', 'connect-timeout = 10', 'max-time = 60',
      `max-filesize = ${key && method === 'GET' ? maxArchiveBytes : 1024 * 1024}`,
      'proto = "=https"', 'write-out = "\\n%{http_code}"'
    ];
    if (body) {
      directory = mkdtempSync(join(tmpdir(), 'elb-daily-ciphertext-'));
      const file = join(directory, 'archive.elbbackup').replaceAll('\\', '/');
      assert.ok(!/["\r\n]/.test(file));
      writeFileSync(file, body, {flag:'wx',mode:0o600});
      config.push('header = "Content-Type: application/octet-stream"',
        `header = "x-amz-content-sha256: ${hash(body)}"`, 'header = "If-None-Match: *"', `data-binary = "@${file}"`);
    }
    const result = spawnSync(process.platform === 'win32' ? 'curl.exe' : 'curl', ['--disable','--config','-'], {
      input:config.join('\n')+'\n', windowsHide:true, timeout:65000, maxBuffer:maxArchiveBytes+1024*1024,
      env:{SystemRoot:process.env.SystemRoot,PATH:process.env.PATH,TEMP:process.env.TEMP,TMP:process.env.TMP}
    });
    assert.equal(result.status,0,'R2 request failed; diagnostics suppressed.');
    assert.equal(result.stdout.subarray(-4).toString(),'\n200','R2 request denied or failed; response suppressed.');
    return result.stdout.subarray(0,-4);
  } finally {
    if (directory) {
      assert.ok(resolve(directory).startsWith(resolve(tmpdir())+sep));
      rmSync(directory,{recursive:true,force:true});
    }
  }
}

export function validateJob(job, context) {
  assert.deepEqual(Object.keys(job).sort(), ['accountId','bucket','deploymentRevisions','permitUrl','repository','source','version'].sort());
  assert.equal(job.version,1); assert.equal(job.repository,repository); assert.equal(context.repository,repository);
  assert.equal(job.bucket,bucket); assert.match(job.accountId,/^[a-f0-9]{32}$/);
  assert.match(job.permitUrl,/^https:\/\/[a-z0-9.-]+\.workers\.dev\/daily-backup-permit$/);
  assert.match(context.runId,/^[1-9][0-9]*$/); assert.match(context.runAttempt,/^[1-9][0-9]*$/);
  assert.match(context.commit,/^[a-f0-9]{40}$/);
  assert.deepEqual(Object.keys(job.source).sort(), ['origin','publishableKey','recipient','contract','sources','sourceManifest'].sort());
  assert.match(job.source.origin,/^https:\/\/[a-z]{20}\.supabase\.co$/);
  assert.match(job.source.publishableKey,/^sb_publishable_[a-zA-Z0-9_-]+$/);
  assert.match(job.source.recipient,/^-----BEGIN PUBLIC KEY-----\r?\n/);
  assert.equal(job.source.contract.shape.length,47);
  assert.deepEqual(job.deploymentRevisions.map(f=>f.slug).sort(),['airport-catalog','recovery-envelope']);
  for (const deployment of job.deploymentRevisions) {
    assert.deepEqual(Object.keys(deployment).sort(),['slug','updated_at','version']);
    assert.ok(Number.isSafeInteger(deployment.version) && deployment.version>0 &&
      Number.isSafeInteger(deployment.updated_at) && deployment.updated_at>0);
  }
  const files = job.source.sourceManifest.files;
  assert.ok(Array.isArray(files) && files.length>0 && files.length<=990);
  assert.deepEqual(files.map(f=>f.name).sort(),Object.keys(job.source.sources).sort());
  assert.ok(files.some(f=>f.name==='deployment-sources.elbbackup'));
  let bytes = 0;
  for (const file of files) {
    assert.match(file.sha256,/^[a-f0-9]{64}$/);
    const content = Buffer.from(job.source.sources[file.name],'base64');
    try { bytes+=content.length; assert.equal(hash(content),file.sha256,'Reconstruction inputs changed.'); }
    finally { content.fill(0); }
  }
  assert.ok(bytes<=32*1024*1024);
  return hash(JSON.stringify(job));
}

async function currentPermit(job, jobSha256) {
  const response = await fetch(job.permitUrl,{redirect:'error',cache:'no-store',signal:AbortSignal.timeout(10000)});
  assert.equal(response.status,200,'Current cost/deployment evidence unavailable.');
  const chunks=[]; let size=0;
  for await (const part of response.body) {size+=part.length;assert.ok(size<=16384);chunks.push(part);}
  const permit=JSON.parse(Buffer.concat(chunks));
  validateCostEvidence(permit,jobSha256,job.source.origin);
  assert.equal(permit.jobSha256,jobSha256,'Prepared job or source inputs changed.');
  assert.deepEqual(permit.deploymentRevisions,job.deploymentRevisions,'Deployed source revision changed.');
  assert.equal(permit.r2.accountId,job.accountId); assert.equal(permit.r2.bucket,bucket);
  assert.equal(permit.r2.policySha256,hash(JSON.stringify(policy))); assert.equal(permit.r2.privateStandardBucket,true);
  return permit;
}

function inventory(xml, day) {
  assert.ok(xml.length<=1024*1024);
  const text=xml.toString('utf8');
  assert.match(text,/<ListBucketResult[\s>]/); assert.match(text,/<IsTruncated>false<\/IsTruncated>/);
  const contents=[...text.matchAll(/<Contents>([\s\S]*?)<\/Contents>/g)].map(match=>{
    const key=match[1].match(/<Key>([a-zA-Z0-9/._-]{1,200})<\/Key>/)?.[1];
    const size=Number(match[1].match(/<Size>([0-9]+)<\/Size>/)?.[1]);
    assert.ok(key && Number.isSafeInteger(size) && size>=0,'Unsupported inventory entry.');
    return {key,size};
  });
  assert.equal(contents.length,(text.match(/<Contents>/g)??[]).length);
  assert.ok(contents.length<maximumObjects && contents.reduce((sum,o)=>sum+o.size,0)+maxArchiveBytes<=maximumObjects*maxArchiveBytes,
    'Approved retained-object/byte ceiling reached.');
  assert.ok(!contents.some(o=>o.key===`daily/${day}.elbbackup`),'Today already has an archive; preserve it.');
}

function validateReservation(receipt,jobSha256,review,context,day) {
  assert.equal(receipt.version,2); assert.equal(receipt.status,'reserved');assert.equal(receipt.day,day);
  assert.match(receipt.readbackToken,/^[a-f0-9]{64}$/);
  assert.equal(receipt.jobSha256,jobSha256);assert.equal(receipt.reviewSha256,hash(JSON.stringify(ownerReviewBudget(review))));
  assert.equal(receipt.sourceEgressBytes,dailySourceEgressBytes);
  for(const name of ['repository','runId','runAttempt','commit'])assert.equal(receipt[name],context[name]);
  const age=Date.now()-Date.parse(receipt.reservedAt);assert.ok(age>=0 && age<30*60*1000,'Reservation is stale.');
}

async function requestAllowance(job,route,payload,headers) {
  const response=await fetch(job.permitUrl.replace('/daily-backup-permit',route),{
    method:'POST',headers:{'Content-Type':'application/json',...headers},body:JSON.stringify(payload),
    redirect:'error',signal:AbortSignal.timeout(10000)
  });
  assert.equal(response.status,201,'Backup allowance was not reserved.');
  const chunks=[];let size=0;
  for await(const part of response.body){size+=part.length;assert.ok(size<=2048);chunks.push(part);}
  return JSON.parse(Buffer.concat(chunks));
}

async function storageAllowance(job,context,reservation,operation) {
  const claim=await requestAllowance(job,'/daily-backup-request',
    {version:2,jobSha256:reservation.jobSha256,sourceOrigin:job.source.origin,...context,reservation,operation},{});
  assert.equal(claim.version,2);assert.equal(claim.status,'request-reserved');assert.equal(claim.operation,operation);
  assert.equal(claim.reservationSha256,hash(JSON.stringify(reservation)));
}

export async function uploadDaily(job, capability, context, request, reservationToken) {
  const jobSha256=validateJob(job,context);
  assert.match(capability,/^[a-f0-9]{64}$/);
  assert.match(reservationToken,/^[a-f0-9]{64}$/);
  const day=new Date().toISOString().slice(0,10);
  const permit=await currentPermit(job,jobSha256);
  const reservation=await requestAllowance(job,'/daily-backup-reservation',
    {version:2,jobSha256,sourceOrigin:job.source.origin,...context},{'x-flightlogx-reservation-token':reservationToken});
  validateReservation(reservation,jobSha256,permit.ownerReview,context,day);
  await storageAllowance(job,context,reservation,'list-before');
  inventory(await request('GET',''),day);
  const archive=await captureLogicalArchive({...job.source,capability});
  try {
    const after=await currentPermit(job,jobSha256);
    assert.deepEqual(ownerReviewBudget(after.ownerReview),ownerReviewBudget(permit.ownerReview),'Owner review changed during export.');
    await storageAllowance(job,context,reservation,'list-after');
    inventory(await request('GET',''),day);
    assert.equal(new Date().toISOString().slice(0,10),day,'UTC day changed; retry on the new day.');
    const key=`daily/${day}.elbbackup`;
    await storageAllowance(job,context,reservation,'upload');
    await request('PUT',key,archive);
    return {version:2,key,bytes:archive.length,sha256:hash(archive),jobSha256,reservation,
      uploadedAt:new Date().toISOString(),...context};
  } finally {archive.fill(0);}
}

export async function readbackDaily(job, pending, context, request) {
  const jobSha256=validateJob(job,context);
  assert.deepEqual(Object.keys(pending).sort(),['version','key','bytes','sha256','jobSha256','uploadedAt','repository','runId','runAttempt','commit','reservation'].sort());
  assert.equal(pending.version,2); assert.equal(pending.jobSha256,jobSha256);
  for (const name of ['repository','runId','runAttempt','commit']) assert.equal(pending[name],context[name]);
  assert.match(pending.sha256,/^[a-f0-9]{64}$/);
  assert.ok(Number.isSafeInteger(pending.bytes) && pending.bytes>422 && pending.bytes<=maxArchiveBytes);
  const age=Date.now()-Date.parse(pending.uploadedAt);
  assert.ok(age>=0 && age<30*60*1000,'Upload request is stale.');
  assert.equal(pending.key,`daily/${pending.uploadedAt.slice(0,10)}.elbbackup`);
  const permit=await currentPermit(job,jobSha256);
  validateReservation(pending.reservation,jobSha256,permit.ownerReview,context,pending.uploadedAt.slice(0,10));
  const claim=await requestAllowance(job,'/daily-backup-readback',
    {version:2,jobSha256,sourceOrigin:job.source.origin,...context,reservation:pending.reservation},{});
  assert.equal(claim.version,2);assert.equal(claim.status,'readback-reserved');
  assert.equal(claim.reservationSha256,hash(JSON.stringify(pending.reservation)));
  await storageAllowance(job,context,pending.reservation,'readback');
  const downloaded=await request('GET',pending.key);
  try {
    assert.equal(downloaded.length,pending.bytes,'Readback size mismatch.');
    assert.equal(hash(downloaded),pending.sha256,'Readback digest mismatch.');
    const reservation={...pending.reservation};delete reservation.readbackToken;
    return {...pending,reservation,status:'verified',verifiedAt:new Date().toISOString()};
  } finally {downloaded.fill(0);}
}

if (process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) {
  try {
    assert.equal(process.argv.length,4); const mode=process.argv[2]; assert.ok(['upload','readback'].includes(mode));
    const raw=readFileSync(process.argv[3]);assert.ok(raw.length<=48*1024*1024);const job=JSON.parse(raw);
    const context={repository:process.env.GITHUB_REPOSITORY,runId:process.env.GITHUB_RUN_ID,
      runAttempt:process.env.GITHUB_RUN_ATTEMPT,commit:process.env.GITHUB_SHA};
    const access={accessKeyId:process.env.R2_ACCESS_KEY_ID,secretAccessKey:process.env.R2_SECRET_ACCESS_KEY};
    const request=(...args)=>r2Request(job.accountId,access,...args);
    if (mode==='upload') {
      const pending=await uploadDaily(job,process.env.SOURCE_CAPABILITY,context,request,process.env.BACKUP_RESERVATION_TOKEN);
      assert.ok(process.env.GITHUB_OUTPUT);appendFileSync(process.env.GITHUB_OUTPUT,'request='+JSON.stringify(pending)+'\n');
      console.log('Encrypted object uploaded; independent readback still required.');
    } else {
      assert.ok(!process.env.SOURCE_CAPABILITY,'Readback job must not receive the source capability.');
      assert.ok(!process.env.BACKUP_RESERVATION_TOKEN,'Readback job must not reserve another source attempt.');
      const receipt=await readbackDaily(job,JSON.parse(process.env.BACKUP_READBACK_REQUEST),context,request);
      console.log(JSON.stringify(receipt));
      if(process.env.GITHUB_STEP_SUMMARY)appendFileSync(process.env.GITHUB_STEP_SUMMARY,'Verified encrypted backup readback\n\n```json\n'+JSON.stringify(receipt)+'\n```\n');
    }
  } catch {console.error('Daily backup stopped; no credentials, participant data or provider diagnostics printed. Previous backups preserved.');process.exitCode=1;}
}
