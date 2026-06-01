# Task Index — sdap.bff.api-test-suite-repair-r2

> **Last Updated**: 2026-06-01 (project initialization via `/project-pipeline`)
> **Total Tasks**: 36 (3 Phase 0 + 4 Phase 1 + 8 Phase 2 + 10 Phase 3 + 5 Phase 4 + 5 Phase 5 + 1 wrap-up)
> **Wrap-up Task**: 090-project-wrap-up.poml (MANDATORY final task)
> **Hard Deadline**: 2026-08-31

---

## Status Legend

- 🔲 not-started
- 🟡 in-progress
- ⏸ blocked
- ✅ completed
- ⏭ deferred
- 🔄 needs retry (from a failed parallel wave)

---

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel-Group | Rigor | Prod-Fix |
|---|---|---|---|---|---|---|---|
| 000 | Capture r1 close-out baseline | 0 | 🔲 | none | P0-W1 | STANDARD | — |
| 001 | Verify 20 real-bug entries reproducible | 0 | 🔲 | 000 | P0-W1 | STANDARD | — |
| 002 | Sibling-owner outreach (Action Engine, Insights, Communications) | 0 | ✅ | 000 | P0-W1 | MINIMAL | — |
| 010 | Fix RB-T044-01 — `ConversationHistorySanitizer` cross-matter privilege leak (HIGH) | 1 | 🔲 | 000, 001, 002 | (sequential) | FULL | ✓ |
| 011 | Fix RB-T028-03/04/05/06 cluster — conditional registration root cause (HIGH × 4) | 1 | 🔲 | 000, 001 | (sequential, after 010) | FULL | ✓ |
| 012 | Resolve RB-T028-02 Insights Layer 2 HOLD (FR-05) | 1 | 🔲 | 002 | P1-W1 | FULL | ✓ |
| 013 | Phase 1 exit triple-run validation gate | 1 | 🔲 | 010, 011, 012 | (sequential, phase-exit) | STANDARD | — |
| 020 | Fix RB-T044-02 — `CitationExtractor.NormalizeCaseLaw` reporter period | 2 | 🔲 | 013 | P2-W1 | FULL | ✓ |
| 021 | Fix RB-T044-04 — `NormalizePatent` EP/WO double-prefix | 2 | 🔲 | 013, 020 | (sequential after 020 — same file) | FULL | ✓ |
| 022 | Fix RB-T053-01 — `CapabilityRouter` Layer-1 classifier (3-option owner decision) | 2 | 🔲 | 013 | P2-W1 | FULL | ✓ |
| 023 | Fix RB-T070-03 — `AnalysisChatContextResolver` dead-path (restore-or-delete owner decision) | 2 | 🔲 | 013 | P2-W1 | FULL | ✓ |
| 024 | Fix RB-T028-01 — `AnalysisContextBuilder` non-deterministic sort | 2 | 🔲 | 013 | P2-W1 | FULL | ✓ |
| 025 | Fix RB-T028-07 — Upload endpoint binding (verify subsumed by 011 first) | 2 | 🔲 | 011, 013 | P2-W1 | FULL | ✓ |
| 026 | Fix RB-T028-02 fallback (conditional — only if 012 outcome = "we-take-bug") | 2 | 🔲 | 012 | P2-W1 (conditional) | FULL | ✓ |
| 029 | Phase 2 exit triple-run validation gate | 2 | 🔲 | 020, 021, 022, 023, 024, 025, 026 | (sequential, phase-exit) | STANDARD | — |
| 030 | Fix RB-T012-01 — `SessionRestoreService` quote handling | 3 | 🔲 | 029 | P3-W1 | FULL | ✓ |
| 031 | Fix RB-T034-01 — `AgentConfigurationService` CancellationToken | 3 | 🔲 | 029 | P3-W1 | FULL | ✓ |
| 032 | Fix RB-T044-03 — `NormalizeStatute` subsection trim | 3 | 🔲 | 029 | P3-W1 | FULL | ✓ |
| 033 | Fix RB-T044-05 — `RegulationPattern` CFR no-period | 3 | 🔲 | 029, 032 | (sequential after 032 — same file CitationExtractor.cs) | FULL | ✓ |
| 034 | Fix RB-T050-01 — `SourcePaneSseEventData.CitationId` JsonIgnore | 3 | 🔲 | 029 | P3-W1 | FULL | ✓ |
| 035 | Fix RB-T070-01 — `AgentConversationService` CancellationToken (3 methods) | 3 | 🔲 | 029 | P3-W1 | FULL | ✓ |
| 036 | Fix RB-T070-02 — `R2SseEventEmitter` RetryAfterSeconds null omission | 3 | 🔲 | 029 | P3-W2 | FULL | ✓ |
| 037 | Fix RB-T028-08 — PrecedentAdmin endpoint binding (verify subsumed by 011) | 3 | 🔲 | 029, 011 | P3-W2 | FULL | ✓ |
| 038 | `Spe.Integration.Tests` triple-run (FR-10; flake quarantine ≤2) | 3 | 🔲 | 030-037 | (sequential, phase-exit) | STANDARD | — |
| 039 | Phase 3 exit validation — cumulative ledger audit | 3 | 🔲 | 038 | (sequential, phase-exit) | STANDARD | — |
| 040 | Track A — PCF/Code Pages test rot audit (read-only) | 4 | 🔲 | 039 | P4-W1 | STANDARD | — |
| 041 | Track B — Mutation testing pilot (Stryker.NET on Services/Ai/Safety) | 4 | 🔲 | 039 | P4-W1 | STANDARD | — |
| 042 | Track C — TestClock + seeded-Guid PoC in Services/Workspace | 4 | 🔲 | 039 | P4-W1 | FULL | — |
| 043 | Track D — Coverlet baseline measurement (waits on `github-actions-rationalization-r1`) | 4 | 🔲 | 039 + external | P4-W1 | STANDARD | — |
| 044 | Track E — Anti-drift effectiveness report (NFR-07 publish-regardless) | 4 | 🔲 | 039 | P4-W1 | STANDARD | — |
| 080 | Update `docs/procedures/testing-and-code-quality.md` (ledger lifecycle + TestClock pattern + Track E findings) | 5 | 🔲 | 040-044 | P5-W1 | STANDARD | — |
| 081 | Extend `.claude/constraints/bff-extensions.md` § F (CONDITIONAL; **MAIN-SESSION-ONLY**) | 5 | 🔲 | 080, 011 | **sequential (.claude/ boundary)** | FULL | — |
| 082 | Final triple-run validation — 6 TRX (FR-15) | 5 | 🔲 | 080, 081 | (sequential, phase-exit) | STANDARD | — |
| 083 | PR + admin-merge cycle (FR-16; merge ≤ 2026-08-31) | 5 | 🔲 | 082 | (sequential) | STANDARD | — |
| 084 | `doc-drift-audit` after procedure updates | 5 | 🔲 | 080, 081 | P5-W2 | STANDARD | — |
| 090 | **Project Wrap-up** — quality gates + repo-cleanup + README/plan close + lessons-learned + exit-ledger | wrap-up | 🔲 | 083, 084 | (sequential, FINAL) | FULL | — |

