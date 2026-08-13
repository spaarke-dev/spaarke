/**
 * navigatorCaptureService — Recent (Viewed) passive capture engine
 * (spaarke-side-pane-navigation-history-r1 task 030, spec FR-03).
 *
 * Re-adopts the recovered `contextService.ts` polling loop
 * (`notes/retired-sidepane-code/contextService.ts`,
 * `startContextChangeDetection`): a fixed-interval poll of
 * `Xrm.Utility.getPageContext()` that detects page changes with no
 * navigation-event API. The retired loop's `sessionStorage` capture sink is
 * replaced here with a `history` `sprk_navitem` upsert via
 * `navItemRepository.ts`.
 *
 * Task-001 spike lesson (do NOT re-litigate — see
 * `notes/task-001-spike-report.md`): **never cache the `Xrm` reference**.
 * Caching at mount made `getPageContext()` go stale after the first
 * navigation. Every tick re-acquires `Xrm` fresh via `getXrm()`
 * (`xrmContext.ts`'s 3-frame walk) — this loop holds no `Xrm` field, only a
 * small amount of derived state (`lastKey`) between ticks.
 *
 * Capture scope (implementation decision, task 030 — see completion note /
 * `notes/task-030-*.md` for the OQ-6 finding): only `pageType==='entityrecord'`
 * pages with both an entity name and id are written as history. `entitylist`,
 * `dashboard`, and `custom` pages (including custom pages — OQ-6) have no
 * single resolvable record target for a *history* row and are treated as
 * "no resolvable entity" (skip write; NO malformed row). This does not
 * foreclose custom-page/entitylist support for **pins/bookmarks** (FR-08,
 * task 051), which capture the current page explicitly on a user gesture
 * rather than via this passive poll.
 *
 * The loop itself has NO dependency on pane visibility/collapse state — it is
 * a plain start/stop function, not a React hook, so the caller (the
 * persistent `SprkSidePaneHost`, `alwaysRender:true`) can start it once at
 * mount and it keeps running while the pane is collapsed (NFR-05). Retention
 * pruning is explicitly NOT done here (task 031).
 *
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/retired-sidepane-code/contextService.ts
 * @see navItemRepository.ts
 * @see src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts
 */

import { getXrm, type XrmContext, type XrmUtility } from '../../utils/xrmContext';
import {
  NavItemPageType,
  bumpHistoryItem,
  createHistoryItem,
  findHistoryItem,
  normalizeGuid,
} from './navItemRepository';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Default poll interval — task-001 spike observed every visit with no misses at 1.5s. */
export const DEFAULT_CAPTURE_POLL_INTERVAL_MS = 1_500;

// ---------------------------------------------------------------------------
// Xrm.Utility.getPageContext() typing
// ---------------------------------------------------------------------------
//
// `xrmContext.ts`'s `XrmUtility` interface (task 010) does not declare
// `getPageContext` — it wasn't needed by that task's callers. Rather than
// widen the shared `XrmUtility` type for a single task-030 consumer (which
// would grow the shared surface every future PCF importer of `xrmContext.ts`
// picks up — ADR-022 "keep the shared surface slim"), this module declares
// the narrow extra shape it needs locally and casts at the boundary, mirroring
// how `contextService.ts` and `useFieldMetadata.ts` each define their own
// narrow Xrm subset rather than depending on one universal Xrm type.

interface PageContextInput {
  entityName?: string;
  entityId?: string;
  pageType?: 'entityrecord' | 'entitylist' | 'dashboard' | 'webresource' | 'custom';
}

interface PageContextResult {
  input?: PageContextInput;
}

