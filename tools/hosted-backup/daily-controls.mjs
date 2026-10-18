export const dailySourceEgressBytes = 64 * 1024 * 1024;
export const maximumReviewAttempts = 31;
const reviewDuration = 31 * 24 * 60 * 60 * 1000;
const encoder = new TextEncoder();
const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',encoder.encode(value))))
  .map(byte=>byte.toString(16).padStart(2,'0')).join('');
const requireValue = (condition,message) => {if(!condition)throw new Error(message);};

export function validateOwnerReview(review,jobSha256,sourceOrigin,now=Date.now()) {
  requireValue(review?.version===1 && /^[a-f0-9]{64}$/.test(jobSha256) && review.jobSha256===jobSha256,
    'Owner review does not match prepared inputs.');
  requireValue(/^https:\/\/[a-z]{20}\.supabase\.co$/.test(sourceOrigin) && review.sourceOrigin===sourceOrigin,
    'Owner review source differs.');
  const approved=Date.parse(review.approvedAt), expires=Date.parse(review.expiresAt);
  requireValue(Number.isFinite(approved) && Number.isFinite(expires) && approved<=now && expires>now &&
    expires>approved && expires-approved<=reviewDuration,'Owner review is missing, expired or exceeds 31 days.');
  requireValue(review.githubPaidActionsBlocked===true && review.supabasePlan==='free','Owner billing review is not approved.');
  for(const name of ['egress','cachedEgress']) {
    const quota=review[name];
    requireValue(Number.isSafeInteger(quota?.used) && quota.used>=0 && Number.isSafeInteger(quota?.included) &&
      quota.included-quota.used>=maximumReviewAttempts*dailySourceEgressBytes,'Reviewed quota cannot cover the reserved source budget.');
  }
}

function validateAttempt(request,review,now) {
  requireValue(request?.version===1,'Unsupported reservation request.');
  validateOwnerReview(review,request.jobSha256,request.sourceOrigin,now);
  requireValue(request.repository==='alphadelta332/flightlogx-preview-backups' &&
    /^[1-9][0-9]*$/.test(request.runId) && /^[1-9][0-9]*$/.test(request.runAttempt) && /^[a-f0-9]{40}$/.test(request.commit),
    'Unexpected reservation caller.');
}

export async function reserveDailyAttempt(storage,request,review,now=Date.now()) {
  validateAttempt(request,review,now);
  const reviewSha256=await digest(JSON.stringify(review));
  const day=new Date(now).toISOString().slice(0,10);
  return storage.transaction(async txn=>{
    const last=await txn.get('source-budget');
    requireValue(!last || last.day<day,'The daily source attempt is already reserved.');
    const attempts=last?.reviewSha256===reviewSha256 ? last.attempts+1 : 1;
    requireValue(attempts<=maximumReviewAttempts,'Owner review source budget is exhausted.');
    const receipt={version:1,status:'reserved',day,reservedAt:new Date(now).toISOString(),jobSha256:request.jobSha256,
      reviewSha256,sourceEgressBytes:dailySourceEgressBytes,repository:request.repository,runId:request.runId,
      runAttempt:request.runAttempt,commit:request.commit};
    // Keep the last day across review/source-input rotation; a new review cannot buy a second attempt today.
    await txn.put('source-budget',{day,reviewSha256,attempts});
    return receipt;
  });
}

export class DailyAttemptGuard {
  constructor(state,env) {this.storage=state.storage;this.env=env;}
  async fetch(request) {
    try {
      const expected=this.env.BACKUP_RESERVATION_SHA256;
      const token=request.headers.get('x-flightlogx-reservation-token');
      requireValue(request.method==='POST' && new URL(request.url).pathname==='/daily-backup-reservation','Unsupported reservation route.');
      requireValue(/^[a-f0-9]{64}$/.test(expected??'') && /^[a-f0-9]{64}$/.test(token??'') &&
        await digest(token)===expected,'Reservation authentication denied.');
      let size=0;const chunks=[];
      for await(const part of request.body??[]) {size+=part.length;requireValue(size<=2048,'Reservation request too large.');chunks.push(part);}
      const bytes=new Uint8Array(size);let offset=0;for(const part of chunks){bytes.set(part,offset);offset+=part.length;}
      const payload=JSON.parse(new TextDecoder().decode(bytes));
      const review=JSON.parse(this.env.OWNER_REVIEW_JSON);
      const receipt=await reserveDailyAttempt(this.storage,payload,review);
      return Response.json(receipt,{status:201,headers:{'Cache-Control':'no-store'}});
    } catch {
      return Response.json({error:'Reservation denied; no source budget granted.'},{status:409,headers:{'Cache-Control':'no-store'}});
    }
  }
}

export default {
  fetch(request,env) {
    if(request.method!=='POST' || new URL(request.url).pathname!=='/daily-backup-reservation')
      return new Response('Not found',{status:404});
    // One stable object for this Preview source, independent of job hash, token and review rotation.
    return env.DAILY_ATTEMPTS.get(env.DAILY_ATTEMPTS.idFromName('flightlogx-preview-source')).fetch(request);
  }
};
