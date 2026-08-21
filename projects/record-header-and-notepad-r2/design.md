# Configurable Record Header — R2

> **Status**: DRAFT — re-scoped 2026-08-21. Supersedes the 2026-07-05 seed (four per-entity PCFs).
> **Project ID**: `record-header-and-notepad-r2` (folder/ID retained for continuity with R1 cross-links; the deliverable is no longer Notepad work)
> **Positioning**: Replace the "one thin PCF per entity" plan with **ONE configuration-driven `RecordHeader` PCF** that works on any entity's main form. Title area + toolbar (AI summary / To Do / Notepad) stay identical everywhere; the field payload and its placement are configured per form via a JSON manifest property.
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-05 · **Re-scoped**: 2026-08-21

<hot-path-declaration>
  <bff>N</bff>
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>

<!--
Pure host-context surface: one PCF + shared-lib additions. No BFF, no SpaarkeAi widgets, no
workflow changes. The 2026-07-05 seed flagged ci-workflows as possibly-Y because of the DEF-06
`exports` migration; DEF-06 is dropped from R2 scope (§7.1), so that risk is gone.
Docs touched: `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` (rewrite) and
`.claude/patterns/ui/record-header-composition.md` (pointer refresh) — neither is a skill directive.
-->

---

## 1. Purpose

R1 shipped `MatterHeaderPcf` v1.0.20 and, more importantly, shipped **the primitives underneath it as entity-agnostic shared code**. The 2026-07-05 R2 seed proposed cloning the Matter PCF four more times. That plan is withdrawn. R2 instead **generalizes the existing control** so a single deployed component serves Matter, Project, Work Assignment, Invoice, Event, and anything after them.

The re-scope rests on three findings from the 2026-08-21 code review of [`src/client/pcf/MatterHeader/`](../../src/client/pcf/MatterHeader/):

1. **The toolbar is already generic.** [`useRecordHeaderToolbarActions`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts) takes `{ entity, recordId, title }` and resolves To Do / Memo badges from the [`SUPPORTED_TODO_PARENTS` (11 entities) / `SUPPORTED_MEMO_PARENTS` (6 entities)](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts) maps. The "same toolbar on every entity" requirement needs **zero** new work.
2. **The manifest is already generic.** `boundField` is documented as "any SingleLine.Text field on the host entity" and the record id comes from `context.mode.contextInfo.entityId`. Nothing in [`ControlManifest.Input.xml`](../../src/client/pcf/MatterHeader/control/ControlManifest.Input.xml) is Matter-specific.
3. **Only ~40 of `MatterHeaderView.tsx`'s 326 lines are configuration.** `ENTITY`, the `FIELDS` array, `LOOKUP_META`, the JSX layout, and the summary field name. The other ~180 lines — form-buffer staging, the pending-changes buffer, `projectLookup()`, the OData lookup-search builder — are **generic machinery that the withdrawn plan would have copy-pasted four times**.

---

## 2. Product statement

A maker adds **one** component — "Spaarke Record Header" — to any entity's main form, pastes a small JSON layout into its property, and gets: the record's key fields laid out in a grid with inline editing, plus the standard toolbar (AI summary sparkle, related To Dos with live count, Notepad with live count). With no JSON at all, the control still renders a sensible default derived from the form. Adding the header to a new entity requires **no code, no build, no new solution** — only a form edit.

---

## 3. Scope

### 3.1 In scope

**One PCF**: `Spaarke.Records.RecordHeader` — generalized from `MatterHeader`, driven by a `layoutJson` manifest property with metadata-derived fallbacks (§5).

**Entity rollout** (owner decision 2026-08-21):

| Wave | Entity | Why this order |
|---|---|---|
| 1 | `sprk_project` | Text / lookup / option-set only — covered by existing renderers |
| 1 | `sprk_workassignment` | Same shape; validates the config path on a second entity |
| 2 | `sprk_invoice` | **Forces the currency + date renderer work** (§6). Explicitly required, not optional. |
| 2 | `sprk_event` | Forces datetime rendering |
| — | `sprk_matter` | Migrated from `MatterHeaderPcf` to the generic control (§8); must render pixel-identically |

