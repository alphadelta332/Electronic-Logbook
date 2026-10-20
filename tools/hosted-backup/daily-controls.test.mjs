import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import controlWorker, { validateOwnerReview, reserveDailyAttempt, reserveDailyReadback, reserveWorkflowRequest, DailyAttemptGuard, dailySourceEgressBytes } from './daily-controls.mjs';
import { reserveComponentRequest, componentRequestLimits } from './daily-request-controls.mjs';
import { ownerReviewBudget } from './daily-cost-policy.mjs';

const sha=value=>createHash('sha256').update(value).digest('hex');
const start=Date.parse('2026-10-18T06:00:00Z');
const day=24*60*60*1000;
const attempt={version:2,jobSha256:'a'.repeat(64),sourceOrigin:'https://'+'b'.repeat(20)+'.supabase.co',
  repository:'alphadelta332/flightlogx-preview-backups',runId:'123',runAttempt:'1',commit:'c'.repeat(40)};
function makeReview(time=start) {
  const at=new Date(time).toISOString(),end=new Date(time+31*day).toISOString();
  return {version:2,approvedAt:at,expiresAt:end,weeklyCheckedAt:at,sourceExpiresAt:end,
    jobSha256:attempt.jobSha256,sourceOrigin:attempt.sourceOrigin,githubPaidActionsBlocked:true,supabasePlan:'free',
    egress:{used:1000,included:5e9},cachedEgress:{used:1000,included:5e9},
    github:{observedAt:at,periodStart:at,periodEnd:end,remainingLinuxMinutes:1997},
    r2:{observedAt:at,periodStart:at,periodEnd:end,storageGbMonths:0.01,requests:67,
      storageBillableGbMonths:0,classABillable:0,classBBillable:0,usageCostUsd:0}};
}
const review=makeReview();

function memoryStorage() {
  const values=new Map();let tail=Promise.resolve();
  return {values,transaction(action){
    const operation=tail.then(()=>action({get:async key=>structuredClone(values.get(key)),put:async(key,value)=>values.set(key,structuredClone(value))}));
    tail=operation.catch(()=>{});return operation;
  }};
}

test('all component ceilings are atomic, count failed attempts and survive rotation/restart without refund',async()=>{
  for(const [component,limit] of Object.entries(componentRequestLimits)) {
    const storage=memoryStorage();
    const results=await Promise.allSettled(Array.from({length:limit+16},()=>reserveComponentRequest(storage,component,start)));
    assert.equal(results.filter(r=>r.status==='fulfilled').length,limit);
    assert.equal(storage.values.get('component-requests').counts[component],limit);
    await assert.rejects(reserveComponentRequest(storage,component,start+1000),/ceiling/);
    await assert.rejects(reserveComponentRequest(storage,component,start-day),/backwards/);
    await reserveComponentRequest(storage,component,start+day);
    assert.equal(storage.values.get('component-requests').counts[component],1);
  }
  assert.equal(componentRequestLimits.collector+componentRequestLimits.workflow+componentRequestLimits.monitor,251);
  await assert.rejects(reserveComponentRequest(memoryStorage(),'unknown',start),/Unknown/);
});

