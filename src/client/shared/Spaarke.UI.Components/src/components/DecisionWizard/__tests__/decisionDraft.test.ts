/**
 * decisionDraft - the pure logic of one item's unrecorded review (task 058). The stated contract:
 *  - excludes are read from the plan, applied in plan order: the EARLIER action taken removes the later one (HANDOFF 1.1)
 *  - the record class is derived from the catalog, never chosen (R-2): a Next step makes it Judgement; Routine only if every
 *    action taken is; nothing taken is a Dismissal
 *  - the review posted lists every offered action exactly once; a dismissal is sent only when nothing was taken
 *  - starting values (templated drafts, a single budget) are not choices: a draft with only defaults is not dirty
 */

import {
  actionScope,
  buildReview,
  deriveRecordClass,
  dismissalValid,
  emptyDraft,
  excludedActionCodes,
  excludedBy,
  hasUnrecordedChoices,
  nextScope,
  parseMoney,
  reviewReady,
  undecidedSteps,
  withDecision,
  withDefaults,
  withDismissal,
  withLookup,
  withNextSelected,
  withValue,
} from '../decisionDraft';
import type { ItemDraft } from '../decisionDraft';
import { approveFirstPlan, pathBPlan, doPlan, BUDGET_A } from '../__fixtures__/decisionWizardFixtures';

const ID = '12345678-1234-4234-8234-123456789abc';
const take = (d: ItemDraft, code: string) => withDecision(d, actionScope(code), 'taken');
const skip = (d: ItemDraft, code: string) => withDecision(d, actionScope(code), 'skipped');

describe('excludes (read from the plan, in plan order)', () => {
  it('taking the earlier action removes the later one it excludes', () => {
    const d = take(emptyDraft(ID), 'approve-variance');
    expect([...excludedActionCodes(approveFirstPlan, d)]).toEqual(['revise-budget']);
    expect(excludedBy(approveFirstPlan, d, 'revise-budget')?.code).toBe('approve-variance');
  });

  it('is mutual: with Revise first, taking it removes Approve instead', () => {
    const d = take(emptyDraft(ID), 'revise-budget');
    expect([...excludedActionCodes(pathBPlan, d)]).toEqual(['approve-variance']);
  });

  it('skipping the earlier action removes nothing', () => {
    const d = skip(emptyDraft(ID), 'approve-variance');
    expect(excludedActionCodes(approveFirstPlan, d).size).toBe(0);
  });

  it('an earlier action that is itself removed excludes nothing (its choice is void)', () => {
    // mark-complete excludes reschedule; a plan [A, B, C] where A excludes B and B excludes C: taking A removes B,
    // and B (removed) must not then remove C even if it was taken earlier.
    const plan = {
      ...doPlan,
      actions: [
        { ...doPlan.actions[0], code: 'a', excludes: ['b'] },
        { ...doPlan.actions[1], code: 'b', excludes: ['c'] },
        { ...doPlan.actions[1], code: 'c', excludes: [] },
      ],
    };
    const d = take(take(emptyDraft(ID), 'b'), 'a');
    expect([...excludedActionCodes(plan, d)]).toEqual(['b']);
  });
});

describe('derived record class (R-2)', () => {
  it('nothing taken is a dismissal', () => {
    const d = skip(skip(skip(emptyDraft(ID), 'send-budget-inquiry'), 'revise-budget'), 'approve-variance');
    expect(deriveRecordClass(pathBPlan, d).recordClass).toBe('Dismissal');
  });

  it('only routine actions taken is routine', () => {
    const d = skip(take(emptyDraft(ID), 'mark-complete'), 'reschedule');
    const derived = deriveRecordClass(doPlan, d);
    expect(derived.recordClass).toBe('Routine');
    expect(derived.why).toContain('Mark complete');
  });

  it('a judgement action makes it a judgement', () => {
    expect(deriveRecordClass(pathBPlan, take(emptyDraft(ID), 'send-budget-inquiry')).recordClass).toBe('Judgement');
  });

  it('any Next step makes it a judgement even when every action taken is routine', () => {
    let d = skip(take(emptyDraft(ID), 'mark-complete'), 'reschedule');
    d = withNextSelected(d, ['add-todo']);
    d = withDecision(d, nextScope('add-todo'), 'taken');
    const derived = deriveRecordClass(doPlan, d);
    expect(derived.recordClass).toBe('Judgement');
    expect(derived.why).toContain('Next step');
  });
});

