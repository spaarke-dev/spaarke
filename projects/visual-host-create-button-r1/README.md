# Visual Host "+" Create Button

> **Last Updated**: 2026-07-08 · **Status**: In Progress · **Owner**: ralph.schroeder@hotmail.com
> **Branch**: `work/visual-host-create-button-r1`

## Overview

Adds a maker-configurable **"+" toolbar button** to the Visual Host PCF that opens the appropriate Create wizard for the entity a visual represents — launched from and auto-associated to the host record. Wizards follow the standard Spaarke wizard template and reuse the ADR-024 polymorphic resolver, with a shared `WizardFollowOns` module consolidating today's four duplicated Next-Steps implementations.

## Quick Links

| Document | Description |
|----------|-------------|
| [spec.md](./spec.md) | AI implementation specification (source of truth) |
| [design.md](./design.md) | Technical design (rev 2, post-#549 realigned) |
| [plan.md](./plan.md) | Implementation plan + WBS |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task tracker + parallel groups |
| [current-task.md](./current-task.md) | Active task state (context recovery) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (pipeline run 2026-07-08) |
| **Progress** | Tasks generated; execution pending |
| **Hot-path** | BFF=N, SpaarkeAi=N, ci=N, skill-directives=N, root-CLAUDE=N |

## Problem Statement

A user viewing a Visual Host visual (e.g. an Events calendar on a Matter form) has no fast path to **create a new record of the type that visual represents**. They must leave the visual, find the subgrid or command bar, and launch the wizard from there — losing context.

## Solution Summary

A third toolbar button — **"+"** — appears (when a maker enables it via two existing `sprk_chartdefinition` columns) between the spaarkle and open icons. Clicking it resolves a wizard from a central `WizardRegistry` and opens it in a Fluent Dialog, seeded with the host record as the association. Wizards follow the standard 4-visible-step template (Associate To → Add Files → Enter Info → Next Steps), write associations via the ADR-024 `applyResolverFields`, dual-bind uploaded documents to both host and child, and offer Send Email / Add To Do / Assign Work follow-ons from a new shared `WizardFollowOns` module. AI prefill ships as an inert seam (no BFF work this release).

## Graduation Criteria

- [ ] Maker toggles `sprk_createwizardenabled` to show/hide the "+" button (no PCF redeploy).
- [ ] "+" on an Event visual opens `CreateEventWizard` (migrated to `applyResolverFields`); Event is regarding the host with all resolver fields populated.
- [ ] "+" on an Invoice visual creates a valid `sprk_invoice` regarding the host.
- [ ] "+" on a KPI visual creates a valid `sprk_kpiassessment` regarding the host Matter/Project.
- [ ] A single uploaded file (Event/Invoice) → one `sprk_document` in both host and child Documents subgrids.
- [ ] Next Steps offers Send Email / Add To Do / Assign Work from the shared `WizardFollowOns`; four duplicate implementations deleted, no regression.
- [ ] AI prefill seam present but inert; `git diff --stat` shows no `Sprk.Bff.Api` files.
- [ ] No regression in spaarkle/open or the three migrated wizards.

## Scope

**In**: "+" button (CardChrome + VisualHostRoot); `WizardRegistry`; migrate `CreateEventWizard` onto `applyResolverFields`; new `CreateInvoiceWizard` + `CreateKPIAssessmentWizard`; auto-association; file dual-bind (Event/Invoice); shared `WizardFollowOns` (migrate all 4 families); AI prefill inert seam; PCF version bump + deploy.
**Out**: AI prefill BFF endpoints + JPS actions; KPI polymorphism beyond Matter+Project; field-mapping inheritance; any `Sprk.Bff.Api` change.

## Dependencies

| Dependency | Status | Notes |
|---|---|---|
| PR #549 (resolver API + `PolymorphicPicker`) | ✅ Merged 2026-07-08 | Wizards build on post-#549 `applyResolverFields` |
| PR #525 (VisualHost files) | ✅ Merged 2026-07-07 | Rebased in via master merge |
| `sprk_recordtype_ref` rows (matter/project) | Ready | Used by Todo/WorkAssignment |

## Changelog

| Date | Change |
|------|--------|
| 2026-07-05 | Design + spec; pipeline paused on #549/#525 overlap |
| 2026-07-07 | #525 merged; deployed VisualHost 1.4.27 / TrackingFieldTrio 1.0.5 |
| 2026-07-08 | #549 merged; docs realigned; pipeline run → artifacts + tasks generated |
