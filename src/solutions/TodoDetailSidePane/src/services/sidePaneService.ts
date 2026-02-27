/**
 * sidePaneService — Side pane lifecycle operations.
 */

import { getXrm } from "../utils/xrmAccess";

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Close the current side pane.
 */
export function closeSidePane(): boolean {
  try {
    const xrm = getXrm();
    const sidePanes = (xrm as any)?.App?.sidePanes;
    const currentPane = sidePanes?.getSelectedPane?.();
    if (currentPane) {
      currentPane.close();
      return true;
    }
  } catch (err) {
    console.warn("[sidePaneService] Failed to close pane:", err);
  }
  return false;
}

/**
 * Open the event form in a modal dialog.
 */
export function openEventForm(eventId: string): void {
  const xrm = getXrm();
  if (!xrm?.Navigation) {
    console.warn("[sidePaneService] Xrm.Navigation not available");
    return;
  }

  xrm.Navigation.openForm({
    entityName: "sprk_event",
    entityId: eventId,
  }).catch((err: unknown) => {
    console.error("[sidePaneService] Failed to open event form:", err);
  });
}
