/**
 * nameResolution — best-effort resolution of a Dataverse record's or saved
 * view's human name (spaarke-side-pane-navigation-history-r1, UAT).
 *
 * WHY: a `sprk_navitem` row's `sprk_displayname` is captured/derived at write
 * time and, when the host didn't supply a name (e.g. "Pin this page" on a form
 * whose `getPageContext().input.entityRecordName` was empty, or a captured
 * view row), it falls back to a GENERIC label — the entity label ("Document",
 * "Communication") or "{Entity} view". UAT feedback: Bookmarks should show the
 * real record name / view name, not the generic type. These helpers resolve
 * the authoritative name so `BookmarksTab.tsx` can upgrade (and self-heal via
 * `renameNavItem`) rows still showing a generic label, and so
 * `bookmarkService.pinCurrentPage` can name a record pin correctly at creation.
 *
 * Best-effort by contract: every function returns `null` (never throws) on any
 * failure — missing privilege, network error, entity/view without a name,
 * `Xrm` not ready — so callers keep the existing generic label rather than
 * breaking. Host-context `Xrm.WebApi` only (project constraint; no BFF).
 *
 * Mirrors the metadata+retrieve pattern already used privately by
 * `navigatorCaptureService.resolveDisplayName` and
 * `bookmarkService.resolveRecordDisplayName`; consolidated here as the shared
 * source both the load-time enrichment and the pin-create path reuse
 * (CLAUDE.md §11).
 */

import type { XrmContext, XrmUtility } from '@spaarke/ui-components';

// `xrmContext.ts`'s `XrmUtility` (task 010) does not declare `getEntityMetadata`
// — narrowed locally + cast at the boundary, mirroring the identical pattern in
// navigatorCaptureService.ts / bookmarkService.ts (ADR-022 "keep the shared
// surface slim").
interface XrmUtilityWithEntityMetadata {
  getEntityMetadata?: (
    entityLogicalName: string,
    attributes?: string[]
  ) => Promise<{ PrimaryNameAttribute?: string }>;
}

/**
 * Resolve a record's primary-name value via `getEntityMetadata`
 * (`PrimaryNameAttribute`) + a scoped `retrieveRecord`. Returns `null` on any
 * failure or when the resolved name is blank.
 */
export async function resolveRecordName(
  xrm: XrmContext | undefined,
  entityLogicalName: string,
  entityId: string
): Promise<string | null> {
  if (!xrm?.WebApi) return null;
  const utility = xrm.Utility as (XrmUtility & XrmUtilityWithEntityMetadata) | undefined;
  if (!utility?.getEntityMetadata) return null;

  try {
    const meta = await utility.getEntityMetadata(entityLogicalName);
    const primaryNameField = meta?.PrimaryNameAttribute;
    if (!primaryNameField) return null;

    const record = await xrm.WebApi.retrieveRecord(entityLogicalName, entityId, `?$select=${primaryNameField}`);
    const name = record?.[primaryNameField];
    return typeof name === 'string' && name.trim().length > 0 ? name : null;
  } catch {
    return null;
  }
}

/**
 * Resolve a saved view's `name`. A stored view target's id may be a personal
 * (`userquery`) OR system (`savedquery`) view — this tries `userquery` first,
 * then `savedquery`, returning the first that resolves a non-blank name.
 * Returns `null` if neither resolves (best-effort — the caller keeps the
 * generic "{Entity} view" label).
 */
export async function resolveViewName(
  xrm: XrmContext | undefined,
  viewId: string
): Promise<string | null> {
  if (!xrm?.WebApi) return null;
  const id = viewId.replace(/[{}]/g, '');
  if (!id) return null;

  for (const entity of ['userquery', 'savedquery'] as const) {
    try {
      const rec = await xrm.WebApi.retrieveRecord(entity, id, '?$select=name');
      const name = rec?.name;
      if (typeof name === 'string' && name.trim().length > 0) return name;
    } catch {
      // Wrong view kind (404) or transient — try the other kind / give up.
    }
  }
  return null;
}