**Shared-library additions** (§6): date/datetime, number/currency, and boolean field renderers; editable option-set; metadata-driven lookup resolution; the config resolver.

**Documentation**: rewrite [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) from "how to write a new per-entity PCF" to "how to configure the header for a new entity" — the old recipe becomes actively wrong the day this ships. Refresh [`.claude/patterns/ui/record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) accordingly.

### 3.2 Out of scope

- **A `sprk_headerconfiguration` Dataverse table.** Explicitly rejected — see §5.4.
- **DEF-06** (shared-lib `exports` field + `moduleResolution: bundler`) — dropped from R2; see §7.1.
- **DEF-08** (`useSprkMemoRepository` promotion) — dropped from R2; see §7.2.
- Any BFF surface. The sparkle refresh icon stays unwired (R1 FR-08a / NFR-07 continue to hold).
- VisualHost `CardChrome` migration (DEF-03) and EventDetailSidePane `MemoSection` (DEF-04) — remain in-code pointers.
- Changes to the Notepad or SmartTodo code pages. Both are already entity-agnostic and this project launches them with the same URL contracts R1 established (`regardingEntity`/`regardingId`; `action=openTodos&regardingType=…&regardingId=…`) — **NFR-09 external-API status unchanged**.

### 3.3 Natural boundary

Entities beyond the five above need no project — they need a form edit. That is the point of R2.

---

## 4. What R2 consumes unchanged

All R1 primitives are consumed verbatim. **No forking.** Missing behavior lands in the shared lib, and every entity picks it up at once.

| Primitive | R1 file | Change in R2 |
|---|---|---|
| `HeaderToolbar` | [`components/HeaderToolbar/`](../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/) | None |
| `RecordHeaderShell` | [`RecordHeaderShell.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/RecordHeaderShell.tsx) | None |
| `FieldGrid` | [`FieldGrid.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/FieldGrid.tsx) | None (2/3 columns, span 1–3 is sufficient — see §10 risk) |
| `TextField` / `TextareaField` / `LookupField` | [`RecordHeader/fields/`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields) | None |
| `OptionSetField` | same | **Extended** — gains edit mode (§6.1) |
| `useRecordFieldValues` | [`useRecordFieldValues.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordFieldValues.ts) | None |
| `useRelatedCount` | [`useRelatedCount.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts) | None |
| `useRecordHeaderToolbarActions` | [`useRecordHeaderToolbarActions.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts) | Minor — auto-hide Notepad slot for unsupported parents (§6.4) |
| `AiSummaryPopover` | [`components/AiSummaryPopover/`](../../src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover) | None |
| `themeStorage` | [`utils/themeStorage.ts`](../../src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts) | None |
| Notepad + SmartTodo code pages | [`src/solutions/Notepad/`](../../src/solutions/Notepad) · [`src/solutions/SmartTodo/`](../../src/solutions/SmartTodo) | None |

---

## 5. Configuration model

### 5.1 Mechanism — JSON on the manifest (owner decision 2026-08-21)

A new manifest property carries the layout:

```xml
<property name="layoutJson" display-name-key="Header layout (JSON)"
          description-key="JSON layout for this form's header. Leave blank to derive a default from the form."
          of-type="Multiple" usage="input" required="false" />
```

**Why JSON-on-manifest over a Dataverse config table** (the VisualHost / DataGrid precedent):

| | JSON on manifest (chosen) | `sprk_headerconfiguration` table (rejected) |
|---|---|---|
| Instances to author | One per form — a handful, ever | Same, but with a table's overhead |
| Cross-environment portability | **Config travels inside the form XML** — solution import carries it | Config records must be seeded per environment |
| New Dataverse surface | None | New entity + solution + seed procedure |
| Extra query on form load | None | One per form load (cacheable) |
| Edit without publishing the form | No — accepted trade-off | Yes |

