/**
 * monitoredService — Monitored lens derivation for the Pinned tab's distinct
 * Monitored group (spaarke-side-pane-navigation-history-r1 task 052, spec
 * FR-09 / OQ-1b / design.md §6c).
 *
 * READ-ONLY over the existing shared, record-level `sprk_monitor` flag —
 * decoupled from the personal pin gesture (task 050's `pinService.ts`, which
 * never writes `sprk_monitor`). This module issues N per-entity
 * `Xrm.WebApi.retrieveMultipleRecords` queries (mirrors
 * `editedByMeService.ts`'s task-042 pattern) filtered `sprk_monitor eq true`,
 * merged and sorted client-side. No writes of any kind occur in this module.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * Schema discovery (task 052, live query against spaarkedev1 `EntityDefinitions`
 * — see `notes/task-052-monitored-scope-escalation.md`):
 * `sprk_monitor` (Boolean) exists on seven UserOwned entities — `sprk_matter`,
 * `sprk_project`, `sprk_document`, `sprk_todo`, `sprk_event`,
 * `sprk_workassignment`, `sprk_invoice` — this module's {@link MONITOR_ENTITY_SET}.
 * `sprk_communication` carries a DIFFERENT field, `sprk_ismonitored` (an
 * unrelated, still-placeholder email-tracking flag from a separate project —
 * see `EmailWorkspace.mapping.ts`'s `EMAIL_TRACKING_FIELDS`) and is
 * deliberately NOT queried here. `sprk_budget` has no monitor field at all.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * Scoping — ACCESS-BASED (revised post-UAT 2026-08-15; supersedes the r1
 * owner-only Path-A exception):
 * The r1 build filtered `_ownerid_value eq {userId}` to answer "monitored AND
 * mine." That breaks in production, where records are owned by the BUSINESS
 * UNIT (or a team), not the individual user — an owner-equality filter would
 * return nothing. The fix is NOT to build an "assigned-to-me" resolver or a
 * BFF membership service: a host-context `Xrm.WebApi.retrieveMultipleRecords`
 * is ALREADY security-trimmed by the Dataverse platform — a non-admin user
 * only ever gets rows they can read via ANY access path (owner, business unit,
 * team, or share). So we simply query `sprk_monitor eq true` with NO owner
 * clause and let Dataverse resolve membership server-side. This is the same
 * principle the read-time trim (`securityTrimService.ts`, task 080) relies on;
 * membership resolution is the PLATFORM's job, not a service we re-implement.
 * Net semantics: "monitored records I can see" (owner ∪ BU ∪ team ∪ shared) —
 * production-correct, host-context only, zero BFF. If a future requirement
 * needs a NARROWER scope (e.g. strictly my-BU, excluding team/share), that is
 * an additional explicit filter layered on top — not a return to owner-only.
 *
 * @see PinnedTab.tsx (task 052) — renders the Monitored group, calls {@link listMonitoredByMe}
 * @see editedByMeService.ts (task 042) — the N-per-entity-query + merge pattern this module mirrors
 * @see pinService.ts (task 050) — the SEPARATE personal-pin path; never merged with this module's output
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-052-monitored-scope-escalation.md
 * @see projects/spaarke-side-pane-navigation-history-r1/spec.md FR-09, "Owner Clarifications" OQ-1b
 * @see projects/spaarke-side-pane-navigation-history-r1/design.md §6c
 */

import { getXrm, type XrmContext, type XrmUtility } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Monitor-bearing entity set — live-verified against spaarkedev1 (task 052).
// See module docblock "Schema discovery".
// ---------------------------------------------------------------------------

export const MONITOR_ENTITY_SET = [
  'sprk_matter',
  'sprk_project',
  'sprk_document',
  'sprk_todo',
  'sprk_event',
  'sprk_workassignment',
  'sprk_invoice',
] as const;

export type MonitorEntityLogicalName = (typeof MONITOR_ENTITY_SET)[number];

/** Per-entity `$top` for the Monitored query. Exported so tests avoid a magic number. */
export const MONITORED_TOP_PER_ENTITY = 10;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** One merged "monitored by me" row — always an `EntityRecord`-shaped target (a direct entity-table query). */
export interface MonitoredItem {
  targetLogicalName: string;
  targetId: string;
  displayName: string;
}

