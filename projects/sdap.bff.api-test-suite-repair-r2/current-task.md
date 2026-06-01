# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-01 (Phase 1d.2 complete — security review request posted; task 011 work essentially complete pending external approval)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Last commit (project work)**: `85258885` — `docs(adr): ADR-030 — BFF Null-Object Kill-Switch Pattern (promoted from r2 draft)`
> **Last PR #318 activity**: issue-comment `4596627823` — Task 011 security review request (RB-T028-06 Auth-collateral)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 011 — RB-T028-03/04/05/06 HIGH cluster — **PHASE 1a/1b/1c/1d.1/1d.2 COMPLETE 2026-06-01** (Option B per E-01) |
| **Step** | All work complete; awaiting external security review approval from `dev@spaarke.com` on PR #318. |
| **Status** | phase-1d-pending-security-review |
| **Next Action** | **Wait for external approval** OR dispatch task 013 (Phase 1 P1-S3 exit triple-run) — note task 013 is BLOCKED on security review approval for merge but the test work itself can proceed in parallel. |

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
| **Task ID** | 011 (re-scoped to Option B per E-01 resolution 2026-06-01) |
| **Task File** | [`tasks/011-fix-rb-t028-cluster.poml`](tasks/011-fix-rb-t028-cluster.poml) |
| **Title** | RB-T028-03/04/05/06 HIGH cluster — Null-Object kill-switch fix (Option B) |
| **Phase** | Phase 1 P1-S2; sub-phases 1a complete / 1b pending / 1c pending / 1d pending |
| **Status** | phase-1a-complete; ready for Phase 1b implementation dispatch |
| **Started** | 2026-06-01 (Phase 1a) — investigation reverted 2026-06-01; re-scoped Option B 2026-06-01 |

### Sub-phase status

