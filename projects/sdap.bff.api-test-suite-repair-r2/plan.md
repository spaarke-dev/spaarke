# Project Plan: sdap.bff.api-test-suite-repair-r2

> **Last Updated**: 2026-06-01
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)
> **Predecessor**: [`sdap-bff.api-test-suite-repair`](../sdap-bff.api-test-suite-repair/) (r1, closed 2026-06-01)

---

## 1. Executive Summary

**Purpose**: Close the 20 real-bug ledger entries surfaced by r1; validate `Spe.Integration.Tests` stability; measure anti-drift effectiveness; pilot four quality improvements (PCF audit / mutation testing / TestClock-Guid PoC / Coverlet baseline). Hard deadline 2026-08-31.

**Scope**:
- 20 real-bug production fixes (Phase 1-3)
- Insights Layer 2 HOLD resolution (Phase 1)
- `Spe.Integration.Tests` triple-run validation (Phase 3)
- 5 Phase-4 parallel quality-improvement tracks
- Phase 5 governance updates + final triple-run + close

**Timeline**: 13 weeks (2026-06-01 → 2026-08-31) | **Estimated POML count**: 35-45 tasks; multi-wave parallel dispatch capped at 6 agents per wave

---

## 2. Architecture Context

### Design Constraints (Binding ADRs)

- **ADR-001** (Minimal API + Workers) — endpoint registration patterns for RB-T028 cluster
- **ADR-007** (SpeFileStore facade) — relevant for LOW entries touching file operations (RB-T012-01 SessionRestoreService)
- **ADR-008** (endpoint filters) — auth-filter pattern (RB-T028-06)
- **ADR-010** (DI minimalism) — **DIRECTLY binding** for RB-T028-03/04/05/06 conditional-registration root cause
- **ADR-013 refined 2026-05-20** (AI extends BFF) — RB-T044-* (AI/Safety) + RB-T053-01 (CapabilityRouter)
- **ADR-018** (kill switches) — **DIRECTLY binding** for RB-T028 cluster (feature-flag application question)
- **ADR-021** (Fluent design / dark mode) — Phase 4 Track A PCF audit reference
- **ADR-022** (PCF platform libraries) — Phase 4 Track A React 16/17 vs 19 boundary
- **ADR-028** (Spaarke Auth v2) — RB-T028-06 Auth tests
- **ADR-029** (BFF Publish Hygiene) — FR-14 Coverlet must preserve baseline

### From Spec (NFRs)

- NFR-01 (r2): Production code changes ARE in scope. Tests modified ONLY for Skip→Pass transitions or Phase 4 Track C PoC
- NFR-02: Each production fix <50% line replacement per file
- NFR-03: HIGH-severity entries require security-review approval in PR before merge
- NFR-04: Every closure commit cites ledger entry ID + resolution mode
- NFR-05: Triple-run validation mandatory before each named phase exit
- NFR-09: Task POMLs declare `<production-fix-per-ledger>true</production-fix-per-ledger>` for production changes (NEW for r2)
- NFR-10: Per-file disjoint sets for parallel agent dispatch (6-agent cap)
- NFR-11: No test may end in `Failed` state at any phase exit

### Discovered Resources (Step 2 Part 1 of `/project-pipeline`)

**Applicable ADRs** (full content auto-loaded per task via `adr-aware`):
ADR-001, ADR-007, ADR-008, ADR-010, ADR-013, ADR-018, ADR-021, ADR-022, ADR-028, ADR-029

