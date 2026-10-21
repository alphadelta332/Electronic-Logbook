import { validateOwnerReview, ownerReviewBudget } from './daily-cost-policy.mjs';
import { chargeComponentRequest } from './daily-request-controls.mjs';
import { cachedBackupPermit, checkPrimaryMonitor, runPrimaryMonitor } from './daily-primary.mjs';
export { validateOwnerReview } from './daily-cost-policy.mjs';
export const dailySourceEgressBytes = 64 * 1024 * 1024;
export const maximumReviewAttempts = 31;
const encoder = new TextEncoder();
const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',encoder.encode(value))))
  .map(byte=>byte.toString(16).padStart(2,'0')).join('');
const requireValue = (condition,message) => {if(!condition)throw new Error(message);};

function validateAttempt(request,review,now) {
  requireValue(request?.version===2,'Unsupported reservation request.');
  validateOwnerReview(review,request.jobSha256,request.sourceOrigin,now);
  requireValue(request.repository==='alphadelta332/flightlogx-preview-backups' &&
    /^[1-9][0-9]*$/.test(request.runId) && /^[1-9][0-9]*$/.test(request.runAttempt) && /^[a-f0-9]{40}$/.test(request.commit),
    'Unexpected reservation caller.');
}

export async function reserveDailyAttempt(storage,request,review,now=Date.now()) {
  validateAttempt(request,review,now);
  const reviewSha256=await digest(JSON.stringify(ownerReviewBudget(review)));
  const day=new Date(now).toISOString().slice(0,10);
  return storage.transaction(async txn=>{
    const last=await txn.get('source-budget');
    requireValue(!last || last.day<day,'The daily source attempt is already reserved.');
    if(last?.budgetVersion!==2 || last.reviewSha256!==reviewSha256)
      requireValue(['github','r2'].every(name=>now-Date.parse(review[name].observedAt)<=24*60*60*1000),
        'Activation or renewed budget needs billing baselines inspected within 24 hours.');
    const legacyBudget=last && last.budgetVersion!==2 && Date.parse(review.approvedAt)<Date.parse(last.day)+24*60*60*1000;
    const attempts=last && (legacyBudget || last.reviewSha256===reviewSha256) ? last.attempts+1 : 1;
    requireValue(attempts<=maximumReviewAttempts,'Owner review source budget is exhausted.');
    const readbackToken=Array.from(crypto.getRandomValues(new Uint8Array(32)))
      .map(byte=>byte.toString(16).padStart(2,'0')).join('');
    const receipt={version:2,status:'reserved',day,reservedAt:new Date(now).toISOString(),jobSha256:request.jobSha256,
      reviewSha256,sourceEgressBytes:dailySourceEgressBytes,repository:request.repository,runId:request.runId,
      runAttempt:request.runAttempt,commit:request.commit,readbackToken};
    // Keep the last day across review/source-input rotation; a new review cannot buy a second attempt today.
    await txn.put('source-budget',{day,reviewSha256,attempts,budgetVersion:2,reservationSha256:await digest(JSON.stringify(receipt)),readbackUsed:false});
    return receipt;
  });
}

export async function reserveDailyReadback(storage,request,review,now=Date.now()) {
  validateAttempt(request,review,now);
  const receipt=request.reservation;
  requireValue(receipt?.version===2 && receipt.status==='reserved' &&
    /^[a-f0-9]{64}$/.test(receipt.readbackToken??'') && receipt.reviewSha256===await digest(JSON.stringify(ownerReviewBudget(review))),
    'Invalid readback allowance.');
  for(const name of ['jobSha256','repository','runId','runAttempt','commit'])
    requireValue(receipt[name]===request[name],'Readback caller differs.');
  const age=now-Date.parse(receipt.reservedAt);
  requireValue(age>=0 && age<30*60*1000,'Readback allowance expired.');
  const reservationSha256=await digest(JSON.stringify(receipt));
  return storage.transaction(async txn=>{
    const last=await txn.get('source-budget');
    requireValue(last?.reservationSha256===reservationSha256 && last.day===receipt.day && last.readbackUsed===false,
      'Readback allowance missing or already used.');
    await txn.put('source-budget',{...last,readbackUsed:true});
    return {version:2,status:'readback-reserved',reservationSha256};
  });
}

