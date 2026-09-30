/**
 * Word add-in commands (function file).
 *
 * These functions are invoked from ribbon buttons and keyboard shortcuts.
 * They run in a separate context from the taskpane, so — mirroring
 * `outlook/commands/index.ts`, the structural reference for this file — the shared `authService` +
 * `apiClient` singletons and a `WordAdapter` instance must all be bootstrapped here independently.
 *
 * spaarkeai-word-add-in-r1 task 037 (FR-17): wires the previously-stubbed `quickSave` and
 * `shareDocument` commands to real Spaarke behavior. See
 * `projects/spaarkeai-word-add-in-r1/notes/037-manifest-change.md` for the design record (notification
 * mechanism, the "open the pane" deliberate exception, and the manifest change this task made).
 */

import { HostAdapterFactory } from '@shared/adapters';
import { WordAdapter } from '@shared/adapters/WordAdapter';
import type { IHostAdapter } from '@shared/adapters';
import { authService, apiClient } from '@shared/services';
import { resolveDocumentIdentity, applyStampPrecedence } from '@shared/taskpane/services/documentIdentityService';
import {
  buildDocumentSaveRequest,
  computeQuickSaveIdempotencyKey,
  arrayBufferToBase64,
} from '@shared/taskpane/services/quickSaveHelpers';
import { mintDocumentShareLink } from '@shared/taskpane/services/shareLinkService';

// Register global functions for Office to call
declare global {
  interface Window {
    showTaskPane: (event: Office.AddinCommands.Event) => void;
    quickSave: (event: Office.AddinCommands.Event) => void;
    shareDocument: (event: Office.AddinCommands.Event) => void;
  }
}

// Build-time configuration (webpack DefinePlugin injects process.env.*), mirroring
// outlook/commands/index.ts's bootstrap and word/taskpane/index.tsx's CONFIG shape.
const CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || '',
  tenantId: process.env.TENANT_ID || '',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || '',
  fallbackRedirectUri: process.env.FALLBACK_REDIRECT_URI || '',
};

let bootstrapped = false;
let wordAdapter: IHostAdapter | null = null;

/**
 * Initialize auth + the API client + a WordAdapter once (the commands context is separate from the
 * taskpane, so none of the taskpane's own bootstrap or `HostAdapterFactory.registerAdapter` call runs
 * here — each entry point owns its own).
 *
 * `HostAdapterFactory.createAndInitialize('word')` is called with the EXPLICIT host literal rather
 * than the no-arg form that falls back to `detectHostType()` reading `Office.context.host`. This file
 * only ever loads inside `word/commands.html` (a Word-only bundle, never shared with Outlook), so the
 * explicit literal is not a guess — and it sidesteps the open host-detection reliability question
 * project CLAUDE.md's 2026-09-09 decision log records for the taskpane's own (necessarily
 * host-generic) bootstrap. `WordAdapter.initialize()` still independently verifies `info.host ===
 * Office.HostType.Word` itself, so this does not bypass the real safety check — it only avoids an
 * unnecessary second layer of detection.
 */
