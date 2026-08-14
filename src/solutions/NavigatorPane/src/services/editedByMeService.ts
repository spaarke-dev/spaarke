/**
 * editedByMeService — Recent (Edited) derivation for the Navigator's
 * Viewed/Edited toggle (spaarke-side-pane-navigation-history-r1 task 042,
 * spec FR-04, OQ-5 RESOLVED).
 *
 * OQ-5 resolution (spec.md "Owner Clarifications"): "edited by me" is derived
 * via **N per-entity `Xrm.WebApi` queries over a fixed core set**
 * (`sprk_matter`, `sprk_project`, `sprk_document`, `sprk_todo`, `sprk_event`,
 * `sprk_communication`), filtered `_modifiedby_value eq {currentUserId}`,
 * ordered `modifiedon desc`, merged and re-sorted client-side. There is no
 * dependency on Dataverse relevance search.
 *
 * R2 (task-001 spike, `notes/task-001-spike-report.md`) is RESOLVED and
 * re-confirmed live against spaarkedev1 for this task (see
 * `notes/task-042-*.md`): OData has no literal `@me` — the filter MUST use
 * the GUID from `Xrm.Utility.getGlobalContext().userSettings.userId`
 * interpolated directly into `_modifiedby_value eq {userId}` (no braces
 * added/stripped — the same convention `navItemRepository.ts`'s
 * `_ownerid_value eq {ownerId}` filters already use). No audit entity, no
 * OnSave hook, no plugin — this module issues only `retrieveMultipleRecords`
 * calls against the six core entity sets.
 *
 * Display-name resolution mirrors `navigatorCaptureService.ts`'s
 * `resolveDisplayName` pattern (`Xrm.Utility.getEntityMetadata` ->
 * `PrimaryNameAttribute`) rather than a hardcoded per-entity field map like
 * `useSprkMemoRepository.ts`'s `PARENT_PRIMARY_NAME_FIELD`: the six core
 * entities do NOT share one label-field convention (`sprk_matter`'s primary
 * name attribute is `sprk_matternumber`, not a human title field), and
 * metadata discovery is the same generalization already established in this
 * project for "resolve a display name for an entity I only know the logical
 * name of at runtime" (CLAUDE.md §11 — reusing the established pattern
 * instead of inventing a second hardcoded map).
 *
 * Negative case (task acceptance criterion): an entity in the core set that
 * errors (missing, no privilege, metadata lookup failure) or returns zero
 * rows contributes NOTHING to the merge and never surfaces an error to the
 * caller — each per-entity fetch is independently try/caught.
 *
 * @see RecentTab.tsx — renders the Viewed/Edited segmented toggle; Edited
 *      mode calls {@link listEditedByMe}.
 * @see src/client/shared/Spaarke.UI.Components/src/services/navigator/navigatorCaptureService.ts
 *      — `resolveDisplayName` (the pattern this module's
 *      `resolvePrimaryNameField` follows).
 * @see src/client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts
 *      — `_ownerid_value eq {ownerId}` filter convention this module mirrors
 *      for `_modifiedby_value eq {userId}`.
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-001-spike-report.md
 *      — R2 resolution (no `@me` literal; GUID interpolation confirmed live).
 * @see projects/spaarke-side-pane-navigation-history-r1/spec.md FR-04 / OQ-5.
 */

import { getXrm, type XrmContext, type XrmUtility } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Fixed core entity set (OQ-5) — validated live against spaarkedev1
// EntityDefinitions during task 042 execution (all six confirmed present;
// none dropped). See notes/task-042-edited-by-me-deviations.md.
// ---------------------------------------------------------------------------

export const CORE_ENTITY_SET = [
  'sprk_matter',
  'sprk_project',
  'sprk_document',
  'sprk_todo',
  'sprk_event',
  'sprk_communication',
] as const;

export type CoreEntityLogicalName = (typeof CORE_ENTITY_SET)[number];

/** Per-entity `$top` for the Edited query. Exported so tests avoid a magic number. */
export const EDITED_BY_ME_TOP_PER_ENTITY = 10;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** One merged "edited by me" row — always an `EntityRecord`-shaped target (a direct entity-table query, never a view/list/link). */
export interface EditedByMeItem {
  targetLogicalName: string;
  targetId: string;
  displayName: string;
  /** ISO datetime string — the record's `modifiedon`. */
  modifiedOn: string;
}

export interface ListEditedByMeOptions {
  /** Per-entity result cap. Defaults to {@link EDITED_BY_ME_TOP_PER_ENTITY}. */
  topPerEntity?: number;
  /** Override the core entity set (tests only — production callers use the default). */
  entities?: readonly string[];
}

