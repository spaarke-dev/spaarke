/**
 * bookmarkService — the two bookmark gestures, both writing `sprk_type=pin`
 * `sprk_navitem` (spaarke-side-pane-navigation-history-r1 task 051, spec
 * FR-08 / OQ-7).
 *
 * `pinCurrentPage` — "Pin this page": one click, no typing. Reads
 * `Xrm.Utility.getPageContext()` (the SAME derivation approach
 * `navigatorCaptureService.ts` uses for the Recent/Viewed capture loop:
 * `entityrecord` -> `etn`/`id`; extended here to also resolve `entitylist`
 * pages via `viewId`) to build a captured logical target, then writes it via
 * `pinService.pin` with `sprk_source=Captured`.
 *
 * `addBookmark` — "+ Add bookmark": manual. Parses a pasted/typed string via
 * `urlParse.ts` and writes `sprk_source=Manual`: an MDA record URL becomes a
 * labeled record target, an MDA entitylist/view URL becomes a `viewid`
 * target, a non-Dataverse `http(s)` URL is stored raw
 * (`navItemRepository.createWeblinkPinItem`), and an unparseable non-URL
 * string throws {@link BookmarkError} with a friendly message — no row is
 * created.
 *
 * Both gestures REUSE the task-050 pin-write path (`pinService.pin` /
 * `navItemRepository.createPinItem`/`createWeblinkPinItem`) rather than
 * re-implementing `Xrm.WebApi` CRUD (CLAUDE.md §11) — this module's own
 * responsibility is deriving the TARGET (from `getPageContext()` or from a
 * parsed URL) and mapping it onto the existing create/dedupe primitives.
 * Never writes `sprk_monitor` (project HARD MUST-NOT — every write here goes
 * through `pinService`/`navItemRepository`, whose only entity is
 * `sprk_navitem`).
 *
 * Dedupe: a `record`/`view` target IS deduped (via `pinService.pin`'s
 * find-before-create, same as the star gesture) — pinning/bookmarking a
 * target that is already pinned (by any gesture) reuses the existing row
 * rather than creating a duplicate. A `weblink` target has no stable
 * target-entity identity to dedupe on (two different pastes of the exact
 * same external URL are treated as two independent bookmarks, same as a
 * browser's own bookmark manager) — see `createWeblinkPinItem`'s own
 * docblock.
 *
 * @see urlParse.ts (task 051) — the pure MDA-URL-shape parser this module maps onto pin writes
 * @see pinService.ts (task 050) — the pin-write path both gestures reuse
 * @see navigatorCaptureService.ts — the `getPageContext()` derivation approach `pinCurrentPage` reuses
 * @see PinnedTab.tsx (task 051) — renders the Bookmarks group, calls both exports here
 */

import { getXrm, type XrmContext, type XrmUtility } from '@spaarke/ui-components';
import {
  NavItemPageType,
  NavItemSource,
  createWeblinkPinItem,
  normalizeGuid,
} from '@spaarke/ui-components/services/navigator/navItemRepository';
import { pin, type PinResult } from './pinService';
import { parseBookmarkInput } from './urlParse';

/**
 * Thrown by {@link pinCurrentPage} (current page not resolvable) and
 * {@link addBookmark} (input did not parse as a URL). Always carries a
 * friendly, render-safe `message` — `PinnedTab.tsx` shows it inline without
 * needing to inspect the error further.
 */
export class BookmarkError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'BookmarkError';
  }
}

export type BookmarkKind = 'record' | 'view' | 'weblink';

export interface AddBookmarkResult extends PinResult {
  kind: BookmarkKind;
}

// ---------------------------------------------------------------------------
// "Pin this page" — captured target derivation
// ---------------------------------------------------------------------------

// `xrmContext.ts`'s `XrmUtility` (task 010) does not declare `getPageContext`
// — narrowed locally and cast at the boundary, mirroring
// `navigatorCaptureService.ts`'s identical pattern (ADR-022 "keep the shared
// surface slim"). Widened here (vs. that module's narrower shape) to also
// read `entityRecordName` (best-effort display name, when the host preloaded
// it) and `viewId` (entitylist pages) — task 051's "Pin this page" resolves
// entitylist targets too, unlike the history capture loop (see that module's
// "Capture scope" docblock note).
interface PageContextInput {
  entityName?: string;
  entityId?: string;
  entityRecordName?: string;
  pageType?: 'entityrecord' | 'entitylist' | 'dashboard' | 'webresource' | 'custom';
  viewId?: string;
}

