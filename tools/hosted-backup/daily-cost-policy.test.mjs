import test from 'node:test';
import assert from 'node:assert/strict';
import { validateOwnerReview, validateCostEvidence, ownerReviewBudget } from './daily-cost-policy.mjs';

const now=Date.parse('2026-10-19T12:00:00Z'),day=24*60*60*1000;
const iso=value=>new Date(value).toISOString();
const jobSha256='a'.repeat(64),origin='https://'+'b'.repeat(20)+'.supabase.co';
function fixture() {
  const at=iso(now-day),start='2026-10-01T00:00:00Z',end='2026-11-01T00:00:00Z';
  const ownerReview={version:2,approvedAt:at,expiresAt:end,sourceExpiresAt:end,weeklyCheckedAt:at,
    jobSha256,sourceOrigin:origin,githubPaidActionsBlocked:true,supabasePlan:'free',
    egress:{used:1,included:5e9},cachedEgress:{used:1,included:5e9},
    github:{observedAt:at,periodStart:start,periodEnd:end,remainingLinuxMinutes:640},
    r2:{observedAt:at,periodStart:start,periodEnd:end,storageGbMonths:1,requests:9999,
      storageBillableGbMonths:0,classABillable:0,classBBillable:0,usageCostUsd:0}};
  return {version:3,checkedAt:iso(now),expiresAt:iso(now+60000),jobSha256,ownerReview,
    deploymentObservedAt:iso(now),supabase:{observedAt:iso(now),plan:'free'},
    github:{observedAt:iso(now),plan:'free',queryStart:start,queryEnd:iso(now),
      actionsMinutes:[{sku:'actions_linux',grossQuantity:7,netAmount:0},{sku:'actions_windows',grossQuantity:100,netAmount:0}]},
    r2:{observedAt:iso(now),newestStorageMeasurementAt:iso(now-60*60*1000),currentBytes:1500000,sampledPeakBytes:1500000,
      pendingUploads:0,infrequentAccessBytes:0,windows:[{from:start,until:iso(now),requests:67},
        {from:iso(now-31*day),until:iso(now),requests:67}]}};
}

test('reviewed balances remain historical while actual zero-net usage passes without calculating a live balance',()=>{
  const permit=fixture();
  assert.deepEqual(validateCostEvidence(permit,jobSha256,origin,now),{warnings:[]});
  assert.equal(permit.ownerReview.github.observedAt,iso(now-day));
  assert.equal(permit.ownerReview.github.remainingLinuxMinutes,640);
  assert.ok(!Object.hasOwn(permit.github,'remainingLinuxMinutes'));
});

test('review rejects expired/old/future baselines, missed weekly checks, paid usage and insufficient starting margins',async t=>{
  const cases={
    'legacy review':r=>r.version=1,
    'wrong input':r=>r.jobSha256='c'.repeat(64),
    'expired review':r=>r.expiresAt=iso(now),
    'review over31days':r=>r.expiresAt=iso(now+31*day),
    'future weekly check':r=>r.weeklyCheckedAt=iso(now+1),
    'weekly check before approval':r=>r.weeklyCheckedAt=iso(now-2*day),
    'weekly expiry':r=>{r.approvedAt=iso(now-7*day);r.github.observedAt=r.approvedAt;r.r2.observedAt=r.approvedAt;r.weeklyCheckedAt=r.approvedAt;},
    'source expiry':r=>r.sourceExpiresAt=iso(now+day),
    'old GitHub baseline':r=>r.github.observedAt=iso(now-2*day-1),
    'old R2 baseline':r=>r.r2.observedAt=iso(now-2*day-1),
    'future R2 baseline':r=>r.r2.observedAt=iso(now),
    'GitHub allowance period ends early':r=>r.github.periodEnd=iso(now+day),
    'R2 allowance period ends early':r=>r.r2.periodEnd=iso(now+day),
    'missing period start':r=>delete r.r2.periodStart,
    'GitHub balance639':r=>r.github.remainingLinuxMinutes=639,
    'fractional balance':r=>r.github.remainingLinuxMinutes=640.5,
    'paid Actions unblocked':r=>r.githubPaidActionsBlocked=false,
    'paid Supabase plan':r=>r.supabasePlan='pro',
    'Supabase reserved byte shortfall':r=>r.egress={used:1,included:31*64*1024*1024},
    'R2 storage baseline above1GBmonth':r=>r.r2.storageGbMonths=1.00001,
    'R2 request baseline10000':r=>r.r2.requests=10000,
    'R2 billable storage':r=>r.r2.storageBillableGbMonths=0.01,
    'R2 billable ClassA':r=>r.r2.classABillable=1,
    'R2 billable ClassB':r=>r.r2.classBBillable=1,
    'R2 charge':r=>r.r2.usageCostUsd=0.01,
    'missing R2 cost':r=>delete r.r2.usageCostUsd,
    'NaN storage':r=>r.r2.storageGbMonths=NaN
  };
  for(const [name,mutate] of Object.entries(cases))await t.test(name,()=>{
    const review=fixture().ownerReview;mutate(review);
    assert.throws(()=>validateOwnerReview(review,jobSha256,origin,now));
  });
});

