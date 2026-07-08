# SEAM-STATUS — core-r2 → Compose r2 dependency dashboard

> **Purpose**: the single file Compose r2 polls to know which core A0 seams are published + consumable.
> **Owner**: `spaarke-ai-architecture-redesign-r2` (core). **Consumer**: `spaarkeai-compose-r2`.
> **Last updated**: 2026-07-08 (seeded at task decomposition — all pending; execution not yet started).

## Overall status

🔲 **PENDING** — core is at its plan-review gate; no seam published yet. This header flips to ✅ **ALL SEAMS PUBLISHED — Compose UNBLOCKED** when task **017** completes.

## Protocol

- Each seam row flips 🔲→✅ (with commit ref) when its **publishing task** completes. `task-execute` does this as an acceptance criterion of that task — so this file is live, not hand-maintained.
- Compose consumes each seam **as it lands** (incremental unblock) — it does NOT wait for all seven. `ComposeDisposition` (010) + `JobAwareCompletionState` (014) are sequenced FIRST per Compose's priority.
- **Task 017** is the milestone: when the last seam is published, its completion posts the consolidated "Compose UNBLOCKED" notice (here + on Compose's tracking) and flips the header above.
- Contracts are walking skeletons (contract + reference producer/consumer + contract test); "published" = merged to master + contract test green + consumable via `Services/Ai/PublicContracts/`.

## Seam dependency table

| Seam | Publishing task(s) | Compose FR unblocked | Status | Published @ |
|---|---|---|---|---|
| `ComposeDisposition v1` (+ SSE frame, provenance, **supersession**) | **010** | FR-04 (draft-into-editor), FR-16 (pending redline), FR-17 (undo/replace) | 🔲 pending | — |
| `JobAwareCompletionState v1` (**consumer-declared ordered steps**) | **014** | FR-05 (create-on-save card), FR-28 (push/save completion) | 🔲 pending | — |
| `OutcomeCard v1` (hosts job state + next-step chips) | **011** | FR-05, FR-28 | 🔲 pending | — |
| `GateDecision v2` / Policy v2 Tier 2c (**hosts parent-association picker**) | **012** + **032** | FR-05 (association prompt), FR-28 (push/save confirm) | 🔲 pending | — |
| `MemoryItem v1` + `memory.write` (AI-initiated, provenance-tagged) | **016** + **057** | FR-30 (persist AI-derived insights) | 🔲 pending | — |
| `TraceEvent v1` + D-F4 view (**host-embeddable**) | **013** + **038** | FR-32 (Context-pane trace hosting) | 🔲 pending | — |
| Triple-twin description hoist (single authored source) | **020** | FR-12 (catalog-row authoring quality) | 🔲 pending | — |
| D-F3 UI-ack contract (ack tokens over correlationId) | **037** | FR-34 (UI ack) | 🔲 pending | — |

## Notes on negotiated deltas (folded into core tasks 2026-07-08)

- **ComposeDisposition** — core owns the envelope (disposition member + `{bindingId}@t{n}` provenance + SSE ledger-ref frame + **supersession**); the structured-edit payload schema (`target_text`/`new_text`/`match_mode`/…) is **Compose-owned** inside the opaque SessionOutput payload.
- **JobAwareCompletionState** — the ordered step SET is **consumer-declared** (Compose: `container → record → profile-analysis → indexing`); the contract is generic over steps.
- **GateDecision v2** — the one dialog **hosts an optional parent-association picker** (associate-to matter/project/invoice/work-assignment/none — same generic entity set as Record memory).
- **D-F4** — the trace view is **host-embeddable** in an arbitrary container (Compose Context pane), not bound to the chat surface.
- **memory.write** — AI-initiated + silent + provenance-tagged (no gate); satisfies FR-30 directly. `AnchoredAnnotation` accepted as NOT a MemoryItem (Compose spec §ADR-Tensions Path-A) — it is document-positional UI state, not governed memory.
- **Terminology** — Compose "workspace-scope memory" = core **Record scope** `(entityType, entityId)`.

*Full requirement source: `notes/HANDOFF-core-r2-A0-contract-requirements.md` (from Compose). Core response: `notes/HANDOFF-response-to-compose-r2.md`.*
