/**
 * worklistWizardItem - builds the decision wizard's `DecisionWizardItem` from rows the Signal read route already returned
 * (task 038). The wizard asks its host for an item (`DecisionItemLoader`); the worklist answers from the sets it holds, so the
 * wizard never reads `sprk_signal` either (D-15) and never asks the user to wait for a second query.
 *
 * What the route does NOT return is shown as missing rather than invented: the rule sentence is `null`, and a closed item carries
 * its closing type and time but no Decision Record (record number, who decided, the values sent). See notes/059-worklist-widget.md.
 *
 * Task: spaarke-ontology-platform-r1, task 059.
 */

import type {
  DecisionEvidenceRef,
  DecisionResolution,
  DecisionWizardItem,
} from '../DecisionWizard/decisionWizardTypes';
import { CONSOLE_STATE_BADGES, resolveConsoleState } from '../ConsoleKit';
import type { EvidenceTier } from '../ConsoleKit';
import type { WorklistSetName, WorklistGroupData, WorklistRouteItem } from './worklistService';

const TIERS: ReadonlySet<string> = new Set(['Fact', 'Interpretation', 'Observation', 'Missing']);

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | undefined => (typeof v === 'string' && v !== '' ? v : undefined);

/**
 * `sprk_evidencerefs` as the route returns it (a JSON array of objects) to the wizard's evidence lines. A line the route's
 * read-time witness rule withheld keeps `withheld: true` and has no text, so the wizard can say the detail is withheld
 * (task 038, "UI" bullet) instead of "No value recorded". Not an array, or not readable: no lines (the wizard says none was recorded).
 */
export function parseEvidenceRefs(raw: string | null | undefined): DecisionEvidenceRef[] {
  if (!raw) return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return [];
  }
  if (!Array.isArray(parsed)) return [];
  const lines: DecisionEvidenceRef[] = [];
  for (const entry of parsed) {
    if (!isObject(entry)) continue;
    const tier = str(entry.tier);
    const line: DecisionEvidenceRef = {
      kind: str(entry.kind) ?? '',
      ref: str(entry.ref) ?? '',
      tier: (tier && TIERS.has(tier) ? tier : 'Missing') as EvidenceTier,
      text: str(entry.text) ?? null,
    };
    const source = str(entry.source);
    const asOf = str(entry.asOf);
    const clause = str(entry.clause);
    if (source) line.source = source;
    if (asOf) line.asOf = asOf;
    if (clause) line.clause = clause;
    if (entry.withheld === true) line.withheld = true;
    lines.push(line);
  }
  return lines;
}

/** A short calendar date; the year is added when it is not the current one. */
export function formatShortDate(iso: string | null | undefined, today: Date): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  return d.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    ...(d.getFullYear() === today.getFullYear() ? {} : { year: 'numeric' }),
  });
}

/**
 * How a closed or suppressed item reads on its line. Closed: how it closed (the Console state's label), with the date for the
 * "Closed earlier" set. Suppressed: until when it is not raised again. Never blank (Missing-never-blank).
 */
export function closedLineText(item: WorklistRouteItem, set: WorklistSetName, today: Date): string {
  if (set === 'suppressed') {
    const until = formatShortDate(item.suppressedUntil, today);
    return until ? `Not raised until ${until}` : 'Suppressed';
  }
  const label = item.resolutionType
    ? CONSOLE_STATE_BADGES[resolveConsoleState(item.resolutionType, null)].label
    : 'Closed';
  const when = set === 'closedEarlier' ? formatShortDate(item.resolvedOn, today) : null;
  return when ? `${label} ${when}` : label;
}

/**
 * The wizard's view of a closed or suppressed item. It is read-only whenever a `resolution` is present. The route returns how the
 * item closed and when, not the Decision Record, so the record number, the person and the values sent are left out (the wizard
 * shows what it has and does not replay steps it has no values for).
 */
export function resolutionFor(item: WorklistRouteItem, set: WorklistSetName, today: Date): DecisionResolution | null {
  if (set === 'lanes') return null;
  if (set === 'suppressed') {
    const until = formatShortDate(item.suppressedUntil, today);
    return {
      resolutionType: 'ConditionCleared',
      leftOpen: true,
      note: until ? `Not raised again until ${until}` : 'Suppressed',
    };
  }
  if (!item.resolutionType) {
    return { resolutionType: 'ConditionCleared', leftOpen: true, note: 'Closed - how it closed was not recorded' };
  }
  return { resolutionType: item.resolutionType, decidedOn: item.resolvedOn ?? null };
}

export interface BuildWizardItemArgs {
  item: WorklistRouteItem;
  /** The group the item came from (its core record and its sibling items). */
  group: WorklistGroupData;
  set: WorklistSetName;
  today?: Date;
}

export function buildWizardItem({ item, group, set, today = new Date() }: BuildWizardItemArgs): DecisionWizardItem {
  const open = set === 'lanes';
  return {
    row: item,
    core: group.core,
    // The route returns no rule sentence (sprk_sentence). The wizard omits its quote block when this is null.
    sentence: null,
    evidence: parseEvidenceRefs(item.evidenceRefs),
    // Other open items on the same core record in the same lane. A Not filed item has none (D-34/35).
    otherOpenItems: open && group.core ? group.items.filter(i => i.signalId !== item.signalId) : [],
    resolution: resolutionFor(item, set, today),
  };
}
