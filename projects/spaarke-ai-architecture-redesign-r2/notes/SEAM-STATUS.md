# SEAM-STATUS — core-r2 → Compose r2 dependency dashboard

> **Purpose**: the single file Compose r2 polls to know which core A0 seams are published + consumable.
> **Owner**: `spaarke-ai-architecture-redesign-r2` (core). **Consumer**: `spaarkeai-compose-r2`.
> **Last updated**: 2026-07-10 (task 017 — seam-publication ordering closure).

## Overall status

✅ **FR-A0-08 SEAM-PUBLICATION-ORDERING OBLIGATION CLOSED (task 017, 2026-07-10)** — all six FR-A0-08
seams (ComposeDisposition, OutcomeCard, ContextEnvelope, ledger provenance `{bindingId}@t{n}`,
GateDecision v2/Policy v2 tier table, JobAwareCompletionState) are published, contract-tested, and
consumable via `Services/Ai/PublicContracts/`. Plus the two bonus A0 contracts (MemoryItem,
TraceEvent) and their companion producers (020 hoist, 032 gate engine, 037 UI-ack, 038 trace view)
are ALSO green. **Compose r2 is unblocked for every task gated on a contract shape.** Full
verification: `notes/seam-publication-ordering.md`.

✅ **ALL SEAMS PUBLISHED ON-BRANCH (2026-07-10)** — the last outstanding row (`MemoryItem v1 +
memory.write`) landed: `memory.write` (task **057**, Phase M/G-R2-B) is code-complete on-branch with
its contract + eval tests green (silent-execute-through-the-real-gate + CAPTURE→RECALL). Every A0/seam
row is now ✅. Consistent with the other rows, the state is **branch (merge pending)** — the whole
dashboard converts from "branch" to a merged status when the branch merges to master; there is no
remaining CORE work blocking Compose r2 (FR-30 unblocked).

## Protocol

- Each seam row flips 🔲→✅ (with commit ref) when its **publishing task** completes. `task-execute` does this as an acceptance criterion of that task — so this file is live, not hand-maintained.
- Compose consumes each seam **as it lands** (incremental unblock) — it does NOT wait for all seven. `ComposeDisposition` (010) + `JobAwareCompletionState` (014) are sequenced FIRST per Compose's priority.
- **Task 017 closed the FR-A0-08 ordering obligation on 2026-07-10** (see `notes/seam-publication-ordering.md`). The full-dashboard "ALL SEAMS PUBLISHED" header remains reserved for when the LAST outstanding row (`memory.write`, 057) lands — whichever task completes 057 should flip it (or a light follow-up task, if the operator wants one filed explicitly).
- Contracts are walking skeletons (contract + reference producer/consumer + contract test); "published" = merged to master + contract test green + consumable via `Services/Ai/PublicContracts/`.

## Seam dependency table