---

## Parallel Execution Plan

Tasks within a wave run concurrently; waves are sequential. **Hard cap: 6 agents per wave** (per CLAUDE.md §5 / project-pipeline Step 5). Build verification (`dotnet build src/server/api/Sprk.Bff.Api/`) is **mandatory between waves touching `.cs` files**.

### Phase 0 — Project Setup (1 wave)

| Wave | Agents | Tasks | Prerequisite | Files Touched | Notes |
|---|---|---|---|---|---|
| P0-W1 | 3 | 000, 001, 002 | none | `projects/.../baseline/`, `decisions/` (disjoint) | Outreach independent from baseline + reproduction |

### Phase 1 — HIGH Severity (3 waves; SEQUENTIAL within Phase 1)

| Wave | Agents | Tasks | Prerequisite | Files Touched | Notes |
|---|---|---|---|---|---|
| P1-S1 | 1 | 010 | P0-W1 + security reviewer assigned (NFR-03) | `Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs` | **Sequential — security-sensitive** |
| P1-S2 | 1 | 011 | 010 ✅ | DI registration paths (single file edit) | **Sequential — D-02 cluster exception; one production change closes 4 entries** |
| P1-W1 | 1 | 012 | P0-W1 + sibling response | `Services/Ai/Insights/Layer2/...` OR decision-only | Parallel-with-other-Phase-1 only after 010+011 done; effectively serial in practice |
| P1-S3 | 1 | 013 | 010, 011, 012 ✅ | TRX artifacts (no code) | Phase 1 exit gate |

