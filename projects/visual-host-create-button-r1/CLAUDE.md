# Visual Host "+" Create Button - AI Context

> **Purpose**: This file provides context for Claude Code when working on visual-host-create-button-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (pipeline resumed 2026-07-08 after #549/#525 merged)
- **Last Updated**: 2026-07-08
- **Current Task**: Not started (tasks pending `/task-create`)
- **Next Action**: Run `/task-create projects/visual-host-create-button-r1` to decompose plan into task files

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI implementation specification (source of truth)
- [`design.md`](design.md) - Technical design (rev 2, post-#549 realigned)
- [`README.md`](README.md) - Overview + graduation criteria
- [`plan.md`](plan.md) - Implementation plan + WBS (6 phases)
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)
- [`notes/pipeline-paused.md`](notes/pipeline-paused.md) - Blocker history (RESOLVED)

### Project Metadata
- **Project Name**: visual-host-create-button-r1
- **Type**: PCF (Visual Host) + Shared UI library (`Spaarke.UI.Components`)
- **Complexity**: Medium-High (new wizards + cross-wizard consolidation + resolver migration)
- **Hot-path**: BFF=N, SpaarkeAi=N, ci-workflows=N, skill-directives=N, root-CLAUDE=N

---

## Context Loading Rules

1. **Always load this file first** when starting work on any task.
2. **Check current-task.md** for active work state (especially after compaction/new session).
3. **Reference spec.md** for requirements + acceptance criteria; **design.md** for the "why".
4. Load the relevant task file from `tasks/`.
5. Apply ADRs relevant to the technologies used (adr-aware).

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Why This Matters
task-execute ensures: knowledge/ADRs loaded, context tracked in current-task.md, checkpointing every 3 steps, quality gates (code-review + adr-check at Step 9.5), recoverable progress.

### Parallel Task Execution
When tasks can run in parallel (no dependencies), each STILL uses task-execute: one message, multiple Skill invocations. Phases B and C are parallelizable after A + D.

### 🚨 MUST: Multi-File Work Decomposition
For tasks modifying 4+ files: decompose into a dependency graph, parallelize independent modules via subagents, serialize tightly-coupled work. See task-execute Step 8.0.

---

## Key Technical Constraints

- **ADR-024 (Polymorphic Resolver)** — CENTRAL. All child association MUST use `PolymorphicResolverService.applyResolverFields` (entity-specific lookup + all resolver fields, 5 incl. `sprk_regardingrecordnumber` post-#549). NEVER the native "Regarding" lookup. ADR-024 was amended by #549 — re-read before implementing.
- **Post-#549 API**: `applyResolverFields` is backward-compatible (now returns `IApplyResolverFieldsResult`, optional `options`). New `PolymorphicPicker` exists but `AssociateToStep` stays as the wizard step. `FieldMappingHandler` inheritance is OUT of scope.
- **ADR-022 / ADR-021** — PCF React + Fluent UI v9; dark-mode via semantic tokens.
- **ADR-007 / ADR-028** — SPE upload via `/api/obo/...` with `authenticatedFetch`.
- **BFF=N (hard)** — NEVER add to `src/server/api/Sprk.Bff.Api/`. AI prefill is an inert seam (`prefillEnabled=false`); its BFF work is a separate project.
- **Reuse-first (CLAUDE.md §11)** — canonical reference is `CreateWorkAssignmentWizard` / `workAssignmentService.ts`.
- **Consolidation** — do NOT ship a new duplicate Next-Steps/SendEmail; use the shared `WizardFollowOns`.
- **Build**: PCF prod build is `npm run build:prod` (NOT `npm run build`). VisualHost imports shared-lib **source** (`Spaarke.UI.Components/src`), so the shared lib's `node_modules` must be installed before a VisualHost build.

---

## Decisions Made

- 2026-07-05: AI prefill stubbed (BFF=N), back-fill later — Owner.
- 2026-07-05: Full `WizardFollowOns` consolidation (all 4 families) — Owner.
- 2026-07-05/07: KPI = Matter + Project, no files step — Owner.
- 2026-07-05: 3rd Next Step = Assign Work — Owner.
- 2026-07-08: Build on post-#549 resolver API (merged) — pipeline resumed.

---

## Implementation Notes

- `CreateEventWizard` currently BYPASSES the resolver (matter/project-only, skips resolver fields) — Phase A migrates it to `applyResolverFields` (a correctness fix, not new deviation).
- File dual-bind: one `sprk_document` sets two `@odata.bind`s (host + child) — Event/Invoice only; KPI has no files step.
- Field manifests are owner-provided (spec FR-16); Phase 0 validates against live schema.

---

## Deferrals & Issues — tracking obligation

Track deferred work + newly-discovered issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues (visibility). File via `/project-defer-issue-tracking` (alias `/defer`) — it writes both. Every entry must name a concrete failing behavior/contract (CLAUDE.md §11). `push-to-github` blocks on entries missing a GitHub URL.

---

## Resources

### Applicable ADRs
- **ADR-024** — Polymorphic Resolver Pattern (central; amended by #549)
- **ADR-022** — PCF Platform Libraries
- **ADR-021** — Fluent UI v9 Design System
- **ADR-007** — SharePoint Embedded storage
- **ADR-028** — Spaarke Auth v2
- **ADR-012** — Shared Component Library (context-agnostic)
- **ADR-038** — Testing strategy (integration-heavy)

### Related Projects
- `set-regarding-and-field-mapping-resolver-r1` (PR #549) — resolver API + `PolymorphicPicker` this project builds on (MERGED).
- VisualHost UAT + TrackingFieldTrio (PR #525) — VisualHost file base (MERGED).

### External Documentation
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`, `MODAL-DECISION-CRITERIA.md`
- `.claude/patterns/dataverse/polymorphic-resolver.md`

---

*This file should be kept updated throughout project lifecycle.*