test('24-hour initial review and seven-day weekly check boundaries are exact; weekly changes do not renew the budget',()=>{
  const review=fixture().ownerReview;
  review.github.observedAt=iso(Date.parse(review.approvedAt)-day);
  review.r2.observedAt=review.github.observedAt;
  validateOwnerReview(review,jobSha256,origin,now);
  review.r2.observedAt=iso(Date.parse(review.approvedAt)-day-1);
  assert.throws(()=>validateOwnerReview(review,jobSha256,origin,now));
  const original=fixture().ownerReview,updated={...original,weeklyCheckedAt:iso(now)};
  assert.deepEqual(ownerReviewBudget(original),ownerReviewBudget(updated));
  validateOwnerReview(updated,jobSha256,origin,now);
  const sevenDays=Date.parse(original.weeklyCheckedAt)+7*day;
  validateOwnerReview(original,jobSha256,origin,sevenDays-1);
  assert.throws(()=>validateOwnerReview(original,jobSha256,origin,sevenDays));
  assert.throws(()=>validateOwnerReview({...updated,weeklyCheckedAt:original.expiresAt},jobSha256,origin,Date.parse(original.expiresAt)));
});

test('reported storage includes next50MiB; warning and hard stop apply to either account total or sampled peak',()=>{
  for(const field of ['currentBytes','sampledPeakBytes']) {
    const permit=fixture();permit.r2[field]=1024*1024*1024-1;
    assert.deepEqual(validateCostEvidence(permit,jobSha256,origin,now).warnings,[]);
    permit.r2[field]++;
    assert.deepEqual(validateCostEvidence(permit,jobSha256,origin,now).warnings,['r2-storage']);
    permit.r2[field]=2*1024*1024*1024-50*1024*1024-1;
    validateCostEvidence(permit,jobSha256,origin,now);
    permit.r2[field]++;
    assert.throws(()=>validateCostEvidence(permit,jobSha256,origin,now),/storage stop/);
  }
});

test('both actual-period and rolling31day request windows warn at5000 and stop at10000',()=>{
  for(let index=0;index<2;index++) {
    const permit=fixture();permit.r2.windows[index].requests=4999;
    assert.deepEqual(validateCostEvidence(permit,jobSha256,origin,now).warnings,[]);
    permit.r2.windows[index].requests=5000;
    assert.deepEqual(validateCostEvidence(permit,jobSha256,origin,now).warnings,['r2-operations']);
    permit.r2.windows[index].requests=9999;validateCostEvidence(permit,jobSha256,origin,now);
    permit.r2.windows[index].requests=10000;
    assert.throws(()=>validateCostEvidence(permit,jobSha256,origin,now));
  }
});

test('live evidence rejects fabricated balances, paid or unknown usage, gaps and stale measurements',async t=>{
  const cases={
    'legacy permit':p=>p.version=2,
    'invented live balance':p=>p.github.remainingLinuxMinutes=1997,
    'invented live Actions block':p=>p.github.paidActionsBlocked=true,
    'invented R2 allowance':p=>p.r2.storageBytes={used:0,included:1e10},
    'invented live Supabase egress':p=>p.supabase.egress={used:0,included:5e9},
    'paid Actions':p=>p.github.actionsMinutes[0].netAmount=0.01,
    'missing Actions charge':p=>delete p.github.actionsMinutes[0].netAmount,
    'unknown Actions SKU':p=>p.github.actionsMinutes[0].sku='gpu',
    'missing GitHub plan':p=>delete p.github.plan,
    'missing GitHub usage':p=>delete p.github.actionsMinutes,
    'GitHub query gap':p=>p.github.queryStart='2026-10-02T00:00:00Z',
    'GitHub query end mismatch':p=>p.github.queryEnd=iso(now-1),
    'missing rolling window':p=>p.r2.windows.pop(),
    'wrong R2 period':p=>p.r2.windows[0].from='2026-10-02T00:00:00Z',
    'wrong rolling start':p=>p.r2.windows[1].from=iso(now-30*day),
    'window incomplete end':p=>p.r2.windows[0].until=iso(now-1),
    'fractional operations':p=>p.r2.windows[0].requests=1.5,
    'stale storage row':p=>p.r2.newestStorageMeasurementAt=iso(now-2*60*60*1000),
    'future storage row':p=>p.r2.newestStorageMeasurementAt=iso(now+1),
    'missing bytes':p=>delete p.r2.currentBytes,
    'negative peak':p=>p.r2.sampledPeakBytes=-1,
    'pending multipart':p=>p.r2.pendingUploads=1,
    'Infrequent Access usage':p=>p.r2.infrequentAccessBytes=1,
    'expired evidence':p=>p.expiresAt=iso(now),
    'future evidence':p=>p.checkedAt=iso(now+1),
    'stale provider':p=>p.supabase.observedAt=iso(now-2*60*60*1000),
    'query ends before current billing period':p=>{
      p.ownerReview.approvedAt=iso(now);p.ownerReview.weeklyCheckedAt=iso(now);
      p.ownerReview.r2.observedAt=iso(now);p.ownerReview.r2.periodStart=iso(now);
      p.r2.observedAt=iso(now-1000);p.r2.windows[0]={from:iso(now),until:p.r2.observedAt,requests:67};
      p.r2.windows[1]={from:iso(now-1000-31*day),until:p.r2.observedAt,requests:67};
    }
  };
  for(const [name,mutate] of Object.entries(cases))await t.test(name,()=>{
    const permit=fixture();mutate(permit);
    assert.throws(()=>validateCostEvidence(permit,jobSha256,origin,now));
  });
});
