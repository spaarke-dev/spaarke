# Task Index — github-actions-rationalization-r1

> **Project**: GitHub Actions Rationalization R1
> **Total Tasks**: 17
> **Generated**: 2026-06-01 by `/project-pipeline`
> **Branch**: `work/github-actions-rationalization-r1`

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress / blocked / needs retry |
| ✅ | Completed |
| ⏭️ | Skipped (with rationale) |

---

## Task Status

| ID | Title | Phase | Rigor | Status | Dependencies | Parallel Group | Parallel-Safe |
|----|-------|-------|-------|--------|--------------|----------------|---------------|
| 001 | Workflow inventory + baseline snapshots | 0: Inventory + Baseline | STANDARD | ✅ | none | A | ✅ |
| 002 | Investigate post-PR-#314 master CI failure (run 26755019759) | 0: Inventory + Baseline | STANDARD | ✅ | none | A | ✅ |
| 010 | Fix deploy-promote.yml cascade (P2) | 1: Fix Broken Workflows | STANDARD | ✅ | 001 | B | ✅ |
| 011 | Fix deploy-infrastructure.yml ghost triggers (P3) | 1: Fix Broken Workflows | STANDARD | ✅ | 001 | B | ✅ |
| 012 | Fix nightly-quality.yml schedule failures (P4) | 1: Fix Broken Workflows | STANDARD | ✅ | 001 | B | ✅ |
| 020 | Audit untested workflows + draft dispositions | 2: Rationalization | STANDARD | ✅ | 001 | — | ❌ (single ledger build) |
| 021 | Execute deploy-* workflow dispositions | 2: Rationalization | STANDARD | ✅ | 020 | C | ✅ |
| 022 | Execute non-deploy workflow dispositions | 2: Rationalization | STANDARD | ✅ | 020 | C | ✅ |
| 030 | Add actionlint pre-merge validation workflow | 3: Prevention | STANDARD | ✅ | 010, 011, 012, 021, 022 | — | ❌ (sequential) |
| 031 | Add actionlint to required-status-checks on master | 3: Prevention | STANDARD | ✅ | 030 | — | ❌ (sequential) |
| 032 | Smoke-test actionlint gate via deliberate-fail PR | 3: Prevention | STANDARD | ✅ | 031 | — | ❌ (sequential) |
| 040 | Add weekly workflow-health-report workflow | 4: Observability + Docs | STANDARD | ✅ | 032 | D | ✅ |
| 041 | Author .github/WORKFLOWS.md | 4: Observability + Docs | MINIMAL | ✅ | 022, 030, 040 | D | ✅ |
| 042 | Author docs/procedures/workflow-incident-response.md | 4: Observability + Docs | MINIMAL | ✅ | 032 | D | ✅ |
| 043 | Document notification-routing steps (FR-12) | 4: Observability + Docs | MINIMAL | ✅ | 041 | D | ✅ |
| 050 | Verify branch-protection gate end-to-end | 5: Validate the Gate | STANDARD | ✅ | 032, 040, 041, 042, 043 | — | ❌ (final verification) |
| 090 | Project wrap-up + lessons learned + repo cleanup | Wrap-up | STANDARD | 🔲 | 050 | — | ❌ (final) |

**Total**: 17 tasks (2 in Phase 0 + 3 in Phase 1 + 3 in Phase 2 + 3 in Phase 3 + 4 in Phase 4 + 1 in Phase 5 + 1 wrap-up).

---

## Wave A Findings (2026-06-01) — INFORMS DOWNSTREAM TASKS

**Task 001 inventory recommendations** (`baseline/workflow-inventory-2026-06-01.md`):
| Recommendation | Count | Workflows |
|---|---|---|
| KEEP | 3 | adr-audit (88.5%), sdap-ci (after src/ fix), deploy-slot-swap (75% but no-op trigger) |
| FIX | 4 | sdap-ci (Risk R1 — DEFERRED per D-01), deploy-promote (P2), deploy-infrastructure (P3), nightly-quality (P4) |
| DELETE | 4 | auto-add-to-project (broken since 2026-03-13), deploy-platform, provision-customer, insights-eval |
| CONSOLIDATE | 2 pairs | deploy-bff-api ↔ deploy-slot-swap; weekly-quality ↔ nightly-quality |

**Projected post-rationalization workflow count**: ~6 retained + 2 new (workflows-validate, report-workflow-health) = **8** ✅ (meets FR-06 target ≤8).

