# Visual Host "+" Create Button - AI Context

> **Purpose**: Context for Claude Code when working on visual-host-create-button-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (pipeline run 2026-07-08)
- **Last Updated**: 2026-07-08
- **Current Task**: Not started (tasks generated, execution pending)
- **Next Action**: Switch session to Sonnet 5, then execute Phase 0 (task 001)

---

## Quick Reference

- [`spec.md`](spec.md) — AI implementation specification (source of truth)
- [`design.md`](design.md) — Technical design (rev 2, post-#549)
- [`plan.md`](plan.md) — 6-phase WBS + discovered resources
- [`current-task.md`](current-task.md) — active task state (recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + parallel groups + model tiers

**Type**: PCF (Visual Host) + Shared UI library. **Complexity**: Medium-High. **Hot-path**: all N.

---

## 🚨 MANDATORY: Task Execution Protocol

All task work MUST use the `task-execute` skill. Trigger phrases ("work on task X", "continue", "next task", "resume task X", "pick up where we left off") → invoke task-execute. It ensures ADRs/knowledge loaded, current-task.md tracked, checkpointing every 3 steps, quality gates (code-review + adr-check at Step 9.5) for FULL rigor, recoverable progress. Parallel tasks: one message, multiple Skill invocations. Multi-file (4+): decompose into a dependency graph, parallelize independent modules via subagents, serialize coupled work.

---

## Execution Model & Tiering (this project uses it — added 2026-07-08)

- **Planning** (design-to-spec, project-pipeline): ran on **Opus 4.8**.
- **Execution**: default **Sonnet 5 at effort `xhigh`** — near-Opus coding at lower cost; same 1M context + tokenizer as Opus 4.8, so no checkpointing changes.
- **Per-task escalation**: each POML carries `<model-tier>`. **opus-tier tasks in THIS project**: the ADR-024 `eventService` resolver migration (Phase A) and the 4-family `WizardFollowOns` consolidation (Phase D). Everything else is `sonnet`. project-pipeline dispatches subagents at their tier; a serial `opus` task on a Sonnet session triggers stop-and-escalate (task-execute Step 0.5).
- **Sonnet-5 discipline**: POMLs are explicit — exact files, cite `workAssignmentService.ts` as the canonical reference to copy, exact ADR-024 contract (5 resolver fields + mutual exclusion), checkable acceptance criteria. FULL-rigor gates stay unconditional.

---

## Key Technical Constraints

- **ADR-024 (Polymorphic Resolver)** — CENTRAL. All child association via `applyResolverFields` (entity-specific lookup + all resolver fields, 5 incl. `sprk_regardingrecordnumber` post-#549). NEVER the native "Regarding" lookup. ADR-024 amended by #549 — re-read before implementing.
- **Post-#549 API**: `applyResolverFields` backward-compatible (returns `IApplyResolverFieldsResult`). New `PolymorphicPicker` exists but `AssociateToStep` stays the wizard step. `FieldMappingHandler` inheritance OUT of scope.
- **ADR-022/021** — PCF React + Fluent v9; dark-mode via semantic tokens.
- **ADR-007/028** — SPE upload via `/api/obo/...` with `authenticatedFetch`.
- **BFF=N (hard)** — NEVER add to `src/server/api/Sprk.Bff.Api/`. AI prefill is an inert seam (`prefillEnabled=false`).
- **Reuse-first (§11)** — canonical reference `CreateWorkAssignmentWizard/workAssignmentService.ts`. Do NOT ship a duplicate Next-Steps/SendEmail — use shared `WizardFollowOns`.
- **Build**: PCF prod build is `npm run build:prod`. VisualHost imports shared-lib **source** — the shared lib's `node_modules` must be installed before a VisualHost build.

---

## Decisions Made

- 2026-07-05: AI prefill stubbed (BFF=N); full `WizardFollowOns` consolidation; KPI = Matter+Project, no files; 3rd Next Step = Assign Work; field manifests owner-provided — Owner.
- 2026-07-08: Build on post-#549 resolver API (merged); execution on Sonnet 5 with opus escalation for Event migration + WizardFollowOns — Owner.

---

## Implementation Notes

- `CreateEventWizard` currently BYPASSES the resolver (matter/project-only, skips resolver fields) — Phase A migrates to `applyResolverFields` (correctness fix).
- File dual-bind: one `sprk_document`, two `@odata.bind`s (host + child) — Event/Invoice only; KPI has no files.
- Field manifests owner-provided (spec FR-16); Phase 0 validates against live schema.

---

## Deferrals & Issues

Track deferred work + issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues. File via `/project-defer-issue-tracking` (`/defer`). Every entry names a concrete failing behavior/contract (§11). `push-to-github` blocks on entries missing a GitHub URL.

---

## Resources

**ADRs**: ADR-024 (central; amended by #549), ADR-022, ADR-021, ADR-007, ADR-028, ADR-012, ADR-011, ADR-038.
**Related projects**: `set-regarding-and-field-mapping-resolver-r1` (#549, MERGED — resolver API + `PolymorphicPicker`); VisualHost UAT + TrackingFieldTrio (#525, MERGED).
**Patterns/standards**: `.claude/patterns/dataverse/polymorphic-resolver.md`, `.claude/patterns/ui/record-modal-selection.md`, `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`, `docs/standards/MODAL-DECISION-CRITERIA.md`.
