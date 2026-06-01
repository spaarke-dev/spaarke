# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-01 (post-/project-pipeline Step 4)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 011 — RB-T028-03/04/05/06 HIGH cluster — ⏸ **ESCALATED 2026-06-01** ([E-01](escalations/E-01-rb-t028-cluster-scope-expansion.md)) |
| **Step** | Investigation complete; root cause cascade discovered (5 layers); production code + tests REVERTED to baseline; escalation E-01 filed |
| **Status** | escalated |
| **Next Action** | Owner reviews E-01 and selects path A/B/C/D. Task 011 blocked until owner direction received. Task 012 (parallel) ✅ COMPLETE. Phase 1 exit gate (task 013) blocked on task 011. |

### Task 012 — ✅ COMPLETE 2026-06-01 (separate parallel task)

All POML steps complete; production fix applied (GroundingVerifier.Normalize internal→public); 3 tests Skip→Pass; triple-run Failed: 0 × 3; quality gates PASS; ledger RB-T028-02 → `repaired`; D-07 finalized; task 026 deferred (subsumed); committed `bce111c3` + pushed.

### Rigor Decision (Task 012 — current)

- **Level**: FULL
- **Reason**: Path (b) chosen — production code change in `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs`; tags include `bff-api`, `ai`; modifying `.cs` file.
- **Step 9.5 quality gates**: code-review + adr-check MANDATORY (MEDIUM severity per ledger — security review NOT required per D-03).

### Task 012 Root Cause Analysis (2026-06-01)

**The ledger hypothesis was incomplete.** The bug is NOT "LLM-mock fixture text drifted" — Python verification confirms the literal quote strings ARE present in the fixture files (after CR stripping). The actual root cause:

- The 3 fixture files (`closing-letter-M-2024-0341.txt`, `settlement-agreement-M-2024-0188.txt`, `decision-memo-M-2024-0512.txt`) are stored on Windows with **CRLF (`\r\n`) line endings** (67/85/83 CRLFs respectively).
- C# raw-string literals (`"""..."""`) normalize multi-line content to **LF (`\n`)** at compile time (per C# 11 spec).
- The test's manual GroundingVerifier mirror at lines 165, 254, 338 uses raw `String.Contains` → `documentText` (CRLF) contains the LF-only quote? **NO.** Fails immediately on all 3 tests.
- **Production behavior is correct**: `GroundingVerifier.Normalize` (line 266) collapses ALL `char.IsWhiteSpace(ch)` including `\r\n` into a single space — so production substring matching is line-ending-tolerant.
- **The test failed to mirror production normalization** — it does a stricter, byte-exact check that production never does.

### Fix Design (Task 012)

