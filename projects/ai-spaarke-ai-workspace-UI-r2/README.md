# AI SpaarkeAi Workspace UI — R2

> **Portfolio**: [Issue #532](https://github.com/spaarke-dev/spaarke/issues/532) · Parent Epic [#430 Insights/Widgets/Search](https://github.com/spaarke-dev/spaarke/issues/430) · [Portfolio Board](https://github.com/users/spaarke-dev/projects/2)
> **Status**: **Complete** (PR #530 + PR #531 merged; both PRs shipped 2026-07-01/02; project registered on portfolio 2026-07-02 via backfill)
> **Branch**: `work/ai-spaarke-ai-workspace-UI-r2`
> **Worktree**: `C:\code_files\spaarke-wt-ai-spaarke-ai-workspace-UI-r2`
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-01
> **Wrap-up date**: 2026-07-01
> **PR**: [#530](https://github.com/spaarke-dev/spaarke/pull/530) — folded Phase 1 + 2 + 3 into single PR per operator direction 2026-07-01 ("don't wait for CI — proceed"). Original plan was 3 independent PRs.
> **Test diet**: PASS (9 MAINTAIN, 0 SCAFFOLDING, 0 AMBIGUOUS) — see [`notes/test-diet-report.md`](notes/test-diet-report.md).
> **Success criteria**: 10 of 14 static-verified; 4 pending post-deploy manual QA — see [`notes/success-criteria-verification.md`](notes/success-criteria-verification.md).
> **Lessons learned**: [`notes/lessons-learned.md`](notes/lessons-learned.md).

## Purpose

Standardize how records open from SpaarkeAi workspace widgets by adopting a two-layout modal standard, retire the contractually-unsupported iframe-hosted OOB `main.aspx` pattern in `SmartTodoModal`, and backfill the missing Communications workspace widget.

## Deliverables (3 phased PRs)

- **PR 1 — Framework + widget migration** — extend `sprk_gridconfiguration.configjson.rowOpen` with optional `formId`; rewrite DataGrid `defaultRecordOpen` to route every row-click through `Xrm.Navigation.navigateTo` (Layout 1, 85% × 85%); 5 existing widgets adopt Layout 1 via the framework change
- **PR 2 — Communications widget** — new section + direct widget for `sprk_communication` (all types: Email / Teams / SMS / other); new config record
- **PR 3 — SmartTodoModal retirement + documentation** — retire the iframe pattern (all callsites → `navigateTo`); delete component; sharpen 5 documentation surfaces

## Success Criteria

See [`spec.md` §Success Criteria](spec.md) — 14 verifiable conditions with explicit verification methods.

## Key artifacts

- [`design.md`](design.md) — project design
- [`spec.md`](spec.md) — AI-optimized implementation spec (21 FRs, 9 NFRs)
- [`plan.md`](plan.md) — implementation plan with phase breakdown
- [`CLAUDE.md`](CLAUDE.md) — AI context loaded at task-execute time
- [`current-task.md`](current-task.md) — active task state (recovery-safe)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry + dependencies
- [`notes/researcher-iframe-main-aspx-2026-07-01.md`](notes/researcher-iframe-main-aspx-2026-07-01.md) — evidence trail

## Predecessor

R1 established the reusable `DataverseEntityViewWidget` foundation and the dual-use Pattern D architecture (sections + direct widgets). See [`../ai-spaarke-ai-workspace-UI-r1/design.md`](../ai-spaarke-ai-workspace-UI-r1/design.md).

## Ship gate

Ready to merge when all 14 success criteria verified, code-review approves each phased PR, and CI green.
