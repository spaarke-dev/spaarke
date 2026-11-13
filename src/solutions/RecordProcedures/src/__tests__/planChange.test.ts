/**
 * The Change plan of the rule wizard (ebilling-r1 task 084c): who may be changed, what a change calls, and in which order.
 */

import { changeBlocker, executePlan, planRule, ruleChoiceNeeds, planWithin, allowedBlocker, NOT_ALLOWED_CHANGE, type Plan } from '../components/wizards/plan';
import type { OverridesApi } from '../services/overridesApi';
import { FENWICK, MATTER, mark, rule } from './fixtures';

const BODY = '{"type":"DailyHoursCap","maxHoursPerDay":12}';
const parent = { ...MATTER, distance: 1 };
const hours = (over = {}) => rule({ policyId: 'POL-H', code: 'HOURS', ruleType: 'DailyHoursCap', ...over });
const ownExcl = (overrideRowId?: string) => mark('Excluded', { distance: 0, chainRecord: MATTER, reason: 'why', overrideRowId });
const ownChanged = mark('Changed', { distance: 0, chainRecord: MATTER, overrideRowId: 'chg-row', reason: 'earlier' });

describe('who can be changed', () => {
  it('a rule of a type with a form, not changed here and not changed by a parent, can', () => {
    expect(changeBlocker(hours())).toBeNull();
    expect(changeBlocker(hours({ ruleType: 'Duplicate' }))).toBeNull();
    expect(changeBlocker(hours({ ruleType: 'AddedFee' }))).toBeNull();
  });

  it('a rate card rule (ApprovedRate) and a rule with no values (ApprovedTimekeeper) offer no Change, and say why', () => {
    expect(changeBlocker(hours({ ruleType: 'ApprovedRate' }))).toMatch(/pin that version/i);
    expect(changeBlocker(hours({ ruleType: 'ApprovedTimekeeper' }))).toMatch(/no values to change/i);
  });

  it('a type this page has no form for, or no type at all, offers no Change', () => {
    expect(changeBlocker(hours({ ruleType: 'Time-entry check' }))).toMatch(/no form for the rule type Time-entry check/);
    expect(changeBlocker(hours({ ruleType: 'SubmissionIntegrity' }))).toMatch(/no form/);
    expect(changeBlocker(hours({ ruleType: null }))).toMatch(/no form/);
  });

  it('P-3: a rule a parent excluded or changed is changed on the parent', () => {
    const byParent = (kind: string) => hours({ marks: [mark(kind, { distance: 1, chainRecord: parent, overrideRowId: 'par' })] });
    expect(changeBlocker(byParent('Excluded'))).toMatch(/Change it on the parent/);
    expect(changeBlocker(byParent('Changed'))).toMatch(/Change it on the parent/);
  });

  it('a rule already changed on this record (an own Changed mark, or the record rule) is run as published first', () => {
    expect(changeBlocker(hours({ recordRule: true, marks: [ownChanged] }))).toMatch(/Run the published rule first/);
    expect(changeBlocker(hours({ recordRule: true }))).toMatch(/Run the published rule first/);
  });
});

describe('what Change calls', () => {
  it('one change call with the policy, the reason (trimmed), the body as text, and the firm only when given', () => {
    const plan = planRule(hours(), 'change', { reason: '  Staffing ', ruleBody: BODY });
    expect(plan.steps).toEqual([{ kind: 'changeRule', policyId: 'POL-H', reason: 'Staffing', firmId: undefined, ruleBody: BODY }]);
    expect(planRule(hours(), 'change', { reason: 'r', ruleBody: BODY, firmId: 'F-1' }).steps).toEqual([{ kind: 'changeRule', policyId: 'POL-H', reason: 'r', firmId: 'F-1', ruleBody: BODY }]);
    expect(planRule(hours(), 'change', { reason: 'r', ruleBody: BODY, firmId: '' }).steps[0]).toMatchObject({ firmId: undefined });
  });

  it('a rule that names one firm is changed for that firm, whatever the input says', () => {
    const plan = planRule(hours({ firmScope: FENWICK }), 'change', { reason: 'r', ruleBody: BODY, firmId: 'someone-else' });
    expect(plan.steps).toEqual([expect.objectContaining({ kind: 'changeRule', firmId: FENWICK.id })]);
  });

  it('nothing is planned without a valid reason, without a body, or where Change is blocked', () => {
    expect(planRule(hours(), 'change', { ruleBody: BODY }).steps).toEqual([]);
    expect(planRule(hours(), 'change', { reason: '   ', ruleBody: BODY }).steps).toEqual([]);
    expect(planRule(hours(), 'change', { reason: 'r' }).steps).toEqual([]);
    expect(planRule(hours(), 'change', { reason: 'r', ruleBody: '' }).steps).toEqual([]);
    expect(planRule(hours({ ruleType: 'ApprovedRate' }), 'change', { reason: 'r', ruleBody: BODY }).steps).toEqual([]);
    expect(planRule(hours({ ruleType: 'Time-entry check' }), 'change', { reason: 'r', ruleBody: BODY }).steps).toEqual([]);
    expect(planRule(hours({ recordRule: true, marks: [ownChanged] }), 'change', { reason: 'r', ruleBody: BODY }).steps).toEqual([]);
  });

  it('a rule excluded on this record is un-excluded first (a change over it is a duplicate), then changed', () => {
    const excluded = hours({ marks: [ownExcl('own-x')] });
    const plan = planRule(excluded, 'change', { reason: 'r', ruleBody: BODY });
    expect(plan.steps.map(s => s.kind)).toEqual(['remove', 'changeRule']);
    expect(plan.steps[0]).toEqual({ kind: 'remove', overrideId: 'own-x' });
  });

  it('an own Exclude the server named no row id for cannot be removed, so the change is not planned', () => {
    expect(planRule(hours({ marks: [ownExcl(undefined)] }), 'change', { reason: 'r', ruleBody: BODY }).steps).toEqual([]);
    expect(ruleChoiceNeeds(hours({ marks: [ownExcl(undefined)] }), 'change')).toEqual({ remove: false, create: false, change: false });
  });
});

