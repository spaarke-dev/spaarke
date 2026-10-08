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
import {
  captureEmailContent,
  hostAdapterEmailReader,
  EmailCaptureError,
  type EmailContentCapture,
} from '@shared/taskpane/services/emailContentCapture';
import { OutlookAdapter } from '@shared/adapters/OutlookAdapter';

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

/** Outlook refuses a notification message longer than 150 characters (`NotificationMessageDetails.message`). */
const MAX_NOTIFICATION_LENGTH = 150;

/** The longest record name quoted inside a notification, so the fixed sentence around it always fits. */
const MAX_NOTIFIED_NAME_LENGTH = 60;

/** A record name shortened to fit inside a notification sentence. */
function notifiedName(name: string): string {
  return name.length <= MAX_NOTIFIED_NAME_LENGTH ? name : `${name.slice(0, MAX_NOTIFIED_NAME_LENGTH - 1)}…`;
}

/** A message cut to Outlook's limit, as a backstop: a longer one is refused and nothing is shown. */
function fitNotification(message: string): string {
  return message.length <= MAX_NOTIFICATION_LENGTH ? message : `${message.slice(0, MAX_NOTIFICATION_LENGTH - 1)}…`;
}

/**
 * Post a transient informational notification on the current mail item, replacing any earlier one under `key`.
 *
 * `replaceAsync`, not `addAsync` (task 084): quick-save first posts "Saving to Spaarke…" under `spaarke_save`
 * and then its outcome under the same key. `replaceAsync` adds the message when the key is new and replaces it
 * when it is not, so the outcome always takes the place of "Saving…".
 */
function notifyInfo(key: string, message: string): void {
  Office.context.mailbox.item?.notificationMessages.replaceAsync(key, {
    type: Office.MailboxEnums.ItemNotificationMessageType.InformationalMessage,
    message: fitNotification(message),
    icon: 'Icon.16x16',
    persistent: false,
  });
}

/** Post an error notification on the current mail item, replacing any earlier error. */
function notifyError(message: string): void {
  Office.context.mailbox.item?.notificationMessages.replaceAsync('spaarke_error', {
    type: Office.MailboxEnums.ItemNotificationMessageType.ErrorMessage,
    message: fitNotification(message),
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
 * Task 116a: read the email's body and attachments here, in the add-in, for the save — the server's Graph fetch
 * cannot reach a B2B guest's home-tenant mailbox. Every attachment goes into the `.eml` (the complete email); the
 * non-inline ones also become documents (inline images are part of the body, not files the sender attached).
 * `undefined` when the host cannot read attachment content (no Mailbox 1.8 read access): then nothing is read and the
 * server fetches the email through Graph as before — sending a body alone would switch that fetch off and lose the
 * attachments.
 */
async function readEmailContent(): Promise<EmailContentCapture | undefined> {
  const adapter = new OutlookAdapter();
  await adapter.initialize();
  if (!adapter.getCapabilities().canGetAttachments) return undefined;
  const attachments = await adapter.getAttachments();
  const documentsToCreate = new Set(attachments.filter(a => !a.isInline).map(a => a.id));
  return captureEmailContent(hostAdapterEmailReader(adapter), attachments, documentsToCreate);
}

/** The outcome notice; names how many attachments were left out of the saved email — never silent. */
function filedNotice(recordName: string, content: EmailContentCapture | undefined): string {
  const filed = `Filed to ${notifiedName(recordName)}.`;
  const skipped = content?.skipped.length ?? 0;
  if (skipped === 0) return filed;
  return `${filed} ${skipped === 1 ? '1 attachment was' : `${skipped} attachments were`} left out: too large, a cloud link or unreadable.`;
}

/** Open the taskpane programmatically, where the host supports it. */
async function openTaskpane(): Promise<void> {
  try {
    await Office.addin?.showAsTaskpane?.();
  } catch {
    // Host may not support programmatic taskpane open — the notification already guides the user.
  }
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
 *
 * Task 084 (#1037): when the caller cannot file to the predicted record (`predicted.canFile === false`
 * — the save would refuse it, filing needs AppendTo), it does NOT post the save either: it says so and
 * opens the taskpane, the same path as "no prediction".
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
      await openTaskpane();
      return;
    }

    if (pre.predicted.canFile === false) {
      // The caller can see the prediction but cannot file to it → never post a save the server would
      // refuse (403 OFFICE_009); let the user choose in the pane instead.
      notifyInfo(
        'spaarke_save',
        `Spaarke suggests ${notifiedName(pre.predicted.name)}, but you can't file to it. Open Spaarke to choose where to file.`
      );
      await openTaskpane();
      return;
    }

    let content: EmailContentCapture | undefined;
    try {
      content = await readEmailContent();
    } catch (error) {
      // A read failure stops the save: nothing is posted (task 116a — never save an email without the content).
      console.error('Quick save could not read the email:', error);
      notifyError(
        error instanceof EmailCaptureError
          ? "Couldn't read this email or an attachment, so nothing was saved. Open Spaarke to try again."
          : 'Could not read the current email.'
      );
      return;
    }

    const idempotencyKey = await computeQuickSaveIdempotencyKey({
      kind: 'email',
      internetMessageId: context.internetMessageId,
      target: pre.predicted,
    });
    const request = buildEmailSaveRequest(context, pre.predicted, idempotencyKey, content);
    await apiClient.post('/api/office/save', request);

    notifyInfo('spaarke_save', filedNotice(pre.predicted.name, content));
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