export interface ListMonitoredByMeOptions {
  /** Per-entity result cap. Defaults to {@link MONITORED_TOP_PER_ENTITY}. */
  topPerEntity?: number;
  /** Override the monitor entity set (tests only — production callers use the default). */
  entities?: readonly string[];
}

// `xrmContext.ts`'s `XrmUtility` (task 010) does not declare
// `getEntityMetadata` — narrowed locally and cast at the boundary, mirroring
// `editedByMeService.ts`'s identical pattern (ADR-022 "keep the shared surface slim").
interface XrmUtilityWithEntityMetadata {
  getEntityMetadata?: (
    entityLogicalName: string,
    attributes?: string[]
  ) => Promise<{ PrimaryNameAttribute?: string }>;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** "sprk_workassignment" -> "Workassignment". Mirrors `editedByMeService.ts`'s `formatEntityFallbackLabel`. */
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
 * Fetch one monitor-entity-set entity's `sprk_monitor=true` rows. The query
 * carries NO owner clause — Dataverse security-trims the result to rows this
 * user can read (owner ∪ BU ∪ team ∪ shared), so membership scoping is done
 * server-side by the platform (see module docblock "Scoping"). Independently
 * try/caught (mirrors `editedByMeService.ts`'s negative case): any failure
 * (entity missing from this environment, no privilege, metadata lookup
 * failure, network error) resolves to an empty array rather than rejecting, so
 * one bad entity never blocks the others.
 */
async function listMonitoredForEntity(
  xrm: XrmContext,
  entityLogicalName: string,
  topPerEntity: number
): Promise<MonitoredItem[]> {
  if (!xrm.WebApi) return [];

  try {
    const idField = `${entityLogicalName}id`;
    const primaryNameField = await resolvePrimaryNameField(xrm, entityLogicalName);
    const selectFields = [idField, ...(primaryNameField ? [primaryNameField] : [])];
    const query =
      `?$select=${selectFields.join(',')}` +
      `&$filter=sprk_monitor eq true` +
      `&$top=${topPerEntity}`;

    const result = await xrm.WebApi.retrieveMultipleRecords(entityLogicalName, query);
    const entities = (result.entities ?? []) as Array<Record<string, unknown>>;

    const items: MonitoredItem[] = [];
    for (const record of entities) {
      const targetId = record[idField];
      if (typeof targetId !== 'string') continue; // malformed row — skip, don't throw
      const rawName = primaryNameField ? record[primaryNameField] : undefined;
      const displayName =
        typeof rawName === 'string' && rawName.length > 0 ? rawName : formatEntityFallbackLabel(entityLogicalName);
      items.push({ targetLogicalName: entityLogicalName, targetId, displayName });
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
 * List the `sprk_monitor=true` records the current user can access across
 * {@link MONITOR_ENTITY_SET}, merged and sorted by display name. Scope is
 * ACCESS-BASED (owner ∪ BU ∪ team ∪ shared), resolved server-side by Dataverse
 * security trimming — see module docblock "Scoping".
 *
 * Read-only: issues `retrieveMultipleRecords` calls only. Never writes
 * `sprk_monitor` or any other field — that flag is toggled on the record's
 * own form (`TrackingFieldTrio`), never from this Navigator surface.
 *
 * Never throws: when `Xrm`/`Xrm.WebApi`/the current user id is unavailable,
 * or every per-entity fetch fails, resolves to `[]`. The current-user id is
 * still required as a signed-in-session gate (a pane with no user context
 * shows nothing) even though it no longer appears in the query filter.
 */
export async function listMonitoredByMe(options: ListMonitoredByMeOptions = {}): Promise<MonitoredItem[]> {
  const xrm = getXrm();
  const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
  if (!xrm?.WebApi || !ownerId) return [];

  const entities = options.entities ?? MONITOR_ENTITY_SET;
  const topPerEntity = options.topPerEntity ?? MONITORED_TOP_PER_ENTITY;

  const perEntityResults = await Promise.all(
    entities.map(entity => listMonitoredForEntity(xrm, entity, topPerEntity))
  );

  const merged = perEntityResults.flat();
  merged.sort((a, b) => a.displayName.localeCompare(b.displayName));
  return merged;
}
