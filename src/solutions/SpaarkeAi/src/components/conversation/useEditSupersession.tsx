/**
 * useEditSupersession — FR-17 undo/replace of a prior AI-applied redline via LEDGER SUPERSESSION
 * (spaarkeai-compose-r2 task 034; design §2.3, spec FR-17, HANDOFF §1 items 3 & 5).
 *
 * The Assistant's "undo that / try another approach" affordance is NOT a client-side DOM undo
 * (ADR-040). Both operations are DURABLE LEDGER WRITES:
 *
 *   - "undo that"          → a RETRACTION: POST the supersession seam, which appends a NEW
 *                            superseding `compose` SessionOutput (empty payload) that becomes the
 *                            highest-turn compose entry for the binding. The Workspace re-materializes
 *                            from CURRENT ledger state → the prior redline disappears. Survives a
 *                            refresh because the retraction is the durable head (HANDOFF §1 item 5).
 *   - "try another approach" → the SAME retraction, CHAINED to a fresh Draft-Alternative dispatch
 *                            (the shipped dispatchConsumer seam). The retraction removes the prior
 *                            redline immediately; the fresh dispatch writes a new proposal at an even
 *                            higher turn → the Workspace re-materializes the fresh suggestion. If the
 *                            fresh proposal is ambiguous, the editor surfaces the FR-19 error (the
 *                            prior is already gone).
 *
 * ── Re-materialization reuse (ADR-030 / §11) ──────────────────────────────────────────────────
 * This hook does NOT re-implement materialization. After a supersession it dispatches the EXISTING
 * Flow-5 `workspace.compose_assistant_insert` signal (via {@link buildComposeApplyEvent}) REFERENCING
 * the current ledger key — never the payload (ADR-030 carries a ledger reference, not source-of-truth
 * content). ComposeWorkspace's shipped receiver re-reads the ledger and drives task 033's
 * `usePendingRedline`, whose per-binding supersession logic strips the prior marks. The fresh-dispatch
 * re-materialize is likewise the dispatchConsumer apply leg ConversationPane already owns.
 *
 * ── §11 justification ─────────────────────────────────────────────────────────────────────────
 * (1) Existing — no supersession-issuance path exists; FR-16 (task 033) MATERIALIZES a draft but does
 *     not RETRACT one. (2) Extension — issuing a superseding SessionOutput + triggering re-render is
 *     distinct from initial materialization; this reuses FR-16's re-render path rather than duplicating
 *     it. (3) Cost-of-doing-nothing — without a durable ledger supersession, "undo that / try another
 *     approach" would be a client DOM undo that is not refresh-durable and loses provenance — exactly
 *     what FR-17's ledger-durable bar forbids.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs (SupersedeComposeOutputAsync — the durable write)
 * @see ./composeApplyLeg.ts (buildComposeApplyEvent — the Flow-5 re-materialize signal, reused)
 * @see @spaarke/compose-components usePendingRedline.ts (task 033 — the re-render this triggers)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  Tooltip,
} from "@fluentui/react-components";
import { ArrowUndoRegular, ArrowSyncRegular, DismissRegular } from "@fluentui/react-icons";
// Deep-import the type (not the barrel) so this Assistant-pane module never transitively pulls the
// TipTap editor widgets — mirrors ConversationPane.tsx / composeApplyLeg.ts import rationale.
import type { ComposeAssistantToWorkspaceFlow } from "@spaarke/compose-components/types/compose-contracts";
import { buildComposeApplyEvent } from "./composeApplyLeg";
import type { ComposeActionRequest } from "./useSerialActionQueue";

/** The most recent AI-applied compose edit — the target of "undo that / try another approach". */
export interface AppliedComposeEdit {
  /** Addressable ledger key `{bindingId}@t{n}` of the applied compose output (the retraction target). */
  readonly ledgerRef: string;
  /** `sprk_playbookconsumer` Binding id that produced the edit. */
  readonly bindingId: string;
  /** The originating dispatch request — replayed verbatim for "try another approach". */
  readonly request: ComposeActionRequest;
  /**
   * DEF-09: the DOCUMENT session the edit's ledger entry lives in (from the edit
   * action's `documentSessionId`). The supersession retraction MUST target THIS
   * session so undo/replace hits the same ledger the redline materialized from —
   * not the chat session. Falls back to the chat session (`getSessionId()`) when
   * absent (older/informational edits).
   */
  readonly sessionId?: string;
}

