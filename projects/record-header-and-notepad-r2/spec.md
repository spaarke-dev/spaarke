# Configurable Record Header (R2) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-24
> **Source**: [`design.md`](design.md) (re-scoped 2026-08-21 · code-verified 2026-08-22 · schema-verified against `spaarkedev1` 2026-08-24)
> **Project ID**: `record-header-and-notepad-r2` (folder retained for R1 cross-link continuity; the deliverable is no longer Notepad work)

---

## Executive Summary

Replace the withdrawn "one thin PCF per entity" plan with **ONE configuration-driven `Spaarke.Records.RecordHeader` PCF** that works on any entity's main form. The title area and toolbar (AI summary · To Do · Notepad) are identical everywhere; the field payload and its placement come from a JSON layout on a manifest property, with metadata-derived defaults when no JSON is supplied. Adding the header to a new entity becomes a form edit — no code, no build, no new solution.

R2 rolls the control out to Project, Work Assignment, Invoice and Event, then migrates Matter off `MatterHeaderPcf` last as the strongest regression test.

---

## Scope

### In Scope

- **One PCF**: `Spaarke.Records.RecordHeader`, generalized from `MatterHeader`, driven by a `layoutJson` manifest property
- **Shared-library renderers**: `DateField` (date + datetime modes), `NumberField` (incl. Money), `BooleanField`; `OptionSetField` extended with edit mode
- **Shared-library machinery hoist**: form-buffer staging, pending-changes buffer, lookup projection + OData search, unified `getXrmPage()`
- **Config resolver**: `resolveHeaderConfig` — pure, tiered, non-throwing
- **Metadata access**: extend the existing `IDataverseClient` contract with lookup `targets` + a page-session cache
- **Summary-field standardization** on `sprk_recordsummary` + remediation of two live breakages from the deleted `sprk_mattersummary` / `sprk_aisummary` columns (FR-22, FR-23)
- **OOB lookup picker** via `Xrm.Utility.lookupObjects`, retiring the custom inline type-ahead (FR-15a)
- **Entity rollout**: Project + Work Assignment (wave 1) → Invoice + Event (wave 2) → **Agreement** (wave 3) → Matter migrated last
- **Control migration**: new `RecordHeaderPcf` solution, Matter form re-bind, `MatterHeaderPcf` retired on delivery
- **Documentation**: rewrite `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` from shipped code; refresh `.claude/patterns/ui/record-header-composition.md`

### Out of Scope

- A `sprk_headerconfiguration` Dataverse table — explicitly rejected (design §5.1.1)
- **DEF-06** (shared-lib `exports` field + `moduleResolution: bundler`) — dropped; standalone migration project
- **DEF-08** (`useSprkMemoRepository` promotion) — dropped; no second consumer
- Any BFF surface. The sparkle **refresh** icon stays unwired (R1 FR-08a / NFR-07)
- **Populating** the summary columns — R2 creates them and renders "No summary yet"; a separate project writes them
- VisualHost `CardChrome` (DEF-03) and EventDetailSidePane `MemoSection` (DEF-04)
- Changes to the Notepad or SmartTodo code pages
- **The seven schema-drift defects** found during verification — standalone issue docs in [`notes/issues/`](notes/issues/README.md)
- Required-marker (`*`) on non-text renderers — see D-10 / FR-11

### Affected Areas

| Path | Change |
|---|---|
| `src/client/pcf/MatterHeader/` → `src/client/pcf/RecordHeader/` | Generalized control; new solution identity |
| `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/` | New renderers; `OptionSetField` edit mode + typography |
| `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/RecordHeaderShell.tsx` | Optional `columns` prop for the skeleton |
| `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/` | `resolveHeaderConfig` + `RecordHeaderFields` |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderFields.ts` | New — hoisted machinery |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts` | Slot auto-hide |
| `src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts` · `XrmDataverseClient.ts` | `targets` projection + metadata cache |
| `src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts` | Add `Page` member; shared `getXrmPage()` |
| Dataverse: `sprk_project` · `sprk_workassignment` · `sprk_event` | New `sprk_aisummary` column |
| `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` · `.claude/patterns/ui/record-header-composition.md` | Rewrite / refresh |