interface PageContextResult {
  input?: PageContextInput;
}

interface XrmUtilityWithPageContext {
  getPageContext?: () => PageContextResult;
}

interface CapturedPageTarget {
  targetLogicalName: string;
  targetId: string;
  pageType: number;
  displayName: string;
}

/** "sprk_matter" -> "Matter". Local duplicate of the same 2-line helper already established in PinnedTab.tsx/RecentTab.tsx/ViewsTab.tsx (CLAUDE.md §11 — extension earned by a real second consumer of the SAME module, not a sibling file). */
function formatEntityFallbackLabel(entityLogicalName: string): string {
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

// `xrmContext.ts`'s `XrmUtility` (task 010) does not declare
// `getEntityMetadata` — narrowed locally and cast at the boundary, mirroring
// `navigatorCaptureService.ts`'s/`monitoredService.ts`'s identical pattern.
interface XrmUtilityWithEntityMetadata {
  getEntityMetadata?: (
    entityLogicalName: string,
    attributes?: string[]
  ) => Promise<{ PrimaryNameAttribute?: string }>;
}

/**
 * Resolve a record's primary-name for `addBookmark`'s `record` branch — a
 * pasted MDA record URL carries no display name of its own (unlike
 * `deriveCapturedTarget`'s `entityRecordName`, which `getPageContext()` often
 * already supplies for free), so without this every manually-bookmarked
 * record would show the same generic "Matter"/"Document" label with no way
 * to tell two bookmarks apart. Mirrors `navigatorCaptureService.ts`'s
 * `resolveDisplayName` (same `getEntityMetadata` + `retrieveRecord` pattern)
 * — that function is not exported (module-private to the capture loop), so
 * this is a local, best-effort duplicate: never throws, falls back to
 * {@link formatEntityFallbackLabel} on any failure (missing privilege,
 * network error, entity has no primary-name attribute, etc.).
 */
async function resolveRecordDisplayName(
  xrm: XrmContext | undefined,
  entityLogicalName: string,
  entityId: string
): Promise<string> {
  const fallback = formatEntityFallbackLabel(entityLogicalName);
  if (!xrm?.WebApi) return fallback;

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

/**
 * Derive the current page's pin target from a freshly-acquired `Xrm`'s
 * `getPageContext()`. Returns `null` when the current page is not
 * resolvable — a dashboard/custom/webresource page, an `entityrecord` page
 * with no id yet (unsaved new-record form), or an `entitylist` page with no
 * `viewId` — so the caller can skip gracefully (no malformed row), per the
 * task's escalation-avoiding directive.
 */
function deriveCapturedTarget(xrm: XrmContext | undefined): CapturedPageTarget | null {
  const utility = xrm?.Utility as (XrmUtility & XrmUtilityWithPageContext) | undefined;
  if (!utility?.getPageContext) return null;

  const input = utility.getPageContext()?.input;
  if (!input) return null;

  if (input.pageType === 'entityrecord' && input.entityName && input.entityId) {
    const label = input.entityRecordName?.trim();
    return {
      targetLogicalName: input.entityName,
      targetId: normalizeGuid(input.entityId),
      pageType: NavItemPageType.EntityRecord,
      displayName: label && label.length > 0 ? label : formatEntityFallbackLabel(input.entityName),
    };
  }

  if (input.pageType === 'entitylist' && input.entityName && input.viewId) {
    return {
      targetLogicalName: input.entityName,
      targetId: normalizeGuid(input.viewId),
      pageType: NavItemPageType.EntityList,
      displayName: `${formatEntityFallbackLabel(input.entityName)} view`,
    };
  }

  return null; // dashboard/custom/webresource, entityrecord w/o id, entitylist w/o viewid — not resolvable
}

/**
 * "Pin this page" (task 051, spec FR-08): bookmark the CURRENT page with no
 * typing. Reads `getPageContext()` fresh (never a cached `Xrm` — task-001
 * spike lesson) and writes a `sprk_type=pin`, `sprk_source=Captured`
 * `sprk_navitem` via the same dedupe-before-create path the star gesture
 * uses ({@link pin}). Throws {@link BookmarkError} when the current page is
 * not resolvable — the caller shows a friendly inline message and creates no
 * row.
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 */
export async function pinCurrentPage(ownerId: string): Promise<PinResult> {
  const xrm = getXrm();
  const target = deriveCapturedTarget(xrm);
  if (!target) {
    throw new BookmarkError("This page can't be pinned.");
  }

  return pin(ownerId, {
    targetLogicalName: target.targetLogicalName,
    targetId: target.targetId,
    pageType: target.pageType,
    displayName: target.displayName,
    source: NavItemSource.Captured,
  });
}

// ---------------------------------------------------------------------------
// "+ Add bookmark" — manual URL-parse target derivation
// ---------------------------------------------------------------------------

/** `new URL(url).hostname`, falling back to the raw URL if that somehow throws (already-validated by `urlParse.ts`, so this is defensive only). */
function formatWeblinkDisplayName(url: string): string {
  try {
    const host = new URL(url).hostname;
    return host || url;
  } catch {
    return url;
  }
}

/**
 * "+ Add bookmark" (task 051, spec FR-08 / OQ-7): parse a pasted/typed
 * string via `urlParse.ts` and write a `sprk_type=pin`, `sprk_source=Manual`
 * `sprk_navitem`:
 *   - An MDA record URL -> a labeled `record` target (deduped via {@link pin}).
 *   - An MDA entitylist/view URL -> a `view` target keyed on `viewId`
 *     (deduped via {@link pin} when `etn` is present; created directly —
 *     no dedupe key — in the rare case a `viewid` URL omits `etn`).
 *   - Any other `http(s)` URL -> a raw `weblink`
 *     (`navItemRepository.createWeblinkPinItem` — no dedupe, see that
 *     function's docblock).
 *   - An unparseable non-URL string -> throws {@link BookmarkError} with the
 *     parser's friendly reason. No row is created.
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 * @param rawInput - The pasted/typed bookmark string.
 */
export async function addBookmark(ownerId: string, rawInput: string): Promise<AddBookmarkResult> {
  const parsed = parseBookmarkInput(rawInput);

  if (parsed.kind === 'reject') {
    throw new BookmarkError(parsed.reason);
  }

  if (parsed.kind === 'record') {
    const displayName = await resolveRecordDisplayName(getXrm(), parsed.etn, parsed.id);
    const result = await pin(ownerId, {
      targetLogicalName: parsed.etn,
      targetId: parsed.id,
      pageType: parsed.pageType,
      displayName,
      source: NavItemSource.Manual,
    });
    return { ...result, kind: 'record' };
  }

  if (parsed.kind === 'view') {
    const displayName = parsed.etn ? `${formatEntityFallbackLabel(parsed.etn)} view` : 'Saved view';
    if (parsed.etn) {
      const result = await pin(ownerId, {
        targetLogicalName: parsed.etn,
        targetId: parsed.viewId,
        pageType: NavItemPageType.EntityList,
        displayName,
        source: NavItemSource.Manual,
      });
      return { ...result, kind: 'view' };
    }
    // A `viewid` URL without `etn` has no clean target-entity identity to
    // dedupe on (rare — real MDA entitylist links always pair `etn` with
    // `viewid`) — fall back to storing the raw MDA URL as a weblink (same
    // path as the true-weblink branch below); it still resolves and opens
    // correctly, just without a labeled logical target. `kind: 'weblink'`
    // here reflects what was actually persisted (`sprk_pagetype=WebLink`),
    // not the caller's `view`-shaped input.
    const created = await createWeblinkPinItem({
      url: rawInput.trim(),
      displayName,
      source: NavItemSource.Manual,
    });
    return { navItemId: created.id, created: true, kind: 'weblink' };
  }

  // weblink — no target identity to dedupe on (see createWeblinkPinItem docblock).
  const created = await createWeblinkPinItem({
    url: parsed.url,
    displayName: formatWeblinkDisplayName(parsed.url),
    source: NavItemSource.Manual,
  });
  return { navItemId: created.id, created: true, kind: 'weblink' };
}