**Applicable Skills**:
- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — mandatory per CLAUDE.md §4 (every task uses)
- [`task-create`](../../.claude/skills/task-create/SKILL.md) — task decomposition (Step 3)
- [`code-review`](../../.claude/skills/code-review/SKILL.md) — Step 9.5 FULL-rigor quality gate
- [`adr-check`](../../.claude/skills/adr-check/SKILL.md) — Step 9.5 ADR-010/018 compliance check
- [`adr-aware`](../../.claude/skills/adr-aware/SKILL.md) — proactive ADR loading per task
- [`ci-cd`](../../.claude/skills/ci-cd/SKILL.md) — Phase 4 Track D (sdap-ci.yml Coverlet)
- [`conflict-check`](../../.claude/skills/conflict-check/SKILL.md) — Phase 0 coordination with `github-actions-rationalization-r1`
- [`context-handoff`](../../.claude/skills/context-handoff/SKILL.md) — proactive checkpointing per CLAUDE.md §5
- [`push-to-github`](../../.claude/skills/push-to-github/SKILL.md) — per-phase exit commits
- [`merge-to-master`](../../.claude/skills/merge-to-master/SKILL.md) — Phase 5 close
- [`repo-cleanup`](../../.claude/skills/repo-cleanup/SKILL.md) — wrap-up
- [`doc-drift-audit`](../../.claude/skills/doc-drift-audit/SKILL.md) — Phase 5 post-procedure-update audit

**Knowledge Docs + Patterns**:
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **binding workflow per CLAUDE.md §10**; FR-08 may extend § F
- `.claude/constraints/{testing,api,auth,ai,azure-deployment,pcf}.md` — topic-scoped MUST/MUST-NOT rules
- [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md) — **modified by FR-07 + FR-13**
- `docs/procedures/{ci-cd-workflow,CODE-REVIEW-BY-MODULE,context-recovery}.md`
- `docs/standards/{CODING-STANDARDS,ANTI-PATTERNS,INTEGRATION-CONTRACTS}.md` — conditional-registration is documented anti-pattern
- `.claude/patterns/api/{service-registration,endpoint-definition,endpoint-filters,background-workers}.md` — RB-T028 fixes
- `.claude/patterns/testing/{integration-tests,mocking-patterns,unit-test-structure}.md` — fixes + FR-10 triple-run
- `.claude/patterns/auth/*` (13 files) — RB-T028-06
- `.claude/patterns/ai/*` (5 files) — RB-T044-*, RB-T053-01

**Canonical implementations to reference**:
- **No existing TestClock/IClock/ISystemClock/TimeProvider usage in `src/`** — Phase 4 Track C is greenfield
- 2 existing test files use TimeProvider (reference only): `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionSyncTests.cs`, `Services/Ai/Insights/Ingest/IngestOrchestratorTests.cs`
- **No existing seeded-Guid provider** — Phase 4 Track C greenfield
- **Workspace PoC target**: 10 files / 4194 LOC in `src/server/api/Sprk.Bff.Api/Services/Workspace/`
- **Safety target** (Phase 4 Track B): 13 files in `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/`
- **Coverlet ALREADY active in CI** (`.github/workflows/sdap-ci.yml` lines 85-102; `config/coverlet.runsettings`). FR-14 is minimal: surface % per project; do NOT add threshold

**Scripts**:
- `scripts/Capture-BffBaseline.ps1` — synthetic baseline of every BFF endpoint
- `scripts/Test-SdapBffApi.ps1` — BFF API smoke test
- `scripts/Test-SessionRestoreLatency.ps1` — relevant for RB-T012-01
- `scripts/Deploy-BffApi.ps1` — Phase 5 optional
- No dedicated triple-run wrapper; uses `dotnet test` directly per r1 task 084 pattern

**r1 Calibration** (from [`../sdap-bff.api-test-suite-repair/notes/lessons-learned.md`](../sdap-bff.api-test-suite-repair/notes/lessons-learned.md)):