test('workflow reserves each exact operation before I/O, rejects repeats/order/drift and retains legacy grants',async()=>{
  const storage=memoryStorage(),reservation=await reserveDailyAttempt(storage,attempt,review,start);
  const payload={...attempt,reservation,operation:'list-before'};
  await assert.rejects(reserveWorkflowRequest(storage,{...payload,operation:'upload'},review,start),/order/);
  await assert.rejects(reserveWorkflowRequest(storage,{...payload,runId:'999'},review,start),/differs/);
  await assert.rejects(reserveWorkflowRequest(storage,{...payload,operation:'delete'},review,start),/Unknown/);
  const claims=await Promise.allSettled(Array.from({length:16},()=>reserveWorkflowRequest(storage,payload,review,start)));
  assert.equal(claims.filter(r=>r.status==='fulfilled').length,1);
  assert.equal(storage.values.get('component-requests').counts.workflow,1);
  await reserveWorkflowRequest(storage,{...payload,operation:'list-after'},review,start);
  await reserveWorkflowRequest(storage,{...payload,operation:'upload'},review,start);
  await assert.rejects(reserveWorkflowRequest(storage,{...payload,operation:'readback'},review,start),/order/);
  await reserveDailyReadback(storage,payload,review,start);
  await reserveWorkflowRequest(storage,{...payload,operation:'readback'},review,start);
  await assert.rejects(reserveWorkflowRequest(storage,{...payload,operation:'readback'},review,start),/spent/);
  assert.equal(storage.values.get('source-budget').attempts,1);
  assert.equal(storage.values.get('component-requests').counts.workflow,4);
  const changed=structuredClone(review);changed.weeklyCheckedAt=new Date(start+1000).toISOString();
  await assert.rejects(reserveWorkflowRequest(storage,payload,changed,start+1000),/spent/);
  await assert.rejects(reserveWorkflowRequest(storage,payload,review,start+30*60*1000),/expired/);
});

test('public wrong method/route/body traffic never invokes the collector or changes counters',async()=>{
  const storage=memoryStorage(),guard=new DailyAttemptGuard({storage},{OWNER_REVIEW_JSON:JSON.stringify(review)});
  for(const request of [new Request('https://controls.example/daily-backup-permit'),
    new Request('https://controls.example/refresh',{method:'POST',body:'{}'}),
    new Request('https://controls.example/daily-backup-request',{method:'POST',body:'x'.repeat(2049)}),
    new Request('https://controls.example/daily-backup-readback',{method:'POST',body:'not-json'})]) {
    assert.equal((await guard.fetch(request)).status,409);
    assert.equal(storage.values.size,0);
  }
});

test('owner review denies expiry, oversized windows, wrong source/inputs, missing approval and insufficient budget',()=>{
  validateOwnerReview(review,attempt.jobSha256,attempt.sourceOrigin,start);
  for(const mutate of [
    r=>r.approvedAt=new Date(start+1).toISOString(),r=>r.expiresAt=new Date(start).toISOString(),
    r=>r.expiresAt=new Date(start+31*day+1).toISOString(),r=>r.jobSha256='d'.repeat(64),
    r=>r.sourceOrigin='https://'+'e'.repeat(20)+'.supabase.co',r=>r.githubPaidActionsBlocked=false,
    r=>r.supabasePlan='pro',r=>r.egress.included=31*dailySourceEgressBytes,r=>r.cachedEgress.used=5e9
  ]){
    const changed=structuredClone(review);mutate(changed);
    assert.throws(()=>validateOwnerReview(changed,attempt.jobSha256,attempt.sourceOrigin,start));
  }
});

test('atomic attempts survive concurrent callers, review rotation and a new guard instance',async()=>{
  const storage=memoryStorage();
  const results=await Promise.allSettled(Array.from({length:32},()=>reserveDailyAttempt(storage,attempt,review,start)));
  assert.equal(results.filter(r=>r.status==='fulfilled').length,1);
  assert.equal(results.filter(r=>r.status==='rejected').length,31);
  assert.equal(results.find(r=>r.status==='fulfilled').value.sourceEgressBytes,dailySourceEgressBytes);
  const renewed={...review,approvedAt:new Date(start+1000).toISOString(),weeklyCheckedAt:new Date(start+1000).toISOString()};
  await assert.rejects(reserveDailyAttempt(storage,attempt,renewed,start+1000),/already reserved/);
  await assert.rejects(reserveDailyAttempt(storage,{...attempt,runAttempt:'2'},review,start),/already reserved/);
  const second=await reserveDailyAttempt(storage,attempt,review,start+day);
  assert.equal(second.day,'2026-10-19');
  assert.equal(storage.values.size,1);
});

