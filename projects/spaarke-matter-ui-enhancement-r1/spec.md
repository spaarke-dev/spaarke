# spaarke-matter-ui-enhancement-r1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-26
> **Source**: [design.md](./design.md) (Rev 6 — recon-validated + 11 §14 questions resolved + 3 Step-2.5 clarifications applied + fluent-v9-component skill enforcement)
> **Visual contract**: `c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\` (Variant H)
> **Recon evidence**: 6 sub-agent passes across Phase A + A.5 (see design.md Appendix C)

---

## Executive Summary

Redesign the Matter main-form Overview tab to deliver a modern Fluent v9 experience. Three deliverables, all built on **existing infrastructure** (no parallel libraries):

1. **Documents (Semantic Search PCF)** — 5 surgical changes to the existing virtual PCF: list/card view toggle, sortable columns, multi-select with bulk-action bar, 3-dot row menu consolidating 8+ actions, Tags filter sourced from existing `sprk_documenttype`, filters relocated from sidebar to command bar
2. **Matter Performance side pane** — implemented as **5 Visual Host PCF instances** + **5 `sprk_chartdefinition` records**, NOT as a new PCF. Requires generic, backward-compatible extensions to existing Visual Host renderers (`DonutChart`, `MetricCard`, `HorizontalStackedBar`) + a new internal `CardChrome` wrapper. **No new visual types. No parallel chart components.** (See §6.4.0 BINDING constraint in design.md.)
3. **Matter Information + Overview form configuration** — native MDA section reconfiguration to 2-column 66/34 layout

**Out of scope (deferred to r2)**: AI Summary Banner (Insights Engine not yet built).

---

## Scope

### In Scope

- **Update** existing PCF: [`src/client/pcf/SemanticSearchControl/`](src/client/pcf/SemanticSearchControl/) — five behavior changes (FR-DOC-01..06)
- **Extend** existing PCF: [`src/client/pcf/VisualHost/`](src/client/pcf/VisualHost/) — generic Custom Options additions + new internal `CardChrome` wrapper (FR-VH-01..05)
- **Author** 5 new `sprk_chartdefinition` records in Dataverse (FR-DV-01..05)
- **Add** 2 small components to `@spaarke/ui-components`: `TagFilter`, `DocumentRowMenu` (FR-SC-01..02)
- **Reconfigure** Matter main-form Overview tab XML (FR-FORM-01)
- **Add** 1 new BFF endpoint: `POST /api/documents/bulk-download` (FR-BFF-02)
- **Add** projection fields `modifiedAt` + `modifiedBy` to existing `/api/ai/search` endpoint (FR-BFF-01)
- **Wire** Application Insights browser SDK across both PCFs (FR-TEL-01)
- **Update** [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) and [docs/guides/VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) with all new Custom Options keys (FR-DOC-09)

### Out of Scope

- **AI Summary Banner / Insights Engine** — deferred to r2 once Insights ships
- **New visual types in Visual Host** — explicitly forbidden by §6.4.0 binding constraint
- **Parallel chart component library** (no `HealthDonut.tsx`, `KpiCard.tsx`, etc.) — explicitly forbidden
- **`MatterPerformancePane` container PCF** — explicitly forbidden; form section handles vertical stacking
- **Schema additions to `sprk_matter`** — all rollups already exist (per Phase A.5 recon)
- **New BFF endpoints for Performance pane** — Visual Host uses Dataverse WebAPI/FetchXML
- **Other Matter tabs** (Calendar, Contacts, Email, Billing, Report Card) — unchanged
- **Mobile/responsive below 1024 px** — desktop MDA only
- **Real-time push** — cards re-fetch on form load + manual refresh only
- **Richer per-card Fluent `Dialog` drill-down** — deferred; v1 reuses existing `OpenDatasetGrid` drill-through

### Affected Areas

| Path | Type of change |
|---|---|
| [`src/client/pcf/SemanticSearchControl/`](src/client/pcf/SemanticSearchControl/) | Modify: `SemanticSearchControl.tsx`, `components/ResultCard.tsx`, `components/FilterPanel.tsx`, `components/FilePreviewDialog.tsx`, `components/ResultsList.tsx`, `services/SemanticSearchApiService.ts`, `types/search.ts`. Add: list-view component, selection state hooks, bulk-action bar, sort state hooks |
| [`src/client/pcf/VisualHost/control/components/`](src/client/pcf/VisualHost/control/components/) | Modify: `DonutChart.tsx`, `MetricCard.tsx`, `HorizontalStackedBar.tsx`, `ChartRenderer.tsx`, `VisualHostRoot.tsx`. Add: `CardChrome.tsx` (internal — NOT a shared library export) |
| `src/client/shared/Spaarke.UI.Components/src/components/` | Add: `TagFilter.tsx`, `DocumentRowMenu.tsx`. Modify: barrel `index.ts` to export both |
| `src/server/api/Sprk.Bff.Api/` | Modify: `Api/AiSearchEndpoints.cs` (or equivalent) to add `modifiedAt`/`modifiedBy` to projection. Add: `Api/DocumentsBulkEndpoints.cs` (or extend existing) for `POST /api/documents/bulk-download` |
| Dataverse | Add: 5 `sprk_chartdefinition` records (Matter Health, Matter Budget, Matter Tasks, Matter Next Date, Matter Activity). Modify: Matter main-form solution XML (section layout, Visual Host placements) |
| [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) | Modify: document new Custom Options keys (`donutLayout`, `donutCenterMode`, `donutCenterLabel`, `showBreakdownRows`, `breakdownValueFormat`, `badge`, `descriptionColor`, `layoutMode`, `headlineFromField`, `subLineTemplate`) |
| [docs/guides/VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) | Modify: add chart-def authoring examples for the 5 new card patterns |

---

## Requirements

### Functional Requirements

#### Documents PCF (FR-DOC-*)

**FR-DOC-01** — Three-dot row menu consolidating 8+ actions
- Replace per-row inline action icons (Preview / AI Summary / Open File / Find Similar) and dialog-toolbar icons (Open Record / Email / Copy Link / Workspace) with a single `MoreVertical20Regular` button per row
- Menu opens Fluent v9 `Menu`; trigger uses `appearance="subtle"`, `size="small"`, and **must `stopPropagation`** so the row's click handler does not fire
- Menu order: Preview · AI summary · Open file · Find similar · *divider* · Download · Copy link · Email (single-doc convenience) · Open record · *divider* · Toggle workspace · Pin to top · Rename · Delete
- `AiSummaryPopover` on the sparkle icon is RETAINED (hover quick-glance) AND added to the menu (keyboard access)
- **Acceptance**: every action previously reachable via inline card icon or `FilePreviewDialog` toolbar is reachable via the 3-dot menu; no orphaned UI affordances

**FR-DOC-02** — Bulk-action bar (visible when ≥1 row checked in list view)
- Sticky bar above column-header row showing: count badge ("N selected"), Clear affordance, and 6 actions: Email selected, Download selected, Pin selected, Delete selected, Document Type → selected (sub-menu of `sprk_documenttype` option values), Share link
- "Email selected" = SPE files as attachments; multi-doc = single email with zip attachment (reuses existing email pipeline extended for zip case)
- "Download selected" = **calls new BFF endpoint `POST /api/documents/bulk-download`** (FR-BFF-02); multi-doc = single zip response
- "Pin selected" = sets `pinned` flag in localStorage (per-user-per-matter); no Dataverse write; pinned rows sort to top regardless of active column sort; pin column shows small icon **only on pinned rows**
- "Delete selected" = soft-delete via Xrm.WebApi; requires confirmation `Dialog`
- "Document Type → selected" = bulk Xrm.WebApi update with **optimistic UI**: apply immediately, show toast with 5s Undo affordance; on failure, toast becomes error with Retry
- "Share link" = open email composer pre-populated with body `{DocName} → {DataverseRecordURL}` per selected doc (NOT SPE files); URL format `https://{env}.crm.dynamics.com/main.aspx?etn=sprk_document&id={guid}&pagetype=entityrecord`
- **Acceptance**: bar appears at ≥1 selection, hides at 0; each action operates on the selected set; failure modes degrade gracefully (toast + Retry)

