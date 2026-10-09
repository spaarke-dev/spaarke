/**
 * Word add-in commands (function file).
 *
 * These functions are invoked from ribbon buttons and keyboard shortcuts.
 * They run in a separate context from the taskpane, so — mirroring
 * `outlook/commands/index.ts`, the structural reference for this file — the shared `authService` +
 * `apiClient` singletons and a `WordAdapter` instance must all be bootstrapped here independently.
 *
 * spaarkeai-word-add-in-r1 task 037 (FR-17) wired `quickSave` to real Spaarke behavior (design record:
 * `notes/037-manifest-change.md`). Task 089 (UAT round 3, UAT-9/10 — `notes/089-identity-mark-quick-save-open-spaarke.md`):
 * Quick Save now saves a VERSION of a document it can identify and keeps both on a foreign name, says what it did,
 * and marks the open document with its identity; the ribbon's Share is replaced by `openSpaarke`.
 */

import { HostAdapterFactory } from '@shared/adapters';
import { WordAdapter } from '@shared/adapters/WordAdapter';
import type { IHostAdapter } from '@shared/adapters';
import { authService, apiClient } from '@shared/services';
import {
  resolveDocumentIdentity,
  applyStampPrecedence,
  writeIdentityStampAfterSave,
  type DocumentIdentityOutcome,
} from '@shared/taskpane/services/documentIdentityService';
import {
  buildDocumentSaveRequest,
  computeQuickSaveIdempotencyKey,
  arrayBufferToBase64,
  describeQuickSaveSuccess,
  describeQuickSaveFailure,
  describeUnsavableIdentity,
  buildQuickSaveRecordLink,
  type QuickSaveStage,
  type QuickSaveTarget,
} from '@shared/taskpane/services/quickSaveHelpers';
import {
  buildOpenSpaarkeUrl,
  configuredSpaarkeAppName,
  openFileUrl,
} from '@shared/taskpane/services/openRecordLauncher';
import { openQuickSaveDialog } from '@shared/commands/quickSaveDialog';
import { readSavedDocument, type QuickSaveResponse } from '@shared/commands/readSavedDocument';

