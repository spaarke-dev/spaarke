# Spaarke Matter UI Enhancement R1

> **Last Updated**: 2026-05-27
>
> **Status**: In Progress

## Overview

Redesigns the Matter main-form Overview tab to deliver a modern Fluent v9 experience. Three deliverables — Documents (Semantic Search PCF behavioral changes), Matter Performance side pane (5 Visual Host instances + 5 chart definitions, no new PCF), and Matter Information / Overview form configuration — all built on existing infrastructure with no parallel component libraries.

## Quick Links

| Document | Description |
|---|---|
| [Project Plan](./plan.md) | Phased implementation plan with WBS, dependencies, parallel groups |
| [Design Spec](./spec.md) | AI-optimized specification (Rev 6, recon-validated) |
| [Design (source)](./design.md) | Original human design document |
| [Task Index](./tasks/TASK-INDEX.md) | Task registry with dependency + parallel-execution graph |
| [CLAUDE.md](./CLAUDE.md) | AI context file (always load on session start) |
| [Current Task](./current-task.md) | Active task state (for context recovery) |
| [Screenshots](./screenshots/) | 5 reference screenshots from prototype Variant H |

## Current Status

| Metric | Value |
|---|---|
| **Phase** | Phase 0 — Foundation |
| **Progress** | 0% (planning artifacts only) |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | spaarke-dev |

## Problem Statement

The existing Matter main-form Overview tab is built on Fluent v8 affordances and a 2024-era information architecture: vertical filter rail, inline per-row icons, no list view, no multi-select, no per-matter performance scorecard. It does not match the prototype Variant H mockups the team has validated, and it leaves the existing Visual Host renderers + `sprk_chartdefinition` infrastructure under-utilized. The team needs Fluent v9 affordances, a sortable list view with multi-select, a 3-dot row menu consolidating 13 actions, a relocated filter set in the command bar, a tags filter sourced from `sprk_documenttype`, and a 5-card Matter Performance side pane — all without creating parallel chart libraries or a `MatterPerformancePane` container PCF.

## Solution Summary

Surgical, additive changes only — no parallel libraries:
1. **Documents PCF** — 5 in-place behavior changes (FR-DOC-01..06) plus telemetry wiring (FR-DOC-07) and a doc-update task (FR-DOC-09).
2. **Visual Host renderer extensions** — generic, backward-compatible Custom Options additions (FR-VH-01..04) + a new internal `CardChrome` wrapper (FR-VH-05). No new visual types.
3. **5 new `sprk_chartdefinition` records** (FR-DV-01..05) wired into 5 Visual Host instances on the form.
4. **2 small shared components** (`TagFilter`, `DocumentRowMenu`) in `@spaarke/ui-components` (FR-SC-01..02).
5. **Form XML** reconfigured to 2-column 66/34 layout (FR-FORM-01).
6. **BFF** gets 2 changes: search-result projection adds `modifiedAt`+`modifiedBy` (FR-BFF-01), new `POST /api/documents/bulk-download` endpoint (FR-BFF-02).

## Graduation Criteria

The project is complete when ALL of the following pass — these mirror spec.md §Success Criteria (36 items condensed to graduate-level checks):

### Form-level
- [ ] Overview tab renders 2-column 66/34 layout in light AND dark themes at ≥1024 px viewports
- [ ] Cold-load p95 < 2.0 s on Matter with full data (NFR-01)
- [ ] All five Visual Host instances appear stacked in the right column wired to their `sprk_chartdefinition` lookups

### Documents PCF
- [ ] 3-dot menu replaces all inline row actions + dialog-toolbar actions; menu items match FR-DOC-01's 13-action order
- [ ] List/card view toggle persists per-matter-per-user in localStorage; default sort `modifiedAt DESC`; multi-select preserved across view toggles; pin state survives reload
- [ ] Tags filter populates from `sprk_documenttype`, multi-select OR semantics, chip row + clear-all, footer count reflects filtered results
- [ ] Bulk-action bar shows at ≥1 selection; each of 6 actions operates correctly (Email selected with zip attachment, Download via FR-BFF-02, Pin localStorage, Delete with confirmation, Doc Type optimistic + 5s Undo, Share link)
- [ ] Click-to-Dialog preview opens at 960 px max-width with 2-column body (thumbnail + metadata pane: AI summary, Tags, Details)
- [ ] AssociatedOnly auto-search behavior (SemanticSearchControl.tsx:359-375) preserved verbatim after filter relocation

