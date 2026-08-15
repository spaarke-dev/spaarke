# Current Task State — spaarkeai-compose-r7

> **Last Updated**: 2026-08-15 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **012** — Save As uniquify (FR-07a) — ANALYZED, ready to implement (see `notes/task-012-analysis.md`) |
| **Step** | Root-cause traced + fix designed; implementation not started |
| **Status** | 012 analyzed / not-implemented (checkpoint) |
| **Next Action** | Implement 012 per `notes/task-012-analysis.md` (Graph `conflictBehavior=rename` for the fork create + fresh `composeLogicalId` for forkNew), commit, then 013 (atomic upsert — same file, serialize), then 020/030/040/041… |

**012 is fully analyzed** — root cause = `UploadSmallAsUserAsync` PUT-by-path defaults to `replace`, so a same-name fork re-versions the original. Fix = route the `forkNew` create through Graph `conflictBehavior=rename` (atomic, no duplicate window; infra exists at `UploadSessionManager.cs:531`) + mint a fresh `composeLogicalId` for the fork. Full plan + file/line map in `notes/task-012-analysis.md`. **012 & 013 both edit `ComposeService.cs` → serialize.**

**Completed this session** (all committed + pushed-pending):
- 001 ✅ gate (`3f5cbfe02`) — baseline **44.96 MB incl PDBs net10**; conflict-check CLEAR; DI-gate verified; PRs #690/#266 OPEN.
- 010 ✅ (`2dde88f3c`) — `composeLogicalId` + `getComposeLogicalIdentity` accessor + `composeIdentity.ts` (localStorage single-slot). Shared key for 040/011.
- 073 ✅ (`fd0b8e4da`, cherry-picked from Group-B subagent) — PDF-intake cause discrimination. **FR-11 end-to-end surfacing deferred to 050/051** (see task-073-notes.md — avoids r2 PublicContracts change + downcast).
- 011 ✅ (`23793f4e9`) — id-less assistant-insert door now carries dedup identity.

**Background stream still in flight (isolated worktree):**
- **Task 075** (FR-13 test-hygiene) — subagent running; will report a commit SHA to cherry-pick. Main session owns TASK-INDEX/current-task/INDEX bookkeeping.

**Key carried decisions**: composeLogicalId is the FR-03/FR-07 shared key; localStorage single active-draft slot; 050/051 must wire the PDF-intake cause-specific message (FR-11 rider). Baseline for NFR-01 deltas = 44.96 MB incl PDBs.

### Files Modified This Session
- All under `projects/spaarkeai-compose-r7/` — created/finalized during `/design-to-spec` → `/project-pipeline` (spec, README, plan, CLAUDE.md, 20 POML tasks, TASK-INDEX) + two re-alignment passes. **All committed + pushed; nothing uncommitted.**

### Critical Context
Project is fully initialized and **execution-ready**: spec (13 FRs / 6 NFRs), 20 validated POML tasks (8 phases), branch `work/spaarkeai-compose-r7` @ `6486c52ea`, **0 behind master, clean, pushed**. The branch is **net10-ready** (master is net10 as of 2026-08-14; BFF Release build clean) and **re-aligned** to the code-quality-and-assurance-r3 + dotnet-10-COMPLETE master (anchors re-verified 2026-08-15). Nothing blocks starting task 001. Do **not** re-run the pipeline — go straight to execution.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none (next: 001) |
| **Task File** | `tasks/001-coordination-gate-baseline.poml` |
| **Title** | Coordination gate + publish-size baseline + env verification |
| **Phase** | 0: Coordination Gate + Baseline |
| **Status** | not-started |
| **Started** | — |

---

## Progress

### Completed Steps (initialization, not task execution)
- [x] `/design-to-spec` → `spec.md` (owner Q&A resolved: client-only draft store; candidate adds; D1 hygiene deferred)
- [x] Both open questions resolved (unified stable-logical-id for FR-03+FR-07; fidelity-wideners home → wrap-up)
- [x] `/project-pipeline` → README, plan, CLAUDE.md, 20 POML tasks + TASK-INDEX (validator clean 20/20)
- [x] `projects/INDEX.md` row appended (BFF=Y, SpaarkeAi=Y)
- [x] net10 readiness: merged net10 master, BFF Release build clean, task 001 + CLAUDE.md updated
- [x] Re-sync to code-quality-r3 + dotnet-10-COMPLETE master; anchors re-verified; baseline → net10 ~44.96 MB

### Current Step
*No active task — awaiting go-ahead for task 001.*

### Files Modified (All Task)
*No implementation files yet — R7 has written no product code.*

