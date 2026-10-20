const day=24*60*60*1000;
const requireValue=(condition,message)=>{if(!condition)throw new Error(message);};
const integer=value=>Number.isSafeInteger(value)&&value>=0;
const amount=value=>typeof value==='number'&&Number.isFinite(value)&&value>=0;
const timestamp=value=>typeof value==='string'?Date.parse(value):NaN;

export function ownerReviewBudget(review) {
  const budget={...review};delete budget.weeklyCheckedAt;
  return budget;
}

export function validateOwnerReview(review,jobSha256,sourceOrigin,now=Date.now()) {
  requireValue(review?.version===2 && /^[a-f0-9]{64}$/.test(jobSha256) && review.jobSha256===jobSha256,
    'Owner review does not match prepared inputs.');
  requireValue(/^https:\/\/[a-z]{20}\.supabase\.co$/.test(sourceOrigin) && review.sourceOrigin===sourceOrigin,
    'Owner review source differs.');
  const approved=timestamp(review.approvedAt),expires=timestamp(review.expiresAt),weekly=timestamp(review.weeklyCheckedAt);
  requireValue(integer(now) && approved<=now && expires>now && expires>approved && expires-approved<=31*day,
    'Owner review is missing, expired or exceeds 31 days.');
  requireValue(weekly>=approved && weekly<=now && now-weekly<7*day,'Weekly usage check is missing, future or overdue.');
  requireValue(timestamp(review.sourceExpiresAt)>=expires,'Source capability expires before the review.');
  requireValue(review.githubPaidActionsBlocked===true && review.supabasePlan==='free','Owner billing review is not approved.');
  for(const name of ['egress','cachedEgress']) {
    const quota=review[name];
    requireValue(integer(quota?.used) && integer(quota?.included) && quota.included-quota.used>=31*64*1024*1024,
      'Reviewed quota cannot cover the reserved source budget.');
  }
  for(const name of ['github','r2']) {
    const baseline=review[name],observed=timestamp(baseline?.observedAt);
    const start=timestamp(baseline?.periodStart),end=timestamp(baseline?.periodEnd);
    requireValue(observed<=approved && approved-observed<=day && start<=observed && end>approved && expires<=end,
      'Reviewed billing baseline is stale or outside its allowance period.');
  }
  requireValue(integer(review.github.remainingLinuxMinutes) && review.github.remainingLinuxMinutes>=640,
    'Reviewed Actions allowance cannot cover the planned backup budget.');
  const r2=review.r2;
  requireValue(amount(r2.storageGbMonths) && r2.storageGbMonths<=1 && integer(r2.requests) && r2.requests<10000 &&
    r2.storageBillableGbMonths===0 && r2.classABillable===0 && r2.classBBillable===0 && r2.usageCostUsd===0,
    'Reviewed R2 usage is paid, missing or exceeds the starting limits.');
}

export function validateCostEvidence(permit,jobSha256,sourceOrigin,now=Date.now()) {
  requireValue(permit?.version===3 && permit.jobSha256===jobSha256,'Unsupported or mismatched cost permit.');
  validateOwnerReview(permit.ownerReview,jobSha256,sourceOrigin,now);
  const checked=timestamp(permit.checkedAt),expires=timestamp(permit.expiresAt);
  requireValue(checked<=now && now-checked<2*60*60*1000 && expires>now && expires>checked && expires-now<=2*60*60*1000,
    'Cost/deployment evidence is stale.');
  for(const observedAt of [permit.deploymentObservedAt,permit.github?.observedAt,permit.supabase?.observedAt,permit.r2?.observedAt]) {
    const observed=timestamp(observedAt);
    requireValue(observed<=checked && now-observed<2*60*60*1000,'Underlying provider evidence is stale.');
  }
  const github=permit.github,r2=permit.r2,review=permit.ownerReview;
  requireValue(github.plan==='free' && !Object.hasOwn(github,'remainingLinuxMinutes') && !Object.hasOwn(github,'paidActionsBlocked'),
    'Live GitHub evidence must not invent a remaining balance or paid-usage stop.');
  requireValue(timestamp(github.queryStart)<=timestamp(review.github.periodStart) && github.queryEnd===github.observedAt &&
    timestamp(github.queryEnd)>=timestamp(review.github.periodStart) &&
    Array.isArray(github.actionsMinutes) && github.actionsMinutes.every(item=>
      ['actions_linux','actions_windows','actions_macos'].includes(item.sku) && amount(item.grossQuantity) && item.netAmount===0),
    'GitHub usage coverage, SKU or charges require review.');
  requireValue(permit.supabase.plan==='free' && !Object.hasOwn(permit.supabase,'egress') && !Object.hasOwn(permit.supabase,'cachedEgress'),
    'Unsupported live Supabase quota evidence.');
  requireValue(!['storageBytes','classA','classB'].some(name=>Object.hasOwn(r2,name)),
    'Exact R2 billing allowances must not be fabricated.');
  const measured=timestamp(r2.newestStorageMeasurementAt),observed=timestamp(r2.observedAt);
  requireValue(measured<=observed && now-measured<2*60*60*1000,'Storage analytics are stale.');
  requireValue(integer(r2.currentBytes) && integer(r2.sampledPeakBytes) && r2.pendingUploads===0 && r2.infrequentAccessBytes===0,
    'Incomplete or unreviewed account storage.');
  const bytes=Math.max(r2.currentBytes,r2.sampledPeakBytes);
  requireValue(bytes+50*1024*1024<2*1024*1024*1024,'Reported R2 storage stop reached.');
  requireValue(Array.isArray(r2.windows) && r2.windows.length===2,'Both R2 usage windows are required.');
  const starts=[timestamp(review.r2.periodStart),observed-31*day];
  for(let index=0;index<2;index++) {
    const window=r2.windows[index];
    requireValue(timestamp(window?.from)===starts[index] && starts[index]<=observed && window.until===r2.observedAt &&
      integer(window.requests) && window.requests<10000,'R2 usage window is missing, changed or at its stop.');
  }
  return {warnings:[...(bytes>=1024*1024*1024?['r2-storage']:[]),
    ...(r2.windows.some(window=>window.requests>=5000)?['r2-operations']:[])]};
}
