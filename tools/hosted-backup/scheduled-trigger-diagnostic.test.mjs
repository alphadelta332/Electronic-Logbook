import assert from 'node:assert/strict';
import test from 'node:test';
import { newRuns, validateCompletedRun, validatePlan } from './scheduled-trigger-diagnostic.mjs';

const plan = {
  repository: 'alphadelta332/flightlogx-preview-backups', commit: 'a'.repeat(40),
  workflows: [{ id: 388166034, path: '.github/workflows/daily-backup.yml' },
    { id: 'timer-control.yml', path: '.github/workflows/timer-control.yml' }],
  baselineRunIds: [1, 2], startedAt: '2026-10-30T00:00:00Z', captureUntil: '2026-10-30T00:50:00Z',
};

test('rejects wrong repositories, workflow identities and unbounded plans', () => {
  assert.equal(validatePlan(plan), plan);
  assert.throws(() => validatePlan({ ...plan, repository: 'another/repo' }));
  assert.throws(() => validatePlan({ ...plan, workflows: [...plan.workflows].reverse() }));
  assert.throws(() => validatePlan({ ...plan, captureUntil: '2026-10-31T00:00:00.001Z' }));
});

test('permits a bounded runnable control without permitting backup job execution', () => {
  const runnable = { ...plan, captureUntil: '2026-10-31T00:00:00Z', workflows:
    [plan.workflows[0], { ...plan.workflows[1], expectedConclusion: 'success' }] };
  assert.equal(validatePlan(runnable), runnable);
  assert.throws(() => validatePlan({ ...runnable, workflows:
    [{ ...plan.workflows[0], expectedConclusion: 'success' }, runnable.workflows[1]] }));
  assert.throws(() => validatePlan({ ...runnable, workflows:
    [plan.workflows[0], { ...plan.workflows[1], expectedConclusion: 'failure' }] }));
});

test('accepts only the one successful marker job for a runnable control', () => {
  const expected = { ...plan.workflows[1], expectedConclusion: 'success' };
  const run = { conclusion: 'success' };
  const job = { name: 'timer', conclusion: 'success', steps:
    ['Set up job', 'Record scheduled trigger', 'Complete job'].map(name => ({ name, conclusion: 'success' })) };
  validateCompletedRun(expected, run, [job]);
  assert.throws(() => validateCompletedRun(plan.workflows[0], run, [job]));
  assert.throws(() => validateCompletedRun(expected, { conclusion: 'skipped' }, []));
  assert.throws(() => validateCompletedRun(expected, run, [{ ...job, steps: [] }]));
  assert.throws(() => validateCompletedRun(expected, run, [{ ...job, name: 'upload' }]));
  assert.throws(() => validateCompletedRun(expected, run, [job, job]));
  assert.throws(() => validateCompletedRun(expected, run, [{ ...job, steps:
    [...job.steps, { name: 'Checkout', conclusion: 'success' }] }]));
  validateCompletedRun(plan.workflows[0], { conclusion: 'skipped' }, []);
  assert.throws(() => validateCompletedRun(plan.workflows[0], { conclusion: 'skipped' }, [job]));
});

test('keeps historical manual/failed/skipped runs separate from fresh schedule evidence', () => {
  const runs = [{ id: 1, event: 'workflow_dispatch', conclusion: 'success' },
    { id: 2, event: 'workflow_dispatch', conclusion: 'skipped' },
    { id: 3, workflow_id: 388166034, event: 'schedule', head_sha: plan.commit,
      status: 'completed', conclusion: 'skipped', actor: { login: 'owner' }, run_attempt: 1 }];
  const current = newRuns(plan, runs);
  assert.equal(current.length, 1);
  assert.equal(current[0].id, 3);
  assert.equal(current[0].event, 'schedule');
});
