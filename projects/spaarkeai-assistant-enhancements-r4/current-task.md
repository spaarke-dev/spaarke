# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-08-17 (by task-execute — **023 COMPLETE**, transitioning to 024)
> **Recovery**: Read "Quick Recovery" first. Tracks the **active task only**; history lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarkeai-assistant-enhancements-r4 — autonomous execution (owner "continue"). **14 of 17 done.** |
| **Task** | ✅ **023 COMPLETE** (follow-on cards, open-tab-gated, FR-06). Next: 🔲 **024** (E2 eval cases — deps 021b✅ + 023✅, both satisfied). |
| **Status** | 023 committed locally (PR HELD). Remaining: 024 → 040 (D9 — needs `--chrome`, owner) → 080 (deploy, owner-gated) → 090 (wrap-up + /test-diet). |
| **Next Action** | Begin task 024 via task-execute: `projects/spaarkeai-assistant-enhancements-r4/tasks/024-e2-eval-cases.poml`. |

### 023 outcome (this session)
Owner §6.5 escalation resolved: (1) client launcher on the `list-tasks` FR-01 surface_launch (no new Dataverse data — 080 scope unchanged); (2) additive WorkspacePane `workspace_tabs_snapshot` open-tab gate. 7 files: `PaneEventTypes.ts` (additive event), `WorkspacePane.tsx` (broadcast at syncState), `WorkspaceLauncherCard.tsx` (NEW), `agendaFollowOnCards.tsx` (NEW, pure gating), `ConversationPane.tsx` (arm/subscribe/render into ProactiveCardStack), `agendaFollowOnCards.test.tsx` (NEW, 8 tests / all 5 ACs), TASK-INDEX/POML. Surface-gate: 5 prod files 0 errors. Step 9.5 PASS. Click→handleSurfaceLaunch (022 registry); WorkspacePane layoutId de-dupe → no-dup guaranteed.

---

## Standing constraints (unchanged)
- **PR HELD** — all commits LOCAL only; do NOT push/PR until owner asks.
- Never deploy BFF from a net8 tree. Measure BFF publish COMPRESSED (≤60 MB). No new HIGH CVE on BFF tasks.
- ADR-042 memory hard-governance DEFERRED to #616 (trustLevel inert).
- Commit footer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **021a+021b deploy TOGETHER at 080** (SSE wire string[]→typed); 080 also creates `sprk_groundedtoolallowlist` col + re-seeds; 022 layout GUIDs are spaarkedev1-specific (per-env update).
- **040 needs a live-DOM `--chrome` session (owner involvement) — NOT autonomous.**

---

## Remaining tasks
024 (E2 eval) → 040 (D9, `--chrome`/owner) → 080 (deploy, owner-gated) → 090 (wrap-up + /test-diet).