/** Dependencies the host (ConversationPane) supplies. */
export interface UseEditSupersessionDeps {
  /** BFF base URL (host only). */
  readonly bffBaseUrl: string;
  /** Stable getter for the active ChatSession id. */
  readonly getSessionId: () => string | null;
  /** `@spaarke/auth` fetch (ADR-028). */
  readonly authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * Dispatch a Flow-5 apply signal on the `workspace` channel (ADR-030). ConversationPane wires this
   * to `(e) => dispatch("workspace", e)`; the event references a ledger entry, never a payload.
   */
  readonly dispatchApply: (event: ComposeAssistantToWorkspaceFlow) => void;
}

export interface UseEditSupersessionResult {
  /** The current undo/replace target, or null (drives the affordance's visibility). */
  readonly lastEdit: AppliedComposeEdit | null;
  /** Record that a compose edit was just applied — ConversationPane calls this from its apply leg. */
  readonly trackAppliedEdit: (edit: AppliedComposeEdit) => void;
  /**
   * DEF-12 — clear the tracked edit WITHOUT a ledger write. Called after an ACCEPT (the redline was
   * committed in the document via usePendingRedline.accept, so there is nothing to retract) so the
   * per-message controls stop rendering (`lastEdit` → null). Undo/Try-another still perform their
   * durable ledger supersessions; this is the accept-path counterpart that just drops the target.
   */
  readonly clearTrackedEdit: () => void;
  /** "undo that" — retract the last edit as a durable ledger supersession, then re-materialize. */
  readonly undo: () => Promise<void>;
  /**
   * "try another approach" — retract the last edit, then re-run its Draft-Alternative dispatch for a
   * fresh proposal. `reDispatch` is the host's serial dispatchConsumer (passed at call time to avoid a
   * definition cycle; its own apply leg re-materializes + re-tracks the fresh edit).
   */
  readonly tryAnother: (reDispatch: (request: ComposeActionRequest) => Promise<unknown>) => Promise<void>;
  /** True while a supersession request is in flight (disables the affordance buttons). */
  readonly busy: boolean;
  /** User-safe error message from a failed supersession, or null. */
  readonly error: string | null;
  /** Clear the current error banner. */
  readonly clearError: () => void;
}

interface SupersedeResult {
  /** Ledger key to re-materialize the CURRENT state from (the new retraction, or the existing head). */
  readonly key: string;
  /** `"superseded"` (a new retraction was written) or `"noop"` (already superseded — idempotent). */
  readonly outcome: string;
}

/**
 * FR-17 hook. Owns the "last applied edit" target and the two durable-supersession operations.
 * Stateless with respect to the editor — all re-rendering flows through the reused Flow-5 apply
 * signal + task 033's `usePendingRedline` (ADR-040 render-follows-store).
 */
