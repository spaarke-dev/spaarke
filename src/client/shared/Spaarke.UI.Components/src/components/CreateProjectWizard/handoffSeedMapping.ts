/**
 * handoffSeedMapping.ts — Assistant hand-off seed → CreateProjectWizard form state.
 *
 * spaarkeai-assistant-enhancements-r1 UAT #1 (create-project parity, 2026-07-18).
 * The project mirror of CreateMatterWizard/handoffSeedMapping.ts: the 012 hand-off
 * transport (`readHandoffFromUrl` → `handoffSeed`) exposes a typed but GENERIC seed
 * (`draftValues` + `resolvedLookups`); this module is the PER-WIZARD field-name
 * translation onto {@link ICreateProjectFormState}.
 *
 * Two layers (identical rules to the matter mapper):
 *  - `draftValues` (free-text the LLM drafted — name / description) → the wizard's
 *    `projectName` / `description`. Tolerant of several key spellings because the
 *    drafted key names are JPS-authored server-side (CREATE-PROJECT@v1) and may
 *    arrive snake_case (`project_name`), camelCase (`projectName`), or as a Dataverse
 *    logical name (`sprk_name` / `sprk_projectdescription`). Unknown keys are ignored.
 *  - `resolvedLookups` (the constrained-field resolver's closed-set output) → the
 *    project-type / practice-area dropdowns. A dropdown is pre-selected ONLY when the
 *    resolver reported `confidence === 'high'` with a `recordId`; low/none confidence
 *    leaves the picker at its default (the user picks) — ADR-039 (the LLM never authors
 *    a closed-set final value; the deterministic resolver does).
 *
 * Pure + host-agnostic (ADR-012) — no Xrm, no React.
 */

import type { ResolvedLookup } from '../../services/surfaceHandoff';
import type { HandoffSeed } from '../../services/surfaceHandoff';
import type { ICreateProjectFormState } from './projectFormTypes';

/** First non-empty string among the candidate keys of a record (tolerant read). */
function firstString(source: Record<string, unknown>, keys: readonly string[]): string | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim().length > 0) return value;
  }
  return undefined;
}

/** Draft-value key aliases for the project NAME free-text field. */
const PROJECT_NAME_KEYS = ['projectName', 'project_name', 'sprk_projectname', 'sprk_name', 'name', 'title'] as const;

/** Draft-value key aliases for the project DESCRIPTION free-text field. */
const PROJECT_DESCRIPTION_KEYS = [
  'description',
  'projectDescription',
  'project_description',
  'sprk_projectdescription',
  'sprk_description',
  'summary',
] as const;

/** resolvedLookups key aliases for the project-type closed-set lookup. */
const PROJECT_TYPE_LOOKUP_KEYS = ['sprk_projecttype_ref', 'projectType', 'project_type', 'projectTypeId'] as const;

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
 * when the resolver was not confident (or produced no record id).
 */
function highConfidenceSelection(lookup: ResolvedLookup | undefined): { id: string; name: string } | undefined {
  if (!lookup || lookup.confidence !== 'high' || !lookup.recordId) return undefined;
  const match = lookup.candidates?.find(c => c.recordId === lookup.recordId);
  return { id: lookup.recordId, name: match?.label ?? '' };
}

/**
 * Map a hand-off seed onto the subset of {@link ICreateProjectFormState} the wizard
 * can pre-fill. Returns `undefined` when there is nothing to seed (so the caller can
 * pass `initialFormValues={undefined}` and get the wizard's empty defaults).
 */
export function mapProjectHandoffSeed(
  seed: HandoffSeed | null | undefined
): Partial<ICreateProjectFormState> | undefined {
  if (!seed) return undefined;

  const draft = seed.draftValues ?? {};
  const lookups = seed.resolvedLookups ?? {};
  const out: Partial<ICreateProjectFormState> = {};

  const projectName = firstString(draft, PROJECT_NAME_KEYS);
  if (projectName !== undefined) out.projectName = projectName;

  const description = firstString(draft, PROJECT_DESCRIPTION_KEYS);
  if (description !== undefined) out.description = description;

  const projectType = highConfidenceSelection(firstLookup(lookups, PROJECT_TYPE_LOOKUP_KEYS));
  if (projectType) {
    out.projectTypeId = projectType.id;
    out.projectTypeName = projectType.name;
  }

  const practiceArea = highConfidenceSelection(firstLookup(lookups, PRACTICE_AREA_LOOKUP_KEYS));
  if (practiceArea) {
    out.practiceAreaId = practiceArea.id;
    out.practiceAreaName = practiceArea.name;
  }

  return Object.keys(out).length > 0 ? out : undefined;
}
