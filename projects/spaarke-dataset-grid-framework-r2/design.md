# Spaarke DataGrid Framework — R2 Design

> **Status**: Discovery — awaiting `/design-to-spec` conversion
> **Predecessor**: `projects/spaarke-datagrid-framework-r1/` (framework R1, shipped 2026-06)
> **Discovery**: 2026-07-01 during `ai-spaarke-ai-workspace-UI-r2` post-merge investigation
> **Related shipped work**: `ai-spaarke-ai-workspace-UI-r2` (Layout 1 unification for row-open) — this project addresses the sizing / configuration layer, orthogonal to R2's modal-standard work

---

## Executive summary

The Spaarke DataGrid framework (R1) established `<DataGrid configId=... />` as the canonical shared-lib grid, driven by a Dataverse `sprk_gridconfiguration` record's `sprk_configjson` schema. R1 shipped the core: FetchXML paging chain, Fluent v9 native rendering, `IDataverseClient` adapter, filter chips, command bar, and lazy-load via IntersectionObserver.

**Production use in `ai-spaarke-ai-workspace-UI-r2` surfaced 11 gaps** — the load-bearing one is a broken height chain that causes multi-section workspace grids to grow unbounded instead of scrolling. This was tactically patched in R2 by adding a per-section `maxHeight` CSS style, but the correct fix belongs in the framework's sizing model. The remaining 10 items span maker experience (config templates, view picker allowlist, per-layout renames), per-instance flexibility (configId override, row-height override), and framework hygiene (default alignment, doc drift).

R2 (this project) collects these gaps and delivers a framework-level treatment. Total estimated effort: **~2.5 days of focused work + one deploy cycle**.

---

## Discovery context

### How this project came to exist

During the post-merge validation of `ai-spaarke-ai-workspace-UI-r2` (2026-07-01), the operator reported: **"the Communications grid shows 50+ rows without pagination — just a single long list no matter how many records."** A DevTools height-chain diagnostic revealed the root cause and surfaced the surrounding gaps captured here.

### What this project is NOT

- Not a rewrite of the DataGrid component
- Not a change to the `sprk_configjson` schema top-level shape (still `_version: '1.0'`)
- Not a retirement of `RecordNavigationModalShell` or any Layout 2 surface
- Not related to the R2 Layout 1 unification (that shipped separately)

R2 is a targeted improvement pass — additive schema fields, additive framework capabilities, tactical hack cleanup.

---

## Issue 1 — Height chain break (P0, load-bearing)

### Symptom

In a multi-section workspace layout (e.g., "Dashboard II" containing Communications), a DataGrid-based section grows to fit ALL records instead of scrolling. When `behavior.pageSize` is 100 (framework default), the operator sees 100 rows without a scrollbar; when set to 25, the operator sees exactly 25 rows without a scrollbar and cannot reach rows 26+.

### Root cause

