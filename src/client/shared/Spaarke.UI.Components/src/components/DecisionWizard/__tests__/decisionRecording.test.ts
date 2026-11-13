/**
 * decisionRecording - which failed attempts keep their review id (task 058). The commit route stores the FIRST outcome of a
 * review id, so only a plain refusal that stored nothing may be edited and re-posted under a fresh id; anything else repeated
 * under a new id would run the steps that already landed again.
 */

import {
  canRecordWithoutSending,
  closesSignal,
  failureKeepsReview,
  isFailedOutcome,
  needsSendChoice,
  resolutionFromRecord,
  reviewKey,
} from '../decisionRecording';
import { failureHeading, failureMessage, describeWarning, lockedNoteText } from '../decisionDisplay';
import { emptyDraft } from '../decisionDraft';
import type { DecisionCommitFailure, DecisionReview } from '../decisionWizardTypes';
import { commitResult, pathBItem, pathBPlan, PATH_B_ID } from '../__fixtures__/decisionWizardFixtures';

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

  it('task 191: the stored failure returned again (409 + replayed) is the Failed outcome too', () => {
    expect(isFailedOutcome(failure({ httpStatus: 409, replayed: true }))).toBe(true);
    expect(isFailedOutcome(failure({ httpStatus: 422, replayed: true }))).toBe(false);
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

describe('task 191 fix round 1', () => {
  const unfinished = failure({
    httpStatus: 409,
    reasonCode: 'ontology.decision.review_unfinished',
    detail:
      'An earlier decision on this item is unfinished. Close this and open the item again to finish it. Nothing was done.',
  });

  it('a refusal because ANOTHER review is unfinished stored nothing, so it does not pin this one, and says what to do', () => {
    expect(failureKeepsReview(unfinished)).toBe(false);
    expect(failureHeading(unfinished)).toBe('An earlier decision on this item is unfinished');
    expect(failureMessage(unfinished)).toMatch(/open the item again to finish it/);
  });

  it('F1: an inquiry recorded without sending reads "inquiry not sent", not "Inquiry sent"', () => {
    const review: DecisionReview = {
      reviewId: 'r',
      confirmed: true,
      actions: [{ code: 'send-budget-inquiry', taken: true }],
    };
    const result = commitResult({
      steps: [
        {
          key: 'a',
          code: 'send-budget-inquiry',
          phase: 4,
          status: 'NotSentByChoice',
          written: [],
          possiblyWritten: [],
        },
      ],
      signals: [{ signalId: PATH_B_ID, state: 'InquirySent' }],
    });
    const resolution = resolutionFromRecord(pathBPlan, pathBItem, emptyDraft('r'), review, result, new Date());
    expect(resolution.leftOpen).toBe(true);
    expect(resolution.note).toBe('Decision recorded, inquiry not sent - this item is still open');

    const sent = commitResult({ signals: [{ signalId: PATH_B_ID, state: 'InquirySent' }] });
    expect(resolutionFromRecord(pathBPlan, pathBItem, emptyDraft('r'), review, sent, new Date()).note).toBeUndefined();
  });

  it('fix round 2: a resumable failure at a landed inquiry may be recorded without sending; others may not', () => {
    const inquiryStep = {
      key: 'k',
      code: 'send-budget-inquiry',
      phase: 4,
      status: 'Failed',
      written: [{ entity: 'sprk_servicerequest', id: 'sr' }],
      possiblyWritten: [],
    };
    const notSent = failure({ resumable: true, steps: [inquiryStep], landed: inquiryStep.written });
    expect(canRecordWithoutSending(notSent)).toBe(true);
    expect(lockedNoteText(notSent)).toMatch(/Record without sending records the decision without it/);
    expect(canRecordWithoutSending({ ...notSent, resumable: false })).toBe(false);
    expect(canRecordWithoutSending(failure({ resumable: true }))).toBe(false);
    expect(canRecordWithoutSending(failure({ resumable: true, steps: [{ ...inquiryStep, status: 'Done' }] }))).toBe(
      false
    );
  });

  it('F2: the locked note names the way on the item offers', () => {
    const landed = [{ entity: 'sprk_servicerequest', id: 'sr' }];
    expect(lockedNoteText(failure({ resumable: true, sendChoiceRequired: true, landed }))).toMatch(
      /check Sent Items, then choose Send again or Record without sending/
    );
    expect(lockedNoteText(failure({ resumable: true, landed }))).toMatch(
      /Retry recording finishes it without repeating/
    );
    expect(lockedNoteText(failure({ resumable: true, landed }))).toMatch(/finish it later from this item/);
    expect(lockedNoteText(failure({ resumable: true }))).not.toMatch(/finish it later/);
    expect(lockedNoteText(failure({ httpStatus: 0 }))).toMatch(/Try recording it again, or close without recording/);
    for (const f of [
      failure({ resumable: true, landed }),
      failure({ resumable: true, sendChoiceRequired: true, landed }),
    ])
      expect(lockedNoteText(f)).not.toMatch(/same decision again/);
  });
});

describe('task 191 (D-223): the inquiry email that may have been sent', () => {
  const choice = failure({ resumable: true, sendChoiceRequired: true, reasonCode: 'ontology.action.write_failed' });

  it('needs the choice only for a resumable failure that asks for it', () => {
    expect(needsSendChoice(choice)).toBe(true);
    expect(needsSendChoice(failure({ resumable: true }))).toBe(false);
    expect(needsSendChoice(failure({ resumable: false, sendChoiceRequired: true }))).toBe(false);
    expect(needsSendChoice(null)).toBe(false);
  });

  it('tells the person to check Sent Items and names the two choices', () => {
    expect(failureMessage(choice)).toMatch(/Check Sent Items/);
    expect(failureMessage(choice)).toMatch(/Send again or Record without sending/);
  });

  it('a replayed failure (409) reads as the failure, not as a review in progress', () => {
    const replay = failure({ httpStatus: 409, replayed: true, reasonCode: 'ontology.decision.not_resumable' });
    expect(failureHeading(replay)).toBe('The decision was not recorded');
    expect(failureMessage(replay)).toBe('The earlier attempt cannot be continued. Nothing more was recorded.');
    expect(failureHeading(failure({ httpStatus: 409 }))).toBe('This decision is already being recorded');
  });

  it('a Signal that is no longer open says so, and that what landed stands', () => {
    const closed = failure({ httpStatus: 409, reasonCode: 'ontology.decision.signal_not_open' });
    expect(failureHeading(closed)).toBe('This item is no longer open');
    expect(failureMessage(closed)).toMatch(/already carried out stands/);
  });

  it('the re-close warning is worded, not the generic follow-up text', () => {
    expect(describeWarning('reclosed_after_system_resolution')).toMatch(/closed automatically/);
  });

  it('the choice is not part of the review content (the same review with a choice is the same review)', () => {
    const review: DecisionReview = { reviewId: 'r', confirmed: true, actions: [] };
    expect(reviewKey({ ...review, retry: true, sendChoice: 'send-again' })).toBe(reviewKey(review));
  });
});