**FR-DOC-03** — Click-to-Dialog preview restructured
- Row click (excluding 3-dot menu trigger) opens existing `FilePreviewDialog` resized to **960 px max-width** (currently 85vw — verify and adjust)
- Body restructured to 2-column grid: left ~640 px thumbnail area; right 320 px metadata pane with sections (top→bottom): AI summary (sparkle icon + paragraph), Tags (single chip from `sprk_documenttype` value), Details (Created by / Created / Size / Type)
- Footer actions left-to-right: Find similar (subtle) · Close (secondary) · Open file (primary)
- **Acceptance**: Dialog renders at 960 px on viewports ≥1024 px; metadata pane sections render in specified order; existing iframe preview load behavior unchanged

**FR-DOC-04** — List/card view toggle with sortable columns + multi-select
- New command-bar control: 2-button toggle group (right end of bar) — `AppsList20Regular` (list) / `Grid20Regular` (card)
- Active = `appearance="primary"`, inactive = `appearance="subtle"`
- **List view** (default): columns Selection checkbox · Pin (icon visible only on pinned rows; click toggles) · Name (file icon + link-styled) · Modified · Modified by (avatar + name) · Match · Three-dot menu. Headers sortable (toggle asc/desc with `ArrowSortUp/Down` indicator)
- **Default sort**: `modifiedAt DESC`. Pinned rows always sort to top regardless of active column sort
- **Card view**: existing `ResultCard` rendering with 3-dot menu replacing inline icons
- View preference persists in localStorage keyed by `(userId, matterId)` for v1
- **Acceptance**: toggling view re-renders without reloading data; sort persists within session; selection state persists across view toggles; pin state survives reload

**FR-DOC-05** — Tags filter sourced from `sprk_documenttype`
- Command-bar `Menu` with `MenuItemCheckbox` entries. Trigger label: "Tags" or "Tags (N)" where N = selected count
- Options source: `sprk_documenttype` choice values via existing `DataverseMetadataService.fetchOptionSet('sprk_documenttype')`. Sort alphabetically
- Selection semantics: OR — document matches if its `documentType` ∈ selected set
- Active-filters chip row renders below command bar when ≥1 selected: `Filtered by: [Type1 ×] [Type2 ×] … Clear all`
- Footer count reflects filtered results: e.g., "Showing 5 of 31 documents · filtered by 2 tags"
- The existing FilterPanel "Document Type" dropdown is RELOCATED to this command-bar Tags filter (not duplicated). Underlying field is unchanged
- Implemented via new `TagFilter` shared component (FR-SC-01)
- **Acceptance**: filter dropdown populates from `sprk_documenttype` option set; multi-select OR behavior verified; chip × removes individual tag; "Clear all" empties selection; footer count is correct

**FR-DOC-06** — Filters relocated from sidebar rail to command-bar dropdowns
- Remove the always-open vertical `FilterPanel` sidebar
- Move existing filters into command bar as compact dropdown buttons: Associated Only · File Type · Date Range · Threshold · Mode (Tags is FR-DOC-05)
- **AssociatedOnly auto-search behavior** ([SemanticSearchControl.tsx](src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx) lines 359-375) MUST be preserved verbatim when the toggle moves to the command bar
- No "Filter Drawer" escape hatch in v1 (per §14 Q6 resolution)
- **Acceptance**: all 5 filters function identically to today; AssociatedOnly toggle still auto-triggers re-search; sidebar `FilterPanel.tsx` is removed (or hidden when bound to the new layout)