**Task 002 disposition** (`decisions/D-01-master-ci-failure-disposition.md`):
- **DEFERRED** — master CI failure is `src/` drift exposed by PR #314 (17 `-warnaserror` build errors + 330 Prettier-unformatted files), NOT a workflow config issue. Per NFR-01, this project does not fix `src/`.
- **Phase 5 FR-13 impact**: deliberate-fail verification PR will need a carve-out around `Build & Test (Release)` if a follow-on `src/`-fix project doesn't land first. Task 050 POML to be lightly updated before Phase 5.
- **Follow-on project suggested**: `sdap-bff-warnaserror-cleanup-r1` (~4–8 h)

**Notable surprises** (inform Phase 1 + Phase 2 tasks):
1. `deploy-promote.yml` artifact contract is broken (downloads `deployment-packages` but sdap-ci produces `test-results-*`/`coverage-reports-*`). Task 010 must address BOTH cascade AND contract. **[Wave B update]**: Investigation showed this was incorrect — `sdap-ci.yml` line 236 DOES produce `deployment-packages`. See D-02 for the correction.
2. `deploy-promote.yml` and `deploy-infrastructure.yml` are **100% loader-failures** (every run has `jobs: []`). Task 011 fix is structural (YAML/path-filter), not retry-the-failing-test.
3. `deploy-slot-swap.yml`'s 75% "success rate" is a no-op trick — most "success" runs have all real jobs skipped because upstream sdap-ci failed. Workflow is effectively dormant; ideal CONSOLIDATE candidate.
4. `auto-add-to-project.yml` broken since 2026-03-13 (29 consecutive fails) — likely expired `GH_TOKEN_PROJECT` secret. Task 020's audit should note this as evidence supporting DELETE.

---

## Wave B Findings (2026-06-01) — INFORMS DOWNSTREAM PHASES

**Task 010 result**: `deploy-promote.yml` cascade fix applied (workflow-level `if:` added to `summary` job; the only job that previously had `if: always()`). 2.1% line replacement. FR-03 satisfied by reasoning: on SDAP CI failure, all 5 jobs evaluate `if:` to false → workflow records as `skipped`, not `failure`. PyYAML parse PASS. Decision record `D-02-deploy-promote-artifact-contract-verified.md` corrects a Wave A inventory error: sdap-ci.yml DOES produce the `deployment-packages` artifact.

**Task 011 result**: `deploy-infrastructure.yml` loader-failure root cause identified and fixed. The bug: `${{ runner.temp }}` was used inside a job-level `env:` block (runner context only available at step level, not job level — silent loader fail). Fix: moved `env:` from job-level to step-level on the only consuming step. 1.1% line replacement. actionlint 1.7.7 PASS (was FAIL before — "context 'runner' is not allowed here"). FR-04 satisfied — loader now succeeds, trigger filters evaluate correctly.

**Task 012 result**: `nightly-quality.yml` failure root cause = **`src/`-regression** (4× CS1739 errors in `tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs`). NFR-01 forbids `src/` fixes. **DELETE recommended for BOTH `nightly-quality.yml` AND `weekly-quality.yml`** (per D-04 consolidate + D-03 delete-by-default). Decision record at `D-03-nightly-and-weekly-quality-disposition.md`. Execution deferred to Phase 2 task 022. FR-05 satisfied via the delete-with-rationale alternative path.

**No `src/` files modified, no `git rm` executed, no commits made by subagents — main session committed Wave B in single integration commit.**

---

## Task 020 Findings (2026-06-01) — DISPOSITIONS DRIVE WAVE C

**7 decision records produced** (D-04 through D-10) + 1 rollup ledger (`ledgers/workflow-disposition-ledger.md`).

**Final disposition map for all 13 workflows**:
| Workflow | Disposition | Decision Record |
|---|---|---|
| adr-audit.yml | KEEP | n/a |
| auto-add-to-project.yml | DELETE | D-10 |
| deploy-bff-api.yml | KEEP (consolidation target) | D-04 |
| deploy-infrastructure.yml | KEEP (Wave B fix applied) | n/a |
| deploy-office-addins.yml | KEEP (overridden — real value via 30 successful deploys/mo) | D-07 |
| deploy-platform.yml | DELETE | D-05 |
| deploy-promote.yml | KEEP (Wave B fix applied) | D-02 |
| deploy-slot-swap.yml | CONSOLIDATE → deploy-bff-api (zero-effort `git rm`) | D-06 |
| insights-eval.yml | DELETE (speak-now flag for 2026-05-28 commit author) | D-09 |
| nightly-quality.yml | DELETE | D-03 |
| provision-customer.yml | DELETE | D-08 |
| sdap-ci.yml | KEEP (Risk R1 DEFERRED per D-01) | D-01 |
| weekly-quality.yml | DELETE | D-03 |

