# Spaarke DataGrid Framework (R1)

> **Last Updated**: 2026-06-01
>
> **Status**: Draft

## Overview

A configuration-driven dataset grid framework in `@spaarke/ui-components` — `<DataGrid configId={...} />` — that any Spaarke surface (MDA Custom Page, SpaarkeAi workspace widget, MCP App widget, future external SPA) can use to display, filter, and act on any Dataverse entity with **zero per-entity TypeScript code**. The framework's contract is one Dataverse record (`sprk_gridconfiguration`) + one injectable Dataverse client (`IDataverseClient` with `Xrm` and `BFF` implementations). R1 also closes the EventsPage record-link-not-opening bug as a side effect of the new `rowOpen.type: "webResource"` default handler.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phase breakdown, WBS, discovered resources |
| [Design Spec](./design.md) | Technical design (~770 lines, source of truth) |
| [Spec](./spec.md) | AI-optimized implementation specification |
| [AI Context](./CLAUDE.md) | Claude Code context for this project |
| [Current Task](./current-task.md) | Active task state (for context recovery) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker (created by task-create) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning |
| **Progress** | 0% |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | Ralph Schroeder |

## Problem Statement

Each Spaarke surface that needs to display a Dataverse entity grid currently re-implements the entire grid layer: data fetching, filter chips, command bar, density, sort, paging, dialog confirmations, dark-mode portals. EventsPage and SemanticSearch each ship their own ~700-line `GridSection` + filter primitives. The forthcoming Matter Health and Budget Performance drill-throughs would have triggered two more bespoke implementations. The cost is not only duplicated UI code — every variant ships slightly different MDA visual parity, accessibility, and Fluent v9 portal behavior, and one of them (EventsPage record link in dialog mode) is broken. Adding a new entity grid currently requires writing TypeScript.

## Solution Summary

Build one `<DataGrid configId={...} />` component in `@spaarke/ui-components`, driven by a single Dataverse record (`sprk_gridconfiguration.sprk_configjson` v1.0) plus injectable `IDataverseClient` (Xrm or BFF). Five filter chip primitives auto-derive from layoutXml + entity metadata. A six-action command bar (create / delete / refresh / export-excel / edit-columns / edit-filters) covers default CRUD. A host extension model (three emit events + handler registry) lets each surface register its own dialogs / wizards / playbook launchers without forking the framework. Adding a new entity grid = author one Dataverse record + one ~50-line Custom Page; no TypeScript.

## Graduation Criteria

The project is considered **complete** when:

- [ ] Matter Health and Budget Performance drill-through dialogs open working `<DataGrid />` views over `sprk_kpiassessment` and `sprk_invoice` with current-Matter context filter applied
- [ ] EventsPage migrated onto the framework (≤200-line host shell) with no UX regression across all 4 modes (system / dialog / embedded / standalone)
- [ ] EventsPage record-link-not-opening bug closed (verified by opening EventsPage as VisualHost drill-through dialog → click row → record opens correctly)
- [ ] Single `<DataGrid configId={...} />` verified working in MDA Custom Page + SpaarkeAi workspace widget + standalone Storybook
- [ ] Adding a new entity grid in a CI test scenario requires zero new TypeScript (Dataverse record + savedquery + ~50-line Custom Page only)
- [ ] 5 BFF passthrough endpoints (`savedquery`, `savedqueries`, `metadata`, `fetch`, `record`) return correct shapes with ADR-008 authorization filters and meet cache hit-rate >95% after warmup
- [ ] SemanticSearch code page migrated to v1.0 schema with zero visual regression vs. pre-migration screenshot
- [ ] Storybook stories pass axe-core (zero serious/critical violations) for every primitive in light + dark
- [ ] MDA visual parity gates pass: light + dark side-by-side screenshot diff vs. native MDA Events sub-grid; zoom 75/100/125/150%
- [ ] Fluent v9 native exploitation gates pass: no hand-rolled checkbox columns, sort arrows, resize handles, keyboard nav, or sticky headers in framework code
- [ ] Perf budgets met: first-page p50 <2s, lazy-load p50 <1s, bundle size delta <120KB gzip
- [ ] Retire targets confirmed removed: `@spaarke/events-components/{GridSection,AssignedToFilter,RecordTypeFilter,StatusFilter}`, `@spaarke/ui-components/DatasetGrid/{GridView,CardView,ListView,VirtualizedGridView,VirtualizedListView}`, `UniversalDatasetGrid` PCF

## Scope

### In Scope

- New `<DataGrid configId={...} />` component in `@spaarke/ui-components` on Fluent v9 native `DataGrid` primitive
- `IDataverseClient` interface + `XrmDataverseClient` (Xrm.WebApi wrapper) + `BffDataverseClient` (BFF passthrough via `@spaarke/auth.authenticatedFetch`)
- Five filter chip primitives (`LookupMultiFilterChip`, `OptionSetMultiFilterChip`, `DateRangeFilterChip`, `TextFilterChip`, `BoolFilterChip`)
- Command bar primitive with six built-in actions + `custom` action registry
- `sprk_configjson` v1.0 schema + three-tier config resolution + host extension model
- Server-side paging with lazy infinite-scroll loading
- Five BFF passthrough endpoints in `Sprk.Bff.Api/Api/Dataverse/`
- Two new Custom Pages (`sprk_kpiassessmentspage.html`, `sprk_invoicespage.html`) + three new `sprk_gridconfiguration` records + two new savedqueries
- VisualHost chart-def `sprk_drillthroughtarget` updates for Matter Health + Budget Performance
- EventsPage migration onto the framework (~150-line host shell) + record-link bug fix
- SemanticSearch code page migration to v1.0 schema
- Retire legacy grid implementations across `@spaarke/events-components`, `@spaarke/ui-components/DatasetGrid`, and `UniversalDatasetGrid` PCF
- Storybook coverage for every primitive (light + dark + axe)
- Design tokens for MDA visual parity (`DataGrid/tokens.ts`)

