import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { existsSync } from 'node:fs';
import childProcess from 'node:child_process';
import { syncBuiltinESMExports } from 'node:module';
import { gzipSync } from 'node:zlib';
import { uploadDaily, readbackDaily, r2Request } from './daily-backup.mjs';
import { sealArchive, openArchive, maxArchiveBytes } from './archive.mjs';
import { reserveDailyAttempt, reserveDailyReadback, reserveWorkflowRequest } from './daily-controls.mjs';
import { cachedBackupPermit } from './daily-primary.mjs';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const identity=generateKeyPairSync('rsa',{modulusLength:3072,
  publicKeyEncoding:{type:'spki',format:'pem'},privateKeyEncoding:{type:'pkcs8',format:'pem'}});
const context={repository:'alphadelta332/flightlogx-preview-backups',runId:'123',runAttempt:'1',commit:'a'.repeat(40)};
const token='b'.repeat(64);
const reservationToken='d'.repeat(64);
const contract=JSON.parse(readFileSync(new URL('./preview-logical-contract.json',import.meta.url)));
const policy=JSON.parse(readFileSync(new URL('./r2-policy.json',import.meta.url)));
const catalogue=gzipSync(Buffer.from(JSON.stringify(Array.from({length:5000},(_,i)=>({i:'X'+String(i).padStart(4,'0'),n:'Synthetic '+i,y:0,x:0})))));
const path='snapshots/'+hash(catalogue)+'.json.gz';
const references={
  'manifest.json':Buffer.from(JSON.stringify({schemaVersion:1,cycle:'2026-10-01',publishedAt:'2026-10-18T00:00:00Z',
    sha256:hash(catalogue),path,count:5000})),[path]:catalogue,'seed.json.gz':catalogue
};