**Final count math**: 13 (current) − 6 (deletes: D-03×2, D-05, D-08, D-09, D-10) − 1 (consolidation: D-06) + 2 (new in Phase 3+4: workflows-validate, report-workflow-health) = **8 workflows**. **FR-06 ≤8 → ✅ MET EXACTLY**.

**Wave C execution mapping**:
- **Task 021** (deploy-* + provision-customer): `git rm` deploy-platform.yml (D-05), deploy-slot-swap.yml (D-06), provision-customer.yml (D-08). KEEP no-ops: deploy-bff-api.yml (D-04), deploy-office-addins.yml (D-07).
- **Task 022** (other non-deploy): `git rm` nightly-quality.yml (D-03), weekly-quality.yml (D-03), insights-eval.yml (D-09), auto-add-to-project.yml (D-10).

**Notable**:
- deploy-slot-swap.yml consolidation is **zero merge effort** — deploy-bff-api.yml is already a functional superset for the in-use prod-only path. Task 021's CONSOLIDATE is a pure `git rm`.
- D-09 has a speak-now flag (2026-05-28 commit by spaarke-dev) — task 022's PR description should mention this so the contributor can object before merge.

---

## Wave C Findings (2026-06-01) — PHASE 2 EXECUTION COMPLETE

**7 file deletions executed via `git rm`** (per ledger):
- Task 021: deploy-platform.yml, deploy-slot-swap.yml, provision-customer.yml (D-05/D-06/D-08)
- Task 022: nightly-quality.yml, weekly-quality.yml, insights-eval.yml, auto-add-to-project.yml (D-03/D-09/D-10)

**Current workflow count**: 13 − 7 = **6**. After Phase 3+4 adds workflows-validate.yml + report-workflow-health.yml = 8 ✅ FR-06 target met.

**Stale references discovered** (NFR-01 forbids fixing now; tracked as TODO):
- **`src/` references to insights-eval.yml** (out of scope — comments only, non-functional):
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsModule.cs:82`
  - `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/DataverseLiveFactResolver.cs:20`
  - These are code comments referencing a deleted workflow. Recommend filing in product backlog for cleanup.
- **`docs/` references to deleted workflows** (multiple files; Phase 4 docs tasks 041/042 will touch some; full doc-drift sweep needed at project close):
  - `docs/procedures/ci-cd-workflow.md` (multiple sections)
  - `docs/architecture/ci-cd-architecture.md`
  - `docs/procedures/DEPENDENCY-MANAGEMENT.md:199-200`
  - `docs/procedures/testing-and-code-quality.md` (Nightly/Weekly sections)
  - `docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`
  - `docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md`
  - `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`
- **`.claude/skills/` references**:
  - `.claude/skills/ci-cd/SKILL.md:171` (auto-add-to-project entry)
  - `.claude/skills/azure-deploy/SKILL.md` (deploy-platform references)

These will be addressed by the doc-drift audit at project wrap-up (task 090) and the relevant Phase 4 docs tasks (041 `.github/WORKFLOWS.md`, 042 incident-response runbook).

---

## Parallel Execution Plan

Tasks within the same group can run simultaneously once their prerequisites are met. The project-pipeline dispatches one task agent per task in a wave; the wave completes before the next is dispatched.

| Wave | Tasks | Prerequisite | Files Touched (disjoint within wave) | Notes |
|------|-------|--------------|---------------------------------------|-------|
| **A** | 001, 002 | none | `baseline/workflow-inventory.md` (001), `decisions/D-01-...` (002) | Phase 0 — start here |
| **B** | 010, 011, 012 | Wave A complete (specifically: 001 for inventory lines) | `deploy-promote.yml`, `deploy-infrastructure.yml`, `nightly-quality.yml` | Phase 1 fixes — 3 different workflow files |
| **— (serial)** | 020 | Wave A complete | Builds full ledger; cannot parallelize | Audit task — drives Phase 2 |
| **C** | 021, 022 | 020 complete | Disjoint workflow file sets per ledger | Phase 2 dispositions |
| **— (serial)** | 030 → 031 → 032 | Wave B + Wave C complete | New workflow + branch protection + verification PR | Phase 3 strictly sequential (NFR-05) |
| **D** | 040, 041, 042, 043 | 032 complete (some have tighter deps; see table) | New workflow (040), `.github/WORKFLOWS.md` (041, 043), `docs/procedures/workflow-incident-response.md` (042) | Phase 4 observability + docs |
| **— (serial)** | 050 | Wave D complete | Verification PR + evidence | Phase 5 gate verification |
| **— (serial)** | 090 | 050 complete | Multiple project artifacts | Wrap-up |

### Dependency Graph (text form)

```
Wave A: 001, 002 [parallel, no prereq]
            │
            ├──> Wave B: 010, 011, 012 [parallel, prereq: 001]
            │       │
            │       └──> 030 (waits also on 020/021/022)
            │
            └──> 020 [serial, prereq: 001]
                    │
                    └──> Wave C: 021, 022 [parallel, prereq: 020]
                            │
                            └──> 030 [serial, prereq: 010,011,012,021,022]
                                    │
                                    └──> 031 [serial, prereq: 030]
                                            │
                                            └──> 032 [serial, prereq: 031]
                                                    │
                                                    └──> Wave D: 040, 041, 042, 043 [parallel, prereq: 032 (041 also needs 022, 030, 040)]
                                                            │
                                                            └──> 050 [serial, prereq: 032, 040, 041, 042, 043]
                                                                    │
                                                                    └──> 090 [serial, prereq: 050]
