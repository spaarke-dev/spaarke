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
| 001 | Workflow inventory + baseline snapshots | 0: Inventory + Baseline | STANDARD | 🔲 | none | A | ✅ |
| 002 | Investigate post-PR-#314 master CI failure (run 26755019759) | 0: Inventory + Baseline | STANDARD | 🔲 | none | A | ✅ |
| 010 | Fix deploy-promote.yml cascade (P2) | 1: Fix Broken Workflows | STANDARD | 🔲 | 001 | B | ✅ |
| 011 | Fix deploy-infrastructure.yml ghost triggers (P3) | 1: Fix Broken Workflows | STANDARD | 🔲 | 001 | B | ✅ |
| 012 | Fix nightly-quality.yml schedule failures (P4) | 1: Fix Broken Workflows | STANDARD | 🔲 | 001 | B | ✅ |
| 020 | Audit untested workflows + draft dispositions | 2: Rationalization | STANDARD | 🔲 | 001 | — | ❌ (single ledger build) |
| 021 | Execute deploy-* workflow dispositions | 2: Rationalization | STANDARD | 🔲 | 020 | C | ✅ |
| 022 | Execute non-deploy workflow dispositions | 2: Rationalization | STANDARD | 🔲 | 020 | C | ✅ |
| 030 | Add actionlint pre-merge validation workflow | 3: Prevention | STANDARD | 🔲 | 010, 011, 012, 021, 022 | — | ❌ (sequential) |
| 031 | Add actionlint to required-status-checks on master | 3: Prevention | STANDARD | 🔲 | 030 | — | ❌ (sequential) |
| 032 | Smoke-test actionlint gate via deliberate-fail PR | 3: Prevention | STANDARD | 🔲 | 031 | — | ❌ (sequential) |
| 040 | Add weekly workflow-health-report workflow | 4: Observability + Docs | STANDARD | 🔲 | 032 | D | ✅ |
| 041 | Author .github/WORKFLOWS.md | 4: Observability + Docs | MINIMAL | 🔲 | 022, 030, 040 | D | ✅ |
| 042 | Author docs/procedures/workflow-incident-response.md | 4: Observability + Docs | MINIMAL | 🔲 | 032 | D | ✅ |
| 043 | Document notification-routing steps (FR-12) | 4: Observability + Docs | MINIMAL | 🔲 | 041 | D | ✅ |
| 050 | Verify branch-protection gate end-to-end | 5: Validate the Gate | STANDARD | 🔲 | 032, 040, 041, 042, 043 | — | ❌ (final verification) |
| 090 | Project wrap-up + lessons learned + repo cleanup | Wrap-up | STANDARD | 🔲 | 050 | — | ❌ (final) |

**Total**: 17 tasks (2 in Phase 0 + 3 in Phase 1 + 3 in Phase 2 + 3 in Phase 3 + 4 in Phase 4 + 1 in Phase 5 + 1 wrap-up).

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
