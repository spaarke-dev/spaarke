/**
 * decisionRecording - pure helpers for the moment a review is posted and the moment it comes back: whether a failed attempt
 * may be repeated with the same review id, and what the record looks like locally once it was written (so the item can flip
 * to read-only in place without another read).
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import { deriveRecordClass } from './decisionDraft';
import type { ItemDraft } from './decisionDraft';
import type {
  DecidedFollowOn,
  DecisionCommitFailure,
  DecisionCommitResult,
  DecisionPlan,
  DecisionResolution,
  DecisionReview,
  DecisionWizardItem,
} from './decisionWizardTypes';
import type { RecordClass } from '../ConsoleKit/consoleStatus';

/**
 * A failure that must keep its review id. The commit route stores the FIRST outcome of a review id: a repeat of the same id with
 * the same content returns it (or resumes it with `retry`), the same id with different content is refused, and a NEW id runs
 * every step again, including the writes that already landed. So only a plain refusal that stored nothing (a 4xx other than
 * 409: forbidden, not found, refused or invalid) may be edited into a different review under a fresh id. Everything else keeps
 * the id: a Failed outcome (500), a review already in progress or in conflict (409), the service unavailable (503), no answer
 * at all (0), and anything that reports landed, possibly landed, resumable, record-may-exist or sent-but-not-recorded.
 */
export function failureKeepsReview(failure: DecisionCommitFailure): boolean {
  if (
    failure.landed.length > 0 ||
    failure.possiblyLanded.length > 0 ||
    failure.resumable ||
    failure.recordMayExist ||
    failure.sentButNotRecorded
  ) {
    return true;
  }
  const plainRefusal = failure.httpStatus >= 400 && failure.httpStatus < 500 && failure.httpStatus !== 409;
  return !plainRefusal;
}

/** The Signal states of a 200 answer that mean the item is closed. `InquirySent` and `Unchanged` leave it open. */
const CLOSED_STATES: readonly string[] = ['Acted', 'Dismissed'];

/**
 * Whether the route says it closed this Signal. Only Acted and Dismissed count; an answer that lists no Signals at all (an older
 * route) is taken at the review's word.
 */
export function closesSignal(result: DecisionCommitResult, signalId: string): boolean {
  const state = result.signals.find(s => s.signalId === signalId)?.state;
  if (state === undefined) return result.signals.length === 0;
  return CLOSED_STATES.includes(state);
}

/** A `Failed` outcome (the route's 500): the answer that says what landed and whether the review can be resumed. */
export function isFailedOutcome(failure: DecisionCommitFailure): boolean {
  return failure.httpStatus === 500;
}

/** The content of a review without its identity: two reviews with the same key are the same decision. */
export function reviewKey(review: DecisionReview): string {
  const content: Partial<DecisionReview> = { ...review };
  delete content.reviewId;
  delete content.retry;
  return JSON.stringify(content);
}

const RECORD_CLASSES: readonly RecordClass[] = ['Judgement', 'Routine', 'Dismissal'];

/** What the closed item shows after this session recorded it: built from the review and the commit result, not re-read. */
export function resolutionFromRecord(
  plan: DecisionPlan,
  item: DecisionWizardItem,
  draft: ItemDraft,
  review: DecisionReview,
  result: DecisionCommitResult,
  now: Date
): DecisionResolution {
  const dismissed = review.dismissal != null;
  const stored = RECORD_CLASSES.find(c => c === result.recordClass);
  const reason = dismissed ? plan.dismissalReasons.find(r => r.code === review.dismissal?.code) : undefined;
  const followOns: DecidedFollowOn[] = (review.nextSteps ?? []).map(n => {
    const written = result.steps.find(s => s.code === n.code)?.written[0];
    return { code: n.code, entity: written?.entity ?? null, id: written?.id ?? null };
  });
  const derived = deriveRecordClass(plan, draft).recordClass;
  return {
    leftOpen: !closesSignal(result, item.row.signalId),
    resolutionType: dismissed ? 'Dismissed' : 'Acted',
    recordClass: stored ?? derived,
    recordNumber: null,
    decidedByName: null,
    decidedOn: now.toISOString(),
    reason: reason ? [reason.label, review.dismissal?.detail].filter(Boolean).join(' - ') : null,
    outcome: dismissed ? 'Dismissed' : 'Authorized',
    steps: review.actions.map(a => ({ code: a.code, taken: a.taken, values: a.values ?? null })),
    followOns,
    // The evidence as it stood when the record was written is what this session just showed.
    factSnapshot: item.evidence,
  };
}
