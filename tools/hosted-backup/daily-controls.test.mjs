import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { validateOwnerReview, reserveDailyAttempt, DailyAttemptGuard, dailySourceEgressBytes } from './daily-controls.mjs';

const sha=value=>createHash('sha256').update(value).digest('hex');
const start=Date.parse('2026-10-18T06:00:00Z');
const day=24*60*60*1000;
const attempt={version:1,jobSha256:'a'.repeat(64),sourceOrigin:'https://'+'b'.repeat(20)+'.supabase.co',
  repository:'alphadelta332/flightlogx-preview-backups',runId:'123',runAttempt:'1',commit:'c'.repeat(40)};
const review={version:1,approvedAt:new Date(start).toISOString(),expiresAt:new Date(start+31*day).toISOString(),
  jobSha256:attempt.jobSha256,sourceOrigin:attempt.sourceOrigin,githubPaidActionsBlocked:true,supabasePlan:'free',
  egress:{used:1000,included:5e9},cachedEgress:{used:1000,included:5e9}};

function memoryStorage() {
  const values=new Map();let tail=Promise.resolve();
  return {values,transaction(action){
    const operation=tail.then(()=>action({get:async key=>structuredClone(values.get(key)),put:async(key,value)=>values.set(key,structuredClone(value))}));
    tail=operation.catch(()=>{});return operation;
  }};
}

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
  const renewed={...review,approvedAt:new Date(start+1000).toISOString(),expiresAt:new Date(start+31*day+1000).toISOString()};
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
  for(let index=0;index<30;index++)await reserveDailyAttempt(storage,attempt,review,nextMidnight+index*day);
  await assert.rejects(reserveDailyAttempt(storage,attempt,review,nextMidnight+30*day),/exhausted/);
  assert.equal(storage.values.get('source-budget').attempts,31);
});

test('guard denies wrong method/token/input and persists the reservation before replying',async()=>{
  const now=Date.now(),token='f'.repeat(64),storage=memoryStorage();
  const current={...review,approvedAt:new Date(now-1000).toISOString(),expiresAt:new Date(now+day).toISOString()};
  const env={OWNER_REVIEW_JSON:JSON.stringify(current),BACKUP_RESERVATION_SHA256:sha(token)};
  const send=(guard,body=attempt,value=token)=>guard.fetch(new Request('https://controls.example/daily-backup-reservation',{
    method:'POST',headers:{'x-flightlogx-reservation-token':value},body:JSON.stringify(body)}));
  const guard=new DailyAttemptGuard({storage},env);
  assert.equal((await send(guard,attempt,'0'.repeat(64))).status,409);
  assert.equal((await send(guard,{...attempt,jobSha256:'0'.repeat(64)})).status,409);
  assert.equal(storage.values.size,0);
  const response=await send(guard);assert.equal(response.status,201);assert.equal(storage.values.size,1);
  assert.equal((await response.json()).reviewSha256,sha(JSON.stringify(current)));
  const restarted=new DailyAttemptGuard({storage},env);
  assert.equal((await send(restarted)).status,409);
});