| Seam | Publishing task(s) | Compose FR unblocked | Status | Published @ |
|---|---|---|---|---|
| `ComposeDisposition v1` (+ SSE frame, provenance, **supersession**) | **010** | FR-04 (draft-into-editor), FR-16 (pending redline), FR-17 (undo/replace) | ✅ **contract green** (7/7 tests) | branch (merge pending) 2026-07-08 |
| `JobAwareCompletionState v1` (**consumer-declared ordered steps**) | **014** | FR-05 (create-on-save card), FR-28 (push/save completion) | ✅ **contract green** (22/22 tests) | branch (merge pending) 2026-07-08 |
| `OutcomeCard v1` (hosts job state + next-step chips) | **011** | FR-05, FR-28 | ✅ **contract green** (10/10 tests) | branch (merge pending) 2026-07-08 |
| `GateDecision v2` / Policy v2 Tier 2c (**hosts parent-association picker**) | **012** + **032** | FR-05 (association prompt), FR-28 (push/save confirm) | ✅ **contract + engine green** (34/34 contract; **engine 032 ✅ 2026-07-09** — ConfirmationPolicyEngine PRODUCER, 7 tiers + E-1..E-6, 138/138 gate suite; producer-side unblocked — Compose r2 consumes the engine PRODUCER + contract now. ⚠️ **Core live-wiring status (corrected 2026-07-09 by task 034):** the engine has 0 core production call-sites — 034 = pre-suspend ValidateChat (NOT engine wiring), 042 reused the existing gated path. Wiring the engine into the core's own live gate is UNASSIGNED in the current WBS (see current-task open item / operator decision). This does NOT affect the Compose seam — Compose consumes the engine directly.) | branch (merge pending) 2026-07-09 |
| `MemoryItem v1` + `memory.write` (AI-initiated, provenance-tagged) | **016** + **057** | FR-30 (persist AI-derived insights) | ✅ **published** (MemoryItem contract 10/10; **memory.write 057 ✅ 2026-07-10** — MemoryWriteHandler + memory.write catalog row authored through the task-020 source; AI-initiated + SILENT through the REAL SideEffectGateAIFunction/ConfirmationPolicyEngine path (declares Write ⇒ gate-wrapped, tier-1 reversible riskProfile ⇒ engine resolves Execute, no dialog); provenance envelope (source=ai-derived / bindingId=loop / sessionId; trustLevel carried-inert) recorded + surfaced on read; upsert-by-(Type,Key) supersession; CAPTURE→RECALL eval joined to the merge gate. Consumable via `IMemoryItemStore` (task 050). **Compose r2 FR-30 unblocked.**) | branch (merge pending) 2026-07-10 |
| `TraceEvent v1` + D-F4 view (**host-embeddable**) | **013** + **038** | FR-32 (Context-pane trace hosting) | ✅ **contract + view published** (7/7 contract; **view 038 ✅ 2026-07-09** — ISessionTraceReader facade + GET /sessions/{id}/trace read surface + host-embeddable ExecutionTraceWidget + honest narration; Compose FR-32 unblocked) | branch (merge pending) 2026-07-09 |
| Triple-twin description hoist (single authored source) | **020** | FR-12 (catalog-row authoring quality) | ✅ **published** (Model 1; parity test + health-check) | branch (merge pending) 2026-07-08 |
| D-F3 UI-ack contract (ack tokens over correlationId) | **037** | FR-34 (UI ack) | ✅ **published** (IUiActionAckCoordinator PublicContracts facade; frameId ack after tab materializes; honest 8s Timeout; 12/12 tests) | branch (merge pending) 2026-07-09 |

## Notes on negotiated deltas (folded into core tasks 2026-07-08)

- **ComposeDisposition** — core owns the envelope (disposition member + `{bindingId}@t{n}` provenance + SSE ledger-ref frame + **supersession**); the structured-edit payload schema (`target_text`/`new_text`/`match_mode`/…) is **Compose-owned** inside the opaque SessionOutput payload.
- **JobAwareCompletionState** — the ordered step SET is **consumer-declared** (Compose: `container → record → profile-analysis → indexing`); the contract is generic over steps.
- **GateDecision v2** — the one dialog **hosts an optional parent-association picker** (associate-to matter/project/invoice/work-assignment/none — same generic entity set as Record memory).
- **D-F4** — the trace view is **host-embeddable** in an arbitrary container (Compose Context pane), not bound to the chat surface.
- **memory.write** — AI-initiated + silent + provenance-tagged (no gate); satisfies FR-30 directly. `AnchoredAnnotation` accepted as NOT a MemoryItem (Compose spec §ADR-Tensions Path-A) — it is document-positional UI state, not governed memory.
- **Terminology** — Compose "workspace-scope memory" = core **Record scope** `(entityType, entityId)`.

*Full requirement source: `notes/HANDOFF-core-r2-A0-contract-requirements.md` (from Compose). Core response: `notes/HANDOFF-response-to-compose-r2.md`.*
