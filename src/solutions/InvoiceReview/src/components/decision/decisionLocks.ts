/**
 * The requests that are locked until they finish (ebilling-r1 task 095f-2ui, review round 1).
 *
 * A decision that came back saved in part (503 incomplete) or with no answer at all (status 0) may already be recorded. It is finished by
 * sending the SAME request again, so the form that sent it is locked and what it sent is kept here, at page level: closing the wizard or the
 * blocked-submission form and opening it again must not lose the lock (a fresh form would send a different reason or note, or for an
 * adjustment a different id, and the server would refuse it or, worse, file it twice).
 *
 * One `DecisionLocks` is held by the `ApprovalProvider` (one per open invoice) and read by the wizard and the blocked banner. It is plain
 * memory, not React state: the forms read it once, when they open, and write it when a send comes back. A lock is cleared when the request
 * finishes, or when the server's state shows the decision is already done.
 */

import type { AdjustmentMethod } from '../../services/decisionApi';
import type { InvoiceOutcome } from '../../services/decisionModel';

/** An approve, reject or return that came back unsure: the exact outcome, reason and note that were sent (the server derives the decision id). */
export interface WizardLock {
  outcome: InvoiceOutcome;
  /** The reason as sent (trimmed); empty for an approve. */
  reason: string;
  /** The note as typed. */
  note: string;
}

/** An Override, a Return or an operator finish (ISS-034) of the blocked submission that came back unsure. */
export interface BlockedLock {
  action: 'override' | 'return' | 'finish';
  /** The reason as sent (trimmed). */
  reason: string;
  /** The note as typed. */
  note: string;
}

/** An adjustment add that came back unsure: its id and what it sent. The same id is what makes the repeat file it once. */
export interface AdjustLock {
  adjustmentId: string;
  method: AdjustmentMethod;
  value: string;
  reason: string;
  rule: string;
  note: string;
}

interface LockMap {
  wizard: WizardLock;
  blocked: BlockedLock;
  adjust: AdjustLock;
}

export interface DecisionLocks {
  get<K extends keyof LockMap>(key: K): LockMap[K] | undefined;
  set<K extends keyof LockMap>(key: K, value: LockMap[K]): void;
  clear(key: keyof LockMap): void;
}

export function createDecisionLocks(): DecisionLocks {
  const held = new Map<keyof LockMap, unknown>();
  return {
    get: key => held.get(key) as never,
    set: (key, value) => {
      held.set(key, value);
    },
    clear: key => {
      held.delete(key);
    },
  };
}