function fixture(t) {
  const sources={'sources/fixture.mjs':Buffer.from('export const synthetic = true;'),
    'deployment-sources.elbbackup':sealArchive({'deployments/synthetic.bundle':Buffer.from('synthetic deployment')},identity.publicKey)};
  const job={version:1,repository:context.repository,accountId:'c'.repeat(32),bucket:'flightlogx-preview-backups',
    permitUrl:'https://synthetic.example.workers.dev/daily-backup-permit',
    deploymentRevisions:[{slug:'airport-catalog',version:1,updated_at:1},{slug:'recovery-envelope',version:1,updated_at:1}],
    source:{origin:'https://'+'a'.repeat(20)+'.supabase.co',publishableKey:'sb_publishable_synthetic',recipient:identity.publicKey,contract,
      sources:Object.fromEntries(Object.entries(sources).map(([name,bytes])=>[name,bytes.toString('base64')])),
      sourceManifest:{revision:context.commit,dirty:true,files:Object.entries(sources).map(([name,bytes])=>({name,sha256:hash(bytes)}))}}};
  const approvedAt=new Date(Date.now()-1000).toISOString();
  const end=new Date(Date.parse(approvedAt)+31*24*60*60*1000).toISOString();
  const ownerReview={version:2,approvedAt,expiresAt:end,weeklyCheckedAt:approvedAt,sourceExpiresAt:end,
    jobSha256:hash(JSON.stringify(job)),sourceOrigin:job.source.origin,githubPaidActionsBlocked:true,supabasePlan:'free',
    egress:{used:1,included:5e9},cachedEgress:{used:1,included:5e9},
    github:{observedAt:approvedAt,periodStart:approvedAt,periodEnd:end,remainingLinuxMinutes:1997},
    r2:{observedAt:approvedAt,periodStart:approvedAt,periodEnd:end,storageGbMonths:0.01,requests:67,
      storageBillableGbMonths:0,classABillable:0,classBBillable:0,usageCostUsd:0}};
  const observedAt=new Date().toISOString();
  const permit={version:3,checkedAt:observedAt,expiresAt:new Date(Date.now()+60000).toISOString(),ownerReview,
    jobSha256:hash(JSON.stringify(job)),deploymentRevisions:structuredClone(job.deploymentRevisions),
    deploymentObservedAt:observedAt,
    github:{observedAt,plan:'free',queryStart:approvedAt,queryEnd:observedAt,
      actionsMinutes:[{sku:'actions_linux',grossQuantity:7,netAmount:0}]},
    supabase:{observedAt,plan:'free'},
    r2:{accountId:job.accountId,bucket:job.bucket,policySha256:hash(JSON.stringify(policy)),privateStandardBucket:true,
      observedAt,currentBytes:1500000,sampledPeakBytes:1500000,newestStorageMeasurementAt:observedAt,
      pendingUploads:0,infrequentAccessBytes:0,windows:[{from:approvedAt,until:observedAt,requests:67},
        {from:new Date(Date.parse(observedAt)-31*24*60*60*1000).toISOString(),until:observedAt,requests:67}]}};
  const rows={
    'storage.buckets':[{id:'airport-catalog',public:true}],
    'storage.objects':Object.entries(references).map(([name,bytes])=>({bucket_id:'airport-catalog',name,
      metadata:{size:bytes.length,eTag:createHash('md5').update(bytes).digest('hex')}})),
    'public.operations':[{note:'synthetic-private-flight'}]
  };
  const snapshot={shape:contract.shape,generated:contract.generatedExpressions,
    context:{method:'GET',readOnly:'on',isolation:'read committed',capturedAt:new Date().toISOString()},
    ledgers:{auth:contract.ledgers.auth,storage:contract.ledgers.storage,application:contract.ledgers.application},
    datasets:contract.shape.map(t=>({table:t.table,rows:(rows[t.table]??[]).map(r=>JSON.stringify(r))})),sequences:[]};
  const state={permits:0,captures:0,puts:0,lists:0,readbacks:0,permitStatus:200,failPut:false,corrupt:false,truncated:false,
    oversizedInventory:false,reserved:false,reservations:0,denyReadback:false,failReadback:false,
    loseReadbackReply:false,alterReadbackReply:false,
    failCapture:false,beforeSecondPermit:()=>{},beforeSecondList:()=>{}};
  const grants=new Map();let tail=Promise.resolve();
  const storage={transaction(action){
    const operation=tail.then(()=>action({get:async key=>structuredClone(grants.get(key)),
      put:async(key,value)=>grants.set(key,structuredClone(value))}));
    tail=operation.catch(()=>{});return operation;
  }};
  const previous=Buffer.from('preserve previous independently retained bytes');
  const objects=new Map([['manual/previous.elbbackup',Buffer.from(previous)]]);
  t.mock.method(globalThis,'fetch',async (url,options)=>{
    if(url===job.permitUrl){
      state.permits++; if(state.permits===2)state.beforeSecondPermit();
      return new Response(JSON.stringify(permit),{status:state.permitStatus});
    }
    if(url===job.permitUrl.replace('/daily-backup-permit','/daily-backup-reservation')){
      state.reservations++;assert.equal(options.method,'POST');
      assert.equal(options.headers['x-flightlogx-reservation-token'],reservationToken);
      const payload=JSON.parse(options.body);
      assert.equal(payload.sourceOrigin,job.source.origin);
      try {
        const receipt=await reserveDailyAttempt(storage,payload,ownerReview);state.reserved=true;
        return Response.json(receipt,{status:201});
      }catch{return Response.json({error:'Already reserved'},{status:409});}
    }
    if(url===job.permitUrl.replace('/daily-backup-permit','/daily-backup-readback')){
      assert.equal(options.method,'POST');
      assert.equal(options.headers['x-flightlogx-reservation-token'],undefined);
      if(state.denyReadback)return new Response('',{status:409});
      try{
        const claim=await reserveDailyReadback(storage,JSON.parse(options.body),ownerReview);
        if(state.loseReadbackReply)throw new Error('Synthetic lost claim response');
        if(state.alterReadbackReply)claim.reservationSha256='0'.repeat(64);
        return Response.json(claim,{status:201});
      }
      catch{return Response.json({error:'Already used'},{status:409});}
    }
    if(url===job.permitUrl.replace('/daily-backup-permit','/daily-backup-request')){
      if(state.denyStorageClaim)return new Response('',{status:409});
      try{
        const claim=await reserveWorkflowRequest(storage,JSON.parse(options.body),ownerReview);
        if(state.loseStorageReply)throw new Error('Synthetic lost storage claim');
        return Response.json(claim,{status:201});
      }
      catch{return Response.json({error:'Request denied'},{status:409});}
    }
    if(url===job.source.origin+'/rest/v1/rpc/elb_backup_snapshot'){
      state.captures++;assert.equal(options.headers['x-flightlogx-backup-token'],token);
      if(state.failCapture)return new Response('Synthetic capture failure',{status:503});
      return new Response(JSON.stringify(snapshot));
    }
    const prefix=job.source.origin+'/storage/v1/object/public/airport-catalog/';
    assert.ok(url.startsWith(prefix),'No unexpected network destination is allowed.');
    return new Response(references[url.slice(prefix.length).split('?')[0]]);
  });
  const uploadRequest=async(method,key,body)=>{
    assert.equal(state.reserved,true,'Every storage request needs a committed allowance.');
    assert.equal(grants.get('component-requests').counts.workflow,state.lists+state.puts+1);
    if(method==='GET' && key===''){
      state.lists++;if(state.lists===2)state.beforeSecondList();
      return Buffer.from('<ListBucketResult><IsTruncated>'+state.truncated+'</IsTruncated>'+[...objects]
        .map(([key,value])=>`<Contents><Key>${key}</Key><Size>${state.oversizedInventory?35*maxArchiveBytes:value.length}</Size></Contents>`).join('')+'</ListBucketResult>');
    }
    assert.equal(method,'PUT');assert.match(key,/^daily\/\d{4}-\d{2}-\d{2}\.elbbackup$/);
    state.puts++;assert.ok(!objects.has(key),'Atomic no-overwrite upload.');
    if(state.failPut)throw new Error('Synthetic upload failure');
    objects.set(key,Buffer.from(body));return Buffer.alloc(0);
  };
  const readRequest=async(method,key)=>{
    assert.equal(method,'GET','Independent readback credential never writes.');
    assert.equal(grants.get('source-budget').readbackUsed,true,'Download must follow durable consumption.');
    assert.equal(grants.get('component-requests').counts.workflow,4);
    state.readbacks++;if(state.failReadback)throw new Error('Synthetic download failure');
    const bytes=Buffer.from(objects.get(key));if(state.corrupt)bytes[bytes.length-1]^=1;return bytes;
  };
  t.after(()=>assert.deepEqual(objects.get('manual/previous.elbbackup'),previous));
  return {job,permit,snapshot,state,objects,uploadRequest,readRequest};
}