### Phase 2 — MEDIUM Severity (3 waves)

| Wave | Agents | Tasks | Prerequisite | Files Touched | Notes |
|---|---|---|---|---|---|
| P2-W1 | 5 | 020, 022, 023, 024, 025, 026* | 013 ✅ | Disjoint Services/ files | *026 conditional on 012 outcome (b); skips if (a)/(c) |
| P2-W2 | 1 | 021 | 020 ✅ | `Services/Ai/Citations/CitationExtractor.cs` | **Sequential after 020 — same file** |
| P2-W3 | 1 | 029 | 020, 021, 022, 023, 024, 025, 026 ✅ | TRX artifacts | Phase 2 exit gate |

### Phase 3 — LOW Severity + Integration Stability (4 waves)

| Wave | Agents | Tasks | Prerequisite | Files Touched | Notes |
|---|---|---|---|---|---|
| P3-W1 | 6 | 030, 031, 032, 034, 035, 036 | 029 ✅ | Disjoint Services/ files (6-agent cap) | |
| P3-W2 | 2 | 033, 037 | 029 + (033 needs 032 ✅) | 033 same file as 032; 037 independent | 033 sequential after 032 |
| P3-W3 | 1 | 038 | 030-037 ✅ | TRX artifacts (integration tests) | FR-10 triple-run gate |
| P3-W4 | 1 | 039 | 038 ✅ | summary doc only | Phase 3 exit |

### Phase 4 — Quality Lift + Audits + Pilots (1 wave)

| Wave | Agents | Tasks | Prerequisite | Files Touched | Notes |
|---|---|---|---|---|---|
| P4-W1 | 5 | 040, 041, 042, 043, 044 | 039 ✅ | All disjoint domains (audits/, baseline/, Services/Workspace, sdap-ci.yml, baseline/) | Track D (043) may slip to Phase 5 if `github-actions-rationalization-r1` Phase 1 unlanded |

### Phase 5 — Governance + Close (3 waves + wrap-up)

| Wave | Agents | Tasks | Prerequisite | Files Touched | Notes |
|---|---|---|---|---|---|
| P5-W1 | 1 | 080 | P4-W1 ✅ | `docs/procedures/testing-and-code-quality.md` | docs/ is parallel-safe; only one P5-W1 task in this case |
| P5-S1 | 1 (main session) | 081 | 080, 011 ✅ | `.claude/constraints/bff-extensions.md` | **MAIN-SESSION-ONLY — `.claude/` boundary per CLAUDE.md §3**; CONDITIONAL |
| P5-W2 | 1 | 084 | 080, 081 ✅ | audit doc (disjoint from 082/083) | Parallel-with-082/083 |
| P5-S2 | 1 | 082 | 080, 081 ✅ | TRX artifacts (6 files) | Final triple-run (FR-15) |
| P5-S3 | 1 | 083 | 082 ✅ | PR + admin-merge | FR-16 (merge ≤ 2026-08-31); NFR-03 admin-merge window |
| Final | 1 | 090 | 083, 084 ✅ | README, plan.md, lessons-learned, exit-ledger | MANDATORY wrap-up; FULL rigor |

---

## Critical Path

```
P0-W1 (000+001+002, parallel) → P1-S1 (010) → P1-S2 (011) → P1-S3 (013) → P2-W1 (5-agent) → P2-W2 (021) → P2-W3 (029) → P3-W1 (6-agent) → P3-W2 (2-agent) → P3-W3 (038) → P3-W4 (039) → P4-W1 (5-agent) → P5-W1 (080) → P5-S1 (081) → P5-S2 (082) → P5-S3 (083) → Final (090)
```