interface XrmUtilityWithPageContext {
  getPageContext?: () => PageContextResult;
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** The derived "current page" — recomputed every poll, never cached as authoritative. */
export interface DerivedCurrentPage {
  entityLogicalName: string;
  /** Normalized (no braces, lowercase). */
  entityId: string;
  /** One of {@link NavItemPageType}. Currently always `EntityRecord` (see module docblock). */
  pageType: number;
}

export interface NavigatorCaptureOptions {
  /** Poll interval in ms. Default {@link DEFAULT_CAPTURE_POLL_INTERVAL_MS}. */
  intervalMs?: number;
  /**
   * Fires whenever the derived current page changes — including the
   * transition TO `null` when the user leaves records (FR-03 negative case).
   * Does NOT fire on ticks where nothing changed.
   */
  onCurrentPageChange?: (page: DerivedCurrentPage | null) => void;
  /**
   * Fires when a history upsert (find/create/bump) fails. Non-fatal — the
   * loop keeps polling on the next tick; it does not retry the failed write.
   */
  onError?: (error: unknown) => void;
}

/** Returned by {@link startNavigatorCapture}; call to stop the poll and release the interval. */
export type StopNavigatorCapture = () => void;

// ---------------------------------------------------------------------------
// Page-context derivation
// ---------------------------------------------------------------------------

/**
 * Read `Xrm.Utility.getPageContext()` from a freshly-acquired `Xrm` and
 * derive the current page, scoped to resolvable `entityrecord` pages only
 * (see module docblock "Capture scope").
 *
 * Returns `undefined` (distinct from `null`) when `Xrm`/`getPageContext` is
 * simply unreachable this tick (host not ready, transient frame-walk miss) —
 * the caller treats that as "cannot observe this tick" and preserves the
 * last known state rather than flickering to "left the record". Returns
 * `null` when `getPageContext()` DID resolve and reports a non-record page
 * (dashboard/entitylist/custom) or an entityrecord with no id yet (e.g. an
 * unsaved new-record form) — a genuine "not on a resolvable record" signal.
 */
function derivePageFromXrm(xrm: XrmContext | undefined): DerivedCurrentPage | null | undefined {
  if (!xrm) return undefined;
  const utility = xrm.Utility as (XrmUtility & XrmUtilityWithPageContext) | undefined;
  if (!utility?.getPageContext) return undefined;

  const pageContext = utility.getPageContext();
  const input = pageContext?.input;
  if (!input) return null;

  if (input.pageType !== 'entityrecord') return null; // entitylist/dashboard/custom/webresource
  if (!input.entityName || !input.entityId) return null; // unsaved/new record — no id yet

  return {
    entityLogicalName: input.entityName,
    entityId: normalizeGuid(input.entityId),
    pageType: NavItemPageType.EntityRecord,
  };
}

/** Stable dedupe key for a derived page, or `null` for "not on a record". */
function keyOf(page: DerivedCurrentPage | null): string | null {
  return page ? `${page.entityLogicalName}|${page.entityId}` : null;
}

// ---------------------------------------------------------------------------
// History upsert (dedupe + bump, FR-03)
// ---------------------------------------------------------------------------

/**
 * Resolve a best-effort display name for a freshly-sighted target via
 * `Xrm.Utility.getEntityMetadata` (`PrimaryNameAttribute`) + a `retrieveRecord`
 * of that attribute. Unlike `useSprkMemoRepository`'s hardcoded 6-entity
 * `PARENT_PRIMARY_NAME_FIELD` map (correct for its closed parent set), the
 * capture engine observes navigation across ANY entity the user visits, so a
 * static map cannot cover it — metadata discovery is the correct generalization.
 * Never throws: on any failure this falls back to a readable-but-generic
 * label so the write still succeeds (a slightly worse label is preferable to
 * a dropped history row).
 */
async function resolveDisplayName(
  xrm: XrmContext | undefined,
  entityLogicalName: string,
  entityId: string
): Promise<string> {
  const fallback = formatEntityFallbackLabel(entityLogicalName);
  if (!xrm?.WebApi) return fallback;

  interface XrmUtilityWithEntityMetadata {
    getEntityMetadata?: (
      entityLogicalNameArg: string,
      attributes?: string[]
    ) => Promise<{ PrimaryNameAttribute?: string }>;
  }
  const utility = xrm.Utility as (XrmUtility & XrmUtilityWithEntityMetadata) | undefined;
  if (!utility?.getEntityMetadata) return fallback;

  try {
    const meta = await utility.getEntityMetadata(entityLogicalName);
    const primaryNameField = meta?.PrimaryNameAttribute;
    if (!primaryNameField) return fallback;

    const record = await xrm.WebApi.retrieveRecord(entityLogicalName, entityId, `?$select=${primaryNameField}`);
    const name = record?.[primaryNameField];
    return typeof name === 'string' && name.length > 0 ? name : fallback;
  } catch {
    return fallback;
  }
}

/** "sprk_matter" -> "Matter". Same shape as `contextService.ts`'s `getEntityDisplayName` fallback. */
function formatEntityFallbackLabel(entityLogicalName: string): string {
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

/**
 * Upsert a `history` `sprk_navitem` for a resolvable page (FR-03): dedupe by
 * target, bump `sprk_lastvisited`/`sprk_visitcount` on an existing row,
 * resolve + store `sprk_displayname` on first sighting.
 */
async function upsertHistoryItem(xrm: XrmContext | undefined, ownerId: string, page: DerivedCurrentPage): Promise<void> {
  const existing = await findHistoryItem(ownerId, page.entityLogicalName, page.entityId);
  if (existing) {
    await bumpHistoryItem(existing.sprk_navitemid, (existing.sprk_visitcount ?? 0) + 1);
    return;
  }

  const displayName = await resolveDisplayName(xrm, page.entityLogicalName, page.entityId);
  await createHistoryItem({
    targetLogicalName: page.entityLogicalName,
    targetId: page.entityId,
    pageType: page.pageType,
    displayName,
  });
}

// ---------------------------------------------------------------------------
// Poll loop
// ---------------------------------------------------------------------------

/**
 * Start the Recent (Viewed) capture poll. Fires once immediately (so the
 * record the pane mounted on is captured without waiting a full interval),
 * then every `intervalMs`. Returns a stop function; call it on host unmount.
 *
 * The loop is intentionally independent of the pane's visibility/collapse
 * state (NFR-05) — start it once and it keeps polling regardless of whether
 * the caller is currently rendering anything.
 */
export function startNavigatorCapture(options: NavigatorCaptureOptions = {}): StopNavigatorCapture {
  const intervalMs = options.intervalMs ?? DEFAULT_CAPTURE_POLL_INTERVAL_MS;
  let lastKey: string | null = null;
  let stopped = false;
  let inFlight = false;

  const tick = async (): Promise<void> => {
    if (stopped || inFlight) return;
    inFlight = true;
    try {
      // Task-001 spike lesson #1: re-acquire Xrm fresh EVERY tick — never cache.
      const xrm = getXrm();
      const page = derivePageFromXrm(xrm);

      if (page === undefined) return; // Xrm/getPageContext unreachable this tick — preserve last known state

      const currentKey = keyOf(page);
      if (currentKey === lastKey) return; // no change

      lastKey = currentKey;
      options.onCurrentPageChange?.(page);

      if (!page) return; // left records (or unresolved) — do not write

      const globalContext = xrm?.Utility?.getGlobalContext();
      const ownerId = globalContext?.userSettings?.userId;
      if (!ownerId) return; // cannot scope the write to the current user — skip rather than write unowned/malformed

      try {
        await upsertHistoryItem(xrm, ownerId, page);
      } catch (err) {
        options.onError?.(err);
      }
    } finally {
      inFlight = false;
    }
  };

  void tick();
  const intervalId = setInterval(() => {
    void tick();
  }, intervalMs);

  return () => {
    stopped = true;
    clearInterval(intervalId);
  };
}
