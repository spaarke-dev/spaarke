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
