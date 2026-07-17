/**
 * handoffSeedMapping.ts — Assistant hand-off seed → CreateEventWizard form state.
 *
 * spaarkeai-assistant-enhancements-r1 task 013 (part 2, "smart pre-seed") — the
 * CreateEventWizard counterpart of CreateMatterWizard's `handoffSeedMapping`. Maps
 * the generic 012 hand-off seed (`useWizardPageBootstrap.handoffSeed`) onto the
 * Event wizard's initial form values so a `create-task` launch (task 002 registry
 * entry → `sprk_createeventwizard`) opens PRE-FILLED with the drafted event name /
 * description.
 *
 *  - `draftValues` free-text (name / description) → `eventName` / `description`.
 *    Tolerant of snake_case / camelCase / logical-name key spellings (the drafted
 *    key names are JPS-authored server-side; task 013 part 3 owns the final schema).
 *  - `resolvedLookups` high-confidence event-type → the event-type dropdown (ONLY
 *    when `confidence === 'high'`; ADR-039 — the deterministic resolver, not the
 *    LLM, authors closed-set values).
 *
 * NOTE (part-3 gap): the `create-task` registry preset injects the Task-subtype
 * event-type GUID into `draftValues` under `sprk_eventtype_ref` (a bare GUID, not a
 * ResolvedLookup — see `surfaceLaunchRegistry.ts`). It is intentionally NOT mapped
 * here because the subtype's DISPLAY NAME is unknown client-side; binding an id
 * without a name would leave the dropdown label blank. Resolving the preset GUID to
 * its display name (or having the wizard resolve it on mount) is server-enrichment /
 * follow-up scope.
 *
 * Pure + host-agnostic (ADR-012). Unit-tested in `__tests__/handoffSeedMapping.test.ts`.
 */

import type { ResolvedLookup, HandoffSeed } from '../../services/surfaceHandoff';
import type { ICreateEventFormState } from './formTypes';

function firstString(source: Record<string, unknown>, keys: readonly string[]): string | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim().length > 0) return value;
  }
  return undefined;
}

const EVENT_NAME_KEYS = [
  'eventName',
  'event_name',
  'sprk_eventname',
  'taskName',
  'task_name',
  'name',
  'title',
] as const;

const EVENT_DESCRIPTION_KEYS = [
  'description',
  'eventDescription',
  'event_description',
  'taskDescription',
  'task_description',
  'sprk_description',
  'summary',
] as const;

const EVENT_TYPE_LOOKUP_KEYS = ['sprk_eventtype_ref', 'eventType', 'event_type', 'eventTypeId'] as const;

function firstLookup(lookups: Record<string, ResolvedLookup>, keys: readonly string[]): ResolvedLookup | undefined {
  for (const key of keys) {
    const value = lookups[key];
    if (value && typeof value === 'object') return value;
  }
  return undefined;
}

function highConfidenceSelection(lookup: ResolvedLookup | undefined): { id: string; name: string } | undefined {
  if (!lookup || lookup.confidence !== 'high' || !lookup.recordId) return undefined;
  const match = lookup.candidates?.find(c => c.recordId === lookup.recordId);
  return { id: lookup.recordId, name: match?.label ?? '' };
}

/**
 * Map a hand-off seed onto the subset of {@link ICreateEventFormState} the wizard
 * can pre-fill. Returns `undefined` when there is nothing to seed.
 */
export function mapEventHandoffSeed(seed: HandoffSeed | null | undefined): Partial<ICreateEventFormState> | undefined {
  if (!seed) return undefined;

  const draft = seed.draftValues ?? {};
  const lookups = seed.resolvedLookups ?? {};
  const out: Partial<ICreateEventFormState> = {};

  const eventName = firstString(draft, EVENT_NAME_KEYS);
  if (eventName !== undefined) out.eventName = eventName;

  const description = firstString(draft, EVENT_DESCRIPTION_KEYS);
  if (description !== undefined) out.description = description;

  const eventType = highConfidenceSelection(firstLookup(lookups, EVENT_TYPE_LOOKUP_KEYS));
  if (eventType) {
    out.eventTypeId = eventType.id;
    out.eventTypeName = eventType.name;
  }

  return Object.keys(out).length > 0 ? out : undefined;
}