test('31-day window spanning 32 UTC dates still grants at most 31 attempts',async()=>{
  const storage=memoryStorage();
  await reserveDailyAttempt(storage,attempt,review,start);
  const nextMidnight=Date.parse('2026-10-19T00:00:00Z');
  for(let index=0;index<30;index++) {
    const time=nextMidnight+index*day;
    await reserveDailyAttempt(storage,attempt,{...review,weeklyCheckedAt:new Date(time).toISOString()},time);
  }
  const last=nextMidnight+30*day;
  await assert.rejects(reserveDailyAttempt(storage,attempt,{...review,weeklyCheckedAt:new Date(last).toISOString()},last),/exhausted/);
  assert.equal(storage.values.get('source-budget').attempts,31);
});

test('guard denies wrong method/token/input and persists the reservation before replying',async()=>{
  const now=Date.now(),token='f'.repeat(64),storage=memoryStorage();
  const current=makeReview(now-1000);
  const env={OWNER_REVIEW_JSON:JSON.stringify(current),BACKUP_RESERVATION_SHA256:sha(token)};
  const send=(guard,body=attempt,value=token)=>guard.fetch(new Request('https://controls.example/daily-backup-reservation',{
    method:'POST',headers:{'x-flightlogx-reservation-token':value},body:JSON.stringify(body)}));
  const guard=new DailyAttemptGuard({storage},env);
  assert.equal((await send(guard,attempt,'0'.repeat(64))).status,409);
  assert.equal((await send(guard,{...attempt,jobSha256:'0'.repeat(64)})).status,409);
  assert.equal(storage.values.size,0);
  const response=await send(guard);assert.equal(response.status,201);assert.equal(storage.values.size,1);
  assert.equal((await response.json()).reviewSha256,sha(JSON.stringify(ownerReviewBudget(current))));
  const restarted=new DailyAttemptGuard({storage},env);
  assert.equal((await send(restarted)).status,409);
});

test('readback is single-use across concurrent callers and review rotation without changing source attempts',async()=>{
  const storage=memoryStorage();
  const reservation=await reserveDailyAttempt(storage,attempt,review,start);
  const payload={...attempt,reservation};
  const rotated={...review,approvedAt:new Date(start+1000).toISOString(),weeklyCheckedAt:new Date(start+1000).toISOString()};
  await assert.rejects(reserveDailyReadback(storage,payload,rotated,start+1000),/Invalid/);
  const results=await Promise.allSettled(Array.from({length:32},()=>reserveDailyReadback(storage,payload,review,start+1000)));
  assert.equal(results.filter(result=>result.status==='fulfilled').length,1);
  assert.equal(results.find(result=>result.status==='fulfilled').value.reservationSha256,sha(JSON.stringify(reservation)));
  assert.equal(storage.values.get('source-budget').attempts,1);
  await assert.rejects(reserveDailyReadback(storage,payload,review,start+2000),/already used/);
  await assert.rejects(reserveDailyAttempt(storage,attempt,rotated,start+2000),/already reserved/);
});

test('missing, altered, legacy, future and expired readback grants do not consume the valid grant',async()=>{
  const storage=memoryStorage();
  const reservation=await reserveDailyAttempt(storage,attempt,review,start);
  const payload={...attempt,reservation};
  for(const mutate of [
    p=>delete p.reservation,p=>p.version=1,p=>p.reservation.version=1,
    p=>p.reservation.readbackToken='0'.repeat(64),p=>p.runId='999',p=>p.commit='d'.repeat(40),
    p=>p.reservation.day='2026-10-17',p=>p.reservation.reservedAt=new Date(start+2000).toISOString(),
    p=>p.reservation.reservedAt=new Date(start-30*60*1000).toISOString()
  ]){
    const changed=structuredClone(payload);mutate(changed);
    await assert.rejects(reserveDailyReadback(storage,changed,review,start+1000));
    assert.equal(storage.values.get('source-budget').readbackUsed,false);
  }
  await assert.rejects(reserveDailyReadback(storage,payload,review,start+30*60*1000),/expired/);
  await reserveDailyReadback(storage,payload,review,start+1000);
});

