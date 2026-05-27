# Matter UI Enhancement — Design Document

> **Project**: spaarke-matter-ui-enhancement-r1
> **Author**: Ralph Schroeder
> **Date**: 2026-05-26
> **Status**: Draft — Rev 6 (fluent-v9-component skill enforcement added 2026-05-26)
> **Visual contract**: `c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\` (Variant H)
> **Recon report**: Phase A + A.5 (this session) — six sub-agent passes covering Semantic Search PCF, Visual Host PCF + Chart Definitions, shared packages (`@spaarke/ui-components`, `@spaarke/auth`), Visual Host architecture, existing chart definitions on Matter, and per-card prototype-vs-current visual diff

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Problem Statement](#2-problem-statement)
3. [Scope](#3-scope)
4. [User Stories](#4-user-stories)
5. [Architecture](#5-architecture)
6. [Component Specifications](#6-component-specifications)
7. [Visual Reference](#7-visual-reference)
8. [Data Contracts](#8-data-contracts)
9. [API Surface](#9-api-surface)
10. [Implementation Phases](#10-implementation-phases)
11. [Dark Mode & Accessibility](#11-dark-mode--accessibility)
12. [Placement Justification (BFF additions)](#12-placement-justification-bff-additions)
13. [ADR Compliance](#13-adr-compliance)
14. [Open Questions](#14-open-questions)
15. [Acceptance Criteria](#15-acceptance-criteria)

---

## 1. Executive Summary

Redesign the Matter main-form **Overview** tab to deliver a modern, information-dense experience consistent with Microsoft Fluent UI v9 and the current Power Apps model-driven app idiom. The redesign keeps the existing MDA form shell (record header, tab bar) and ships **three deliverables**:

1. **Documents (Semantic Search PCF update)** — five behavior changes to the existing `SemanticSearchControl` PCF: SharePoint-style **list view** with sortable columns + multi-select; **list/card view toggle**; **Tags filter**; consolidated **three-dot row overflow menu** (replaces today's inline action icons AND the dialog-toolbar action icons — 8 actions total); filter chips moved from the always-open sidebar rail to compact command-bar dropdowns.
2. **Matter Performance side pane** — **NOT a new PCF**. Implemented as **five Visual Host instances** (the existing `VisualHost` PCF), each bound to its own `sprk_chartdefinition` record: Matter Health (donut + breakdown), Budget, Tasks, Next Date, Activity. Requires **surgical extensions to Visual Host** (estimated ~10-12 hours of renderer code) so the existing visual types (Donut, MetricCard, HorizontalStackedBar) match the prototype's visual contract; the extensions are **generic** (e.g., `donutLayout: "matrixRight"`, `badge` slot on MetricCard, `descriptionColor`) and benefit any future scorecard.
3. **Matter Information section + Overview form configuration** — surface Matter Information prominently at the top of the left column using native MDA configuration; restructure the Overview tab as a **2-column 66/34 layout** with the Documents PCF in the left section and the five Visual Host cards stacked in the right section.

**Layout target**: 66 / 34 (left / right) on the Overview tab.

**Out of scope (deferred to a future release)**: AI Summary Banner — depends on the **Insights Engine** which has not been built yet. The original design.md draft (Rev 1) included an AI Summary banner PCF; this Rev 2 removes it entirely. A future r2 will reintroduce the banner once the Insights Engine ships.

**Why now**: The current Matter form has billboard-sized KPI gauges and a flat-weighted section grid that obscure hierarchy; the Documents section is dominated by an always-open filter rail and per-row inline action-icon strip that conflict with Fluent standards; matter health is split across three separate cards. The prototype (rounds 1–3, Variant H) validated a layout that fixes all of these.

**Scope reduction relative to Rev 1**: original draft proposed 3 PCFs + 5 new shared components + 2 new BFF endpoints. Rev 2 is **1 PCF update + Visual Host extensions + 5 chart-definition records + 2 small new shared components + 0 new BFF endpoints + 0 new schema additions to `sprk_matter`**.

---

## 2. Problem Statement

The Matter Overview tab as currently shipped has four discrete UX problems:

1. **Information density inverted.** The Performance Grades gauges, Financial Metrics card, and Upcoming Tasks list each consume 250–350 px of vertical space for low-density information (three letter grades, one progress bar, an overdue count). Users cannot scan matter health in a single glance.
2. **No visual hierarchy.** Every section uses identical card chrome, heading styling, and padding. There is no visual cue to indicate primary vs. supporting information.
3. **Documents section is mis-sized.** The filter rail (Associated Only / File Type / Date Range / Threshold / Mode) is always expanded and consumes ~280 px horizontally before the first document is visible. Per-row inline action icons (Preview / AI summary / Open file / Find similar) clutter the row and diverge from the Fluent overflow-menu standard.
4. **Inconsistent with modern Fluent / MDA chrome.** Tag-styled lookups (Matter Type, Practice Area) read as clickable links; SVG gauges are visually heavy and require manual dark-mode tokenization; AI affordances appear without context.

The prototype project (`spaarke-prototype/projects/2026-05-matter-form-redesign`) explored four design directions, then incorporated three rounds of stakeholder feedback to converge on **Variant H** as the approved layout.

---

## 3. Scope

### 3.1 In Scope

- **Matter form Overview tab redesign** — section reconfiguration (2-column 66/34 layout, named sections, ordering)
- **Semantic Search PCF update** at [src/client/pcf/SemanticSearchControl/](src/client/pcf/SemanticSearchControl/): five behavior changes described in §6.3
- **Matter Performance side pane** delivered via five **Visual Host instances** + five **`sprk_chartdefinition` records** + Visual Host renderer extensions (§6.4). **No new PCF folder.**
- **Visual Host extensions** at [src/client/pcf/VisualHost/](src/client/pcf/VisualHost/): generic Custom-Options additions to `DonutChart`, `MetricCard`, `HorizontalStackedBar`, plus a new `CardChrome` wrapper for per-card title + corner icons (§6.4)
- **Two small shared components** in `@spaarke/ui-components`: `TagFilter` (multi-select tag picker for the Documents command bar) and `DocumentRowMenu` (Fluent v9 `Menu` that consolidates row actions). All other previously-proposed shared components (`HealthDonut`, `KpiCard`, `DocumentPreviewDialog`) are obviated by Visual Host extensions and the existing `FilePreview` export
- **Form solution component** — updated Matter main-form XML capturing the new section layout, the five Visual Host placements, and any `sprk_chartdefinition` lookup references
- **Five `sprk_chartdefinition` records** seeded into Dataverse (one per KPI card)
- **Telemetry wiring** — Application Insights (or chosen destination per §14 Q5) for: Semantic Search PCF events (view toggle, tag filter applied, three-dot menu action, preview Dialog opened) and Visual Host events (card render, expand-icon click). Current PCF emits `console.log` only

### 3.2 Out of Scope

- **AI Summary Banner** — depends on Insights Engine which has not been built yet. Deferred to a future r2 once Insights ships
- **Insights Engine endpoints / Services/Ai/Insights/** — not part of this project
- **Other Matter tabs** (Calendar, Contacts, Email, Billing, Report Card) — unchanged
- **Replacement of the Overview tab with a code page** — explicitly rejected; the redesign uses MDA form sections + PCF instances
- **New Dataverse entities or new fields on `sprk_matter`** — the recon confirmed `sprk_matter` already carries the area-grade and financial rollup fields the design needs. Cross-entity data (task counts, next-event date, recent-activity count) is fetched via Visual Host's existing FetchXML aggregation pattern against `sprk_event` and `sprk_workassignment` — no schema additions required
- **New BFF endpoints for Performance pane** — Visual Host uses Dataverse WebAPI / FetchXML directly; no BFF round-trip. (Note: one new BFF endpoint `/api/documents/bulk-download` is added for the Documents PCF bulk-download bulk-action — see §9.1 and §12.2 — but this is unrelated to the Performance pane.)
- **Semantic Search engine changes** — the underlying retrieval pipeline (hybrid / semantic / keyword) is unchanged; only the PCF UI changes
- **Authentication / authorization changes** — both PCFs already use `@spaarke/auth` (`authenticatedFetch`, `resolveTenantIdSync`); no auth wiring changes
- **Mobile / responsive** — desktop MDA form context only; minimum viewport 1024 px. **The right-rail Performance side pane must compress gracefully down to this minimum** (no stacking / no overflow) — see §11.3.
- **Real-time updates** — cards re-fetch on form load + manual refresh; no WebSocket / SignalR push

---

## 4. User Stories

(US-1 and US-2 from Rev 1 — the AI Summary banner stories — are removed; Insights Engine is out of scope.)

| ID | Story |
|---|---|
| **US-3** | As a legal professional, I want to see the matter's composite health grade (A / B+ / C / etc.) with a breakdown of Budget Controls, Guidelines Compliance, and Outcomes Success **in a single card** at the top of the right rail, so I do not have to assemble health context from three separate widgets. |
| **US-4** | As a legal professional, I want to see budget consumed, tasks overdue, next critical date, and recent activity as separate compact cards beneath Matter Health, each with a corner "expand" affordance, so I can drill into any single metric without losing context of the others. |
| **US-5** | As a legal professional, I want to search documents using semantic, keyword, or hybrid modes, with filters for Associated-Only, File Type, Date Range, Threshold, and **Tags**, so I can locate matter documents efficiently. |
| **US-6** | As a legal professional, I want to toggle the documents view between a SharePoint-style list (with sortable columns and multi-select) and a card/tile grid, so I can match the view to my current task (bulk operations vs. visual browsing). |
| **US-7** | As a legal professional, I want each document row's actions hidden behind a single three-dot menu (Preview / AI summary / Open file / Find similar / Download / Copy link / Pin / Rename / Delete / Email / Copy link / Workspace), so the row stays scannable and **all eight actions today scattered between row icons and the preview-Dialog toolbar are consolidated in one place**. |
| **US-8** | As a legal professional, I want clicking a document row to open a large preview Dialog (~960 px) with thumbnail, AI summary, tags, and metadata. (Note: this is already the current behavior via `FilePreviewDialog` — see §6.3.2 for clarification.) |
| **US-9** | As a power user, I want all surfaces (Matter Info, Documents PCF, five Performance cards) to work correctly in both light and dark mode using Fluent tokens, with no hardcoded colors. |
| **US-10** | As a developer maintaining the form, I want the new chart definitions and the extended Visual Host visual types to follow the established Spaarke Visual Host conventions (`sprk_chartdefinition` config, `sprk_optionsjson` Custom Options, token-based color thresholds) so they integrate cleanly and are reusable for any future scorecard. |

---

## 5. Architecture

### 5.1 Form structure (MDA)

The Matter form **Overview tab** is reconfigured. No other tabs are touched. The form header section is **unchanged** (no AI Summary banner in this release).

```
Matter main form
├── Header section (unchanged)
├── Tab: OVERVIEW (active)
│   └── Section layout: 2-column, 66 / 34
│       ├── Left column (66%)
│       │   ├── Section: Matter Information (native MDA, 2 sub-columns)
│       │   │   ├── Matter Number * (text)
│       │   │   ├── Matter Name (text)
│       │   │   ├── Matter Type (lookup, chip-rendered)
│       │   │   ├── Practice Area (lookup, chip-rendered)
│       │   │   └── Matter Description (multiline, spans 2)
│       │   └── Section: Documents (single field bound to Semantic Search PCF)
│       └── Right column (34%)
│           └── Section: Matter Performance
│               ├── Visual Host instance: Matter Health   (chart def: "Matter Health Composite")
│               ├── Visual Host instance: Budget          (chart def: "Matter Budget")
│               ├── Visual Host instance: Tasks           (chart def: "Matter Tasks")
│               ├── Visual Host instance: Next Date       (chart def: "Matter Next Date")
│               └── Visual Host instance: Activity        (chart def: "Matter Activity")
├── Tab: CALENDAR    (unchanged)
├── Tab: CONTACTS    (unchanged)
├── Tab: EMAIL       (unchanged)
├── Tab: BILLING     (unchanged)
└── Tab: REPORT CARD (unchanged)
```

### 5.2 Component inventory

| Component | New / Existing | Type | Location |
|---|---|---|---|
| **Matter Information section** | Existing (config only) | MDA native section | Form XML |
| **Documents** | Existing (5 sub-task updates) | PCF (virtual, Fluent v9) | [src/client/pcf/SemanticSearchControl/](src/client/pcf/SemanticSearchControl/) |
| **Five Performance KPI cards** | New (chart definitions only) | `sprk_chartdefinition` records consumed by Visual Host | Dataverse data |
| **Visual Host renderer extensions** | Updates to existing PCF | PCF (virtual, Fluent v9) | [src/client/pcf/VisualHost/](src/client/pcf/VisualHost/) — see §6.4 |
| **CardChrome wrapper** | New | React component (internal to Visual Host) | `src/client/pcf/VisualHost/control/components/CardChrome.tsx` |
| **TagFilter** | New (shared) | React component | `src/client/shared/Spaarke.UI.Components/src/components/TagFilter.tsx` |
| **DocumentRowMenu** | New (shared) | React component | `src/client/shared/Spaarke.UI.Components/src/components/DocumentRowMenu.tsx` |

**Components proposed in Rev 1 that are obviated by recon**:

| Component | Why removed |
|---|---|
| `HealthDonut` (shared) | Visual Host's existing `DonutChart` visual type with new Custom Options handles this — see §6.4.1 |
| `KpiCard` (shared) | Visual Host's existing `MetricCard` + `HorizontalStackedBar` + `DueDateCard` cover all 5 cards |
| `DocumentPreviewDialog` (shared) | Already exists as `FilePreview` in `@spaarke/ui-components` (44+ exports); reuse |
| `MatterPerformancePane` (new PCF) | Five separate Visual Host instances placed by form XML — no container PCF needed |
| `MatterSummaryBanner` (new PCF) | Insights Engine out of scope — deferred to r2 |

### 5.3 Data flow

```
Matter form load
  ↓