### Performance side pane (Visual Host)
- [ ] Matter Health card renders donut + composite letter center + 3-area breakdown rows; segments colored by `colorThresholds`; themed in dark mode
- [ ] Matter Budget renders `$50K`-style headline + sub-line + horizontal progress bar (color-thresholded)
- [ ] Matter Tasks shows overdue count + red Fluent `Badge` (only when >0) + upcoming sub-line via FetchXML aggregation
- [ ] Matter Next Date renders date + event title + days-from-now via `DueDateCard`
- [ ] Matter Activity shows event count + brand-color sub-line via FetchXML
- [ ] Each card has `CardChrome` header + corner expand icon; AI sparkle slot hidden in v1; expand opens drill-through via existing `sprk_chartdefinition` Drill Through Settings
- [ ] Every existing in-production chart def (Matter KPI Scorecard, Matter Financial Metrics Scorecard, all others) renders unchanged after FR-VH-* extensions land (NFR-05 — binding)

### Cross-cutting
- [ ] axe DevTools reports zero WCAG 2.1 AA violations on Overview tab in light + dark (NFR-02)
- [ ] Automated grep confirms zero hardcoded hex/rgb in PCF + Visual Host source touched by this project (NFR-04)
- [ ] App Insights events fire per FR-TEL-01; no `console.log` in production telemetry paths
- [ ] `code-review` (FULL rigor) + `adr-check` skills pass for every code-implementation task
- [ ] [VISUALHOST-ARCHITECTURE.md](../../docs/architecture/VISUALHOST-ARCHITECTURE.md) and [VISUALHOST-SETUP-GUIDE.md](../../docs/guides/VISUALHOST-SETUP-GUIDE.md) updated with every new Custom Options key + chart-def authoring examples (FR-DOC-09)
- [ ] `/fluent-v9-component` skill invoked at task start for every FR-DOC, FR-VH, FR-SC task (design.md §6.0 binding)
- [ ] BFF additions verified per `.claude/constraints/bff-extensions.md` (Placement Justification documented; publish-size impact verified; no new HIGH-severity CVEs)

## Scope

### In Scope

- 5 surgical changes to existing SemanticSearchControl PCF (list/card toggle, sortable columns + multi-select, 3-dot row menu, tags filter from `sprk_documenttype`, filters relocated to command bar)
- 5 backward-compatible Custom Options additions to Visual Host renderers (`DonutChart`, `MetricCard`, `HorizontalStackedBar`) + internal `CardChrome` wrapper
- 5 new `sprk_chartdefinition` records (Matter Health, Matter Budget, Matter Tasks, Matter Next Date, Matter Activity)
- 2 new shared components (`TagFilter`, `DocumentRowMenu`) in `@spaarke/ui-components`
- 1 new BFF endpoint (`POST /api/documents/bulk-download`) + projection additions to `/api/ai/search`
- Matter main-form Overview tab XML reconfiguration (2-column 66/34, 5 stacked Visual Host instances on right rail)
- Application Insights browser-SDK wiring across both PCFs
- Doc updates to `VISUALHOST-ARCHITECTURE.md` + `VISUALHOST-SETUP-GUIDE.md`

### Out of Scope

- **AI Summary Banner / Insights Engine** — deferred to r2 once Insights Engine ships
- **New visual types** in Visual Host (explicitly forbidden by spec §6.4.0 binding constraint)
- **Parallel chart component libraries** (no `HealthDonut.tsx`, `KpiCard.tsx`, etc. — Visual Host owns rendering)
- **`MatterPerformancePane` container PCF** — form section handles vertical stacking
- **Schema additions to `sprk_matter`** — all rollups already exist (Phase A.5 recon confirmed)
- **New BFF endpoints for Performance pane** — Visual Host reads Dataverse WebAPI/FetchXML directly
- Other Matter tabs (Calendar, Contacts, Email, Billing, Report Card) — unchanged
- Mobile / responsive below 1024 px — desktop MDA only
- Real-time push — cards re-fetch on form load + manual refresh only

## Key Decisions

