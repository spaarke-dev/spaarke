/**
 * Side Pane Service - Control side pane behavior
 *
 * Provides utilities to close the side pane and navigate to parent records.
 * Uses the global Xrm.App.sidePanes API available in Custom Pages.
 *
 * @see design.md - Event Detail Side Pane specification
 */

import {
  OOB_MODAL_SIZES,
  cleanGuid,
  getXrm,
  readRelativeRecordTarget,
  resolveRegardingPairTable,
  safeRegardingRecordUrl,
} from '@spaarke/ui-components';

/**
 * Xrm.App.sidePanes type definition (subset needed)
 */
interface IXrmSidePanes {
  getSelectedPane(): { close: () => void } | null;
}

/**
 * Xrm.Navigation type definition (subset needed)
 */
interface IXrmNavigation {
  openUrl(options: { url: string }): void;
}

/**
 * Get Xrm.App.sidePanes: the shared cross-frame walker (task 081 / C-8) with
 * the 'sidePanes' capability, so the nearest frame exposing App.sidePanes
 * wins (was a parent-first parent/window read; see `getXrm` for the order rule).
 */
function getXrmSidePanes(): IXrmSidePanes | null {
  const sidePanes = getXrm('sidePanes')?.App?.sidePanes;
  if (!sidePanes) {
    console.warn("[SidePaneService] Xrm.App.sidePanes not available");
    return null;
  }
  return sidePanes as unknown as IXrmSidePanes;
}

/**
 * Get Xrm.Navigation from the nearest frame whose Navigation has `openUrl`
 * (shared cross-frame walker, task 081 / C-8).
 */
function getXrmNavigation(): IXrmNavigation | null {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const navigation = getXrm((x: any) => typeof x.Navigation?.openUrl === 'function')?.Navigation;
  return navigation ? (navigation as unknown as IXrmNavigation) : null;
}

/**
 * Close the current side pane
 *
 * Closes the side pane that hosts this Custom Page.
 *
 * @returns true if close was initiated, false if API not available
 */
export function closeSidePane(): boolean {
  const sidePanes = getXrmSidePanes();
  if (!sidePanes) {
    console.warn("[SidePaneService] Cannot close - sidePanes API not available");
    return false;
  }

  try {
    const currentPane = sidePanes.getSelectedPane();
    if (currentPane) {
      currentPane.close();
      return true;
    } else {
      console.warn("[SidePaneService] No selected pane to close");
      return false;
    }
  } catch (error) {
    console.error("[SidePaneService] Error closing side pane:", error);
    return false;
  }
}

/**
 * Navigate to a parent record URL
 *
 * Opens the parent record in the main app area (not the side pane).
 *
 * @param url - Full URL to the parent record
 * @returns true if navigation was initiated, false if URL invalid or API not available
 */
// ─────────────────────────────────────────────────────────────────────────────
// Open Event Record (Modal)
// ─────────────────────────────────────────────────────────────────────────────

/** Event modal form GUID for opening full record */
const EVENT_MODAL_FORM_ID = "90d2eff7-6703-f111-8407-7ced8d1dc988";

/**
 * Open the full Event record in a modal dialog.
 * Uses Xrm.Navigation.navigateTo with target: 2 (modal dialog).
 */
export function openEventRecord(eventId: string): void {
  try {
    // Shared cross-frame walker (task 081 / C-8): nearest frame that can
    // navigateTo, else the nearest that can openForm (the fallback below).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = getXrm('navigation') ?? getXrm('openForm');

    if (!xrm?.Navigation?.navigateTo) {
      console.warn("[SidePaneService] Xrm.Navigation.navigateTo not available, falling back to openForm");
      if (xrm?.Navigation?.openForm) {
        xrm.Navigation.openForm({
          entityName: "sprk_event",
          entityId: eventId,
          formId: EVENT_MODAL_FORM_ID,
        });
      } else {
        console.error("[SidePaneService] Cannot open form - Xrm.Navigation not available");
      }
      return;
    }

    const pageInput = {
      pageType: "entityrecord",
      entityName: "sprk_event",
      entityId: eventId,
      formId: EVENT_MODAL_FORM_ID,
    };

    // `record` OOB size (85%×85%) — record-modal-selection.md invariant
    // (spec FR-11/FR-18, task 090); was an ad-hoc 80%×80% literal.
    const navigationOptions = {
      target: 2,
      width: OOB_MODAL_SIZES.record.width,
      height: OOB_MODAL_SIZES.record.height,
      position: 1,
    };

    xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
      () => console.log("[SidePaneService] Modal closed"),
      (error: unknown) => console.error("[SidePaneService] Error opening modal:", error)
    );
  } catch (error) {
    console.error("[SidePaneService] Exception opening modal:", error);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Navigate to Parent Record
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Navigate to a parent record. Only a same-origin relative record URL is opened; any other value is refused
 * (nothing is navigated) and the URL passed on is rebuilt in the canonical form.
 */
export function navigateToParentRecord(storedUrl: string): boolean {
  const url = safeRegardingRecordUrl(storedUrl);
  if (!url) {
    console.warn("[SidePaneService] Cannot navigate - not a same-origin record URL");
    return false;
  }

  try {
    // First try using Xrm.Navigation for proper routing
    const navigation = getXrmNavigation();
    if (navigation) {
      navigation.openUrl({ url });
      return true;
    }

    // Fallback: Open in parent window (main app area)
    if (window.parent && window.parent !== window) {
      window.parent.location.href = url;
      return true;
    }

    // Last fallback: Open in same window
    window.location.href = url;
    return true;
  } catch (error) {
    console.error("[SidePaneService] Error navigating to parent:", error);
    return false;
  }
}

/**
 * The URL the Parent button may open: the stored regarding record URL when it is a same-origin relative record URL
 * that names the row's regarding record; otherwise the URL built from the row's pair (the record type's table and the
 * record id); otherwise `null` (do not navigate).
 */
export async function resolveParentRecordUrl(
  storedUrl: string | null | undefined,
  recordTypeId: string | null | undefined,
  recordId: string | null | undefined
): Promise<string | null> {
  const stored = readRelativeRecordTarget(storedUrl);
  const pairId = cleanGuid(recordId);
  // The stored value is a record URL that names the pair's record (or there is no pair to compare): no lookup needed.
  if (stored && (!pairId || stored.entityId === pairId)) {
    return safeRegardingRecordUrl(storedUrl);
  }
  const table = pairId ? await resolveRegardingPairTable(getXrm()?.WebApi, recordTypeId) : null;
  return safeRegardingRecordUrl(storedUrl, table, pairId);
}

/** Open the parent record named by the row's stored URL / pair. Returns false when nothing was opened. */
export async function openParentRecord(
  storedUrl: string | null | undefined,
  recordTypeId: string | null | undefined,
  recordId: string | null | undefined
): Promise<boolean> {
  const url = await resolveParentRecordUrl(storedUrl, recordTypeId, recordId);
  if (!url) {
    console.warn("[SidePaneService] Cannot navigate - no parent record URL could be resolved");
    return false;
  }
  return navigateToParentRecord(url);
}
