# Task Index — Agent Framework Fit Assessment R1

> **Legend**: 🔲 pending · ▶️ in progress · ✅ complete · ⚠️ blocked/gap

Canonical plan: [`SPEC.md`](./SPEC.md) · Project conventions: [`CLAUDE.md`](./CLAUDE.md)

---

## Phase progress

| # | Phase | Status | Notes |
|---|---|---|---|
| 0 | Primary-source baseline (task 000) | ✅ | Done 2026-06-03. SHA afa7834e (1.9 release). 34 primary sources, 100% within recency floor. Critical findings: AF 1.0 GA April 2026; #6268 streaming bug affects S1; massive sample catalog expansion. |
| 1 | Inventory current state (tasks 001-002) | 🔲 | Read Spaarke AI code + non-BFF AI touchpoints; produce structured findings tables |
| 2 | Agent Framework feature mapping (task 003) | 🔲 | Map Microsoft.Agents.AI + Workflows surface area; **mandatory live-URL citations** from notes/00 |
| 3 | Per-surface decision analysis (task 004) | 🔲 | Apply SPEC §4 criteria to each of S1-S8 |
| 4 | Deployment + migration (task 005) | 🔲 | Deployment model + aggregated migration cost / risks |
| 5 | Synthesis (task 006) | 🔲 | Write docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md; **§10 Sources appendix mandatory** |
| 6 | Review + sign-off (tasks 007-008) | 🔲 | Adversarial review + source recency re-check + sign-off + unblock note for agent-framework-knowledge-r1 |

## Tasks

| ID | Title | Phase | Rigor | Parallel group | Status | Owner |
|---|---|---|---|---|---|---|
| [000](tasks/000-refresh-primary-sources.poml) | Refresh primary sources baseline (recent-content-only) | 0 | STANDARD | — | ✅ | Claude (2026-06-03) |
| [001](tasks/001-inventory-spaarke-ai-surfaces.poml) | Inventory Spaarke AI code surfaces (S1-S4 + S8 catch) | 1 | STANDARD | A | 🔲 | — |
| [002](tasks/002-inventory-non-bff-ai-touchpoints.poml) | Inventory non-BFF AI touchpoints (S5-S7) | 1 | STANDARD | A | 🔲 | — |
| [003](tasks/003-map-agent-framework-features.poml) | Map Agent Framework feature surface vs. Extensions.AI baseline | 2 | STANDARD | B | 🔲 | — |
| [004](tasks/004-per-surface-decision-analysis.poml) | Apply decision criteria to each surface; produce per-surface matrix | 3 | STANDARD | — | 🔲 | — |
| [005](tasks/005-deployment-and-migration-analysis.poml) | Deployment model recommendations + aggregated migration cost analysis | 4 | STANDARD | — | 🔲 | — |
| [006](tasks/006-write-assessment-document.poml) | Synthesize findings into docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md | 5 | FULL | — | 🔲 | — |
| [007](tasks/007-adversarial-review.poml) | Adversarial review + source recency re-check; revise as needed | 6 | FULL | — | 🔲 | — |
| [008](tasks/008-project-wrap-up.poml) | Sign-off + unblock note for agent-framework-knowledge-r1 | 6 | MINIMAL | — | 🔲 | — |

## Parallel execution groups

- **Task 000** runs FIRST sequentially — all downstream tasks depend on the primary-source baseline it produces.
- **Group A** (tasks 001, 002): Both inventory tasks, independent sources (BFF code vs. non-BFF projects). Both depend on 000. Safe to fan out in parallel after 000 lands via two `task-execute` Skill calls in one message.
- **Group B** (task 003): Independent of A but depends on 000. Safe to start in parallel with A after 000 lands.
- **Tasks 004, 005, 006, 007, 008** sequential — each depends on the previous.

## Gaps / blocks log

_None yet — populate as tasks execute._

## Reference

- Canonical plan: [`SPEC.md`](./SPEC.md)
- Project conventions: [`CLAUDE.md`](./CLAUDE.md)
- Status overview: [`README.md`](./README.md)
- Current task state: [`current-task.md`](./current-task.md)
- Parked downstream project: [`../agent-framework-knowledge-r1/`](../agent-framework-knowledge-r1/)