**FR-DOC-07** — Telemetry events emitted (Application Insights)
- Replace `console.log` calls in Semantic Search PCF with App Insights events. Event names (minimum): `view_toggled` (value), `tag_filter_applied` (tag count), `three_dot_menu_action_invoked` (action name), `preview_dialog_opened`, `bulk_action_invoked` (action name + selection count)
- **Acceptance**: events visible in App Insights Live Metrics or query in <60s after firing; no `console.log` of telemetry events remains in production code path

**FR-DOC-09** — Documentation updates (companion task to FR-VH-*)
- Every new Custom Options key from FR-VH-01..03 documented in [docs/architecture/VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) per-visual-type table AND with authoring examples in [docs/guides/VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md), in the same PR as the code change
- **Acceptance**: docs PR reviewer can find each new key documented; chart-definition author can author a chart using only the docs (without reading source)

#### Visual Host renderer extensions (FR-VH-*)

> **Binding constraint**: every FR-VH-* must be implemented as a **minimal generic addition** to the named visual type. NO new visual types. NO Matter-specific code paths. Every change MUST verify existing in-production chart definitions still render unchanged. See design.md §6.4.0.

**FR-VH-01** — `DonutChart` enhancement: `fieldPivot` consumption + `matrixRight` layout + `colorThresholds` segment coloring + center value via `meanOfFields`
- New `sprk_optionsjson` keys (donut-specific): `donutLayout: "standard" | "matrixRight"` (default `standard`), `donutCenterMode: "total" | "meanOfFields"`, `donutCenterLabel: string`, `showBreakdownRows: boolean`, `breakdownValueFormat: "score" | "scoreOver100" | "percentage" | "ratio"`
- Reused keys: `fieldPivot.fields[]`, `colorThresholds[]`, `valueFormat`
- Enable field-pivot consumption in `ChartRenderer.tsx` for `Donut` case (today: aggregation only)
- When `donutLayout: "matrixRight"`: render donut on left + breakdown rows on right in a grid; each row = field label (uppercase small caps) + formatted value
- When `donutCenterMode: "meanOfFields"`: compute `mean(fieldPivot.fields[].value)` and render in donut center (letter via `valueFormat: "letterGrade"` thresholds if applicable, otherwise raw value)
- Segment colors derived from `colorThresholds[].tokenSet` (reuse `getTokenSetColors()` from `MetricCardMatrix.tsx`)
- **Backward compat**: chart defs with `donutLayout` absent (or `"standard"`) and no `fieldPivot` MUST render exactly as today
- **Acceptance**: Matter Health chart def renders donut + 3 labeled rows + center "B+" / "85/100"; existing Donut chart defs render unchanged

**FR-VH-02** — `MetricCard` `badge` slot
- New `sprk_optionsjson` key: `badge: { text: string, tone: "danger" | "warning" | "success" | "neutral", position: "inline" }` (per-field via `fieldPivot.fields[].badge` for field-pivot mode)
- When `badge` present: render Fluent v9 `Badge` next to the value in a flex row
- **Backward compat**: chart defs without `badge` render unchanged
- **Acceptance**: Matter Tasks chart def renders "4 [overdue]" with red Fluent `Badge` when `overdue > 0`; chart defs without `badge` are unaffected

**FR-VH-03** — `MetricCard` `descriptionColor` prop
- New `sprk_optionsjson` key: `descriptionColor: "brand" | "neutral" | "success" | "warning" | "danger"` (default `"neutral"`)
- Description Text element receives the matching `tokens.color*Foreground*` value
- **Backward compat**: chart defs without `descriptionColor` render with existing `colorNeutralForeground3`
- **Acceptance**: Matter Activity chart def renders sub-line "events in last 7 days" in brand color; existing MetricCard chart defs unchanged

**FR-VH-04** — `HorizontalStackedBar` `headlineAboveBar` layout
- New `sprk_optionsjson` keys: `layoutMode: "default" | "headlineAboveBar"` (default `default`), `headlineFromField: string`, `subLineTemplate: string` (template supports `{remaining}`, `{percent}`, `{total}` placeholders)
- When `layoutMode: "headlineAboveBar"`: render headline (large) + sub-line (small) ABOVE the bar; suppress the existing top-right total label and bottom-right remaining label
- **Backward compat**: chart defs without `layoutMode` render in default (current) layout
- **Acceptance**: Matter Budget chart def renders `$50K` + `33% of $150K` headline above a color-thresholded progress bar; existing HSBar chart defs unchanged

**FR-VH-05** — `CardChrome` internal wrapper component
- New file: `src/client/pcf/VisualHost/control/components/CardChrome.tsx` (internal to Visual Host — NOT exported from `@spaarke/ui-components`)
- Props: `{ title?: string, onExpand?: () => void, onAiSummary?: () => Promise<ISummaryData>, showAiSparkle?: boolean (default false), children: ReactNode }`
- Renders per-card title bar + corner-icon slots; wraps each card component in `VisualHostRoot.tsx`
- `onExpand` wires to existing `handleExpandClick` (honors chart-def Drill Through Settings) — no new `ClickActionHandler` code
- `showAiSparkle: false` by default in v1 (Insights Engine deferred); slot exists in contract for r2
- **Acceptance**: each of the 5 cards renders with title + expand icon; clicking expand triggers the chart-def's drill-through to the configured entity list view

#### Chart definitions (FR-DV-*)

**FR-DV-01** — `Matter Health Composite` chart def
- Visual Type: `Donut` (100000004)
- Entity: `sprk_matter`
- `sprk_optionsjson`:
  ```json
  {
    "fieldPivot": {
      "fields": [
        { "field": "sprk_guidelinecompliancegrade_current", "label": "Guidelines Compliance" },
        { "field": "sprk_budgetcompliancegrade_current", "label": "Budget Controls" },
        { "field": "sprk_outcomecompliancegrade_current", "label": "Outcomes Success" }
      ]
    },
    "valueFormat": "letterGrade",
    "colorThresholds": [
      { "range": [0.85, 1.00], "tokenSet": "brand" },
      { "range": [0.70, 0.84], "tokenSet": "warning" },
      { "range": [0.00, 0.69], "tokenSet": "danger" }
    ],
    "donutLayout": "matrixRight",
    "donutCenterMode": "meanOfFields",
    "showBreakdownRows": true,
    "breakdownValueFormat": "scoreOver100"
  }
  ```
