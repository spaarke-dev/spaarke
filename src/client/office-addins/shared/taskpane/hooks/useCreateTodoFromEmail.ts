import { useCallback, useState } from 'react';
import { findCommunicationByMessageId, type CommunicationLookupResult } from '../services/communicationLookupService';
import { openCreateTodoWizard, type BuildCreateTodoLaunchUrlInput } from '../services/createTodoLauncher';

/**
 * useCreateTodoFromEmail.ts
 *
 * Backs the FR-27 "Create To Do" Outlook ribbon click flow. Encapsulates the
 * sequential decisions documented in
 * `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md`
 * §3 (Outlook entry-point):
 *
 *   1. Read the current email's `internetMessageId` + `subject` from the
 *      injected adapter.
 *   2. Look up an existing `sprk_communication` via the BFF
 *      (`findCommunicationByMessageId`).
 *      - 200 → email already saved → step 4 with the found id + subject.
 *      - 404 / null → email not saved → step 3 (save flow).
 *      - Other error → surface to UI; don't open the wizard.
 *   3. Invoke the caller-supplied `saveEmailToSpaarke` callback. The host is
 *      responsible for running the existing save flow UI (Save view + entity
 *      picker + status polling). When complete, the callback MUST return the
 *      `communicationId` of the newly-created `sprk_communication`. Either:
 *        a. The save endpoint returns it directly (preferred — requires BFF
 *           change), OR
 *        b. The host re-runs `findCommunicationByMessageId` after the job
 *           completes (current state).
 *   4. Build the launch URL via `openCreateTodoWizard` and open in a new
 *      browser window with `initialRegarding = { entityType: 'sprk_communication',
 *      recordId: <id>, recordName: <subject> }`.
 *
 * The hook itself does NOT render any UI — callers wire it to a Fluent v9
 * button + status MessageBar. The state machine + busy flag are returned so
 * callers can disable buttons during the in-flight save.
 *
 * @see projects/smart-todo-decoupling-r3/spec.md FR-27 / FR-16
 * @see projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md
 */

/**
 * Adapter interface for the current Outlook email. Allows tests to inject a
 * pure mock without depending on Office.js. In production, the Outlook
 * taskpane wires this to `OutlookAdapter.getItemId` + `getSubject` + an
 * `internetMessageId` getter (the OutlookAdapter exposes itemId; for the
 * RFC-5322 message id the adapter must read `Office.context.mailbox.item.internetMessageId`).
 */
export interface CurrentEmailReader {
  /** Returns the email's RFC-5322 internet message id (e.g. `<abc@host>`). */
  getInternetMessageId: () => Promise<string>;
  /** Returns the email's subject (displayed in the AssociateToStep card). */
  getSubject: () => Promise<string>;
}

/**
 * Caller-supplied callback that runs the existing email-save flow and
 * resolves to the `sprk_communicationid` of the newly-saved email.
 *
 * The hook intentionally does NOT own the save UI — that lives in the
 * Outlook taskpane's SaveView. The host wires the two together by passing a
 * thunk that opens the SaveView, awaits completion, then re-looks-up.
 */
export type SaveEmailToSpaarkeFn = () => Promise<CommunicationLookupResult | null>;

/**
 * Discriminated-union state machine for the click flow. Surfaced to the UI
 * so the host can render the right MessageBar / Spinner / Button state.
 */
export type CreateTodoFlowState =
  | { kind: 'idle' }
  | { kind: 'looking-up' }
  | { kind: 'saving' }
  | { kind: 'launching'; communication: CommunicationLookupResult }
  | { kind: 'opened'; communication: CommunicationLookupResult }
  | { kind: 'error'; message: string };

/**
 * Hook options.
 */
export interface UseCreateTodoFromEmailOptions {
  /** Reader for the current email's internetMessageId + subject. */
  emailReader: CurrentEmailReader;
  /**
   * Caller-supplied save flow. Invoked when the email is not yet saved.
   * Returns the new `sprk_communication` triple or `null` if the user
   * cancelled / save failed.
   */
  saveEmailToSpaarke: SaveEmailToSpaarkeFn;
  /**
   * SmartTodo Code Page base URL. Typically read from `SMARTTODO_CODEPAGE_URL`
   * env var by the host. No hardcoded URLs allowed (CLAUDE.md §16).
   */
  codePageBaseUrl: string;
  /**
   * Optional override for the `window.open` call (test seam). Defaults to
   * the global `window.open` bound to the launcher service's standard
   * features string.
   */
  windowOpen?: (url: string, target: string, features: string) => Window | null;
}

