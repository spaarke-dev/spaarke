/**
 * Outlook add-in commands (function file).
 *
 * These functions are invoked from ribbon buttons and keyboard shortcuts.
 * They run in a separate context from the taskpane, so the shared `authService`
 * + `apiClient` singletons must be bootstrapped here independently.
 */

import { authService, apiClient } from '@shared/services';
import { fetchEnginePreSelection } from '@shared/taskpane/services/communicationSuggestionsService';
import {
  buildEmailSaveRequest,
  computeQuickSaveIdempotencyKey,
  type QuickSaveEmailContext,
  type QuickSaveRecipient,
} from '@shared/taskpane/services/quickSaveHelpers';

// Register global functions for Office to call
declare global {
  interface Window {
    showTaskPane: (event: Office.AddinCommands.Event) => void;
    quickSave: (event: Office.AddinCommands.Event) => void;
  }
}

// Build-time configuration (webpack DefinePlugin injects process.env.*), mirroring
// the taskpane bootstrap in outlook/taskpane/index.tsx.
const CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || '',
  tenantId: process.env.TENANT_ID || '',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || '',
  fallbackRedirectUri: process.env.FALLBACK_REDIRECT_URI || '',
};

let bootstrapped = false;

/** Initialize auth + the API client once (the commands context is separate from the taskpane). */
async function ensureBootstrapped(): Promise<void> {
  if (bootstrapped) return;
  await authService.initialize({
    clientId: CONFIG.clientId,
    tenantId: CONFIG.tenantId,
    bffApiClientId: CONFIG.bffApiClientId,
    ...(CONFIG.fallbackRedirectUri ? { fallbackRedirectUri: CONFIG.fallbackRedirectUri } : {}),
  });
  apiClient.configure({
    baseUrl: CONFIG.bffApiBaseUrl,
    bffApiClientId: CONFIG.bffApiClientId,
  });
  bootstrapped = true;
}

/** Post a transient informational notification on the current mail item. */
function notifyInfo(key: string, message: string): void {
  Office.context.mailbox.item?.notificationMessages.addAsync(key, {
    type: Office.MailboxEnums.ItemNotificationMessageType.InformationalMessage,
    message,
    icon: 'Icon.16x16',
    persistent: false,
  });
}

/** Post an error notification on the current mail item. */
function notifyError(message: string): void {
  Office.context.mailbox.item?.notificationMessages.addAsync('spaarke_error', {
    type: Office.MailboxEnums.ItemNotificationMessageType.ErrorMessage,
    message,
  });
}

/** Read the open email's metadata for quick-save (reading pane — synchronous properties). */
function readEmailContext(): QuickSaveEmailContext | null {
  const item = Office.context.mailbox.item;
  if (!item || !item.internetMessageId) return null;

  const recipients: QuickSaveRecipient[] = [];
  (item.to ?? []).forEach(r => recipients.push({ email: r.emailAddress, displayName: r.displayName, type: 'to' }));
  (item.cc ?? []).forEach(r => recipients.push({ email: r.emailAddress, displayName: r.displayName, type: 'cc' }));

  return {
    internetMessageId: item.internetMessageId,
    subject: item.subject ?? '',
    ...(item.from?.emailAddress ? { senderEmail: item.from.emailAddress } : {}),
    ...(item.from?.displayName ? { senderName: item.from.displayName } : {}),
    recipients,
    ...(item.dateTimeCreated ? { sentDate: new Date(item.dateTimeCreated) } : {}),
  };
}

/**
 * Opens the taskpane.
 */
function showTaskPane(event: Office.AddinCommands.Event): void {
  // The taskpane will be shown automatically by Office
  // This function just signals completion
  event.completed();
}

/**
 * One-click quick-save (FR-B2 / GitHub #234): file the current email to the Association
 * Engine's PREDICTED record. Reuses the SHARED candidate model (`derivePrimaryReview`
 * via fetchEnginePreSelection — no fork) so the prediction matches the taskpane picker
 * and the code page. When the engine has no prediction (email not captured / no usable
 * candidate), it does NOT auto-file a guess — it opens the taskpane so the user chooses.
 */
async function quickSave(event: Office.AddinCommands.Event): Promise<void> {
  try {
    notifyInfo('spaarke_save', 'Saving to Spaarke…');
    await ensureBootstrapped();

    const context = readEmailContext();
    if (!context) {
      notifyError('Could not read the current email.');
      return;
    }

    // Best-effort prediction (a failure must not block — falls through to the picker).
    const pre = await fetchEnginePreSelection(context.internetMessageId).catch(() => null);

    if (!pre) {
      // No prediction → open the taskpane for an explicit choice (never auto-file a guess).
      notifyInfo('spaarke_save', 'No suggested record — open Spaarke to choose where to file.');
      try {
        await Office.addin?.showAsTaskpane?.();
      } catch {
        // Host may not support programmatic taskpane open — the notification already guides the user.
      }
      return;
    }

    const idempotencyKey = await computeQuickSaveIdempotencyKey(context.internetMessageId, pre.predicted);
    const request = buildEmailSaveRequest(context, pre.predicted, idempotencyKey);
    await apiClient.post('/api/office/save', request);

    notifyInfo('spaarke_save', `Filed to ${pre.predicted.name}.`);
  } catch (error) {
    console.error('Quick save failed:', error);
    notifyError('Failed to save email. Open Spaarke to try manually.');
  } finally {
    event.completed();
  }
}

// Initialize and register commands
Office.onReady(() => {
  // Register global functions
  window.showTaskPane = showTaskPane;
  window.quickSave = quickSave;

  // Unified (JSON) manifest executeFunction registration — the manifest's
  // `quickSave` action (QuickSaveButton, mailRead ribbon) invokes this function.
  Office.actions?.associate?.('quickSave', quickSave);
});

// Export for module systems
export { showTaskPane, quickSave };