- Drill Through: `sprk_kpiassessment` filtered by `_sprk_matter_value`
- **Acceptance**: chart renders donut + 3 labeled rows + composite letter center + sub-value `XX/100`; expand opens KPI assessment list filtered to current matter

**FR-DV-02** — `Matter Budget` chart def
- Visual Type: `HorizontalStackedBar` (100000012)
- Entity: `sprk_matter`
- Data: `sprk_totalspendtodate` (current) + `sprk_totalbudget` (total)
- `sprk_optionsjson`:
  ```json
  {
    "valueFormat": "currency",
    "layoutMode": "headlineAboveBar",
    "headlineFromField": "sprk_totalspendtodate",
    "subLineTemplate": "{percent}% of {total}",
    "colorThresholds": [
      { "range": [0.0, 0.6], "tokenSet": "success" },
      { "range": [0.6, 0.85], "tokenSet": "warning" },
      { "range": [0.85, 1.0], "tokenSet": "danger" }
    ]
  }
  ```
- Drill Through: `sprk_invoice` filtered by `_sprk_matter_value`
- **Acceptance**: chart renders `$50K` headline + `33% of $150K` sub-line + colored progress bar; expand opens invoice list filtered to current matter

**FR-DV-03** — `Matter Tasks` chart def
- Visual Type: `MetricCard` (100000000)
- Entity: `sprk_event`
- Data via **FetchXML aggregation**: COUNT overdue (`regardingmatter = @currentMatter AND finalduedate < today AND eventstatus ≠ Completed`) + COUNT upcoming (`regardingmatter = @currentMatter AND finalduedate BETWEEN today AND today+30 AND eventstatus ≠ Completed`)
- `sprk_optionsjson`:
  ```json
  {
    "fieldPivot": {
      "fields": [
        { "field": "overdue", "label": "Overdue", "badge": { "text": "overdue", "tone": "danger", "position": "inline" } }
      ]
    },
    "cardDescription": "{upcoming} upcoming this month"
  }
  ```
- Drill Through: `sprk_event` filtered to overdue + upcoming for current matter
- **Acceptance**: chart renders `4 [overdue]` + sub-line `2 upcoming this month`; overdue badge appears only when count > 0; expand opens task list

**FR-DV-04** — `Matter Next Date` chart def
- Visual Type: `DueDateCard` (100000008) — no renderer changes
- Entity: `sprk_event`
- Data via FetchXML: TOP 1 ORDER BY `finalduedate ASC` WHERE `regardingmatter = @currentMatter AND finalduedate ≥ today`
- Drill Through: `sprk_event` filtered to upcoming for current matter
- **Acceptance**: chart renders date + event title + days-from-now (e.g., `May 28 / Pre-trial hearing · in 3d`); expand opens upcoming events list

**FR-DV-05** — `Matter Activity` chart def
- Visual Type: `MetricCard` (100000000)
- Entity: `sprk_event`
- Data via FetchXML: COUNT where `regardingmatter = @currentMatter AND (actualstart OR completeddate) ≥ today-7`
- `sprk_optionsjson`:
  ```json
  {
    "descriptionColor": "brand",
    "cardDescription": "events in last 7 days"
  }
  ```
- Drill Through: `sprk_event` filtered to last-7-days for current matter
- **Acceptance**: chart renders `5` + brand-colored sub-line `events in last 7 days`; expand opens activity list

#### Shared components (FR-SC-*)

**FR-SC-01** — `TagFilter` in `@spaarke/ui-components`
- File: `src/client/shared/Spaarke.UI.Components/src/components/TagFilter.tsx`
- Generic multi-select chip filter (NOT bound to document type — usable for any choice field)
- Props: `{ options: TagFilterOption[], selected: string[], onChange: (selected: string[]) => void, label?: string, sortAlphabetical?: boolean }`
- Renders Fluent v9 `Menu` + `MenuItemCheckbox` entries; trigger button shows label + count badge
- Below-bar active-tags chip row when ≥1 selected; chip × removes single, "Clear all" empties
- Exported from barrel `src/index.ts`
- **Acceptance**: component renders independently in Storybook-style harness; passes a11y check (Fluent v9 handles ARIA); first consumer is the Semantic Search PCF Tags filter

