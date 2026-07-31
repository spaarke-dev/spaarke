# Current Task — `ai-advanced-capabilities-agreements-r1`

> Context-recovery anchor. Updated by `task-execute` at every step; reset at task completion (root CLAUDE.md §7).

## Active task

- **Task**: WAVE 0 — 001 ∥ 002 ∥ 010 (parallel, task-execute Step 0.3 parallel mode)
- **Status**: in-progress — 001 ✅ (7 rows seeded w/ GUIDs; alt-key dup-test PASS; KNW-012 forward ref for 003;
  footgun fix 7e022e7dd still hub-branch-only). 002 ✅ (generalize-IN-PLACE on GUID row 34c9ecf2…; actionCode→
  agreement-review, consumerType "nda-review" RETAINED by design — FR-01 territory for 020-023; 71 dotnet tests pass;
  live LLM eval env-blocked; B1–B16 taxonomy hand-off in notes/002-execution-notes.md for 003; deferred:
  ComposeSummaryPageGenerator.cs stale doc-comments → 030). 010 running.
- **Orchestration**: agents do NOT write .claude/, current-task.md, TASK-INDEX.md, or git commits —
  main session aggregates, runs wave-end build verification (npm builds for UI.Components +
  Compose.Components; dotnet build if 002 touched .cs eval tests), then single wave commit.
- **Next action**: on agent completion — verify acceptance, update TASK-INDEX (🔲→✅), build-verify, commit, start Wave 1 (003 ∥ 020 ∥ 052).

## Coordination state (ALL CLOSED — owner confirmations 2026-07-31)

- Hub answers: [notes/COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md](notes/COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md).
  Q1: deep-threading slice OURS (022; A1/A3-core hub-shipped — never rebuild). Q3: we load the 7 seeds (001).
- **Owner confirmations (2026-07-31, chat)**: ✅ **Q4 `sprk_key` alt-key CREATED** (001 still sanity-verifies via
  describe/dup-test before keying on it). ✅ **Q2 promote-FK fix FIXED** — ⚠️ caveat: no fix commit visible on
  origin/master or the hub branch as of the last fetch; 033 step-0 MUST still verify empirically (promote a
  summary-row-less session → durable FK or non-2xx) before building on promote. ✅ **Q5 Phase-1 UAT OK** — the
  wizard-finish seam (stable + additive, carries `subDomain`) is approved to build on; 033's UAT escalation
  downgrades to a quick seam re-check.

## Steps completed this task

_(none — no active task)_

## Files modified this task

_(none)_

## Decisions this task

_(none)_