- **Sibling-fixture pattern is canonical**: 5 fixture sites share 7 missing DI config keys. r2 loads `docs/procedures/testing-and-code-quality.md` upfront — does NOT re-discover.
- **RB-T028 cluster (5 HIGH) = one root cause**: conditional service registration + unconditional endpoint mapping. ONE production change can close 4 entries (D-02 cluster exception).
- **Cluster-level classification scales**: r1 task 028 dispositioned 47 residuals via ~6 cluster entries. Use cluster commits where root cause shared.
- **"Absorb deviations, don't defer"**: r1 added additive `<notes>` to POMLs rather than re-planning mid-task. r2 follows for Phase 4 audit findings.
- **No CI-script anti-drift was right call**: constraint docs + PR template + reviewer judgment sufficed. FR-09 measures empirically; do NOT add CI script even if findings push that way.
- **Triple-run validation pattern (r1 task 084) is canonical**: same protocol for FR-10 (integration) + FR-15 (final).
- **Parallel dispatch: 13 waves at 6-agent cap with anti-parallelism guard on shared fixtures**. r2 Phase 1 (RB-T028 cluster) is sequential. Phases 2-5 parallelize where disjoint.
- **Repair-not-rewrite escalation rate 1.23%** (1/81 files) — r2 NFR-02 keeps <50% line-replacement rule.

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| **D-01**: NFR-01 RELAXED — `src/` changes IN scope | r1 was test-only by design; r2 mandate is production fix | Tasks include `<production-fix-per-ledger>true</production-fix-per-ledger>` (NEW for r2) |
| **D-02**: One fix = one entry closed; cluster exception for shared root cause | Clean attribution; RB-T028-03/04/05/06 share root cause | ONE PR can close 4 entries simultaneously |
| **D-03**: HIGH gets security review; MED + LOW get FULL-rigor | RB-T044-01 cascade taught: "obvious" fixes still need scrutiny | NFR-03 PR-merge gate |
| **D-04**: Phase 4 tracks are pilot-grade | Protect 2026-08-31 deadline; full execution = r3 | Audit docs + score reports + PoC; NO remediation |
| **D-05**: Real-bug ledger is source of truth | Per-entry state transitions auditable | TASK-INDEX shows ledger-entries-closed alongside task-complete |
| **D-06**: r3 NOT pre-committed | Decision based on r2 findings | Phase 5 produces r3 scope recommendation, not r3 plan |

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Project Setup + Baseline           (1 week  | 2026-06-01 → 2026-06-08)
└─ Reproduce 20 entries; sibling outreach; artifacts authored

Phase 1: HIGH Severity + Insights HOLD      (3 weeks | 2026-06-09 → 2026-06-29)
└─ LEADS — Phase 2-5 BLOCKED until exit
└─ SEQUENTIAL within phase (RB-T028 cluster shared root cause)

Phase 2: MEDIUM Severity                    (3 weeks | 2026-06-30 → 2026-07-20)
└─ 7 MED entries; parallel waves within 6-agent cap

Phase 3: LOW Severity + Integration Stab.   (2 weeks | 2026-07-21 → 2026-08-03)
└─ 8 LOW entries; Spe.Integration.Tests triple-run

Phase 4: Quality Lift + Audits + Pilots     (3 weeks | 2026-08-04 → 2026-08-24)
└─ 5 parallel tracks: PCF audit / mutation / TestClock / Coverlet / anti-drift

Phase 5: Governance + Close                 (1 week  | 2026-08-25 → 2026-08-31)
└─ Docs updates; final triple-run; PR + admin-merge; lessons-learned
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 BLOCKS Phases 2-5 (RB-T028 cluster root-cause fix may unblock other entries)
- Phase 4 Track D (Coverlet) BLOCKED BY `github-actions-rationalization-r1` Phase 1 (external)
- Phase 5 § F constraint extension BLOCKED BY Phase 1 RB-T028 root-cause analysis
- Final triple-run (FR-15) BLOCKS PR merge (NFR-05)

