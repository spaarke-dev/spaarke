# Core R2 → Compose R2 — Task 017 Seam-Publication-Ordering Closure (reciprocal filing)

> **From**: `spaarke-ai-architecture-redesign-r2` (core), task **017** (Seam-publication ordering +
> cross-project obligation filing)
> **To**: `spaarkeai-compose-r2` (satellite)
> **Date**: 2026-07-10
> **Purpose**: file the Compose-consumes side of the FR-A0-08 obligation (filed both ways per
> spec FR-A0-08 "cross-project obligation filed both ways"). Core's own filing:
> `projects/spaarke-ai-architecture-redesign-r2/notes/seam-publication-ordering.md` +
> `projects/spaarke-ai-architecture-redesign-r2/notes/SEAM-STATUS.md`.

---

## What's confirmed unblocked (contract-shape gated tasks)

All six FR-A0-08 seams — **ComposeDisposition v1**, **OutcomeCard v1**, **ContextEnvelope v1**
(workspace slice), **ledger provenance `{bindingId}@t{n}`**, **GateDecision v2 / Policy v2 tier
table**, **JobAwareCompletionState v1** — are published, contract-tested, and consumable via
`Services/Ai/PublicContracts/`. Plus the two bonus A0 contracts your other tasks reference:
**MemoryItem v1** (contract shape only — see below) and **TraceEvent v1** + its D-F4
host-embeddable view (task 038 — **now done**, corrects a stale line in your `CLAUDE.md`, see
"Correction" below).

Producer/engine companions also confirmed live:
- **020** (triple-twin description hoist) — ✅ published, unblocks catalog rows.
- **032** (ConfirmationPolicyEngine, Policy v2 gate engine) — ✅ live, 7 tiers + E-1..E-6.
- **037** (D-F3 UI-ack, `IUiActionAckCoordinator`) — ✅ published.
- **038** (TraceEvent D-F4 view, `ISessionTraceReader` + `GET /sessions/{id}/trace` +
  `ExecutionTraceWidget`) — ✅ published 2026-07-09.

**No-forked-seam rule (FR-D-03) reaffirmed**: continue consuming these seven contract shapes
exactly as published — no local variant. The one negotiated Path-A exception remains
`AnchoredAnnotation` (document-positional UI state, explicitly not a `MemoryItem`). Core task
**072** (Cross-satellite seam-fork verification, gate G-R2-D) is the binding enforcement point that
checks for forks of any of these seven shapes.

**`/conflict-check` reaffirmed**: run it before every BFF PR that touches `Services/Ai/**` — both
projects are listed in `projects/INDEX.md`'s BFF hot-path overlap section.

---

## Correction to your `CLAUDE.md` snapshot

Your project's `CLAUDE.md` (§ "Core Phase A0 dependency", as of 2026-07-08) lists:

> "FR-32 trace hosting (064) — TraceEvent shape present, but the D-F4 view (core task 038) 🔲
> pending."

This is now **stale** — core task 038 completed 2026-07-09 (`ISessionTraceReader` facade + read
endpoint + `ExecutionTraceWidget`, 7/7+37/37 server, 33/33+8 client). Your task 064 (FR-32 trace
hosting) should be re-checked against current state; the view seam is no longer a blocker.

---

## One remaining outstanding item — NOT yet unblocked

**`memory.write` (core task 057)** — the AI-initiated, silent, provenance-tagged write mechanism —
is **still 🔲 not started** (dependency: core task 050, which IS done). Your `CLAUDE.md` already
tracks this correctly: *"FR-30 memory.write (063) — MemoryItem shape present, but the `memory.write`
tool impl (core task 057) 🔲 pending."* That remains accurate. `MemoryItem v1`'s contract SHAPE
(task 016) is published and green (10/10 tests) — you can build/test against the shape now — but
the live write path your task 063 needs is not yet available.

This is why core's `notes/SEAM-STATUS.md` has **not** flipped its header to the full "ALL SEAMS
PUBLISHED — Compose UNBLOCKED" state: one dashboard row (MemoryItem + memory.write) has an
outstanding half. Core's task 017 (this filing) closed the FR-A0-08 **seam-publication-ordering**
obligation (the six contract seams above) — a narrower, already-satisfied claim — while being
transparent that `memory.write` remains a separate, tracked gap for your task 063.

**Action for Compose r2**: keep task 063 gated on core task 057 landing; watch
`projects/spaarke-ai-architecture-redesign-r2/notes/SEAM-STATUS.md` for the flip. No action needed
on your side beyond that — this is core's item to close.

---

## Note on posting to "compose-r2 project tracking"

Task 017's POML asked for this notice to also be posted as a comment on the compose-r2 GitHub
Project tracking issue. A search of `spaarke-dev/spaarke` GitHub issues found **no open "[Project]:
spaarkeai-compose-r2" tracking issue** (only the closed predecessor, `spaarkeai-compose-r1` — issue
#514). This file-based HANDOFF (the established cross-project communication pattern already in use
in both projects' `notes/` folders) is filed in its place. If/when a compose-r2 Project Issue is
opened (`/devops-project-register` or similar), this note's content should be cross-posted there.