export function useEditSupersession(deps: UseEditSupersessionDeps): UseEditSupersessionResult {
  const { bffBaseUrl, getSessionId, authenticatedFetch, dispatchApply } = deps;

  const [lastEdit, setLastEdit] = React.useState<AppliedComposeEdit | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const clearError = React.useCallback(() => setError(null), []);

  const trackAppliedEdit = React.useCallback((edit: AppliedComposeEdit) => {
    setLastEdit(edit);
    setError(null);
  }, []);

  const clearTrackedEdit = React.useCallback(() => {
    setLastEdit(null);
    setError(null);
  }, []);

  /**
   * POST the durable supersession seam. Returns the key to re-materialize CURRENT ledger state from,
   * or null on failure. The retraction is a NEW superseding `compose` SessionOutput (ADR-040) — never
   * a client-only mutation.
   */
  const supersede = React.useCallback(
    async (supersedesRef: string, sessionIdOverride?: string): Promise<SupersedeResult | null> => {
      // DEF-09: retract in the SAME session the edit lives in (the document session for
      // a compose edit), not the chat session.
      const sessionId = sessionIdOverride ?? getSessionId();
      if (!sessionId || !bffBaseUrl) return null;
      try {
        const url = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/compose-outputs/supersede`;
        const response = await authenticatedFetch(url, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ supersedesRef }),
        });
        if (!response.ok) return null;
        const data = (await response.json()) as { key?: string; outcome?: string };
        if (!data?.key) return null;
        return { key: data.key, outcome: data.outcome ?? "superseded" };
      } catch {
        return null;
      }
    },
    [bffBaseUrl, getSessionId, authenticatedFetch]
  );

  const undo = React.useCallback(async (): Promise<void> => {
    const edit = lastEdit;
    if (!edit) return;
    // DEF-09: operate on the edit's own (document) session, falling back to chat.
    const sessionId = edit.sessionId ?? getSessionId();
    if (!sessionId) return;

    setBusy(true);
    setError(null);
    try {
      const result = await supersede(edit.ledgerRef, sessionId);
      if (!result) {
        setError("Couldn't undo the last edit. Please try again.");
        return;
      }
      // Re-materialize from CURRENT ledger state via the reused Flow-5 signal (references the ledger
      // key, never the payload — ADR-030). The retraction (or existing head on a no-op) re-reads as
      // empty → task 033's usePendingRedline strips the prior redline. NOT a DOM undo.
      dispatchApply(buildComposeApplyEvent(result.key, edit.bindingId, sessionId));
      setLastEdit(null);
    } finally {
      setBusy(false);
    }
  }, [lastEdit, getSessionId, supersede, dispatchApply]);

  const tryAnother = React.useCallback(
    async (reDispatch: (request: ComposeActionRequest) => Promise<unknown>): Promise<void> => {
      const edit = lastEdit;
      if (!edit) return;
      // DEF-09: operate on the edit's own (document) session, falling back to chat.
      const sessionId = edit.sessionId ?? getSessionId();
      if (!sessionId) return;

      setBusy(true);
      setError(null);
      try {
        // 1. Retract the prior redline (durable supersession) and re-materialize it away NOW, so the
        //    old suggestion is gone while the fresh proposal streams.
        const result = await supersede(edit.ledgerRef, sessionId);
        if (result) {
          dispatchApply(buildComposeApplyEvent(result.key, edit.bindingId, sessionId));
        }
        // 2. Chain a fresh Draft-Alternative dispatch. Its own apply leg (ConversationPane) writes a
        //    new proposal at a higher turn + re-materializes it + re-tracks the new edit. An ambiguous
        //    fresh proposal surfaces the FR-19 error in the editor (the prior is already retracted).
        try {
          await reDispatch(edit.request);
        } catch {
          setError("Couldn't draft another approach. The previous suggestion was removed — please try again.");
        }
      } finally {
        setBusy(false);
      }
    },
    [lastEdit, getSessionId, supersede, dispatchApply]
  );

  return { lastEdit, trackAppliedEdit, clearTrackedEdit, undo, tryAnother, busy, error, clearError };
}

// ---------------------------------------------------------------------------
// EditSupersessionBar — the Assistant "undo that / try another approach" affordance
// (Fluent v9, dark-mode-correct via semantic tokens — ADR-021).
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
  },
  actions: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalS,
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    marginInlineEnd: tokens.spacingHorizontalXS,
  },
});

export interface EditSupersessionBarProps {
  /** Rendered only when a `lastEdit` target exists. */
  readonly lastEdit: AppliedComposeEdit | null;
  readonly busy: boolean;
  readonly error: string | null;
  readonly onUndo: () => void;
  readonly onTryAnother: () => void;
  readonly onDismissError: () => void;
}

/**
 * Compact affordance shown after an AI redline is applied. Two intents — retract ("Undo that") and
 * retract+redraft ("Try another approach") — both routed to durable ledger supersessions. Fluent v9
 * primitives are theme-aware under the shell's FluentProvider (dark-mode-correct, ADR-021).
 */
export function EditSupersessionBar(props: EditSupersessionBarProps): React.JSX.Element | null {
  const { lastEdit, busy, error, onUndo, onTryAnother, onDismissError } = props;
  const styles = useStyles();

  if (!lastEdit && !error) return null;

  return (
    <div className={styles.root} role="group" aria-label="Undo or replace the last AI edit">
      {lastEdit && (
        <div className={styles.actions}>
          <span className={styles.hint}>AI edit applied</span>
          <Tooltip content="Remove the last AI redline (returns the document to its prior state)" relationship="description">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowUndoRegular />}
              disabled={busy}
              onClick={onUndo}
            >
              Undo that
            </Button>
          </Tooltip>
          <Tooltip content="Remove the last AI redline and draft a fresh alternative" relationship="description">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowSyncRegular />}
              disabled={busy}
              onClick={onTryAnother}
            >
              Try another approach
            </Button>
          </Tooltip>
        </div>
      )}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
          <MessageBarActions>
            <Button
              appearance="transparent"
              size="small"
              icon={<DismissRegular />}
              aria-label="Dismiss"
              onClick={onDismissError}
            />
          </MessageBarActions>
        </MessageBar>
      )}
    </div>
  );
}

export default useEditSupersession;