test('pre-upgrade ledger denies same-day reservation and unissued readback without resetting its counter',async()=>{
  const storage=memoryStorage();
  storage.values.set('source-budget',{day:'2026-10-18',reviewSha256:'e'.repeat(64),attempts:7});
  await assert.rejects(reserveDailyAttempt(storage,attempt,review,start),/already reserved/);
  const other=memoryStorage();const reservation=await reserveDailyAttempt(other,attempt,review,start);
  await assert.rejects(reserveDailyReadback(storage,{...attempt,reservation},review,start),/missing/);
  await reserveDailyAttempt(storage,attempt,review,start+day);
  assert.equal(storage.values.get('source-budget').attempts,8);
});

test('worker rejects unauthenticated source reservations before reaching durable storage',async()=>{
  const env={BACKUP_RESERVATION_SHA256:sha('f'.repeat(64)),DAILY_ATTEMPTS:{get(){assert.fail('Unauthenticated request reached storage');}}};
  const response=await controlWorker.fetch(new Request('https://controls.example/daily-backup-reservation',{
    method:'POST',headers:{'x-flightlogx-reservation-token':'0'.repeat(64)},body:'{}'}),env);
  assert.equal(response.status,409);
});

test('weekly acknowledgement preserves the grant identity and accumulated source attempts',async()=>{
  const storage=memoryStorage();
  const reservation=await reserveDailyAttempt(storage,attempt,review,start);
  const acknowledged={...review,weeklyCheckedAt:new Date(start+1000).toISOString()};
  await reserveDailyReadback(storage,{...attempt,reservation},acknowledged,start+1000);
  await assert.rejects(reserveDailyAttempt(storage,attempt,acknowledged,start+1000),/already reserved/);
  const next=await reserveDailyAttempt(storage,attempt,{...acknowledged,weeklyCheckedAt:new Date(start+day).toISOString()},start+day);
  assert.equal(next.reviewSha256,reservation.reviewSha256);
  assert.equal(storage.values.get('source-budget').attempts,2);
});

test('an exhausted legacy budget cannot be revived by weekly checks but a genuinely new baseline can renew it',async()=>{
  const storage=memoryStorage();
  storage.values.set('source-budget',{day:'2026-10-18',reviewSha256:'e'.repeat(64),attempts:31});
  const acknowledged={...review,weeklyCheckedAt:new Date(start+day).toISOString()};
  await assert.rejects(reserveDailyAttempt(storage,attempt,acknowledged,start+day),/exhausted/);
  assert.equal(storage.values.get('source-budget').attempts,31);
  await reserveDailyAttempt(storage,attempt,makeReview(start+day),start+day);
  assert.equal(storage.values.get('source-budget').attempts,1);
  assert.equal(storage.values.get('source-budget').day,'2026-10-19');
});

test('first activation cannot use old baselines even with a fresh weekly acknowledgement',async()=>{
  const storage=memoryStorage(),later=start+2*day;
  const acknowledged={...review,weeklyCheckedAt:new Date(later).toISOString()};
  validateOwnerReview(acknowledged,attempt.jobSha256,attempt.sourceOrigin,later);
  await assert.rejects(reserveDailyAttempt(storage,attempt,acknowledged,later),/within 24 hours/);
  assert.equal(storage.values.size,0);
  await reserveDailyAttempt(storage,attempt,makeReview(later),later);
  assert.equal(storage.values.get('source-budget').attempts,1);
});

test('a new source day supersedes the old readback grant without spending the new one',async()=>{
  const storage=memoryStorage();
  const midnight=Date.parse('2026-10-19T00:00:00Z');
  const yesterday=await reserveDailyAttempt(storage,attempt,review,midnight-10*60*1000);
  const today=await reserveDailyAttempt(storage,attempt,review,midnight);
  await assert.rejects(reserveDailyReadback(storage,{...attempt,reservation:yesterday},review,midnight),/missing/);
  assert.equal(storage.values.get('source-budget').readbackUsed,false);
  await reserveDailyReadback(storage,{...attempt,reservation:today},review,midnight);
  assert.equal(storage.values.get('source-budget').attempts,2);
});