test('production cached permit feeds real runner capture, encryption and independent readback',async t=>{
  const f=fixture(t),review=f.permit.ownerReview,at=f.permit.checkedAt;
  const provider={accountId:f.job.accountId,projectRef:f.job.source.origin.slice(8,28),bucket:f.job.bucket,
    r2Period:{periodStart:review.r2.periodStart,periodEnd:review.r2.periodEnd},policy,
    deploymentRevisions:f.job.deploymentRevisions};
  const history={accountId:provider.accountId,jobSha256:f.permit.jobSha256};
  const evidence={version:2,status:'collected-not-permitted',retrievedAt:at,
    github:{retrievedAt:at,periodStart:review.github.periodStart,plan:'free',actionsMinutes:f.permit.github.actionsMinutes},
    supabase:{retrievedAt:at,plan:'free',deploymentRevisions:f.job.deploymentRevisions},
    r2:{...f.permit.r2,reviewedPeriod:provider.r2Period}};
  const cache={startedAt:Date.parse(at),fingerprint:hash(JSON.stringify(provider)),evidence};
  const storage={transaction:async action=>action({get:async key=>{
    assert.equal(key,'provider-cache');return structuredClone(cache);
  }})};
  const actual=await cachedBackupPermit(storage,{BACKUP_ENABLED:'true',PRIMARY_MONITOR_JSON:JSON.stringify({provider,history}),
    OWNER_REVIEW_JSON:JSON.stringify(review)});
  for(const key of Object.keys(f.permit))delete f.permit[key];Object.assign(f.permit,actual);
  const pending=await uploadDaily(f.job,token,context,f.uploadRequest,reservationToken);
  assert.equal((await readbackDaily(f.job,pending,context,f.readRequest)).status,'verified');
  assert.equal(f.state.captures,1);assert.equal(f.state.puts,1);assert.equal(f.state.readbacks,1);
});

