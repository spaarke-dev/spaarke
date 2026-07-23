/**
 * handoffSeedMapping.ts — Assistant hand-off seed → CreateMatterWizard form state.
 *
 * spaarkeai-assistant-enhancements-r1 task 013 (part 2, "smart pre-seed"). The 012
 * hand-off transport (`useWizardPageBootstrap.handoffSeed`) exposes a typed but
 * GENERIC seed (`draftValues` + `resolvedLookups`); this module is the PER-WIZARD
 * field-name translation the 012 modules explicitly deferred here (see
 * `surfaceHandoff/readHandoff.ts` `handoffSeed` JSDoc).
 *
 * Two layers:
 *  - `draftValues` (free-text the LLM drafted — name / description) → the wizard's
 *    `matterName` / `summary`. Tolerant of several key spellings because the drafted
 *    key names are JPS-authored server-side (task 013 part 3 owns the final schema)
 *    and may arrive snake_case (`matter_name`), camelCase (`matterName`), or as a
 *    Dataverse logical name (`sprk_mattername` / `sprk_name`). Unknown keys are
 *    ignored (never dumped into a form field).
 *  - `resolvedLookups` (the constrained-field resolver's closed-set output — task
 *    010) → the matter-type / practice-area dropdowns. A dropdown is pre-selected
 *    ONLY when the resolver reported `confidence === 'high'` and a `recordId` is
 *    present; low/none confidence leaves the picker at its default (the user picks),
 *    per the task 013 pre-select rule + ADR-039 (the LLM never authors a closed-set
 *    final value; the deterministic resolver does).
 *
 * Pure + host-agnostic (ADR-012) — no Xrm, no React. Unit-tested in
 * `__tests__/handoffSeedMapping.test.ts`.
 */

import type { ResolvedLookup } from '../../services/surfaceHandoff';
import type { HandoffSeed } from '../../services/surfaceHandoff';
import type { ICreateMatterFormState } from './formTypes';

/** First non-empty string among the candidate keys of a record (tolerant read). */
function firstString(source: Record<string, unknown>, keys: readonly string[]): string | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim().length > 0) return value;
  }
  return undefined;
}

/** Draft-value key aliases for the matter NAME free-text field. */
const MATTER_NAME_KEYS = ['matterName', 'matter_name', 'sprk_mattername', 'sprk_name', 'name', 'title'] as const;

/** Draft-value key aliases for the matter DESCRIPTION / summary free-text field. */
const MATTER_SUMMARY_KEYS = [
  'summary',
  'matterDescription',
  'matter_description',
  'sprk_matterdescription',
  'sprk_description',
  'description',
] as const;

/** resolvedLookups key aliases for the matter-type closed-set lookup. */
const MATTER_TYPE_LOOKUP_KEYS = ['sprk_mattertype_ref', 'matterType', 'matter_type', 'matterTypeId'] as const;

/** resolvedLookups key aliases for the practice-area closed-set lookup. */
const PRACTICE_AREA_LOOKUP_KEYS = ['sprk_practicearea_ref', 'practiceArea', 'practice_area', 'practiceAreaId'] as const;

/** Read the first resolvedLookups entry present under any alias key. */
function firstLookup(lookups: Record<string, ResolvedLookup>, keys: readonly string[]): ResolvedLookup | undefined {
  for (const key of keys) {
    const value = lookups[key];
    if (value && typeof value === 'object') return value;
  }
  return undefined;
}

/**
 * Resolve a high-confidence lookup to an `{ id, name }` pre-select, or `undefined`
 * when the resolver was not confident (or produced no record id). The display name
 * is taken from the matching candidate's label when the resolver supplied one; the
 * id alone is still set otherwise so the wizard binds the correct record.
 */
function highConfidenceSelection(lookup: ResolvedLookup | undefined): { id: string; name: string } | undefined {
  if (!lookup || lookup.confidence !== 'high' || !lookup.recordId) return undefined;
  const match = lookup.candidates?.find(c => c.recordId === lookup.recordId);
  return { id: lookup.recordId, name: match?.label ?? '' };
}

/**
 * Map a hand-off seed onto the subset of {@link ICreateMatterFormState} the wizard
 * can pre-fill. Returns `undefined` when there is nothing to seed (so the caller can
 * pass `initialFormValues={undefined}` and get the wizard's empty defaults).
 */
export function mapMatterHandoffSeed(
  seed: HandoffSeed | null | undefined
): Partial<ICreateMatterFormState> | undefined {
  if (!seed) return undefined;

  const draft = seed.draftValues ?? {};
  const lookups = seed.resolvedLookups ?? {};
  const out: Partial<ICreateMatterFormState> = {};

  const matterName = firstString(draft, MATTER_NAME_KEYS);
  if (matterName !== undefined) out.matterName = matterName;

  const summary = firstString(draft, MATTER_SUMMARY_KEYS);
  if (summary !== undefined) out.summary = summary;

  const matterType = highConfidenceSelection(firstLookup(lookups, MATTER_TYPE_LOOKUP_KEYS));
  if (matterType) {
    out.matterTypeId = matterType.id;
    out.matterTypeName = matterType.name;
  }

  const practiceArea = highConfidenceSelection(firstLookup(lookups, PRACTICE_AREA_LOOKUP_KEYS));
  if (practiceArea) {
    out.practiceAreaId = practiceArea.id;
    out.practiceAreaName = practiceArea.name;
  }

  return Object.keys(out).length > 0 ? out : undefined;
}