---

## Requirements

### Functional Requirements

**Configuration model**

1. **FR-01** — Add a `layoutJson` manifest property (`usage="input"`, not bound) carrying a `RecordHeaderConfiguration` v1.0 JSON layout. *Acceptance*: a maker can paste JSON into the form designer's control-properties panel; the value persists in form XML and survives a solution export/import round-trip.
   - `description-key` MUST contain **no apostrophes** (`pac solution import` fails with `noAposStringType`).
2. **FR-02** — Implement `resolveHeaderConfig(manifestJson, formMetadata) → ResolvedHeaderConfig`: pure, no React, no I/O. *Acceptance*: unit-testable in isolation; every output field fully resolved (no optionals after merge).
3. **FR-03** — Validation mirrors `isValidDataGridConfiguration`: shallow, non-throwing discriminator (`_version === '1.0'` + `Array.isArray(fields)`). *Acceptance*: malformed JSON, wrong `_version`, and absent property each produce `console.warn` + derived defaults. **Never throws. Never renders blank.**
4. **FR-04** — Tier-2 derived defaults: primary name field first (span 2), then up to four further non-system fields in form order, skipping the same `skipSet` as `synthesizeColumnsFromMetadata`. *Acceptance*: the control renders usefully on a form with no `layoutJson` at all.
5. **FR-05** — The resolver MUST clamp `span = min(span, columns)`. *Acceptance*: a `span: 3` field in a `columns: 2` layout renders at span 2, not as an implicit third grid track.

**Renderers**