test('real capture/encryption plus separate readback produces a verified receipt and preserves prior objects',async t=>{
  const f=fixture(t);
  const pending=await uploadDaily(f.job,token,context,f.uploadRequest,reservationToken);
  assert.equal(pending.status,undefined); assert.equal(f.state.puts,1);
  const stored=f.objects.get(pending.key);
  assert.equal(stored.includes(Buffer.from('synthetic-private-flight')),false);
  const files=openArchive(stored,identity.privateKey);
  assert.equal(JSON.parse(files['logical.json']).datasets.length,47);
  assert.equal(JSON.parse(files['logical.json']).datasets.find(d=>d.table==='public.operations').rows[0],JSON.stringify({note:'synthetic-private-flight'}));
  assert.ok(files['reference/'+path].equals(catalogue));
  assert.ok(files['deployment-sources.elbbackup']);Object.values(files).forEach(bytes=>bytes.fill(0));
  const receipt=await readbackDaily(f.job,pending,context,f.readRequest);
  assert.equal(receipt.status,'verified');assert.equal(receipt.sha256,hash(stored));assert.equal(f.state.readbacks,1);
  assert.equal(receipt.reservation.readbackToken,undefined);
  assert.ok(!JSON.stringify(receipt).includes(pending.reservation.readbackToken));
  await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/not reserved/);
  assert.equal(f.state.readbacks,1);
  await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/not reserved/);
  assert.equal(f.state.lists,2);
  assert.equal(f.state.captures,1);assert.equal(f.state.puts,1);
});

test('missing, stale, incorrect and exhausted evidence stops before source export or R2 access',async t=>{
  for(const mutate of [
    f=>f.state.permitStatus=503,
    f=>f.permit.checkedAt=new Date(Date.now()-3*60*60*1000).toISOString(),
    f=>f.permit.expiresAt=new Date(Date.now()-1).toISOString(),
    f=>f.permit.github.observedAt=new Date(Date.now()-3*60*60*1000).toISOString(),
    f=>f.permit.supabase.observedAt=undefined,
    f=>f.permit.r2.observedAt=new Date(Date.now()+60000).toISOString(),
    f=>f.permit.deploymentObservedAt=new Date(Date.now()-3*60*60*1000).toISOString(),
    f=>f.permit.jobSha256='0'.repeat(64),
    f=>f.permit.deploymentRevisions[0].version++,
    f=>f.permit.ownerReview.githubPaidActionsBlocked=false,
    f=>f.permit.ownerReview.github.remainingLinuxMinutes=639,
    f=>f.permit.supabase.plan='pro',
    f=>f.permit.ownerReview.egress.included=1024,
    f=>f.permit.ownerReview.cachedEgress.used=5e9,
    f=>f.permit.ownerReview.expiresAt=new Date(Date.now()-1).toISOString(),
    f=>f.permit.ownerReview.expiresAt=new Date(Date.now()+32*24*60*60*1000).toISOString(),
    f=>f.permit.supabase.egress={used:0,included:5e9},
    f=>f.permit.github.paidActionsBlocked=true,
    f=>f.permit.r2.privateStandardBucket=false,
    f=>f.permit.r2.policySha256='0'.repeat(64),
    f=>f.permit.r2.currentBytes=2*1024*1024*1024,
    f=>f.permit.r2.windows[0].requests=10000,
    f=>f.permit.r2.windows[1].requests=10000
  ])await t.test(String(mutate),async t=>{
    const f=fixture(t);mutate(f);await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken));
    assert.equal(f.state.captures,0);assert.equal(f.state.lists,0);assert.equal(f.state.puts,0);
  });
});

test('inventory limits, truncated listings and another writer stop without replacing old copies',async t=>{
  for(const mutate of [
    f=>f.state.truncated=true,
    f=>{for(let i=0;i<34;i++)f.objects.set('checks/'+i,Buffer.alloc(1));},
    f=>f.state.oversizedInventory=true
  ])await t.test(String(mutate),async t=>{
    const f=fixture(t);mutate(f);await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken));
    assert.equal(f.state.captures,0);assert.equal(f.state.puts,0);
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/not reserved/);
    assert.equal(f.state.lists,1);
  });
  await t.test('concurrent archive between capture and upload',async t=>{
    const f=fixture(t);f.state.beforeSecondList=()=>f.objects.set('daily/'+new Date().toISOString().slice(0,10)+'.elbbackup',Buffer.from('other writer'));
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/Today already has/);assert.equal(f.state.puts,0);
  });
});

