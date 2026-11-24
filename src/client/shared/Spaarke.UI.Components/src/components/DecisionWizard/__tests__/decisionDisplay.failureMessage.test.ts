/**
 * failureMessage - the commit refusal for a billing item (task 175, D-207): the route refuses a Signal whose subject is an invoice or
 * an invoice line with `ontology.decision.decided_in_invoice_review`, and the wizard says where to decide it.
 */
import { failureHeading, failureMessage } from '../decisionDisplay';
import type { DecisionCommitFailure } from '../decisionWizardTypes';

const failure = (overrides: Partial<DecisionCommitFailure> = {}): DecisionCommitFailure => ({
  httpStatus: 422,
  title: 'The decision was refused',
  detail: 'Subject is an invoice.',
  reasonCode: null,
  correlationId: 'trace-1',
  landed: [],
  possiblyLanded: [],
  steps: [],
  recordMayExist: false,
  sentButNotRecorded: false,
  resumable: false,
  replayed: false,
  ...overrides,
});

const CODE = 'ontology.decision.decided_in_invoice_review';

describe('a billing item refused by the route (task 175, D-207)', () => {
  it('a 409 with the code says to decide it in Invoice Review, not that it is being recorded', () => {
    const f = failure({ httpStatus: 409, reasonCode: CODE });
    expect(failureHeading(f)).toBe('Decide this in Invoice Review');
    expect(failureMessage(f)).toBe('Invoice items are decided in Invoice Review. Nothing was recorded.');
  });

  it('the code wins over any status', () => {
    for (const httpStatus of [403, 409, 422]) {
      const f = failure({ httpStatus, reasonCode: CODE });
      expect(failureHeading(f)).toBe('Decide this in Invoice Review');
      expect(failureMessage(f)).toBe('Invoice items are decided in Invoice Review. Nothing was recorded.');
    }
  });

  it('negative: a 409 with another code keeps the existing heading and text', () => {
    const f = failure({ httpStatus: 409, reasonCode: 'ontology.decision.review_in_progress' });
    expect(failureHeading(f)).toBe('This decision is already being recorded');
    expect(failureMessage(f)).toMatch(/already being recorded/);
    const none = failure({ httpStatus: 409, reasonCode: null });
    expect(failureHeading(none)).toBe('This decision is already being recorded');
    expect(failureMessage(none)).toMatch(/already being recorded/);
  });

  it('negative: an unknown code on a 422 falls back to the status heading and the route sentence', () => {
    const f = failure({ reasonCode: 'ontology.decision.something_else' });
    expect(failureHeading(f)).toBe('The decision was refused');
    expect(failureMessage(f)).toBe('Subject is an invoice.');
  });
});