6. **FR-06** — `DateField` covering `DateTime`/`DateOnly` and `DateTime`/`DateAndTime`, mode selected from the metadata `Format` (one component, not two). *Acceptance*: `sprk_invoicedate` renders as a locale date; `sprk_plannedstart` renders as a locale date **and** time; edit uses a Fluent date picker.
7. **FR-07** — `NumberField` covering `Integer`, `Decimal`, `Double`, `Money`; currency symbol and precision from metadata; right-aligned. *Acceptance*: `sprk_totalamount` renders as currency, not `12500`.
8. **FR-08** — `BooleanField` covering `Boolean`/`TwoOptions`; read = Yes/No label, edit = Fluent `Switch`. *Acceptance*: `sprk_highpriority` renders as Yes/No, not `true`/`false`.
9. **FR-09** — Extend `OptionSetField` with edit mode via Fluent `Dropdown` fed by `getOptions()`, and align its label typography to `fontSizeBase300` / `colorNeutralForeground1`. *Acceptance*: `sprk_invoicestatus` is editable; its label matches sibling renderers in a mixed grid.
10. **FR-10** — Every new renderer follows the `TextField` contract verbatim: props `{ label, value, span, required?, onSave?, disabled? }`; renderer applies its own `gridColumn`; `editable = typeof onSave === 'function' && disabled !== true`; draft/commit/**revert-on-reject-and-stay-in-edit**; Enter commits / Escape cancels; blur commits; `filled-lighter` + `size="small"` + tiny `Spinner`. *Acceptance*: contract test suite passes identically against every renderer.
11. **FR-11** — Em-dash (`—`) for empty values **including empty string**, across all renderers; align `TextField`, which today renders `''` as an empty box. *Acceptance*: `null`, `undefined` and `''` all render `—`.
   - **Explicitly NOT in scope (D-10)**: the required `*` marker stays TextField-only. Required-ness is therefore invisible on date / number / option-set / boolean / lookup cells. Dataverse still enforces on save.

**Control behaviour**

12. **FR-12** — Self-detect entity and record from `context.mode.contextInfo.entityTypeName` / `.entityId`, falling back to `context.page.*`. *Acceptance*: no entity name is compiled into the control; both surfaces require the established type-cast idiom.
13. **FR-13** — Preserve R1's form-buffer dirty-state pattern **exactly**: edits stage via `getXrmPage().getAttribute(n).setValue(v)`; the pending buffer resolves display as `pendingX[name] ?? values?.[name]`; buffers reset on `recordId` change. *Acceptance*: editing a field makes the form dirty with **no PCF re-render or loading flash**; the form's own Save commits.
14. **FR-14** — Unify the missing-attribute path: **both** text and lookup saves throw `Field '…' not on form`. Today `saveLookup` warns and silently no-ops. *Acceptance*: a `layoutJson` naming a field absent from the form fails loudly on edit, never silently drops the user's input.
15. **FR-15** — Resolve lookup targets from metadata, deleting `LOOKUP_META`. *Acceptance*: Matter Type and Practice Area render and resolve correctly with zero hard-coded target/id/name constants.
15a. **FR-15a** *(added 2026-08-25)* — **Editable lookups use the OOB picker**: the cell displays the current value and opens `Xrm.Utility.lookupObjects({ entityTypes: [Targets[0]], allowMultiSelect: false })` on click. The returned `{ id, name, entityType }` is already the form-buffer `setValue` payload. Retire the custom inline type-ahead from the header path and **delete the custom OData lookup-search builder**. *Acceptance*: clicking a lookup cell opens the native picker with Records / Recent records, **+ New** and **Advanced**; selection stages to the form buffer; read-only lookups still render via the display-only `fields/LookupField`.
   - **"+ New" behaviour is NOT controllable from the PCF.** It opens a quick-create **flyout** only when the *target* entity has `IsQuickCreateEnabled = true` **and** a published Quick Create form; otherwise it navigates away. Live status (2026-08-25): only `sprk_matter` qualifies. `contact` has the form but the flag is off. The five `*_ref` taxonomy tables have neither. **Out of R2 scope** — these are Dataverse config changes on tables R2 does not touch. Recommended disposition in design §6.5: flip the flag for `contact`; deliberately leave the taxonomy tables off so users cannot mint new type values from a header lookup.
16. **FR-16** — Toolbar slot auto-hide: omit the Notepad slot when `buildMemoFilterForParent` returns `null`, and the To Do slot when `buildTodoFilterForParent` returns `null`. *Acceptance*: on an entity outside the respective map, the icon does not render at all (rather than opening a surface that cannot save).
17. **FR-17** — Sparkle visibility keys on **attribute existence in metadata, not value population**. *Acceptance*: sparkle shows when `summaryField` names an existing attribute even at 0 populated records; popover renders an explicit **"No summary yet"** state; sparkle hidden when `summaryField` is absent or names a non-existent attribute.
18. **FR-18** — `RecordHeaderShell` gains an optional `columns` prop (default `3`) driving the loading skeleton. *Acceptance*: a `columns: 2` header shows a 2-column skeleton during load.

**Shared-library structure**

19. **FR-19** — Hoist from `MatterHeaderView.tsx` into the shared library: form-buffer staging, pending buffer + display resolution, `projectLookup()`, the OData lookup-search builder generalized over metadata, and the lookup grid-cell wrapper. Proposed home `hooks/useRecordHeaderFields.ts` + `components/RecordHeader/RecordHeaderFields.tsx`. *Acceptance*: the PCF view contains no save/search machinery; `MatterHeaderHost`-equivalent theme/host code stays in the PCF layer.
20. **FR-20** — Land **one** shared `getXrmPage()` and migrate both existing duplicates (`MatterHeaderView`, `FieldMappingHandler`); add the `Page` member to `utils/xrmContext.ts`'s `XrmContext`. *Acceptance*: exactly one `getXrmPage` implementation in `src/`.
21. **FR-21** — Extend `EntityAttributeMetadata` with `targets?: string[]` and project it in `XrmDataverseClient.projectAttribute`; add a module-level, page-session metadata cache keyed by entity logical name. *Acceptance*: repeated `retrieveEntityMetadata` calls for the same entity issue no additional network requests within a page session; DataGrid inherits both changes without modification.

**Dataverse schema**

22. **FR-22** *(rewritten 2026-08-25)* — **Standardize on `sprk_recordsummary`.** The owner has already created the column on all six entities, so R2 does **no schema creation**. Instead R2 (a) defaults `summaryField` to the existing shared constant `RECORDSUMMARY_FIELD` (`toolbarLaunchDefaults.ts:90`), and (b) remediates residual references to the now-deleted `sprk_mattersummary` / `sprk_aisummary`. *Acceptance*: no source file references either deleted column; the sparkle reads `sprk_recordsummary` on every entity.
23. **FR-23** *(new 2026-08-25)* — **Fix the two live breakages caused by the column deletions**:
   - **RS-1** — `MatterHeaderView.tsx:83` puts `sprk_mattersummary` in its `$select`, so the **shipped v1.0.20 Matter header returns HTTP 400 and fails to load entirely**. R2 fixes it by construction; evaluate a v1.0.21 hotfix if R2 will not ship soon. *Acceptance*: Matter's header loads.
   - **RS-2** — the `sprk_aitopicregistry` row **"Matter Summary"** (`sprk_topicname=matter-summary`) is enabled with `sprk_targetfield=sprk_mattersummary`, a column that no longer exists, so the BFF OutputRouter `work_product` leg writes to nothing. **Dataverse data fix**: set `sprk_targetfield` = `sprk_recordsummary`. *Acceptance*: the registry row targets an existing column.
   - *Verified NOT broken, do not re-investigate*: the sibling row "Matter Health Insight" → `sprk_performancesummary` (still exists on Matter, as do `sprk_financialsummary` / `sprk_tasksummary`); `InvoiceExtractionJobHandler.cs:384` names `sprk_aisummary` **only in a comment** — the value goes to context variable `extraction.aiSummary` (`:236`), so fix the comment and confirm the consuming mapping targets `sprk_recordsummary`.
24. **FR-24** *(new 2026-08-25)* — Add `sprk_agreement → sprk_regardingagreement` to **both** `SUPPORTED_TODO_PARENTS` (11 → 12) and `SUPPORTED_MEMO_PARENTS` (6 → 7). Both lookups now exist on `sprk_todo` and `sprk_memo` (live-verified). *Acceptance*: Agreement's header shows working To Do and Notepad badges.

**Rollout + migration**

25. **FR-25** — Bind the control with the confirmed layout on each entity's **main** form — never the legacy "Information" form. Live-verified targets: Matter `4fa382f2-…` · Project `5aa00242-…` · Work Assignment `7e578eef-…` · Invoice `93aa1c69-…` · Event `eaf22dcb-…` ("Event main form" only, of 10) · **Agreement `59d88274-a1a0-f111-aaac-000d3a99d1d7`** ("Agreement main form", created 2026-08-25). *Acceptance*: per-form criteria below, met on all **six**.
26. **FR-26** — Ship `Spaarke.Records.RecordHeader` in a new `RecordHeaderPcf` solution; re-bind the Matter form; verify parity vs v1.0.20; then retire `MatterHeaderPcf` in two ordered steps (remove all form references + publish, then delete the CustomControl). *Acceptance*: Matter renders identically **except that lookup cells now open the OOB picker** (FR-15a — an intended change, not a regression); the old control is gone on delivery.
27. **FR-27** — Rewrite `RECORD-HEADER-PCF-AUTHORING-GUIDE.md` **from shipped code**, not by diffing the old one (it has already drifted from v1.0.20). Preserve the bundle-optimization triad section; correct "4 version locations" → 5. *Acceptance*: no reference to the retired per-entity recipe survives.

### Non-Functional Requirements

- **NFR-01** — TTI **≤300 ms warm / ≤800 ms cold** (restored from R1). R2 adds pre-render metadata calls, so this MUST be measured per wave, not assumed.
- **NFR-02** — Bundle **≤250 KB minified**. R1 shipped 62.4 KiB (63,812 bytes). Measure per wave, not once at the end.
- **NFR-03** — Fluent v9 semantic tokens exclusively; zero hex/rgb/hsl (ADR-021). Dark and high-contrast supported.
- **NFR-04** — Shared components React 16/17-safe: no `use()`, no `useSyncExternalStore`, no `createRoot` (ADR-022).
- **NFR-05** — All Dataverse I/O via `Xrm.WebApi` / `Xrm.Page`. No `@spaarke/auth`, no raw `fetch` to the Dataverse API.
- **NFR-06** — No endpoint, service, DI registration, or package added to `src/server/api/Sprk.Bff.Api/**`.
- **NFR-07** — Notepad + SmartTodo launch contracts byte-identical: `regardingEntity`/`regardingId` and `action=openTodos&regardingType=…&regardingId=…` are external API.
- **NFR-08** — Bundle-optimization triad intact: `featureconfig.json` + `webpack.config.js` + deep-path `@spaarke/ui-components/dist/*` imports. Do not "clean up" the deep paths.
- **NFR-09** — Cross-environment portability: no literal record GUIDs, environment names, tenant/subscription ids, or user ids in any shipped artifact. No `window.SPAARKE_*`, no build-time-inlined `.env`.
- **NFR-10** — Graceful degradation: malformed or absent `layoutJson` never blanks a form and never throws.
- **NFR-11** — `ensure-dist-fresh` `prebuild`/`prebuild:prod` guard wired in the new PCF folder (binding since 2026-07-07).

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-006** | PCF for form-bound UI |
| **ADR-012** | Shared component library is the home for reusable primitives |
| **ADR-021** | Fluent v9 semantic tokens only |
| **ADR-022** | React 16/17-safe shared components (PCF platform libraries) |
| **ADR-024** | Polymorphic resolver pattern — `sprk_memo` Path C |
| **ADR-038** | Testing strategy; `resolveHeaderConfig` is a pure function → unit tests are a KEEP category |
| **ADR-020** | Versioning — PCF version sync across **5** locations |
| **ADR-028** | **N/A** — host-context `Xrm` only, no BFF, no `@spaarke/auth` |
| **ADR-011** | Cited only to correct a misreading: it contains no "typed components > runtime schemas" rule. Its actual MUSTs ("reuse shared components", "MUST NOT duplicate UI primitives") support this design |

### MUST Rules

- ✅ MUST use `Xrm.WebApi` / `Xrm.Page` for all Dataverse I/O
- ✅ MUST preserve R1's form-buffer dirty-state pattern exactly (writing straight to Dataverse re-rendered the whole PCF — R1 v1.0.7)
- ✅ MUST keep the bundle-optimization triad intact
- ✅ MUST degrade gracefully on bad config (`console.warn` + derived defaults)
- ✅ MUST clamp `span ≤ columns` in the resolver — `FieldGrid` does not validate
- ✅ MUST call `Xrm.Navigation.navigateTo` **directly** on `xrm.Navigation` (aliasing strips `this` → silent no-op); property is `webresourceName`; `data` is a URL-encoded **string**
- ❌ MUST NOT add anything to `src/server/api/Sprk.Bff.Api/**`
- ❌ MUST NOT wire the sparkle refresh icon (DEF-01)
- ❌ MUST NOT create a `sprk_headerconfiguration` table
- ❌ MUST NOT do the DEF-06 `exports` migration, or "clean up" deep-path imports
- ❌ MUST NOT modify `src/client/pcf/VisualHost/**` or `src/solutions/EventDetailSidePane/**`
- ❌ MUST NOT fork any R1 shared primitive — missing behaviour lands in the shared lib
- ❌ MUST NOT put apostrophes in manifest string attributes

### Existing Patterns to Follow

- `components/DataGrid/configResolution.ts` — the proven tiered-resolver shape (mirror structure **and** test approach)
- `types/DataGridConfiguration.ts:479` `isValidDataGridConfiguration` — shallow non-throwing guard
- `RecordHeader/fields/TextField.tsx` — canonical renderer contract
- `services/XrmDataverseClient.ts` — metadata access; extend, don't replace
- `.claude/patterns/pcf/pcf-build-scaffold.md` — 10 build/runtime gotchas from R1 UAT
- `.claude/patterns/pcf/xrm-webapi-related-count.md` — `Xrm.WebApi` strips `@odata.count`

### Known traps (all cost R1 a release)

- `MatterHeaderView.tsx:53-62` claims the shared lib has an `exports` map. **It does not** — stale comment from the reverted task 063. Delete during migration.
- Two different `LookupField` components exist; the **editable** one is top-level and has **no `span` prop**.
- `context.mode.contextInfo` and `context.page` are both absent from `@types/powerapps-component-framework`.
- Fluent portals mount outside the PCF subtree — the manual `FluentProvider` wrap is required; platform-library theming does not reach them.

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

No BFF surface, no SpaarkeAi widgets, no workflow changes. DEF-06 is out of scope, so the `pcf-scripts` ripple that would have made `ci-workflows` a Y is gone. Docs touched are a guide and a pattern pointer — neither is a skill directive.

### New Components (§11 three-question gate)

| New component | Existing overlap (verified) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `RecordHeader` control | `MatterHeader` PCF | **This IS the extension** — same control generalized, old one retired | Four more PCF solutions to version, deploy, bind and fix in parallel; ~82 lines of machinery duplicated 4× |
| `DateField` | `TextField` | No — `TextField` does `String(value)` (`:117`) | `sprk_invoicedate` renders `2026-08-21T00:00:00Z` |
| `NumberField` | `TextField` | No — cannot format Money by currency/precision | `sprk_totalamount` renders `12500` |
| `BooleanField` | `TextField` | No — would render `true`/`false` | `sprk_highpriority` (in all four confirmed layouts) shows as raw `true`/`false` |
| `OptionSetField` edit mode | `OptionSetField` | **Yes — direct extension** | Status becomes read-only on four entities, unlike every other field in the header |
| `layoutJson` property | `title`, `showVersion` | **Yes — same manifest surface** | Field payload stays compiled in; a new entity needs a code change and redeploy |
| `resolveHeaderConfig` | `configResolution.ts` (DataGrid) | No — different domain object (fields/spans vs columns/views); mirrors its structure | Config precedence spreads through the render path untested |
| `useRecordHeaderFields` / `RecordHeaderFields` | none (grep-verified) | n/a | ~82 lines of machinery stay in the PCF and get copied per entity |
| Shared `getXrmPage()` | `FieldMappingHandler.getXrmPage()` + `MatterHeaderView.getXrmPage()` | **Yes — consolidate the two existing duplicates** | A third copy ships; `xrmContext.ts` keeps omitting `Page` |
| Metadata access | `IDataverseClient` / `XrmDataverseClient` | **Yes — extend with `targets` + cache** | A parallel raw-`fetch` path would duplicate a contract, need an NFR-05 carve-out, and leave two code paths to sync |
| **`sprk_aisummary` column** ×3 | Matter `sprk_mattersummary`; Invoice `sprk_aisummary`; Project's 3 specialised summaries | **No** — the column does not exist on those three entities; Project's three are structured insight-card fields, not narrative prose | Owner requires the sparkle on every rollout entity. With no column it can **never** render on Work Assignment or Event — a dead affordance, not an empty one |
| ~~`sprk_headerconfiguration` table~~ | — | **Rejected** — no concrete failure without it | n/a |

---

## ADR Tensions (per CLAUDE.md §6.5)

> **No ADR tensions surfaced at design time.** All listed ADRs apply without exception.

Two points worth recording because they *look* like tensions and are not:

1. **ADR-011 does not block configuration-driven controls.** R1's project CLAUDE.md paraphrased it as "typed components > runtime schemas". [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) contains no such rule; its MUSTs point toward this design, and VisualHost (`sprk_chartdefinition`) + DataGrid (`sprk_gridconfiguration`) are established config-driven precedent.
2. **Hoisting `Xrm.Page` access into the shared library does not violate ADR-012 / `.claude/constraints/pcf.md`.** That rule bars *PCF-specific* APIs in shared components. `Xrm.Page` is a host **form** API, and the shared library already uses it (`FieldMappingHandler.ts:478-512, 552-561`) alongside direct `Xrm.WebApi` calls in `useRecordFieldValues` / `useRelatedCount`. The established split is: components props-driven, Xrm access in `hooks/` and `services/` behind a window/parent walker — exactly where FR-19 puts it.

**Choosing `IDataverseClient` over raw `fetch` (FR-21) avoided a tension** that the earlier draft would have created: `EntityDefinitions/ManyToOneRelationships` is unreachable by `Xrm.WebApi` and would have required an NFR-05 carve-out.

---

## Success Criteria

### Per-form (binding on all five entities)

1. [ ] Renders configured fields at configured spans — *Verify*: visual check against the §9 layout
2. [ ] Inline edit stages to the form buffer, goes dirty, **no re-render flash** — *Verify*: edit a field, observe the form's Save button activate with no skeleton flash; Save persists
3. [ ] Toolbar identical to Matter's: title, sparkle, To Do badge, Notepad badge — *Verify*: side-by-side
4. [ ] To Do opens SmartTodo filtered to this record; Notepad opens scoped to this record — *Verify*: click each; confirm filter and 25%×35% modal
5. [ ] Malformed / absent `layoutJson` degrades to derived defaults — *Verify*: paste `{{{`, then clear the property; header renders both times
6. [ ] A `layoutJson` field not on the form fails loudly — *Verify*: configure an absent field, attempt an edit, observe the throw
7. [ ] Version footer present — *Verify*: visual
8. [ ] Bundle ≤250 KB minified — *Verify*: measure `bundle.js` after `npm run build:prod`
9. [ ] TTI ≤300 ms warm / ≤800 ms cold — *Verify*: browser perf trace, cold and warm

### Renderer-specific

10. [ ] Invoice `sprk_totalamount` renders as currency with correct symbol and precision — *Verify*: visual against the record
11. [ ] Event `sprk_plannedstart` / `sprk_plannedend` render date **and** time — *Verify*: visual
12. [ ] `sprk_highpriority` renders Yes/No and toggles — *Verify*: edit and Save
13. [ ] `sprk_invoicestatus` is editable via dropdown with the correct 2 options — *Verify*: edit and Save
14. [ ] `null`, `undefined` and `''` all render `—` in every renderer — *Verify*: unit tests

### Project-level

15. [ ] Matter renders identically to `MatterHeaderPcf` v1.0.20 **except** that lookup cells open the OOB picker rather than an inline dropdown (intended, FR-15a) — *Verify*: screenshot diff, light and dark, against the pre-change baseline, with the lookup interaction excluded from the diff
15a. [ ] Agreement renders with the full toolbar (sparkle + To Do + Notepad) on "Agreement main form" — *Verify*: seed an Agreement record first; the entity has 0 today
15b. [ ] FR-16 slot auto-hide verified on a To-Do-but-not-Memo parent (`contact`, `sprk_document`, `sprk_organization`, `sprk_analysis` or `sprk_communication`) — *Verify*: Notepad icon absent. **Note**: Agreement is no longer the test case for this, now that both lookups exist
16. [ ] `LOOKUP_META` deleted; Matter Type + Practice Area still render and search — *Verify*: grep for `LOOKUP_META` returns nothing; functional check
17. [ ] `sprk_aisummary` exists on Project / WA / Event and imports into a clean environment — *Verify*: metadata query + fresh-env solution import
18. [ ] Sparkle shows with "No summary yet" on entities whose summary column is empty — *Verify*: click sparkle on a Project record
19. [ ] Exactly one `getXrmPage` implementation in `src/` — *Verify*: grep
20. [ ] `MatterHeaderPcf` control and solution removed — *Verify*: `pac solution list`; control absent from the form designer gallery
21. [ ] Authoring guide contains no reference to the retired per-entity recipe — *Verify*: read
22. [ ] Portability: solution imports into a fresh environment and the header works with no environment-specific config beyond form binding — *Verify*: fresh-env import

---

## Dependencies

### Prerequisites

- `MatterHeaderPcf` v1.0.20 baseline captured (screenshots light + dark, exact 5-field layout + spans, bound field) **before any code change** — it is the parity target
- Maker access to `spaarkedev1` for form binding (5 forms)
- `dataverse-create-schema` for FR-22

### External

- **Summary population** is owned by a separate project. R2 creates the columns and renders "No summary yet"; sparkle content stays empty until that project ships. Not a blocker for any R2 acceptance criterion.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Control identity | Option A (display-name rename, zero migration) or B (new control + re-bind)? | **B** — reaffirmed after the corrected trade was presented | FR-24; new `RecordHeaderPcf` solution + Matter re-bind + retirement |
| Form transport | Do main forms move between environments inside a solution? | **Yes** | §13 portability argument holds; `layoutJson` authored once in dev; re-bind is once, not per-environment |
| Config mechanism | JSON-only vs config record? | **JSON-only on the manifest**; spike is an ergonomics check, not a gate | FR-01; `SingleLine.Text` named as the proven fallback |
| Retirement timing | Retire `MatterHeaderPcf` on delivery or hold a dormant release? | **On delivery** | FR-24; rollback window exists only between re-bind and retirement |
| Metadata path | Reuse `IDataverseClient` or raw-`fetch` `EntityDefinitions`? | **Reuse `IDataverseClient`** | FR-21; avoids an NFR-05 carve-out; DataGrid benefits |
| Field lists | §9 lists were wrong — accept live-verified corrections? | **Yes**, and use MCP/live to resolve | §9 rewritten from `spaarkedev1` |
| Sparkle + summaries | Keep the sparkle given the columns are empty / absent? | **Keep it — and R2 creates the missing columns** | FR-17, FR-22 |
| Per-entity layouts | Confirm proposed layouts? | **Confirmed**, with `sprk_highpriority` added | FR-23; gives `BooleanField` a real consumer |
| Skeleton mismatch | Accept the 3-column flash or pass `columns`? | **Pass `columns`** | FR-18 |
| Renderer conventions | Em-dash `''` everywhere? Required marker on all renderers? | **Em-dash yes; required marker NOT adopted** | FR-11 + its documented consequence |
| Schema-drift defects | Fix in R2 or document separately? | **Document separately**, grouped by area | Out of scope; `notes/issues/` |

## Assumptions

- **`sprk_aisummary` naming** — assuming the uniform Invoice-style name on all three new columns rather than entity-prefixed (`sprk_projectsummary` etc.). Rationale: matches the newest precedent and simplifies both `layoutJson` and the populating project. Matter's `sprk_mattersummary` stays as a pre-existing exception.
- **Column properties** — assuming Memo / 5000 chars / nullable / no default / no form placement, matching Invoice's `sprk_aisummary`.
- **Layouts are defaults, not contracts** — makers may tune them post-ship without a code change; acceptance criteria are written against the shipped defaults.
- **`columns: 3`** for all five entities.
- **PCF version** starts at `1.1.0` for the renamed control (new identity, carrying R1's feature set forward). Confirm at implementation time.

## Unresolved Questions

- [ ] **`layoutJson` editor ergonomics** — does the classic form designer present a usable multi-line editor for a static `of-type="Multiple"` input property, and does the value survive an export/import round-trip without truncation? *Blocks*: nothing in the design — only the manifest `of-type` (fallback `SingleLine.Text` is proven). Run as the first implementation task.
- [ ] **PCF starting version number** — `1.1.0` assumed. *Blocks*: the 5-location version sync in FR-24.
- [ ] **Required-marker gap** — D-10 leaves required-ness invisible on non-text renderers. *Blocks*: nothing; revisit if UAT flags it.

---

*AI-optimized specification. Original design: [`design.md`](design.md).*
