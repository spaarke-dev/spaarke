/**
 * composeApplyLeg — the FR-13 draft-alternative APPLY leg (spaarkeai-compose-r2
 * task 046, design.md §3 Flow 5 + §7.2).
 *
 * After a Compose AI action dispatches through the shipped Click-path seam
 * (`dispatchConsumer` → `POST /api/ai/chat/sessions/{id}/dispatch`), a
 * `compose-draft-alternative` action writes a `compose`-disposition
 * `SessionOutput` to the session ledger (OutputRouter pass-through; ADR-040
 * store-before-render). The Assistant (`ConversationPane`) then emits the
 * EXISTING `workspace.compose_assistant_insert` discriminant REFERENCING that
 * stored ledger entry (`ledgerRef = {bindingId}@t{n}`) — NEVER the edit payload
 * (design §7.2). `ComposeWorkspace`'s shipped Flow-5 receiver re-materializes
 * the pending redline FROM the ledger at that `ledgerRef`.
 *
 * This module holds the PURE pieces of that leg (ledger-key resolution + event
 * construction) so they are unit-testable without mounting the 20-hook
 * `ConversationPane`. The network read + `dispatch(...)` call stay in
 * `ConversationPane` (which owns `authenticatedFetch` / `getSessionId` /
 * `dispatch`).
 *
 * Contract note (ZERO new discriminants — ADR-030): the emitted event is the
 * R1-shipped `compose_assistant_insert` (`compose-contracts.ts` Flow 5, task
 * 016 added its `ledgerRef?` field). No new PaneEventBus discriminant is
 * introduced. The retracted `compose_edit_apply_request` name (Spike 0) is NOT
 * used.
 *
 * @see src/client/shared/Spaarke.Compose.Components/src/types/compose-contracts.ts
 *      (ComposeAssistantToWorkspaceFlow — Flow 5, with `ledgerRef?`)
 * @see src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx
 *      (the shipped Flow-5 receiver → materializeComposeDraftFromLedger)
 */

// Deep-import the type (not the barrel) so this module never pulls the TipTap
// editor widgets — matches the deep-import rationale in ConversationPane.tsx.
import type { ComposeAssistantToWorkspaceFlow } from "@spaarke/compose-components/types/compose-contracts";

/**
 * Minimal projection of a `compose`-disposition ledger output as returned by the
 * session-ledger read endpoint (`GET /api/ai/chat/sessions/{id}/compose-outputs`).
 * Mirrors `ComposeLedgerOutput` (ComposeWorkspace) but only the identity fields
 * the apply leg needs — the opaque Compose-owned `payload` stays server/ledger-side.
 */
export interface ComposeLedgerOutputLite {
  /** Addressable ledger key `{bindingId}@t{n}` — the Flow-5 `ledgerRef`. */
  readonly key: string;
  /** `sprk_playbookconsumer` Binding id that produced the output. */
  readonly bindingId: string;
  /** 1-based session turn (supersession ordering — highest turn = current). */
  readonly turn: number;
  /** Rendering-contract member; always `"compose"` for compose outputs. */
  readonly disposition: string;
}

/**
 * Resolve the CURRENT (highest-turn) `compose`-disposition ledger key for a
 * binding, or `null` when the binding wrote no compose output.
 *
 * Gating on `bindingId` is what makes this precise: an informational action
 * (explain / compare / summarize-changes / defined-terms) has no matching
 * compose output → returns `null` → no apply leg is emitted. Tolerant of a
 * non-array / malformed input (returns `null`), never throws — the apply leg
 * must never break the dispatch it follows.
 *
 * agreements-r1 task 031(d) gating note (FR-16(d)): as of ai-advanced-capabilities-agreements-r1
 * task 030, the agreement-review Binding's `sprk_disposition` is ALSO `compose` (flipped from
 * Informational) — this function's own bindingId gate no longer disambiguates "compose disposition"
 * from "a redline-shaped edit" (that stale claim predates the flip). What DOES keep a review's
 * findings out of this apply leg is a SEPARATE, structural invariant, not a payload-shape check
 * here: the review dispatches via `useConsumerChips.dispatchBinding` (chat-chip Click path), never
 * via `ConversationPane.dispatchComposeAction` (the compose-EDITOR-toolbar path this module's
 * `emitComposeApplyLeg` caller is exclusively wired to). This function is therefore never invoked
 * with the review's bindingId in practice — verified by trace (`ConversationPane.dispatchComposeAction`
 * has exactly two callers, both compose-editor-toolbar actions: `dispatchReviseDocument` and the
 * inline AI toolbar's draft-alternative/explain/compare/etc.) and by regression test
 * (`useConsumerChips.surface-launch.test.tsx` "task 031(d) gating" describe block — an NDA-REVIEW-shaped
 * result renders a plain confirmation, never `metadata.composeEdit`). A payload-shape check could not
 * be added HERE even if desired: this function only receives `ComposeLedgerOutputLite`
 * (key/bindingId/turn/disposition) — the actual payload (flaggedSections vs. edits) never reaches it.
 */
export function resolveCurrentComposeLedgerRef(outputs: unknown, bindingId: string): string | null {
  if (!Array.isArray(outputs) || !bindingId) return null;
  let current: ComposeLedgerOutputLite | null = null;
  for (const entry of outputs) {
    if (entry === null || typeof entry !== "object") continue;
    const o = entry as Partial<ComposeLedgerOutputLite>;
    if (o.disposition !== "compose") continue;
    if (o.bindingId !== bindingId) continue;
    if (typeof o.key !== "string" || o.key.length === 0) continue;
    if (typeof o.turn !== "number") continue;
    if (current === null || o.turn > current.turn) {
      current = { key: o.key, bindingId: o.bindingId, turn: o.turn, disposition: o.disposition };
    }
  }
  return current ? current.key : null;
}

/**
 * Build the Flow-5 `workspace.compose_assistant_insert` event REFERENCING the
 * ledger entry (ADR-040 — the event carries a ledger reference, NEVER the edit
 * payload; the Compose workspace re-materializes FROM the stored entry).
 *
 * `contentHtml` is intentionally EMPTY: the R1 Flow-5 shape requires it, but the
 * `ledgerRef` materialize path never reads it — making "reference, not payload"
 * explicit. `documentRef`/`sourceNodeId`/`sourcePlaybookId` are likewise unused
 * by the ledgerRef path; they carry honest minimal values to satisfy the shape.
 */
export function buildComposeApplyEvent(
  ledgerRef: string,
  bindingId: string,
  sessionId: string
): ComposeAssistantToWorkspaceFlow {
  return {
    type: "compose_assistant_insert",
    // The load-bearing field — ComposeWorkspace materializes FROM this stored entry.
    ledgerRef,
    // Required by the R1 Flow-5 shape but NOT read on the ledgerRef path (ADR-040:
    // the payload lives in the ledger, never the event).
    documentRef: { speDriveItemId: "" },
    sourceNodeId: bindingId,
    sourcePlaybookId: "",
    contentHtml: "",
    format: "html",
    insertMode: "insert-at-cursor",
    requireUserConfirm: false,
    sessionId,
    timestamp: new Date().toISOString(),
  };
}
