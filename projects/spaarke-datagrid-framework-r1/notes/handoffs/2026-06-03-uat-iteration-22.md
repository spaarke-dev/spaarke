# Handoff — Phase C UAT iteration through Round-23

> **Created**: 2026-06-03 (pre-compact)
> **Project**: `spaarke-datagrid-framework-r1`
> **Active branch**: `work/spaarke-datagrid-framework-r1`
> **Worktree**: `c:\code_files\spaarke-wt-spaarke-datagrid-framework-r1`
> **PR**: #329

---

## TL;DR

Phase A + B + C tasks 020–025 SHIPPED. Phase C UAT (task 026) has been iterating for ~23 rounds of fixes, all uncommitted, all deployed to DEV. Two outstanding items:

1. **Parent-context filter NOT working** — the grid shows ALL records instead of just the matter's. Root cause: VisualHost chart-def doesn't carry `sprk_contextfieldname`. **User is performing the data fix** via Dataverse MCP:
   - `update_record` on `sprk_chartdefinition` → `a8b8df8b-f359-f111-a825-3833c5d9bcab` → `sprk_contextfieldname = '_sprk_matter_value'`
   - Same on `7bf5b79e-f359-f111-a825-3833c5d9bcab`
2. **Column-menu drop shadow** still possibly being clipped by Fluent's portal — Round-23 added inline `filter: drop-shadow` on `MenuPopover` via `style` prop. User to verify next session.

Once both resolve, Phase C UAT graduates → commit + push everything + close task 026 + move to Phase D / E / F.

---

## What shipped through task 025 (committed)

All committed and deployed BEFORE today's UAT iteration:

| Phase | Tasks | Status |
|---|---|---|
| A — Foundation | 001–009 | ✅ Committed |
| B — BFF passthrough | 010–016 | ✅ Committed (017 deploy DEFERRED ⏸ pending insights-engine-r2 master merge) |
| C — Drill-throughs | 020–025 | ✅ Committed + deployed to DEV |
| C UAT | 026 | 🟡 In progress (this session) |

Last committed SHA: `76e6a79c` (FetchXML top-strip fix).

---

## What's UNCOMMITTED (today's UAT work — Rounds 1-23)