1. **Production change** (in scope per FR-05 path-b + bff-api tag): Promote `GroundingVerifier.Normalize` from `internal static` → `public static` (+ XML doc clarifying it's the canonical grounding-text normalization). Makes the load-bearing invariant a documented public API surface that tests and other components can mirror precisely. Tiny additive change, <5% line replacement.
2. **Test change** (in Skip→Pass scope per NFR-01): replace each `documentText.Should().Contain(quote)` call with `GroundingVerifier.Normalize(documentText).Should().Contain(GroundingVerifier.Normalize(quote))` — aligning the test's check with the production mechanic. Plus remove Skip + transition trait `real-bug-pending-fix` → `repaired`.

### Prior Task 010 Rigor Decision (archived; tasks complete)

- HIGH severity production fix in `src/`; security-sensitive (cross-matter privilege leak); modifying `.cs` file; FULL rigor with security review per NFR-03.

### Critical Context — Bug Re-Analysis (2026-06-01)

Ledger's recommended fix (simple inversion `if (i > fromTurnIndex)` → `if (i < fromTurnIndex)`) is INCOMPLETE — it would break the currently-passing test `Sanitizer_StripsRetrievalBlocks_PreservesConclusions` (which uses `fromTurnIndex=3` with no matter markers and expects retrievals at indices 0, 2 to be stripped).

**Correct unified semantic** (verified against all tests):
- If `history[fromTurnIndex]` is a matter marker (`ExtractMatterId` returns non-null) → **matter-pivot mode**: pass through messages where `i < fromTurnIndex`; from `i >= fromTurnIndex` onward, strip retrieval messages UNTIL a different matter marker is encountered; messages after the new marker pass through.
- Else → **legacy mode**: strip retrieval messages where `i <= fromTurnIndex`; pass through `i > fromTurnIndex`. (Preserves currently-passing `Sanitizer_StripsRetrievalBlocks_PreservesConclusions` contract.)

This obeys the D-03 lesson: an "obvious" inversion would have introduced a regression. Full scrutiny applied.

### Files Modified This Session

- `projects/sdap.bff.api-test-suite-repair-r2/{README.md, plan.md, CLAUDE.md, current-task.md}` — Created
- `projects/sdap.bff.api-test-suite-repair-r2/tasks/*.poml` — 36 POML files Created
- `projects/sdap.bff.api-test-suite-repair-r2/tasks/TASK-INDEX.md` — Created (with parallel-execution plan)
- `projects/sdap.bff.api-test-suite-repair-r2/{notes,decisions,baseline,audits,ledgers}/.gitkeep` — Scaffolding created
- **Commit** `4221c9dd` — staged + pushed to `origin/work/sdap.bff.api-test-suite-repair-r2` (newly-created remote branch)
- **Draft PR** [#318](https://github.com/spaarke-dev/spaarke/pull/318) opened against master

### Critical Context

`/project-pipeline` Steps 0-4 are complete. Step 5 (autonomous task execution) is the next phase of work. **Phase 0 first wave (P0-W1)** runs tasks 000, 001, 002 in parallel — they have disjoint outputs (baseline / reproducibility / outreach) and are all `parallel-safe: true`.

Phase 0 will take ~1 week of calendar; tasks 000 + 001 are agent-executable; **task 002 requires owner action** (actually sending outreach to siblings) — the task drafts the outreach but the owner sends.

---

## Active Task (Full Details)

| Field | Value |
|---|---|
| **Task ID** | none |
| **Task File** | — (none yet) |
| **Title** | Project initialization (pipeline Steps 0-2) |
| **Phase** | Pre-Phase-0 (pipeline setup) |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps

- [x] `/project-pipeline` Step 0.3: pre-flight checks (branch, working tree, sync, build) — 2026-06-01
- [x] `/project-pipeline` Step 0.5: master staleness audit — 2026-06-01 (7 unmerged `origin/work/*` branches; non-blocking)
- [x] `/project-pipeline` Step 1: spec.md validation — 2026-06-01 (225 lines, all required sections)
- [x] `/project-pipeline` Step 1.5: overlap detection — 2026-06-01 (16 open PRs, no path overlap with r2)
- [x] `/project-pipeline` Step 2 Part 1: comprehensive resource discovery — 2026-06-01 (10 ADRs, 13 skills, ~20 patterns/constraints catalogued)
- [x] `/project-pipeline` Step 2 Part 2: `project-setup` artifact generation — 2026-06-01 (README + plan + CLAUDE.md + current-task.md + scaffolding)
- [x] `/project-pipeline` Step 3: `task-create` — 36 POML tasks + TASK-INDEX.md generated — 2026-06-01
- [x] `/project-pipeline` Step 4: commit `4221c9dd` + push `-u origin work/sdap.bff.api-test-suite-repair-r2` + draft PR #318 against master — 2026-06-01
- [x] **Task 000 (Phase 0 P0-W1)**: r1 close-out baseline captured — `baseline/r1-closeout-2026-06-01.md` (9 sections; 20 ledger entries enumerated with severity/fix-by/r2-phase mapping; branch protection captured — `enforce_admins: true` + 4 required checks observed vs 3 in POML pre-condition; CI gate state confirmed — duplicate-YAML fix in place, Coverlet at lines 85-102, skip-tests removed; 4 anti-drift surfaces verified cross-referenced; 5 owner clarifications captured). r1 final test counts: 6,030 unit + 421 integration = 6,451 total / 0 Failed / 235 Skipped (51 trace to ledger entries). — 2026-06-01
- [x] **Task 001 (Phase 0 P0-W1)**: 20-entry reproducibility verification — 2026-06-01. All 20 entries verified-reproducible via pragmatic static-inspection methodology (Skip + Trait + production-path-extant). 5/5 HIGH ready for Phase 1 task 010 + task 011. Output: `baseline/20-entries-reproducibility-verification.md`. Zero `needs-investigation` flagged. Three production-path citations had drifted to "equivalent path" (anticipated by ledger): RB-T028-02 (`Layer2OutcomeExtractor.cs` → `Services/Ai/Insights/Extraction/` namespace), RB-T028-05 (no `ReAnalysisFlowEndpoints.cs`; routes through `ChatEndpoints.cs` + `Program.cs`), RB-T028-07 (`Api/Ai/UploadEndpoints.cs` → `Api/UploadEndpoints.cs`).
- [x] **Task 002 complete** (P0-W1, parallel task 3 of 3): Sibling-coordination consolidated to `dev@spaarke.com`; r1 priority-order.md FR-06 slots populated; D-07 placeholder created; path (c) removed from FR-05 — 2026-06-01
- [x] **Task 010 complete** (Phase 1 P1-S1, FULL rigor, HIGH severity): RB-T044-01 cross-matter privilege-leak fix — `ConversationHistorySanitizer.StripRetrievedContent` re-implemented with **unified matter-pivot-aware semantic** (ledger's one-line inversion would have broken `Sanitizer_StripsRetrievalBlocks_PreservesConclusions` — D-03 lesson confirmed). Production file: ~42 added lines on 113-line file (~37% — NFR-02 compliant). 5 PrivilegeLeakageTests Skip→Pass + 1 new 3-matter-pivot regression test (`MatterPivot_ThreeMatters_StripsOnlyImmediatelyPreviousMatterContent`) — FR-02 explicit "cross-matter regression test added" requirement satisfied. Per-fix triple-run: 3 × **Failed: 0** / 5,899 Passed / 132 Skipped / 6,031 Total — see [`baseline/per-fix-triple-run-rb-t044-01-2026-06-01.md`](baseline/per-fix-triple-run-rb-t044-01-2026-06-01.md). Step 9.5 quality gates: `code-review` PASS (0 Critical / 0 Warning / 1 cosmetic Suggestion); `adr-check` PASS (7 ADRs compliant: ADR-001, ADR-007, ADR-008, ADR-010, ADR-013 refined, ADR-015, ADR-029); BFF Hygiene § A all 6 rules satisfied. Security review request to `dev@spaarke.com` per NFR-03 pending in PR #318 comment. — 2026-06-01
- [⏸] **Task 011 ESCALATED** (Phase 1 P1-S2, FULL rigor, HIGH cluster, 4 RB IDs): Investigation surfaced a 5-layer root-cause cascade much wider than the r1 ledger's "shared root cause" framing captured. Layer 1 (NotificationService misregistered) → Layer 2 (IBriefingAi? param-inference) → Layer 3 (IInvoiceSearchService conditional) → Layer 4 (PendingPlanManager conditional on ChatEndpoints) → Layer 5+ (likely 10-20 more endpoint handlers). r1 ledger's preferred Approach 1 (conditional endpoint mapping) violates NFR-01 because tests expect endpoints to function with `Analysis:Enabled=false`. r1 ledger's alternative Approach 2 (NullObject services) requires ~10+ Null impls + new ADR (~20-30h, exceeds task estimate). All production code + test changes during investigation REVERTED to baseline. **Escalation filed**: [`escalations/E-01-rb-t028-cluster-scope-expansion.md`](escalations/E-01-rb-t028-cluster-scope-expansion.md). Owner decision required (path A/B/C/D). — 2026-06-01

### Current Step

Phase 1 P1-S1 (task 010) complete. Awaiting security review on PR #318 before P1-S2 (task 011 cluster) can begin.

### Files Modified (All Task)

See "Files Modified This Session" above.

### Decisions Made

- 2026-06-01: Reuse `work/sdap.bff.api-test-suite-repair-r2` branch at Step 4 — no new `feature/...` branch. (Owner decision)
- 2026-06-01: All 5 spec.md "Unresolved Questions" RESOLVED by owner:
  - Security reviewer (NFR-03) = `dev@spaarke.com`
  - Insights sibling contact (FR-05) = `dev@spaarke.com`
  - Phase 4 staffing = Parallel (5 tracks in 1 wave)
  - `github-actions-rationalization-r1` Phase 1 = complete or imminent (no Track D slip)
  - **r3 = NOT planned** (D-06 updated). r2 is comprehensive closure; urgent BFF-development blocker.
- 2026-06-01 (Task 010): **RB-T044-01 fix DOES NOT follow ledger's one-line-inversion recommendation.** The recommended `if (i > fromTurnIndex)` → `if (i < fromTurnIndex)` would have broken the currently-passing `Sanitizer_StripsRetrievalBlocks_PreservesConclusions` test (no matter markers; `fromTurnIndex=3`; expects indices 0+2 retrievals stripped — simple inversion preserves them, test fails). Vindicates D-03 ("obvious fixes still cascade"). Implemented **unified matter-pivot-aware semantic** instead: matter-pivot mode (anchor is matter marker) strips retrievals from anchor forward until next different matter marker; legacy mode (anchor is not a marker) strips retrievals where i ≤ fromTurnIndex (preserves prior contract). All 30 PrivilegeLeakageTests pass (29 originally + 1 new regression test).

---

## Next Action

**Next Step**: Dispatch Phase 0 P0-W1 wave — 3 parallel agents (one per task).

Send ONE message with 3 `Skill` tool invocations:
- `Skill(skill="task-execute", args="projects/sdap.bff.api-test-suite-repair-r2/tasks/000-capture-r1-baseline.poml")`
- `Skill(skill="task-execute", args="projects/sdap.bff.api-test-suite-repair-r2/tasks/001-verify-20-bugs-reproducible.poml")`
- `Skill(skill="task-execute", args="projects/sdap.bff.api-test-suite-repair-r2/tasks/002-sibling-owner-outreach.poml")`

**Pre-conditions**:
- All 36 POML tasks present ✅
- TASK-INDEX.md with parallel-execution plan present ✅
- Branch `work/sdap.bff.api-test-suite-repair-r2` tracks `origin/work/sdap.bff.api-test-suite-repair-r2` ✅
- Draft PR #318 open ✅

**Key Context**:
- Task 000: STANDARD rigor — read r1 ledgers, branch protection, CI gate, current test counts; produce `baseline/r1-closeout-2026-06-01.md`
- Task 001: STANDARD rigor — verify 20 currently-Skipped tests are still appropriately Skipped (no regression-disguised-as-Skip); produce `baseline/20-entries-reproducibility-verification.md`
- Task 002: MINIMAL rigor — **DRAFTS** outreach docs to Action Engine / Insights / Communications owners; owner-action follow-up to actually send (Claude cannot send emails)

**Expected Output**:
- 3 baseline / outreach docs in `projects/.../baseline/` + `projects/.../decisions/owner-responses/`
- TASK-INDEX statuses for 000, 001, 002 → ✅
- After wave: build verification `dotnet build src/server/api/Sprk.Bff.Api/` (any wave touching `.cs` files — Phase 0 doesn't, but the protocol applies generally)

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session

- Started: 2026-06-01 (with `/project-pipeline` invocation)
- Focus: Project initialization via `/project-pipeline`

### Key Learnings

- r2 inverts r1's NFR-01: production code IS in scope; tests are NOT (except Skip→Pass transitions + Phase 4 Track C PoC)
- RB-T028-03/04/05/06 share one root cause — D-02 cluster exception applies

### Handoff Notes

*No handoff notes (initialization session)*

---

## Quick Reference

### Project Context

- **Project**: sdap.bff.api-test-suite-repair-r2
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) (will be created by `/task-create`)

### Applicable ADRs

(See CLAUDE.md "Binding ADRs" section for full list with relevance)

### Knowledge Files Loaded

- `spec.md` (this project)
- `design.md` (this project)
- `../sdap-bff.api-test-suite-repair/notes/lessons-learned.md` (r1 calibration)
- `../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` (the 20 entries)

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read CLAUDE.md (project-scoped AI context)
3. **Find next task**: Read `tasks/TASK-INDEX.md` for first 🔲 task
4. **Resume**: Invoke `task-execute` skill with that task file path

**Commands**:
- `/project-continue` — Full project context reload + master sync
- `/context-handoff` — Save current state before compaction
- "where was I?" — Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