// Register global functions for Office to call
declare global {
  interface Window {
    showTaskPane: (event: Office.AddinCommands.Event) => void;
    quickSave: (event: Office.AddinCommands.Event) => void;
    openSpaarke: (event: Office.AddinCommands.Event) => void;
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

/** Longest notification text sent to the dialog (task 089 — server reasons are unbounded). */
const NOTIFY_MAX_CHARS = 400;

/**
 * Show a message to the user via a small, self-closing Dialog API window. Best-effort only —
 * every failure (unsupported host, a rejected `displayDialogAsync`) is swallowed so this can never
 * block `event.completed()` on the caller's `finally`.
 *
 * Task 089: an error carries a reason the user must be able to read (the server's own message), so it gets a
 * larger window, and `notify.html` keeps it open for 8 s (or until dismissed) instead of a success's 2.2 s.
 */
function notify(message: string, status: 'success' | 'error' = 'success'): void {
  try {
    // The text travels in the dialog URL, and an error can now carry the server's own detail — bound it so a long
    // reason cannot push the URL past what the host accepts (the dialog would then not open at all).
    const text = message.length > NOTIFY_MAX_CHARS ? `${message.slice(0, NOTIFY_MAX_CHARS - 1)}…` : message;
    const url = `${window.location.origin}/word/commands-notify.html?message=${encodeURIComponent(text)}&status=${status === 'error' ? 'error' : 'info'}`;
    const size = status === 'error' ? { height: 30, width: 35 } : { height: 20, width: 25 };
    Office.context.ui.displayDialogAsync(url, { ...size, promptBeforeOpen: false }, () => {
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
 * Resolve the CURRENTLY OPEN document's identity with the precedence the pane uses (task 051 owner decision — a
 * resolved cloud URL wins; the identity stamp is the fallback only for `not_cloud_document` / `not_resolvable` /
 * `not_spaarke_document`; a disagreement is a conflict with no default). The stamp read is capability-gated
 * (NFR-10). Formerly the Share command's; task 089 makes it Quick Save's (042 round 3 §2: Quick Save used to send
 * no identity at all, so every second save of a document collided with its own record).
 */
async function resolveOpenDocumentIdentity(adapter: IHostAdapter): Promise<DocumentIdentityOutcome> {
  const url = await adapter.getDocumentUrl();
  const urlOutcome = await resolveDocumentIdentity(url);
  const stampId = adapter.getCapabilities().canReadDocumentStamp ? await adapter.readDocumentStamp() : null;
  return applyStampPrecedence(urlOutcome, stampId);
}

/**
 * Quick Save the open document to Spaarke (task 037 / FR-17; reworked by task 089 for UAT-9, owner decisions
 * 2026-10-03 — `notes/042-uat-round3-2026-10-03.md` §2).
 *
 * 1. **Identity first** — URL, then the identity stamp ({@link resolveOpenDocumentIdentity}). A file NAME is never
 *    identity (#1005).
 * 2. **Known document** (`resolved`) → a new VERSION of it (`existingDocumentId` + `isNewVersion`, FR-11). The stamp
 *    only chooses which id to send; the server's `OfficeVersionSaveAuthorizationFilter` still decides (014 §3).
 * 3. **Unknown document** (`new`) → a CREATE under the pane's own file-name rule, with `allowRename`, so a name that
 *    belongs to a DIFFERENT document is kept-both by the server instead of refused.
 * 4. **Anything else** (conflict / indeterminate / denied / error) → nothing is saved and the user is told why —
 *    the pane would not save silently there either (task 024).
 * 5. **After any success** → the open document is marked with the saved id (capability-gated, non-fatal), so the
 *    next save of it — ribbon or pane, now or after reopening — is recognised.
 * 6. **The notification says what happened, in words**: "Saved to Spaarke as '{name}'", "Saved a new version of
 *    '{name}'", or the server's own message for a refusal. Never the fixed "Failed to save" text.
 *
 * Task 110 (UAT round 11, item 2): the Spaarke dialog opens at the START with a progress state and is then updated in
 * place with the result — success (plus an "Open in Spaarke" link to the document record, when `ORG_URL` is set) or
 * the error. See `quickSaveDialog.ts` for the host-support paths and the `event.completed()` lifetime.
 *
 * Never opens the task pane. `event.completed()` is called exactly once on every path — when the save is over and the
 * dialog is closed (or capped), or at once with no dialog — asserted by `__tests__/commands.test.ts`, not by inspection.
 */
async function quickSave(event: Office.AddinCommands.Event): Promise<void> {
  const dialog = await openQuickSaveDialog({
    onComplete: () => event.completed(),
    orgUrl: process.env.ORG_URL,
    pagePath: '/word/commands-notify.html',
  });
  let stage: QuickSaveStage = 'connect';
  try {
    const adapter = await ensureBootstrapped();

    stage = 'read';
    const title = await adapter.getSubject();

    let identity: DocumentIdentityOutcome;
    try {
      identity = await resolveOpenDocumentIdentity(adapter);
    } catch (error) {
      identity = {
        kind: 'error',
        message: (error as { message?: string } | null)?.message || 'identity resolution failed',
      };
    }
    const unsavable = describeUnsavableIdentity(identity);
    if (unsavable) {
      await dialog.show({ message: unsavable, status: 'error' });
      return;
    }

    const target: QuickSaveTarget =
      identity.kind === 'resolved' ? { mode: 'version', existingDocumentId: identity.documentId } : { mode: 'create' };

    const contentBase64 = arrayBufferToBase64(await adapter.getDocumentContent({ format: 'ooxml' }));

    stage = 'save';
    const idempotencyKey = await computeQuickSaveIdempotencyKey({
      kind: 'document',
      title,
      contentBase64,
      ...(target.mode === 'version' ? { existingDocumentId: target.existingDocumentId } : {}),
    });
    const request = buildDocumentSaveRequest({ title }, contentBase64, idempotencyKey, target);
    const response = await apiClient.post<QuickSaveResponse>('/api/office/save', request);

    const saved = await readSavedDocument(
      response?.statusUrl,
      target.mode === 'version' ? target.existingDocumentId : null
    );
    if (saved.kind === 'failed') {
      await dialog.show({
        message: `Spaarke could not finish saving this document: ${saved.reason}.`,
        status: 'error',
      });
      return;
    }
    if (saved.kind === 'saved') {
      await writeIdentityStampAfterSave(adapter, saved.document.documentId);
    }

    await dialog.show({
      message: describeQuickSaveSuccess({
        target,
        requestedFileName: request.document.fileName,
        documentLabel: identity.kind === 'resolved' ? identity.documentName || identity.fileName || null : null,
        saved: saved.kind === 'saved' ? saved.document : null,
        duplicate: response?.duplicate === true,
      }),
      status: 'success',
      linkUrl:
        saved.kind === 'saved'
          ? buildQuickSaveRecordLink(process.env.ORG_URL, configuredSpaarkeAppName(), saved.document.documentId)
          : null,
    });
  } catch (error) {
    console.error('Quick save failed:', error);
    await dialog.show({ message: describeQuickSaveFailure(error, stage), status: 'error' });
  } finally {
    dialog.finish();
  }
}

/** Whether this host can open a browser window from a command (`OpenBrowserWindowApi` 1.1) — a capability, not a host check. */
function canOpenBrowserWindow(): boolean {
  try {
    return (
      Office.context.requirements.isSetSupported('OpenBrowserWindowApi', '1.1') &&
      typeof Office.context.ui?.openBrowserWindow === 'function'
    );
  } catch {
    return false;
  }
}

/**
 * Open Spaarke (task 089, UAT-10 — replaces the ribbon's Share, owner 2026-10-03): opens the Spaarke app at its
 * Workspace, the Console — `{ORG_URL}/main.aspx?appname={SPAARKE_APP_NAME}&pagetype=webresource&webresourceName=sprk_spaarkeai`
 * (`buildOpenSpaarkeUrl`; app resolution: `notes/042-uat-round3-2026-10-03.md` §3a).
 *
 * Built from the build settings only — no sign-in, token or BFF call is involved, so nothing is bootstrapped. Opened
 * with `Office.context.ui.openBrowserWindow` where the host supports it, else `window.open` (the same capability-
 * decided opener the pane uses for an https URL, `openFileUrl`). Without `ORG_URL` or an app name the command opens
 * nothing and says it is not set up — the pane's rule for an unset `ORG_URL`, never a guessed link.
 */
function openSpaarke(event: Office.AddinCommands.Event): void {
  try {
    const url = buildOpenSpaarkeUrl(process.env.ORG_URL, configuredSpaarkeAppName());
    if (!url) {
      notify(
        "Open Spaarke isn't set up for this add-in: its Spaarke address or app name is missing. Ask your administrator.",
        'error'
      );
      return;
    }

    const result = openFileUrl(url, canOpenBrowserWindow());
    if (!result.opened) {
      notify(`Couldn't open Spaarke: ${result.reason ?? 'the window did not open'}`, 'error');
    }
  } catch (error) {
    console.error('Open Spaarke failed:', error);
    const message = (error as { message?: string } | null)?.message;
    notify(`Couldn't open Spaarke${message ? ` (${message})` : ''}.`, 'error');
  } finally {
    event.completed();
  }
}

// Initialize and register commands
Office.onReady(() => {
  window.showTaskPane = showTaskPane;
  window.quickSave = quickSave;
  window.openSpaarke = openSpaarke;

  // Unified (JSON) manifest executeFunction registration — the manifest's `quickSave` / `openSpaarke` actions
  // invoke these functions (mirrors outlook/commands/index.ts's convention). The XML manifest's
  // <FunctionName> values resolve through the same registrations / window globals.
  Office.actions?.associate?.('quickSave', quickSave);
  Office.actions?.associate?.('openSpaarke', openSpaarke);

  // Task 078: in the COMBINED Outlook + Word package, Word's quick-save action is `quickSaveDocument`,
  // because one extension cannot hold two actions named `quickSave` and Outlook's keeps its id (it is the
  // live app). `quickSave` above stays registered for the Word XML manifest, the fallback for Word builds
  // older than 2501. Rename map: WORD_FUNCTION_RENAMES in packaging/mergeUnifiedManifest.js.
  Office.actions?.associate?.('quickSaveDocument', quickSave);
});

export { showTaskPane, quickSave, openSpaarke };