The deciding factor is volume. VisualHost and DataGrid justify a table because makers author *many* configurations across dashboards; a record header is 1:1 with a form section and there will be a handful in total. Per CLAUDE.md §11 cost-of-doing-nothing, nothing concretely fails without the table today.

**Reversibility**: the resolver (§5.3) is a pure function over `(manifestJson, derivedDefaults)`. If a second consumer surface later needs shared layouts (a Code Page record header, a side pane), a config-record tier slots in above `manifestJson` without touching renderers — the same three-tier shape as [`configResolution.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts).

### 5.2 Schema — `RecordHeaderConfiguration` v1.0

```json
{
  "_version": "1.0",
  "title": "Matter",
  "columns": 3,
  "summaryField": "sprk_mattersummary",
  "fields": [
    { "name": "sprk_matternumber",      "span": 1, "required": true },
    { "name": "sprk_mattername",        "span": 2 },
    { "name": "sprk_mattertype",        "span": 1 },
    { "name": "sprk_practicearea",      "span": 1 },
    { "name": "sprk_matterdescription", "span": 3, "maxLines": 10 }
  ]
}
```

| Key | Required | Meaning |
|---|---|---|
| `_version` | yes | Discriminator. Non-`"1.0"` → treated as unconfigured, `console.warn`, fall through to derived defaults. |
| `title` | no | Toolbar title. Default: entity display name from metadata. |
| `columns` | no | `2` or `3`. Default `3`. |
| `summaryField` | no | Field backing the sparkle popover. Omitted → sparkle icon hidden (**not** shown-and-empty; see §10). |
| `fields[].name` | yes | Logical name. For lookups, the **lookup attribute** name (`sprk_mattertype`), not `_sprk_mattertype_value`. |
| `fields[].span` | no | `1`–`3`. Default derived from renderer (textarea → `columns`, else `1`). |
| `fields[].label` | no | Override. Default: the form control's label. |
| `fields[].renderer` | no | Override: `text` \| `textarea` \| `lookup` \| `optionset` \| `date` \| `datetime` \| `number` \| `currency` \| `boolean`. Default: derived from attribute type. |
| `fields[].readOnly` | no | Suppress inline editing for this cell. Default `false`. |
| `fields[].required` | no | Renders the `*` marker. Default: derived from attribute requirement level. |

**Invalid JSON never throws.** Parse failure → `console.warn` + derived defaults, mirroring [`isValidDataGridConfiguration`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts) and [`parseOptionsJson`](../../src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts). A malformed paste must never blank a production form.

### 5.3 Resolution — two tiers

1. **`layoutJson`** on the manifest (when present and valid)
2. **Derived defaults** — primary name field first (span 2), then up to four further non-system fields **in form order**, skipping `createdon`/`modifiedon`/`createdby`/`modifiedby`/`ownerid`/`statecode`/`statuscode`/`versionnumber` and the primary id. Same shape as `synthesizeColumnsFromMetadata` in [`configResolution.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts).

Tier 2 is what makes "drop it on a new form and it works" true, and it is why the control can never render blank.

### 5.4 Metadata source — form context first

The control is **form-embedded**, and the R1 write path already requires every editable field to be present on the form (`Xrm.Page.getAttribute(name)` → `throw new Error("Field '…' not on form")`, [MatterHeaderView.tsx:186](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L186)). That existing constraint is an asset: the form context can supply almost everything config would otherwise have to state.