**Total critical-path steps**: ~14 (each P1-Sx is sequential; Phase 4 collapses into 1 wave).

**Estimated calendar**: Phase 0 (Wk 1) · Phase 1 (Wk 2-4) · Phase 2 (Wk 5-7) · Phase 3 (Wk 8-9) · Phase 4 (Wk 10-12) · Phase 5 (Wk 13) — landing on or before 2026-08-31.

---

## Sequential Gates (intentional)

These tasks intentionally do NOT parallelize, by design:

| Task | Reason |
|---|---|
| 010 | HIGH security fix; mandatory security review (NFR-03); needs dedicated focus |
| 011 | RB-T028 cluster — single registration-path edit; parallel agents would race on the same file (D-02 cluster exception applies) |
| 013, 029, 038, 039, 082 | Phase-exit / integration / final triple-run validation gates |
| 021 | File-overlap with 020 (`CitationExtractor.cs`) |
| 033 | File-overlap with 032 (`CitationExtractor.cs`) |
| 081 | **Touches `.claude/` — MAIN-SESSION-ONLY per CLAUDE.md §3 sub-agent write boundary**. CONDITIONAL: only runs if Phase 1 RB-T028 root cause warrants a new binding rule. |
| 083 | PR + admin-merge gate (FR-16) |
| 090 | Final wrap-up |

---

## Ledger Entry Coverage

All 20 real-bug ledger entries from `../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` map to specific r2 tasks:

| Severity | Entry | Task |
|---|---|---|
| HIGH | RB-T044-01 | 010 |
| HIGH | RB-T028-03 | 011 (cluster) |
| HIGH | RB-T028-04 | 011 (cluster) |
| HIGH | RB-T028-05 | 011 (cluster) |
| HIGH | RB-T028-06 | 011 (cluster) |
| MED | RB-T028-02 | 012 (Phase 1) + 026 (Phase 2 fallback) |
| MED | RB-T044-02 | 020 |
| MED | RB-T044-04 | 021 |
| MED | RB-T053-01 | 022 |
| MED | RB-T070-03 | 023 |
| MED | RB-T028-01 | 024 |
| MED | RB-T028-07 | 025 (may be subsumed by 011) |
| LOW | RB-T012-01 | 030 |
| LOW | RB-T034-01 | 031 |
| LOW | RB-T044-03 | 032 |
| LOW | RB-T044-05 | 033 |
| LOW | RB-T050-01 | 034 |
| LOW | RB-T070-01 | 035 |
| LOW | RB-T070-02 | 036 |
| LOW | RB-T028-08 | 037 (may be subsumed by 011) |

**Coverage**: 20 / 20 ✓

---

## How to Execute

**Sequential tasks**: invoke `task-execute` skill with the task POML path.

**Parallel waves**: send ONE message with MULTIPLE `Skill` tool invocations (one per task in the wave). Each `task-execute` invocation handles its own context loading.

**Between waves touching `.cs` files**: main session MUST run `dotnet build src/server/api/Sprk.Bff.Api/` and STOP if it fails. Do not dispatch next wave on a broken build.

**Failed task in a wave**: mark as 🔄 (needs retry) — NOT ❌ (abandoned). Main session decides retry-or-stop at wave boundary.

**`.claude/`-touching tasks (081)**: MAIN SESSION ONLY. Sub-agents will fail with "Edit denied" — that's the boundary working correctly. See CLAUDE.md §3.

**Context management**: checkpoint after every group (not every task); > 60% → `/checkpoint` + `/compact`; > 70% → STOP.

---

## Trigger Phrases (per CLAUDE.md §4)

| User Says | Required Action |
|---|---|
| "continue" / "next task" | Find first 🔲 in this index, invoke `task-execute` |
| "work on task X" | Invoke `task-execute` with the POML |
| "execute Phase 1 wave" | Dispatch all parallel P1-W1 tasks via multi-Skill message |
| "where was I?" | Load `current-task.md` first, then this index |

---

*Updated by `task-execute` after each task completion (status 🔲 → ✅). Re-read after every wave to find next pending task.*