### Decisions Made (project-level, carried into execution)
- 2026-08-13 — Autosave draft store = **client-only** (no BFF surface; NFR-03 satisfied structurally).
- 2026-08-13 — FR-03 draft recovery + FR-07 client dedup **unified on one stable logical id** (`sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`); FR-07(b) introduces it (none exists today).
- 2026-08-13 — Fidelity wideners deferred; **home named at wrap-up** (task 090). D1 dev-data hygiene = leave/defer.
- 2026-08-14 — Branch retargeted **net10** (master is net10; never deploy BFF from a net8 tree → 503).
- 2026-08-15 — Publish baseline is **net10 ~44.96 MB incl PDBs** (supersedes net8 ~46.94 MB); task 001 re-measures empirically.

---

## Next Action

**Next Step**: `task-execute` on **task 001** (coordination gate). It runs `/conflict-check`, measures the **net10** publish baseline, verifies the PDF DI compound gate is ON in the target env, checks watched PRs, and writes `notes/coordination-baseline.md`.

**Pre-conditions**:
- Branch clean + 0 behind master ✅ (already true at handoff)
- Confirm net10-readiness in task 001 step 0.5 (SDK 10.0.1xx, Release build clean — already verified at init)

**Recommended execution order** (from TASK-INDEX):
1. **001** (gate) → 2. **010** (stable logical id — opus; blocks Phase 4) → 3. **011→012→013** (save-identity vectors) → 4. **020** (Save dropdown), **030** (name modal) → 5. **040→041** (autosave) → 6. **050→051** (PDF parity) → 7. **060→061** (hotkeys) → 8. **Group B: 073 ∥ 075** (parallel) + **070,071,072,074** (sequential) → 9. **090** (wrap-up).

**Key Context**:
- Critical path: `001 → 010 → 040 → 041 → 090`.
- opus tasks: 010, 013, 050. xhigh: 011, 012, 071.
- **parallel-safe:false on ALL Compose-spine tasks**; only Group B (073, 075) is parallel.
- Coordination: `/conflict-check` before every BFF PR; 061 vs assistant-r3 (`ConversationPane`/`SprkChatInput`); 073 consume `Services/Ai/PublicContracts/` (no fork); 075 watch PR #690.
- Anchor caveat (post-2026-08-15 merge): `AnalysisServicesModule.cs` DI gate shifted to ~L145/165 (grep the symbols, don't trust exact lines); all other anchors intact.

**Expected Output of task 001**: `notes/coordination-baseline.md` with net10-readiness, conflict-check result, net10 publish baseline (MB + PDB convention), PDF DI gate ON/OFF, PR #690/#266 status.

---

## Blockers

**Status**: None. Execution is owner-gated (deliberate), not blocked.

---

## Session Notes

### Current Session
- Focus: `/design-to-spec` → `/project-pipeline` initialization + net10 migration + code-quality-r3 re-alignment. All committed/pushed.

### Key Learnings
- No non-rotating document identity exists today — FR-07(b) must introduce one; it is the shared key for draft recovery (FR-03) + client dedup (FR-07).
- Master went net10 (2026-08-14) and absorbed the code-quality-r3 BFF refactor (2026-08-15); R7's only anchor drift was `AnalysisServicesModule.cs` — everything else intact.

### Handoff Notes (for the fresh post-compact session)
1. Read this file + `projects/spaarkeai-compose-r7/CLAUDE.md` (constraints + 2026-08-15 re-sync note) + `spec.md` (FR/NFR closed sets).
2. Confirm branch clean + 0 behind master (`git status`, `git rev-list --count HEAD..origin/master`). If behind, `git merge origin/master` first (INDEX.md is the usual conflict).
3. Start with **"work on task 001"** — do NOT re-run the pipeline; tasks already exist and validate clean.
4. Portfolio: project is NOT registered on the board (no README portfolio pointer) — run `/devops-project-register` if desired (optional).
5. Held decisions still stand: merge-to-master deferred to wrap-up; fidelity-wideners home named at task 090.

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r7 · **Branch**: `work/spaarkeai-compose-r7` @ `6486c52ea`
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Spec**: [`spec.md`](./spec.md) · **Plan**: [`plan.md`](./plan.md)

### Applicable ADRs
- ADR-049 (save path) · ADR-050 (name modal) · ADR-032 (PDF gate) · ADR-007/013 (`ProjectForMount` async tension) · ADR-021 (Fluent dark-mode) · ADR-038 (tests)

### Knowledge Files
- `spec.md`, `plan.md`, `CLAUDE.md`, `notes/r6-defer-register-consolidated.md`, `tasks/TASK-INDEX.md`

---

## Recovery Instructions

1. **Quick Recovery**: read the section at top (<30s).
2. **Confirm sync**: branch clean + 0 behind master; merge master if behind (INDEX.md conflict expected).
3. **Begin**: "work on task 001" → `task-execute`.
4. **Full protocol**: [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md).

**Commands**: `/project-continue` (full reload + sync) · "where was I?" (quick recovery).

---

*This file is the primary source of truth for active work state. Keep it updated.*
