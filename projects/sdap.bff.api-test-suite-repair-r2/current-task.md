# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-01 (Task 037 Phase 3 P3-W2 — RB-T028-08 LOW closure executed; awaiting commit by main session; sibling 033 already complete in this wave)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Last commit (project work)**: `546ebcb3` — HEAD when task 037 began (no commits this task; main session bundles P3-W2: 033 + 037)
> **Last PR #318 activity**: tasks 010 / 011 / 012 (Phase 1) + 020 / 021 / 022 / 023 / 024 / 025 (Phase 2) + 030 / 031 / 032 / 034 / 035 / 036 (Phase 3 P3-W1) — Phase 3 P3-W2 closing (033 + 037 done)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 037 — Phase 3 P3-W2 — RB-T028-08 (LOW) repaired (sibling 033 already complete this wave) |
| **Step** | 10 of 10 (investigation confirmed fixture-config gap NOT subsumed by 011; TestUserId valid-GUID fix applied; test un-Skip'd; targeted run 6/6 PASS; full integration suite 370/0/52/422 zero regression; full unit suite 5927/0/109/6036 zero ripple; ledger row updated; Step 9.5 STANDARD/FULL gates PASS) |
| **Status** | completed-2026-06-01 (no commit by this task; main session bundles per project convention) |
| **Next Action** | Phase 3 P3-W2 complete (033 + 037 both ✅). Next: dispatch P3-W3 (task 038 — `Spe.Integration.Tests` triple-run / FR-10 — validates RB-T028-07 9-test slice + RB-T028-08 1-test slice). Then P3-W4 (task 039 — Phase 3 exit cumulative ledger audit). |

### Task 037 outcome (2026-06-01) — REPAIRED (fixture-config gap, NOT subsumed by 011)

- **Investigation verdict**: **FIXTURE-CONFIG GAP** (mirror of task 025 RB-T028-07 pattern). NOT subsumed by 011, NOT signature drift, NOT a production gap.
- **Root cause**: `IntegrationTestConstants.TestUserId` (line 27 of `IntegrationTestFixture.cs`) was `"test-user-00000000-0000-0000-0000-integration001"` — superficially GUID-shaped (47 chars) but starts with `test-user-` so `Guid.TryParse` returns FALSE. Production `PrecedentAdminEndpoints.CreatePrecedent` (lines 152-164) uses the caller's `oid` claim as the fallback reviewer-id when the request body's `reviewerByUserId` is null — via `Guid.TryParse(callerOid, out var callerGuid)`. With the non-GUID fixture literal, the fallback fails silently → `ReviewerByUserId` stays `null` → Moq predicate `r.ReviewerByUserId.HasValue && != Guid.Empty` is FALSE → "expected once, but was 0 times".
- **Verification before fix**: Ran the test post-Skip-removal at HEAD `546ebcb3` to capture the actual Moq failure. TRX showed `Performed invocations: IPrecedentBoard.CreateTentativeAsync(... ReviewerByUserId = , CancellationToken)` — the empty trailing value confirmed null reviewer, confirming the fixture-config hypothesis.
- **Test-only change**: `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` — `IntegrationTestConstants.TestUserId` (line 27) changed from `"test-user-00000000-0000-0000-0000-integration001"` to `"11111111-1111-1111-1111-111111111111"`. XML doc expanded (~lines 26-45) to document the Entra-ID `oid` contract and cross-reference RB-T028-08 / task 037. All consumers (both fake auth handlers' "oid" + NameIdentifier claims at lines 371/372 and 435/436) pick up the new value automatically.
- **Test transition**: `tests/integration/Spe.Integration.Tests/Api/Insights/PrecedentAdminEndpointsTests.cs` line 55 — `[Fact(Skip = "RB-T028-08: ...")]` → `[Fact]`; per-test `[Trait("status","real-bug-pending-fix")]` → `[Trait("status","repaired")]`.
- **No production code change**: `PrecedentAdminEndpoints.cs`, `IPrecedentBoard.cs`, `InsightsModule.cs`, `EndpointMappingExtensions.cs` all confirmed correct and unchanged.
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` succeeds, 0 errors, 2 warnings (pre-existing NU1903 Kiota CVEs; zero new); `dotnet build tests/integration/Spe.Integration.Tests/` succeeds, 0 errors, 3 warnings (pre-existing).
- **Targeted run (full class)**: `dotnet test --filter "FullyQualifiedName~PrecedentAdminEndpointsTests"` → **6 Passed / 0 Failed / 0 Skipped / 6 Total** (5 s). Re-enabled `PostPrecedent_AsAdmin_Returns_201_WithTentativeStatus` passes; 5 other tests in class still pass — no regression.
- **Full integration regression check**: `dotnet test tests/integration/Spe.Integration.Tests/` → **370 Passed / 0 Failed / 52 Skipped / 422 Total** (27 s) — zero regression from shared TestUserId change.
- **Full unit regression check**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → **5927 Passed / 0 Failed / 109 Skipped / 6036 Total** (1m12s) — zero cross-project ripple.
- **Ledger transition**: RB-T028-08 row → `repaired` with full root-cause documentation + Tests Skip'd → Pass row + verification metrics; resolution commit placeholder TBD pending main-session commit.
- **Step 9.5 quality gates** (effectively STANDARD per task 025 precedent — test-only change): code-review PASS (1 constant value change + XML doc expansion, minimal-blast-radius, mirrors task 025 RB-T028-07 fixture-config pattern, full regression sweep documented). adr-check PASS (ADR-010 / ADR-018 / ADR-001 / ADR-008 all NOT triggered — no DI/flag/endpoint/filter changes; `bff-extensions.md` not triggered — no new endpoint/package/DI/background work).
- **NFR-04 commit message (for main session)**: `fix(bff-api): correct TestUserId to valid-GUID format for oid-fallback contract (RB-T028-08; repaired)`
- **No commit by this task** — main session bundles per project convention.

### Task 033 outcome (2026-06-01) — REPAIRED

- **Production change**: `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs` — `RegulationPattern()` regex (line 78) relaxed inter-letter periods from mandatory to optional: `C\.F\.R\.?` → `C\.?F\.?R\.?`. Named groups (`title`, `part`) preserved; regex flags (Compiled / ExplicitCapture / IgnoreCase) + 500ms timeout preserved. XML doc updated (~lines 71-76) to document the no-period form contract per RB-T044-05.
- **Why a fix was needed**: Class XML doc line 15 explicitly lists `21 CFR Part 312` (no-period form) as a supported input, but the original regex required the period form `C.F.R.` (only the trailing period was optional). The no-period form was never matched, contradicting the documented contract.
- **Test change**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/CitationExtractorTests.cs` — `ExtractCitations_Regulation_NoPeriodForm_MatchedAndNormalized` (~line 157) flipped `[Fact(Skip = "...")]`→`[Fact]`; per-test `[Trait("status","real-bug-pending-fix")]` removed (class-level `[Trait("status","repaired")]` applies).
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` succeeds, 0 errors, 17 warnings (all pre-existing — NU1903 Kiota CVE × 2, CS0618 obsolete × 6, CS1998 async × 5, CS8601 × 2, CS8604 × 2; zero new warnings; none touch CitationExtractor.cs).
- **Targeted test run**: `dotnet test --filter "FullyQualifiedName~CitationExtractorTests"` → 30 Passed / 0 Failed / 0 Skipped / Duration 22ms. Confirms:
  - Re-enabled `ExtractCitations_Regulation_NoPeriodForm_MatchedAndNormalized` (RB-T044-05) PASSES.
  - 2 existing Regulation Theory cases (`47 C.F.R. § 73.3999`, `40 C.F.R. § 122.26`) still PASS — no regression.
  - Sibling-fix preservation verified: 4 CaseLaw Theory (task 020 RB-T044-02 NormalizeCaseLaw `.TrimEnd('.')` removal), 3 Statute Theory + 1 Statute strip-subsection Fact (task 032 RB-T044-03 NormalizeStatute paren-strip), 2 US Patent + 2 EP/WO Patent (task 021 RB-T044-04 NormalizePatent double-prefix removal) — ALL PASS. Tasks 020/021/032 fixes NOT disturbed.
- **Ledger transition**: `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` — RB-T044-05 row updated with Status=`repaired`, transition date 2026-06-01, "Tests Skip'd → Pass" row showing 30/30 targeted-run result; resolution commit placeholder TBD pending main-session commit.
- **Step 9.5 FULL rigor gates**: code-review PASS (5-char regex literal change, minimal-blast-radius, matches POML §goal + ledger §Recommended Fix verbatim, sibling fixes verified untouched), adr-check PASS (ADR-013 refined — change is INSIDE `Services/Ai/Safety/Citations/`, facade discipline preserved; ADR-015 — no LLM-text logging added; ADR-010 — no DI changes; bff-extensions.md §A all 5 rules satisfied — no new endpoint/service/DI/package/background work; §F test update obligation satisfied).
- **NFR-04 commit message (for main session)**: `fix(bff-api): RegulationPattern accepts CFR no-period form (RB-T044-05; repaired)`
- **No commit by this task** — main session bundles per project convention (sibling task 037 also in P3-W2).

### P3-W2 wave status

| Task | Ledger | File | Status |
|---|---|---|---|
| 033 | RB-T044-05 | CitationExtractor.cs (RegulationPattern) | ✅ repaired-2026-06-01 |
| 037 (this) | RB-T028-08 | IntegrationTestFixture.cs (TestUserId) + PrecedentAdminEndpointsTests.cs (Skip removed) | ✅ repaired-2026-06-01 |

**P3-W2 wave COMPLETE.** Both LOW closures landed; P3-W3 (task 038 integration triple-run) unblocked.

### Phase 3 closures running total (P3-W1 + P3-W2 partial)

| Task | Ledger | Status |
|---|---|---|
| 030 | (P3-W1) | ✅ repaired-2026-06-01 |
| 031 | (P3-W1) | ✅ repaired-2026-06-01 |
| 032 | RB-T044-03 | ✅ repaired-2026-06-01 |
| 033 | RB-T044-05 | ✅ repaired-2026-06-01 (this task) |
| 034 | (P3-W1) | ✅ repaired-2026-06-01 |
| 035 | RB-T070-01 | ✅ repaired-2026-06-01 |
| 036 | RB-T070-02 | ✅ repaired-2026-06-01 |
| 037 | RB-T028-08 | ✅ repaired-2026-06-01 (this task; fixture-config gap NOT subsumed by 011) |

### Files Modified This Session (task 037)

- `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` — `IntegrationTestConstants.TestUserId` constant changed from non-GUID literal to valid GUID `"11111111-1111-1111-1111-111111111111"`; XML doc expanded to document Entra-ID `oid` contract
- `tests/integration/Spe.Integration.Tests/Api/Insights/PrecedentAdminEndpointsTests.cs` — Skip attribute removed from `PostPrecedent_AsAdmin_Returns_201_WithTentativeStatus` (line 55); per-test trait transitioned `real-bug-pending-fix` → `repaired`
- `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` — RB-T028-08 row → `repaired` with full root-cause documentation
- `projects/sdap.bff.api-test-suite-repair-r2/tasks/037-fix-rb-t028-08.poml` — `<status>` → `completed-2026-06-01`
- `projects/sdap.bff.api-test-suite-repair-r2/tasks/TASK-INDEX.md` — 037 row → ✅ with FULL→STANDARD rigor note
- `projects/sdap.bff.api-test-suite-repair-r2/current-task.md` — this file

### Files Modified Earlier This Session (task 033 — completed prior, retained for traceability)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs` — production regex relaxation (RegulationPattern line 78 + XML doc lines 71-76)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/CitationExtractorTests.cs` — Skip removed + per-test trait removed (line 157)
- `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` — RB-T044-05 row → `repaired`
- `projects/sdap.bff.api-test-suite-repair-r2/tasks/033-fix-rb-t044-05.poml` — `<status>` → `completed-2026-06-01`

---

## Active Task (Full Details)

| Field | Value |
|---|---|
| **Task ID** | 033 — Phase 3 P3-W2 — RB-T044-05 (LOW) closure |
| **Task File** | [`tasks/033-fix-rb-t044-05.poml`](tasks/033-fix-rb-t044-05.poml) |
| **Title** | P3 — Fix RB-T044-05: CitationExtractor.RegulationPattern accept documented `CFR` (no-period) form |
| **Phase** | Phase 3 P3-W2 |
| **Status** | completed-2026-06-01 (REPAIRED) |
| **Started + Completed** | 2026-06-01 |

---

## Progress

### Completed Steps (task 033)

- [x] Step 1 — Loaded r1 ledger entry RB-T044-05 (lines 547-575); documented bug + recommended fix verified.
- [x] Step 2 — Verified task 032 commit landed (HEAD `546ebcb3` Phase 3 P3-W1 wave bundle; `NormalizeStatute` with subsection paren-strip confirmed at lines 174-186; `StatutePattern` regex with `[a-z]?(?:\([a-z0-9]+\))*` confirmed at line 45).
- [x] Step 3 — Read `CitationExtractor.cs` focusing on `RegulationPattern()` (line 74-78).
- [x] Step 4 — Applied minimal regex edit: `C\.F\.R\.?` → `C\.?F\.?R\.?` (5 chars added). XML doc updated to document RB-T044-05 contract. Named groups + flags + timeout preserved.
- [x] Step 5 — Build BFF: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors / 17 warnings (zero new).
- [x] Step 6 — Edit `CitationExtractorTests.cs`: removed `[Fact(Skip = "RB-T044-05: ...")]`→`[Fact]`; removed per-test `[Trait("status","real-bug-pending-fix")]`.
- [x] Step 7 — Targeted run: `dotnet test --filter "FullyQualifiedName~CitationExtractorTests"` → 30 Passed / 0 Failed / 0 Skipped.
- [x] Step 8 — Ledger row RB-T044-05 updated: status → `repaired`; fix-commit SHA placeholder TBD + date 2026-06-01.
- [x] Step 9 — (no commit by this task — main session bundles per NFR-04)
- [x] Step 9.5 — FULL rigor gates: code-review PASS + adr-check PASS.
- [x] Step 10 — `current-task.md` updated (this file).

### Current Step

Done. Awaiting main-session commit + sibling task 037 completion + next dispatch (P3-W3 task 038).

### Decisions Made

- 2026-06-01 (task 033): No `.claude/` writes (sub-agent boundary respected); no commits (main session bundles P3-W2 + remaining Phase 3 artifacts per project convention).

---

## Next Action

**Sibling task 037 completion + Phase 3 P3-W3 dispatch** (task 038 — `Spe.Integration.Tests` triple-run / FR-10 — validates RB-T028-07 9-test slice).

After P3-W3 completes:
- **Task 039** (P3-W4) — Phase 3 exit cumulative ledger audit (NOT a triple-run; confirms all Phase 1 + 2 + 3 closures).

**Trigger phrase to resume**: "continue" / "dispatch P3-W3" / "execute task 038" — CLAUDE.md §4 auto-routes to the next 🔲 task and into task-execute.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session (task 033)

- Started: 2026-06-01 (task-execute skill, FULL rigor)
- Focus: RB-T044-05 LOW closure — regex literal repair on CitationExtractor.RegulationPattern
- Outcome: REPAIRED — Skip→Pass on 1 unit test; 4 sibling fixes (tasks 020/021/032 + existing Regulation Theory) verified intact

### Key Learnings

- Single-character regex relaxation (`\.` → `\.?`) is the textbook minimal-blast-radius repair pattern for "literal vs documented form" contract bugs. Same pattern would apply to similar literal-form regex constraints in other CitationExtractor patterns if filed in future.
- The 4 same-file siblings (NormalizeCaseLaw / NormalizePatent / NormalizeStatute / NormalizeRegulation, plus CaseLawPattern / StatutePattern / PatentPattern / RegulationPattern / SecFilingPattern) are ORTHOGONAL — each repair touches one method/pattern only. The coordination protocol (serialize P3-W1 → P3-W2 by task 032 commit landing first) protected against merge race but the actual functional touch surface never overlapped.
- `dotnet test --filter "FullyQualifiedName~CitationExtractorTests"` runs 30 tests in 22ms — covers all 4 sibling repairs in a single targeted invocation. This makes it a perfect smoke gate for any future change to CitationExtractor.cs.

### Handoff Notes

**For next session** — task 037 is the remaining P3-W2 task (parallel-safe, different file). Then P3-W3 (task 038 integration triple-run) and P3-W4 (task 039 cumulative audit).

**If `/compact` runs**: this current-task.md is the SOURCE OF TRUTH. The Quick Recovery section + Next Action section together contain everything needed to resume.

---

## Quick Reference

### Project Context

- **Project**: sdap.bff.api-test-suite-repair-r2
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Phase 1 exit baseline**: [`baseline/phase1-exit-triple-run-2026-06-01.md`](baseline/phase1-exit-triple-run-2026-06-01.md)
- **Phase 2 exit baseline**: [`baseline/phase2-exit-triple-run-2026-06-01.md`](baseline/phase2-exit-triple-run-2026-06-01.md)

### Knowledge Files Loaded (task 033)

- `projects/sdap.bff.api-test-suite-repair-r2/tasks/033-fix-rb-t044-05.poml` (task spec for this task)
- `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` (RB-T044-05 entry — bug detail + Recommended Fix)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs` (production target — read pre + post fix)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/CitationExtractorTests.cs` (test target — read pre + post Skip flip)

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