async function ensureBootstrapped(): Promise<IHostAdapter> {
  if (!bootstrapped) {
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

  if (!wordAdapter) {
    HostAdapterFactory.registerAdapter('word', WordAdapter);
    wordAdapter = await HostAdapterFactory.createAndInitialize('word');
  }

  return wordAdapter;
}

// ---------------------------------------------------------------------------------------------
// Notification surface.
//
// Word has no equivalent of Outlook's `Office.context.mailbox.item.notificationMessages` — that
// API is Mailbox-only. An earlier version of this file tried the Ribbon API
// (`Office.ribbon.requestUpdate`) for a transient status label — that does NOT work: the installed
// `@types/office-js`'s `Office.Control` (the shape `requestUpdate` accepts) has only `id` and
// `enabled?: boolean`, no `label` (confirmed by `npx tsc --noEmit` catching it as TS2353 during this
// task — see notes/037-manifest-change.md). The Ribbon API can only toggle a control's enabled state,
// not its text.
//
// The genuine, documented, Word-compatible mechanism for a UI-less command to show the user
// something without opening the task pane is the Dialog API (`Office.context.ui.displayDialogAsync`
// — DialogApi requirement set, Excel/Outlook/PowerPoint/Word; the type's own doc comment explicitly
// says it is callable "from a UI-less command button"). `word/commands/notify.html` (emitted as
// `word/commands-notify.html`, see webpack.config.js) is a tiny, dependency-free page that displays
// the message and self-closes — see that file's header comment for the full rationale.
// ---------------------------------------------------------------------------------------------

/**
 * Show a brief message to the user via a small, self-closing Dialog API window. Best-effort only —
 * every failure (unsupported host, a rejected `displayDialogAsync`) is swallowed so this can never
 * block `event.completed()` on the caller's `finally`.
 */
function notify(message: string, status: 'success' | 'error' = 'success'): void {
  try {
    const url = `${window.location.origin}/word/commands-notify.html?message=${encodeURIComponent(message)}&status=${status === 'error' ? 'error' : 'info'}`;
    Office.context.ui.displayDialogAsync(url, { height: 20, width: 25, promptBeforeOpen: false }, () => {
      // Best effort — a failed dialog open is not itself a failure of the command that called it.
    });
  } catch {
    // Office.context.ui unavailable — never blocks completion.
  }
}

/**
 * Opens the taskpane.
 */
function showTaskPane(event: Office.AddinCommands.Event): void {
  event.completed();
}

/**
 * Quick save the current document to Spaarke DMS (spaarkeai-word-add-in-r1 task 037 / FR-17).
 *
 * Always a CREATE of a new, unfiled `sprk_document` — no association-engine prediction exists for
 * documents (unlike Outlook's `quickSave`, which files to a PREDICTED record), and `useSaveFlow`
 * already treats an association as optional for a Document save. The user can file/associate the
 * document later from the pane. `computeQuickSaveIdempotencyKey`'s content-based key (not a
 * network/server round trip) is what stops a double ribbon click from creating two documents.
 *
 * `event.completed()` fires on every path (success, extraction failure, network/auth failure) via the
 * `finally` block — asserted by unit test in `__tests__/commands.test.ts`, not by inspection.
 */
async function quickSave(event: Office.AddinCommands.Event): Promise<void> {
  try {
    const adapter = await ensureBootstrapped();

    const [title, contentBuffer] = await Promise.all([
      adapter.getSubject(),
      adapter.getDocumentContent({ format: 'ooxml' }),
    ]);
    const contentBase64 = arrayBufferToBase64(contentBuffer);

    const idempotencyKey = await computeQuickSaveIdempotencyKey({ kind: 'document', title, contentBase64 });
    const request = buildDocumentSaveRequest({ title }, contentBase64, idempotencyKey);

    await apiClient.post('/api/office/save', request);

    notify('Saved to Spaarke.', 'success');
  } catch (error) {
    console.error('Quick save failed:', error);
    notify('Failed to save. Open Spaarke to try manually.', 'error');
  } finally {
    event.completed();
  }
}

/**
 * Resolve the CURRENTLY OPEN document to its `sprk_documentid`, the same precedence the pane uses
 * (task 051 owner decision — a resolved cloud URL wins; the client-side identity stamp is the
 * fallback only for `not_cloud_document` / `not_resolvable` / `not_spaarke_document`; a disagreement
 * is a conflict with no default). Returns `null` for every non-`'resolved'` outcome — there is
 * nothing to share.
 */
async function resolveCurrentDocumentId(adapter: IHostAdapter): Promise<string | null> {
  const url = await adapter.getDocumentUrl();
  const urlOutcome = await resolveDocumentIdentity(url);
  const stampId = adapter.getCapabilities().canReadDocumentStamp ? await adapter.readDocumentStamp() : null;
  const identity = applyStampPrecedence(urlOutcome, stampId);
  return identity.kind === 'resolved' ? identity.documentId : null;
}

/**
 * Share the current document via Spaarke (spaarkeai-word-add-in-r1 task 037 / FR-17).
 *
 * Mints a share link for the document's resolved Spaarke identity via the EXISTING
 * `POST /api/documents/{documentId}/share-link` route (see `shareLinkService.ts` for why this does
 * not reuse `sendEmailService.ts`'s private copy of the same call). On success, the link is copied to
 * the clipboard (best effort — never blocks completion) and a notification confirms it, without
 * opening the pane.
 *
 * DELIBERATE EXCEPTION (stated per the task's own constraint): when the open document has no resolved
 * Spaarke identity yet — it was never saved to Spaarke — there is nothing to share. Rather than fail
 * silently or guess, this opens the task pane so the user can save the document first, mirroring
 * `outlook/commands/index.ts`'s `quickSave` precedent for "nothing to act on, let the user choose."
 * Every other path completes WITHOUT opening the pane.
 */
async function shareDocument(event: Office.AddinCommands.Event): Promise<void> {
  try {
    const adapter = await ensureBootstrapped();
    const documentId = await resolveCurrentDocumentId(adapter);

    if (!documentId) {
      // Nothing to share yet — deliberate pane-opening exception, not an error path.
      try {
        await Office.addin?.showAsTaskpane?.();
      } catch {
        // Host may not support programmatic taskpane open — nothing further to do here.
      }
      return;
    }

    const minted = await mintDocumentShareLink(documentId);
    if (!minted.ok) {
      notify(minted.message || 'Failed to create a share link.', 'error');
      return;
    }

    try {
      await navigator.clipboard?.writeText?.(minted.url);
    } catch {
      // Clipboard write is a best-effort nicety — the notification is the primary contract.
    }
    notify('Share link copied to clipboard.', 'success');
  } catch (error) {
    console.error('Share document failed:', error);
    notify('Failed to share. Open Spaarke to try manually.', 'error');
  } finally {
    event.completed();
  }
}

// Initialize and register commands
Office.onReady(() => {
  window.showTaskPane = showTaskPane;
  window.quickSave = quickSave;
  window.shareDocument = shareDocument;

  // Unified (JSON) manifest executeFunction registration — the manifest's `quickSave` / `shareDocument`
  // actions invoke these functions (mirrors outlook/commands/index.ts's convention).
  Office.actions?.associate?.('quickSave', quickSave);
  Office.actions?.associate?.('shareDocument', shareDocument);
});

export { showTaskPane, quickSave, shareDocument };