**High-Risk Items:**
- RB-T044-01 cross-matter privilege-leak fix — Mitigation: mandatory security review (NFR-03); regression tests; staged dev-env validation
- RB-T028 cluster root-cause fix breaking other endpoints — Mitigation: per-fix triple-run; one flag at a time; automated revert
- Sibling project unresponsive on RB-T028-02 — Mitigation: 1-week timeout → `archived-pending-sibling-engagement`
- Mutation testing finds 50+ weak assertions — Mitigation: D-04 caps r2 to top-10; remediation = r3

---

## 4. Phase Breakdown

### Phase 0: Project Setup + Baseline (Week 1 | 2026-06-01 → 2026-06-08)

**Objectives:**
1. Capture r1 close-out state as r2 baseline
2. Confirm all 20 real-bug entries reproducible (no regression-disguised-as-Skip)
3. Author project artifacts (this plan + tasks)
4. Sibling-owner outreach (Action Engine, Insights, Communications) — FR-06
5. Confirm `github-actions-rationalization-r1` status (FR-14 sequencing)

**Deliverables:**
- [ ] `baseline/r1-closeout-2026-06-01.md` (r1 state snapshot)
- [ ] `baseline/20-entries-reproducibility-verification.md`
- [ ] 35-45 POML tasks in `tasks/` (via `task-create`)
- [ ] `decisions/D-07-insights-layer2-resolution-path.md` (after sibling response)
- [ ] Sibling-outreach captures in `decisions/owner-responses/`

**Critical Tasks:**
- 000-capture-r1-baseline (FIRST — measurement reference)
- 001-verify-20-bugs-reproducible (MUST complete before Phase 1)
- 002-sibling-owner-outreach (parallel — independent communication threads)

**Inputs**: r1 ledgers, r1 lessons-learned, design.md, spec.md
**Outputs**: Baseline docs; reproducibility verification; sibling responses

---

### Phase 1: HIGH Severity + Insights HOLD (Weeks 2-4 | 2026-06-09 → 2026-06-29)

**Objectives:**
1. Fix RB-T044-01 (cross-matter privilege-leak) with security review (NFR-03)
2. Fix RB-T028-03/04/05/06 (4-entry cluster, shared root cause) via single production change
3. Resolve RB-T028-02 (Insights Layer 2 HOLD)

**Deliverables:**
- [ ] RB-T044-01 production fix + security-review record + regression tests (5 tests flip Skip → Pass)
- [ ] RB-T028-03/04/05/06 root-cause fix (37 tests flip Skip → Pass)
- [ ] RB-T028-02 resolution (3 tests: transferred / fixed / archived)
- [ ] Phase 1 exit triple-run artifact in `baseline/`

**Critical Tasks:**
- 010-fix-rb-t044-01 (security-sensitive; SEQUENTIAL; full quality gate)
- 011-fix-rb-t028-cluster (SEQUENTIAL after T044-01; shared-root-cause production change)
- 012-resolve-insights-hold (parallel with 011; gated on sibling response)
- 013-phase1-exit-triple-run (after 010+011+012)

**Sequencing rationale**: RB-T028 cluster is a single registration-path edit — parallel agents would race. RB-T044-01 needs dedicated security attention.

**Inputs**: r1 ledger entries 044-01 + 028-02/03/04/05/06; ADR-010, ADR-018; security reviewer assignment
**Outputs**: 5 production fix PRs; 45 tests flip Skip → Pass; D-07 decision record

---

### Phase 2: MEDIUM Severity (Weeks 5-7 | 2026-06-30 → 2026-07-20)

**Objectives:**
1. Fix 7 MED entries with FULL-rigor review (security review if touching auth)

**Deliverables:**
- [ ] 7 MED production fixes (~30-40 tests flip Skip → Pass)
- [ ] Per-entry commit citing ledger ID (NFR-04)
- [ ] Phase 2 exit triple-run artifact

