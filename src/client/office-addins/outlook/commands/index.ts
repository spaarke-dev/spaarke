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
import { openQuickSaveDialog } from '@shared/commands/quickSaveDialog';
import { readSavedDocument, type QuickSaveResponse } from '@shared/commands/readSavedDocument';
import { buildQuickSaveRecordLink } from '@shared/taskpane/services/quickSaveHelpers';
import { configuredSpaarkeAppName } from '@shared/taskpane/services/openRecordLauncher';

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

/** The sentence naming how many attachments were left out of the saved email (empty when none). */
function skippedNotice(content: EmailContentCapture | undefined): string {
  const skipped = content?.skipped.length ?? 0;
  if (skipped === 0) return '';
  return ` ${skipped === 1 ? '1 attachment was' : `${skipped} attachments were`} left out: too large, a cloud link or unreadable.`;
}

/** The result-dialog text for a saved email. Names how many attachments were left out — never silent. */
function describeEmailSaved(
  target: { name: string } | null,
  content: EmailContentCapture | undefined,
  duplicate: boolean
): string {
  if (duplicate) {
    return 'This email is already saved in Spaarke. Nothing new was saved.';
  }
  return target
    ? `Saved to Spaarke and filed to ${target.name}.${skippedNotice(content)}`
    : `Saved to Spaarke — not filed to a record. Open Spaarke to file it.${skippedNotice(content)}`;
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
 * One-click quick-save (FR-B2 / GitHub #234), with the same progress -> result dialog as Word's Quick Save (task 118,
 * UAT round 12 item O5; the controller and page are shared: `shared/commands/quickSaveDialog.ts`).
 *
 * The dialog opens at once ("Saving to Spaarke…"), then shows the outcome in place — success with an "Open in
 * Spaarke" link to the saved .eml's `sprk_document`, or the error. It replaces the Outlook item info bar, which
 * stayed behind after the click and could not carry a link.
 *
 * Filing: the Association Engine's PREDICTED record (the SHARED `derivePrimaryReview` model via
 * `fetchEnginePreSelection` — no fork) when the caller can file to it. When there is no prediction — including the
 * 404 "not saved yet" answer, which is a normal state, not an error — or the caller cannot file to it (task 084: a
 * contact, a record without AppendTo), the email is saved UNFILED and the dialog says so. An unfiled save is a
 * supported case (owner standing decision); the user files it later from the pane. A guess is never auto-filed.
 *
 * `event.completed()` runs exactly once, when the save is over and the dialog is gone (see `quickSaveDialog.ts`).
 */
async function quickSave(event: Office.AddinCommands.Event): Promise<void> {
  const dialog = await openQuickSaveDialog({
    onComplete: () => event.completed(),
    orgUrl: process.env.ORG_URL,
    pagePath: '/outlook/commands-notify.html',
  });
  try {
    await ensureBootstrapped();

    const context = readEmailContext();
    if (!context) {
      await dialog.show({ message: 'Could not read the current email.', status: 'error' });
      return;
    }

    // Best-effort prediction. 404 ("not saved yet") resolves null inside the service; any other failure only
    // means no suggestion — it must never block the save.
    const pre = await fetchEnginePreSelection(context.internetMessageId).catch(error => {
      console.warn('[Spaarke] Quick Save: the suggested record could not be read; saving unfiled', error);
      return null;
    });
    const target = pre && pre.predicted.canFile !== false ? pre.predicted : null;

    let content: EmailContentCapture | undefined;
    try {
      content = await readEmailContent();
    } catch (error) {
      // A read failure stops the save: nothing is posted (task 116a — never save an email without the content).
      console.error('Quick save could not read the email:', error);
      await dialog.show({
        message:
          error instanceof EmailCaptureError
            ? "Couldn't read this email or an attachment, so nothing was saved. Open Spaarke to try again."
            : 'Could not read the current email.',
        status: 'error',
      });
      return;
    }

    const idempotencyKey = await computeQuickSaveIdempotencyKey({
      kind: 'email',
      internetMessageId: context.internetMessageId,
      target,
    });
    const request = buildEmailSaveRequest(context, target, idempotencyKey, content);
    const response = await apiClient.post<QuickSaveResponse>('/api/office/save', request);

    const saved = await readSavedDocument(response?.statusUrl, null);
    if (saved.kind === 'failed') {
      await dialog.show({
        message: `Spaarke could not finish saving this email: ${saved.reason}.`,
        status: 'error',
      });
      return;
    }

    await dialog.show({
      message: describeEmailSaved(target, content, response?.duplicate === true),
      status: 'success',
      linkUrl:
        saved.kind === 'saved'
          ? buildQuickSaveRecordLink(process.env.ORG_URL, configuredSpaarkeAppName(), saved.document.documentId)
          : null,
    });
  } catch (error) {
    console.error('Quick save failed:', error);
    await dialog.show({ message: 'Failed to save email. Open Spaarke to try manually.', status: 'error' });
  } finally {
    dialog.finish();
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
export { showTaskPane, quickSave, describeEmailSaved };
