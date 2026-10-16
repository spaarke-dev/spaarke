/**
 * classificationView - how the Classification area words what the BFF returns (task 103; D-49, FR-59). Pure, so the wording is
 * testable without rendering.
 *
 * Recall is shown against the floor the BFF returned (80 percent on at least 50 labelled items). A category never measured shows
 * "Not measured" and no figure, never zero. No classifier confidence is shown: none exists (#5).
 */

import type { StatusBadgeTone } from '../../StatusBadge';
import type { ClassificationRecall, ClassificationRuleReference, ClassificationVersionState } from './classificationService';

export interface RecallView {
  /** The badge text. */
  label: string;
  tone: StatusBadgeTone;
  /** The figures behind the badge; null when not measured. */
  detail: string | null;
  /** Set when the category was edited after the measurement, so the figure may be out of date; null otherwise. */
  staleNote: string | null;
}

const RECALL_BADGE: Record<ClassificationRecall['status'], { label: string; tone: StatusBadgeTone }> = {
  NotMeasured: { label: 'Not measured', tone: 'neutral' },
  MeetsFloor: { label: 'Meets floor', tone: 'success' },
  BelowFloor: { label: 'Below floor', tone: 'critical' },
  SetTooSmall: { label: 'Set too small', tone: 'warning' },
};

export const MEASURED_BEFORE_LAST_EDIT = 'Measured before the last edit';

/** "88.9% on 92 items, measured 2026-10-10 (floor 80% on 50 items)", with the badge that judges it. */
export function describeRecall(recall: ClassificationRecall, floor: { percent: number; minItems: number }): RecallView {
  const badge = RECALL_BADGE[recall.status];
  if (recall.status === 'NotMeasured' || recall.recall === null) {
    return { ...RECALL_BADGE.NotMeasured, detail: null, staleNote: null };
  }
  const size = recall.labelledSetSize === null ? 'an unknown number of items' : `${recall.labelledSetSize} items`;
  const when = recall.measuredOn === null ? '' : `, measured ${recall.measuredOn}`;
  return {
    ...badge,
    detail: `${recall.recall.toFixed(1)}% on ${size}${when} (floor ${floor.percent}% on ${floor.minItems} items)`,
    staleNote: recall.measuredBeforeLastEdit ? MEASURED_BEFORE_LAST_EDIT : null,
  };
}

const STATE_WORDS: Record<ClassificationVersionState, string> = {
  InForce: 'in force',
  Draft: 'draft',
  Earlier: 'earlier version',
};

/** One rule (policy) with the versions of it that name the category, newest first. */
export interface RuleReadsView {
  policyId: string;
  policyCode: string;
  policyName: string | null;
  policyEnabled: boolean;
  /** For example "version 2 (in force), version 1 (earlier version)". */
  versions: string;
}

/** Groups the version-level references by rule, so a rule with two versions that name the category is listed once. */
export function groupRuleReads(readBy: ReadonlyArray<ClassificationRuleReference>): RuleReadsView[] {
  const byPolicy = new Map<string, ClassificationRuleReference[]>();
  for (const ref of readBy) {
    const group = byPolicy.get(ref.policyId);
    if (group) group.push(ref);
    else byPolicy.set(ref.policyId, [ref]);
  }
  return [...byPolicy.values()].map(group => {
    const ordered = [...group].sort((a, b) => (b.versionNumber ?? 0) - (a.versionNumber ?? 0));
    const first = ordered[0];
    return {
      policyId: first.policyId,
      policyCode: first.policyCode,
      policyName: first.policyName,
      policyEnabled: first.policyEnabled,
      versions: ordered
        .map(r => `${r.versionNumber === null ? 'a version' : `version ${r.versionNumber}`} (${STATE_WORDS[r.versionState]})`)
        .join(', '),
    };
  });
}

/** The volume cell: the count, or a dash when it could not be read (never zero for unknown). */
export function describeVolume(volume: number | null): string {
  return volume === null ? '—' : String(volume);
}
