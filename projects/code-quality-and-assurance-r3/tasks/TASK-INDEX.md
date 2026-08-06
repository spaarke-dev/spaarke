# TASK-INDEX — code-quality-and-assurance-r3

> **Program**: standing quality program (single project, single worktree; surfaces = workstreams/phases).
> **Execution**: operator-gated. All task work via `task-execute`. Assessments (010–015) run via the `quality-assessment` Workflow — operator per-run opt-in ("use a workflow"); Fable adversarial-verification is non-negotiable (NFR-05).
> **Baseline**: BFF publish 46.89 MB compressed (ceiling 60 MB). `/conflict-check` before every remediation PR (19 worktrees touch BFF).

---

## Status Table

| ID | Title | Phase | Rigor | Model | Effort | Deps | Group | Status |
|----|-------|-------|-------|-------|--------|------|-------|--------|
| 001 | Author the Code Quality Rubric (D1–D11) | 0 Program Foundation | STANDARD | opus | high | none | P0 | 🔲 |
| 002 | Scaffold the living SCORECARD.md | 0 Program Foundation | MINIMAL | sonnet | medium | 001 | P0 | 🔲 |
| 003 | Build the quality-assessment Workflow | 0 Program Foundation | STANDARD | fable | xhigh | 001 | none | 🔲 |
| 010 | Assess shared client libs (Spaarke.*) | 1 Full Assessment | STANDARD | fable | xhigh | 001,003 | P1 | 🔲 |
| 011 | Assess shared server libs | 1 Full Assessment | STANDARD | fable | xhigh | 001,003 | P1 | 🔲 |
| 012 | Assess PCF controls | 1 Full Assessment | STANDARD | fable | xhigh | 001,003 | P1 | 🔲 |
| 013 | Assess Dataverse model + ALM | 1 Full Assessment | STANDARD | fable | xhigh | 001,003 | P1 | 🔲 |
| 014 | Assess code pages + build sprawl | 1 Full Assessment | STANDARD | fable | xhigh | 001,003 | P1 | 🔲 |
| 015 | Assess plugins | 1 Full Assessment | STANDARD | fable | xhigh | 001,003 | P1 | 🔲 |
| 016 | Re-baseline SCORECARD (aggregate) | 1 Full Assessment | STANDARD | opus | high | 010,011,012,013,014,015 | none | 🔲 |
| 020 | BFF: delete 6 dead-code items | 2 BFF Remediation | FULL | sonnet | high | none | none | 🔲 |
| 021 | BFF: downcast consolidation + invoice bug | 2 BFF Remediation | FULL | opus | xhigh | none | none | 🔲 |
| 022 | BFF: .eml builder cleanup | 2 BFF Remediation | FULL | sonnet | high | none | none | 🔲 |
| 023 | BFF: auth closure via @spaarke/auth | 2 BFF Remediation | FULL | opus | xhigh | none | none | 🔲 |
| 024 | BFF: AI-facade compliance | 2 BFF Remediation | FULL | opus | high | none | none | 🔲 |
| 025 | BFF: Endpoints/→Api/ migration | 2 BFF Remediation | STANDARD | sonnet | medium | none | none | 🔲 |
| 026 | BFF: DI decompose + Finance rename | 2 BFF Remediation | STANDARD | sonnet | high | none | none | 🔲 |
| 027 | BFF: repo hygiene (tarballs/artifacts) | 2 BFF Remediation | STANDARD | sonnet | low | none | none | 🔲 |
| 030 | Horizontal: security sweep | 3 Horizontals | STANDARD | opus | high | none | P3 | 🔲 |
| 031 | Horizontal: test-quality sweep | 3 Horizontals | FULL | sonnet | high | none | none | 🔲 |
| 032 | Horizontal: dependency + CVE hygiene | 3 Horizontals | STANDARD | sonnet | medium | none | P3 | 🔲 |
| 033 | Horizontal: observability sweep | 3 Horizontals | STANDARD | sonnet | medium | none | P3 | 🔲 |
| 034 | Horizontal: doc-drift audit | 3 Horizontals | MINIMAL | sonnet | low | none | P3 | 🔲 |
| 040 | Forcing-function: expand ArchTests | 4 Forcing-Functions | FULL | opus | high | none | P4 | 🔲 |
| 041 | Forcing-function: mechanical baseline | 4 Forcing-Functions | STANDARD | sonnet | high | none | P4 | 🔲 |
| 042 | Forcing-function: CI gates | 4 Forcing-Functions | STANDARD | sonnet | high | 040 | none | 🔲 |
| 090 | Project wrap-up | 9 Wrap-up | STANDARD | sonnet | medium | prior | none | 🔲 |

