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
 * A failure that must keep its review id: something may have landed or been sent (a new id would run the landed steps again),
 * the answer is not known (the same id makes a repeat safe), or the failed attempt is resumable. Only a plain refusal (nothing
 * written) may be edited into a different review with a fresh id.
 */
export function failureKeepsReview(failure: DecisionCommitFailure): boolean {
  return (
    failure.landed.length > 0 ||
    failure.possiblyLanded.length > 0 ||
    failure.resumable ||
    failure.recordMayExist ||
    failure.sentButNotRecorded ||
    failure.httpStatus === 0
  );
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
