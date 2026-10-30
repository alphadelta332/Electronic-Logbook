import assert from 'node:assert/strict';
import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const repository = 'alphadelta332/flightlogx-preview-backups';
export function validatePlan(plan) {
  assert.equal(plan.repository, repository);
  assert.match(plan.commit, /^[a-f0-9]{40}$/);
  assert.equal(plan.workflows.length, 2);
  assert.equal(plan.workflows[0].id, 388166034);
  assert.equal(plan.workflows[0].path, '.github/workflows/daily-backup.yml');
  assert.equal(plan.workflows[1].path, '.github/workflows/timer-control.yml');
  assert.equal(plan.workflows[1].id, 'timer-control.yml');
  assert.equal(plan.workflows[0].expectedConclusion ?? 'skipped', 'skipped');
  assert.ok(['skipped', 'success'].includes(plan.workflows[1].expectedConclusion ?? 'skipped'));
  assert.ok(Array.isArray(plan.baselineRunIds) && plan.baselineRunIds.every(Number.isSafeInteger));
  const start = Date.parse(plan.startedAt), deadline = Date.parse(plan.captureUntil);
  assert.ok(Number.isFinite(start) && deadline > start && deadline - start <= 86400000);
  return plan;
}

export function validateCompletedRun(expected, run, jobs) {
  assert.equal(run.conclusion, expected.expectedConclusion ?? 'skipped');
  if (run.conclusion === 'skipped') {
    assert.ok(jobs.every(job => job.conclusion === 'skipped' && (job.steps ?? []).length === 0));
    return;
  }
  assert.equal(jobs.length, 1);
  assert.equal(jobs[0].name, 'timer');
  assert.equal(jobs[0].conclusion, 'success');
  const steps = jobs[0].steps ?? [];
  assert.equal(steps.filter(step => step.name === 'Record scheduled trigger' && step.conclusion === 'success').length, 1);
  assert.ok(steps.every(step => ['Set up job', 'Record scheduled trigger', 'Complete job'].includes(step.name)));
}

function gh(path, method = 'GET') {
  const result = spawnSync('gh', ['api', '--method', method, 'repos/' + repository + path],
    { windowsHide: true, timeout: 20000, maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, 'GitHub API failed; raw diagnostics suppressed.');
  return result.stdout.length ? JSON.parse(result.stdout) : null;
}

export function newRuns(plan, runs) {
  return runs.filter(run => !plan.baselineRunIds.includes(run.id)).map(run => ({
    id: run.id, workflowId: run.workflow_id, commit: run.head_sha, event: run.event,
    status: run.status, conclusion: run.conclusion, createdAt: run.created_at,
    actor: run.actor?.login, attempt: run.run_attempt,
  }));
}

function inspect(plan, report) {
  assert.equal(gh('').private, true);
  assert.equal(gh('/commits/main').sha, plan.commit, 'Private main changed.');
  assert.equal(gh('/actions/variables/BACKUP_ENABLED').value, 'false');
  const workflows = plan.workflows.map(expected => {
    const actual = gh('/actions/workflows/' + expected.id);
    assert.equal(actual.path, expected.path);
    return { id: actual.id, path: actual.path, state: actual.state,
      expectedConclusion: expected.expectedConclusion ?? 'skipped' };
  });
  const runs = newRuns(plan, gh('/actions/runs?per_page=100').workflow_runs);
  report.observation = { observedAt: new Date().toISOString(), backupEnabled: false, workflows, runs };
  for (const run of runs) {
    assert.equal(run.commit, plan.commit);
    assert.equal(run.event, 'schedule', 'Unexpected trigger; manual dispatch cannot pass.');
    assert.equal(run.attempt, 1);
    assert.ok(workflows.some(workflow => workflow.id === run.workflowId));
    if (run.status === 'completed') {
      const jobs = gh('/actions/runs/' + run.id + '/jobs').jobs;
      run.jobs = jobs.map(job => ({ name: job.name, conclusion: job.conclusion,
        steps: (job.steps ?? []).map(step => ({ name: step.name, conclusion: step.conclusion })) }));
      validateCompletedRun(workflows.find(workflow => workflow.id === run.workflowId), run, jobs);
    }
  }
}

function stop(plan) {
  const failures = [];
  for (const workflow of plan.workflows) {
    try {
      const current = gh('/actions/workflows/' + workflow.id);
      assert.equal(current.path, workflow.path);
      if (current.state !== 'disabled_manually') gh('/actions/workflows/' + workflow.id + '/disable', 'PUT');
      assert.equal(gh('/actions/workflows/' + workflow.id).state, 'disabled_manually');
    } catch { failures.push({ workflowId: workflow.id, stage: 'disable' }); }
  }
  assert.equal(gh('/actions/variables/BACKUP_ENABLED').value, 'false');
  assert.deepEqual(failures, [], 'Independent shutdown incomplete.');
  return { observedAt: new Date().toISOString(), disabled: true, backupEnabled: false };
}

async function main() {
  const [requestedMode, ...args] = process.argv.slice(2);
  const planPath = args.at(-1);
  const plan = validatePlan(JSON.parse(readFileSync(planPath)));
  const mode = plan.probe ?? requestedMode;
  assert.ok(['capture', 'stop', 'probe', 'probe-failure', 'inspect'].includes(mode));
  const output = join(dirname(resolve(planPath)), mode + '-' + Date.now() + '-' + process.pid + '.json');
  const report = { startedAt: new Date().toISOString(), mode, completed: false };
  let failed = false;
  try {
    if (mode.startsWith('probe')) {
      const child = spawnSync(process.execPath, ['-e', mode === 'probe' ? 'process.exit(0)' : 'process.exit(7)'],
        { windowsHide: true, timeout: 5000 });
      assert.equal(child.status, 0, 'Probe child failure.');
    } else if (mode === 'stop') report.shutdown = stop(plan);
    else if (mode === 'inspect') inspect(plan, report);
    else {
      assert.ok(Date.now() < Date.parse(plan.captureUntil), 'Capture window expired.');
      do {
        inspect(plan, report);
        appendFileSync(join(dirname(output), 'observations.jsonl'), JSON.stringify(report.observation) + '\n');
        if (report.observation.workflows.every(workflow => report.observation.runs.some(run =>
          run.workflowId === workflow.id && run.status === 'completed'))) break;
        await new Promise(resolve => setTimeout(resolve, Math.min(300000, Math.max(0, Date.parse(plan.captureUntil) - Date.now()))));
      } while (Date.now() < Date.parse(plan.captureUntil));
    }
    report.completed = true;
  } catch { report.failed = true; failed = true; }
  finally {
    if (mode === 'capture') {
      try { report.shutdown = stop(plan); } catch { report.shutdownFailed = true; failed = true; }
    }
    report.finishedAt = new Date().toISOString();
    writeFileSync(output, JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  }
  console.log(JSON.stringify({ receipt: output, completed: report.completed, failed }));
  process.exitCode = failed ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().catch(() => { console.error('Invalid diagnostic invocation; no remote action.'); process.exitCode = 1; });
}
