# Visual Host "+" Create Button

> **Last Updated**: 2026-07-09 · **Status**: Complete · **Owner**: ralph.schroeder@hotmail.com
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
| [notes/lessons-learned.md](./notes/lessons-learned.md) | Wrap-up retrospective |
| [notes/defer-issues.md](./notes/defer-issues.md) | Open issue: Invoice Vendor Org lookup ([#587](https://github.com/spaarke-dev/spaarke/issues/587)) |
| [notes/test-diet-report.md](./notes/test-diet-report.md) | Test-diet reconciliation (163 MAINTAIN, 0 scaffolding) |
| [`docs/guides/VISUALHOST-SETUP-GUIDE.md` § "+" Create Button Configuration](../../docs/guides/VISUALHOST-SETUP-GUIDE.md#-create-button-configuration-v1432) | Maker-facing valid-keys reference |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete (wrapped up 2026-07-09) |
| **Progress** | 17/17 tasks complete (001–050 + 090 wrap-up) |
| **Hot-path** | BFF=N, SpaarkeAi=N, ci=N, skill-directives=N, root-CLAUDE=N |
| **Deployed** | VisualHost v1.4.32 on spaarkedev1 |
| **Known open issue** | Invoice "Vendor Organization" lookup — see [#587](https://github.com/spaarke-dev/spaarke/issues/587), deferred to a follow-on session |

## Problem Statement

A user viewing a Visual Host visual (e.g. an Events calendar on a Matter form) has no fast path to **create a new record of the type that visual represents**. They must leave the visual, find the subgrid or command bar, and launch the wizard from there — losing context.

## Solution Summary

A third toolbar button — **"+"** — appears (when a maker enables it via two existing `sprk_chartdefinition` columns) between the spaarkle and open icons. Clicking it resolves a wizard from a central `WizardRegistry` and opens it in a Fluent Dialog, seeded with the host record as the association. Wizards follow the standard 4-visible-step template (Associate To → Add Files → Enter Info → Next Steps), write associations via the ADR-024 `applyResolverFields`, dual-bind uploaded documents to both host and child, and offer Send Email / Add To Do / Assign Work follow-ons from a new shared `WizardFollowOns` module. AI prefill ships as an inert seam (no BFF work this release).

> **Scope note (2026-07-08 owner decision)**: the third wizard originally spec'd as "KPI Assessment" (`sprk_kpiassessment`) was retargeted mid-project to **`sprk_reportcard`** — the parent review-artifact record KPI line-items belong to — via `CreateReportCardWizard`. Registry key: `report-card`. See `spec.md`'s amendment banner + `notes/field-manifests/reportcard.md`.

## Graduation Criteria

- [x] Maker toggles `sprk_createwizardenabled` to show/hide the "+" button (no PCF redeploy).
- [x] "+" on an Event visual opens `CreateEventWizard` (migrated to `applyResolverFields`); Event is regarding the host with all resolver fields populated.
- [x] "+" on an Invoice visual creates a valid `sprk_invoice` regarding the host.
- [x] "+" on a Report Card visual creates a valid `sprk_reportcard` regarding the host Matter/Project (retargeted from KPI Assessment — see scope note above).
- [x] A single uploaded file (Event/Invoice) → one `sprk_document` in both host and child Documents subgrids.
- [x] Next Steps offers Send Email / Add To Do / Assign Work from the shared `WizardFollowOns`; four duplicate implementations deleted, no regression.
- [x] AI prefill seam present but inert; `git diff --stat` shows no `Sprk.Bff.Api` files.
- [x] No regression in spaarkle/open or the three migrated wizards.

All 8 graduation criteria verified via live browser UAT (2026-07-08/09), except the Invoice Vendor Organization lookup sub-issue (tracked separately, [#587](https://github.com/spaarke-dev/spaarke/issues/587) — does not block core Invoice creation).

## Scope

**In**: "+" button (CardChrome + VisualHostRoot); `WizardRegistry`; migrate `CreateEventWizard` onto `applyResolverFields`; new `CreateInvoiceWizard` + `CreateReportCardWizard` (retargeted from the originally-spec'd `CreateKPIAssessmentWizard`); auto-association; file dual-bind (Event/Invoice); shared `WizardFollowOns` (migrate all 4 families); AI prefill inert seam; PCF version bump + deploy.
**Out**: AI prefill BFF endpoints + JPS actions; Report Card polymorphism beyond Matter+Project; field-mapping inheritance (spun into a separate follow-on project, `set-regarding-and-field-mapping-resolver-r2`); any `Sprk.Bff.Api` change.

## Dependencies

| Dependency | Status | Notes |
|---|---|---|
| PR #549 (resolver API + `PolymorphicPicker`) | ✅ Merged 2026-07-08 | Wizards build on post-#549 `applyResolverFields` |
| PR #525 (VisualHost files) | ✅ Merged 2026-07-07 | Rebased in via master merge |
| `sprk_recordtype_ref` rows (matter/project) | Ready | Used by Todo/WorkAssignment |
| PR #585 (this project) | ✅ Merged 2026-07-09 | Main implementation + UAT fixes |

## Changelog

| Date | Change |
|------|--------|
| 2026-07-05 | Design + spec; pipeline paused on #549/#525 overlap |
| 2026-07-07 | #525 merged; deployed VisualHost 1.4.27 / TrackingFieldTrio 1.0.5 |
| 2026-07-08 | #549 merged; docs realigned; pipeline run → artifacts + tasks generated; third wizard retargeted KPI Assessment → Report Card (owner decision) |
| 2026-07-08/09 | Full implementation (tasks 001–050); live browser UAT across all 3 wizards; deployed v1.4.29 → v1.4.31 (dialog-sizing fix, toolbar spacing) |
| 2026-07-09 | PR #585 merged to master; v1.4.32 follow-up fix (Event Matter Type/Practice Area lookups — root-caused + fixed); `/test-diet` (163 MAINTAIN, 0 scaffolding); task 090 wrap-up; Invoice Vendor Org lookup deferred to Issue #587 |
