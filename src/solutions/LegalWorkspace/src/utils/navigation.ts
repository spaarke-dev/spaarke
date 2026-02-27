/**
 * navigation.ts
 *
 * Navigation utilities for opening Dataverse records/views from
 * a standalone HTML web resource. Uses Xrm.Navigation.openForm when
 * available (frame-walked), with postMessage fallback.
 */

import { getXrm } from '../services/xrmProvider';

export interface INavigationMessage {
  action: "openRecord" | "openView";
  entityName: string;
  entityId?: string;
  viewId?: string;
}

/**
 * Navigate to a Dataverse entity record or view.
 *
 * Strategy:
 *   1. Use Xrm.Navigation.openForm (works from web resources)
 *   2. Fall back to window.parent.postMessage for Custom Page hosts
 */
export function navigateToEntity(message: INavigationMessage): void {
  if (!message.entityName) {
    console.error("[navigation] navigateToEntity: entityName is required", message);
    return;
  }

  // Try Xrm.Navigation.openForm first (reliable from web resources)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm = getXrm() as any;

  if (message.action === "openRecord" && message.entityId && xrm?.Navigation?.openForm) {
    try {
      console.info("[navigation] Opening record via Xrm.Navigation.openForm:", message.entityName, message.entityId);
      xrm.Navigation.openForm({
        entityName: message.entityName,
        entityId: message.entityId,
      });
      return;
    } catch (err) {
      console.warn("[navigation] Xrm.Navigation.openForm failed, falling back to postMessage:", err);
    }
  }

  // Fallback: postMessage to parent frame (navigateToEntity only)
  try {
    console.info("[navigation] Posting navigation message to parent:", message);
    window.parent.postMessage(message, "*");
  } catch (err) {
    console.error("[navigation] Failed to post navigation message:", err, message);
  }
}

/**
 * Open a Dataverse entity record as a dialog main form.
 *
 * Uses Xrm.Navigation.openForm with target: 2 (dialog) to open the record
 * in a modal overlay instead of full-page navigation.
 *
 * @param entityName - The logical name of the entity (e.g. "sprk_matter")
 * @param entityId   - The GUID of the record to open
 */
export function openRecordDialog(entityName: string, entityId: string): void {
  if (!entityName || !entityId) {
    console.error("[navigation] openRecordDialog: entityName and entityId are required");
    return;
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm = getXrm() as any;

  if (!xrm?.Navigation?.openForm) {
    console.error("[navigation] openRecordDialog: Xrm.Navigation.openForm is not available");
    return;
  }

  try {
    xrm.Navigation.openForm(
      { entityName, entityId },
      {
        target: 2,
        width: { value: 80, unit: "%" },
        height: { value: 80, unit: "%" },
      }
    );
  } catch (err) {
    console.error("[navigation] openRecordDialog failed:", err);
  }
}
