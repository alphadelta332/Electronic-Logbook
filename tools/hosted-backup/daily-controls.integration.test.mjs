import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:net';

test('local Cloudflare SQLite Durable Object grants one concurrent reservation and retains it after restart',async()=>{
  const directory=mkdtempSync(join(tmpdir(),'elb-daily-controls-'));
  const socket=createServer();await new Promise(resolve=>socket.listen(0,'127.0.0.1',resolve));
  const port=socket.address().port;await new Promise(resolve=>socket.close(resolve));
  const token='a'.repeat(64),jobSha256='b'.repeat(64),sourceOrigin='https://'+'c'.repeat(20)+'.supabase.co';
  const review={version:1,jobSha256,sourceOrigin,approvedAt:new Date(Date.now()-1000).toISOString(),
    expiresAt:new Date(Date.now()+24*60*60*1000).toISOString(),githubPaidActionsBlocked:true,supabasePlan:'free',
    egress:{used:1,included:5e9},cachedEgress:{used:1,included:5e9}};
  const config=JSON.parse(readFileSync(new URL('./daily-controls.wrangler.json',import.meta.url)));
  config.main=fileURLToPath(new URL('./daily-controls-worker.mjs',import.meta.url));
  config.vars={OWNER_REVIEW_JSON:JSON.stringify(review),BACKUP_RESERVATION_SHA256:createHash('sha256').update(token).digest('hex')};
  const configFile=join(directory,'wrangler.json');writeFileSync(configFile,JSON.stringify(config));
  let child,closed,log='';
  const url=`http://127.0.0.1:${port}/daily-backup-reservation`;
  const payload={version:1,jobSha256,sourceOrigin,repository:'alphadelta332/flightlogx-preview-backups',
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
    await stop();await start();
    assert.equal((await send()).status,409);
  }finally{
    await stop();
    await assert.rejects(fetch(url,{signal:AbortSignal.timeout(500)}),'Synthetic Worker must release its listening port.');
    // Preserve synthetic SQLite state as local test evidence; never point this harness at a live namespace.
  }
});
