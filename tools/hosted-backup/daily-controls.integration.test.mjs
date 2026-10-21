import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:net';

test('local Cloudflare SQLite grants one source/readback claim each and preserves both across restart',async()=>{
  const directory=mkdtempSync(join(tmpdir(),'elb-daily-controls-'));
  const socket=createServer();await new Promise(resolve=>socket.listen(0,'127.0.0.1',resolve));
  const port=socket.address().port;await new Promise(resolve=>socket.close(resolve));
  const token='a'.repeat(64),jobSha256='b'.repeat(64),sourceOrigin='https://'+'c'.repeat(20)+'.supabase.co';
  const at=new Date(Date.now()-1000).toISOString(),end=new Date(Date.now()+31*24*60*60*1000-1000).toISOString();
  const review={version:2,jobSha256,sourceOrigin,approvedAt:at,weeklyCheckedAt:at,sourceExpiresAt:end,
    expiresAt:new Date(Date.now()+24*60*60*1000).toISOString(),githubPaidActionsBlocked:true,supabasePlan:'free',
    egress:{used:1,included:5e9},cachedEgress:{used:1,included:5e9},
    github:{observedAt:at,periodStart:at,periodEnd:end,remainingLinuxMinutes:1997},
    r2:{observedAt:at,periodStart:at,periodEnd:end,storageGbMonths:0.01,requests:67,
      storageBillableGbMonths:0,classABillable:0,classBBillable:0,usageCostUsd:0}};
  const config=JSON.parse(readFileSync(new URL('./daily-controls.wrangler.json',import.meta.url)));
  // Synthetic-only entrypoint exercises internal counters/cache locks without exposing them in the real Worker.
  const harness=join(directory,'worker.mjs');
  writeFileSync(harness,`
    import router, { DailyAttemptGuard as Base } from ${JSON.stringify(fileURLToPath(new URL('./daily-controls.mjs',import.meta.url)))};
    import { reserveComponentRequest, refreshProviderEvidence, cachedProviderEvidence } from ${JSON.stringify(fileURLToPath(new URL('./daily-request-controls.mjs',import.meta.url)))};
    import { checkBackupMonitor } from ${JSON.stringify(fileURLToPath(new URL('./daily-monitor.mjs',import.meta.url)))};
    import { cachedBackupPermit, checkPrimaryMonitor } from ${JSON.stringify(fileURLToPath(new URL('./daily-primary.mjs',import.meta.url)))};
    import { sendBackupAlert } from ${JSON.stringify(fileURLToPath(new URL('./daily-alert-delivery.mjs',import.meta.url)))};
    import { inspectBackupObject } from ${JSON.stringify(fileURLToPath(new URL('./daily-monitor-observation.mjs',import.meta.url)))};
    const policy=${readFileSync(new URL('./r2-policy.json',import.meta.url),'utf8')};
    const now=${Date.now()}, day=24*60*60*1000;
    const config={version:2,githubUser:'alphadelta332',accountId:'a'.repeat(32),supabaseOrganization:'fixture-org',
      projectRef:'b'.repeat(20),bucket:policy.bucket,policy,
      r2Period:{periodStart:new Date(now-day).toISOString(),periodEnd:new Date(now+day).toISOString()},
      retiredBucketReview:{bucket:'retired-fixture-bucket',observedAt:new Date(now-1000).toISOString(),
        zeroStorageAt:new Date(now-60000).toISOString()},
      deploymentRevisions:['airport-catalog','recovery-envelope'].map(slug=>({slug,version:1,updated_at:123}))};
    const credentials={github:'g'.repeat(30),cloudflare:'c'.repeat(30),supabaseProject:'s'.repeat(30),supabaseOrganization:'o'.repeat(30)};
    let calls=0;
    const provider=async(url,options)=>{
      calls++;
      if(url==='https://api.resend.com/emails')return Response.json({id:'01234567-89ab-cdef-0123-456789abcdef'});
      if(url.includes('/actions/'))return Response.json({total_count:0,workflow_runs:[]});
      if(url.endsWith('/user'))return Response.json({login:config.githubUser,plan:{name:'free'}});
      if(url.includes('github.com'))return Response.json({user:config.githubUser,
        timePeriod:{year:new Date(now).getUTCFullYear(),month:new Date(now).getUTCMonth()+1},usageItems:[]});
      if(url.includes('/organizations/'))return Response.json({id:config.supabaseOrganization,plan:'free'});
      if(url.endsWith('/functions'))return Response.json(config.deploymentRevisions.map(item=>({...item,status:'ACTIVE'})));
      if(url.endsWith('/graphql')){
        const account={},variables=JSON.parse(options.body).variables;
        for(let i=0;variables['from'+i];i++){
          account['operations'+i]=['PutBucket','DeleteBucket'].map(actionType=>({sum:{requests:1},
            dimensions:{actionType,bucketName:config.retiredBucketReview.bucket}}));
          account['storage'+i]=[{max:{payloadSize:1500,metadataSize:129,uploadCount:0},
            dimensions:{bucketName:config.bucket,datetime:new Date(now-60000).toISOString()}},
            {max:{payloadSize:0,metadataSize:0,uploadCount:0},dimensions:{bucketName:config.retiredBucketReview.bucket,
              datetime:config.retiredBucketReview.zeroStorageAt}}];
        }
        return Response.json({data:{viewer:{accounts:[account]}}});
      }
      const wrap=result=>Response.json({success:true,errors:[],result});
      if(url.endsWith('/entitlements'))return wrap(['r2.enabled','r2.fedramp','r2.infrequent_access'].map(key=>
        ({id:key,feature:{key},allocation:{type:'bool',value:key!=='r2.fedramp'},deleted_date:''})));
      if(url.includes('/r2/buckets?')){
        const jurisdiction=options.headers['cf-r2-jurisdiction'];
        const buckets=jurisdiction==='default'?[{name:config.bucket,creation_date:'2026-10-17T03:58:25Z'}]:[];
        return Response.json({success:true,errors:[],result:{buckets},result_info:{total_count:buckets.length}});
      }
      if(url.endsWith('/metrics')){
        const zero={objects:0,payloadSize:0,metadataSize:0};
        return wrap({standard:{published:{...zero,objects:1,payloadSize:1500,metadataSize:129},uploaded:zero},
          infrequentAccess:{published:zero,uploaded:zero}});
      }
      if(url.endsWith('/lock'))return wrap(policy.lock);
      if(url.endsWith('/lifecycle'))return wrap(policy.lifecycle);
      if(url.endsWith('/domains/managed'))return wrap({enabled:false});
      if(url.endsWith('/domains/custom'))return wrap({domains:[]});
      if(url.endsWith('/r2/buckets/'+config.bucket))return wrap({name:config.bucket,jurisdiction:'default',storage_class:'Standard'});
      throw new Error('Unreviewed synthetic request');
    };
    export class DailyAttemptGuard extends Base {
      async fetch(request) {
        const route=new URL(request.url).pathname;
        if(!route.startsWith('/test-'))return super.fetch(request);
        const payload=await request.json();
        if(route==='/test-state')return Response.json({requests:await this.storage.get('component-requests'),
          cache:await this.storage.get('provider-cache'),source:await this.storage.get('source-budget'),monitor:await this.storage.get('monitor-state')});
        try {
          if(['/test-primary','/test-permit'].includes(route)) {
            const review=JSON.parse(this.env.OWNER_REVIEW_JSON);
            review.sourceOrigin='https://'+config.projectRef+'.supabase.co';
            review.r2.periodStart=config.r2Period.periodStart;review.r2.periodEnd=config.r2Period.periodEnd;
            const history={workflowId:1,workflowPath:'.github/workflows/daily-backup.yml',branch:'main',
              commit:'d'.repeat(40),jobSha256:review.jobSha256,accountId:config.accountId};
            const primaryEnv={...this.env,MONITOR_ENABLED:'true',BACKUP_ENABLED:'true',OWNER_REVIEW_JSON:JSON.stringify(review),
              PRIMARY_MONITOR_JSON:JSON.stringify({provider:config,history}),
              PROVIDER_CREDENTIALS_JSON:JSON.stringify(credentials),
              MONITOR_CREDENTIALS_JSON:JSON.stringify({github:'g'.repeat(30)}),
              ALERT_DELIVERY_JSON:JSON.stringify({version:1,recipient:'owner@example.test'}),RESEND_SEND_TOKEN:'re_'+'a'.repeat(30),
              MONITOR_HEARTBEAT:{completed:async()=>{await this.storage.put('test-heartbeat',true);}}};
            if(route==='/test-permit')return Response.json({permit:await cachedBackupPermit(this.storage,primaryEnv,payload.now),calls});
            const result=await checkPrimaryMonitor(this.storage,primaryEnv,provider,()=>payload.now);
            return Response.json({result,calls,heartbeat:await this.storage.get('test-heartbeat')});
          }
          if(route==='/test-monitor')return Response.json(await checkBackupMonitor(this.storage,null,async (r2,github)=>{
            for(let i=0;i<5;i++)await github(async()=>{});
            await inspectBackupObject('a'.repeat(32),{accessKeyId:'b'.repeat(32),secretAccessKey:'c'.repeat(64)},
              {version:2,status:'verified',key:'daily/2026-10-20.elbbackup',bytes:500,
                sha256:'${createHash('sha256').update(Buffer.alloc(500,42)).digest('hex')}',verifiedAt:new Date(payload.now).toISOString()},
              r2,async()=>new Response(new Uint8Array(500).fill(42)),payload.now);
            await r2(async()=>{});
            return {version:1,observedAt:new Date(payload.now).toISOString(),usage:'allowed',lastRun:null,lastVerified:null};
          },message=>sendBackupAlert(this.storage,{version:1,recipient:'owner@example.test'},'re_'+'a'.repeat(30),message,
            async()=>{if(payload.fail)throw new Error('Synthetic lost delivery');return Response.json({id:'01234567-89ab-cdef-0123-456789abcdef'});},
            ()=>payload.now),()=>payload.now));
          if(route==='/test-cache')return Response.json({evidence:await cachedProviderEvidence(this.storage,payload.changed ?
            {...config,retiredBucketReview:{...config.retiredBucketReview,zeroStorageAt:new Date(now-120000).toISOString()}} : config,payload.now),calls});
          if(route==='/test-refresh-valid')return Response.json(await refreshProviderEvidence(this.storage,config,credentials,provider,()=>payload.now),{status:201});
          if(route==='/test-component')await reserveComponentRequest(this.storage,payload.component,payload.now);
          else await refreshProviderEvidence(this.storage,{}, {},()=>{throw new Error('No provider I/O allowed');},()=>payload.now);
          return new Response('',{status:201});
        } catch {return new Response('',{status:409});}
      }
    }
    export default {fetch(request,env) {
      if(new URL(request.url).pathname.startsWith('/test-'))
        return env.DAILY_ATTEMPTS.get(env.DAILY_ATTEMPTS.idFromName('flightlogx-preview-source')).fetch(request);
      return router.fetch(request,env);
    }};
  `);
  config.main=harness;
  config.services=[]; // This isolated primary fixture deliberately has no observer deployment.
  config.vars={OWNER_REVIEW_JSON:JSON.stringify(review),BACKUP_RESERVATION_SHA256:createHash('sha256').update(token).digest('hex')};
  const configFile=join(directory,'wrangler.json');writeFileSync(configFile,JSON.stringify(config));
  let child,closed,log='';
  const url=`http://127.0.0.1:${port}/daily-backup-reservation`;
  const payload={version:2,jobSha256,sourceOrigin,repository:'alphadelta332/flightlogx-preview-backups',
    runId:'123',runAttempt:'1',commit:'d'.repeat(40)};
  const send=(value=token,body=payload)=>fetch(url,{method:'POST',headers:{'x-flightlogx-reservation-token':value},
    body:JSON.stringify(body),signal:AbortSignal.timeout(10000)});
  const start=async()=>{
    log='';child=spawn(process.execPath,[join(process.env.APPDATA,'npm/node_modules/wrangler/bin/wrangler.js'),
      'dev','--local','--config',configFile,'--ip','127.0.0.1','--port',String(port),'--persist-to',join(directory,'state')],{
      windowsHide:true,stdio:['pipe','pipe','pipe'],env:{SystemRoot:process.env.SystemRoot,PATH:process.env.PATH,
        TEMP:process.env.TEMP,TMP:process.env.TMP,USERPROFILE:process.env.USERPROFILE,APPDATA:process.env.APPDATA,
        LOCALAPPDATA:process.env.LOCALAPPDATA,WRANGLER_SEND_METRICS:'false',CI:'true',BROWSER:'none'}
    });
    closed=new Promise(resolve=>child.once('close',resolve));
    for(const stream of [child.stdout,child.stderr])stream.on('data',part=>{log=(log+part.toString()).slice(-8000);});
    let ready=false;
    for(let index=0;index<100;index++){
      if(child.exitCode!==null)break;
      try{const response=await fetch(url,{signal:AbortSignal.timeout(500)});if(response.status===404){ready=true;break;}}catch{}
      await new Promise(resolve=>setTimeout(resolve,150));
    }
    assert.ok(ready,'Synthetic local Worker failed to start: '+log);
  };
  const stop=async()=>{
    if(!child)return;
    child.stdin.end();
    if(child.exitCode===null){
      if(process.platform==='win32')spawnSync('taskkill.exe',['/PID',String(child.pid),'/T','/F'],{windowsHide:true,timeout:10000});
      else child.kill();
    }
    let timer;
    try{
      const ended=await Promise.race([closed.then(()=>true),new Promise(resolve=>{timer=setTimeout(()=>resolve(false),10000);})]);
      assert.ok(ended,'Local Worker process did not stop.');child=undefined;
    }finally{clearTimeout(timer);}
  };
  try{
    await start();
    assert.equal((await send('0'.repeat(64))).status,409);
    assert.equal((await send(token,{...payload,jobSha256:'e'.repeat(64)})).status,409);
    const responses=await Promise.all(Array.from({length:16},()=>send()));
    assert.equal(responses.filter(r=>r.status===201).length,1);
    assert.equal(responses.filter(r=>r.status===409).length,15);
    const receipt=await responses.find(r=>r.status===201).json();assert.equal(receipt.sourceEgressBytes,64*1024*1024);
    const requestStorage=operation=>fetch(url.replace('/daily-backup-reservation','/daily-backup-request'),{
      method:'POST',body:JSON.stringify({...payload,reservation:receipt,operation}),signal:AbortSignal.timeout(5000)});
    const listClaims=await Promise.all(Array.from({length:16},()=>requestStorage('list-before')));
    assert.equal(listClaims.filter(r=>r.status===201).length,1);
    assert.equal((await requestStorage('list-after')).status,201);
    assert.equal((await requestStorage('upload')).status,201);
    const internal=(route,body={})=>fetch(url.replace('/daily-backup-reservation',route),{
      method:'POST',body:JSON.stringify(body),signal:AbortSignal.timeout(10000)});
    const syntheticTime=Math.floor(Date.now()/(24*60*60*1000))*24*60*60*1000-2*24*60*60*1000;
    const monitors=await Promise.all(Array.from({length:24},()=>internal('/test-component',{component:'monitor',now:Date.now()-24*60*60*1000})));
    // The production workflow clock is later, so use that same current day for the shared counter.
    assert.equal(monitors.filter(r=>r.status===201).length,0);
    const currentTime=Date.now();
    const currentMonitors=await Promise.all(Array.from({length:24},()=>internal('/test-component',{component:'monitor',now:currentTime})));
    assert.equal(currentMonitors.filter(r=>r.status===201).length,12);
    await Promise.all(Array.from({length:16},()=>internal('/test-refresh',{now:syntheticTime})));
    let state=await (await internal('/test-state')).json();
    assert.equal(state.cache.batches,1);assert.equal(state.requests.counts.workflow,3);assert.equal(state.source.attempts,1);
    await stop();await start();
    assert.equal((await send()).status,409);
    assert.equal((await requestStorage('list-before')).status,409);
    assert.equal((await internal('/test-component',{component:'monitor',now:currentTime})).status,409);
    await internal('/test-refresh',{now:syntheticTime});
    state=await (await internal('/test-state')).json();assert.equal(state.cache.batches,1);
    for(let index=1;index<=21;index++)await internal('/test-refresh',{now:syntheticTime+index*60*60*1000});
    state=await (await internal('/test-state')).json();assert.equal(state.cache.batches,21);
    const readback=body=>fetch(url.replace('/daily-backup-reservation','/daily-backup-readback'),{
      method:'POST',body:JSON.stringify(body),signal:AbortSignal.timeout(5000)});
    const claim={...payload,reservation:receipt};
    assert.equal((await readback({...claim,reservation:{...receipt,readbackToken:'0'.repeat(64)}})).status,409);
    const claims=await Promise.all(Array.from({length:16},()=>readback(claim)));
    assert.equal(claims.filter(response=>response.status===201).length,1);
    assert.equal(claims.filter(response=>response.status===409).length,15);
    assert.equal((await requestStorage('readback')).status,201);
    await stop();await start();
    assert.equal((await readback(claim)).status,409);
    assert.equal((await send()).status,409);
    assert.equal((await requestStorage('readback')).status,409);
    state=await (await internal('/test-state')).json();
    assert.equal(state.requests.counts.workflow,4);assert.equal(state.requests.counts.monitor,12);
    assert.equal(state.cache.batches,21);assert.equal(state.source.attempts,1);
    const collectedAt=Date.now();
    // Exercise actual collection/history/delivery/heartbeat through the primary composition.
    const primaries=await Promise.all(Array.from({length:16},()=>internal('/test-primary',{now:collectedAt})));
    assert.equal(primaries.filter(r=>r.status===200).length,1);
    const primary=await primaries.find(r=>r.status===200).json();
    assert.deepEqual(primary.result,{conditions:['missing-run','review-expiring','stale-backup'],delivery:'accepted'});
    assert.equal(primary.calls,18);assert.equal(primary.heartbeat,true);
    state=await (await internal('/test-state')).json();
    const evidence=state.cache.evidence;
    assert.deepEqual(evidence.r2.windows.map(window=>window.requests),[2,2]);
    assert.equal(evidence.r2.retiredBucketReview.bucket,'retired-fixture-bucket');
    assert.deepEqual(evidence.r2.retiredBucketReview.windows,[{PutBucket:1,DeleteBucket:1,zeroStorageSamples:1},
      {PutBucket:1,DeleteBucket:1,zeroStorageSamples:1}]);
    const before=await (await internal('/test-cache',{now:collectedAt+1000})).json();
    assert.deepEqual(before.evidence,evidence);assert.equal(before.calls,18);
    const savedState=await (await internal('/test-state')).json();
    const firstPermit=await (await internal('/test-permit',{now:collectedAt+1000})).json();
    assert.equal(firstPermit.calls,18);assert.equal(firstPermit.permit.version,3);
    assert.equal(firstPermit.permit.checkedAt,evidence.retrievedAt);
    assert.deepEqual(firstPermit.permit.deploymentRevisions,evidence.supabase.deploymentRevisions);
    assert.equal(firstPermit.permit.r2.policySha256,createHash('sha256').update(JSON.stringify(JSON.parse(readFileSync(new URL('./r2-policy.json',import.meta.url))))).digest('hex'));
    assert.deepEqual(await (await internal('/test-state')).json(),savedState);
    await stop();await start();
    const after=await (await internal('/test-cache',{now:collectedAt+1000})).json();
    assert.deepEqual(after.evidence,evidence);assert.equal(after.calls,0,'Cache read after process restart must make no provider calls.');
    assert.equal((await internal('/test-cache',{now:collectedAt+1000,changed:true})).status,409);
    const restartedPermit=await (await internal('/test-permit',{now:collectedAt+1000})).json();
    assert.deepEqual(restartedPermit.permit,firstPermit.permit);assert.equal(restartedPermit.calls,0);
    assert.deepEqual(await (await internal('/test-state')).json(),savedState);
    assert.equal((await internal('/test-permit',{now:collectedAt+2*60*60*1000})).status,409);
    assert.equal((await internal('/test-refresh-valid',{now:collectedAt+1000})).status,409);
    assert.equal((await internal('/test-cache',{now:collectedAt+2*60*60*1000})).status,409);
    state=await (await internal('/test-state')).json();
    assert.deepEqual(state.requests.counts,{workflow:4,monitor:12,collector:11,github:2,supabase:2,monitorGithub:2});
    assert.equal(state.cache.batches,1);assert.equal(state.source.attempts,1);
    assert.equal((await internal('/test-primary',{now:collectedAt+2000})).status,409);
    state=await (await internal('/test-state')).json();
    assert.equal(state.monitor.checks,1);assert.equal(state.monitor.resend.attempt,1);
    assert.equal(state.requests.counts.monitorGithub,2);assert.equal(state.cache.batches,1);
    assert.equal((await fetch(url.replace('/daily-backup-reservation','/internal-monitor'),{method:'POST'})).status,404);
    const monitorTime=Math.floor(Date.now()/(24*60*60*1000))*24*60*60*1000+2*24*60*60*1000+20*60*60*1000;
    assert.equal((await internal('/test-monitor',{now:monitorTime,fail:true})).status,409);
    state=await (await internal('/test-state')).json();
    const incident=state.monitor.incident;
    assert.equal(state.monitor.dailyAttempts,1);assert.equal(state.requests.counts.monitor,2);
    // Rotate configuration/token and restart the real local process without replacing its SQLite namespace.
    await stop();
    config.vars.BACKUP_RESERVATION_SHA256=createHash('sha256').update('e'.repeat(64)).digest('hex');
    config.vars.OWNER_REVIEW_JSON=JSON.stringify({...review,weeklyCheckedAt:new Date().toISOString()});
    writeFileSync(configFile,JSON.stringify(config));await start();
    assert.equal((await internal('/test-monitor',{now:monitorTime+4*60*60*1000,fail:true})).status,409);
    state=await (await internal('/test-state')).json();
    assert.equal(state.monitor.incident,incident);assert.equal(state.monitor.dailyAttempts,1);
    assert.equal(state.monitor.monthlyAttempts,3);assert.equal(state.requests.counts.monitor,2);
    assert.equal(state.requests.counts.monitorGithub,5);
    assert.equal(state.monitor.resend.attempt,2);assert.match(state.monitor.resend.bodySha256,/^[a-f0-9]{64}$/);
    const wireDigest=state.monitor.resend.bodySha256;
    await stop();await start();
    for(let index=2;index<=6;index++){
      const result=await (await internal('/test-monitor',{now:monitorTime+index*4*60*60*1000})).json();
      assert.equal(result.delivery,'suppressed');
      assert.deepEqual(result.conditions,['missing-run','review-required','stale-backup']);
    }
    state=await (await internal('/test-state')).json();
    assert.equal(state.requests.counts.monitor,12);assert.equal(state.monitor.dailyAttempts,1);
    assert.equal(state.requests.counts.monitorGithub,30);
    assert.equal(state.monitor.checks,6);assert.equal(state.monitor.incident,incident);
    assert.equal(state.monitor.resend.bodySha256,wireDigest);assert.equal(state.monitor.resend.attempt,2);
    assert.equal(state.source.attempts,1);assert.equal(state.cache.batches,1);
  }finally{
    await stop();
    await assert.rejects(fetch(url,{signal:AbortSignal.timeout(500)}),'Synthetic Worker must release its listening port.');
    // Preserve synthetic SQLite state as local test evidence; never point this harness at a live namespace.
  }
});
