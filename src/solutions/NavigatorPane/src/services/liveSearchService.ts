/**
 * liveSearchService — `QuickSwitcher.tsx`'s live Dataverse escalation
 * (spaarke-side-pane-navigation-history-r1 task 070, spec FR-11).
 *
 * Fires ONLY when the local fuzzy-match over `navigatorSearchIndex.ts`'s
 * already-loaded/already-trimmed Recent+Pinned+Views entries finds nothing
 * (project HARD constraint — local-first, escalate-on-miss). Host-context
 * only: `Xrm.WebApi` under the signed-in user for records, `ViewService` for
 * views. No BFF, no plugin, no per-form web resource.
 *
 * Records: reuses `editedByMeService.ts`'s exported `CORE_ENTITY_SET` (the
 * fixed core entity list already established for this project's other
 * per-entity-query features — root CLAUDE.md §11 "extend over fork", not a
 * second hardcoded entity list) with a `contains(<primaryNameField>,
 * '<query>')` filter per entity. Primary-name-field resolution mirrors
 * `editedByMeService.ts`/`monitoredService.ts`'s identical
 * `getEntityMetadata` -> `PrimaryNameAttribute` pattern (kept as a small
 * local duplicate per those same files' own precedent rather than a shared
 * export for one more caller).
 *
 * Views: `ViewService.getAllUserQueries()` — the SAME call `ViewsTab.tsx`
 * makes — filtered client-side by name substring. `ViewService` has no
 * built-in name-search parameter; adding one for this single caller would be
 * premature extension (root CLAUDE.md §11) over a client-side filter on the
 * already-small personal-userquery result set.
 *
 * Security trim (task 080): a record surfaced here is NEVER re-checked by
 * `securityTrimService.ts` — it does not need to be. This is a LIVE query
 * against the entity itself (not a cached label), so Dataverse row-level
 * security means an inaccessible record is never returned in the first
 * place. Identical reasoning to `RecentTab.tsx`'s Edited-tab exemption and
 * `PinnedTab.tsx`'s Monitored-group exemption (see `securityTrimService.ts`
 * module docblock "Exemptions").
 *
 * @see RecentTab.tsx / PinnedTab.tsx / ViewsTab.tsx / navigatorSearchIndex.ts — the local-first path this escalates FROM
 * @see editedByMeService.ts — `CORE_ENTITY_SET` + the metadata-resolution pattern this module mirrors
 * @see EventDetailSidePane/src/services/eventService.ts — the `Xrm.WebApi` retrieve pattern this project's live lookups model on
 */

import { ViewService, getXrm, type IViewDefinition, type XrmContext, type XrmUtility } from '@spaarke/ui-components';
import { CORE_ENTITY_SET } from './editedByMeService';
import type { SearchIndexEntry } from './navigatorSearchIndex';

/** Per-entity result cap for the live record search. Exported so tests avoid a magic number. */
export const LIVE_RECORD_TOP_PER_ENTITY = 5;
/** Overall cap on merged live record results shown to the user. */
export const LIVE_RECORD_RESULT_CAP = 8;
/** Overall cap on merged live view results shown to the user. */
export const LIVE_VIEW_RESULT_CAP = 5;

const KNOWN_ENTITY_CHIP_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_document: 'Document',
};

