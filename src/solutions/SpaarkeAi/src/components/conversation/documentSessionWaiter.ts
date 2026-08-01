/**
 * documentSessionWaiter — DEF-09 review-dispatch routing seam (ai-advanced-capabilities-agreements-r1
 * task 031, spec FR-16(c)).
 *
 * THE PROBLEM: the agreement-review Binding's `sprk_disposition` was flipped Informational -> Compose
 * (task 030), so its output is now a `compose`-disposition SessionOutput that `ComposeWorkspace`'s
 * FR-04 refresh-durability effect re-materializes from `GET .../compose-outputs` on THAT SAME session
 * (`state.sessionId`). But the review dispatches via `chips.dispatchBinding` (`ConversationPane.
 * handleReviewNda` / `useAgreementReviewGate.dispatchReview` / `.dispatchBothSequentially`), which is
 * bound to the CHAT session — the WRITE and the redline-materialize READ must coincide (the shipped
 * DEF-09 precedent for the Compose EDIT toolbar, `dispatchComposeAction`'s `sessionIdOverride`).
 *
 * WHY A SIMPLE "reuse getSessionId()" DOESN'T SUFFICE: for a freshly chat-uploaded file, `mountFileInCompose`
 * seeds `compose.upload.sessionId = chatSessionId`, and `ComposeWorkspace`'s `state.sessionId` for that
 * upload-mount door literally equals that same value — so the chat session and the document session
 * DO coincide by construction for that one case. They do NOT coincide when the reviewed file was
 * Browse-mounted (its `documentSessionId` is a client-minted, chat-unrelated id — `mintDocumentSessionId()`
 * in ComposeWorkspace.tsx) and then ingested into the chat session for the Assistant to see it — the
 * review would then write to the chat session while ComposeWorkspace reads from the browse-minted one.
 * The robust value is `state.sessionId` AS ESTABLISHED BY ComposeWorkspace, surfaced to the host via the
 * EXISTING `registerComposeActiveDocument` reactive callback (the SAME conduit the "revise this document"
 * flow already uses for its own `activeComposeDocSessionId` backfill — DEF-11 TEXT-path close).
 *
 * TIMING: `mountFileInCompose` (fires the `widget_load` seed) and the review dispatch happen in the SAME
 * synchronous call — the document-session backfill is virtually never ready yet on a fresh mount (it
 * requires ComposeWorkspace to render, load bytes, and call back). This module lets the caller AWAIT
 * that backfill, correlated by the REVIEWED file's own session-file id (never a different file's stale
 * value), with a bounded timeout that degrades to `null` — never hangs, never throws — so a genuinely
 * never-mounting Compose surface (no WorkspacePane / a failed mount) still lets the review dispatch
 * proceed on the bound chat session, i.e. exactly the pre-031 behavior, not a regression.
 *
 * Pulled out of the ~20-hook `ConversationPane` (mirrors `composeApplyLeg.ts`'s extraction rationale) so
 * the waiter/timeout state machine is directly unit-testable without mounting the full pane.
 */

/** Default bound for {@link DocumentSessionWaiter.awaitDocumentSessionId} — see module header. */
export const DEFAULT_DOCUMENT_SESSION_WAIT_TIMEOUT_MS = 8000;

export interface DocumentSessionWaiter {
  /**
   * Resolve the document session id for `fileId`. Resolves IMMEDIATELY if already known (a prior
   * {@link notify} call for this exact fileId — e.g. an already-open/ingested document, or a repeat
   * review of a file whose gate already resolved once). Otherwise waits up to `timeoutMs` for a
   * matching `notify`, then resolves `null` — never rejects, never hangs past the bound.
   */
  awaitDocumentSessionId(fileId: string, timeoutMs?: number): Promise<string | null>;
  /**
   * Called by the host's `registerComposeActiveDocument` handler whenever a document registers
   * (fresh mount OR an already-open tab's re-registration/tab_change) — resolves any in-flight
   * waiter for THIS fileId and remembers the value for a subsequent immediate resolve. A no-op when
   * either argument is falsy/empty (a pointer-only registration with no session yet).
   */
  notify(fileId: string, documentSessionId: string | undefined): void;
  /** Session reset (`handleSessionCreated`) — drops all known values and in-flight waiters. */
  reset(): void;
}

/**
 * Create a fresh waiter. One instance per `ConversationPane` mount (session-scoped via {@link
 * DocumentSessionWaiter.reset}, called on a fresh chat session — a prior session's file ids/session
 * ids must never leak into a new session's resolution).
 */
export function createDocumentSessionWaiter(): DocumentSessionWaiter {
  const known = new Map<string, string>();
  const waiters = new Map<string, Array<(id: string | null) => void>>();

  function notify(fileId: string, documentSessionId: string | undefined): void {
    if (!fileId || !documentSessionId) return;
    known.set(fileId, documentSessionId);
    const pending = waiters.get(fileId);
    if (!pending || pending.length === 0) return;
    waiters.delete(fileId);
    for (const settle of pending) settle(documentSessionId);
  }

  function awaitDocumentSessionId(
    fileId: string,
    timeoutMs: number = DEFAULT_DOCUMENT_SESSION_WAIT_TIMEOUT_MS
  ): Promise<string | null> {
    const existing = known.get(fileId);
    if (existing) return Promise.resolve(existing);

    return new Promise<string | null>((resolve) => {
      let settled = false;
      const settleOnce = (value: string | null): void => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        const pending = waiters.get(fileId);
        if (pending) {
          const idx = pending.indexOf(settleOnce);
          if (idx >= 0) pending.splice(idx, 1);
          if (pending.length === 0) waiters.delete(fileId);
        }
        resolve(value);
      };
      const timer = setTimeout(() => settleOnce(null), timeoutMs);
      const list = waiters.get(fileId) ?? [];
      list.push(settleOnce);
      waiters.set(fileId, list);
    });
  }

  function reset(): void {
    known.clear();
    waiters.clear();
  }

  return { awaitDocumentSessionId, notify, reset };
}