Matter record (Dataverse, via xrm context)
  ↓
┌──────────────────────────┬──────────────────────────────────────────┐
│ Documents (PCF)          │ Five Visual Host instances               │
│  ↓                       │  ↓ each reads its own sprk_chartdefinition
│ @spaarke/auth →          │  ↓ each runs WebAPI / FetchXML
│ authenticatedFetch →     │     against sprk_matter (rollup fields)
│ POST /api/ai/search      │     or sprk_event / sprk_workassignment
│ (existing)               │     (cross-entity aggregation)
└──────────────────────────┴──────────────────────────────────────────┘
```

**Cold load target**: <2.0s p95 for the full form. All five Visual Host instances fetch in parallel via WebAPI; the Documents PCF fetches via BFF. Each renders a skeleton until its data resolves.

**No new BFF endpoints for the Performance side.** Visual Host's existing pipeline handles every card.

---

## 6. Component Specifications

### 6.0 BINDING — `/fluent-v9-component` skill is mandatory for every UI task

> 🚨 **Every task in this project that authors or modifies a Fluent UI v9 React component MUST invoke the `/fluent-v9-component` skill at task start.** This is enforced in `code-review`.

**Trigger surface (binding)**: any task touching files in any of these locations:

- `src/client/pcf/SemanticSearchControl/**/*.tsx` (FR-DOC-01..06)
- `src/client/pcf/VisualHost/control/components/**/*.tsx` (FR-VH-01..05)
- `src/client/shared/Spaarke.UI.Components/src/components/**/*.tsx` (FR-SC-01..02)
- Any other Fluent v9 React component authored or modified by this project

**What the skill does** (per its [SKILL.md](.claude/skills/fluent-v9-component/SKILL.md)):

- Loads the Fluent UI v9 authoring patterns ([.claude/patterns/ui/](.claude/patterns/ui/), [.claude/patterns/pcf/](.claude/patterns/pcf/))
- Applies Spaarke conventions (theming via `tokens.color*` only; no hex; Modern Theming API where applicable)
- Surfaces critical gotchas (portal rendering inside MDA, React version boundaries for shared components consumed by both PCFs and Code Pages, slots architecture for component variants)

**Rules** (all binding):

1. **MUST invoke `/fluent-v9-component` as Step 0.5 of any UI-authoring task**, before reading the affected files.
2. **MUST cite which Fluent v9 patterns informed the implementation** in the PR description (e.g., "applied [fluent-v9-portal-gotcha.md](.claude/patterns/ui/fluent-v9-portal-gotcha.md) for the Menu inside the MDA form").
3. **MUST verify no hardcoded hex/rgb** before merge (automated grep — see NFR-04 in spec.md).
4. **MUST use Fluent v9 slots architecture** (not arbitrary `className` prop forking) for component variants.
5. **MUST respect React version boundaries** — shared components in `@spaarke/ui-components` consumed by both PCFs (React 16/17 platform libs per ADR-022) and Code Pages (React 19 per ADR-021) must not use React 18+ exclusive features.

**Anti-patterns forbidden in this project**:

- ❌ Skipping the skill invocation "because the change is small" — Fluent v9 has too many subtle gotchas (portals, slots, theming, React version) for the skill to be optional
- ❌ Authoring a Fluent v9 component by copying from another component without consulting the patterns
- ❌ Using `useStyles` patterns inconsistent with Griffel's idioms (see [fluent-v9-component-authoring.md](.claude/patterns/ui/fluent-v9-component-authoring.md))
- ❌ Rendering a Dialog / Menu / Tooltip inside MDA without applying the portal gotcha fix (see [fluent-v9-portal-gotcha.md](.claude/patterns/ui/fluent-v9-portal-gotcha.md))
- ❌ Mixing Fluent v8 imports (`@fluentui/react`) with v9 (`@fluentui/react-components`) in any file touched by this project

This binding section is the UI counterpart to §6.4.0 (Visual Host engine constraint). Where §6.4.0 governs **chart rendering** architecture, §6.0 governs **Fluent v9 component authoring**. Both are non-negotiable for this project.

---

### 6.1 (Removed — AI Summary Banner deferred to r2)

The Rev 1 AI Summary banner specification is removed. Insights Engine has not been built. When that ships, a future project will reintroduce the banner as either a new PCF or a Visual Host visual type.

### 6.2 Matter Information section

**No code change.** Pure MDA form configuration:

- Section title: "Matter Information"
- Layout: 2 sub-columns
- Fields, in order: Matter Number (required, full width of left sub-column), Matter Name (full width of right sub-column), Matter Type (lookup, left), Practice Area (lookup, right), Matter Description (multiline, spans both sub-columns)
- Default field-level styling (chip rendering for Matter Type / Practice Area lookups) is retained — confirm Power Apps default rendering matches the prototype; if not, no remediation in this project (out of scope)

### 6.3 Documents — Semantic Search PCF (update)

**Existing PCF root**: [src/client/pcf/SemanticSearchControl/](src/client/pcf/SemanticSearchControl/)
**Entry**: [SemanticSearchControl.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx) (1012 lines)
**Manifest**: virtual PCF, Fluent v9 (9.46.2), React 16.14 (platform libs per ADR-022). Already uses `@spaarke/auth` (`authenticatedFetch`, `resolveTenantIdSync`) and `@spaarke/ui-components` (`themeStorage`, `AiSummaryPopover`). Modern Theming runtime detection via `ThemeService`.

**Prototype reference**: [`MatterDocuments.tsx`](file:///c:/code_files/spaarke-prototype/projects/2026-05-matter-form-redesign/src/shared/MatterDocuments.tsx), [`DocumentList.tsx`](file:///c:/code_files/spaarke-prototype/projects/2026-05-matter-form-redesign/src/shared/DocumentList.tsx).

**Five behavior changes** — each can be implemented as a discrete task:

#### 6.3.1 Three-dot menu — consolidate **all 8** row actions

Replace the current inline icons with a single `MoreVertical20Regular` button that opens a Fluent v9 `Menu`. The menu must consolidate **all 8 existing actions**, not just the 4 inline-card actions. Recon confirmed:

- **4 inline-card actions today** (in [ResultCard.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/ResultCard.tsx) lines 230–272): Preview (eye), AI Summary (sparkle / `AiSummaryPopover`), Open File (folder), Find Similar (document-search)
- **4 dialog-toolbar actions today** (in [FilePreviewDialog.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx) lines 150–287): Open Record, Email, Copy Link, Toggle Workspace (star)

**Menu structure** (in order): `Preview` · `AI summary` · `Open file` · `Find similar` · *divider* · `Download` · `Copy link` · `Email` (single-doc convenience — kept per §14 Q3) · `Open record` · *divider* · `Toggle workspace` · `Pin to top` · `Rename` · `Delete`.

- Menu trigger: `appearance="subtle"`, `size="small"`. The trigger button **must `stopPropagation`** so the row's own click handler (preview) does not fire.
- **`AiSummaryPopover` hover affordance**: KEPT on the sparkle icon (low-friction quick-glance) AND surfaced as the "AI summary" menu item (keyboard-reachable). Two entry points, same content (§14 Q4 resolution).

This work uses the new **`DocumentRowMenu`** shared component (§5.2).

#### 6.3.1.b Bulk-action menu (visible when ≥1 row checked in list view)

When list view is active and the user selects ≥1 row via checkbox, a contextual bulk-action bar appears above the column header row. Actions, in order:

| Action | Behavior | Notes |
|---|---|---|
| **Email selected** | Email the SPE file(s) from selected document records as **attachments**. Multiple = single email with all attached as a zip. | Reuses existing single-doc email pipeline (`SendEmailDialog` / `DocumentEmailWizard`) extended for multi-attachment zip case |
| **Download selected** | Download the SPE file(s) from selected document records. Multiple = single zip. | **New BFF endpoint `POST /api/documents/bulk-download`** (resolved during Step 2.5 of design-to-spec) — POSTs list of document IDs, streams a zip back. See §9.1 and §12.2 placement justification. |
| **Pin selected** | Pin selected docs to top of the list (per-user-per-matter persistence, same store as view preference) | Sets `pinned` flag in localStorage; no Dataverse write. List view shows a small pin icon **only on pinned rows** in the dedicated Pin column; clicking the icon toggles pin state. |
| **Delete selected** | Soft-delete selected records (Dataverse). Confirmation Dialog required. | Standard Xrm.WebApi delete + optimistic UI refresh |
| **Document type → selected** | Assign a Document Type choice value (single-select choice) to all selected records. | Sub-menu lists `sprk_documenttype` option values. **UX: optimistic — apply immediately, show toast with 5-second Undo affordance**. On failure, toast becomes error with Retry; Undo restores prior values via Xrm.WebApi reverse update. |
| **Share link** | Open an email composer whose body is pre-populated with **clickable links to each selected `sprk_document` record** (NOT the SPE file). Body lines render as `{Document Name} → {URL}`. No attachments. | Distinct from "Email selected" which emails files. URL format: `https://{env}.crm.dynamics.com/main.aspx?etn=sprk_document&id={guid}&pagetype=entityrecord`. Reuses `SendEmailDialog` with a different body-template path. |