/**
 * Hook result.
 */
export interface UseCreateTodoFromEmailResult {
  /** Current state — drives the UI rendering. */
  state: CreateTodoFlowState;
  /** True while the hook is doing async work (looking up or saving). */
  isBusy: boolean;
  /** Begin the click flow. Idempotent while busy (returns immediately). */
  start: () => Promise<void>;
  /** Reset state to idle after the user dismisses an error / success. */
  reset: () => void;
}

/**
 * Hook backing the Outlook ribbon "Create To Do" click flow.
 *
 * Usage:
 *
 * ```tsx
 * const { state, isBusy, start, reset } = useCreateTodoFromEmail({
 *   emailReader: outlookEmailReader,
 *   saveEmailToSpaarke: hostSaveFlow,
 *   codePageBaseUrl: process.env.SMARTTODO_CODEPAGE_URL ?? '',
 * });
 * <Button disabled={isBusy} onClick={() => void start()}>Create To Do</Button>
 * ```
 */
export function useCreateTodoFromEmail(options: UseCreateTodoFromEmailOptions): UseCreateTodoFromEmailResult {
  const { emailReader, saveEmailToSpaarke, codePageBaseUrl, windowOpen } = options;

  const [state, setState] = useState<CreateTodoFlowState>({ kind: 'idle' });

  const isBusy = state.kind === 'looking-up' || state.kind === 'saving' || state.kind === 'launching';

  const reset = useCallback(() => setState({ kind: 'idle' }), []);

  const launch = useCallback(
    (communication: CommunicationLookupResult): void => {
      setState({ kind: 'launching', communication });
      try {
        const launchInput: BuildCreateTodoLaunchUrlInput = {
          codePageBaseUrl,
          communicationId: communication.communicationId,
          recordName: communication.subject,
          // entityType defaults to 'sprk_communication' per FR-27 — explicit for clarity.
          entityType: 'sprk_communication',
        };
        const opened = windowOpen ? openCreateTodoWizard(launchInput, windowOpen) : openCreateTodoWizard(launchInput);
        if (!opened) {
          // Popup blocked — surface a clear message.
          setState({
            kind: 'error',
            message: 'The browser blocked the To Do window. Please allow popups for this site and try again.',
          });
          return;
        }
        setState({ kind: 'opened', communication });
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to launch the Create To Do wizard.';
        setState({ kind: 'error', message });
      }
    },
    [codePageBaseUrl, windowOpen]
  );

  const start = useCallback(async (): Promise<void> => {
    // Guard against double-clicks while busy.
    if (state.kind === 'looking-up' || state.kind === 'saving' || state.kind === 'launching') {
      return;
    }

    setState({ kind: 'looking-up' });

    let internetMessageId: string;
    try {
      internetMessageId = await emailReader.getInternetMessageId();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to read the current email.';
      setState({ kind: 'error', message });
      return;
    }

    // Email subject is read regardless of save path — it's the
    // AssociateToStep selected-record card label.
    let subject = '';
    try {
      subject = await emailReader.getSubject();
    } catch {
      // Non-fatal: fall through with an empty subject. The wizard tolerates
      // an empty recordName (renders a generic "selected" card).
    }

    // Step 1: look up existing communication
    let existing: CommunicationLookupResult | null;
    try {
      existing = await findCommunicationByMessageId(internetMessageId);
    } catch (err) {
      const message =
        err instanceof Error ? err.message : 'Failed to look up whether this email is already saved to Spaarke.';
      setState({ kind: 'error', message });
      return;
    }

    if (existing) {
      // Email already saved — go straight to launch. Use the freshly-read
      // subject (BFF subject may be stale if user renamed locally).
      launch({ ...existing, subject: subject || existing.subject });
      return;
    }

    // Step 2: email not saved → invoke the host save flow
    setState({ kind: 'saving' });

    let saved: CommunicationLookupResult | null;
    try {
      saved = await saveEmailToSpaarke();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to save the email to Spaarke.';
      setState({ kind: 'error', message });
      return;
    }

    if (!saved) {
      // Save was cancelled or returned no communicationId.
      setState({
        kind: 'error',
        message:
          'The email was not saved to Spaarke, so the To Do cannot be linked. Try again or save the email first.',
      });
      return;
    }

    // Step 3: launch the wizard with the (now-saved) communication.
    launch({ ...saved, subject: subject || saved.subject });
  }, [state.kind, emailReader, saveEmailToSpaarke, launch]);

  return { state, isBusy, start, reset };
}
