# sdap-bff.api-test-suite-repair

> **Status**: 🟢 Planning — Ready for Phase 0 task execution
> **Created**: 2026-05-31
> **Owner**: ralph.schroeder@hotmail.com
> **Branch**: `work/sdap-bff.api-test-suite-repair`
> **Predecessor**: [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — Phase 4 facade shipped 2026-05-26; test repair deliberately deferred to this project

---

## Purpose

Repair the `Sprk.Bff.Api.Tests` suite (5,215 tests / 4,844 pass / 269 fail / 17 compile-broken files as of 2026-05-30 measured baseline) and `Spe.Integration.Tests` to a **zero-failure baseline**. Restore the BFF CI gate that admin-bypass merging has rendered fictional (10/10 last `sdap-ci.yml` runs failed but code still merges). Install anti-drift governance so the rot mechanism cannot recur.

Four outcomes ship as one bundle:

| Outcome | What it delivers |
|---|---|
| **A. Compile health** | All 17 compile-broken files repaired or archived; `dotnet build -warnaserror` returns 0 errors |
| **B. Runtime green** | Zero failing tests across `Sprk.Bff.Api.Tests` + `Spe.Integration.Tests`. Every touched test ends in `repaired`, `real-bug-pending-fix`, `flaky-quarantined`, or archived per §6.2 of [`design.md`](design.md) |
| **C. CI gate restoration** | `enforce_admins: true` on master; `skip-tests` removed from `deploy-bff-api.yml`; emergency-deploy procedure documented with named approver |
| **D. Anti-drift governance** | Test-update obligation added to `.claude/constraints/bff-extensions.md`, PR template, code review checklist, and root CLAUDE.md §10 |

Outcome C is load-bearing — A/B/D are valueless without it.

---

## Key Files

- [`spec.md`](spec.md) — AI-optimized specification (238 lines; 30 FRs, 12 NFRs, 14 success criteria)
- [`design.md`](design.md) — Full design document (759 lines; locked decisions §4–§6, parallelism plan §7)
- [`bff.api-repair-overview.txt`](bff.api-repair-overview.txt) — Historical framing (preserved; superseded by design.md §3 measured baseline)
- [`plan.md`](plan.md) — Implementation plan + WBS + discovered resources
- [`CLAUDE.md`](CLAUDE.md) — **Project context, loaded by every task agent** (binding rules per NFR-08)
- [`current-task.md`](current-task.md) — Active task state (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Per-phase task tracker + 13-wave parallel execution map

---

## Folder Structure

```
projects/sdap-bff.api-test-suite-repair/
├── README.md                       (this file)
├── spec.md                         AI-optimized spec
├── design.md                       Full design with locked decisions
├── bff.api-repair-overview.txt     Historical framing (preserved)
├── plan.md                         Implementation plan
├── CLAUDE.md                       Binding project context
├── current-task.md                 Active task state
├── tasks/
│   ├── TASK-INDEX.md               Task tracker + parallel groups
│   └── NNN-*.poml                  ~58 task definitions
├── notes/                          Ephemeral working files
│   ├── debug/  drafts/  handoffs/  spikes/
├── baseline/                       Phase 0 baseline artifacts (TRX, compile-errors, CI snapshot)
├── decisions/                      D-01..D-06 — captured decisions
├── escalations/                    §4.8 rewrite escalations (if any)
└── ledgers/                        repair/archive/real-bug/flaky/rewrite/exit ledgers
```

---

## Graduation Criteria (from spec.md §Success Criteria, 14 items, all required)

1. [ ] Test project compiles cleanly under `-warnaserror` — `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -warnaserror` returns 0 errors
2. [ ] **Zero failing tests** across `Sprk.Bff.Api.Tests` AND `Spe.Integration.Tests` — `dotnet test` summary shows `Failed: 0`
3. [ ] Every touched test ends in a §6.2 final end-state — Phase 4 audit script counts traits + archive renames; total equals touched-files count
4. [ ] CI gate is operational — Deliberately-failing PR is blocked by `Build & Test (Release)`
5. [ ] `enforce_admins: true` for required status checks; `skip-tests` removed from `deploy-bff-api.yml` — `gh api .../branches/master/protection` + `deploy-bff-api.yml` diff
6. [ ] Last 5 `sdap-ci.yml` runs on master are SUCCESS — `gh run list`
7. [ ] Last 3 `deploy-bff-api.yml` runs are SUCCESS (or N/A)
8. [ ] `.claude/constraints/bff-extensions.md` includes test-update obligation
9. [ ] CLAUDE.md §10 references the test-update obligation
10. [ ] `docs/procedures/bff-deploy-emergency.md` exists with named approver
11. [ ] Project CLAUDE.md exists and is referenced by every task agent (NFR-08)
12. [ ] Rewrite escalations stayed under 5% of touched files (per §4.8 hard limit)
13. [ ] Exit ledger published with per-state counts + sibling-coordination outcomes
14. [ ] `real-bug-ledger.md` + `flaky-ledger.md` exist with fix-by dates for any non-Pass entries

---

## How to Work on This Project

**ABSOLUTE RULE**: All task work MUST use the [`task-execute`](../../.claude/skills/task-execute/SKILL.md) skill. DO NOT read POML files directly and implement manually. See [`CLAUDE.md`](CLAUDE.md) Task Execution Protocol.

**To start Phase 0**:
```
/task-execute projects/sdap-bff.api-test-suite-repair/tasks/001-baseline-capture.poml
```

**Recommended**: Start in a fresh session (preserves pipeline-init context).

**Parallel execution**: 5 tasks in Phase 0 Wave 1 (001, 002, 003, 006, 007) can dispatch in parallel via task-execute. See [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) "Parallel Execution Groups" for the full 13-wave plan.

---

## Project Constraints (NON-NEGOTIABLE)

These are restated in [`CLAUDE.md`](CLAUDE.md) and every task POML's `<repair-not-rewrite>true</repair-not-rewrite>` metadata:

- ❌ **MUST NOT** modify production code: `src/`, `power-platform/`, `infra/`, `scripts/` (NFR-01)
- ❌ **MUST NOT** rewrite tests (>50% line replacement requires §4.8 escalation; hard limit ≤5% of touched files)
- ❌ **MUST NOT** rewrite `CustomWebAppFactory.cs` (§4.5 — extend only)
- ❌ **MUST NOT** leave any test in `Failed` state at project close (§4.3)
- ❌ **MUST NOT** silently delete tests (NFR-06 — archive via `*.archived-YYYY-MM-DD` rename)
- ❌ **MUST NOT** increase BFF DI registration count via test scaffolding (ADR-010, NFR-03)
- ✅ **MUST** cite [`design.md`](design.md) §3 measured numbers (5,215 / 4,844 / 269 / 17), NOT the overview's stale "283 failures" framing
- ✅ **MUST** tag every touched test with `[Trait("status", …)]` from §6.2 taxonomy
- ✅ **MUST** run full suite before AND after any `CustomWebAppFactory.cs` change (NFR-07 anti-parallelism guard)

---

## Effort Estimate

| Phase | Person-hours | Wall-clock (with parallelism) |
|---|---|---|
| Phase 0 — Baseline + Decisions | 4–6h | 1 day |
| Phase 1 — Unblock (5 parallel tracks) | 20–31h | 3–5 days |
| Phase 2+3 — Repair (5 parallel tracks) | 48–75h | 10–18 days |
| Phase 4 — Governance + Validation | 8–12h | 2–3 days |
| **Total** | **80–124h** | **16–27 days** |

If executed without parallelism: ~28–40 calendar days at typical pace. Parallelism is the project's structural advantage (per design.md §10).

---

## Active Sibling-Project Coordination

Per spec §2.3 + design §2.3 — these projects touch BFF and must coordinate:

| Project | Risk | Action |
|---|---|---|
| `ai-spaarke-action-engine-r1` | HIGH — new BFF endpoints/services | Phase 0 task 005: priority-order sign-off; Action Engine adopts test convention this project establishes |
| `ai-spaarke-insights-engine-r1` | MEDIUM — adds tests under `Services/Ai/` | Daily sync during Phase 2+3 23-M track; priority-order sequences Insights-active files last |
| `x-email-communication-solution-r2` | MEDIUM — Communications test files in compile-broken set | Owner-aligned for Phase 1 task 011 + Phase 2+3 task 055/056 |

---

## Pipeline Position

```
bff.api-repair-overview.txt (2026-05-28, framing)
      ↓
design.md (2026-05-30, measured baseline + locked decisions)
      ↓ /design-to-spec (2026-05-31)
spec.md
      ↓ /project-pipeline (2026-05-31 — THIS RUN)
plan.md + CLAUDE.md + ~58 task POMLs + TASK-INDEX.md
      ↓ /task-execute per task (Phase 0 → 1 → 2+3 → 4 → wrap-up)
Outcomes A + B + C + D shipped
      ↓
Project closed
```

---

*Last Updated: 2026-05-31 by `/project-pipeline` initialization. Update during execution as phases close.*