describe('buildReview', () => {
  it('lists every action the plan offered exactly once, taken or skipped, with the values sent', () => {
    let d = emptyDraft(ID);
    d = take(d, 'send-budget-inquiry');
    d = withValue(d, actionScope('send-budget-inquiry'), 'to', 'a@b.example');
    d = withValue(d, actionScope('send-budget-inquiry'), 'subject', 'S');
    d = withValue(d, actionScope('send-budget-inquiry'), 'body', 'B');
    d = take(d, 'revise-budget');
    d = withLookup(d, actionScope('revise-budget'), 'budget', BUDGET_A);
    d = withValue(d, actionScope('revise-budget'), 'amount', '250,000.50');
    d = withValue(d, actionScope('revise-budget'), 'reason', 'Appeal added');
    d = skip(d, 'approve-variance');
    const review = buildReview(pathBPlan, d);
    expect(review.reviewId).toBe(ID);
    expect(review.confirmed).toBe(true);
    expect(review.actions.map(a => [a.code, a.taken])).toEqual([
      ['send-budget-inquiry', true],
      ['revise-budget', true],
      ['approve-variance', false],
    ]);
    expect(review.actions[0].values).toEqual({ to: 'a@b.example', subject: 'S', body: 'B' });
    // Money goes as a number; a Lookup as the picked id; a skipped action carries no values.
    expect(review.actions[1].values).toEqual({ budget: BUDGET_A.id, amount: 250000.5, reason: 'Appeal added' });
    expect(review.actions[2]).toEqual({ code: 'approve-variance', taken: false });
    expect(review.dismissal).toBeUndefined();
    expect(review.nextSteps).toBeUndefined();
  });

  it('an action removed by an exclusion is sent as skipped, with no values', () => {
    let d = take(emptyDraft(ID), 'approve-variance');
    d = withValue(d, actionScope('approve-variance'), 'amount', '10');
    d = take(d, 'revise-budget'); // a stale choice on a removed step must not count
    const review = buildReview(approveFirstPlan, d);
    const revise = review.actions.find(a => a.code === 'revise-budget');
    expect(revise).toEqual({ code: 'revise-budget', taken: false });
  });

  it('sends a dismissal only when nothing was taken, with detail only when typed', () => {
    let d = emptyDraft(ID);
    for (const a of pathBPlan.actions) d = skip(d, a.code);
    d = withDismissal(d, 'other', '  wrong  ');
    const review = buildReview(pathBPlan, d);
    expect(review.dismissal).toEqual({ code: 'other', detail: 'wrong' });
    expect(review.actions.every(a => !a.taken)).toBe(true);
    const plain = buildReview(pathBPlan, withDismissal(d, 'already-approved-offline', ''));
    expect(plain.dismissal).toEqual({ code: 'already-approved-offline' });
  });

  it('carries the ticked Next steps taken, Also resolve and the retry flag', () => {
    let d = take(emptyDraft(ID), 'send-budget-inquiry');
    d = withNextSelected(d, ['add-todo']);
    d = withDecision(d, nextScope('add-todo'), 'taken');
    d = withValue(d, nextScope('add-todo'), 'title', 'Chase');
    const review = buildReview(pathBPlan, { ...d, alsoResolve: ['x'] }, { retry: true });
    expect(review.nextSteps).toEqual([{ code: 'add-todo', values: { title: 'Chase' } }]);
    expect(review.alsoResolve).toEqual(['x']);
    expect(review.retry).toBe(true);
    expect(review.dismissal).toBeUndefined();
  });

  it('a ticked Next step that was skipped is not sent', () => {
    let d = skip(emptyDraft(ID), 'send-budget-inquiry');
    d = withNextSelected(d, ['add-todo']);
    d = withDecision(d, nextScope('add-todo'), 'skipped');
    expect(buildReview(pathBPlan, d).nextSteps).toBeUndefined();
  });
});

