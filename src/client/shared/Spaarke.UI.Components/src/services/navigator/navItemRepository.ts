/**
 * navItemRepository — `Xrm.WebApi` CRUD for the per-user `sprk_navitem`
 * entity (spaarke-side-pane-navigation-history-r1 task 030, spec FR-03/FR-06;
 * `listHistoryItems`/`listPinItems`/`createPinItem` added task 041 — Recent
 * (Viewed) tab read + minimal promote-to-pin, extended by 050/052 rather than
 * introducing a second read/write path).
 *
 * Mirrors `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts`'s
 * `Xrm.WebApi` CRUD shape (`retrieveMultipleRecords` / `createRecord` /
 * `updateRecord`, raw Client API — NOT the `retrieveMultiple` name used by
 * some repo wrapper types) and
 * `src/solutions/EventDetailSidePane/src/services/eventService.ts`'s
 * error-parse pattern, but resolves `Xrm` via the shared-lib's widened
 * `getXrm()` frame-walk (`../../utils/xrmContext.ts`, task 010: 3-frame
 * window -> parent -> top, never throws) instead of a locally duplicated
 * frame-walk.
 *
 * Per the task-001 spike lesson ("NEVER cache the Xrm reference — re-acquire
 * every poll"): every exported function here calls `getXrm()` itself rather
 * than accepting a cached `Xrm` instance from the caller, so a long-lived
 * caller (the task-030 capture poller) can never hand this module a stale
 * reference.
 *
 * @see src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts
 * @see src/solutions/EventDetailSidePane/src/services/eventService.ts
 * @see src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-001-spike-report.md
 *
 * `findPinItem`/`deleteNavItem` added task 050 (full pin gesture — pinService.ts
 * dedupe-before-create + unstar/delete) — extends this module rather than
 * introducing a second sprk_navitem read/write path, per the file's own
 * "task 050/052 own the full pin gesture ... may extend this helper" note.
 */

import { getXrm } from '../../utils/xrmContext';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ENTITY = 'sprk_navitem';

/** `sprk_type` global option-set values (deployed spaarkedev1 — task 021). */
export const NavItemType = {
  History: 100000000,
  Pin: 100000001,
} as const;

/** `sprk_source` global option-set values (deployed spaarkedev1 — task 021). */
export const NavItemSource = {
  Captured: 100000000,
  Manual: 100000001,
} as const;

/** `sprk_pagetype` global option-set values (deployed spaarkedev1 — task 021). */
export const NavItemPageType = {
  EntityRecord: 100000000,
  EntityList: 100000001,
  Custom: 100000002,
  WebLink: 100000003,
} as const;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface NavItemRecord {
  sprk_navitemid: string;
  sprk_type: number;
  sprk_source: number;
  sprk_targetlogicalname: string | null;
  sprk_targetid: string | null;
  sprk_pagetype: number;
  sprk_url: string | null;
  sprk_displayname: string;
  sprk_lastvisited: string;
  sprk_visitcount: number;
}

export interface CreateHistoryItemInput {
  targetLogicalName: string;
  targetId: string;
  /** One of {@link NavItemPageType}. */
  pageType: number;
  displayName: string;
  /** Defaults to `new Date()`. Exposed for deterministic tests. */
  visitedAt?: Date;
}

/**
 * Input for {@link createPinItem}. Deliberately mirrors {@link CreateHistoryItemInput}'s
 * shape (same target/pageType/displayName triple) rather than introducing a divergent
 * pin-specific contract — task 050 (full pin gesture: toggle/un-pin/pin-from-page) builds
 * on this same shape.
 */
export interface CreatePinItemInput {
  targetLogicalName: string;
  targetId: string;
  /** One of {@link NavItemPageType}. */
  pageType: number;
  displayName: string;
}

/**
 * Thrown by every repository function on any `Xrm.WebApi` failure (including
 * "Xrm not available"), with a user-friendly `message` produced by
 * {@link parseWebApiError}. Callers (the capture poller) catch this and treat
 * it as a non-fatal, per-tick failure — capture continues on the next poll.
 */
export class NavItemRepositoryError extends Error {
  public readonly cause?: unknown;