The bulk-action bar must:
- Show a count badge ("3 selected") and a "Clear" affordance to deselect all
- Hide when 0 rows selected
- Stay sticky above the column header so users can scroll while keeping actions reachable

#### 6.3.2 Click-to-Dialog preview (already mostly implemented — clarify scope)

**Important correction from Rev 1**: The current PCF **already** opens a click-to-Dialog preview via [FilePreviewDialog.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx) (85vw × 85vh modal, lines 143–287). There is **no hover preview popover on documents** to remove. The only hover popover in the PCF today is `AiSummaryPopover` on the sparkle icon (for AI summary text, not document preview).

**Scope for this task**:

- Confirm row click (excluding the three-dot menu trigger) opens `FilePreviewDialog`. (Already works via [SemanticSearchControl.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx) line 171 — keep behavior.)
- Resize the Dialog to the prototype's 960 px max-width (currently 85vw — may be wider). Confirm width target.
- Restructure the Dialog body to match the prototype: two-column grid, left ~640 px thumbnail area, right 320 px metadata pane.
- Right pane content (in order): AI summary section (sparkle icon + paragraph), Tags section (outline badges sourced from `sprk_filekeywords` — see §14 Q1), Details section (Created by / Created / Size / Type).
- Dialog actions, left to right: `Find similar` (subtle) · `Close` (secondary) · `Open file` (primary).

#### 6.3.3 List/card view toggle

- New command bar control: two-button toggle group at the right end of the command bar — `AppsList20Regular` (list view) / `Grid20Regular` (card view).
- Active state uses `appearance="primary"`; inactive uses `appearance="subtle"`.
- **List view** (default): SharePoint-style with columns: Selection checkbox · **Pin** (small pin icon visible only on pinned rows; click to toggle) · Name (file icon + link-styled name) · **Modified** · **Modified by** (avatar + name) · Match · Three-dot menu. Column headers are sortable (click to toggle asc/desc, indicator: `ArrowSortUp/Down`). Pinned rows sort to the top regardless of active column sort.
  - **Default sort**: `modifiedAt DESC` (per §14 Q2 resolution).
  - **BFF projection addition required**: `SearchResult` today returns `createdAt` + `createdBy` only. Both `created*` AND `modified*` will be projected (per §14 Q2 — keep both for flexibility). `/api/ai/search` projection adds `modifiedAt: string` (ISO), `modifiedBy: string`.
- **Card view**: today's existing `ResultCard.tsx` render, with the 3-dot menu in place of inline icons.
- View preference persists per-matter-per-user in `localStorage` keyed by `(userId, matterId)` for v1. Per-user-global preference deferred to v2.

**Genuinely net new**: no list-layout component exists today; no checkbox/multi-select state; no sortable columns. All from-scratch in this PCF.

#### 6.3.4 Tags filter (sourced from `sprk_documenttype`)

**Resolved per §14 Q1**: the filter labeled "Tags" in the prototype is sourced from `sprk_documenttype` (existing single-choice column on `sprk_document`). The free-text `sprk_filekeywords` field is NOT used. This means:

- **No schema change required.**
- **No BFF projection change for tags** — `SearchResult.documentType` is already projected (per existing `search.ts` shape).
- **Each document has one Document Type value** displayed as a single chip in the preview Dialog "Tags" section.
- **The filter is multi-select across distinct Type values** — a document matches if its `documentType` value is in the selected set (OR semantics).

**Command-bar control**: a `Menu` with `MenuItemCheckbox` entries.

- Trigger button label: "Tags" (when none selected) or "Tags (N)" where N = selected count.
- **Option source**: `sprk_documenttype` option-set values fetched via the existing `DataverseMetadataService.fetchOptionSet('sprk_documenttype')` (already used by the current FilterPanel for the Document Type filter). Sort alphabetically.
- Selection semantics: **inclusive** (OR) across the selected option values.
- When one or more tags are selected, render an active-filters chip row below the command bar: "Filtered by: `[Type1 ×] [Type2 ×] … Clear all`". Each chip's `×` removes that value; "Clear all" empties the selection.
- Update the footer count to reflect filtered results: e.g., "Showing 5 of 31 documents · filtered by 2 tags".

**Relationship to existing Document Type filter**: the current FilterPanel sidebar has a Document Type multi-select dropdown. This task **relocates it to the command bar** with the chip-style chrome above, and renames the UI label "Document Type" → "Tags" (matching the prototype). The underlying field is unchanged.

This work uses the new **`TagFilter`** shared component (§5.2) — a generic multi-select chip filter usable for any choice field; `sprk_documenttype` is the first consumer.