describe('readiness', () => {
  it('an action with no decision yet blocks the review', () => {
    const d = take(emptyDraft(ID), 'send-budget-inquiry');
    expect(undecidedSteps(pathBPlan, d)).toEqual(['Revise budget', 'Approve variance']);
    expect(reviewReady(pathBPlan, d)).toBe(false);
  });

  it('a taken action with a required field empty blocks the review; a malformed Money value too', () => {
    let d = skip(skip(take(emptyDraft(ID), 'approve-variance'), 'send-budget-inquiry'), 'revise-budget');
    d = withDecision(d, actionScope('approve-variance'), 'taken');
    expect(reviewReady(approveFirstPlan, d)).toBe(false);
    d = withValue(d, actionScope('approve-variance'), 'amount', '-5');
    expect(reviewReady(approveFirstPlan, d)).toBe(false);
    d = withValue(d, actionScope('approve-variance'), 'amount', '5');
    expect(reviewReady(approveFirstPlan, d)).toBe(true);
  });

  it('a dismissal needs a reason from the list, and Other needs detail', () => {
    let d = emptyDraft(ID);
    for (const a of pathBPlan.actions) d = skip(d, a.code);
    expect(reviewReady(pathBPlan, d)).toBe(false);
    expect(dismissalValid(pathBPlan, withDismissal(d, 'not-on-the-list'))).toBe(false);
    d = withDismissal(d, 'other');
    expect(reviewReady(pathBPlan, d)).toBe(false);
    d = withDismissal(d, 'other', 'because');
    expect(reviewReady(pathBPlan, d)).toBe(true);
  });

  it('parses Money as zero or more, with thousands separators', () => {
    expect(parseMoney('1,250.5')).toBe(1250.5);
    expect(parseMoney('0')).toBe(0);
    expect(parseMoney('-1')).toBeNull();
    expect(parseMoney('abc')).toBeNull();
    expect(parseMoney('  ')).toBeNull();
  });
});

describe('the discard check: unrecorded CHOICES, not starting values', () => {
  it('a fresh draft and a draft with only starting values are clean', () => {
    const d = withDefaults(emptyDraft(ID), actionScope('send-budget-inquiry'), { body: 'templated' });
    expect(hasUnrecordedChoices(emptyDraft(ID))).toBe(false);
    expect(hasUnrecordedChoices(d)).toBe(false);
    const preselected = withLookup(emptyDraft(ID), actionScope('revise-budget'), 'budget', BUDGET_A, true);
    expect(hasUnrecordedChoices(preselected)).toBe(false);
  });

  it.each([
    ['a decision', (d: ItemDraft) => skip(d, 'revise-budget')],
    ['a typed value', (d: ItemDraft) => withValue(d, actionScope('send-budget-inquiry'), 'to', 'x')],
    ['a ticked Next step', (d: ItemDraft) => withNextSelected(d, ['add-todo'])],
    ['a dismissal reason', (d: ItemDraft) => withDismissal(d, 'other')],
    ['a picked lookup', (d: ItemDraft) => withLookup(d, actionScope('revise-budget'), 'budget', BUDGET_A)],
  ])('%s is a choice', (_name, change) => {
    expect(hasUnrecordedChoices(change(emptyDraft(ID)))).toBe(true);
  });

  it('un-ticking a Next step forgets its decision', () => {
    let d = withNextSelected(emptyDraft(ID), ['add-todo']);
    d = withDecision(d, nextScope('add-todo'), 'taken');
    d = withNextSelected(d, []);
    expect(d.decisions[nextScope('add-todo')]).toBeUndefined();
  });
});
