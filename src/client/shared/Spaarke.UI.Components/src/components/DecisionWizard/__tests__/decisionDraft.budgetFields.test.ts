/**
 * Task 167 (D-193) - the budget step's new fields at the pure-logic level. The stated contract:
 *  - a parameter the server gave a default for starts at that value, as a STARTING value (not a choice), and the user can change it
 *  - an End date before its Start date is flagged and keeps the step from going on; the same day, or a missing date, is fine
 *  - the posted review carries every filled optional field, the currency included; blank optional fields are not sent
 *  - nothing changes for a parameter without a default or a date order
 */

import {
  actionScope,
  buildReview,
  dateOrderMessage,
  emptyDraft,
  hasUnrecordedChoices,
  reviewReady,
  stepValid,
  withDecision,
  withValue,
} from '../decisionDraft';
import type { ItemDraft } from '../decisionDraft';
import { withTemplatedDefaults } from '../templatedDrafts';
import { EURO_ID, DOLLAR_ID, createBudget, createBudgetPlan, pathBItem, reviseBudget } from '../__fixtures__/decisionWizardFixtures';

const ID = '12345678-1234-4234-8234-123456789abc';
const scope = actionScope('create-budget');
const endDate = createBudget.parameters.find(p => p.code === 'endDate')!;

function filled(extra: Record<string, string> = {}): ItemDraft {
  let d = withTemplatedDefaults(emptyDraft(ID), createBudgetPlan, pathBItem);
  d = withValue(d, scope, 'amount', '250000');
  d = withValue(d, scope, 'reason', 'No budget was ever set');
  for (const [code, value] of Object.entries(extra)) d = withValue(d, scope, code, value);
  return withDecision(d, scope, 'taken');
}

describe('the server default (the matter business unit currency)', () => {
  it('starts the Currency field at the default, and a draft holding only that default is not dirty', () => {
    const d = withTemplatedDefaults(emptyDraft(ID), createBudgetPlan, pathBItem);

    expect(d.defaults[scope]).toMatchObject({ currency: EURO_ID });
    expect(hasUnrecordedChoices(d)).toBe(false);
  });

  it('is sent when the user changes nothing, and replaced when the user picks another currency', () => {
    expect(buildReview(createBudgetPlan, filled()).actions[0].values).toMatchObject({ currency: EURO_ID });
    expect(buildReview(createBudgetPlan, filled({ currency: DOLLAR_ID })).actions[0].values).toMatchObject({ currency: DOLLAR_ID });
  });

  it('is not sent when the user clears it (an omitted currency is the server default again)', () => {
    const values = buildReview(createBudgetPlan, filled({ currency: '' })).actions[0].values!;

    expect(values).not.toHaveProperty('currency');
  });

  it('must not fire: an action whose parameters declare no default starts empty', () => {
    const d = withTemplatedDefaults(emptyDraft(ID), { ...createBudgetPlan, actions: [reviseBudget] }, pathBItem);

    expect(d.defaults[actionScope('revise-budget')]).toBeUndefined();
  });
});

describe('End date must not be before Start date', () => {
  it('flags an end date before the start date, in words, on the end date', () => {
    const d = filled({ startDate: '2026-06-02', endDate: '2026-06-01' });

    expect(dateOrderMessage(createBudget, endDate, d, scope)).toBe('End date cannot be before Start date.');
    expect(stepValid(createBudget, d, scope)).toBe(false);
    expect(reviewReady(createBudgetPlan, d)).toBe(false);
  });

  it.each([
    ['the same day', '2026-06-01', '2026-06-01'],
    ['a later end date', '2026-06-01', '2026-06-30'],
    ['no start date', '', '2026-06-30'],
    ['no end date', '2026-06-01', ''],
    ['neither', '', ''],
  ])('must not fire: %s', (_label, startDate, endDateValue) => {
    const d = filled({ startDate, endDate: endDateValue });

    expect(dateOrderMessage(createBudget, endDate, d, scope)).toBeNull();
    expect(stepValid(createBudget, d, scope)).toBe(true);
  });

  it('does not judge a half-typed date: only two well-formed dates are compared', () => {
    const d = filled({ startDate: '2026-06-02', endDate: '2026-06-0' });

    // The malformed date is already refused by the Date rule; the order message stays out of the way.
    expect(dateOrderMessage(createBudget, endDate, d, scope)).toBeNull();
    expect(stepValid(createBudget, d, scope)).toBe(false);
  });
});

describe('the posted review', () => {
  it('carries every filled optional field and leaves the blank ones out', () => {
    const full = buildReview(
      createBudgetPlan,
      filled({ category: '100000001', year: '2026', startDate: '2026-01-01', endDate: '2026-12-31', period: '0' })
    ).actions[0];
    const bare = buildReview(createBudgetPlan, filled()).actions[0];

    expect(full.values).toEqual({
      amount: 250000,
      category: '100000001',
      year: '2026',
      startDate: '2026-01-01',
      endDate: '2026-12-31',
      period: '0',
      currency: EURO_ID,
      reason: 'No budget was ever set',
    });
    expect(bare.values).toEqual({ amount: 250000, currency: EURO_ID, reason: 'No budget was ever set' });
  });

  it('refuses a Category that is not one of the plan options (the commit would)', () => {
    expect(stepValid(createBudget, filled({ category: '999' }), scope)).toBe(false);
    expect(stepValid(createBudget, filled({ category: '100000000' }), scope)).toBe(true);
  });
});