// `xrmContext.ts`'s `XrmUtility` (task 010) does not declare
// `getEntityMetadata` — narrowed locally and cast at the boundary, mirroring
// `navigatorCaptureService.ts`'s identical pattern rather than widening the
// shared surface for one more caller (ADR-022 "keep the shared surface slim").
interface XrmUtilityWithEntityMetadata {
  getEntityMetadata?: (
    entityLogicalName: string,
    attributes?: string[]
  ) => Promise<{ PrimaryNameAttribute?: string }>;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** "sprk_matter" -> "Matter". Same fallback shape as `navigatorCaptureService.ts`'s `formatEntityFallbackLabel`. */
function formatEntityFallbackLabel(entityLogicalName: string): string {
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

/**
 * Resolve an entity's primary-name attribute via `Xrm.Utility.getEntityMetadata`.
 * Never throws — a missing/failed lookup returns `null`, and the caller falls
 * back to {@link formatEntityFallbackLabel}.
 */
async function resolvePrimaryNameField(xrm: XrmContext, entityLogicalName: string): Promise<string | null> {
  const utility = xrm.Utility as (XrmUtility & XrmUtilityWithEntityMetadata) | undefined;
  if (!utility?.getEntityMetadata) return null;
  try {
    const meta = await utility.getEntityMetadata(entityLogicalName);
    return meta?.PrimaryNameAttribute ?? null;
  } catch {
    return null;
  }
}

/**
 * Fetch one core-set entity's `modifiedby=me` rows. Independently try/caught
 * (task acceptance criterion — negative case): any failure (entity missing
 * from this environment, no privilege, metadata lookup failure, network
 * error) resolves to an empty array rather than rejecting, so one bad entity
 * never blocks the other five.
 */
async function listEditedForEntity(
  xrm: XrmContext,
  entityLogicalName: string,
  userId: string,
  topPerEntity: number
): Promise<EditedByMeItem[]> {
  if (!xrm.WebApi) return [];

  try {
    const idField = `${entityLogicalName}id`;
    const primaryNameField = await resolvePrimaryNameField(xrm, entityLogicalName);
    const selectFields = [idField, 'modifiedon', ...(primaryNameField ? [primaryNameField] : [])];
    const query =
      `?$select=${selectFields.join(',')}` +
      `&$filter=_modifiedby_value eq ${userId}` +
      `&$orderby=modifiedon desc` +
      `&$top=${topPerEntity}`;

    const result = await xrm.WebApi.retrieveMultipleRecords(entityLogicalName, query);
    const entities = (result.entities ?? []) as Array<Record<string, unknown>>;

    const items: EditedByMeItem[] = [];
    for (const record of entities) {
      const targetId = record[idField];
      const modifiedOn = record.modifiedon;
      if (typeof targetId !== 'string' || typeof modifiedOn !== 'string') continue; // malformed row — skip, don't throw
      const rawName = primaryNameField ? record[primaryNameField] : undefined;
      const displayName =
        typeof rawName === 'string' && rawName.length > 0 ? rawName : formatEntityFallbackLabel(entityLogicalName);
      items.push({ targetLogicalName: entityLogicalName, targetId, displayName, modifiedOn });
    }
    return items;
  } catch {
    // Entity not present in this environment, no privilege, or any other
    // WebApi failure — contributes nothing to the merge, no error surfaced.
    return [];
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * List the current user's recently-EDITED records across the fixed core
 * entity set (FR-04 / OQ-5), merged and sorted `modifiedon desc`. A record
 * modified by a flow (not the UI) still appears — the derivation is purely
 * `modifiedon`-based, with no dependency on the Navigator's own capture
 * history (`sprk_navitem`) or the `audit` entity.
 *
 * Never throws: when `Xrm`/`Xrm.WebApi`/the current user id is unavailable,
 * or every per-entity fetch fails, resolves to `[]`.
 */
export async function listEditedByMe(options: ListEditedByMeOptions = {}): Promise<EditedByMeItem[]> {
  const xrm = getXrm();
  const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
  if (!xrm?.WebApi || !ownerId) return [];

  const entities = options.entities ?? CORE_ENTITY_SET;
  const topPerEntity = options.topPerEntity ?? EDITED_BY_ME_TOP_PER_ENTITY;

  const perEntityResults = await Promise.all(
    entities.map(entity => listEditedForEntity(xrm, entity, ownerId, topPerEntity))
  );

  const merged = perEntityResults.flat();
  merged.sort((a, b) => new Date(b.modifiedOn).getTime() - new Date(a.modifiedOn).getTime());
  return merged;
}
