# FR-C4 (on-demand full email body) — escalation + options

**Date**: 2026-08-06 · **Status**: 🔔 ESCALATED to owner
**By**: task 042b (opus) — correctly stopped rather than building new agent-tool surface
**FR-C1 status**: ✅ DONE + committed (94955e609). The Assistant now sees an open email's subject/from/date ambiently. This escalation is ONLY about the "summarize the full body on-demand" verb.

---

## The gap
FR-C4 as written: *"'summarize this email' triggers an `eml-render` fetch; the full body is not injected into every turn."* The POML assumed `eml-render` was already an agent tool. It is not — it's a **client HTTP endpoint** (`GET /api/documents/{id}/eml-render`, OBO, for the reading pane). The agent's ONLY document-body mechanism is `DocumentContextService.InjectDocumentContextAsync` = **per-turn context injection** (what FR-C4 says to avoid). There is no on-demand document-body agent tool to wire `emlDocumentId` into.

## Verified facts
- `emlDocumentId` IS a `sprk_document` (the `.eml` archive; resolved by `resolveEmlArchiveDocumentId`). It is now persisted in the email tab's `widgetData` as a fetch handle (excluded from agent-visible state per ADR-015).
- `InjectDocumentContextAsync(documentId)` accepts a `sprk_document` id → fetches via SPE → extracts text → chunks (30K budget). It CAN render an `.eml`.
- The turn's document is `request.DocumentId ?? session.DocumentId` — a per-request `DocumentId` scopes to that turn (session context unchanged if not switched).

## Options

### Option B — reuse per-turn document injection, scoped on-demand (RECOMMENDED)
When the user asks about the active email, the client attaches the email's `emlDocumentId` as **that turn's** `ChatSendMessageRequest.DocumentId` (NOT a session context switch). The existing `InjectDocumentContextAsync` renders the body for that turn only → not every turn. Reuses 100% of shipped document-summarize machinery; no new catalog surface, no new endpoint, no new agent tool (§10/§11 reuse-first; ADR-039 closed-catalog untouched). Trigger can reuse the Phase-D task-038 document-context chip pattern or active-tab awareness. **Work**: verify per-turn (not session-persisted) scoping of `request.DocumentId`; add the client trigger that attaches `emlDocumentId` on an email-summarize intent. Smaller + ADR-clean. §6.5 Path C (pivot to comply with existing architecture — the POML's "eml-render tool" was a mis-assumption).

### Option A — new on-demand agent tool (literal POML)
Add an `eml-render`/read-email-body tool to the ADR-039 closed catalog + route `emlDocumentId` to the agent + have the agent invoke it. Purest "tool-based on-demand," but adds catalog + BFF agent-tool surface (bigger; heavier §10/§11 justification; more deploy risk). Only preferred if we want the body fetch to be an explicit agent-decided tool call rather than a client-attached turn document.

### Option C — defer FR-C4 (ship FR-C1 only now)
FR-C1 (see the email) ships + deploys with D now; FR-C4 (full-body summarize) becomes a scoped follow-on. Fastest to ship the headline value, but leaves "summarize this email" with only the 200-char snippet until the follow-on. Conflicts with the standing "address things now" directive.

## Recommendation
**Option B** — reuse the document-injection path scoped on-demand. Best robustness-per-effort, ADR-compliant, no new closed-catalog surface, and it makes an email behave like a document for the one turn the user asks about it (which is exactly the mental model). Add as task 042c-fr-c4 (client trigger + turn-scoped documentId), deploy with 043.

---

## UPDATE 2026-08-06 — Option B investigation hit a SprkChat wall (2nd escalation)
The opus agent (042c-fr-c4) verified the `documentId` ATTACH is clean (one-shot ref in `handleDecorateOutboundBodyWithRevise`, mirrors the `activeContext`/`modelTierOverride` decorate), and confirmed server turn-scoping (`request.DocumentId` injects for one turn, never persisted to session). **But the SEND TRIGGER blocks it:** a `SendMessage` turn (which runs `DocumentContextService`) is produced ONLY by `SprkChat.handleSend`, and `ISprkChatProps` exposes **no host programmatic-send prop and no composer-prefill prop** (scanned types.ts:805+). The task-038 "Reanalyze" chip flows through `chips.dispatchBinding` (the **dispatch/Action** path), NOT SendMessage — so it can't carry `ChatSendMessageRequest.DocumentId` into `DocumentContextService`. A deterministic click → SendMessage therefore needs a NEW send seam on shared `SprkChat` = spec MUST-NOT ("MUST NOT fork SprkChat") + guardrail (d).

**Companion bug found (real, independent):** the active-tab focus stamp's `emlDocumentId` goes STALE when the user browses emails *within* an active tab — `WorkspaceTabManager.updateTab` fires `_notifyPersistChange` but not `_notifyActiveTabChange` (:515-523). FR-C1's server visibility is fine (reads persisted `widgetData`); only a client chip reading the stamp would target the wrong email. Fix if we build a client chip: re-broadcast `active_widget_changed` (existing ADR-030 event) on email-tab `widgetData` change — client-only, low risk.

### The impossible triangle (pick two)
- deterministic click + on-demand(one turn) → needs a SprkChat send/prefill seam (**B1**).
- no SprkChat change + on-demand → Binding dispatch (**B2**, = Option A dispatch family owner rejected) OR a NL classifier (ADR-039 violation — no).
- no SprkChat change + deterministic → attach emlDocumentId every-turn-while-email-active (violates FR-C4 "not every turn").

### Real choice → B1 vs C
- **B1 (recommended)** — add ONE optional, additive host-send prop pair on `SprkChat` (mirrors the existing `injectLocalMessage`/`onLocalMessageInjected` pattern): non-null slot + not-streaming → SprkChat calls its own `handleSend` once + acks; the one-shot `documentId` still rides the UNCHANGED `onDecorateOutboundBody` seam. Additive/optional (zero change for existing consumers; not a divergent copy → arguably an EXTENSION, not a fork), + the companion focus-stamp fix. Touches shared `@spaarke/ui-components` (version bump). Truest realization of the owner's Option B. **§6.5 Path A exception** on "MUST NOT fork SprkChat" — needs explicit owner sign-off.
- **C** — defer FR-C4; ship FR-C1 (already done + committed 94955e609). Zero shared-lib risk; the summarize-full-body verb becomes a scoped follow-on. "summarize this email" runs on the compact snippet until then.
- **B2 (not recommended)** — chip → dispatch the summarize Binding at `emlDocumentId`. No SprkChat change, but it's the Option A dispatch family the owner did NOT choose, and likely needs the Binding to accept an arbitrary documentId operand (catalog/BFF nuance).