| Decision | Rationale | ADR / Source |
|---|---|---|
| Use 5 Visual Host instances + 5 chart defs, NOT a new Performance pane PCF | Existing Visual Host infrastructure is generic and sufficient; new PCF would be parallel chart library (forbidden) | spec.md §6.4.0; design.md Rev 6 |
| Tags filter source = `sprk_documenttype` (relocated existing filter) | No new schema; reuses existing `DataverseMetadataService.fetchOptionSet`; existing `FilterPanel` filter relocated, not duplicated | spec.md §FR-DOC-05; §14 Q3 |
| `AiSummaryPopover` retained alongside 3-dot menu entry | Hover quick-glance + keyboard access via menu both valuable | spec.md §FR-DOC-01; §14 Q5 |
| `POST /api/documents/bulk-download` (new BFF endpoint) instead of client-side zip | Server-side streaming via `SpeFileStore` is far less memory-bound than client zip; reuses existing auth + ADR-008 pattern | spec.md §FR-BFF-02; §14 Q15 |
| Per-card `CardChrome.showAiSparkle: false` in v1 | Insights Engine deferred to r2; sparkle slot exists in contract for forward compat | spec.md §FR-VH-05; §14 Q9 |
| Document Type bulk apply uses optimistic UI + 5s Undo toast | Confirmation dialog would block flow for the most common bulk operation | spec.md §FR-DOC-02; §14 Q16 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| FR-VH-01..04 break an existing in-production chart def (NFR-05) | High | Medium | Phase 0 task enumerates the regression-test inventory; Phase 2 task 025 runs a smoke check against every listed chart def before deploy |
| `sprk_event` field names differ from spec assumptions (`sprk_finalduedate`, `sprk_eventstatus`, `sprk_regardingmatter`) | Medium | Medium | Phase 0 task 003 verifies field names via MCP `describe_table` before any Phase 3 chart-def task starts |
| Fluent v9 portal gotcha (Menu / Dialog inside MDA) bites in 3-dot menu or bulk-action dropdowns | Medium | Medium | Every UI task invokes `/fluent-v9-component` (binding); `.claude/patterns/ui/fluent-v9-portal-gotcha.md` loaded |
| FR-BFF-02 bulk-download pushes BFF publish-size past `.claude/constraints/bff-extensions.md` baseline | Low | Low | Uses `System.IO.Compression.ZipArchive` (BCL, no new NuGet); `dotnet publish` size compared before merge |
| Form solution XML edit pushes a managed/unmanaged solution mismatch | Medium | Low | `dataverse:dv-solution` skill handles export/edit/import; one assumption listed in spec to verify |
| Task 010+011 both add to `@spaarke/ui-components` barrel — barrel merge conflict | Low | Medium | 012 (barrel) is a serial follow-up after both ship; not in same parallel group |

## Dependencies

| Dependency | Type | Status | Notes |
|---|---|---|---|
| `@spaarke/auth` v2 | Internal | Ready | Both PCFs already wire it; preserve unchanged (NFR-07) |
| `@spaarke/ui-components` v2.0.0 (Fluent v9.73.2) | Internal | Ready | Adds 2 new exports (`TagFilter`, `DocumentRowMenu`) |
| Visual Host renderers (`DonutChart`, `MetricCard`, `HorizontalStackedBar`, `MetricCardMatrix`) | Internal | Ready | Phase 2 extends these generically — all entry points confirmed |
| `FieldPivotService`, `cardConfigResolver` (Visual Host) | Internal | Ready | FR-VH-01 plugs into existing field-pivot data flow |
| `SpeFileStore` facade (ADR-007) | Internal | Ready | FR-BFF-02 uses `SpeFileStore.DownloadFileAsync` for streamed file fetches |
| Existing `SemanticSearchAuthorizationFilter` (endpoint filter pattern, ADR-008) | Internal | Ready | FR-BFF-02 follows this shape |
| Application Insights resource (instrumentation key) | External | TBD | Confirm with architecture in Phase 0; may use existing Spaarke App Insights or new |
| Dataverse permissions to author `sprk_chartdefinition` + edit Matter form XML | External | TBD | Required for Phase 3 + Phase 6; standard dev-env permissions |
| Matter KPI Scorecard + Matter Financial Metrics Scorecard chart defs (in production) | Internal | Required for NFR-05 | These are the regression baseline for FR-VH-* extensions |

## Team

| Role | Name | Responsibilities |
|---|---|---|
| Owner | spaarke-dev | Overall accountability; final sign-off on graduation criteria |
| AI Implementer | Claude Code | Task execution via `task-execute` skill |
| Reviewer | spaarke-dev | Code review, dark-mode + a11y verification, NFR-05 regression check before merge |

## Changelog

| Date | Version | Change | Author |
|---|---|---|---|
| 2026-05-27 | 1.0 | Initial scaffolding via `/project-pipeline` | Claude Code |

---

*Generated from spec.md by /project-pipeline. See plan.md for phased implementation strategy.*