/** "sprk_project" -> "Project". Local duplicate — see module docblock. */
function formatEntityFallbackLabel(entityLogicalName: string): string {
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

function chipForEntity(entityLogicalName: string): string {
  return KNOWN_ENTITY_CHIP_LABELS[entityLogicalName] ?? formatEntityFallbackLabel(entityLogicalName);
}

// `xrmContext.ts`'s `XrmUtility` does not declare `getEntityMetadata` —
// narrowed locally and cast at the boundary, mirroring
// `editedByMeService.ts`/`monitoredService.ts`'s identical pattern.
interface XrmUtilityWithEntityMetadata {
  getEntityMetadata?: (
    entityLogicalName: string,
    attributes?: string[]
  ) => Promise<{ PrimaryNameAttribute?: string }>;
}

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
 * Prepares a user-typed search string for safe interpolation into a `contains()`
 * OData filter clause: first doubles any single quote (the OData STRING-LITERAL
 * escape, `'` -> `''` — prevents the literal from being closed early / filter
 * injection), then `encodeURIComponent`s the result (a self-review / code-review
 * fix, task 070) so a query containing URL-structural characters (`&`, `#`,
 * `%`, `+`) cannot corrupt the query string `Xrm.WebApi.retrieveMultipleRecords`'s
 * `options` parameter is appended into — e.g. an unencoded "Smith & Co" would
 * have its `&` read as an OData query-option separator, truncating `$filter`
 * and silently returning zero results for an otherwise-accessible record.
 * `Xrm.WebApi` decodes the percent-escaping server-side, so the OData syntax
 * (`contains(`, the field name, the surrounding quotes) is unaffected — only
 * the interpolated VALUE is encoded.
 */
function encodeODataStringLiteral(value: string): string {
  return encodeURIComponent(value.replace(/'/g, "''"));
}

/**
 * Live-search one entity's name field for `query` (a `contains()` filter).
 * Never throws — mirrors `editedByMeService.ts`'s per-entity "one bad entity
 * never blocks the others" contract.
 */
async function searchEntityRecords(
  xrm: XrmContext,
  entityLogicalName: string,
  query: string
): Promise<SearchIndexEntry[]> {
  if (!xrm.WebApi) return [];

  try {
    const idField = `${entityLogicalName}id`;
    const primaryNameField = await resolvePrimaryNameField(xrm, entityLogicalName);
    if (!primaryNameField) return []; // No name field to safely search — skip this entity.

    const filter = `contains(${primaryNameField}, '${encodeODataStringLiteral(query)}')`;
    const result = await xrm.WebApi.retrieveMultipleRecords(
      entityLogicalName,
      `?$select=${idField},${primaryNameField}&$filter=${filter}&$top=${LIVE_RECORD_TOP_PER_ENTITY}`
    );
    const entities = (result.entities ?? []) as Array<Record<string, unknown>>;

    const entries: SearchIndexEntry[] = [];
    for (const record of entities) {
      const id = record[idField];
      const name = record[primaryNameField];
      if (typeof id !== 'string' || typeof name !== 'string') continue; // malformed row — skip, don't throw
      entries.push({
        id: `live-record-${entityLogicalName}-${id}`,
        label: name,
        chipLabel: chipForEntity(entityLogicalName),
        target: { type: 'entityrecord', entityLogicalName, entityId: id },
      });
    }
    return entries;
  } catch {
    // Entity not present in this environment, no privilege, or any other
    // WebApi failure — contributes nothing to the merge, no error surfaced.
    return [];
  }
}

/**
 * Live-search Dataverse records across {@link CORE_ENTITY_SET} for `query`.
 * Never throws: resolves to `[]` when `Xrm`/`Xrm.WebApi` is unavailable, the
 * query is blank, or every per-entity fetch fails.
 */
export async function liveSearchRecords(
  query: string,
  entities: readonly string[] = CORE_ENTITY_SET
): Promise<SearchIndexEntry[]> {
  const trimmed = query.trim();
  if (!trimmed) return [];

  const xrm = getXrm();
  if (!xrm?.WebApi) return [];

  const perEntityResults = await Promise.all(entities.map(entity => searchEntityRecords(xrm, entity, trimmed)));
  return perEntityResults.flat().slice(0, LIVE_RECORD_RESULT_CAP);
}

/**
 * Live-search the signed-in user's personal views (`ViewService.getAllUserQueries()`)
 * for `query`, filtered client-side by name substring. Never throws.
 */
export async function liveSearchViews(query: string): Promise<SearchIndexEntry[]> {
  const trimmed = query.trim();
  if (!trimmed) return [];

  const xrm = getXrm();
  if (!xrm) return [];

  try {
    const viewService = new ViewService(xrm);
    const views = await viewService.getAllUserQueries();
    const needle = trimmed.toLowerCase();

    return views
      .filter((view: IViewDefinition) => view.name.toLowerCase().includes(needle))
      .slice(0, LIVE_VIEW_RESULT_CAP)
      .map((view: IViewDefinition): SearchIndexEntry => ({
        id: `live-view-${view.id}`,
        label: view.name,
        chipLabel: 'View',
        target: { type: 'entitylist', entityLogicalName: view.entityLogicalName, viewId: view.id },
      }));
  } catch {
    return [];
  }
}

/** Runs both live escalations concurrently and merges (records first, then views). */
export async function liveSearch(query: string): Promise<SearchIndexEntry[]> {
  const [records, views] = await Promise.all([liveSearchRecords(query), liveSearchViews(query)]);
  return [...records, ...views];
}