  constructor(message: string, cause?: unknown) {
    super(message);
    this.name = 'NavItemRepositoryError';
    this.cause = cause;
  }
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/**
 * Normalize a Dataverse GUID: strip braces, lowercase. Mirrors
 * `contextService.ts`'s `normalizeGuid` (retired reference impl this task
 * re-adopts) — kept local rather than extracted to a shared util because it
 * is a two-line pure function (CLAUDE.md §11: extension is earned by a real
 * second consumer, not anticipated).
 */
export function normalizeGuid(id: string): string {
  return id.replace(/[{}]/g, '').toLowerCase();
}

/** Escape a single-quote for safe interpolation into an OData string literal. */
function escapeODataString(value: string): string {
  return value.replace(/'/g, "''");
}

/** Mirrors `eventService.ts`'s `parseWebApiError` — user-friendly Dataverse error messages. */
function parseWebApiError(error: unknown): string {
  if (error instanceof Error) {
    const message = error.message;
    if (message.includes('privilege')) {
      return 'You do not have permission to access navigation history.';
    }
    if (message.includes('does not exist') || message.includes('could not be found')) {
      return 'The navigation history record no longer exists.';
    }
    if (message.includes('network') || message.includes('fetch')) {
      return 'Network error while syncing navigation history.';
    }
    return message;
  }
  return 'An unexpected error occurred while syncing navigation history.';
}

function requireWebApi() {
  const xrm = getXrm();
  if (!xrm?.WebApi) {
    throw new NavItemRepositoryError('Xrm.WebApi is not available in this environment.');
  }
  return xrm.WebApi;
}

// ---------------------------------------------------------------------------
// Repository
// ---------------------------------------------------------------------------

const HISTORY_SELECT =
  'sprk_navitemid,sprk_type,sprk_source,sprk_targetlogicalname,sprk_targetid,sprk_pagetype,sprk_url,sprk_displayname,sprk_lastvisited,sprk_visitcount';

/**
 * Find the current user's existing `history` `sprk_navitem` for a given
 * target, if one exists. Used to dedupe on capture (FR-03): a target already
 * seen is bumped via {@link bumpHistoryItem} instead of re-created.
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 * @param targetLogicalName - e.g. `sprk_matter`.
 * @param targetId - Target record GUID (normalized internally).
 */
export async function findHistoryItem(
  ownerId: string,
  targetLogicalName: string,
  targetId: string
): Promise<NavItemRecord | null> {
  const webApi = requireWebApi();
  const normalizedTargetId = normalizeGuid(targetId);
  const filter =
    `_ownerid_value eq ${ownerId} and sprk_type eq ${NavItemType.History}` +
    ` and sprk_targetlogicalname eq '${escapeODataString(targetLogicalName)}'` +
    ` and sprk_targetid eq '${escapeODataString(normalizedTargetId)}'`;
  const query = `?$select=${HISTORY_SELECT}&$filter=${filter}&$top=1`;

  try {
    const result = await webApi.retrieveMultipleRecords(ENTITY, query);
    const entities = (result.entities ?? []) as unknown as NavItemRecord[];
    return entities[0] ?? null;
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * Create a new `history` `sprk_navitem` for a first-sighted target
 * (`sprk_source=Captured`, `sprk_visitcount=1`).
 */
export async function createHistoryItem(input: CreateHistoryItemInput): Promise<{ id: string }> {
  const webApi = requireWebApi();
  const now = (input.visitedAt ?? new Date()).toISOString();
  const data: Record<string, unknown> = {
    sprk_type: NavItemType.History,
    sprk_source: NavItemSource.Captured,
    sprk_targetlogicalname: input.targetLogicalName,
    sprk_targetid: normalizeGuid(input.targetId),
    sprk_pagetype: input.pageType,
    sprk_displayname: input.displayName,
    sprk_lastvisited: now,
    sprk_visitcount: 1,
  };

  try {
    const created = await webApi.createRecord(ENTITY, data);
    return { id: created.id };
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * Bump an existing `history` row's `sprk_lastvisited` / `sprk_visitcount` on
 * re-visit (FR-03 dedupe) rather than creating a duplicate row.
 */
export async function bumpHistoryItem(
  navItemId: string,
  nextVisitCount: number,
  visitedAt: Date = new Date()
): Promise<void> {
  const webApi = requireWebApi();
  try {
    await webApi.updateRecord(ENTITY, navItemId, {
      sprk_lastvisited: visitedAt.toISOString(),
      sprk_visitcount: nextVisitCount,
    });
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * List the current user's `history` `sprk_navitem` rows, newest-first by
 * `sprk_lastvisited` (task 041, spec FR-03 UI). Owner-scoped — mirrors
 * {@link findHistoryItem}'s `_ownerid_value eq {ownerId}` filter.
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 */
export async function listHistoryItems(ownerId: string): Promise<NavItemRecord[]> {
  const webApi = requireWebApi();
  const filter = `_ownerid_value eq ${ownerId} and sprk_type eq ${NavItemType.History}`;
  const query = `?$select=${HISTORY_SELECT}&$filter=${filter}&$orderby=sprk_lastvisited desc`;

  try {
    const result = await webApi.retrieveMultipleRecords(ENTITY, query);
    return (result.entities ?? []) as unknown as NavItemRecord[];
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * List the current user's `pin` `sprk_navitem` rows (task 041 minimal read —
 * used to hydrate a row's pinned-state; task 050/052 own the full pin-gesture
 * read/write surface and may extend this rather than duplicating a second
 * pin-list query).
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 */
export async function listPinItems(ownerId: string): Promise<NavItemRecord[]> {
  const webApi = requireWebApi();
  const filter = `_ownerid_value eq ${ownerId} and sprk_type eq ${NavItemType.Pin}`;
  const query = `?$select=${HISTORY_SELECT}&$filter=${filter}&$orderby=sprk_lastvisited desc`;

  try {
    const result = await webApi.retrieveMultipleRecords(ENTITY, query);
    return (result.entities ?? []) as unknown as NavItemRecord[];
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * Create a per-user `pin` `sprk_navitem` (task 041 MINIMAL promote-to-pin —
 * the Recent tab's inline star). `sprk_source` is always `Manual` here: a
 * user explicitly promoted a history row, as distinct from a future
 * `Captured` "Pin this page" gesture (FR-08, task 050/051) which this helper
 * does not attempt to distinguish. Task 050 owns the FULL pin gesture
 * (toggle/un-pin/duplicate-guard/pin-current-page) and may extend this
 * helper rather than introducing a second create path.
 */
export async function createPinItem(input: CreatePinItemInput): Promise<{ id: string }> {
  const webApi = requireWebApi();
  const now = new Date().toISOString();
  const data: Record<string, unknown> = {
    sprk_type: NavItemType.Pin,
    sprk_source: NavItemSource.Manual,
    sprk_targetlogicalname: input.targetLogicalName,
    sprk_targetid: normalizeGuid(input.targetId),
    sprk_pagetype: input.pageType,
    sprk_displayname: input.displayName,
    sprk_lastvisited: now,
    sprk_visitcount: 1,
  };

  try {
    const created = await webApi.createRecord(ENTITY, data);
    return { id: created.id };
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * Find the current user's existing `pin` `sprk_navitem` for a given target, if
 * one exists (task 050). Mirrors {@link findHistoryItem}'s shape/filter but
 * for `sprk_type=Pin` — used by `pinService.ts` to dedupe-before-create (a
 * star on an already-pinned target must not create a duplicate row) and to
 * resolve the row id to delete on unstar.
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 * @param targetLogicalName - e.g. `sprk_matter`.
 * @param targetId - Target record GUID (normalized internally).
 */
export async function findPinItem(
  ownerId: string,
  targetLogicalName: string,
  targetId: string
): Promise<NavItemRecord | null> {
  const webApi = requireWebApi();
  const normalizedTargetId = normalizeGuid(targetId);
  const filter =
    `_ownerid_value eq ${ownerId} and sprk_type eq ${NavItemType.Pin}` +
    ` and sprk_targetlogicalname eq '${escapeODataString(targetLogicalName)}'` +
    ` and sprk_targetid eq '${escapeODataString(normalizedTargetId)}'`;
  const query = `?$select=${HISTORY_SELECT}&$filter=${filter}&$top=1`;

  try {
    const result = await webApi.retrieveMultipleRecords(ENTITY, query);
    const entities = (result.entities ?? []) as unknown as NavItemRecord[];
    return entities[0] ?? null;
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}

/**
 * Delete a `sprk_navitem` by id (task 050 — unstar removes the per-user pin
 * row). Generic by design (not `deletePinItem`): the same primitive is the
 * correct shape for any future `sprk_navitem` removal (e.g. a "clear history"
 * affordance), so it is not pin-specific in name or implementation — only its
 * CALLERS (`pinService.ts`) are pin-specific.
 */
export async function deleteNavItem(navItemId: string): Promise<void> {
  const webApi = requireWebApi();
  try {
    await webApi.deleteRecord(ENTITY, navItemId);
  } catch (err) {
    throw new NavItemRepositoryError(parseWebApiError(err), err);
  }
}
