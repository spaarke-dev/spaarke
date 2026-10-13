/**
 * consoleStatus — the nine Console states and their StatusBadge tones, in ONE table.
 *
 * Spaarke Console kit (spaarke-ontology-platform-r1 task 057, D-24; reconciliation R-4, R-14, R-15).
 * There is deliberately no StatusChip component: a state is a `StatusBadge` with a label and a tone, and
 * this table is the only place a state is mapped to either. Context-agnostic (ADR-012): no Dataverse,
 * PCF or Xrm dependency; callers pass the stored column values to `resolveConsoleState`.
 */

import type { StatusBadgeTone } from '../StatusBadge';

/** The nine display states of the Console (HANDOFF section 1.3 "Show state"). */
export type ConsoleState =
  'Open' | 'Decided' | 'Done' | 'Dismissed' | 'ClearedItself' | 'Superseded' | 'RuleRetired' | 'Authorized' | 'Denied';

export interface ConsoleStateBadge {
  label: string;
  tone: StatusBadgeTone;
  /** One-line plain-language meaning, usable as a tooltip. */
  description: string;
}

/** State to badge. Every ConsoleState has an entry (the type enforces it). */
export const CONSOLE_STATE_BADGES: Readonly<Record<ConsoleState, ConsoleStateBadge>> = {
  Open: { label: 'To decide', tone: 'info', description: 'Waiting for a decision.' },
  Decided: { label: 'Decided', tone: 'success', description: 'A person decided, through the gate.' },
  Done: { label: 'Done', tone: 'success', description: 'Your own work, finished (a Routine record).' },
  Dismissed: {
    label: 'Dismissed',
    tone: 'warning',
    description: 'A person dismissed it with a reason.',
  },
  ClearedItself: {
    label: 'Cleared on its own',
    tone: 'info',
    description: 'No one decided. The condition stopped being true on re-evaluation.',
  },
  Superseded: { label: 'Superseded', tone: 'neutral', description: 'Replaced by a newer item on the same subject.' },
  RuleRetired: {
    label: 'Closed - rule retired',
    tone: 'neutral',
    description: 'The rule was retired; nothing was decided.',
  },
  Authorized: { label: 'Authorized', tone: 'success', description: 'Approved at the gate; the action ran.' },
  Denied: { label: 'Denied', tone: 'critical', description: 'Declined at the gate; nothing ran.' },
};

/** Stored `sprk_resolutiontype` values; null / undefined while the Signal is open. */
export type ResolutionType = 'Acted' | 'Dismissed' | 'ConditionCleared' | 'Superseded' | 'PolicyRetired';
/** Stored `sprk_recordclass` values. */
export type RecordClass = 'Judgement' | 'Routine' | 'Dismissal';

/**
 * Derive the display state from stored columns (R-14). Acted + Routine reads as Done (R-4: the stored
 * outcome stays Authorized, only the display changes); Acted otherwise reads as Decided.
 */
export function resolveConsoleState(
  resolutionType: ResolutionType | null | undefined,
  recordClass?: RecordClass | null
): ConsoleState {
  switch (resolutionType) {
    case 'Acted':
      return recordClass === 'Routine' ? 'Done' : 'Decided';
    case 'Dismissed':
      return 'Dismissed';
    case 'ConditionCleared':
      return 'ClearedItself';
    case 'Superseded':
      return 'Superseded';
    case 'PolicyRetired':
      return 'RuleRetired';
    default:
      return 'Open';
  }
}

/** Stored `sprk_decisionoutcome` values on `sprk_decisionrecord`. */
export type DecisionOutcome = 'Authorized' | 'Denied' | 'Dismissed';

/**
 * Derive the display state of a Decision Record row from its own columns (`sprk_decisionoutcome`,
 * `sprk_recordclass`). `sprk_resolutiontype` lives on `sprk_signal`, not here, so use this resolver for
 * `RecordRow`. A Routine record displays as Done whatever its outcome (R-4); the stored outcome is unchanged.
 */
export function resolveDecisionRecordState(
  outcome: DecisionOutcome | null | undefined,
  recordClass?: RecordClass | null
): ConsoleState {
  if (recordClass === 'Routine') return 'Done';
  switch (outcome) {
    case 'Authorized':
      return 'Authorized';
    case 'Denied':
      return 'Denied';
    case 'Dismissed':
      return 'Dismissed';
    default:
      return 'Open';
  }
}