#### 6.3.5 Move filters from sidebar rail to command-bar dropdowns

- Remove the always-open vertical filter sidebar ([FilterPanel.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilterPanel.tsx)).
- Move existing filters (Associated Only, File Type, Date Range, Threshold, Mode, Matter Type) into the command bar as compact dropdown buttons next to the new Tags filter.
- **Preserve the AssociatedOnly auto-search behavior** — current code at [SemanticSearchControl.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx) lines 359–375 auto-triggers re-search on toggle. This must continue to work when the toggle moves to the command bar.
- **Drawer escape hatch (open question — §14 Q6)**: Rev 1 mentioned a "Filter" button opening a Drawer with the full filter UI for power users. Decide for v1 whether this is needed or whether the command-bar-only path is sufficient.

**Backwards compatibility**: existing PCF manifest properties **must not break**. All new behaviors (view mode, tag filter) are additive; existing search query semantics, mode default (Hybrid), and result-set contract are unchanged.

**Telemetry** (currently `console.log` only — see §14 Q5 for destination): track `view_toggled` (value), `tag_filter_applied` (tag count), `three_dot_menu_action_invoked` (action name), `preview_dialog_opened`.

### 6.4 Matter Performance — five Visual Host instances + chart definitions

#### 6.4.0 BINDING CONSTRAINT — Visual Host is the chart engine

> 🚨 **This constraint is binding for every task in this project that touches charts, KPI cards, or scorecard rendering.** Violations are blockers in `code-review`.

**Read first (mandatory before any Visual Host or chart-definition task)**:
- [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) — extension model, 3-tier configuration system (PCF prop > chart def field > `sprk_optionsjson` Custom Options > defaults), `IChartDefinition` / `ICardConfig` / `IFieldPivotConfig` / `IColorThreshold` types, visual-type registry
- [docs/guides/VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) — chart-definition authoring, FetchXML patterns, theming, drill-through

**Rules** (all binding):

1. **REUSE existing visual types.** All 5 cards are rendered by existing visual types: `Donut` (100000004), `MetricCard` (100000000), `HorizontalStackedBar` (100000012), `DueDateCard` (100000008). **No new visual type is added by this project.**
2. **EXTEND via Custom Options, not new components.** Where a card's prototype intent isn't met by existing options, add **generic** keys to `sprk_optionsjson` vocabulary (`donutLayout`, `badge`, `descriptionColor`, `layoutMode`). Every extension MUST be usable by any future scorecard — no Matter-specific naming, no Matter-specific code paths.
3. **MINIMAL renderer code changes only.** When Custom Options aren't enough (e.g., Donut needs `fieldPivot` consumption), the code change is a thin addition inside the existing visual-type renderer (`DonutChart.tsx`, `MetricCard.tsx`, `HorizontalStackedBar.tsx`). **Do not create new visual-type components.**
4. **NO parallel chart component library.** Do NOT create `HealthDonut`, `KpiCard`, `MatterHealthComposite`, `ScorecardCard` etc. in `@spaarke/ui-components` or anywhere else. Visual Host owns chart rendering. Period.
5. **Configuration over code.** Every "feature" for these cards should manifest first as a chart-definition record + Custom Options JSON. Code changes are the exception, not the default.
6. **Backward compatibility is binding.** Every Visual Host extension MUST leave existing chart definitions rendering unchanged (verify with the existing Matter KPI Scorecard, Matter Financial Metrics Scorecard, and other in-production chart defs).
7. **Document your Custom Options.** Every new Custom Options key added by this project MUST be documented in [VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) and [VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) in the same PR that introduces it.
8. **Design rationale check.** Every PR that adds Visual Host code MUST include a "Why not Custom Options alone?" paragraph in the description, explaining why the change couldn't be achieved by configuration. This is enforced in code review.

**Anti-patterns explicitly forbidden in this project**:

- ❌ Creating a new visual type (`MatterHealthComposite`, `ScorecardDonut`, etc.)
- ❌ Building a parallel React component (`HealthDonut.tsx`, `KpiCard.tsx`) that duplicates Visual Host's renderers
- ❌ Bypassing `sprk_chartdefinition` and hard-coding chart configuration in a PCF
- ❌ Building a `MatterPerformancePane` container PCF that wraps Visual Host instances (the form section's section-level layout handles stacking; no container needed)
- ❌ Adding Matter-specific code paths inside generic Visual Host components

**Evidence base**: this constraint reflects the recon findings from Phase A.5 — Visual Host is sufficiently expressive that ~all the design requirements reduce to Custom Options additions + ~10-12h of generic renderer code. The two existing in-production chart definitions (Matter KPI Scorecard, Matter Financial Metrics Scorecard) prove the pattern.

---

**Strategy**: every KPI card on the right rail is a separate `VisualHost` PCF instance bound to a Matter form section, configured by a `sprk_chartdefinition` record in Dataverse. Visual Host (a virtual PCF using Fluent v9 + token-based theming + WebAPI data fetching) is the chart engine; per-card customization happens via `sprk_optionsjson` Custom Options.

**Existing comparable chart definitions** that established this pattern:
- **Matter KPI Scorecard** — MetricCard visual type, `fieldPivot` over `sprk_guidelinecompliancegrade_current`, `sprk_budgetcompliancegrade_current`, `sprk_outcomecompliancegrade_current`, with `valueFormat: "letterGrade"` + `colorThresholds`
- **Matter Financial Metrics Scorecard** — MetricCard visual type, `fieldPivot` over `sprk_totalbudget`, `sprk_totalspendtodate`, `sprk_remainingbudget` with `valueFormat: "currency"`

The five new chart definitions extend this pattern.

#### Per-card mapping

| Card | Visual Type | Data source | `sprk_optionsjson` keys | Renderer code change |
|---|---|---|---|---|
| **6.4.1 Matter Health** | `Donut` (100000004) — extended | `sprk_matter` (3 fields: `sprk_guidelinecompliancegrade_current`, `sprk_budgetcompliancegrade_current`, `sprk_outcomecompliancegrade_current`). **Donut center value = mean of the 3 (per §14 Q10)** rendered as the composite letter + score/100. | `fieldPivot.fields[]`, `colorThresholds[]`, `valueFormat: "letterGrade"`, **net-new**: `donutLayout: "matrixRight"`, `donutCenterMode: "meanOfFields"` (compute center as `mean(fieldPivot.fields[].value)`), `donutCenterLabel`, `showBreakdownRows: true`, `breakdownValueFormat: "scoreOver100"` | ~4h: enable `fieldPivot` consumption in `DonutChart.tsx`; render donut + labeled-row layout; segment coloring from thresholds; compute center via mean |
| **6.4.2 Budget** | `HorizontalStackedBar` (100000012) — extended | `sprk_matter` (`sprk_totalbudget`, `sprk_totalspendtodate`) | `colorThresholds`, `valueFormat: "currency"`, **net-new**: `layoutMode: "headlineAboveBar"`, `headlineFromField`, `subLineTemplate` | ~2h: refactor layout to render headline + sub-line ABOVE the bar (prototype mockup) instead of around it |
| **6.4.3 Tasks** | `MetricCard` (100000000) — extended | **FetchXML aggregation** on `sprk_event`: count where regardingmatter=current + finalduedate<today + status≠Completed; second count for upcoming-this-month | `fieldPivot.fields[]` (overdue, upcoming), **net-new**: `badge: { text, tone: "danger" \| "warning" \| "success", position: "inline" }` | ~1h: add Fluent `Badge` slot next to the value when `badge` is present |
| **6.4.4 Next Date** | `DueDateCard` (100000008) | **FetchXML lookup** on `sprk_event`: TOP 1, ORDER BY finalduedate ASC, WHERE regardingmatter=current AND finalduedate≥today | None new — existing options sufficient | **No renderer code change** ✅ |
| **6.4.5 Activity** | `MetricCard` (100000000) — extended | **FetchXML aggregation** on `sprk_event`: count where regardingmatter=current AND actualstart or completeddate in last 7 days | `fieldPivot.fields[]` (count), **net-new**: `descriptionColor: "brand" \| "neutral" \| "success" \| "warning" \| "danger"` | ~30min: token-based color override on description text |

#### 6.4.6 Per-card chrome — new `CardChrome` wrapper

Visual Host today renders AI-sparkle + expand icons at the **frame/toolbar level** ([VisualHostRoot.tsx](src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx) lines 531–555), not per-card. The prototype shows each card with its own title bar + corner icons (AI sparkle where applicable, expand on all).

Add a new **`CardChrome.tsx`** wrapper component inside Visual Host:

```typescript
interface ICardChromeProps {
  title?: string;
  onExpand?: () => void;
  onAiSummary?: () => Promise<ISummaryData>;
  showAiSparkle?: boolean;   // gated on Insights Engine availability — see §14 Q7
  children: React.ReactNode;
}
```

`VisualHostRoot.tsx` wraps each card component with `CardChrome`, passing the chart definition's `sprk_name` as `title` and wiring `onExpand` to the **existing `handleExpandClick`** which honors the chart def's **Drill Through Settings** (`sprk_drillthroughtarget` + related fields). Per §14 Q9 resolution, the expand icon opens the underlying entity's list view (e.g., Matter Health → `sprk_kpiassessment` list filtered to current matter; Budget → `sprk_invoice` list; Tasks/Next Date/Activity → `sprk_event` list with appropriate filter). **No new ClickActionHandler code.**

`showAiSparkle` defaults to **`false` for v1** (per §14 Q7 resolution — Insights Engine not yet shipped); the slot exists in the component contract so r2 can surface it without further code changes.

**Estimated effort**: ~3h.

#### 6.4.7 Card expand → drill-through (resolved per §14 Q9)

The corner expand icon on each card reuses Visual Host's **existing Drill Through Settings** on `sprk_chartdefinition` (`sprk_drillthroughtarget` + related fields, consumed by `handleExpandClick` in `VisualHostRoot.tsx` lines 314–428). **No new code in `ClickActionHandler.ts`.**

Per-card chart-definition drill targets:

| Card | Drill target entity | Filter |
|---|---|---|
| Matter Health | `sprk_kpiassessment` | `_sprk_matter_value` = current matter |
| Budget | `sprk_invoice` | `_sprk_matter_value` = current matter |
| Tasks | `sprk_event` | `_sprk_regardingmatter_value` = current matter AND (`finalduedate < today` OR `finalduedate BETWEEN today AND today+30`) |
| Next Date | `sprk_event` | `_sprk_regardingmatter_value` = current matter AND `finalduedate ≥ today` |
| Activity | `sprk_event` | `_sprk_regardingmatter_value` = current matter AND `(actualstart OR completeddate) ≥ today-7` |

Each filter is encoded into the chart definition's Drill Through Settings (`sprk_drillthroughtarget` + `filterField` + `filterValue` parameters). The expand opens the configured entity list view in the drill-through web-resource dialog (90%×85% per existing pattern). The richer per-card Fluent Dialog drill-down is **deferred to a future release**.

#### Total Visual Host renderer code surface

- **New files** (2): `CardChrome.tsx`, no other new components needed
- **Modified files** (4): `DonutChart.tsx`, `MetricCard.tsx`, `HorizontalStackedBar.tsx`, `ChartRenderer.tsx`, `VisualHostRoot.tsx` (CardChrome wiring)
- **Unchanged files**: `DueDateCard.tsx` already aligned

**Total estimated effort**: ~10-12 hours of renderer work + 5 chart-definition records seeded into Dataverse + form section configuration.

**Empty / loading states** required on every card: shimmer skeleton during initial fetch, "—" or "No data yet" if the underlying entity returns no records.

---

## 7. Visual Reference

### 7.1 Approved mockup

`c:\code_files\spaarke-prototype\projects\spaarke-matter-details-layout=v2\matter mock up.jpg` — the user's composite that anchored Variant H.

### 7.2 Project screenshots

[projects/spaarke-matter-ui-enhancement-r1/screenshots/](projects/spaarke-matter-ui-enhancement-r1/screenshots/) — five prototype screenshots showing the target layout: `matter-overview-1.png`, `documents-section-1.png`, `documents-section-2.png`, `document-details-modal-1.png`, `performance-section-1.png`.

### 7.3 Prototype implementation

`c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\` — Variant H. The prototype uses Fluent v9 only, mock data, and contains no production logic. **Do not copy code verbatim** — re-implement against real Dataverse / BFF data. The prototype is a visual contract, not a code source.

### 7.4 Visual Host as the chart engine (binding architectural stance)

The prototype's KPI cards are **rendering contracts**, not code prescriptions. Production renders them via Visual Host with `sprk_chartdefinition` records — the Visual Host extensions in §6.4 are the **only** code path. Do not build a parallel chart component library.

See **§6.4.0** for the full binding constraint, anti-patterns, and required doc reviews before any chart-related task.

---

## 8. Data Contracts

### 8.1 `sprk_matter` field surface — confirmed via recon

**Available now (no schema change needed)**:

| Field | Type | Used by card |
|---|---|---|
| `sprk_guidelinecompliancegrade_current` | Decimal 0.00–1.00 | Matter Health |
| `sprk_budgetcompliancegrade_current` | Decimal 0.00–1.00 | Matter Health |
| `sprk_outcomecompliancegrade_current` | Decimal 0.00–1.00 | Matter Health |
| `sprk_guidelinecompliancegrade_average` | Decimal | (trend, deferred) |
| `sprk_budgetcompliancegrade_average` | Decimal | (trend, deferred) |
| `sprk_outcomecompliancegrade_average` | Decimal | (trend, deferred) |
| `sprk_totalbudget` | Currency | Budget |
| `sprk_totalspendtodate` | Currency | Budget |
| `sprk_remainingbudget` | Currency | Budget (sub-line) |
| `sprk_budgetutilizationpercent` | Decimal | Budget (sub-line) |
| `sprk_invoicecount` | Whole Number | (reserved) |
| `sprk_activesignalcount` | Whole Number | (reserved) |

**Computed client-side from existing fields** (no schema needed):

| Computed value | Formula | Source |
|---|---|---|
| Composite score (0–100, for Matter Health donut center sub-value) | `mean(guidelinescore, budgetscore, outcomescore) × 100` — simple arithmetic mean (per §14 Q10) | 3 fields above |
| Composite letter grade (for Matter Health donut center letter) | Apply `valueFormat: "letterGrade"` thresholds to the composite score | Visual Host's existing `valueFormatters.ts` |

**Cross-entity via FetchXML aggregation (no schema needed)**:

| Card | FetchXML target | Query semantics |
|---|---|---|
| Tasks (overdue) | `sprk_event` | COUNT where regardingmatter=current AND finalduedate<today AND status≠Completed |
| Tasks (upcoming this month) | `sprk_event` | COUNT where regardingmatter=current AND finalduedate BETWEEN today AND today+30 AND status≠Completed |
| Next Date | `sprk_event` | TOP 1, ORDER BY finalduedate ASC, WHERE regardingmatter=current AND finalduedate≥today |
| Activity (last 7 days) | `sprk_event` | COUNT where regardingmatter=current AND (actualstart OR completeddate) in last 7 days |

### 8.2 Documents (search results) — additive projection requirements

The existing `SearchResult` shape ([search.ts](src/client/pcf/SemanticSearchControl/SemanticSearchControl/types/search.ts) lines 82–103) is unchanged in semantics. **Two** fields must be **added** to the projection (down from three in Rev 2 — `tags` no longer needed, see §6.3.4):

| Field | Purpose | Source on `sprk_document` |
|---|---|---|
| `modifiedAt: string` (ISO) | Sortable "Modified" column in list view + default sort | `modifiedon` (standard system field) |
| `modifiedBy: string` | Sortable "Modified by" column in list view | `modifiedby` (standard system lookup) |

The existing `documentType: string` is reused for the Tags filter and the Dialog Tags chip (per §6.3.4 — sourced from `sprk_documenttype`). The existing `aiSummary` is fetched on demand from `sprk_FileSummary` (ntext). The existing `tldr` is from `sprk_filetldr`. Thumbnail URL comes from the existing `/api/documents/{id}/preview-url` endpoint.

### 8.3 (Removed — AI Summary model deferred to r2 with Insights Engine)

---

## 9. API Surface

### 9.1 New endpoints

Performance side: **no new endpoints**. Visual Host reads `sprk_matter` and aggregates `sprk_event` / `sprk_workassignment` via Dataverse WebAPI / FetchXML directly from the PCF context.

Documents PCF: **one new endpoint** for bulk-download bulk-action:

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/documents/bulk-download` | Accepts JSON `{ documentIds: string[] }`; streams a zip file containing the requested SPE files; sets `Content-Disposition: attachment; filename="documents-{matterId}-{timestamp}.zip"`. Implements server-side zip assembly via existing Graph / SPE file access. See §12.2 placement justification. |

### 9.2 Existing endpoints used by Documents PCF (unchanged behavior, projection additive)

| Method | Path | Used by |
|---|---|---|
| POST | `/api/ai/search` | Documents PCF — semantic / hybrid / keyword search. **Projection must add `tags`, `modifiedAt`, `modifiedBy`.** |
| GET | `/api/documents/{id}/preview-url` | Documents PCF — preview Dialog iframe |
| GET | `/api/documents/{id}/open-links` | Documents PCF — Open File menu action |
| GET (or POST) | `/api/documents/{id}/find-similar` | Documents PCF — Find similar menu action |
| (Dataverse) | sprk_document `sprk_FileSummary` field | Documents PCF — AI Summary menu action (on-demand WebAPI fetch from result row) |

### 9.3 Authentication — already using `@spaarke/auth`

Both PCFs (`SemanticSearchControl` and `VisualHost`) already use the shared `@spaarke/auth` v2 package. No changes required. The shared SSO contract is **binding** per [feedback memory](C:/Users/RalphSchroeder/.claude/projects/c--code-files-spaarke/memory/feedback_auth-true-sso-requirement.md).

Canonical wiring (per [authInit.ts](src/client/pcf/SemanticSearchControl/SemanticSearchControl/authInit.ts) in `SemanticSearchControl`):

```typescript
import { initAuth, type IAuthConfig } from '@spaarke/auth';
const config: IAuthConfig = {
  clientId: clientAppId,
  redirectUri: window.location.origin,
  bffApiScope: `api://${bffAppId}/SDAP.Access`,
  bffBaseUrl: bffApiUrl,
  proactiveRefresh: true,
};
// authority intentionally omitted — @spaarke/auth resolves tenant-specific via resolveTenantFromXrm()
await initAuth(config);
```

All BFF calls route through `authenticatedFetch(url, opts)`. Shared MSAL `localStorage` cache (INV-1) gives true SSO across PCFs and code pages.

---

## 10. Implementation Phases

### Phase 0 — Recon (DONE)

Phase A + A.5 recon (six sub-agent passes; see Rev 2 metadata at top of doc). Outcomes:
- Semantic Search PCF current state audited
- Visual Host architecture, extension model, and Custom Options vocabulary documented
- `sprk_matter` field surface inventoried; composite letter computable client-side; cross-entity data via FetchXML
- Five existing-but-deprecated `matterMainCards.ts` chart configs found; Matter KPI Scorecard + Matter Financial Metrics Scorecard chart defs already in production proving the pattern
- Per-card visual diff complete: ~10-12h of Visual Host renderer extensions + 1 new `CardChrome` wrapper + 5 chart-definition records

### Phase 1 — Visual Host extensions + shared components (parallel)

**Phase 1 entry gate (binding)**: every task in this phase MUST:

1. **Invoke `/fluent-v9-component`** at task start (per §6.0 binding) — every Phase 1 task authors or modifies a Fluent v9 component.
2. Review [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) and [docs/guides/VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) and confirm the §6.4.0 binding constraint.

Tasks that introduce new visual types, parallel chart components, or skip `/fluent-v9-component` are blocked at code review.

1. `DonutChart.tsx` — enable `fieldPivot` consumption + `donutLayout: "matrixRight"` + `colorThresholds` segment coloring + center value/letter via mean-of-fields. **Verify all existing chart definitions using `Donut` still render unchanged.** (~4h)
2. `MetricCard.tsx` — add `badge` slot + `descriptionColor` prop. **Verify existing MetricCard chart defs (Matter KPI Scorecard, Matter Financial Metrics Scorecard, deprecated Guidelines/Budget/Outcomes cards) still render unchanged.** (~1.5h)
3. `HorizontalStackedBar.tsx` — add `layoutMode: "headlineAboveBar"` option to render headline + sub-line above bar. **Verify existing HorizontalStackedBar chart defs still render in default layout.** (~2h)
4. `CardChrome.tsx` (new internal file inside Visual Host) — wrapper with title + corner-icon slots; `VisualHostRoot.tsx` to wrap each rendered card. NOT a shared library component — internal to Visual Host. (~3h)
5. `TagFilter.tsx` in `@spaarke/ui-components` — generic multi-select chip filter (first consumer: document type). Fluent v9 `Menu` + `MenuItemCheckbox`. (~3h)
6. `DocumentRowMenu.tsx` in `@spaarke/ui-components` — Fluent v9 `Menu` with consolidated row actions, stop-propagation trigger. (~3h)

**Documentation update (binding companion task)**: every new Custom Options key added by tasks 1-3 (`donutLayout`, `donutCenterMode`, `donutCenterLabel`, `showBreakdownRows`, `breakdownValueFormat`, `badge`, `descriptionColor`, `layoutMode`, `headlineFromField`, `subLineTemplate`) MUST be added to the per-visual-type table in [VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) AND to authoring examples in [VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md), in the same PR as the code change.

### Phase 2 — Chart definitions + form configuration (depends on Phase 1)

**Phase 2 entry gate (binding)**: chart-definition authoring must follow [VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md). The Matter KPI Scorecard and Matter Financial Metrics Scorecard chart defs (already in production) are the canonical reference.

1. Seed five `sprk_chartdefinition` records: Matter Health Composite, Matter Budget, Matter Tasks, Matter Next Date, Matter Activity. Each with `sprk_optionsjson` Custom Options per §6.4 mapping table. **Drill Through Settings (per §6.4.7) configured on each record.**
2. Update Matter main-form solution XML for 2-column 66/34 Overview layout: Matter Information + Documents in left section, five Visual Host instances stacked in right section.
3. Wire each Visual Host's `chartDefinition` lookup to the corresponding seeded record.

### Phase 3 — Documents PCF (depends on Phase 1 shared components)

**Phase 3 entry gate (binding)**: every task in this phase MUST invoke `/fluent-v9-component` at task start (per §6.0 binding). The Documents PCF is Fluent v9 throughout; every sub-task here authors or modifies Fluent v9 components.

Five sub-tasks aligning with §6.3.1–§6.3.5. Each independently testable and mergeable. Recommended order:

1. §6.3.1 three-dot menu (consolidates all 8 actions)
2. §6.3.5 move filters to command bar (preserves AssociatedOnly auto-search)
3. §6.3.3 list/card view toggle (introduces list-layout + selection state)
4. §6.3.4 Tags filter (requires §14 Q1 resolution + BFF projection update for `tags`)
5. §6.3.2 click-to-Dialog resize (re-style existing FilePreviewDialog to match prototype 960px layout)

### Phase 4 — BFF additions

1. `/api/ai/search` projection: add `modifiedAt: string`, `modifiedBy: string` to `SearchResult`. (Per §14 Q1 resolution, `tags` is NOT added — the Tags filter uses the existing `documentType` field sourced from `sprk_documenttype`.)
2. **New endpoint** `POST /api/documents/bulk-download`: accepts JSON `{ documentIds: string[] }`; streams a zip via existing Graph / SPE file access. See §9.1 and §12.2 placement justification. Authorization: same policy as `GET /api/documents/{id}/preview-url`. Max 500 documents per request (returns 413 above that).

### Phase 5 — Telemetry + integration test

1. **Wire Application Insights** (per §14 Q5 resolution): direct browser-side connection via `@microsoft/applicationinsights-web` (and optionally `@microsoft/applicationinsights-react-js` for component-level tracking). Instrumentation key distributed to each PCF via the existing manifest-property env-var pattern (same mechanism used for `apiBaseUrl`, `tenantId`, `clientAppId`, `bffAppId`). No BFF round-trip per event — the SDK batches and posts directly to App Insights. Event names per §6.3.5 ("`view_toggled`", "`tag_filter_applied`", "`three_dot_menu_action_invoked`", "`preview_dialog_opened`", "`bulk_action_invoked`") and Visual Host equivalents ("`card_rendered`", "`card_expanded`").
2. End-to-end smoke test on a dev environment: load matters with full data, partial data, brand-new (empty) state — verify each card degrades gracefully.
3. Performance test: form cold-load p95 < 2.0s.
4. Verify dark mode via `ThemeToggle` end-to-end.
5. **Viewport responsiveness test** (per §14 Q11): verify Performance side pane compresses gracefully at 1024 px minimum width without overflow or stacking.

### Phase 6 — Documentation & promotion

1. Add `.claude/patterns/matter-overview/` pointer files (Documents PCF + Visual Host extensions).
2. Update [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) to document the new `donutLayout`, `badge`, `descriptionColor`, `CardChrome` features.
3. Capture before/after screenshots into [projects/spaarke-matter-ui-enhancement-r1/screenshots/](projects/spaarke-matter-ui-enhancement-r1/screenshots/).

---

## 11. Dark Mode & Accessibility

### 11.1 Dark mode (binding)

Per ADR-021, ADR-022, and the Fluent v9 patterns merged in PR #299:

- **All colors via Fluent v9 semantic tokens** (`tokens.colorBrandBackground`, `tokens.colorPaletteGreenBackground2`, etc.) — **no hardcoded hex / rgb anywhere**.
- **`colorThresholds` in Custom Options** use named token sets (`brand` / `warning` / `danger` / `success` / `neutral`) → resolved by Visual Host's existing `getTokenSetColors()` in `MetricCardMatrix.tsx` (lines 138–177). Token sets auto-adapt to dark mode via `FluentProvider` context.
- All icons from `@fluentui/react-icons` (auto-theme).
- **PCF Modern Theming API** — both PCFs already detect theme via `context.fluentDesignLanguage` runtime resolution (Visual Host: `providers/ThemeProvider.ts`; SemanticSearchControl: `services/ThemeService.ts`). Reference: [.claude/patterns/pcf/fluent-v9-modern-theming.md](.claude/patterns/pcf/fluent-v9-modern-theming.md).
- Test every chart definition + PCF in both light and dark; capture both screenshots before sign-off.

Relevant Fluent v9 patterns from PR #299:
- [.claude/patterns/ui/fluent-v9-component-authoring.md](.claude/patterns/ui/fluent-v9-component-authoring.md)
- [.claude/patterns/ui/fluent-v9-portal-gotcha.md](.claude/patterns/ui/fluent-v9-portal-gotcha.md) — critical for Menu / Dialog inside MDA
- [.claude/patterns/ui/fluent-v9-react-version-boundaries.md](.claude/patterns/ui/fluent-v9-react-version-boundaries.md)
- [.claude/patterns/ui/fluent-v9-theming.md](.claude/patterns/ui/fluent-v9-theming.md)

### 11.2 Accessibility (binding)

- All interactive elements (buttons, menu items, filter dropdowns, view-toggle buttons, three-dot menus, corner icons) have `aria-label` and are keyboard-reachable.
- Document rows: full row is the activation target for the preview Dialog; three-dot menu is a separate focusable element with `stopPropagation`.
- Tag filter: `MenuItemCheckbox` is the native a11y primitive (Fluent v9 handles ARIA correctly).
- KPI cards: cards are `role="button"` when click activates the drill-down; `aria-label` summarizes the metric (e.g., "Matter Health: B plus, composite of 3 areas, click to expand").
- Color contrast: every text / icon / control must pass WCAG 2.1 AA against its background in both themes. Audit using axe DevTools.
- Reduce motion: respect `prefers-reduced-motion` — disable card hover transitions.

### 11.3 Viewport responsiveness (binding — per §14 Q11)

- **Hard minimum**: 1024 px desktop viewport. No mobile / tablet support.
- **Performance side pane (right rail, 34%)** must remain a single vertical column down to the minimum viewport — no stacking, no horizontal overflow.
- Within each card, prototype layout treatments compress as follows:
  - **Matter Health (donut + breakdown)**: donut shrinks proportionally (min 100 px); breakdown rows stay readable (min font 12 px); label small-caps may abbreviate if needed
  - **Budget**: bar always spans card width; headline + sub-line never wrap awkwardly
  - **MetricCard cards (Tasks, Activity)**: value font scales down within tokens.fontSizeBase-* range; sub-line truncates with ellipsis if needed
- The Documents PCF (left column, 66%) inherits standard PCF responsive behavior; the list-view column set may collapse "Modified by" → avatar-only at narrow widths.

---

## 12. Placement Justification (BFF additions)

Per [CLAUDE.md §10](CLAUDE.md) + [.claude/constraints/bff-extensions.md](.claude/constraints/bff-extensions.md), every BFF addition requires explicit placement justification.

### 12.1 `/api/ai/search` projection additions (existing endpoint)

Two fields added to the search result projection: `modifiedAt`, `modifiedBy`. (`tags` removed from this list in Rev 3 — see §6.3.4.)

- **Not a new endpoint** — additive projection within an existing endpoint
- **Publish-size impact**: zero — no new NuGet packages
- **CVE check**: N/A
- **No CRUD → AI direct dependency introduced** — these are metadata fields, not AI-internal types

### 12.2 `POST /api/documents/bulk-download` (new endpoint)

**Placement**: BFF (`Sprk.Bff.Api/Api/DocumentsBulkEndpoints.cs` or extend existing Documents endpoints class).

**Rationale**: bulk-download requires server-side aggregation across multiple SPE files (via Graph API) into a single zip stream. Three reasons this belongs in BFF, not client-side:

1. **Authorization** — each document needs an authorization check before zipping; centralized in BFF policy authorization filter
2. **Stream efficiency** — Graph file downloads → zip stream → HTTP response can flow without buffering entire files in memory; client-side JSZip would buffer all files in browser memory (limits realistic to ~50 files)
3. **Bandwidth** — single server-stream zip is more efficient than N×file-download → client-zip → user-download

**Publish-size impact**: zero — uses existing `System.IO.Compression.ZipArchive` (BCL); no new NuGet packages.

**CVE check**: N/A — uses BCL primitive types only.

**No CRUD → AI direct dependency introduced** — endpoint composes existing document storage services (Graph / SPE access); no AI internals.

**Authorization**: same policy as `GET /api/documents/{id}/preview-url` (document read).

**Request contract**:
```json
POST /api/documents/bulk-download
{ "documentIds": ["{guid}", "{guid}", ...] }
```

**Response**: HTTP 200 with `Content-Type: application/zip`, `Content-Disposition: attachment; filename="documents-{matterId-or-bulk}-{timestamp}.zip"`. On any individual document failure: include a `_FAILED.txt` manifest in the zip listing failed documents and reasons; continue zipping the rest. On total failure (e.g., zero documents accessible): HTTP 4xx with problem details.

**Out-of-scope for v1**: streaming chunked uploads/downloads, signed pre-flight URLs, async job queue for very large bulk requests (>500 documents — return 413 Payload Too Large).

---

## 13. ADR Compliance

| ADR | Topic | Compliance |
|---|---|---|
| ADR-006 | Code page architecture | N/A — this project uses PCF, not code page. Code page explicitly rejected in §3.2. |
| ADR-013 (refined 2026-05-20) | BFF AI access via PublicContracts facade | N/A — no BFF AI endpoints added |
| ADR-021 | Fluent UI design system | Compliant — all PCFs + chart definitions use Fluent v9 tokens; PCF Modern Theming API in use |
| ADR-022 | PCF platform libraries (React 16 + Fluent v9 externalized) | Compliant — both PCFs are virtual with platform libs |
| ADR-028 | Spaarke Auth v2 | Compliant — both PCFs already use `@spaarke/auth` (`initAuth`, `authenticatedFetch`); no auth changes |
| Performance budget | Cold-load p95 < 2.0s | Targeted in §10 Phase 5 |
| Dark mode | Fluent token usage only | Binding — see §11.1 |
| PCF conventions | Per [src/client/pcf/CLAUDE.md](src/client/pcf/CLAUDE.md) | Updates to existing PCFs follow established folder structure |
| Visual Host conventions | Per [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) and [docs/guides/VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) | **Binding — see §6.4.0**. Extensions are generic (donutLayout, badge, descriptionColor); reusable for any scorecard. No new visual types; no parallel chart components; every Custom Options addition documented in same PR. |
| Fluent UI v9 authoring | Per [.claude/skills/fluent-v9-component/SKILL.md](.claude/skills/fluent-v9-component/SKILL.md) | **Binding — see §6.0**. `/fluent-v9-component` skill MUST be invoked at task start for every task that authors or modifies a Fluent v9 React component. PR description MUST cite which patterns informed the implementation. |

Confirm the ADR index ([.claude/adr/INDEX.md](.claude/adr/INDEX.md)) for any additional ADRs that constrain this work. Run `adr-check` skill at task FULL rigor.

---

## 14. Open Questions

The Rev 2 §14 list of 11 questions was resolved with Ralph on 2026-05-26. Resolutions are recorded inline below; **two follow-ups remain open** (Q8b, Q9).

### Resolved

| # | Question | Resolution (applied in design.md) |
|---|---|---|
| 1 | **Tags filter source** | Use existing `sprk_documenttype` choice field (NOT `sprk_filekeywords`). See §6.3.4. **No schema change. No `tags` projection in BFF.** UI label "Tags" maps to the document type field; multi-select across distinct option values. |
| 2 | **Modified vs Created columns** | Project BOTH `created*` AND `modified*` in `SearchResult`. Default sort = `modifiedAt DESC`. See §8.2. |
| 3 | **DocumentRowMenu action list** | Keep all 8 existing actions + Pin / Rename / Delete. **Email is retained in the per-row menu** for single-doc convenience (also appears in bulk-action bar). See §6.3.1. |
| 4 | **AiSummaryPopover fate** | Keep the hover popover on the sparkle icon (low-friction quick-glance) AND add as a 3-dot menu item (keyboard-reachable). Two entry points, same content. See §6.3.1. |
| 5 | **Telemetry destination** | Application Insights, direct browser SDK (`@microsoft/applicationinsights-web`). Instrumentation key distributed to each PCF via the existing manifest-property env-var pattern (same mechanism as `apiBaseUrl` / `tenantId` / `clientAppId` / `bffAppId`). No BFF round-trip per event. Goal: enough signal to diagnose issue → resolution where `console.log` is insufficient. See §10 Phase 5. |
| 6 | **Drawer escape hatch in Documents command bar** | Dropped from v1. Command-bar dropdowns alone are sufficient. See §6.3.5. |
| 7 | **Per-card AI sparkle in CardChrome** | Slot hidden by default (`showAiSparkle: false`). Code path exists in `CardChrome.tsx` — surfaced once Insights Engine ships in r2. See §6.4.6. |
| 8a | **Bulk-action menu actions (multi-select)** | Confirmed action list: Email selected (SPE, multiple → single email + zip), Download selected (SPE, multiple → zip), Pin selected (keeps at top), Delete selected, Document Type → selected (bulk assign choice value), Share link. See §6.3.1.b. |
| 10 | **Composite letter computation** | Simple arithmetic mean of the 3 area scores: `mean(guideline, budget, outcome) × 100`. Letter via existing `valueFormat: "letterGrade"` thresholds. See §8.1 + §6.4.1. |
| 11 | **Side pane viewport responsiveness** | Hard minimum 1024 px; right rail must compress gracefully — no stacking, no overflow. See §3.2 and the new §11.3 binding section. |

### Also resolved (final two follow-ups, 2026-05-26)

| # | Question | Resolution (applied in design.md) |
|---|---|---|
| 8b | **"Share link" bulk action** | Distinct from "Email selected" (which emails SPE files). "Share link" opens an email composer pre-populated with **clickable links to the `sprk_document` records themselves** (NOT the SPE files). Body lines render as `{Document Name} → {URL}`. No attachments. URL = the Dataverse entity-record URL. See §6.3.1.b. |
| 9 | **Card expand click action** | Reuse the existing `sprk_chartdefinition` **Drill Through Settings** — per-card chart def specifies the target entity + filter; click opens the existing drill-through web-resource dialog. Targets: Matter Health → `sprk_kpiassessment`, Budget → `sprk_invoice`, Tasks/Next Date/Activity → `sprk_event` with appropriate filters. **No new code in `ClickActionHandler.ts`.** See §6.4.7. |

**All §14 questions are resolved. Design.md is ready for `/design-to-spec`.**

---

## 15. Acceptance Criteria

### 15.1 Form-level

- [ ] Overview tab renders with 2-column 66/34 layout in both light and dark themes.
- [ ] Matter Information section appears at the top of the left column.
- [ ] Documents PCF appears below Matter Information in the left column.
- [ ] Five Visual Host instances appear stacked in the right column with all cards rendered.
- [ ] Form cold-load p95 < 2.0s on a Matter with full data (8+ docs, full scorecard, current KPIs).
- [ ] All five cards degrade gracefully when their backing data is absent (skeleton → empty state).

### 15.2 (Removed — AI Summary banner deferred to r2)

### 15.3 Documents PCF

- [ ] Three-dot menu replaces all inline row actions; menu items match §6.3.1 (consolidates all 8 existing actions).
- [ ] Click row (excluding menu) opens preview Dialog at 960px max-width with restructured 2-column body.
- [ ] Dialog shows large thumbnail, AI summary, tags, and metadata; Find similar / Close / Open file buttons are wired.
- [ ] List view shows sortable columns (Name / Modified / Modified by / Match) and multi-select checkboxes.
- [ ] Card view renders responsive tile grid; cards retain match badge overlay and three-dot menu in corner.
- [ ] View toggle persists user preference (per-matter-per-user via localStorage for v1).
- [ ] Tags filter dropdown lists alphabetically-sorted distinct tags from associated documents; OR-match.
- [ ] Active-tags chip row appears when ≥1 tag selected; each chip's × removes; "Clear all" empties selection.
- [ ] Filtered count reflected in the footer.
- [ ] All existing PCF behaviors (semantic / hybrid / keyword search, threshold, associated-only with auto-search, date range, file type, matter type) continue to work after filters move to command bar.
- [ ] `/api/ai/search` projection additions (`tags`, `modifiedAt`, `modifiedBy`) deployed.

### 15.4 Matter Performance side pane (Visual Host)

- [ ] Five `sprk_chartdefinition` records seeded; each Visual Host instance wired to its chart definition.
- [ ] Matter Health card renders donut + composite letter center + 3-area breakdown rows from `sprk_*compliancegrade_current` fields.
- [ ] Donut segments coloured by `colorThresholds` token sets (brand / warning / danger) and themed correctly in dark mode.
- [ ] Budget card shows `$50K`-style headline + `33% of $150K` sub-line + horizontal progress bar (color-thresholded).
- [ ] Tasks card shows overdue count (red Fluent `Badge` when >0) + upcoming count sub-line via FetchXML aggregation on `sprk_event`.
- [ ] Next Date card shows date + event title + days-from-now via `DueDateCard` (no renderer changes).
- [ ] Activity card shows event count + brand-color sub-line via FetchXML on `sprk_event`.
- [ ] Each card has `CardChrome` header + corner expand icon; AI sparkle slot hidden by default per §14 Q7.
- [ ] All cards render skeletons while loading; empty-state when no data.

### 15.5 Cross-cutting

- [ ] All UI uses Fluent v9 tokens — automated grep confirms zero hardcoded hex/rgb in PCF and Visual Host source.
- [ ] axe DevTools reports zero WCAG 2.1 AA violations on the Overview tab in both light and dark.
- [ ] Telemetry events fire for: PCF render time, document menu actions, view toggle, tag filter applied, KPI card expand. Destination per §14 Q5.
- [ ] `code-review` skill (FULL rigor) + `adr-check` skill pass for every task.

---

## Appendix A — Prototype iteration history

Variant H is the result of three rounds of stakeholder iteration on the prototype:

| Round | Variants explored | Key feedback that drove next round |
|---|---|---|
| 1 | A (Health-First Hero), B (Dashboard Tiles), C (Timeline-led), D (Split Workbench) | Donut visual concept (A) good but too large; tile drill-downs (B) need work; timeline (C) too space-consuming; Documents control in D best, modal needs to be larger |
| 2 | E (Refined Composite), F (Donut Tile Dashboard), G (Insight Hub) | Add AI Summary narrative (later removed from r1 scope); flip columns (Tasks left, Docs right); larger doc preview; drill-down corner on every tile |
| 3 (final) | H (Composite — user mockup) | Approved layout. Subsequent revisions: 66/34 column ratio; Documents M365 list + card toggle + Tags filter; three-dot menu; click-to-Dialog only |

Complete prototype repo: `c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\`.

---

## Appendix B — File pointer index (production targets)

| Concern | Production path (target) | Prototype reference (visual contract) |
|---|---|---|
| Matter form XML | `src/solutions/.../Matter.systemform.xml` (or equivalent in solution) | n/a — MDA config |
| Semantic Search PCF | [src/client/pcf/SemanticSearchControl/](src/client/pcf/SemanticSearchControl/) | `MatterDocuments.tsx`, `DocumentList.tsx` |
| Visual Host PCF | [src/client/pcf/VisualHost/](src/client/pcf/VisualHost/) | `VariantH_Composite.tsx` right column |
| Visual Host — `DonutChart.tsx` extensions | [src/client/pcf/VisualHost/control/components/DonutChart.tsx](src/client/pcf/VisualHost/control/components/DonutChart.tsx) | matter-overview-1.png (Matter Health card) |
| Visual Host — `MetricCard.tsx` extensions | [src/client/pcf/VisualHost/control/components/MetricCard.tsx](src/client/pcf/VisualHost/control/components/MetricCard.tsx) | matter-overview-1.png (Tasks, Activity cards) |
| Visual Host — `HorizontalStackedBar.tsx` refactor | [src/client/pcf/VisualHost/control/components/HorizontalStackedBar.tsx](src/client/pcf/VisualHost/control/components/HorizontalStackedBar.tsx) | matter-overview-1.png (Budget card) |
| Visual Host — `CardChrome.tsx` (new) | `src/client/pcf/VisualHost/control/components/CardChrome.tsx` (new) | per-card title + corner icons across all 5 |
| Visual Host — `ChartRenderer.tsx` updates | [src/client/pcf/VisualHost/control/components/ChartRenderer.tsx](src/client/pcf/VisualHost/control/components/ChartRenderer.tsx) | wire Donut fieldPivot + CardChrome |
| Shared: `TagFilter` (new) | `src/client/shared/Spaarke.UI.Components/src/components/TagFilter.tsx` (new) | (composed inline in `MatterDocuments.tsx`) |
| Shared: `DocumentRowMenu` (new) | `src/client/shared/Spaarke.UI.Components/src/components/DocumentRowMenu.tsx` (new) | `DocumentList.tsx` (`DocumentRowMenu` export) |
| Shared: `FilePreview` (existing, reuse) | `src/client/shared/Spaarke.UI.Components/src/components/FilePreview.tsx` | document-details-modal-1.png |
| 5 Dataverse chart definitions | `sprk_chartdefinition` records (seeded via script or unpacked into solution) | n/a — Dataverse data |
| BFF `/api/ai/search` projection | `src/server/api/Sprk.Bff.Api/...` (additive: `tags`, `modifiedAt`, `modifiedBy`) | n/a |

---

## Appendix C — Recon evidence trail

The decisions in this Rev 2 design are traceable to six sub-agent recon passes (Phase A + A.5) executed in this session:

1. **Semantic Search PCF audit** — found existing `@spaarke/auth` + `@spaarke/ui-components` integration; confirmed there is NO hover document preview to remove (only `AiSummaryPopover` on sparkle); confirmed `SearchResult` lacks `tags`; confirmed 8 (not 4) actions to consolidate; confirmed `console.log`-only telemetry.
2. **Visual Host + Chart Definitions audit** — confirmed 13 visual types incl. Donut, MetricCard, HorizontalStackedBar, DueDateCard; confirmed `sprk_chartdefinition` entity surface; established Path B (composer pattern, later refined to "no composer — five direct Visual Host instances").
3. **Shared packages + Matter Scorecard data** — confirmed `@spaarke/ui-components` v2.0.0 has 44+ components including `FilePreview` (obviates Rev 1's `DocumentPreviewDialog`); confirmed `@spaarke/auth` v2.0.0 surface; confirmed Insights Engine NOT built (out of scope).
4. **Visual Host architecture deep-dive** — documented 3-tier config (PCF override > Chart Def field > Custom Options > defaults); enumerated Custom Options vocabulary for Donut / MetricCard / HSBar / DueDateCard; established that `donutLayout: "matrixRight"` + reuse of `fieldPivot` + `colorThresholds` is the minimum extension for Matter Health.
5. **Chart definitions + `sprk_matter` field inventory** — confirmed 3 area grades + 3 trend variants + financial rollups all already on `sprk_matter`; confirmed composite letter is computable client-side; confirmed Tasks / Next Date / Activity need FetchXML aggregation against `sprk_event`.
6. **Per-card visual diff** — confirmed `DueDateCard` already aligned (no code change); confirmed Donut needs fieldPivot + new layout; confirmed MetricCard needs badge + descriptionColor; confirmed HSBar needs headline-above-bar layout; confirmed CardChrome wrapper needs to be added; total ~10-12h of renderer work.

The full agent transcripts are not included in this design doc to keep it readable, but are available in the session record for traceability.