| Needed | Source | Fallback |
|---|---|---|
| Entity logical name | `context.mode.contextInfo.entityTypeName` | — (proven in [VisualHostRoot.tsx:253](../../src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx#L253), [TrackingFieldTrio](../../src/client/pcf/TrackingFieldTrio/index.ts#L346)) |
| Field label | `formContext.getControl(n).getLabel()` | `EntityDefinitions` DisplayName → humanized logical name |
| Attribute type → renderer | `getAttribute(n).getAttributeType()` | `EntityDefinitions` `AttributeType` |
| Option-set options | `getAttribute(n).getOptions()` | `EntityDefinitions` `OptionSet` |
| Required marker | `getAttribute(n).getRequiredLevel()` | config `required` |
| Lookup target entity | `EntityDefinitions/ManyToOneRelationships` | — |
| Lookup id/name fields | target's `PrimaryIdAttribute` / `PrimaryNameAttribute` | — |

**This is what deletes `LOOKUP_META`.** Matter's lookups point at non-conventional `*_ref` entities (`sprk_mattertype_ref` / `sprk_mattertype_refid` / `sprk_mattertypename`), which is exactly why R1 hard-coded them — but those three values are the relationship target plus its own primary id and primary name attributes, all readable from metadata. The `ManyToOneRelationships` query pattern already exists in the repo at [PolymorphicResolverService.ts:481](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts#L481) and [TodoRegardingUpdateBuilder.ts:292](../../src/client/shared/Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts#L292).

> **Discovery task (blocking, before `/design-to-spec` locks §9)**: confirm via Dataverse MCP that `sprk_mattertype_ref.PrimaryIdAttribute === 'sprk_mattertype_refid'` and `PrimaryNameAttribute === 'sprk_mattertypename'`. If either differs, `fields[].lookup: { entity, idField, nameField }` returns to the schema as an optional escape hatch. Everything else in this design is unaffected either way.

---

## 6. Shared-library work

These are required regardless of config mechanism — and note that **the withdrawn four-PCF plan needed most of them too**: its own §5.2 Invoice field list (currency amount, due date, status) cannot render correctly with today's renderer set. Today a Money value renders as `12500` and a DateTime as `2026-08-21T00:00:00Z`, because `TextField` does `String(value)`.

### 6.1 New / extended field renderers

| Renderer | Covers | Notes |
|---|---|---|
| `DateField` | `DateTime` (`DateOnly` + `DateAndTime`) | Locale-formatted display; date picker on edit. Blocks Invoice + Event. |
| `NumberField` | `Integer`, `Decimal`, `Double`, `Money` | Currency symbol + precision from metadata; right-aligned per `defaultAlignFor`. Blocks Invoice. |
| `BooleanField` | `Boolean`, `TwoOptions` | Read = Yes/No label; edit = Fluent `Switch`. |
| `OptionSetField` **(extend)** | `Picklist`, `Status`, `State` | Currently display-only ([OptionSetField.tsx](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/OptionSetField.tsx)). Add edit via Fluent `Dropdown` fed by `getOptions()`. |

Every renderer follows the established contracts: owns its own `gridColumn: span N`, em-dash for null, Fluent v9 semantic tokens only (ADR-021), React 16/17-safe (ADR-022), and `onSave` optional → omit for read-only.

### 6.2 Hoist the generic machinery out of the view

Move from `MatterHeaderView.tsx` into the shared library so it exists once:

- Form-buffer staging (`saveText` / `saveLookup` via `Xrm.Page.getAttribute().setValue()`) — the R1 v1.0.7 dirty-state pattern, which must be preserved exactly (it exists because writing straight to Dataverse re-rendered the whole PCF on every edit)
- The pending-changes buffer and `pendingX[name] ?? values?.[name]` display resolution
- `projectLookup()` (`_field_value` + `@OData.Community.Display.V1.FormattedValue` → `ILookupItem`)
- The lookup OData search builder, generalized over metadata-resolved target/id/name

Proposed home: `hooks/useRecordHeaderFields.ts` + `components/RecordHeader/RecordHeaderFields.tsx`. Exact split is a `/design-to-spec` decision.

### 6.3 Config resolver

`resolveHeaderConfig(manifestJson, formMetadata) → ResolvedHeaderConfig` — pure, no React, no I/O, exhaustively unit-testable. Deliberately mirrors [`configResolution.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts), which is the proven in-repo shape for this problem.

### 6.4 Toolbar slot auto-hide

`sprk_todo` supports 11 parents; `sprk_memo` supports 6. On an entity with To Dos but no Memo lookup (Contact, Document, Organization, Analysis, Communication), the annotation icon currently still renders and opens a Notepad that cannot save. `useRecordHeaderToolbarActions` must omit the slot when `buildMemoFilterForParent` returns `null`. Same for the sparkle when no `summaryField` resolves.

---

## 7. Structural items from the 2026-07-05 seed — both dropped

### 7.1 DEF-06 (`exports` field + `moduleResolution: bundler`) — DROPPED

The seed's rationale was "four new PCFs land at once, so do the migration once for all of them." R2 now ships **one** PCF, so the leverage is gone while the cost — a repo-wide `pcf-scripts/tsconfig_base.json` bump requiring every PCF solution ZIP to be rebuilt and smoke-tested — is unchanged. R1 already attempted and reverted this (see [plan-extension.md](../record-header-and-notepad-r1/plan-extension.md) task 063). It should be its own migration project when someone wants it, not a passenger here.

**Consequence**: R2 keeps R1's `@spaarke/ui-components/dist/*` deep-path import convention, which is also **mandatory** for bundle size per the authoring guide's optimization triad (`featureconfig.json` + `webpack.config.js` + deep-path imports — ~40 KB vs 1.6 MB without). Do not "clean up" those imports.

### 7.2 DEF-08 (promote `useSprkMemoRepository`) — DROPPED

The seed made promotion conditional on "does any PCF render memo content inline?" The answer for a configurable header is **no** — it launches the Notepad, it does not embed it. No second consumer, so per CLAUDE.md §11 the promotion is unjustified. The trigger remains where R1 left it: whenever `EventDetailSidePane/MemoSection.tsx` is next touched (DEF-04).

---

## 8. Control identity + migration

The deployed control is `Spaarke.Records.MatterHeader` v1.0.20 (solution `sprk_Spaarke.Records.MatterHeader`), bound to the Matter main form.

Changing `constructor=` in the manifest creates a **new** control and orphans that binding — it is not a rename. Two options:

| Option | Cost | Verdict |
|---|---|---|
| Keep `constructor="MatterHeader"` | Zero migration; the control is called "MatterHeader" on Invoice and Event forever | ❌ |
| Ship `Spaarke.Records.RecordHeader`, re-bind the single Matter form, retire `MatterHeader` | One form edit, once | ✅ **Recommended** |

Migration sequence: ship `RecordHeader` → bind it to the Matter form with a `layoutJson` reproducing R1's five-field layout → **verify pixel-identical rendering against v1.0.20** → remove the old control from the form → retire the old solution. The version footer (kept, per `src/client/pcf/CLAUDE.md`) is the in-UI check that the swap took.

Matter is therefore both the last migration and the strongest regression test: R1's live-QA behaviors — form-buffer dirty state with no re-render flash, 25%×35% Notepad modal, `openTodos` SmartTodo filter, `sprk_mattersummary` sparkle, dark/high-contrast theming — must all survive unchanged.

---

## 9. Per-entity requirements

Field lists below are **inherited unverified** from the 2026-07-05 seed. Every one is `TBD-CONFIRM`.

> **Blocking discovery task**: verify via Dataverse MCP, per entity, before `/design-to-spec` locks acceptance criteria — primary name attribute, each listed field's logical name and attribute type, and whether a summary field exists **and is actually populated**. R1 lost a release to exactly this: v1.0.20 fixed a sparkle popover that had been silently empty on every Matter in production because `sprk_recordsummary` is written on zero records while `sprk_mattersummary` holds the real data. Do not assume `sprk_recordsummary` on any of these four.

| Entity | Draft fields (unverified) | New renderers needed | Memo/To Do support |
|---|---|---|---|
| `sprk_project` | name, status (optionset), owner (lookup), start date, target end date | date | both ✅ |
| `sprk_workassignment` | name, status (optionset), assigned to (lookup), start date, estimated hours | date, number | both ✅ |
| `sprk_invoice` | name, invoice number, **amount (currency)**, status (optionset), **due date** | **currency, date** | both ✅ |
| `sprk_event` | name, type, **start (datetime)**, **end (datetime)**, location | **datetime** | both ✅ |
| `sprk_matter` | as R1 v1.0.20 | none | both ✅ |

All five are in both `SUPPORTED_TODO_PARENTS` and `SUPPORTED_MEMO_PARENTS`, so no toolbar map changes are needed for this rollout — §6.4's auto-hide is for entities added later.

**Per-form acceptance criteria** (each binding):

- Renders configured fields at configured spans; inline edit stages to the form buffer and goes dirty without a PCF re-render
- Toolbar identical to Matter's: title, sparkle, To Do badge, Notepad badge
- To Do opens SmartTodo filtered to this record; Notepad opens scoped to this record
- Bundle ≤250 KB minified (R1 shipped 62.4 KiB; shared-lib footprint dominates, so the ceiling should hold — but §10 tracks it as the one thing that could regress)
- Malformed / absent `layoutJson` degrades to derived defaults, never blank, never thrown
- Version footer present

---

## 10. Risks

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| **Single control = shared blast radius.** A regression breaks every bound entity at once. | High | Med | Partly pre-existing — all header PCFs already share one library, so a shared-lib bug breaks all of them today regardless. Mitigate with staged form binding (wave 1 → soak → wave 2), version footer, and Matter migrated **last**. |
| **Bundle growth from four new renderers** breaches the 250 KB ceiling. | Med | Low–Med | Measure per wave, not at the end. The optimization triad (§7.1) is mandatory and must not be disturbed. |
| **Metadata-derived lookups fail on a non-conventional target** (§5.4 discovery). | Med | Med | Escape hatch already designed: optional `fields[].lookup: { entity, idField, nameField }`. Cheap to add if discovery says so. |
| **Summary field absent or unpopulated** on Project / Work Assignment / Invoice / Event. | Med | **High** — this already happened on Matter | Sparkle hidden when `summaryField` is absent (§5.2), so the failure mode is a missing icon rather than a broken one. Confirm per entity in discovery. |
| **Layout change requires a form publish** (the accepted cost of JSON-on-manifest). | Low | Certain | Accepted 2026-08-21. Reversible via the resolver's tier design (§5.1). |
| **`FieldGrid` caps at 2–3 columns, span 1–3.** If a form wants a 4-column header or explicit row breaks, config cannot express it. | Low | Low | Not in R2. Revisit only if a real form needs it. |
| Form binding needs maker access the dev session lacks. | Low | Low | Replicate R1's maker checklist ([`matter-form-binding-instructions.md`](../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md)) per entity. |

---

## 11. Component justification (CLAUDE.md §11)

| New surface | Existing overlap | Why not extend it | Cost of doing nothing |
|---|---|---|---|
| `RecordHeader` control | `MatterHeader` PCF | This **is** the extension — same control, generalized, old one retired (§8) | Four more PCF solutions to version, deploy, bind, and fix in parallel; ~180 lines of edit machinery duplicated four times |
| `DateField` / `NumberField` / `BooleanField` | `TextField` | `TextField` does `String(value)` — it cannot format a Money value by currency/precision or a DateTime by user locale | Invoice amount renders `12500`; due date renders `2026-08-21T00:00:00Z`. Invoice ships broken. |
| `OptionSetField` edit mode | `OptionSetField` | Direct extension of the existing component | Project / Work Assignment / Invoice status become read-only, unlike every other field in the header |
| `layoutJson` property | `title`, `showVersion` properties | Same manifest surface, one more input | Field payload stays compiled in; a new entity needs a code change and a redeploy |
| `resolveHeaderConfig` | `configResolution.ts` (DataGrid) | Different domain object (fields/spans vs columns/views); deliberately mirrors its structure and test approach | Config precedence spreads through the render path untested |
| ~~`sprk_headerconfiguration` table~~ | — | **Rejected** — §5.4. No concrete failure without it. | n/a |

---

## 12. ADR posture

Inherited and unchanged: **ADR-006** (PCF for form-bound UI), **ADR-012** (shared library), **ADR-021** (Fluent v9 semantic tokens only), **ADR-022** (React 16/17-safe shared components), **ADR-024** (`sprk_memo` Path C dual-field), **ADR-038** (testing strategy). **ADR-028** is N/A — host-context `Xrm` only, no `@spaarke/auth`, no BFF.

**No ADR conflict, and no §6.5 escalation.** Worth stating explicitly because R1's project CLAUDE.md paraphrases ADR-011 as "typed components > runtime schemas," which reads like a blocker for a configuration-driven control. [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) contains no such rule; its actual MUSTs — "reuse shared components from `@spaarke/ui-components`", "MUST NOT duplicate UI primitives" — point toward this design. The repo's own configuration-driven frameworks (VisualHost `sprk_chartdefinition`, DataGrid `sprk_gridconfiguration`) are established precedent for the pattern.

Also unchanged: **NFR-07** (no BFF) and **NFR-09** (Notepad launch-contract URL params are external API — do not rename).

---

## 13. Cross-environment portability (binding)

R1 shipped clean on this axis and R2 must preserve it: no literal record GUIDs, environment names, tenant/subscription ids, or user/contact/business-unit ids in any shipped bundle. `window.SPAARKE_*` globals and build-time-inlined `.env` values remain unacceptable.

The JSON-on-manifest decision **strengthens** this position: layout configuration lives in form XML and travels with the solution, so there are no config records to seed per environment. Portability check per deliverable: import the solution ZIP into a fresh environment and verify the header renders, toolbar actions launch, and badges fetch — with no additional environment-specific configuration.

---

## 14. Rough effort

| Work | Estimate |
|---|---|
| Generic view + entity self-detection + config resolver (§5, §6.3) | 1–2 d |
| Hoist generic machinery to shared lib (§6.2) | 1 d |
| Four new/extended renderers (§6.1) | 2–3 d |
| Metadata-driven lookup resolution (§5.4) | 1 d |
| Control rename + Matter migration + parity QA (§8) | 0.5–1 d |
| Per-entity config + form binding + QA | 0.5 d × 4 |
| Guide rewrite + pattern refresh (§3.1) | 0.5 d |
| **Total** | **~8–11 dev-days** |

The withdrawn four-PCF plan estimated 4–6 h × 4 ≈ 3 days — but that number excluded the renderer work it also needed (§6), and left five controls to maintain instead of one.

---

## 15. Next steps

1. **Discovery pass (blocking)** — Dataverse MCP, per entity: primary name attribute; each field's logical name + attribute type; summary-field existence **and population**; the `sprk_mattertype_ref` primary id/name check from §5.4.
2. **`/design-to-spec`** on this document → numbered FRs/NFRs with per-entity acceptance criteria.
3. **`/project-pipeline`** → worktree + task list + `projects/INDEX.md` registration.
4. Sequence tasks: shared-lib renderers and the resolver land **before** any form binding; Matter migrates **last**.

---

## 16. Related deferrals

- **DEF-01** (sparkle refresh → BFF regen endpoint) — absorbed by the future Insights Engine / AI Summary project. Not R2.
- **DEF-03** (VisualHost `CardChrome` → `HeaderToolbar`) — in-code pointer; R2B when someone touches VisualHost.
- **DEF-04** (EventDetailSidePane `MemoSection`) — in-code pointer; R2B when someone touches it. Also the trigger for DEF-08.
- **DEF-06** (`exports` field migration) — dropped from R2 (§7.1); standalone migration project when wanted.
- **DEF-08** (`useSprkMemoRepository` promotion) — dropped from R2 (§7.2); trigger stays on DEF-04.

---

*Re-scoped 2026-08-21 from the 2026-07-05 four-PCF seed, per owner decision: one configurable control; Project + Work Assignment first; Invoice explicitly required; JSON over config table.*