```

### Critical Path

`001 → 020 → 021/022 → 030 → 031 → 032 → 040 → 041 → 050 → 090`

(Tasks 002, 010, 011, 012 run in parallel with the critical path; 042, 043 run in parallel with 040/041.)

---

## Rigor Level Distribution

| Rigor | Count | Tasks |
|-------|-------|-------|
| FULL | 0 | — (no `.cs`/`.ts` modifications in this project) |
| STANDARD | 14 | 001, 002, 010, 011, 012, 020, 021, 022, 030, 031, 032, 040, 050, 090 |
| MINIMAL | 3 | 041, 042, 043 |

Total: 17.

---

## How to Execute Parallel Waves

1. Check all prerequisites for a wave are complete (✅).
2. Invoke `task-execute` skill with multiple Skill tool calls in ONE message — one per task in the wave.
3. Wait for ALL tasks in the wave to complete before dispatching the next.
4. Max concurrency: 6 agents per wave (hard limit per task-execute skill).
5. After each wave, run build/repo verification:
   - This project has no `.cs`/`.ts` changes, so `dotnet build` / `npm run build` not needed.
   - Instead: `actionlint .github/workflows/*.yml` to confirm all workflows still parse.
6. Update this TASK-INDEX.md as each task completes (🔲 → ✅).

---

## High-Risk Tasks

| Task | Risk | Mitigation |
|------|------|------------|
| 002 | Master CI failure may be a `src/` regression — out of scope per NFR-01 | Document as DEFERRED with product-backlog follow-up; don't fix `src/` |
| 010 | `workflow_run` cascade fix may break legitimate post-CI workflows | Test on test branch first; rollback if downstream workflows break |
| 011 | Root cause of ghost triggers unknown until investigation | Allow extra time; the file may need delete-with-rationale rather than repair |
| 030, 031 | First actionlint PR can't satisfy its own required check (chicken-and-egg) | Brief enforce_admins bypass per predecessor pattern; logged in decisions/ |
| 050 | If gate fails to block, that's a project-level regression | Don't merge under any circumstance; escalate immediately |

---

## Project Status Summary

- **Phase 0** (001, 002): Inventory + baseline. ~5h.
- **Phase 1** (010, 011, 012): Fix P2/P3/P4. ~9h. Parallel.
- **Phase 2** (020 → 021, 022): Rationalize untested workflows. ~9h.
- **Phase 3** (030 → 031 → 032): Add actionlint gate. ~4h. Sequential.
- **Phase 4** (040 + 041, 042, 043): Observability + docs. ~8h. Mostly parallel.
- **Phase 5** (050): Verify gate end-to-end. ~1h.
- **Wrap-up** (090): Quality gates + lessons learned + cleanup. ~2h.

**Total estimate**: ~38 hours / 6–9 calendar days (matches spec).

---

*Generated by `/project-pipeline` 2026-06-01. Update statuses as tasks complete.*
