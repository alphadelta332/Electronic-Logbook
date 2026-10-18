import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {sealArchive} from './archive.mjs';
import {airportFiles} from './orchestrate.mjs';

export async function captureLogicalArchive({origin,publishableKey,capability,recipient,contract,sources,sourceManifest}) {
  assert.match(origin,/^https:\/\/[a-z]{20}\.supabase\.co$/);
  assert.match(capability,/^[a-f0-9]{64}$/);assert.ok(publishableKey.startsWith('sb_publishable_'));
  const response=await fetch(origin+'/rest/v1/rpc/elb_backup_snapshot',{method:'GET',headers:{apikey:publishableKey,
    'x-flightlogx-backup-token':capability},redirect:'error',signal:AbortSignal.timeout(30000)});
  assert.equal(response.status,200,'Hosted capability export failed; response suppressed.');assert.ok(response.body);
  const chunks=[];let size=0;
  for await(const part of response.body){size+=part.length;assert.ok(size<=32*1024*1024,'Logical export exceeds the approved limit.');chunks.push(part);}
  const bytes=Buffer.concat(chunks);const snapshot=JSON.parse(bytes);
  assert.deepEqual(snapshot.shape,contract.shape);assert.deepEqual(snapshot.datasets.map(d=>d.table),contract.shape.map(t=>t.table));
  assert.deepEqual(snapshot.context.method,'GET');assert.equal(snapshot.context.readOnly,'on');assert.equal(snapshot.context.isolation,'read committed');
  assert.deepEqual(snapshot.generated,contract.generatedExpressions);
  assert.deepEqual(snapshot.ledgers,{auth:contract.ledgers.auth,storage:contract.ledgers.storage,application:contract.ledgers.application});
  const rows=name=>snapshot.datasets.find(d=>d.table===name).rows.map(r=>JSON.parse(r));
  const inventory={buckets:rows('storage.buckets'),objects:rows('storage.objects').map(o=>({bucket:o.bucket_id,name:o.name,metadata:o.metadata}))};
  const references=await airportFiles(origin,inventory);
  const files=Object.fromEntries(Object.entries(sources).map(([n,b])=>[n,Buffer.from(b,'base64')]));
  try {
    for(const file of sourceManifest.files)assert.equal(createHash('sha256').update(files[file.name]).digest('hex'),file.sha256);
    const verification={version:1,accessMode:'logical-capability',capturedAt:snapshot.context.capturedAt,contract,
      storage:inventory,sources:sourceManifest,files:Object.entries({...files,...references}).map(([name,b])=>({name,sha256:createHash('sha256').update(b).digest('hex')})),
      limits:'Logical data recovery; service configuration/Google acceptance remain required.'};
    return sealArchive({...files,...references,'logical.json':bytes,'verification.json':Buffer.from(JSON.stringify(verification))},recipient);
  } finally {bytes.fill(0);chunks.forEach(b=>b.fill(0));Object.values(files).forEach(b=>b.fill(0));Object.values(references).forEach(b=>b.fill(0));}
}

if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
  try {let input='';for await(const b of process.stdin){input+=b;assert.ok(input.length<=48*1024*1024);}const request=JSON.parse(input);input='';
    process.stdout.write(await captureLogicalArchive(request));
  }catch{console.error('Logical export client failed; no credential, response or record diagnostics printed.');process.exitCode=1;}
}