test('post-capture drift and upload failures leave prior backups and never report verification',async t=>{
  await t.test('deployment changed during capture',async t=>{
    const f=fixture(t);f.state.beforeSecondPermit=()=>f.permit.deploymentRevisions[0].version++;
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/Deployed source/);assert.equal(f.state.puts,0);
  });
  await t.test('upload failure',async t=>{
    const f=fixture(t);f.state.failPut=true;
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/Synthetic upload failure/);assert.equal(f.objects.size,1);
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/not reserved/);assert.equal(f.state.captures,1);
  });
});

test('corrupt readback, stale/mismatched requests and altered prepared sources cannot produce a verified receipt',async t=>{
  const f=fixture(t);const pending=await uploadDaily(f.job,token,context,f.uploadRequest,reservationToken);
  await assert.rejects(readbackDaily(f.job,{...pending,runId:'999'},context,f.readRequest));
  await assert.rejects(readbackDaily(f.job,{...pending,uploadedAt:new Date(Date.now()-31*60*1000).toISOString()},context,f.readRequest),/stale/);
  f.state.corrupt=true;await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/Readback digest/);
  await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/not reserved/);
  assert.equal(f.state.readbacks,1);
  f.job.source.sources['sources/fixture.mjs']=Buffer.from('altered').toString('base64');
  await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/Reconstruction inputs/);
});

test('readback size mismatch consumes the allowance and never reports verification',async t=>{
  const f=fixture(t);const pending=await uploadDaily(f.job,token,context,f.uploadRequest,reservationToken);
  await assert.rejects(readbackDaily(f.job,{...pending,bytes:pending.bytes+1},context,f.readRequest),/Readback size/);
  await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/not reserved/);
  assert.equal(f.state.readbacks,1);
});

test('denied readback never downloads; a failed download cannot reuse its allowance',async t=>{
  const f=fixture(t);const pending=await uploadDaily(f.job,token,context,f.uploadRequest,reservationToken);
  f.state.denyReadback=true;
  await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/not reserved/);
  assert.equal(f.state.readbacks,0);
  f.state.denyReadback=false;f.state.failReadback=true;
  await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/Synthetic download failure/);
  await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/not reserved/);
  assert.equal(f.state.readbacks,1);
});

test('capture failure consumes the reservation before source reading and prevents another attempt today',async t=>{
  const f=fixture(t);f.state.failCapture=true;
  await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/Hosted capability export failed/);
  assert.equal(f.state.reserved,true);assert.equal(f.state.captures,1);assert.equal(f.state.puts,0);
  await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/not reserved/);
  assert.equal(f.state.captures,1);assert.equal(f.state.lists,1);
});

test('denied or lost per-request allowance stops before storage/source I/O and never buys a retry',async t=>{
  for(const field of ['denyStorageClaim','loseStorageReply'])await t.test(field,async t=>{
    const f=fixture(t);f.state[field]=true;
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/not reserved/);
    assert.equal(f.state.lists,0);assert.equal(f.state.puts,0);assert.equal(f.state.captures,0);
    f.state[field]=false;
    await assert.rejects(uploadDaily(f.job,token,context,f.uploadRequest,reservationToken),/not reserved/);
    assert.equal(f.state.lists,0);
  });
});

test('lost or mismatched readback claim responses prevent downloads and cannot refund the consumed grant',async t=>{
  for(const field of ['loseReadbackReply','alterReadbackReply'])await t.test(field,async t=>{
    const f=fixture(t);const pending=await uploadDaily(f.job,token,context,f.uploadRequest,reservationToken);
    f.state[field]=true;
    await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest));
    f.state[field]=false;
    await assert.rejects(readbackDaily(f.job,pending,context,f.readRequest),/not reserved/);
    assert.equal(f.state.readbacks,0);
  });
});

