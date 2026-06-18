# Current Task State — R6 (Wave C-G18 task 074 — done)

> **Last Updated**: 2026-06-18 (Pillar 9 per-turn prompt builder landed)
> **Mode**: Wave C-G18 (Pillar 9 BFF prompt-builder refinement) — COMPLETE
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Task 074 — closeout

| Task | Scope | Status | Evidence note |
|------|-------|--------|---------------|
| 074 | `SprkChatAgentFactory.BuildWorkspaceStateBlock` refined to derive per-tab FR-57 visible state from `WorkspaceTabWidgetData` (Option A — server-side derivation). FR-58 + FR-59 privacy filter wired (visible + has-state, BOTH required). Budget integration unchanged (existing `TryReservePromptBudget` helper). | ✅ | `notes/task-074-evidence.md` |

Pillar 9 BFF surface is now complete:
- Task 072 (registry extension) + Task 073 (per-widget impls) — frontend canonical shapes
- Task 074 (this) — BFF derives the SAME 4-variant FR-57 shapes server-side from the typed `WorkspaceTabWidgetData` polymorphic union
- 3-tab integration test (`Pillar9PrivacyFilterTests`) verifies the privacy filter end-to-end

TASK-INDEX 074 flipped 🔲 → ✅.

---

## Next task

The next pending entry per `TASK-INDEX.md` should be the next C-G* wave entry (075+). Resume there.
