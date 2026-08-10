# SpaarkeAI Assistant Enhancements R3

> **The Assistant ⇄ Workspace Interaction Contract** — Conversational Capability Parity.

## What this delivers

Every open workspace widget gets a matching Assistant tool set — an **overview/query** tool (answers chat questions from the authoritative source) and, for act-on surfaces, **per-item action cards** keyed to the selected item — mounted only while the widget's tab is open. Fixes the R2 UAT gap where the Assistant was *aware* of tabs but had no tool to answer from them.

**Spine**: the active-item handle model (§5.5) — the Assistant holds `{id,type,label}`, never content; every fact/action is a tool that fetches by id. Generalized from the shipped Compose active-document flow.

## Scope (owner-locked 2026-08-10)

- **Overview parity**: all grids + Daily Briefing + Calendar (ONE parameterized `configId` tool).
- **Per-item cards**: Email + Documents.
- **Email replies**: auto-drafted, with a **binding thread-preservation invariant** (draft above the quoted thread, never replacing it).
- **Selection hint**: handle stays in the prompt (no `get_selection` tool).
- **Phase 0**: out (shipped on R2).

## Status

- **Phase**: Tasks generated — **execution owner-gated (not auto-started)**.
- **Created**: 2026-08-10.
- **Predecessor**: spaarkeai-assistant-enhancements-r2 (shipped surface-awareness + true-resume + Phase 0).

## Graduation criteria

1. Overview DoD — "how many overdue tasks do I have?" answers correctly (server-side `today`, no query error, no duplicate tab).
2. Per-item DoD — select an email → Reply card → composer pre-filled with auto-draft **+ preserved quoted thread**.
3. Summarize-the-thread answers in chat (same as file summarize).
4. Document per-item cards work (Summarize from RAG/body, record-id cited).
5. Overview parity across all in-scope surfaces.
6. Tool economy — only open tabs' tools mount.
7. Registration contract enforced at all four sites.
8. Governance — no item content in the prompt.
9. BFF hygiene — ≤60 MB publish, no new HIGH CVE, dual-mount email parity intact.

## Key files

- [`spec.md`](spec.md) — AI-optimized spec (15 FRs / 9 NFRs).
- [`design.md`](design.md) — the interaction contract (§5.5 orchestration model).
- [`plan.md`](plan.md) — WBS + parallel groups + hot-path coordination.
- [`CLAUDE.md`](CLAUDE.md) — AI context (load first).
- [`current-task.md`](current-task.md) — active task state.
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + dependencies + parallel groups.
- [`notes/R3-SESSION-CONTEXT.md`](notes/R3-SESSION-CONTEXT.md) · [`notes/design-review-2026-08-10.md`](notes/design-review-2026-08-10.md).

## ⚠️ Coordination

BFF=Y, SpaarkeAi=Y. `/conflict-check` before every BFF/`ConversationPane` PR. Consume `Services/Ai/PublicContracts/` seams (redesign-r2 sole owner — no fork). Keep the reactive card surface distinct from the ADR-047 spine. Re-sync `origin/master` before Phase 1 execution (branch is 5 commits behind at init).
