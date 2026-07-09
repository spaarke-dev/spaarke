# SEAM-STATUS — core-r2 → Compose r2 dependency dashboard

> **Purpose**: the single file Compose r2 polls to know which core A0 seams are published + consumable.
> **Owner**: `spaarke-ai-architecture-redesign-r2` (core). **Consumer**: `spaarkeai-compose-r2`.
> **Last updated**: 2026-07-08 (seeded at task decomposition — all pending; execution not yet started).

## Overall status

🟡 **IN PROGRESS (2026-07-09)** — **all 6 A0 contract shapes green** + **020 hoist ✅, 037 UI-ack ✅, 032 gate engine ✅** (Wave J parallel batch merged 2026-07-09). Remaining for full seam publication: the impl halves **038** (trace view) + **057** (memory.write), then the **017** milestone. Header flips to ✅ **ALL SEAMS PUBLISHED — Compose UNBLOCKED** when task **017** completes (after 038/057 land). *Compose can bind to all 6 green contract shapes + the live GateDecision v2 engine (association picker) + UI-ack coordinator now; full publication = merge to master.*

## Protocol

- Each seam row flips 🔲→✅ (with commit ref) when its **publishing task** completes. `task-execute` does this as an acceptance criterion of that task — so this file is live, not hand-maintained.
- Compose consumes each seam **as it lands** (incremental unblock) — it does NOT wait for all seven. `ComposeDisposition` (010) + `JobAwareCompletionState` (014) are sequenced FIRST per Compose's priority.
- **Task 017** is the milestone: when the last seam is published, its completion posts the consolidated "Compose UNBLOCKED" notice (here + on Compose's tracking) and flips the header above.
- Contracts are walking skeletons (contract + reference producer/consumer + contract test); "published" = merged to master + contract test green + consumable via `Services/Ai/PublicContracts/`.

## Seam dependency table

| Seam | Publishing task(s) | Compose FR unblocked | Status | Published @ |
|---|---|---|---|---|
| `ComposeDisposition v1` (+ SSE frame, provenance, **supersession**) | **010** | FR-04 (draft-into-editor), FR-16 (pending redline), FR-17 (undo/replace) | ✅ **contract green** (7/7 tests) | branch (merge pending) 2026-07-08 |
| `JobAwareCompletionState v1` (**consumer-declared ordered steps**) | **014** | FR-05 (create-on-save card), FR-28 (push/save completion) | ✅ **contract green** (22/22 tests) | branch (merge pending) 2026-07-08 |
| `OutcomeCard v1` (hosts job state + next-step chips) | **011** | FR-05, FR-28 | ✅ **contract green** (10/10 tests) | branch (merge pending) 2026-07-08 |
| `GateDecision v2` / Policy v2 Tier 2c (**hosts parent-association picker**) | **012** + **032** | FR-05 (association prompt), FR-28 (push/save confirm) | ✅ **contract + engine green** (34/34 contract; **engine 032 ✅ 2026-07-09** — ConfirmationPolicyEngine PRODUCER, 7 tiers + E-1..E-6, 138/138 gate suite; producer-side unblocked — Compose r2 consumes the engine PRODUCER + contract now. ⚠️ **Core live-wiring status (corrected 2026-07-09 by task 034):** the engine has 0 core production call-sites — 034 = pre-suspend ValidateChat (NOT engine wiring), 042 reused the existing gated path. Wiring the engine into the core's own live gate is UNASSIGNED in the current WBS (see current-task open item / operator decision). This does NOT affect the Compose seam — Compose consumes the engine directly.) | branch (merge pending) 2026-07-09 |
| `MemoryItem v1` + `memory.write` (AI-initiated, provenance-tagged) | **016** + **057** | FR-30 (persist AI-derived insights) | ✅ **contract green** (10/10; memory.write 057 pending) | branch (merge pending) 2026-07-08 |
| `TraceEvent v1` + D-F4 view (**host-embeddable**) | **013** + **038** | FR-32 (Context-pane trace hosting) | ✅ **contract green** (7/7; view 038 pending) | branch (merge pending) 2026-07-08 |
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
