# Visual Host "+" Create Button

> **Last Updated**: 2026-07-08
>
> **Status**: In Progress
> **Owner**: ralph.schroeder@hotmail.com
> **Branch**: `work/visual-host-create-button-r1`

## Overview

Adds a maker-configurable **"+" toolbar button** to the Visual Host PCF that opens the appropriate Create wizard for the entity a visual represents — launched from and auto-associated to the host record. Wizards follow the standard Spaarke wizard template and reuse the ADR-024 polymorphic resolver, with a shared `WizardFollowOns` module consolidating today's four duplicated Next-Steps implementations.

## Quick Links

| Document | Description |
|----------|-------------|
| [spec.md](./spec.md) | AI implementation specification (source of truth) |
| [design.md](./design.md) | Technical design (rev 2, post-#549 realigned) |
| [plan.md](./plan.md) | Implementation plan + WBS |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task tracker (created by task-create) |
| [current-task.md](./current-task.md) | Active task state (context recovery) |
| [notes/pipeline-paused.md](./notes/pipeline-paused.md) | Blocker history (RESOLVED 2026-07-08) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (pipeline resumed after #549/#525 merged) |
| **Progress** | Planning complete; tasks pending |
| **Owner** | ralph.schroeder@hotmail.com |

## Problem Statement

A user viewing a Visual Host visual (e.g. an Events calendar on a Matter form) has no fast path to **create a new record of the type that visual represents**. They must leave the visual, find the subgrid or command bar, and launch the wizard from there — losing context. There is no one-click "create a related record from this visual" affordance.

## Solution Summary

A third toolbar button — **"+"** — appears (when a maker enables it via two existing `sprk_chartdefinition` columns) between the existing spaarkle and open icons. Clicking it resolves a wizard from a central `WizardRegistry` and opens it in a Fluent Dialog, seeded with the host record as the association. Wizards follow the standard 4-visible-step template (Associate To → Add Files → Enter Info → Next Steps), write associations via the ADR-024 `applyResolverFields`, dual-bind uploaded documents to both host and child, and offer Send Email / Add To Do / Assign Work follow-ons from a new shared `WizardFollowOns` module. AI prefill ships as an inert seam (no BFF work this release).

## Graduation Criteria

The project is **complete** when:

- [ ] A maker can toggle `sprk_createwizardenabled` to show/hide the "+" button with no PCF redeploy.
- [ ] "+" on an Event visual opens `CreateEventWizard` (migrated to `applyResolverFields`); created Event is regarding the host with all resolver fields populated.
- [ ] "+" on an Invoice visual creates a valid `sprk_invoice` regarding the host via `applyResolverFields`.
- [ ] "+" on a KPI visual creates a valid `sprk_kpiassessment` regarding the host Matter/Project.
- [ ] A single uploaded file (Event/Invoice) becomes one `sprk_document` in both the host and child Documents subgrids.
- [ ] Next Steps offers Send Email / Add To Do / Assign Work from the shared `WizardFollowOns`; the four duplicate implementations are deleted with no regression.
- [ ] AI prefill seam present but inert; `git diff --stat` shows no `Sprk.Bff.Api` files.
- [ ] No regression in spaarkle/open buttons or the three migrated wizards.

## Scope

### In Scope

- Visual Host "+" button (`CardChrome` + legacy `VisualHostRoot`), gated on `sprk_createwizardenabled`.
- `WizardRegistry` dispatcher + `WizardHostProps` contract.
- Migrate `CreateEventWizard`/`eventService` onto `applyResolverFields` (ADR-024 fix); wire to `event` key.
- New `CreateInvoiceWizard` + `CreateKPIAssessmentWizard` (+ services) on the standard template.
- Auto-association from host (`initialAssociation` + `lockAssociation`).
- File dual-bind (Event + Invoice) via `EntityCreationService.createDocumentRecords` multi-bind.
- New shared `WizardFollowOns` module; migrate all four wizard families onto it, delete duplicates.
- AI prefill inert seam (`useAiPrefill`, `prefillEnabled=false`).
- PCF version bump + deploy.

### Out of Scope

- AI prefill BFF endpoints + JPS prefill actions (separate follow-on).
- KPI polymorphism beyond Matter + Project.
- Field-mapping inheritance (`FieldMappingHandler`) in the wizards.
- Any `Sprk.Bff.Api` change (hot-path stays BFF=N).

## Key Decisions

| Decision | Rationale | Ref |
|----------|-----------|-----|
| Reuse `sprk_chartdefinition` columns (already created) | Existing maker config surface; no new table | spec §Scope |
| Registry-based dispatch | Visual Host stays agnostic to specific wizards | design §5.3 |
| Prefill stubbed (BFF=N) | Owner: back-fill AI prefill later | Owner 2026-07-05 |
| KPI = Matter + Project, no files step | Owner scoping | Owner 2026-07-05/07 |
| Full `WizardFollowOns` consolidation | End 4-way duplication | Owner 2026-07-05 |
| Build on post-#549 resolver API | #549 merged 2026-07-08 | design R9 |

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Follow-on consolidation touches 3 existing wizards | Med | Config-driven grid; per-wizard regression pass (Phase D) |
| `CreateEventWizard` was ADR-024 non-compliant | Med | Migrate to `applyResolverFields`; regression-test existing surfaces |
| `sprk_regardingrecordnumber` may be absent on Event/KPI | Low | Phase 0 verifies; small additive column if needed |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| PR #549 (resolver API + picker) | Internal | ✅ Merged 2026-07-08 | Wizards build on post-#549 `applyResolverFields` |
| PR #525 (VisualHost files) | Internal | ✅ Merged 2026-07-07 | Rebased in via master merge |
| `sprk_recordtype_ref` rows (matter/project) | Internal | Ready | Used by Todo/WorkAssignment |

## Changelog

| Date | Change |
|------|--------|
| 2026-07-05 | Design + spec authored; pipeline paused on #549/#525 overlap |
| 2026-07-07 | #525 merged; PCF deploy of VisualHost 1.4.27 / TrackingFieldTrio 1.0.5 |
| 2026-07-08 | #549 merged; docs realigned to post-#549 API; pipeline resumed; artifacts generated |

---

*Project artifacts generated by project-pipeline / project-setup. Source: `spec.md`.*