| Sub-phase | Status | Effort | Notes |
|---|---|---|---|
| **1a** Inventory + Design | ✅ COMPLETE 2026-06-01 (`d2cdce20`) | ~4h actual | 44 conditional services audited; 13 in-scope; ADR-030 drafted |
| **1b** Implementation (8 production commits) | ✅ COMPLETE 2026-06-01 | ~12h actual | Tier 1 (`d207ae93`) → Tier 2 (`1cfac08c`) → Tier 3 (`5613b8ad`) → Tier 1.5 r1 (`d932f355`) → Tier 1.5 r2 (`43ca4f9b`) → Tier 1.5 r3 (`dbd3888e`) → Tier 1.5 r4 (`56e74b84`) — 18 services migrated total (10 P3 + 8 P1) |
| **1c** Tests Skip→Pass + per-fix triple-run + Step 9.5 gates | ✅ COMPLETE 2026-06-01 (`08343e32`) | ~3h | 37 Passed / 0 Failed / 4 Skipped on focused integration; Failed: 0 × 3 on unit triple-run (5,902/129/6,031); ledger entries RB-T028-03/04/05/06 → `repaired`; Step 9.5: code-review PASS-WITH-CONCERNS (4 warnings), adr-check PASS (9 ADRs) |
| **1d.1** ADR-030 promotion | ✅ COMPLETE 2026-06-01 (`85258885`) | ~30min | Concise (`.claude/adr/`) + full (`docs/adr/`) + 2 INDEX updates |
| **1d.2** Security review request | ✅ COMPLETE 2026-06-01 (PR #318 comment `4596627823`) | ~15min | RB-T028-06 Auth-collateral; awaiting `dev@spaarke.com` approval |
| **1d.3** Project state files | 🔄 in-progress | ~10min | This file + TASK-INDEX |

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
- [⏸] **Task 011 ESCALATED** (Phase 1 P1-S2, FULL rigor, HIGH cluster, 4 RB IDs): Investigation surfaced a 5-layer root-cause cascade much wider than the r1 ledger's "shared root cause" framing captured. Layer 1 (NotificationService misregistered) → Layer 2 (IBriefingAi? param-inference) → Layer 3 (IInvoiceSearchService conditional) → Layer 4 (PendingPlanManager conditional on ChatEndpoints) → Layer 5+ (likely 10-20 more endpoint handlers). r1 ledger's preferred Approach 1 (conditional endpoint mapping) violates NFR-01 because tests expect endpoints to function with `Analysis:Enabled=false`. r1 ledger's alternative Approach 2 (NullObject services) requires ~10+ Null impls + new ADR (~20-30h, exceeds task estimate). All production code + test changes during investigation REVERTED to baseline. **Escalation filed**: [`escalations/E-01-rb-t028-cluster-scope-expansion.md`](escalations/E-01-rb-t028-cluster-scope-expansion.md). Owner decision: **Option B (Null-Object pattern)** selected 2026-06-01. — 2026-06-01
- [x] **Task 011 Phase 1a complete** (STANDARD rigor, inventory + design): comprehensive asymmetric-registration inventory built across 4 DI modules + 11 endpoint files. **Findings**: 44 conditional service registrations (across `DocumentIntelligence:Enabled`, `Analysis:Enabled`, `DocumentIntelligence:RecordMatchingEnabled`); 13 in-scope services for remediation (8 BLOCKING / 5 LATENT); 4 already-correct conditional pairs. Per-service Null-Object designs documented in 3 patterns: P1 Promote-to-unconditional (5 services), P2 Quiet no-op (0), P3 Fail-fast Null-Object (7 services), 1 endpoint refactor (B8 `SearchIndexClient`). 3 artifacts written: [`baseline/asymmetric-registration-inventory-2026-06-01.md`](baseline/asymmetric-registration-inventory-2026-06-01.md), [`decisions/D-09-nullobject-design.md`](decisions/D-09-nullobject-design.md), [`decisions/ADR-030-DRAFT-bff-nullobject-kill-switch.md`](decisions/ADR-030-DRAFT-bff-nullobject-kill-switch.md). Phase 1b implementation order: Tier 1 (Promote, 1.5h) → Tier 2 (P3 Null-Objects, 5h) → Tier 3 (concrete unseal + B8 refactor, 5h) → triple-runs + gates (~3.5h) ≈ 15h total. Expected Skip→Pass: 36 (corrected from r1 ledger's 37 — see inventory §5.A.1). — 2026-06-01

### Current Step

Task 011 Phase 1a complete. Owner reviewed E-01 escalation and selected **Option B (NullObject pattern)**. Phase 1a inventory + design + ADR-030 draft are pushed (commit `d2cdce20`). Phase 1b implementation (~15h) is the next significant work unit; benefits from a fresh-context session.

**PR #318 security review status**: APPROVED 2026-06-01 by `dev@spaarke.com` for task 010 (RB-T044-01). D-08 record captures the approval. The approval clears the eventual merge gate for task 010's work; subsequent HIGH-severity fixes (task 011's cluster touches RB-T028-06 Auth) will need their own security-review request on PR #318 when their work is complete.

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

**Next Step**: Dispatch **Task 011 Phase 1b — Tier 1 implementation** (Promote-to-unconditional, ~1.5h, lowest risk). Start here regardless of context budget. After Tier 1 lands cleanly, Tier 2 (~5h) and Tier 3 (~5h) dispatch as their own focused agents.

**Trigger phrase to resume in any session**: "**continue with task 011 Phase 1b**" or "**continue**" — CLAUDE.md §4 auto-detection will route to `task-execute` against task 011.

**Tier 1 — Promote-to-unconditional** (4 DI moves; lowest risk):
1. `NotificationService` — move from inside `AnalysisServicesModule.AddPlaybookServices` if-block to top-level (Layer 1 / B1 finding)
2. `ChatSessionManager` — promote (zero AI deps; B4)
3. `ChatHistoryManager` — promote (zero AI deps; B5)
4. `ChatDataverseRepository` (L5-bis if applicable) — promote per D-09 §X

**Tier 2 — P3 Null-Objects for facade services** (7 Null classes; ~5h):
See [`decisions/D-09-nullobject-design.md`](decisions/D-09-nullobject-design.md) §4 for the per-service order. Pattern: each Null impl throws `FeatureDisabledException` with a descriptive message; endpoints that consume the service catch and return 503 ServiceUnavailable with ProblemDetails (consistent with ADR-018).

**Tier 3 — Unseal sealed classes + `SearchIndexClient` refactor** (~5h):
- Unseal `SprkChatAgentFactory`; create `NullSprkChatAgentFactory` subclass (B2)
- Unseal `PendingPlanManager`; create `NullPendingPlanManager` (B3)
- Refactor `KnowledgeBaseEndpoints` to route through `IRagService` instead of injecting `SearchIndexClient` directly (B8 — incidental ADR-007 facade-alignment cleanup)

**After all 3 tiers + per-fix triple-run + Step 9.5 gates pass**:
- Phase 1c: tests Skip→Pass (36 expected); ledger transitions for RB-T028-03/04/05/06; commit + push
- Phase 1d: security review request on PR #318; main session promotes ADR-030-DRAFT to `.claude/adr/ADR-030-bff-nullobject-kill-switch.md` (sub-agent write boundary)

**Pre-conditions for Phase 1b**:
- All Phase 1a artifacts pushed ✅ (commit `d2cdce20`)
- ADR-030 draft authored (`decisions/ADR-030-DRAFT-bff-nullobject-kill-switch.md`) ✅
- D-09 Null-Object designs documented ✅
- Implementation order in D-09 §4 ✅
- Inventory in `baseline/asymmetric-registration-inventory-2026-06-01.md` ✅
- Branch `work/sdap.bff.api-test-suite-repair-r2` tracks origin (clean working tree)
- Build green: `dotnet build src/server/api/Sprk.Bff.Api/` should still return 0 errors (verify before Tier 1 start)

**Key Context for next session**:
- Owner direction (2026-06-01): "no r3 — all issues resolved in r2", "urgent BFF blocker — no delays"
- E-01 resolution: Option B chosen — Null-Object pattern (architecturally cleanest; ~15h total Phase 1b)
- All NFRs apply: NFR-01 inverted (production IS in scope); NFR-02 <50% per file; NFR-03 security review for HIGH (`dev@spaarke.com`); NFR-04 commit cites ledger IDs; D-03 FULL rigor gates
- Sub-agent write boundary applies to ADR-030 promotion (Phase 1d only — `.claude/adr/` is main-session-only)

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

### Handoff Notes (2026-06-01 milestone checkpoint)

**Session summary** — completed in this session:
1. `/project-pipeline` Steps 0-4: full project initialization (artifacts, 36 POML tasks, branch + draft PR #318)
2. Resolved all 5 spec.md unresolved questions (security reviewer / Insights contact / Phase 4 staffing / CI rationalization / no-r3)
3. Phase 0 P0-W1 parallel wave (tasks 000 + 001 + 002 → all ✅)
4. Phase 1 task 010 (RB-T044-01 cross-matter privilege leak): production fix + security review APPROVED by `dev@spaarke.com` (D-08)
5. Phase 1 task 012 (RB-T028-02 Insights HOLD path b): production fix in `GroundingVerifier.cs` (NOT in cited `Layer2OutcomeExtractor.cs` — r1 ledger location was wrong); test changes; ledger transitioned; D-07 finalized; task 026 deferred
6. Task 011 escalated (E-01) — 5-layer cascade discovered; investigation reverted to baseline; owner chose Option B
7. Task 011 Phase 1a: complete asymmetric-registration inventory + Null-Object designs + ADR-030 draft

**What's NOT done** (Phase 1b/1c/1d remain):
- Tier 1 promote-to-unconditional (4 DI moves; lowest risk; ~1.5h) — START HERE
- Tier 2 P3 Null-Object impls (7 classes; ~5h)
- Tier 3 unseal sealed classes + `SearchIndexClient` refactor (~5h)
- Tests Skip→Pass (36 transitions across `KnowledgeBaseEndpointsTests`, `ChatEndpointsTests`, `ReAnalysisFlowTests`, `AuthorizationIntegrationTests`)
- Per-fix triple-run validation (3 × `dotnet test --logger trx`)
- Step 9.5 quality gates (code-review + adr-check)
- Security review request on PR #318 for the cluster fix (RB-T028-06 Auth implications)
- ADR-030 promotion from `decisions/` draft to `.claude/adr/` (main-session-only)

**Critical reminders for recovery**:
- Owner = `ralph.schroeder@hotmail.com`; sibling/security contact = `dev@spaarke.com` (same person)
- PR #318 is the bundle PR for ALL r2 work — don't open a separate PR for task 011
- Phase 1 is sequential within phase; Phase 2+ can parallelize where files disjoint (see TASK-INDEX)
- After Phase 1b completes, dispatch Phase 1c (test transitions) + 1d (security review) before unblocking task 013 (Phase 1 exit triple-run)
- TASK-INDEX.md was hand-edited inline (status icons + task 026 marked deferred); don't revert those manual edits

**If `/compact` runs**: this current-task.md is the SOURCE OF TRUTH. The Quick Recovery section + this Handoff Notes section together contain everything needed to resume. The CLAUDE.md trigger phrase "continue with task 011 Phase 1b" routes correctly via §4.

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