test('R2 adapter rejects dangerous methods, names and credentials before starting curl',()=>{
  const access={accessKeyId:'a'.repeat(32),secretAccessKey:'b'.repeat(64)};
  for(const method of ['DELETE','POST','PATCH'])assert.throws(()=>r2Request('c'.repeat(32),access,method,'daily/test'));
  for(const key of ['../escape','daily/../../escape','x?lifecycle','x"\nuser=other'])
    assert.throws(()=>r2Request('c'.repeat(32),access,'GET',key));
  assert.throws(()=>r2Request('bad',access,'GET'));
  assert.throws(()=>r2Request('c'.repeat(32),{...access,secretAccessKey:'unsafe\n'},'GET'));
});

test('curl adapter keeps secrets off argv/environment, uploads ciphertext conditionally and cleans temporary output',t=>{
  let config,outputFile;
  const access={accessKeyId:'a'.repeat(32),secretAccessKey:'b'.repeat(64)};
  const ciphertext=sealArchive({'synthetic.txt':Buffer.from('synthetic')},identity.publicKey);
  const mocked=t.mock.method(childProcess,'spawnSync',(executable,args,options)=>{
    assert.match(executable,/^curl(?:\.exe)?$/);assert.deepEqual(args,['--disable','--config','-']);
    assert.ok(!JSON.stringify(args).includes(access.secretAccessKey));
    assert.ok(!JSON.stringify(options.env).includes(access.secretAccessKey));
    assert.equal(options.env.SOURCE_CAPABILITY,undefined);
    config=options.input;
    outputFile=config.match(/data-binary = "@([^"]+)"/)[1];
    assert.deepEqual(readFileSync(outputFile),ciphertext);
    return {status:0,stdout:Buffer.from('\n200')};
  });
  syncBuiltinESMExports();
  try {
    r2Request('c'.repeat(32),access,'PUT','daily/2026-10-18.elbbackup',ciphertext);
    assert.match(config,/If-None-Match: \*/);assert.ok(config.includes('x-amz-content-sha256: '+hash(ciphertext)));
    assert.equal(existsSync(outputFile),false);
  } finally {mocked.mock.restore();syncBuiltinESMExports();}
});

test('private workflow has no schedule, no billable artifacts/caches, bounded jobs and separated credentials',()=>{
  const workflow=JSON.parse(readFileSync(new URL('./daily-workflow.json',import.meta.url)));
  assert.deepEqual(Object.keys(workflow.on),['workflow_dispatch']);
  assert.deepEqual(workflow.permissions,{contents:'read'});
  assert.equal(workflow.concurrency['cancel-in-progress'],false);
  assert.match(workflow.jobs.upload.if,/github.event.repository.private == true/);
  assert.match(workflow.jobs.upload.if,/vars.BACKUP_ENABLED == 'true'/);
  assert.equal(workflow.jobs.readback.needs,'upload');
  for(const job of Object.values(workflow.jobs)) {
    assert.equal(job['runs-on'],'ubuntu-24.04');assert.equal(job['timeout-minutes'],5);
    for(const step of job.steps)if(step.uses) {
      assert.match(step.uses,/^actions\/(checkout|setup-node)@[a-f0-9]{40}$/);
      assert.ok(!step.with.cache);
      if(step.uses.startsWith('actions/checkout'))assert.equal(step.with['persist-credentials'],false);
    }
  }
  const upload=workflow.jobs.upload.steps.at(-1).env, readback=workflow.jobs.readback.steps.at(-1).env;
  assert.ok(upload.SOURCE_CAPABILITY);assert.equal(readback.SOURCE_CAPABILITY,undefined);
  assert.ok(upload.BACKUP_RESERVATION_TOKEN);assert.equal(readback.BACKUP_RESERVATION_TOKEN,undefined);
  assert.match(upload.R2_ACCESS_KEY_ID,/R2_UPLOAD_/);assert.match(readback.R2_ACCESS_KEY_ID,/R2_READBACK_/);
  assert.doesNotMatch(JSON.stringify(workflow),/MANAGEMENT|SERVICE_ROLE|RECOVERY|OWNER|SIGNING|upload-artifact|cache@/);
});