Located in [`buildDynamicWorkspaceConfig.ts:167-171`](../../src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/buildDynamicWorkspaceConfig.ts#L167-L171):

```typescript
// Apply defaultHeight from registration if factory didn't set minHeight
if (registration.defaultHeight && !sectionConfig.style?.minHeight) {
  minHeight: registration.defaultHeight,   // ← MIN, not MAX
}
```

`SectionMetadata.defaultHeight` (e.g., `'480px'` for entity-view widgets) is applied as **`min-height`** — a FLOOR without a corresponding CEILING. So:

- Section starts at 480px (floor)
- When content > 480px, section grows unbounded
- The DataGrid's inner scroll surface (which HAS `overflow: auto` and is correctly configured per height-chain contract § 7.2) never overflows because its container has infinite height
- IntersectionObserver on the DataGrid's sentinel fires once at initial mount (sentinel is visible because the section grew to reveal it), fetching all pages up to available records
- Result: no scrollbar, all rows visible, no lazy-load-on-scroll behavior

### Evidence

DevTools height-chain dump (from the discovery session) showed at depth 10 in the DOM tree:

```
div (SectionPanel card) | h=1218px | display=flex | minH=480px | overflow=hidden
```

Section grew from its 480px floor to **1218px** to fit 25 rows. The scrollable body at depth 4 (`overflow: auto`, `flex: 1 1 0%`) had `scrollHeight === clientHeight === 1047px` — no overflow → no scrollbar.

### Tactical mitigation (already deployed 2026-07-01)

Patched via per-section `maxHeight` in all 6 entity-list registrations under `src/solutions/LegalWorkspace/src/sections/`:

```typescript
style: { overflow: "hidden", maxHeight: "480px", display: "flex" },
```

Deployed via `Deploy-AllDataGridConsumers.ps1 -Only LegalWorkspace`. Verified: Communications now shows ~10 rows with a visible Fluent scrollbar, and scrolling triggers page-2 fetch.

**Why this is tactical, not proper**: it forces 480px on the section **everywhere**, even in single-section full-page layouts (e.g., the "Documents" system workspace layout added by ai-spaarke-ai-workspace-UI-r2 follow-up PR #531). A user opening "Documents" as its own tab now sees a tiny 480px window on a full-page tab — wasted vertical space.

### Proposed R2 fix

Add a new `SectionMetadata` field to distinguish sizing intent:

```typescript
// SectionMetadata additions
readonly defaultHeight?: string;                     // existing — used as MIN OR MAX depending on...
readonly contentSizing?: 'grow' | 'clamped';         // NEW — default 'grow' for back-compat
```

Semantics:

- **`contentSizing: 'grow'`** (default; back-compat) — `defaultHeight` is applied as `min-height`. Section grows with content. Correct for cards, banners, headers, non-scrolling widgets.
- **`contentSizing: 'clamped'`** — `defaultHeight` is applied as `max-height` + `flex: 1 1 0` on the section content wrapper. Section is capped; inner content scrolls. Correct for DataGrid-based entity lists, kanbans, any widget where content > container-height is normal.

Update logic in `buildDynamicWorkspaceConfig.ts`:

```typescript
if (registration.defaultHeight && !sectionConfig.style?.minHeight && !sectionConfig.style?.maxHeight) {
  const sizingKey = registration.contentSizing === 'clamped' ? 'maxHeight' : 'minHeight';
  sectionConfig.style = {
    ...sectionConfig.style,
    [sizingKey]: registration.defaultHeight,
    ...(registration.contentSizing === 'clamped' && { overflow: 'hidden', display: 'flex' }),
  };
}
```

Update the 6 entity-list sections in `sectionMetadataCatalog.ts` to include `contentSizing: 'clamped'`. Then remove the tactical `maxHeight` from the section registration files (Issue 9 below).

### Effort

~2 hours: 1 hr framework change + tests, 1 hr metadata catalog updates + regression testing.

---

## Issue 2 — Per-layout row-height override (P0)

### Symptom

A widget that needs 480px in a multi-section dashboard needs 800px+ in a single-section full-page layout to make good use of the vertical space. Today the widget has one hardcoded `defaultHeight` metadata value baked into its section registration — no way to vary per layout instance.

### Impact

- The "Documents" system workspace layout deployed by ai-spaarke-ai-workspace-UI-r2 follow-up (PR #531) currently uses the Documents section's `defaultHeight: '480px'`. As a full-tab layout it wastes ~500px of vertical space.
- Any future full-page single-section layout inherits the same problem.
- Blocks the "different behavior per layout" flexibility the operator asked for during discovery.

### Proposed R2 fix

Extend the `LayoutJsonRow` schema (Dataverse `sprk_workspacelayout.sprk_sectionsjson`) to accept an optional `rowHeight`:

```json
{
  "id": "row-1",
  "columns": "1fr",
  "rowHeight": "80vh",             ← NEW: optional; overrides section defaultHeight for this row only
  "sections": ["documents"]
}
```

Semantics:

- If `rowHeight` is set: the row is clamped to that height. Sections in the row respect that ceiling regardless of their `defaultHeight` / `contentSizing`.
- If `rowHeight` is absent: current behavior — sections apply their own `contentSizing` rule.

Additionally, the `WorkspaceLayoutWizard` needs a UI to set `rowHeight` per row (input field with common presets: `auto`, `40vh`, `60vh`, `80vh`, `100vh`).

### Effort

~4 hours: 1 hr type + schema change, 2 hr wizard UI, 1 hr regression + deploy testing.

### Interaction with Issue 1

Issue 1 provides the default (per-widget); Issue 2 provides the per-instance override. Both are needed. Ship Issue 1 first (broader coverage), then Issue 2 (fine-grained control).

---

## Issue 3 — Per-instance `configId` override in layout JSON (P1)

### Symptom

Communications config record has `pageSize: 25` — good for embedded workspace widgets, but wrong for a full-page "All Communications" code page where fewer round trips are preferable (e.g., `pageSize: 100`).

Today the config record is a single global; every consumer sees the same behavior. To vary, an operator either:

- Creates alternate config records + manually swaps GUIDs in code (requires code deploy)
- Accepts one-size-fits-all behavior

### Impact

- Blocks maker-driven per-layout tuning
- Encourages code-level GUID swaps that require developer intervention for what should be a maker task
- Blocks the operator's stated use case: "Documents at 25 in the dashboard, 100 in the Documents-only layout"

### Proposed R2 fix

Extend `LayoutJsonRow.sections` from `string[]` to `Array<string | SectionInstance>`:

```typescript
type SectionInstance = {
  id: string;                         // required: section registration id
  configIdOverride?: string;          // NEW: override the section's baked-in configId
  label?: string;                     // NEW (folds Issue 6): override the section title
  overrides?: {
    pageSize?: number;                // NEW: override behavior.pageSize
    availableViews?: string[];        // NEW: override source.availableViews (see Issue 5)
    // room for future overrides
  };
};
```

Backward-compat: bare-string entries continue to work (`"documents"` treated as `{ id: "documents" }`).

Wizard update: when placing a section into a row slot, the wizard offers an "Advanced" panel with fields for configId picker (dropdown of `sprk_gridconfiguration` records for the section's entity), label override, pageSize override, view allowlist.

### Effort

~1 day: schema/type change (2 hr) + framework wiring in `buildDynamicWorkspaceConfig` (2 hr) + wizard UI (3 hr) + regression + tests (1 hr).

### Interaction with Issue 5 + Issue 6

Issue 5's `availableViews` and Issue 6's `label` per-instance rename both live in the same schema slot as Issue 3's `SectionInstance`. Ship them together as a single schema evolution.

---

## Issue 4 — Width preference declaration on widgets (P1)

### Symptom

Some widgets (Communications, Invoices) have many columns and need FULL row width to display email subjects / invoice descriptions without truncation. Others (small info cards) work fine at 50% width. Today, the `WorkspaceLayoutWizard` lets makers drop any widget into any slot regardless of the widget's intent — resulting in cramped rendering when a full-width widget lands in a `1fr 1fr` row.

### Proposed R2 fix

Add a new `SectionMetadata` field:

```typescript
readonly widthPreference?: 'full' | 'half' | 'any';
```

Semantics:

- **`'full'`** — widget renders correctly only at full row width. Wizard prevents placement in a multi-slot row; runtime dev-guard warns if placed in one.
- **`'half'`** — widget designed for ~50% width. Wizard warns if placed in a full-width slot (may have too much whitespace).
- **`'any'`** — no preference (default; current behavior).

Wizard behavior:

- When user drops a `full` widget into a multi-slot row: modal asks "This widget needs full width — convert the row to single-column?" with Yes / Cancel
- When user drops a `half` widget into a single-slot row: subtle warning icon on the section chip; hover tooltip explains
- Runtime dev-mode guard (in `sectionRegistry.ts:141-179` existing drift check): if a widget rendered in a multi-column row has `widthPreference: 'full'`, `console.warn`

### Recommended settings for the 6 entity-list widgets

Based on their existing column counts (via savedquery layoutXml):

- Communications, Invoices, Work Assignments — `widthPreference: 'full'` (many columns, wide readable rows)
- Documents, Matters, Projects — `widthPreference: 'any'` (fewer columns, work at 50% too)

Operator can override during authoring.

### Effort

~2 hours: metadata field + type update (30 min), wizard UI logic (1 hr), runtime dev-guard (15 min), tests (15 min).

---

## Issue 5 — View picker allowlist (`availableViews`) (P1)

### Symptom

`<ViewSelector>` dropdown shows every active savedquery for the entity (via `dataverseClient.retrieveSavedQueriesForEntity`). Operator screenshot showed Communications with `Active Communications` / `All Incoming Email` / `Inactive Communications` — some of which are irrelevant for a specific dashboard context. Today, no way to restrict.

### Impact

- Cluttered picker on entities with many savedqueries
- Blocks maker-controlled UX (operator: "call it 'Email' and only show the Email-relevant views")
- Anti-fix (delete savedqueries) is destructive — affects Dataverse-wide behavior

### Proposed R2 fix

Additive schema field on `SourceSavedQuery`:

```typescript
export interface SourceSavedQuery {
  type: 'savedquery';
  savedQueryId: string;
  availableViews?: string[];      // NEW: optional allowlist of savedquery ids
}
```

Semantics:

- If `availableViews` is set: `<ViewSelector>` filters the `retrieveSavedQueriesForEntity` result to only the listed ids
- If absent: current behavior (all siblings show)
- Backward-compat: no existing record breaks

Runtime: filter in `configResolution.ts` where `availableViews` is loaded — one array intersection call.

### Effort

~1 hour: type field (15 min), filter step in configResolution (15 min), unit test (15 min), regression (15 min).

### Interaction with Issue 3

For the "per-layout view allowlist" use case, Issue 3's `SectionInstance.overrides.availableViews` covers it. For the "global per-widget allowlist" use case, this Issue 5 covers it. Both are useful; ship both.

---

## Issue 6 — Per-instance section rename (P2, folded into Issue 3)

### Symptom

Operator wants "Communications" section to be called "Email" in one layout (email-focused dashboard) and "Communications" in another (multi-purpose dashboard).

### Proposed R2 fix

Covered by Issue 3's `SectionInstance.label` field. No separate work — ships as part of Issue 3.

---

## Issue 7 — Config templates in `scripts/config-templates/` (P2)

### Symptom

The DataGrid framework has ~9 top-level configjson keys (`_version`, `source`, `display`, `filterChips`, `commandBar`, `rowOpen`, `secondaryActions`, `columns`, `behavior`) with 30+ nested fields. Makers authoring a new config today either:

- Copy from an existing record (fine if a similar record exists)
- Read the schema TypeScript file (`DataGridConfiguration.ts`, 400+ lines)
- Read the guide (`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`) — recently updated with a full annotated template

The annotated template in the guide (added 2026-07-01) IS a good starting point, but it's inside a large doc. A dedicated `scripts/config-templates/` folder would give makers a smaller, cleaner starting point.

### Proposed R2 fix

Create three template files:

- `scripts/config-templates/entity-list-basic.json` — minimum viable for an entity-list workspace widget (source + display + rowOpen + behavior.pageSize)
- `scripts/config-templates/entity-list-drill-through.json` — parent-context filter + explicit column overrides + secondaryActions
- `scripts/config-templates/entity-list-full.json` — every field with sensible defaults + `$comment` for each

Each template is a valid `sprk_configjson` payload — a maker copies, replaces the source IDs, and imports via MCP or the maker portal.

Update `DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md § Step 2` to reference the templates as the recommended starting point.

### Effort

~30 min: 3 files × 10 min each + guide update (5 min).

---

## Issue 8 — Framework `pageSize` default alignment (P2, doc drift)

### Symptom

`DataGridConfiguration.ts:329` comment says `Default 50 per design.md`. Runtime code (`DataGrid.tsx:847`) uses `?? 100`. Discrepancy causes maker confusion and inconsistent expectations.

### Options

- **Option A (align docs to code)**: update the comment to `Default 100`, add a note that "50 was the original spec but the framework increased the effective default during implementation"
- **Option B (align code to docs)**: change `?? 100` → `?? 50`. Reduces default page-2 fetches — but might cause more IntersectionObserver-triggered fetches for grids that today happily load all rows on page 1
- **Option C (make the default context-aware)**: framework picks based on whether the DataGrid is mounted with a `parentContext` (drill-through, likely fewer records → 100 OK) vs standalone (workspace widget, likely more records → 25 is better default)

Recommendation: **Option A now** (fix the doc), consider Option C as a separate future enhancement.

### Effort

~15 min: comment update in `DataGridConfiguration.ts` + note in the guide.

---

## Issue 9 — Unwind ai-spaarke-ai-workspace-UI-r2 tactical `maxHeight` hack (P3)

### Symptom

The tactical fix deployed during ai-spaarke-ai-workspace-UI-r2 follow-up (2026-07-01) sets `maxHeight: '480px'` directly in the 6 entity-list section registration files. This is a section-level hack — the framework's sizing model still doesn't know about clamped widgets.

### Impact

- Full-page single-section layouts (like the new "Documents" system layout) waste vertical space (capped at 480px)
- Section registrations now know about pixel values, violating the "framework decides sizing, sections declare intent" principle
- Any future single-section deployment inherits the wasted space until the maker adds an explicit override

### Proposed R2 fix

After Issue 1 (framework `contentSizing`) ships:

1. Update `sectionMetadataCatalog.ts` — add `contentSizing: 'clamped'` to the 6 entity-list metadata entries
2. Remove the `maxHeight` + `display: flex` additions from the 6 registration files' `style` blocks
3. Redeploy LegalWorkspace

After Issue 2 (per-row height override) ships:

4. Update `system-layouts.json` — for the single-section "Documents" / "Projects" / "Invoices" / "Work Assignments" / "Communications" layouts, set `rowHeight: '80vh'` or similar so they use most of the tab height
5. Redeploy system layouts via `Deploy-SystemWorkspaceLayouts.ps1 -Force`

### Effort

~30 min: metadata + registration cleanup + layout JSON updates.

---

## Issue 10 — Scrollbar UX polish (P3, deferred by decision)

### Discussion

During ai-spaarke-ai-workspace-UI-r2 follow-up, the operator asked about hiding the scrollbar ("cluttered UI"). The recommendation was: **keep the Fluent v9 native scrollbar visible** because:

- Modern data-grid UX conventions require visible scrollbars for discoverability ("is there more?", "how much more?")
- Fluent v9's native scrollbar is already thin and unobtrusive
- Hiding scrollbars is appropriate for marketing / conversational / chat surfaces, NOT for data grids

### R2 decision

**No work planned.** This item is captured to prevent re-litigation. If a future project genuinely needs a hide-scrollbar option (rare use case), it can be added as an opt-in `behavior.hideScrollbar: boolean` flag.

### Effort

0 (decision to defer).

---

## Issue 12 — SpaarkeAi build-time alias for LegalWorkspace source (P2, operational gotcha)

### Symptom

During the ai-spaarke-ai-workspace-UI-r2 follow-up investigation (2026-07-01), a fix to `src/solutions/LegalWorkspace/src/sections/*.registration.ts` was applied and LegalWorkspace was rebuilt + redeployed. But the runtime consumer (`SpaarkeAi`, viewed by the user in the browser) continued serving the OLD bundle with none of the fix visible in the DOM.

Root cause discovered by comparing the built LegalWorkspace HTML (had 6 `maxHeight:"480px"` hits) against the built SpaarkeAi HTML (had 0 hits from the new code, 1 pre-existing unrelated hit).

### Root cause

`src/solutions/SpaarkeAi/vite.config.ts` includes:

```typescript
resolve: {
  alias: {
    "@spaarke/legal-workspace/src": path.resolve(__dirname, "../LegalWorkspace/src"),
    "@spaarke/legal-workspace":     path.resolve(__dirname, "../LegalWorkspace/src"),
  }
}
```

This is a Vite alias — at SpaarkeAi's BUILD time, imports of `@spaarke/legal-workspace` resolve directly to `../LegalWorkspace/src/**` and Vite bundles that source into SpaarkeAi's output. There is **no npm dependency on LegalWorkspace**; there is **no built `dist/` from LegalWorkspace shared**.

Consequence:
- Deploying `LegalWorkspace` (as `sprk_corporateworkspace`) updates the STANDALONE LegalWorkspace code page only
- SpaarkeAi (as `sprk_spaarkeai`) keeps whatever code it bundled at its own last build
- Section registration edits require BOTH code pages to be rebuilt + redeployed for the change to reach every consumer

### Impact

- Operational: any future edit under `src/solutions/LegalWorkspace/src/**` must be paired with a SpaarkeAi rebuild + redeploy. Deploying LegalWorkspace alone is a NO-OP for SpaarkeAi consumers (which is the majority use case).
- Debug time: during the 2026-07-01 investigation, ~30 minutes of DOM diagnostics were spent chasing the "why doesn't my fix show up?" question before checking whether the bundle contained the change.
- Future incidents: any developer touching section registrations without knowing this alias exists will hit the same trap.

### Options

- **Option A** — Add a warning to `code-page-deploy` skill + `BUILD-A-NEW-WORKSPACE-WIDGET.md` making the SpaarkeAi dual-deploy requirement visible. Low-cost documentation fix.
- **Option B** — Extract LegalWorkspace's section registry into a proper shared package (`@spaarke/legal-workspace` in `src/client/shared/`). SpaarkeAi consumes the built shared package via a normal file: dependency. Then deploying LegalWorkspace becomes moot for SpaarkeAi — SpaarkeAi's build picks up whatever the shared package published.
- **Option C** — Add a preflight check to `Deploy-CorporateWorkspace.ps1` / `Deploy-AllDataGridConsumers.ps1` that WARNS if SpaarkeAi's build timestamp is older than the section source files. Non-blocking warning + suggestion to also deploy SpaarkeAi.

### Recommendation

**Option A now** (~1 hour: warning in the skill + guide reference), **Option B as follow-on architectural work** (~1 day: proper shared library extraction; touches all workspace-hosting code pages).

### Effort

- Option A: ~1 hour
- Option B: ~1 day (architectural change; requires broader review)
- Option C: ~30 min

---

## Issue 11 — Better default `pageSize` (P3)

### Discussion

Framework default is 100. During ai-spaarke-ai-workspace-UI-r2 follow-up, all 6 entity-list workspace widgets had their `behavior.pageSize` explicitly set to 25 because 100 caused UX problems (page-1 fetched all records → no scroll needed → lazy load never triggered).

If workspace-embedded widgets are the majority consumer today, arguably the framework default should be 25 or 50.

### Options

- Keep 100 (safer for drill-through / full-page consumers where fewer round trips matter)
- Change to 50 (compromise; workspace widgets still need explicit override for 25)
- Change to 25 (matches the majority use case; drill-through / full-page consumers now need explicit override for 100)

### Recommendation

**Discuss with reviewer**. If Issue 8 Option C (context-aware default) ships, this becomes moot — the framework picks per-consumer. If not, pick 50 as the compromise.

### Effort

~15 min if we change; 0 if we keep 100. Reviewer discussion needed.

---

## Deployment strategy

R2 ships as **2 phased PRs**:

### PR 1 — Framework changes + tactical hack removal

Contents:
- Issue 1: `contentSizing` field + `buildDynamicWorkspaceConfig` wiring
- Issue 5: `availableViews` field + `configResolution` filtering
- Issue 8: doc alignment for `pageSize` default
- Issue 9: unwind tactical `maxHeight` hack in 6 sections + add `contentSizing: 'clamped'` in metadata catalog

Deploy scope:
- Shared libs rebuild (Spaarke.UI.Components, Spaarke.AI.Widgets)
- LegalWorkspace redeploy
- SpaarkeAi redeploy (indirect via shared libs)

### PR 2 — Wizard + per-instance overrides

Contents:
- Issue 2: `rowHeight` in LayoutJsonRow + WorkspaceLayoutWizard UI
- Issue 3: `SectionInstance` schema + framework wiring + wizard "Advanced" panel
- Issue 4: `widthPreference` on SectionMetadata + wizard validation
- Issue 7: config templates
- Issue 11: pageSize default decision (if changed)

Deploy scope:
- Same as PR 1 + WorkspaceLayoutWizard redeploy

Each PR is independently mergeable + reversible. PR 2 depends on PR 1 for the schema foundation.

---

## Success criteria

Numbered acceptance conditions:

1. **[Issue 1]** Framework `SectionMetadata` supports `contentSizing: 'grow' | 'clamped'`; `buildDynamicWorkspaceConfig` applies `defaultHeight` as `max-height` when `'clamped'`; type test + regression on the 6 entity-list widgets in Dashboard II shows visible scrollbar + lazy-load-on-scroll behavior.
2. **[Issue 2]** `LayoutJsonRow` accepts `rowHeight?: string`; the wizard exposes a UI for setting it; a row with `rowHeight: '80vh'` renders at 80% viewport height; the Documents system layout benefits from the change without touching the section metadata.
3. **[Issue 3]** `LayoutJsonRow.sections` accepts both `string` and `SectionInstance` shapes; setting `configIdOverride` in a section instance renders a different config record than the section's baked-in default; wizard "Advanced" panel exposes the override.
4. **[Issue 4]** `SectionMetadata.widthPreference` field exists; wizard blocks/warns on invalid placements; runtime dev-guard logs a warning for `'full'`-preferred sections in multi-column rows.
5. **[Issue 5]** `SourceSavedQuery.availableViews` field filters the `<ViewSelector>` dropdown; when absent, all siblings show (back-compat verified).
6. **[Issue 6]** Section rename in a layout instance renders in the section header and picker chip; the underlying metadata `label` is unchanged.
7. **[Issue 7]** Three config template files exist in `scripts/config-templates/`; guide § Step 2 references them.
8. **[Issue 8]** Doc drift on `pageSize` default resolved; comment matches runtime.
9. **[Issue 9]** All 6 entity-list section registration files have their tactical `maxHeight` removed; metadata catalog entries have `contentSizing: 'clamped'`; single-section full-page layouts (like Documents) no longer waste vertical space.
10. **[Issue 10]** Documented decision to keep scrollbars; no code change.
11. **[Issue 11]** `pageSize` default decision documented; changed OR kept intentionally.

---

## Non-goals

- Not adding an editable-cells feature (out of scope; separate follow-on)
- Not adding server-side aggregations (out of scope; use BFF endpoints or savedquery rollups)
- Not migrating legacy `IGridConfigJson` consumers (that retirement is on the R1 backlog under `spaarke-datagrid-framework-r1/notes/phase-f-legacy-retirement.md` or similar; not R2's problem)
- Not changing the `sprk_configjson` `_version` — still `'1.0'`, additive additions only

---

## Dependencies

- **`spaarke-datagrid-framework-r1`** — R2 builds on the R1 framework; no schema-breaking changes
- **`ai-spaarke-ai-workspace-UI-r2`** — the 6 entity-list widgets shipped during R2 (specifically Communications) are the primary regression surface for R2's Issue 1 + Issue 9. Any Issue 1 change must verify all 6 widgets still work.
- **`ai-spaarke-ai-workspace-UI-r2` follow-up PR #531** — the 4 new System Workspace Layouts (Communications, Projects, Invoices, Work Assignments) are the primary regression surface for Issue 2 + Issue 9. Any single-section full-page fix must verify these 4 layouts + the pre-existing "Documents" system layout render correctly.

---

## References

- **Discovery session artifacts** — `projects/ai-spaarke-ai-workspace-UI-r2/notes/` (config record audit, phase1-verification, communications config record, smart-todo-modal-callsites)
- **Framework R1 project** — `projects/spaarke-datagrid-framework-r1/` (contains original design + shipped state)
- **Architecture doc** — [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) (R2 updates should extend § 6.5 with the new fields)
- **Configuration guide** — [`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../../docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md) (updated 2026-07-01 with full field reference; R2 adds new fields to Step 5 subsections)
- **Widget authoring guide** — [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — § 7.2 (Height chain) is the load-bearing prior art for Issue 1; R2 updates should call out `contentSizing` as the framework-level replacement for per-widget height hacks
- **Height-chain diagnostic script** — captured in the widget authoring guide § 7.2.4; also embedded in the operator's DevTools workflow during discovery

---

## ADR touchpoints

- **ADR-012 (shared component library)** — framework changes stay in `@spaarke/ui-components`; no PCF-specific dependencies; consumers (LegalWorkspace, SpaarkeAi, wizards) redeploy independently
- **ADR-021 (Fluent UI v9)** — no v8 imports; Fluent v9 native scrollbar retained (Issue 10)
- **ADR-022 (React 19)** — framework code stays React-16-safe (PCF consumer compatibility)
- **ADR-028 (Spaarke Auth v2)** — no auth surface touched; `Xrm.WebApi` via `XrmDataverseClient` continues
- **ADR-038 (testing strategy)** — new tests for `buildDynamicWorkspaceConfig` (Issue 1 + 2), `configResolution` (Issue 5), `WorkspaceLayoutWizard` UI (Issues 2 + 3 + 4). All MAINTAIN-class (framework-contract testing per the 6 KEEP categories)

---

## Next step

Run `/design-to-spec projects/spaarke-dataset-grid-framework-r2/design.md` to produce `spec.md`, then `/project-pipeline` for `plan.md` + `CLAUDE.md` + `tasks/`.