All Phase C UAT fixes are uncommitted on the worktree. Working tree currently shows ~30 modified files + new files in:
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/HeaderCellContent.tsx` (NEW — heavy iteration)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/ViewSelector.tsx` (NEW)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/filterChips/` (NEW directory: types, chipDiscovery, chipFetchXml, FilterChipBar)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (heavy modification)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/commandBar/CommandBar.tsx` (modified — OOB refactor)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts` (DisplayName + ResolvedBehavior)
- `src/client/shared/Spaarke.UI.Components/src/services/XrmDataverseClient.ts` (Xrm.WebApi DisplayName fetch)
- `src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts` (`EntityAttributeMetadata.displayName`)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/useLazyLoad.ts` (strip `top` when paging)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/filterChips/types.ts` (`op` field on text/lookup)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/filterChips/chipFetchXml.ts` (respect `op` for text filter)
- `src/solutions/sprk_kpiassessmentspage/src/main.tsx` (Custom Page shell — parseMatterId fallbacks, theme prop)
- `src/solutions/sprk_kpiassessmentspage/index.html` (`box-sizing: border-box` reset)
- `src/solutions/sprk_invoicespage/src/main.tsx`
- `src/solutions/sprk_invoicespage/index.html`
- Plus 15 files reformatted by `npx prettier --write` (lint fix for PR #329 Client Quality CI)

---

## Architecture decisions made during UAT

| Decision | Rationale |
|---|---|
| `HeaderCellContent` is the Fluent-v9-compatible bridge replacing the lifted `ColumnHeaderMenu` from Events | Spec FR-DG-11 mandates Fluent v9 native DataGrid; Events `<th>`-based component doesn't compose inside Fluent's `DataGridHeaderCell` |
| Filter UI = OOB Power Apps structured form (Equals dropdown + Input + Apply/Clear), NOT the chip primitive | Matches user-shared OOB screenshot; lifted shape from existing `ColumnHeaderMenu.renderFilterContent` |
| Inline `filter: drop-shadow` on `MenuPopover` via React `style` prop | Fluent v9 PopoverSurface portal wrapper has hardcoded styles that win over Griffel className; inline style beats class specificity |
| `filterCard` div wrapping form content INSIDE FluentProvider INSIDE PopoverSurface | Without the wrapper, `rowGap` set on PopoverSurface only sees 1 direct child (FluentProvider) → form rows stack flush |
| `box-sizing: border-box` in Custom Page index.html | Without it, DataGrid root padding adds to 100% width/height → grid overflows the modal viewport |
| `text` filter ChipValue extended with `op?: 'equals'\|'contains'\|'begins'` | User explicitly called out that "Equals" operator was behaving like "Contains" — chipFetchXml now respects the chosen operator |
| Chip discovery falls back to text-chip for every column when metadata is thin | Xrm SDK returns 0 attributes for `sprk_kpiassessment` in our env (confirmed via console diagnostic); fallback ensures "Filter by" always works |

---

## Outstanding bugs (in priority order)

### 1. Parent-context filter not applying (BIG)
**Status**: User performing data fix this session.

**Diagnosis** (confirmed via console.info `[DataGrid] fetchXml composition`):
- `parentContext.matterId: ''` (empty string)
- `parentFilter` is sometimes correct, sometimes undefined depending on render
- The URL data envelope (from VisualHost) only contains `entityName=sprk_matter&mode=dialog` — NO `filterValue`
- My in-shell fallbacks (`window.parent.Xrm.Page`, `window.top.Xrm.Page`, `Xrm.Utility.getPageContext()`) all return nothing because Power Apps dialog Custom Pages are isolated from the originating form's Xrm context

**Fix the user is performing**:
- Update both `sprk_chartdefinition` records via Dataverse MCP: set `sprk_contextfieldname = '_sprk_matter_value'`
- Once set, VisualHostRoot's `handleExpandClick` builds URL with `filterValue=<matterGuid>` → my Custom Page's `parseMatterId` URL-envelope parser picks it up → `overlayParentContextFilter` injects the condition into FetchXML

**Verify after fix**: drill-through dialog → DevTools Console → `[DataGrid] fetchXml composition` should show `parentContext: {matterId: '<guid>'}`, `hasParentFilterMatch: true`, and the FetchXML should contain `<condition attribute="sprk_matter" operator="eq" value="{guid}"/>`.

### 2. Column-menu shadow not visible
**Round-23 fix**: inline `filter: drop-shadow(0 6px 12px rgba(0,0,0,0.18)) drop-shadow(0 2px 4px rgba(0,0,0,0.12))` on `<MenuPopover style={...}>`. Should win specificity-wise. User to verify next session.

### 3. Other minor visual polish from user feedback (deferred)
- Row borders: user noted earlier they shouldn't extend fully to the outer card; partially addressed via padding on `gridScroll` but more polish possible
- View selector dropdown polish — deferred
- Column header font / row heights — landed at 14px / 41px header / 38px body per user spec
- Operator semantics in filter — `equals` / `contains` / `begins` wired end-to-end in `chipFetchXml.ts` (Round-13)

---

## Known platform limitations (NOT code bugs)

| Issue | Source | Workaround |
|---|---|---|
| `Xrm.Utility.getEntityMetadata('sprk_kpiassessment')` returns 0 attributes | Likely Xrm SDK version / entity ACL in this org | Text-chip fallback in `chipDescriptors` memo (works) + humanized labels (Performancearea instead of Performance Area) |
| Custom Page dialog has no `Xrm.Page` access to originating form | Power Apps platform isolation | URL data envelope from VisualHost (requires chart-def `sprk_contextfieldname`) |

---

## How to resume next session

1. **Read `current-task.md`** — should still point at task 026
2. **Read this handoff file** for context
3. **Run `npm run build`** in `src/client/shared/Spaarke.UI.Components/` to verify clean
4. **Check `git status`** to see uncommitted changes
5. **Ask user**:
   - Did the chart-def update via Dataverse MCP succeed?
   - Did drill-through dialog show the parent-filtered grid?
   - Did the column-menu shadow finally appear?
6. **If all yes**: commit the bundle ("UAT iteration through Round-23 — Phase C UAT close") + push + verify PR #329 CI passes (after Prettier normalization was run, lint should be clean; Debug test was a flake)
7. **If chart-def or shadow still broken**: continue iterating

---

## Files for quick reference

- `projects/spaarke-datagrid-framework-r1/spec.md` — original spec
- `projects/spaarke-datagrid-framework-r1/design.md` — design
- `projects/spaarke-datagrid-framework-r1/tasks/026-phase-c-uat.poml` — UAT task
- `projects/spaarke-datagrid-framework-r1/current-task.md` — task state
- `projects/spaarke-datagrid-framework-r1/CLAUDE.md` — project-level AI instructions
- `projects/spaarke-datagrid-framework-r1/notes/testing-screenshots/` — user-supplied OOB + current-state screenshots for visual reference

## Deployed bundles (DEV)

| Web resource | webresourceid | Last bundle |
|---|---|---|
| `sprk_kpiassessmentspage.html` | `8329ddcf-9e5e-f111-ab0c-7c1e521b425f` | 1245 KB (Round-23, SHA `3cfd1481...`) |
| `sprk_invoicespage.html` | `b329ddcf-9e5e-f111-ab0c-7c1e521b425f` | 1245 KB (Round-23, SHA `797aef5d...`) |

Both `UPDATE` (not CREATE) — solution `spaarke_core`, ComponentType=61.

---

## POST-COMPACT DOC TASK — Parent-context filtered grids (architectural pattern)

**Why this is here**: user asked late in session (after chart-def fix verified working) whether the "X - Matter Context" dedicated savedqueries we created in task 020 are REQUIRED for every future parent-context drill-through grid. Answer is no, they're one of three options. This pattern needs to be canonical documentation so Phase D (EventsPage migration), Phase E (SemanticSearch), and any future drill-through reuses the right shape.

**Where to write the doc** (after compact, when resuming):
- `projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md` (project-local first)
- Then promote to `docs/architecture/` or `.claude/patterns/ui/` once stable (likely as `.claude/patterns/datagrid/parent-context-pattern.md`)

**Doc content to include**:

### Parent-context filtering — two architectural layers

The framework filters a drill-through grid to records related to a parent record (e.g., Invoices for a Matter) via TWO composable layers:

| Layer | Where it lives | What it does |
|---|---|---|
| **Base savedquery** | configjson `source.savedQueryId` | Defines columns, sort, base filters (statecode=0, querytype=0) |
| **Parent overlay** | configjson `behavior.parentContextFilter` | At runtime, `overlayParentContextFilter` (`fetchXmlOverlay.ts`) injects `<condition attribute='sprk_matter' operator='eq' value='{matterGuid}'/>` into the base FetchXML |

The overlay runs INSIDE the framework regardless of which savedquery you pair it with. So you can pair the overlay with ANY savedquery.

### Three options for the savedquery pairing

| Option | Setup | Best when |
|---|---|---|
| **A. Dedicated context view** (what R1 task 020 used) | Create `<Entity> - <Parent> Context` savedquery + reference its GUID in configjson `source.savedQueryId` | UX needs columns tailored for drill-through (strip Owner column when context is already a Matter, etc.) |
| **B. Reuse existing standard view** | Reference any existing savedquery GUID in configjson `source.savedQueryId` + add `behavior.parentContextFilter` overlay | Standard "Active X" view's columns are already adequate |
| **C. Auto-pick entity default** | `source.type: 'savedquery-set', entityLogicalName: 'X'` + overlay | Quick scaffold — framework picks the default view automatically |

### Always required (regardless of option A/B/C)

Three things, for every parent-context grid:

1. **configjson `behavior.parentContextFilter`** — `{ attribute: 'sprk_matter', parentContextKey: 'matterId', operator: 'eq' }` (or analogous for other parent entity types)
2. **VisualHost chart-def `sprk_contextfieldname`** — the LOOKUP COLUMN REFERENCE on the CHILD entity pointing to the parent. Format: `_<lookupfield>_value`. Examples (note these differ per entity):
   - KPI Assessment → Matter: `_sprk_matter_value`
   - Invoice → Matter: `_sprk_matter_value`
   - Event → Matter: `_sprk_regardingmatter_value` (because the Event lookup is named `sprk_regardingmatter`, not `sprk_matter`)
3. **Custom Page shell** — must parse the URL `data=` envelope (form-encoded, NOT JSON; VisualHost convention) and pass `parentContext={{ matterId }}` to `<DataGrid>`. The shell pattern is shared verbatim between `sprk_kpiassessmentspage` and `sprk_invoicespage` — reusable for any future drill-through page.

### Anti-patterns avoided

- ❌ Embedding `<condition value='@MatterId'/>` placeholders in the savedquery FetchXML — Dataverse rejects placeholder syntax at save time (task 020 deviation D-020-02). Always use the overlay at runtime.
- ❌ Touching VisualHost PCF code to pass recordId — NFR-06 forbids it in R1. The chart-def `sprk_contextfieldname` field is the data-side hook that lets VisualHost include the matter id in the URL.
- ❌ Relying on `Xrm.Page` inside the Custom Page dialog — Power Apps `navigateTo({pageType:'webresource'})` dialogs are isolated from the originating form's Xrm context. The URL data envelope is the only reliable channel.

### Where this pattern shows up next

- Phase D (EventsPage migration, task 031) — will pair `behavior.parentContextFilter` with Event→Matter lookup `_sprk_regardingmatter_value`
- Phase E (SemanticSearch migration, task 041) — may not need parent context (search is global), but the option-A vs B vs C choice will apply if any drill-through views are added
- Any future workspace widget hosting a `<DataGrid>` inside a parent-record context (Calendar widget, Insights Engine, etc.)

---

## Useful skills / commands

- Build lib: `cd src/client/shared/Spaarke.UI.Components && npm run build`
- Build Custom Page: `cd src/solutions/sprk_<name>page && rm -rf dist/ node_modules/.vite/ .vite/ && npm run build`
- Deploy (reusable script at `%TEMP%\dv-deploy-r1\Deploy-DatagridFrameworkCodePages.ps1`)
- Prettier normalize: `npx prettier --write "src/client/**/*.{ts,tsx}"`
- Console diagnostics to look for: `[DataGrid] fetchXml composition`, `[sprk_kpiassessmentspage] parseMatterId resolved to:`, `[XrmDataverseClient] retrieveEntityMetadata`
