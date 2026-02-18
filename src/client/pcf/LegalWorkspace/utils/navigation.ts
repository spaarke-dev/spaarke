/**
 * navigation.ts
 *
 * Type-safe postMessage wrapper for cross-frame navigation within
 * a Power Apps Custom Page. Messages are posted to window.parent
 * so the host Power Apps shell can handle routing.
 */

export interface INavigationMessage {
  action: "openRecord" | "openView";
  entityName: string;
  entityId?: string;
  viewId?: string;
}

/**
 * Navigate to a Dataverse entity record or view by posting a structured
 * message to the parent frame. The receiving Power Apps host page must
 * implement the corresponding message handler.
 *
 * @param message - The navigation intent to broadcast.
 *
 * @example
 * // Open a specific record
 * navigateToEntity({ action: 'openRecord', entityName: 'spe_matter', entityId: '00000000-...' });
 *
 * @example
 * // Open a view
 * navigateToEntity({ action: 'openView', entityName: 'spe_matter', viewId: '11111111-...' });
 */
export function navigateToEntity(message: INavigationMessage): void {
  if (!message.entityName) {
    console.error("[navigation] navigateToEntity: entityName is required", message);
    return;
  }

  if (message.action === "openRecord" && !message.entityId) {
    console.warn(
      "[navigation] navigateToEntity: action 'openRecord' called without entityId. The host may ignore this message.",
      message
    );
  }

  if (message.action === "openView" && !message.viewId) {
    console.warn(
      "[navigation] navigateToEntity: action 'openView' called without viewId. The host may fall back to the default view.",
      message
    );
  }

  try {
    window.parent.postMessage(message, "*");
  } catch (err) {
    console.error("[navigation] Failed to post navigation message:", err, message);
  }
}
