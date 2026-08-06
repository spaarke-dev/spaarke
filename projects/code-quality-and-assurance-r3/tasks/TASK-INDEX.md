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
| 020 | BFF-A: delete low-contention dead code (Scopes/Retry/StubLiveFact/archives) | 2 BFF Remediation | FULL | sonnet | high | none | A | 🔲 |
| 021 | BFF-A: fix invoice-totals cast in-place (Bug-1) + test | 2 BFF Remediation | FULL | opus | high | none | A | 🔲 |
| 022 | BFF-A: .eml builder cleanup (Bug-2) | 2 BFF Remediation | FULL | sonnet | high | none | A | 🔲 |
| 023 | BFF-A: auth closure via @spaarke/auth (§6 RESOLVED) | 2 BFF Remediation | FULL | opus | xhigh | none | A | 🔲 |
| 027 | BFF-A: repo hygiene (tarballs/artifacts) | 2 BFF Remediation | STANDARD | sonnet | low | none | A | 🔲 |
| 024 | BFF-B: AI-facade compliance | 2 BFF Remediation | FULL | opus | high | none | B | 🔲 |
| 025 | BFF-B: Endpoints/→Api/ migration | 2 BFF Remediation | STANDARD | sonnet | medium | none | B | 🔲 |
| 026 | BFF-B: DI decompose + Finance rename | 2 BFF Remediation | STANDARD | sonnet | high | none | B | 🔲 |
| 028 | BFF-B: 13→1 downcast consolidation | 2 BFF Remediation | FULL | opus | xhigh | 021 | B | 🔲 |
| 029 | BFF-B: delete dead Safety cluster (Services/Ai) | 2 BFF Remediation | FULL | sonnet | high | none | B | 🔲 |
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
| **BFF (020–029)** | 020–029 | **false** | BFF hot-path contention (19 worktrees). Small sequential PRs; `/conflict-check` before each PR. Split into A/B tranches (below). |

`parallel-safe:false` waves: **BFF 020–029**, **031**, **041**, **042**.

### BFF A/B tranche ordering (per BFF workstream design §0/§5 + handoff §4)

The BFF workstream lands as small PRs in two tranches off the one branch:

| Tranche | Tasks | When | Why |
|---|---|---|---|
| **A — first (low/no conflict)** | 020 (dead code), 021 (Bug-1 invoice cast), 022 (Bug-2 .eml), 023 (auth), 027 (repo hygiene) | ASAP | Ships the two production bug fixes + auth closure + hygiene fast; touches low-contention paths. |
| **B — quiet window (wide edits / contested `Services/Ai`)** | 028 (13→1 downcast consolidation, deps 021), 029 (Safety-cluster deletion), 024 (facade), 025 (Endpoints→Api), 026 (DI decompose + Finance rename) | after Tranche A, in a quiet window | Wide edits across contested `Services/Dataverse/Finance/Ai`; sequence with `/conflict-check`. |

> **§6 owner gate — RESOLVED.** The BFF design §6 A/B/C decision (anonymous Finance-write endpoint) is **resolved to `@spaarke/auth`/ADR-028** (owner, 2026-08-06) — encoded in task **023**; no separate decision task. Residual gate = whether `sprk_subgrid_parent_rollup.js` can attach a `@spaarke/auth` token (task 023 `<escalation>`; a gap elevates to task 030 / FR-17).
> **Data-driven gate** — task 026's Finance-handler rename requires a Dataverse `sprk_analysistool.sprk_handlerclass` pre-check (task 026 `<escalation>`; never touch a `HandlerId` string).

---

## Critical Path

```
001 (rubric) → 003 (workflow) → [010–015 assessments, parallel] → 016 (re-baseline gate)
                                                                      │
                                          gates surface-2..6 remediation planning (DEFERRED)
```

- **001 → 003**: the workflow's finders are one-per-rubric-dimension, so the rubric must exist first.
- **[010–015] → 016**: no aggregate grade until every surface is scored (FR-04); 016 is the honest re-baseline gate.
- **BFF 020–029 are independent** of the Phase-0/1 gate (BFF is already assessed — workstream #1). They run as small sequential PRs off the one branch in A→B tranche order, `/conflict-check` each. Only intra-BFF dep: 028 deps 021.
- **040 → 042**: CI gates run the expanded ArchTests fitness functions, so 040 precedes 042.

---

## Deferred

- **Phase 5 — surfaces 2–6 remediation**: remediation tasks for shared client libs, shared server libs, PCF, Dataverse model+ALM, code pages, and plugins are **task-created only AFTER** each surface's Fable-verified assessment `design.md` exists (tasks 010–015 outputs). They are NOT enumerated here yet — this is the assessment-first owner decision (spec.md Owner Clarifications; CLAUDE.md Decisions Made).
- **NG1 — two-Dataverse-stacks unification**: out of scope; filed as a backlog **Idea** (FR-23) via `/devops-idea-create`. Characterized by task 011; not remediated in R3.

---

## Goal-eligibility

- **Candidate goal-eligible waves**: the **assessment wave (010–015)** (read-only, machine-verifiable end-state = design.md + SCORECARD row exists, ≥3 low-ambiguity tasks) and the **BFF behavior-neutral subset (020, 025, 027)** (dead-code deletion / namespace-only migration / repo hygiene — closed acceptance criteria) MAY be run under `/goal`. The Haiku evaluator is a transcript-only stopping-condition check — Step 9.5 + orchestrator authority are unchanged; tasks are never auto-completed on goal achievement.
- **NOT goal-eligible**: **bug-fix/correctness** (021, 028), **auth/security** (023 auth, 030 security), **CI** (042), **test-modifying / contested** (029 deletes tests in Services/Ai, 031, 040), **data-driven rename** (026) — these carry judgment boundaries / irreversibility / security sensitivity and require per-task operator gating.
