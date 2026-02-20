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

  // Fallback: postMessage to parent frame
  try {
    console.info("[navigation] Posting navigation message to parent:", message);
    window.parent.postMessage(message, "*");
  } catch (err) {
    console.error("[navigation] Failed to post navigation message:", err, message);
  }
}