describe('what Change needs from the caller', () => {
  it('only the change right, plus the removal when it un-excludes first', () => {
    expect(ruleChoiceNeeds(hours(), 'change')).toEqual({ remove: false, create: false, change: true });
    expect(ruleChoiceNeeds(hours({ marks: [ownExcl('x')] }), 'change')).toEqual({ remove: true, create: false, change: true });
    expect(ruleChoiceNeeds(hours({ ruleType: 'ApprovedRate' }), 'change')).toEqual({ remove: false, create: false, change: false });
  });

  it('the other choices never need the change right', () => {
    for (const c of ['keep', 'exclude', 'parent', 'changed'] as const) expect(ruleChoiceNeeds(hours({ marks: [ownExcl('x')] }), c).change).toBe(false);
  });

  it('a plan within the needs: the change call is inside "change", not "create"', () => {
    const plan: Plan = planRule(hours(), 'change', { reason: 'r', ruleBody: BODY });
    expect(planWithin(plan, { remove: false, create: false, change: true })).toBe(true);
    expect(planWithin(plan, { remove: true, create: true, change: false })).toBe(false);
  });

  it('the words when the change right is missing', () => {
    expect(allowedBlocker({ remove: false, create: false, change: true }, { remove: true, create: true, change: false })).toBe(NOT_ALLOWED_CHANGE);
    expect(NOT_ALLOWED_CHANGE).toMatch(/cannot change a rule/);
  });
});

describe('running a Change plan', () => {
  const failure = { status: 400, errorCode: null, title: 'Rule change is not valid', detail: null, problems: [], fieldErrors: [{ field: 'ruleBody', message: '/maxHoursPerDay: too big', reason: null }], signInFailed: false };
  const changed = { override: { id: 'new' }, recordRule: { policyId: 'new-pol' }, closeComplete: true, affectedText: '4 open invoices.', message: 'Closed.' };
  const api = (over: Partial<OverridesApi> = {}): OverridesApi =>
    ({
      removeOverride: jest.fn(async () => ({ ok: true, value: { ruleSwitchedOff: false, closeComplete: true, affectedText: null, message: null } })),
      changeRule: jest.fn(async () => ({ ok: true, value: changed })),
      ...over,
    }) as never;

  it('sends the change with the plan\'s values and records its answer', async () => {
    const a = api();
    const run = await executePlan(a, 'matters', 'rec', planRule(hours(), 'change', { reason: 'r', ruleBody: BODY, firmId: 'F-1' }));
    expect(a.changeRule).toHaveBeenCalledWith('matters', 'rec', { policyId: 'POL-H', reason: 'r', firmId: 'F-1', ruleBody: BODY });
    expect(run).toMatchObject({ ok: true, completed: 1, changed: [{ closeComplete: true, affectedText: '4 open invoices.', message: 'Closed.' }] });
  });

  it('removes first, then changes, in that order; a refused change keeps the removal done and carries the field errors', async () => {
    const order: string[] = [];
    const a = api({
      removeOverride: jest.fn(async () => (order.push('remove'), { ok: true, value: {} as never })) as never,
      changeRule: jest.fn(async () => (order.push('change'), { ok: false, failure })) as never,
    });
    const run = await executePlan(a, 'matters', 'rec', planRule(hours({ marks: [ownExcl('own-x')] }), 'change', { reason: 'r', ruleBody: BODY }));
    expect(order).toEqual(['remove', 'change']);
    expect(run).toMatchObject({ ok: false, completed: 1, failure });
  });
});