**Critical Tasks** (parallel-safe; disjoint files):
- 020-fix-rb-t044-02 (CitationExtractor.NormalizeCaseLaw)
- 021-fix-rb-t044-04 (NormalizePatent EP/WO double-prefix)
- 022-fix-rb-t053-01 (CapabilityRouter — owner decision required at task start)
- 023-fix-rb-t070-03 (AnalysisChatContextResolver — restore-or-delete decision)
- 024-fix-rb-t028-01 (AnalysisContextBuilder OrderByDescending tie-breaker)
- 025-fix-rb-t028-07 (Upload endpoint binding — may already be Phase 1)
- 026-fix-rb-t028-02-fallback (if not resolved in Phase 1)
- 029-phase2-exit-triple-run

**Parallel waves:**
- Wave 1 (5 agents): 020, 021, 022, 023, 024
- Wave 2 (2 agents): 025, 026

**Inputs**: ADR-013 refined, ADR-008, ADR-018; patterns/api/*
**Outputs**: 7 PRs; ledger entries `repaired` / `transferred` / `archived`

---

### Phase 3: LOW Severity + Integration Stability (Weeks 8-9 | 2026-07-21 → 2026-08-03)

**Objectives:**
1. Fix 8 LOW entries (mostly 1-line production fixes)
2. Validate `Spe.Integration.Tests` triple-run stability (FR-10)

**Deliverables:**
- [ ] 8 LOW production fixes
- [ ] 3-run `Spe.Integration.Tests` baseline; ≤2 flakes; any flake quarantined + ledgered
- [ ] Phase 3 exit triple-run artifact (combined unit + integration)

**Critical Tasks** (parallel-safe):
- 030-fix-rb-t012-01 (SessionRestoreService Trim quotes)
- 031-fix-rb-t034-01 (AgentConfigurationService cancellation token)
- 032-fix-rb-t044-03 (NormalizeStatute subsections)
- 033-fix-rb-t044-05 (RegulationPattern CFR no-period)
- 034-fix-rb-t050-01 (SourcePaneSseEventData null citation omission)
- 035-fix-rb-t070-01 (AgentConversationService cancellation token)
- 036-fix-rb-t070-02 (R2SseEventEmitter null RetryAfter omission)
- 037-fix-rb-t028-08 (PrecedentAdmin endpoint binding)
- 038-spe-integration-triple-run (3 runs; flake quarantine)
- 039-phase3-exit-validation

**Parallel waves:**
- Wave 1 (6 agents): 030, 031, 032, 033, 034, 035
- Wave 2 (3 agents): 036, 037, 038

**Inputs**: ADR-007 (SpeFileStore); ADR-008 (filters); patterns/api/*
**Outputs**: 8 PRs; `Spe.Integration.Tests` triple-run TRX baseline

---

### Phase 4: Quality Lift + Audits + Pilots (Weeks 10-12 | 2026-08-04 → 2026-08-24)

**Objectives:**
1. Five PARALLEL tracks producing pilot-grade artifacts (D-04 caps scope)

**Tracks** (all parallel, disjoint domains):

- **Track A — PCF/Code Pages Test Rot Audit** (read-only): task 040-pcf-codepages-audit → `audits/pcf-codepages-test-rot-2026-08-XX.md`
- **Track B — Mutation Testing Pilot** (Stryker.NET): task 041-mutation-testing-pilot-safety → `baseline/mutation-testing-Services-Ai-Safety-2026-08-XX.md` (score + top-10 + r3 recommendation)
- **Track C — TestClock + Seeded Guid PoC**: task 042-testclock-poc-workspace → PoC working in ≥1 Workspace test class; pattern doc in `docs/procedures/testing-and-code-quality.md`
- **Track D — Coverlet Baseline** (BLOCKED BY `github-actions-rationalization-r1` Phase 1): task 043-coverlet-baseline-measurement → baseline % per project in exit-ledger
- **Track E — Anti-Drift Effectiveness Report**: task 044-anti-drift-effectiveness-report → `baseline/anti-drift-effectiveness-2026-08-XX.md` (published whether favorable or not — NFR-07)

**Parallel wave**: All 5 tracks dispatch in ONE wave (5 agents; disjoint deliverables; within 6-agent cap).

**Critical decision point**: Track D start gated on external CI rationalization project. If behind, slip to Phase 5 (does not block other tracks).

**Inputs**: All Phase 1-3 ledger transitions; PR history 2026-06-01 → 2026-08-15
**Outputs**: 5 deliverables — all pilot-grade; none ship as required-status-checks

---

### Phase 5: Governance + Close (Week 13 | 2026-08-25 → 2026-08-31)

**Objectives:**
1. Update governance docs based on Phase 1-4 findings
2. Conditionally extend `.claude/constraints/bff-extensions.md` § F (if RB-T028 root cause warrants new rule)
3. Final triple-run validation (FR-15)
4. PR + admin-merge cycle + lessons-learned

**Deliverables:**
- [ ] `docs/procedures/testing-and-code-quality.md` updated (ledger lifecycle + TestClock pattern + Track E findings)
- [ ] (Conditional) `.claude/constraints/bff-extensions.md` § F extension with unconditional-service-registration rule
- [ ] 6-TRX final triple-run artifact (both projects, 3 runs each)
- [ ] `ledgers/exit-ledger.md` (cumulative r2 state)
- [ ] `notes/lessons-learned.md`
- [ ] PR merged to master ≤ 2026-08-31 (FR-16)

**Critical Tasks:**
- 080-update-testing-procedure-doc (parallel-safe; not `.claude/`)
- 081-extend-bff-extensions-constraint (CONDITIONAL; **`parallel-safe: false` — main-session-only per CLAUDE.md §3**)
- 082-final-triple-run-validation
- 083-pr-and-admin-merge-cycle
- 084-doc-drift-audit-post-procedure-update
- 090-project-wrap-up (creates `lessons-learned.md` + `exit-ledger.md`; MANDATORY final task)

**Sequencing**:
- 080 + 081 run sequentially (081 depends on 080's structure)
- 082 before 083
- 084 after 080/081 (audits docs work for drift)
- 090 last

**Inputs**: All Phase 1-4 ledger transitions + audit findings
**Outputs**: All graduation criteria satisfied; project closed

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| `github-actions-rationalization-r1` Phase 1 | ✅ Complete or imminent (resolved 2026-06-01) | LOW | No slip expected; Phase 4 Track D runs as planned |
| `ai-spaarke-insights-engine-r1` owner | ✅ `dev@spaarke.com` (resolved 2026-06-01) | LOW | Contact known; task 002 + task 012 use this address |
| Security reviewer named | ✅ `dev@spaarke.com` (resolved 2026-06-01; NFR-03 unblocked) | LOW | task 010 (RB-T044-01) + cluster task 011 merge gates have named reviewer |
| Stryker.NET v3.x compat | Unvalidated | LOW | Phase 4 Track B validates; version-pin or scope reduction if incompatible |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| r1 `real-bug-ledger.md` | `projects/sdap-bff.api-test-suite-repair/ledgers/` | ✅ Available |
| r1 `lessons-learned.md` | `projects/sdap-bff.api-test-suite-repair/notes/` | ✅ Available (load at Phase 0) |
| r1 `priority-order.md` | `projects/sdap-bff.api-test-suite-repair/` | ✅ Available (FR-06 modifies TBD slots) |
| `enforce_admins: true` | GitHub branch protection | ✅ Restored 2026-06-01 |
| `config/coverlet.runsettings` | repo root | ✅ Already wired in CI |

---

## 6. Testing Strategy

**Unit Tests** (`Sprk.Bff.Api.Tests`):
- Triple-run validated at r1 close — canonical green baseline
- r2 maintains Failed: 0; tests modified ONLY for Skip→Pass transitions (NFR-01)
- Phase 4 Track C adds TestClock-pattern tests in `Services/Workspace/*` (additive, not replacement)

**Integration Tests** (`Spe.Integration.Tests`):
- Phase 3 FR-10 triple-run validates stability (mirrors r1 task 084 unit pattern)
- ≤2 flakes acceptable; quarantine + ledger any beyond that

**Mutation Testing**:
- Phase 4 Track B pilots Stryker.NET against `Services/Ai/Safety/*` — measurement only, no remediation (D-04)

**Triple-Run Validation Gates** (NFR-05):
- Phase 1 exit: `Sprk.Bff.Api.Tests` triple-run
- Phase 3 exit: `Spe.Integration.Tests` triple-run (FR-10)
- Phase 5 exit: BOTH projects triple-run (FR-15; 6 TRX artifacts)

---

## 7. Acceptance Criteria

### Technical Acceptance (per phase)

- **Phase 0**: Baseline captured; 20 entries verified reproducible; sibling outreach sent
- **Phase 1**: 5 HIGH closed; RB-T044-01 has security-review record; +45 tests Skip→Pass; exit triple-run green
- **Phase 2**: 7 MED closed; +30-40 tests Skip→Pass; exit triple-run green
- **Phase 3**: 8 LOW closed; Spe.Integration.Tests triple-run ≤2 flakes; all 20 ledger entries closed
- **Phase 4**: 5 deliverables published (audit + mutation report + TestClock PoC + Coverlet baseline + anti-drift report)
- **Phase 5**: Governance docs updated; final triple-run green (6 TRX); PR merged ≤ 2026-08-31; lessons-learned authored

### Business Acceptance

- [ ] Zero ledger entries with fix-by date ≤ 2026-09-30 in `open` / `assigned-to-r2` / `in-progress` state at r2 close
- [ ] Anti-drift compliance rate measured and published (whatever the finding)
- [ ] r3 scope recommendation derived from r2 audit findings — not pre-committed

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | RB-T044-01 fix introduces new bug | LOW | HIGH | Security review (NFR-03); regression tests; staged dev-env validation |
| R2 | RB-T028 root-cause fix breaks other endpoints | MED | HIGH | Per-fix triple-run; one flag at a time; automated revert if regression |
| R3 | Mutation testing finds 50+ weak assertions | MED | MED | D-04 caps r2 to scoring + top-10; remediation = r3 |
| R4 | PCF audit reveals worse rot than BFF | MED | MED | Audit IS the deliverable; r3 scope derives from findings |
| R5 | Sibling project blocks RB-T028-02 transfer | LOW | LOW | 1-week timeout → `archived-pending-sibling-engagement` |
| R6 | `Spe.Integration.Tests` triple-run >2 flakes | LOW | LOW | Phase 4 audit task; doesn't block Phase 3 close |
| R7 | Anti-drift report shows <80% compliance | MED | LOW | NFR-07 publishes anyway; includes corrective-action proposals |
| R8 | 2026-08-31 deadline slips | MED | HIGH | Per-phase hard end date; 1-week slip → next-phase descope; Phase 4 tracks droppable |
| R9 | `github-actions-rationalization-r1` slips | MED | LOW | Phase 4 Track D slips to Phase 5; other tracks unaffected |

---

## 9. Next Steps

1. **Review this plan** for the 5 unresolved questions in [spec.md "Unresolved Questions"](spec.md#unresolved-questions)
2. **Run** `/task-create projects/sdap.bff.api-test-suite-repair-r2` to generate ~35-45 POML files + TASK-INDEX.md (`/project-pipeline` Step 3 does this automatically)
3. **Phase 0 begins**: Task 000 captures r1 baseline; Task 001 verifies 20-entry reproducibility; Task 002 sends sibling outreach

---

**Status**: Ready for Tasks (Step 3 of `/project-pipeline`)
**Next Action**: Invoke `/task-create` to decompose into POML tasks

---

*For Claude Code: This plan provides implementation context. Load relevant phase sections when executing tasks via `task-execute`.*