**FR-SC-02** — `DocumentRowMenu` in `@spaarke/ui-components`
- File: `src/client/shared/Spaarke.UI.Components/src/components/DocumentRowMenu.tsx`
- Fluent v9 `Menu` with the 13-action structure from FR-DOC-01
- Trigger button must `stopPropagation` (so row click handler doesn't fire)
- Props: `{ document: IDocumentRowMenuTarget, onAction: (action: DocumentRowAction) => void, disabledActions?: DocumentRowAction[] }`
- `disabledActions` allows per-document permission scoping (e.g., disable Delete if user lacks delete permission)
- Exported from barrel
- **Acceptance**: menu renders all 13 actions; trigger `stopPropagation` verified by integration test (row click does not fire when menu trigger is clicked); first consumer is the Semantic Search PCF

#### Form configuration + telemetry + BFF (FR-FORM-*, FR-TEL-*, FR-BFF-*)

**FR-FORM-01** — Matter form Overview tab 2-column 66/34 layout
- Update Matter main-form solution XML
- Left column (66%): Matter Information section (native MDA, 2 sub-columns: Matter Number / Matter Name / Matter Type / Practice Area / Matter Description), then Documents section bound to Semantic Search PCF
- Right column (34%): Matter Performance section containing 5 stacked Visual Host instances (each wired to its `sprk_chartdefinition` lookup)
- Header section unchanged (no AI Summary banner in v1)
- Other tabs unchanged
- **Acceptance**: form renders in 2-column layout at viewport ≥1024 px; pinning a Visual Host instance to a section binds correctly via its chart def lookup

**FR-TEL-01** — Application Insights wiring (both PCFs)
- Use `@microsoft/applicationinsights-web` (and optionally `@microsoft/applicationinsights-react-js`) — direct browser SDK connection, NOT BFF-proxied
- Instrumentation key distributed via the existing PCF manifest-property env-var pattern (same mechanism as `apiBaseUrl`, `tenantId`, `clientAppId`, `bffAppId`)
- Events for Semantic Search PCF per FR-DOC-07
- Events for Visual Host: `card_rendered` (per Visual Host instance, with chart-def name), `card_expanded` (with chart-def name)
- **Acceptance**: events visible in App Insights within 60s; no PII in event payloads; instrumentation key sourced from manifest property (no hardcoding)

**FR-BFF-01** — `/api/ai/search` projection additions
- Add `modifiedAt: string` (ISO datetime) and `modifiedBy: string` (display name) fields to the `SearchResult` shape and the projection
- Source: standard system fields `modifiedon` and `modifiedby` on `sprk_document`
- Existing fields and behavior unchanged
- **Acceptance**: projection returns `modifiedAt` + `modifiedBy` for every result; existing fields unchanged; existing clients (this PCF and any others) unaffected

**FR-BFF-02** — `POST /api/documents/bulk-download` (new endpoint)
- File: `src/server/api/Sprk.Bff.Api/Api/DocumentsBulkEndpoints.cs` (or extend existing Documents endpoints class)
- Request: `POST` with JSON `{ documentIds: string[] }`. Max 500 ids per request (returns HTTP 413 above that)
- Response: HTTP 200 with `Content-Type: application/zip`, `Content-Disposition: attachment; filename="documents-{matterIdOrBulk}-{timestamp}.zip"`
- Uses `System.IO.Compression.ZipArchive` (BCL, no new NuGet); streams Graph file downloads → zip → HTTP response without buffering entire files in memory
- Authorization: same policy as `GET /api/documents/{id}/preview-url`; uses endpoint filter per ADR-008
- Per-document failure: include `_FAILED.txt` manifest in zip listing failed documents and reasons; continue zipping the rest
- Total failure (zero documents accessible): HTTP 4xx with problem details
- **Acceptance**: endpoint authenticates via existing scheme; returns zip stream for valid request; per-document failures degrade gracefully (manifest in zip); 413 returned for >500 ids

### Non-Functional Requirements

- **NFR-01** — Cold-load p95 < 2.0 s for Matter Overview form with full data (8+ docs, full scorecard, current KPIs)
- **NFR-02** — All UI passes axe DevTools WCAG 2.1 AA in both light and dark themes; zero violations on Overview tab
- **NFR-03** — Viewport responsiveness: hard minimum 1024 px desktop; right rail compresses gracefully — no horizontal overflow, no row stacking, no readable-text degradation below 12px
- **NFR-04** — Fluent v9 tokens only — automated grep confirms zero hardcoded hex/rgb in PCF and Visual Host source touched by this project
- **NFR-05** — **Visual Host backward compatibility (binding)**: every existing in-production chart definition (Matter KPI Scorecard, Matter Financial Metrics Scorecard, any deprecated `matterMainCards.ts` configs still in use, all other existing `sprk_chartdefinition` records) renders unchanged after FR-VH-01..05 extensions land. Verify before merging each Visual Host PR
- **NFR-06** — BFF additions follow ADR-029 publish hygiene: no new NuGet packages; no transitive CVE introductions; publish-size impact verified (baseline ~60 MB per `.claude/constraints/azure-deployment.md`)
- **NFR-07** — Auth: both PCFs use `@spaarke/auth` v2 unchanged (already wired); shared MSAL `localStorage` cache (INV-1 SSO) preserved
- **NFR-08** — `prefers-reduced-motion` respected: card hover transitions disabled when set

---

## Technical Constraints

### Applicable ADRs

- **ADR-006** — UI Surface Architecture: this project uses PCF (form binding context); compliant
- **ADR-007** — `SpeFileStore` facade: FR-BFF-02 bulk-download MUST access SPE files via the facade only; no Graph SDK types leaked above
- **ADR-008** — Endpoint filters for auth: FR-BFF-02 MUST use endpoint filter (not global middleware) for auth/authorization
- **ADR-010** — DI minimalism: FR-BFF-02 implementation MUST NOT push DI registrations above the ≤15 cap
- **ADR-012** — Shared component library: FR-SC-01, FR-SC-02 are added to `@spaarke/ui-components`; abstracted services via `IDataService` where applicable
- **ADR-019** — SPE access: applies to FR-BFF-02 bulk-download Graph access
- **ADR-021** — Fluent UI v9 Design System: ALL UI (PCFs, Visual Host extensions, shared components) MUST use Fluent v9 tokens; dark mode required (NFR-04)
- **ADR-022** — PCF Platform Libraries: PCFs use React 16/17 platform-provided; FR-DOC-* and FR-VH-* changes MUST NOT pull in React 18+ dependencies
- **ADR-028** — Spaarke Auth Architecture (v2): both PCFs already use `@spaarke/auth`; FR-BFF-02 MUST follow function-based contract + managed identity pattern
- **ADR-029** — BFF Publish Hygiene: applies to FR-BFF-02 (NFR-06)

### MUST Rules (extracted)

- ✅ **MUST invoke `/fluent-v9-component` at task start** for every task that authors or modifies a Fluent v9 React component (binding constraint, design.md §6.0). Applies to: FR-DOC-01..06, FR-VH-01..05, FR-SC-01..02. Skipping this is a code-review block.
- ✅ MUST cite which Fluent v9 patterns informed the implementation in the PR description (e.g., `fluent-v9-portal-gotcha.md`, `fluent-v9-theming.md`)
- ✅ MUST use existing Visual Host visual types only (Donut / MetricCard / HorizontalStackedBar / DueDateCard) — no new visual types in this project (binding constraint, design.md §6.4.0)
- ✅ MUST extend Visual Host via Custom Options keys + minimal renderer code; extensions MUST be generic (no Matter-specific code paths in Visual Host)
- ✅ MUST preserve backward compatibility for every existing `sprk_chartdefinition` record (NFR-05)
- ✅ MUST document every new Custom Options key in [VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) + [VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) in the same PR as the code change (FR-DOC-09)
- ✅ MUST include a "Why not Custom Options alone?" paragraph in every PR that adds Visual Host renderer code
- ✅ MUST use `@spaarke/auth` `authenticatedFetch` for all BFF calls; no manual bearer headers
- ✅ MUST use Fluent v9 semantic tokens only (no hex/rgb)
- ✅ MUST preserve `AssociatedOnly` auto-search behavior (SemanticSearchControl.tsx lines 359-375) when moving the toggle to the command bar
- ❌ MUST NOT create new visual types in Visual Host (e.g., `MatterHealthComposite`, `ScorecardDonut`)
- ❌ MUST NOT build parallel chart components in `@spaarke/ui-components` (e.g., `HealthDonut`, `KpiCard`) — Visual Host owns chart rendering
- ❌ MUST NOT build a `MatterPerformancePane` container PCF — form section handles stacking
- ❌ MUST NOT add Matter-specific code paths inside generic Visual Host components
- ❌ MUST NOT introduce new schema fields on `sprk_matter` — all rollups already exist
- ❌ MUST NOT add new BFF endpoints for the Performance pane — Visual Host uses Dataverse WebAPI / FetchXML directly
- ❌ MUST NOT use hover-popover for document preview (`FilePreviewDialog` is the only preview path)
- ❌ MUST NOT use `console.log` for production telemetry — App Insights only
- ❌ MUST NOT skip `/fluent-v9-component` invocation "because the change is small" — Fluent v9 has too many subtle gotchas (portals, slots, theming, React version boundaries) for the skill to be optional
- ❌ MUST NOT mix Fluent v8 imports (`@fluentui/react`) with v9 (`@fluentui/react-components`) in any file touched by this project
- ❌ MUST NOT use React 18+ exclusive features in shared components consumed by both PCFs (React 16/17 per ADR-022) and Code Pages (React 19 per ADR-021)

### Existing Patterns to Follow

- **Visual Host extension model**: see `cardConfigResolver.ts` (3-tier merge), `MetricCardMatrix.tsx` lines 138-177 (`getTokenSetColors()`), `FieldPivotService.ts` (field-pivot data flow)
- **Existing in-production chart defs**: Matter KPI Scorecard (uses `fieldPivot` + `valueFormat: "letterGrade"` + `colorThresholds`), Matter Financial Metrics Scorecard (uses `fieldPivot` + `valueFormat: "currency"`)
- **PCF auth wiring**: see [SpeDocumentViewer authInit.ts](src/client/pcf/SpeDocumentViewer/control/authInit.ts) (canonical), [SpeDocumentViewer BffClient.ts](src/client/pcf/SpeDocumentViewer/control/BffClient.ts) (canonical `authenticatedFetch` usage)
- **PCF theming**: see Visual Host `providers/ThemeProvider.ts` + Semantic Search `services/ThemeService.ts` for Modern Theming API runtime detection
- **Fluent v9 patterns (project repo)**: [.claude/patterns/ui/](.claude/patterns/ui/) — component authoring, portal gotcha (critical for Menu / Dialog inside MDA), React version boundaries, theming. [.claude/patterns/pcf/](.claude/patterns/pcf/) — modern theming, canvas-vs-MDA disabled
- **Endpoint pattern (ADR-008)**: see existing endpoint filters in `src/server/api/Sprk.Bff.Api/Api/`
- **App Insights JS SDK**: standard `@microsoft/applicationinsights-web` setup with instrumentation key from manifest property

---

## Success Criteria

(Mirrors design.md §15.)

### Form-level

1. [ ] Overview tab renders with 2-column 66/34 layout in light AND dark themes — verify by: visual inspection + ThemeToggle test
2. [ ] Matter Information section appears at the top of the left column — verify by: form load on dev matter
3. [ ] Documents PCF appears below Matter Information in the left column — verify by: form load
4. [ ] Five Visual Host instances appear stacked in the right column — verify by: form load + DOM inspect (5 distinct VisualHost instances)
5. [ ] Form cold-load p95 < 2.0 s on a Matter with full data — verify by: App Insights `Page Load Time` percentile query, sample ≥30 loads
6. [ ] All five cards degrade gracefully when backing data is absent — verify by: load a brand-new matter (empty state) and a partial-data matter; each card shows skeleton → empty state

### Documents PCF

7. [ ] Three-dot menu replaces all inline row actions and dialog-toolbar actions; menu items match FR-DOC-01 order — verify by: visual inspection of every action's presence
8. [ ] Click row (excluding menu trigger) opens preview Dialog at 960 px max-width with 2-column body — verify by: row click + DOM inspect
9. [ ] Dialog shows thumbnail, AI summary, Tags chip, Details; Find similar / Close / Open file buttons wired — verify by: open Dialog on doc with full metadata
10. [ ] List view shows sortable columns and multi-select checkboxes; default sort = modifiedAt DESC — verify by: toggle to list view + click each column header
11. [ ] Card view renders responsive tile grid; cards retain match badge + 3-dot menu in corner — verify by: toggle to card view
12. [ ] View toggle persists per-matter-per-user via localStorage — verify by: toggle view, reload form, confirm preference retained; switch matters, confirm independent
13. [ ] Tags filter dropdown lists alphabetically-sorted `sprk_documenttype` values; OR-match — verify by: select 2 tags, confirm union of matching docs
14. [ ] Active-tags chip row appears when ≥1 tag selected; × removes; "Clear all" empties — verify by: chip interaction
15. [ ] Filtered count reflected in footer — verify by: "Showing X of Y" string check
16. [ ] All existing PCF behaviors continue after filter relocation — verify by: each filter exercised; AssociatedOnly auto-search verified
17. [ ] `/api/ai/search` projection includes `modifiedAt` + `modifiedBy` — verify by: BFF integration test or browser DevTools network tab
18. [ ] Bulk-action bar appears at ≥1 selection — verify by: select rows
19. [ ] Each bulk action operates correctly: Email selected (zip attachment), Download selected (calls bulk-download endpoint), Pin selected (localStorage), Delete selected (with confirmation), Document Type → selected (optimistic + 5s Undo), Share link (email with record URLs) — verify by: exercise each
20. [ ] `POST /api/documents/bulk-download` returns valid zip for valid request; HTTP 413 for >500 ids; per-doc failures appear in `_FAILED.txt` manifest — verify by: integration test

### Performance side pane (Visual Host)

21. [ ] Five `sprk_chartdefinition` records seeded; each Visual Host instance wired correctly — verify by: chart def query + form load
22. [ ] Matter Health card renders donut + composite letter + 3-area breakdown rows from `sprk_*compliancegrade_current` fields — verify by: visual against prototype mockup
23. [ ] Donut segments colored by `colorThresholds` (brand / warning / danger) and themed correctly in dark mode — verify by: dark mode toggle
24. [ ] Budget card shows `$50K`-style headline + sub-line + horizontal progress bar (color-thresholded by % spent) — verify by: visual + dev-tools color inspect
25. [ ] Tasks card shows overdue count + red Fluent `Badge` (visible when >0) + upcoming sub-line via FetchXML aggregation — verify by: load matter with mixed task data
26. [ ] Next Date card shows date + event title + days-from-now via `DueDateCard` — verify by: load matter with upcoming event
27. [ ] Activity card shows event count + brand-color sub-line via FetchXML — verify by: load matter with recent events
28. [ ] Each card has `CardChrome` header + corner expand icon; AI sparkle slot hidden in v1 — verify by: DOM inspect
29. [ ] Card expand opens drill-through to underlying entity list via existing `sprk_chartdefinition` Drill Through Settings — verify by: click expand on each card; correct entity list opens with current-matter filter
30. [ ] All cards render skeletons while loading; empty-state when no data — verify by: throttle network + load empty matter

### Cross-cutting

31. [ ] All UI uses Fluent v9 tokens — automated grep confirms zero hardcoded hex/rgb in touched files — verify by: CI grep
32. [ ] axe DevTools reports zero WCAG 2.1 AA violations on Overview tab in light + dark — verify by: axe scan
33. [ ] App Insights events fire as specified in FR-TEL-01 — verify by: App Insights Live Metrics during exercise
34. [ ] `code-review` skill (FULL rigor) + `adr-check` skill pass for every task — verify by: skill output
35. [ ] All existing in-production chart definitions render unchanged after FR-VH-* extensions — verify by: regression smoke test on Matter KPI Scorecard + Matter Financial Metrics Scorecard chart defs
36. [ ] [VISUALHOST-ARCHITECTURE.md](docs/architecture/VISUALHOST-ARCHITECTURE.md) and [VISUALHOST-SETUP-GUIDE.md](docs/guides/VISUALHOST-SETUP-GUIDE.md) updated with all new Custom Options keys, examples, and chart-def authoring patterns — verify by: doc PR review

---

## Dependencies

### Prerequisites

- ADR awareness: ADR-006, ADR-007, ADR-008, ADR-010, ADR-012, ADR-019, ADR-021, ADR-022, ADR-028, ADR-029 loaded before relevant tasks
- **`/fluent-v9-component` skill invocation** at the start of every UI-authoring task (binding gate per design.md §6.0). Loads patterns from [.claude/patterns/ui/](.claude/patterns/ui/) and [.claude/patterns/pcf/](.claude/patterns/pcf/)
- Visual Host architecture + setup-guide docs reviewed before any Visual Host or chart-definition task (binding gate per design.md §6.4.0)
- Recon evidence: design.md Appendix C (6 sub-agent passes) available for traceability
- Dev environment with: an App Insights resource (instrumentation key), a dev Dataverse environment with the existing Matter KPI Scorecard and Matter Financial Metrics Scorecard chart defs to regression-test against

### External Dependencies

- Application Insights resource provisioned (or use existing Spaarke App Insights instance — confirm with architecture)
- Dataverse permissions to author `sprk_chartdefinition` records and modify Matter main form solution XML
- Test matters with: full KPI assessment data, full financial rollups, mixed task states (overdue + upcoming), recent events (for Activity card), and empty / brand-new state for skeleton verification

### Skills Used

- **`fluent-v9-component`** — **MANDATORY** for every task touching Fluent v9 React components (FR-DOC-01..06, FR-VH-01..05, FR-SC-01..02). Binding per design.md §6.0. Invoke at Step 0.5 of each task, before reading affected files. See [.claude/skills/fluent-v9-component/SKILL.md](.claude/skills/fluent-v9-component/SKILL.md).
- `task-execute` — every task in this project (mandatory per [CLAUDE.md §4](CLAUDE.md))
- `adr-aware` — load relevant ADRs at task start
- `adr-check` — quality gate at Step 9.5 of FULL-rigor tasks
- `code-review` — quality gate at Step 9.5 of FULL-rigor tasks; **enforces `/fluent-v9-component` invocation on UI PRs** (blocks merges that skipped it)
- `pcf-deploy` — when deploying SemanticSearchControl or VisualHost PCFs (uses `npm run build:prod`)
- `dataverse:dv-metadata` — for any sprk_documenttype option set work
- `dataverse:dv-solution` — for Matter main-form solution XML changes
- `bff-deploy` — for BFF endpoint deployment
- `code-page-deploy` — N/A (this project does not use Code Pages)

### Skill invocation map per FR

| FR group | `/fluent-v9-component` required? | Other key skills |
|---|---|---|
| FR-DOC-01..06 (Documents PCF UI) | **YES** | `pcf-deploy` at the end |
| FR-DOC-07 (telemetry) | No (no Fluent component changes) | — |
| FR-DOC-09 (docs updates) | No | — |
| FR-VH-01..05 (Visual Host renderer extensions) | **YES** | `pcf-deploy` at the end |
| FR-DV-01..05 (chart definition records) | No (Dataverse data, not React) | `dataverse:dv-metadata` or PowerShell seed script |
| FR-SC-01..02 (shared components) | **YES** | None other |
| FR-FORM-01 (solution XML) | No | `dataverse:dv-solution` |
| FR-TEL-01 (App Insights wiring) | No (SDK integration, not Fluent) | — |
| FR-BFF-01..02 (BFF additions) | No (C#, not React) | `bff-deploy` at the end |

---

## Owner Clarifications

Captured during Phase A + A.5 recon, §14 resolution rounds, and design-to-spec Step 2.5:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| AI Summary Banner | Should the Rev 1 AI Summary banner be in scope? | No — Insights Engine not yet built; deferred to r2 | Removed entire FR group (AI Summary Banner PCF), §6.1, US-1/US-2, scorecard payload model |
| Performance pane PCF | Build a new `MatterPerformancePane` PCF? | No — use 5 Visual Host instances + 5 chart definitions instead | Saves 1 new PCF; binding architectural stance §6.4.0 |
| Tags filter source | Use `sprk_filekeywords` (ntext) or new multi-select choices column or `sprk_documenttype`? | Use existing `sprk_documenttype` choice field | No schema change; no `tags` projection in BFF; Tags filter = relocated Document Type filter |
| Sortable columns | Modified vs Created? | Project BOTH `created*` AND `modified*`; default sort `modifiedAt DESC` | FR-BFF-01 adds modifiedAt/modifiedBy |
| 3-dot menu actions | Trim list or keep all? | Keep all 8 existing + Pin/Rename/Delete; keep Email in per-row (single-doc convenience) | FR-DOC-01 13-action menu |
| AiSummaryPopover fate | Remove from sparkle hover after adding to 3-dot menu? | Keep both — hover popover for quick-glance + menu item for keyboard access | FR-DOC-01 retains hover affordance |
| Telemetry destination | App Insights via BFF endpoint or direct? | Direct browser SDK; instrumentation key via manifest env-var pattern | FR-TEL-01 |
| Filter Drawer escape hatch | Needed in v1? | Drop from v1 — command-bar dropdowns sufficient | FR-DOC-06 |
| AI sparkle on CardChrome | Hide / disable / omit during r1? | Hide (`showAiSparkle: false` default); slot exists for r2 | FR-VH-05 |
| Bulk-action menu actions | Which actions appear? | Email selected, Download selected, Pin selected, Delete selected, Document Type → selected, Share link | FR-DOC-02 |
| Composite letter formula | What aggregation for Matter Health? | Simple arithmetic mean of 3 area scores | FR-DV-01 uses `donutCenterMode: "meanOfFields"` |
| Side pane responsiveness | 1024 px hard minimum? Compress vs stack? | Hard 1024 px minimum; right rail compresses gracefully, no stacking, no overflow | NFR-03 |
| Share link bulk action | What does it do? | Email composer with body containing clickable `sprk_document` record URLs (NOT SPE files) | FR-DOC-02 Share link row |
| Card expand action mode | New `OpenDrillDialog` or reuse existing? | Reuse existing `sprk_chartdefinition` Drill Through Settings (per-card target entity + filter) | FR-VH-05 onExpand → existing handleExpandClick |
| Bulk download | New BFF endpoint or client-side zip? | New BFF endpoint `POST /api/documents/bulk-download` (justifies §12.2 placement) | FR-BFF-02 |
| Pin column rendering | Always show / hover / no column? | Small pin icon visible only on pinned rows; click toggles | FR-DOC-02 Pin selected + FR-DOC-04 list view |
| Doc Type bulk UX | Confirmation dialog or optimistic? | Optimistic UI; toast with 5s Undo; error toast with Retry on failure | FR-DOC-02 Document Type row |

---

## Assumptions

- **`sprk_event` filter fields** — assumed `sprk_regardingmatter` lookup, `sprk_finalduedate` date, `sprk_eventstatus` choice exist on `sprk_event`. Verify in Phase 2.1 during chart def authoring; adjust FetchXML if field names differ.
- **App Insights resource** — assuming an existing Spaarke App Insights resource will be used and its instrumentation key is available. If a new resource is needed, treat as a Phase 5 prerequisite.
- **Solution layer** — assuming Matter main form lives in the unmanaged Spaarke solution that's also the deployment target. If the form is in a managed solution, schema changes may need to flow through a separate process (Phase 2.2 to confirm).
- **`sprk_documenttype` option set** — assuming the existing FilterPanel's "Document Type" filter currently functions; the Tags filter relocation does not change the option set itself.
- **Matter KPI Scorecard / Matter Financial Metrics Scorecard chart defs** — assumed to still be in production and render via current MetricCard visual type; required for NFR-05 regression verification.

---

## Unresolved Questions

*All 11 §14 questions and 3 Step-2.5 questions are resolved. No open blockers for `/project-pipeline`.*

Potential mid-implementation clarifications (non-blocking):

- [ ] **Telemetry event schema** — final list of event names and properties (PII review) before Phase 5 — Blocks: telemetry validation
- [ ] **Bulk-download endpoint authorization scope** — same policy as preview-url assumed; confirm with architecture if a separate policy is needed for bulk operations
- [ ] **Existing chart defs regression-test inventory** — full list of in-production chart defs to regression-test against FR-VH-* extensions; Phase 1 task to enumerate before any Visual Host code change merges

---

*AI-optimized specification. Original design: [design.md](./design.md) Rev 5. Generated by `/design-to-spec` 2026-05-26.*