Legend: 🔲 not-started · 🔄 in-progress · ✅ complete · ⏸️ deferred/blocked.

---

## Parallel Execution Groups

| Group | Tasks | parallel-safe | Notes |
|-------|-------|---------------|-------|
| **P0** | 001, 002 | true | 002 depends on 001 but both are foundation docs; 002 runs immediately after 001. |
| **P1** | 010, 011, 012, 013, 014, 015 | true | Assessments are READ-ONLY ⇒ conflict-free ⇒ may run in parallel anytime (each needs the "use a workflow" opt-in + a Fable verify pass). |
| **P3** | 030, 032, 033, 034 | true | Sweep-style horizontals. **031 is sequential (`parallel-safe:false`)** — it modifies `tests/**` and coordinates with `ci-cd-unit-test-remediation-r1`. |
| **P4** | 040 | true | 040 (ArchTests) is parallel-safe. **041 and 042 are sequential (`parallel-safe:false`)** — 041 edits build config per-surface; 042 edits `.github/workflows` (owned by `ci-cd-unit-test-remediation-r1`) and depends on 040. |
| **BFF (020–027)** | 020–027 | **false** | BFF hot-path contention (19 worktrees). Small sequential PRs; `/conflict-check` before each PR; sequence Finance/Communication/Email tasks into quiet windows. |

`parallel-safe:false` waves: **BFF 020–027**, **031**, **041**, **042**.

---

## Critical Path

```
001 (rubric) → 003 (workflow) → [010–015 assessments, parallel] → 016 (re-baseline gate)
                                                                      │
                                          gates surface-2..6 remediation planning (DEFERRED)
```

- **001 → 003**: the workflow's finders are one-per-rubric-dimension, so the rubric must exist first.
- **[010–015] → 016**: no aggregate grade until every surface is scored (FR-04); 016 is the honest re-baseline gate.
- **BFF 020–027 are independent** of the Phase-0/1 gate (BFF is already assessed — workstream #1). They run as small sequential PRs off the one branch anytime, `/conflict-check` each.
- **040 → 042**: CI gates run the expanded ArchTests fitness functions, so 040 precedes 042.

---

## Deferred

- **Phase 5 — surfaces 2–6 remediation**: remediation tasks for shared client libs, shared server libs, PCF, Dataverse model+ALM, code pages, and plugins are **task-created only AFTER** each surface's Fable-verified assessment `design.md` exists (tasks 010–015 outputs). They are NOT enumerated here yet — this is the assessment-first owner decision (spec.md Owner Clarifications; CLAUDE.md Decisions Made).
- **NG1 — two-Dataverse-stacks unification**: out of scope; filed as a backlog **Idea** (FR-23) via `/devops-idea-create`. Characterized by task 011; not remediated in R3.

---

## Goal-eligibility

- **Candidate goal-eligible waves**: the **assessment wave (010–015)** (read-only, machine-verifiable end-state = design.md + SCORECARD row exists, ≥3 low-ambiguity tasks) and the **BFF hygiene subset (020, 025, 027)** (behavior-neutral, closed acceptance criteria) MAY be run under `/goal`. The Haiku evaluator is a transcript-only stopping-condition check — Step 9.5 + orchestrator authority are unchanged; tasks are never auto-completed on goal achievement.
- **NOT goal-eligible**: **auth/security** (021 bug-fix, 023 auth, 030 security), **CI** (042), **test-modifying** (020 deletes tests, 031, 040) — these carry judgment boundaries / irreversibility / security sensitivity and require per-task operator gating.