### Out of Scope

- `SemanticSearchControl` PCF code — NOT touched
- `VisualHost` PCF code — NOT touched (only chart-def data changes)
- OOB sub-grids on entity main forms — not replaced
- Per-user grid customization (column show/hide, reorder, save view) — R2 candidate
- Per-user filter persistence across sessions — R2 candidate
- Charting / visualization (`VisualHost` remains canonical)
- Write operations through `BffDataverseClient` (R1 = read-only; writes go through Xrm.WebApi)
- Server-side "Export to Excel" via Dataverse workflow (R1 = client-side CSV only)
- Cross-entity joins beyond FetchXML `<link-entity>`

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Build a generic framework rather than two bespoke drill-through grids | OC-01: triggered the entire project — same pattern needed for 4+ surfaces | — |
| `IDataverseClient` abstraction (Xrm + BFF impls) for cross-context portability | OC-05: framework must work outside MDA (workspace widgets, code-pages, future external SPA) | ADR-012, ADR-028 |
| `sprk_gridconfiguration.sprk_configjson` v1.0 as the single config carrier | OC-10/11: entity schema kept minimal; configjson holds everything | — |
| Three-tier config resolution: explicit prop overrides → record → metadata + layoutXml fallbacks | OC-35: zero-config rendering when no record exists; per-view and per-entity records both supported | — |
| Fluent v9 native `DataGrid` primitive — no hand-rolled `<table>` | OC-27: don't hand-roll what Fluent provides | ADR-021 |
| React-16-safe framework code (`useId`/`useSyncExternalStore`/`createRoot` forbidden) | Enables future PCF host adoption in R2 without rewrite | ADR-022 |
| BFF passthrough endpoints in R1 (not R2) | OC-32: full portable framework from day one | ADR-008, ADR-028, ADR-029 |
| Lazy infinite-scroll paging (default 100/page) — not pagination, not "Load More" | OC-30: better UX for the matter-context drill-throughs which are typically <500 records | — |
| Client-side CSV export in R1; server-side Excel deferred | OC-26/33: 99% of users need quick visible-columns export | — |
| `rowOpen.type: "webResource"` default uses `Xrm.Navigation.navigateTo` | Closes record-link-not-opening bug (side pane opens behind dialog) | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Fluent v9 native `DataGrid` lacks a feature we'd taken for granted in hand-rolled (e.g. cell-level focus restoration) | Med | Med | Storybook story per feature gate (NFR-07); if missing, fall back is wrapper-level not framework-level |
| MDA visual parity drift between MDA OOB grid updates and our codified `tokens.ts` | Med | Low | Tokens centralized in one file; quarterly visual diff check |
| Bundle size budget (<120KB gzip on `@spaarke/ui-components`) exceeded by Fluent v9 DataGrid + chips | Med | Med | webpack-bundle-analyzer in CI; budget gate per NFR-10 |
| EventsPage migration introduces functional regression in one of the 4 modes | High | Low | UAT all 4 modes per FR-MIG-04; Calendar pane preserved |
| Cache invalidation gap: savedquery edited in dev appears stale for up to 1 hour (FR-BFF-01) | Med | Low | 1-hour TTL is intentional; if dev-friction high, add cache-bust endpoint in R2 |
| SemanticSearch migration breaks an existing UAT scenario | High | Low | Phase E reserved for SS-specific testing; visual diff gate |
| Lazy-load page cookie expiry (60 min Dataverse default) breaks scroll continuity | Low | Low | AS-03: framework refreshes from page 1 if user returns after 60+ minutes idle |
| Calendar pane mutual exclusivity broken by event detail pane migration | Med | Low | Lift wiring as host concern (D-task), not framework concern |
| `@fluentui/react-components` major version bump during R1 invalidates DataGrid API assumptions | Low | Low | Pin v9.46+; flag major version PRs for review |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| VisualHost v1.4.16 (drill-through dialog mode works) | Internal | Ready | Phase C drill-throughs require this version's behavior |
| `@spaarke/auth` v2 (`authenticatedFetch`) | Internal | Ready | ADR-028; required for `BffDataverseClient` |
| `@spaarke/ui-components` Storybook setup | Internal | Ready | Existing; stories live alongside component code |
| Fluent UI v9.46+ in `@spaarke/ui-components` | External | Ready | Required for `subtleSelection`, `focusMode`, `resizableColumns` props |
| Sprk.Bff.Api `IDistributedCache` (Redis) | Internal | Ready | Existing; required for FR-BFF-01/02/03 caching |
| Dataverse savedquery for `sprk_kpiassessment` Matter-context view | External | Pending | Phase C author task — UQ-04 |
| Dataverse savedquery for `sprk_invoice` Matter-context view | External | Pending | Phase C author task — UQ-04 |
| Power Apps Maker portal access for Custom Page registration | External | Ready | Standard deployment |
| SpaarkeAi Calendar workspace widget owner sign-off on FR-MIG-05 | External | Pending | UQ-06 — needs coordination before Phase D |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability, scope decisions, owner clarifications |
| Developer | Claude Code (AI-assisted) | Implementation across Phase A–F |
| Reviewer | Ralph Schroeder | Code review, ADR compliance, MDA visual parity gates |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-01 | 1.0 | Initial draft via `/project-pipeline` | Claude Code (AI-assisted) |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