export async function reserveWorkflowRequest(storage,request,review,now=Date.now()) {
  validateAttempt(request,review,now);
  const receipt=request.reservation;
  requireValue(receipt?.version===2 && receipt.status==='reserved' &&
    receipt.reviewSha256===await digest(JSON.stringify(ownerReviewBudget(review))), 'Invalid request allowance.');
  for(const name of ['jobSha256','repository','runId','runAttempt','commit'])
    requireValue(receipt[name]===request[name],'Storage caller differs.');
  const age=now-Date.parse(receipt.reservedAt);
  requireValue(age>=0 && age<30*60*1000,'Storage allowance expired.');
  const reservationSha256=await digest(JSON.stringify(receipt));
  requireValue(['list-before','list-after','upload','readback'].includes(request.operation),'Unknown storage operation.');
  return storage.transaction(async txn=>{
    const last=await txn.get('source-budget');
    requireValue(last?.reservationSha256===reservationSha256 && last.day===receipt.day,'Storage allowance missing.');
    const used=last.storageOperations??[];
    const preceding={'list-after':'list-before',upload:'list-after'}[request.operation];
    requireValue(!used.includes(request.operation) && (!preceding || used.includes(preceding)) &&
      (request.operation==='readback' ? last.readbackUsed===true && used.includes('upload') : last.readbackUsed===false),
      'Storage operation already spent or out of order.');
    await chargeComponentRequest(txn,'workflow',now);
    await txn.put('source-budget',{...last,storageOperations:[...used,request.operation]});
    return {version:2,status:'request-reserved',operation:request.operation,reservationSha256};
  });
}

export class DailyAttemptGuard {
  constructor(state,env) {this.storage=state.storage;this.env=env;}
  async fetch(request) {
    try {
      if (request.method === 'GET' && new URL(request.url).pathname === '/daily-backup-permit')
        return Response.json(await cachedBackupPermit(this.storage,this.env),{headers:{'Cache-Control':'no-store'}});
      if (request.method === 'POST' && new URL(request.url).pathname === '/internal-monitor' && !request.body) {
        if (this.env.MONITOR_ENABLED !== 'true') return new Response('Disabled', {status:409});
        return Response.json(await checkPrimaryMonitor(this.storage,this.env));
      }
      const expected=this.env.BACKUP_RESERVATION_SHA256;
      const token=request.headers.get('x-flightlogx-reservation-token');
      const route=new URL(request.url).pathname;
      requireValue(request.method==='POST' && ['/daily-backup-reservation','/daily-backup-readback','/daily-backup-request'].includes(route),'Unsupported reservation route.');
      if(route==='/daily-backup-reservation')requireValue(/^[a-f0-9]{64}$/.test(expected??'') && /^[a-f0-9]{64}$/.test(token??'') &&
        await digest(token)===expected,'Reservation authentication denied.');
      let size=0;const chunks=[];
      for await(const part of request.body??[]) {size+=part.length;requireValue(size<=2048,'Reservation request too large.');chunks.push(part);}
      const bytes=new Uint8Array(size);let offset=0;for(const part of chunks){bytes.set(part,offset);offset+=part.length;}
      const payload=JSON.parse(new TextDecoder().decode(bytes));
      const review=JSON.parse(this.env.OWNER_REVIEW_JSON);
      const receipt=route==='/daily-backup-reservation' ? await reserveDailyAttempt(this.storage,payload,review)
        : route==='/daily-backup-readback' ? await reserveDailyReadback(this.storage,payload,review)
        : await reserveWorkflowRequest(this.storage,payload,review);
      return Response.json(receipt,{status:201,headers:{'Cache-Control':'no-store'}});
    } catch {
      return Response.json({error:'Backup allowance denied.'},{status:409,headers:{'Cache-Control':'no-store'}});
    }
  }
}

export default {
  async scheduled(controller,env) { await runPrimaryMonitor(env); },
  async fetch(request,env) {
    const route=new URL(request.url).pathname;
    if(request.method==='GET' && route==='/daily-backup-permit') {
      if(env.BACKUP_ENABLED!=='true')return Response.json({error:'Backup permit unavailable.'},{status:409,headers:{'Cache-Control':'no-store'}});
      return env.DAILY_ATTEMPTS.get(env.DAILY_ATTEMPTS.idFromName('flightlogx-preview-source')).fetch(request);
    }
    if(request.method!=='POST' || !['/daily-backup-reservation','/daily-backup-readback','/daily-backup-request'].includes(route))
      return new Response('Not found',{status:404});
    if(route==='/daily-backup-reservation') {
      const token=request.headers.get('x-flightlogx-reservation-token');
      if(!/^[a-f0-9]{64}$/.test(token??'') || await digest(token)!==env.BACKUP_RESERVATION_SHA256)
        return Response.json({error:'Backup allowance denied.'},{status:409});
    }
    // One stable object for this Preview source, independent of job hash, token and review rotation.
    return env.DAILY_ATTEMPTS.get(env.DAILY_ATTEMPTS.idFromName('flightlogx-preview-source')).fetch(request);
  }
};
