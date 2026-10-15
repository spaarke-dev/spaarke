/**
 * decisionItemContext - what every step of ONE open item reads. The wizard's step configs are captured by the shell when a
 * dynamic step is added (a Next step, a step that comes back after an exclusion lifts), so a step's content must never close
 * over the draft: it reads the draft from this context, which always holds the latest. Step contents are components with static
 * props (a code), and `canAdvance` predicates read through refs for the same reason.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import * as React from 'react';
import type { ILookupItem } from '../../types/LookupTypes';
import type { ItemDraft } from './decisionDraft';
import type {
  DecisionCommitFailure,
  DecisionCommitResult,
  DecisionLookupRequest,
  DecisionPlan,
  DecisionResolution,
  DecisionWizardItem,
  SuppressionStatus,
} from './decisionWizardTypes';

export type DecisionLookup = (request: Omit<DecisionLookupRequest, 'core'>) => Promise<ILookupItem[]>;

export interface DecisionItemContextValue {
  item: DecisionWizardItem;
  plan: DecisionPlan;
  /** Null while it loads or when it could not be read (`suppressionFailed`). Never read for a closed item. */
  suppression: SuppressionStatus | null;
  suppressionFailed: boolean;
  draft: ItemDraft;
  update: (change: (draft: ItemDraft) => ItemDraft) => void;
  /** Present when the item is closed (opened closed, or just recorded): every step then shows what was sent, read-only. */
  resolution: DecisionResolution | null;
  /** True for the item just recorded in this session (the status bar leads with "Recorded"). */
  justRecorded: boolean;
  /** The last failed attempt to record this item, if any. */
  failure: DecisionCommitFailure | null;
  /** A failed attempt pins the review: its fields and choices are read-only until the attempt is repeated. */
  locked: boolean;
  /** Items known to be closed (recorded here, resolved by Also resolve, or opened closed). */
  closedIds: ReadonlySet<string>;
  /** The route's 200 answer for the item just recorded (warnings, per-item states); null otherwise. */
  recordResult: DecisionCommitResult | null;
  /** Absent when the host cannot search; a Lookup / Person field then says so. */
  lookup: DecisionLookup | null;
  /** Other open items remaining after this one (for the status bar). */
  openItemsLeft: number | null;
  /** "Now" for ages and due states; injectable for tests. */
  today: Date;
}

export const DecisionItemContext = React.createContext<DecisionItemContextValue | null>(null);

export function useDecisionItem(): DecisionItemContextValue {
  const value = React.useContext(DecisionItemContext);
  if (!value) throw new Error('useDecisionItem must be used inside the decision wizard');
  return value;
}
