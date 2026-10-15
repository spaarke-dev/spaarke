/**
 * decisionRecording - which failed attempts keep their review id (task 058). The commit route stores the FIRST outcome of a
 * review id, so only a plain refusal that stored nothing may be edited and re-posted under a fresh id; anything else repeated
 * under a new id would run the steps that already landed again.
 */

import { closesSignal, failureKeepsReview, isFailedOutcome } from '../decisionRecording';
import type { DecisionCommitFailure } from '../decisionWizardTypes';
import { commitResult } from '../__fixtures__/decisionWizardFixtures';

const failure = (overrides: Partial<DecisionCommitFailure>): DecisionCommitFailure => ({
  httpStatus: 500,
  title: 't',
  detail: 'd',
  landed: [],
  possiblyLanded: [],
  steps: [],
  recordMayExist: false,
  sentButNotRecorded: false,
  resumable: false,
  replayed: false,
  ...overrides,
});

describe('failureKeepsReview', () => {
  it.each([
    ['a Failed outcome with nothing landed', { httpStatus: 500 }],
    ['a review already in progress or in conflict', { httpStatus: 409 }],
    ['the service unavailable', { httpStatus: 503 }],
    ['no answer at all', { httpStatus: 0 }],
    ['a refusal that reports a landed write', { httpStatus: 422, landed: [{ entity: 'e', id: '1' }] }],
    ['a resumable failure', { httpStatus: 422, resumable: true }],
  ])('keeps the id after %s', (_name, overrides) => {
    expect(failureKeepsReview(failure(overrides))).toBe(true);
  });

  it.each([[400], [403], [404], [422]])('lets a plain %i refusal be edited under a fresh id', status => {
    expect(failureKeepsReview(failure({ httpStatus: status }))).toBe(false);
  });
});

describe('isFailedOutcome / closesSignal', () => {
  it('only a 500 is the Failed outcome whose resumable flag decides retry', () => {
    expect(isFailedOutcome(failure({ httpStatus: 500 }))).toBe(true);
    expect(isFailedOutcome(failure({ httpStatus: 409 }))).toBe(false);
    expect(isFailedOutcome(failure({ httpStatus: 422 }))).toBe(false);
  });

  it('counts only Acted and Dismissed as closed; an answer that lists no Signals is taken at the review’s word', () => {
    const result = commitResult({
      signals: [
        { signalId: 'a', state: 'Acted' },
        { signalId: 'b', state: 'Dismissed' },
        { signalId: 'c', state: 'InquirySent' },
        { signalId: 'd', state: 'Unchanged', warning: 'signal_close_failed' },
      ],
    });
    expect(['a', 'b', 'c', 'd', 'z'].map(id => closesSignal(result, id))).toEqual([true, true, false, false, false]);
    expect(closesSignal(commitResult({ signals: [] }), 'a')).toBe(true);
  });
});
