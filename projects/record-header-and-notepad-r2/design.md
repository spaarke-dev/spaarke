# Configurable Record Header — R2

> **Status**: DRAFT — re-scoped 2026-08-21. Supersedes the 2026-07-05 seed (four per-entity PCFs).
> **Project ID**: `record-header-and-notepad-r2` (folder/ID retained for continuity with R1 cross-links; the deliverable is no longer Notepad work)
> **Positioning**: Replace the "one thin PCF per entity" plan with **ONE configuration-driven `RecordHeader` PCF** that works on any entity's main form. Title area + toolbar (AI summary / To Do / Notepad) stay identical everywhere; the field payload and its placement are configured per form via a JSON manifest property.
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-05 · **Re-scoped**: 2026-08-21 · **Code-verified**: 2026-08-22 · **Schema-verified against `spaarkedev1`**: 2026-08-24
>
> **2026-08-22 revision** — every code claim in this document was checked line-by-line against the repo, and the §9 entity schemas were verified offline against `docs/data-model/**` plus live query/write code. Corrections are marked inline.
>
> **Material changes**: §1.3 line accounting corrected · §5.1 apostrophe fix + spike, with §5.1.1 added (the `layoutJson` mechanism has no in-repo precedent — but a proven fallback exists, so the risk is ergonomic, not existential) · §5.4 re-pointed at the existing `IDataverseClient` contract instead of a new raw-`fetch` path, with §5.4.1 comparison, plus caching + TTI requirements · §6.1 renderers re-scoped against real fields · §8 identity facts corrected · §9 **field lists substantially rewritten — five drafted fields do not exist** · §13 portability claim resolved.
>
> **2026-08-24 revision** — §9 entity schemas **live-verified against `spaarkedev1`** via the Dataverse Web API; discovery is closed. Six drafted fields do not exist; `sprk_project`'s primary name is `sprk_projectnumber`; Event's `DateAndTime` pair is `sprk_plannedstart`/`sprk_plannedend` (`scheduledstart`/`scheduledend` and `sprk_location` do **not** exist); §5.4's lookup-metadata check passed exactly, so `LOOKUP_META` can be deleted and the `fields[].lookup` escape hatch is not needed. Sparkle + summary fields **kept** per owner, with visibility keyed on attribute existence rather than population. `BooleanField` **reinstated** — it has consumers on all four entities. Two live-code defects filed in §9.1.
>
> **Owner decisions — all closed**: ✅ D-1 option B (new `RecordHeader` control) · ✅ D-2 forms ship in a solution · ✅ D-3 JSON-only on manifest · ✅ D-4 retire `MatterHeaderPcf` on delivery · ✅ D-5 reuse `IDataverseClient` · ✅ D-6 §9 rewritten from live schema. **Ready for `/design-to-spec`.**

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

The re-scope rests on three findings from the 2026-08-21 code review of [`src/client/pcf/MatterHeader/`](../../src/client/pcf/MatterHeader/), **re-verified line-by-line 2026-08-22** (numbers below are the corrected ones):

1. **The toolbar is already generic.** [`useRecordHeaderToolbarActions`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts) takes `{ entity, recordId, title }` and resolves To Do / Memo badges from the [`SUPPORTED_TODO_PARENTS` / `SUPPORTED_MEMO_PARENTS`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts) maps. Counts confirmed from code 2026-08-22: **11 To Do parents** ([`toolbarLaunchDefaults.ts:150-162`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L150-L162)) and **6 Memo parents** ([`:105-112`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L105-L112)). The "same toolbar on every entity" requirement needs **zero** new work.
2. **No manifest property *mechanism* is Matter-specific.** `boundField` is "any SingleLine.Text field on the host entity" — and its value is **never read**; it exists only so the control appears in the form designer's field gallery. The record id comes from `context.mode.contextInfo.entityId` ([`index.ts:28-36`](../../src/client/pcf/MatterHeader/control/index.ts#L28-L36)). `title` and `showVersion` input properties already exist ([`ControlManifest.Input.xml:20-28`](../../src/client/pcf/MatterHeader/control/ControlManifest.Input.xml#L20-L28)). What *is* Matter-specific is the control's **identity strings** — `constructor="MatterHeader"`, `display-name-key="Matter Header"`, and descriptions naming Matter / `sprk_mattersummary` / the recommended `sprk_matternumber` binding — all replaced by §8.
3. **~80 of `MatterHeaderView.tsx`'s 326 lines collapse into configuration; ~82 are reusable machinery the withdrawn plan would have copy-pasted four times.** Corrected accounting (2026-08-22 full-file classification; the earlier "~40 config / ~180 machinery" split was wrong in both directions and left ~106 lines unaccounted):

   | Category | Lines | Where |
   |---|---|---|
   | Entity-specific **declarative config** | **59** | `ENTITY` (75), `FIELDS` (77–84), `LOOKUP_META` (89–100), `sprk_mattersummary` fetch (271–277), JSX field layout incl. hard-coded English labels (287–317) |
   | Entity-specific **per-field wiring** (also eliminated by config) | **21** | memoized save callbacks (193–195, 237–246), display-resolution consts (249–256) |
   | **Reusable core machinery** | **82** | `getXrmPage` (132–142), `projectLookup` (145–155), pending buffer + reset effect (166–172), `saveText` (175–191), `searchLookup` (198–214), `saveLookup` (216–235) |
   | Generic boilerplate (imports, styles, props, JSX chrome, footer) | 63 | — |
   | Comments + blanks | 101 | — |

   The generalization argument gets **stronger**, not weaker: ~80 lines become JSON, not ~40. Separately, `MatterHeaderHost.tsx` holds a further ~107 lines of generic theme/high-contrast host machinery that stays in the PCF layer (not part of the §6.2 hoist).

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
| 1 | `sprk_project` | Exercises date + boolean + option-set (§9 layouts) — corrected 2026-08-24; the seed's "existing renderers only" claim was based on a wrong field list |
| 1 | `sprk_workassignment` | Same renderer shape; validates the config path on a second entity |
| 2 | `sprk_invoice` | **Forces the currency renderer** (§6). Explicitly required, not optional. |
| 2 | `sprk_event` | Forces **datetime** (`sprk_plannedstart` / `sprk_plannedend`) plus a lookup-typed "type" field |
| 3 | **`sprk_agreement`** *(added 2026-08-25)* | Needs **no new renderers**. Carries R2's only toolbar-map change (§9.2) and needs a seeded record — it has 0 today |
| — | `sprk_matter` | Migrated from `MatterHeaderPcf` to the generic control (§8); must render pixel-identically |

**Shared-library additions** (§6): date/datetime, number/currency, and boolean field renderers; editable option-set; metadata-driven lookup resolution; the config resolver.

**Summary-field standardization** (owner 2026-08-25, §9): all entities use **`sprk_recordsummary`**. The columns already exist — the owner created them — so R2 has **no schema work**; it verifies them and remediates two residual references to the now-deleted `sprk_mattersummary` / `sprk_aisummary`, one of which has broken the shipped Matter header. R2 stays a pure client surface plus one Dataverse **data** fix (`sprk_aitopicregistry`).

**Documentation**: rewrite [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) from "how to write a new per-entity PCF" to "how to configure the header for a new entity" — the old recipe becomes actively wrong the day this ships. Refresh [`.claude/patterns/ui/record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) accordingly (its withdrawal warning and the `patterns/ui/INDEX.md` row are already corrected on this branch; only the body sections need the post-ship update).

> **Scoped 2026-08-22**: the guide is 354 lines; **~170–190 lines (≈50–55%) are replaced or heavily revised**, ~120 survive verbatim. The §6 bundle-optimization block (~57 lines) survives as design §3.1 assumed. **But the guide has *already* drifted from shipped v1.0.20** — its manifest example shows a `recordId` input-only control (actual: `boundField` bound + `title` + `showVersion`), its modal sizes are stale (SmartTodo 85%×85% → `openTodos` filtered launch; Notepad 70%×80% → 25%×35%), it documents a `recordSummary` hook parameter retired at v1.0.10, it repeats the ADR-011 "typed components — no runtime schemas" line that §12 debunks, and it claims platform-library theming removes the need for a manual `FluentProvider` (R1 live QA proved otherwise). **Consequence: the new guide must be authored from shipped code, not diffed from the old one.** It must also correct "4 version locations" → 5.

### 3.2 Out of scope

- **A `sprk_headerconfiguration` Dataverse table.** Explicitly rejected — see §5.4.
- **DEF-06** (shared-lib `exports` field + `moduleResolution: bundler`) — dropped from R2; see §7.1.
- **DEF-08** (`useSprkMemoRepository` promotion) — dropped from R2; see §7.2.
- Any BFF surface. The sparkle refresh icon stays unwired (R1 FR-08a / NFR-07 continue to hold).
- VisualHost `CardChrome` migration (DEF-03) and EventDetailSidePane `MemoSection` (DEF-04) — remain in-code pointers.
- **The seven schema-drift defects found during §9 verification** — captured as standalone issue docs in [`notes/issues/`](notes/issues/README.md) for evaluation as focused fix projects (§9.1).
- Changes to the Notepad or SmartTodo code pages. Both are already entity-agnostic and this project launches them with the same URL contracts R1 established (`regardingEntity`/`regardingId`; `action=openTodos&regardingType=…&regardingId=…`) — **NFR-09 external-API status unchanged**.

### 3.3 Natural boundary

Entities beyond the five above need no project — they need a form edit. That is the point of R2.

---

## 4. What R2 consumes unchanged

All R1 primitives are consumed verbatim. **No forking.** Missing behavior lands in the shared lib, and every entity picks it up at once.

| Primitive | R1 file | Change in R2 |
|---|---|---|
| `HeaderToolbar` | [`components/HeaderToolbar/`](../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/) | None. Note it **already** renders the sparkle only when `aiSummary` is passed ([`HeaderToolbar.tsx:154`](../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx#L154)) — half of §6.4 is pre-solved |
| `RecordHeaderShell` | [`RecordHeaderShell.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/RecordHeaderShell.tsx) | **Small change (owner, 2026-08-24)** — add an optional `columns` prop and drive the loading skeleton from it. Today it hard-codes `repeat(3, 1fr)` and 6 cells ([`:93-131`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/RecordHeaderShell.tsx#L93-L131)), so a `columns: 2` header flashes a mismatched skeleton. Pixel-parity on Matter is a binding acceptance criterion, so a load-time mismatch is worth closing. Default stays `3` — backward compatible |
| `FieldGrid` | [`FieldGrid.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/FieldGrid.tsx) | None — **but it does not validate span**. `FieldGrid` never sets `gridColumn`; each cell applies its own. A `span: 3` cell in a `columns: 2` grid silently creates an implicit third track and breaks the layout. **`resolveHeaderConfig` MUST clamp `span = min(span, columns)`** (§6.3) |
| `TextField` / `TextareaField` | [`RecordHeader/fields/`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields) | None — `TextField` is the canonical renderer contract new renderers copy (§6.1) |
| **Lookup — REPLACED by the OOB picker** (owner, 2026-08-25) | [`RecordHeader/fields/LookupField.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/LookupField.tsx) (display-only) · [`components/LookupField/LookupField.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx) (custom search-as-you-type) | **Changed.** The `lookup` renderer now displays the current value and opens **`Xrm.Utility.lookupObjects`** on click — see §6.5. This **retires the custom type-ahead** from the header path and **removes the OData lookup-search builder from the §6.2 hoist entirely** |
| `OptionSetField` | same | **Extended** — gains edit mode (§6.1). Also fix its **stale label typography** (`caption1` / `colorNeutralForeground2`, [`:86-89`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/OptionSetField.tsx#L86-L89)) — the siblings moved to `fontSizeBase300` / `colorNeutralForeground1` at v1.0.4 and it was left behind, so mixed grids look off today |
| `useRecordFieldValues` | [`useRecordFieldValues.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordFieldValues.ts) | None |
| `useRelatedCount` | [`useRelatedCount.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts) | None |
| `useRecordHeaderToolbarActions` | [`useRecordHeaderToolbarActions.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts) | Minor — auto-hide Notepad slot for unsupported parents (§6.4). Today the slot is pushed on `annotationEnabled` alone ([`:223, 325-333`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts#L325-L333)); a null memo filter only zeroes the badge |
| `AiSummaryPopover` | [`components/AiSummaryPopover/`](../../src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover) | None |
| `themeStorage` | [`utils/themeStorage.ts`](../../src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts) | None |
| Notepad + SmartTodo code pages | [`src/solutions/Notepad/`](../../src/solutions/Notepad) · [`src/solutions/SmartTodo/`](../../src/solutions/SmartTodo) | None |

---

## 5. Configuration model

### 5.1 Mechanism — JSON on the manifest (owner decision 2026-08-21)

A new manifest property carries the layout:

```xml
<property name="layoutJson" display-name-key="Header layout (JSON)"
          description-key="JSON layout for the header on this form. Leave blank to derive a default from the form."
          of-type="Multiple" usage="input" required="false" />
```

> ⚠️ **No apostrophes in `description-key`.** The original draft read `"…for this form's header…"`; apostrophes in manifest string attributes fail `pac solution import` with `noAposStringType` ([`pcf-deploy/SKILL.md:447`](../../.claude/skills/pcf-deploy/SKILL.md)). Corrected above.

> ⚠️ **Blocking spike (D-3) — the mechanism is unproven in this repo. Full analysis in §5.1.1.** `of-type="Multiple"` is the correct PCF type name for multiline text and is used here (e.g. [`CommunicationConnections ControlManifest.Input.xml:7`](../../src/client/pcf/CommunicationConnections/CommunicationConnections/ControlManifest.Input.xml#L7)), **but every in-repo JSON-carrying property is either `usage="bound"` to a real multiline column or a config *record* pointer**. A repo-wide scan finds **zero** `of-type="Multiple"` + `usage="input"` properties. Neither cited precedent works the proposed way: VisualHost passes a `Lookup.Simple` / GUID pointer and fetches `sprk_optionsjson` from a `sprk_chartdefinition` record ([`ConfigurationLoader.ts:53-54, 164, 387-413`](../../src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts)); DataGrid uses `sprk_gridconfiguration`. Before `/design-to-spec` locks §5, verify empirically that the form designer (classic path — R1 found the modern designer unreliable for header-region PCF binding) presents a usable multi-line editor for a static `Multiple` input property and accepts a ~1 KB JSON paste. If it renders a single-line box, the §2 product promise degrades and the fallback tier must be named.

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

### 5.1.1 D-3 analysis — where the config actually lives

**The mechanic, plainly.** A PCF manifest property is one of two kinds:

- **`usage="bound"`** — wired to a real Dataverse **column**. Its value is *record data*, different for every record. This is how `boundField` works.
- **`usage="input"`** — a **static value the maker types once** when placing the control on a form. It is not stored in any record; it is written into the **form's XML** (`customizations.xml`) and travels with the form. This is how `title` and `showVersion` work today.

`layoutJson` is proposed as the second kind. The layout is not per-record data, so `input` is conceptually right.

**The problem is the editor, not the concept.** Nothing in this repo has ever put a *large* value into a static `input` property. Static input values are typed into the form designer's control-properties panel, and we have not verified what that panel gives you for `of-type="Multiple"`. Three ways it can go:

| If the designer gives us… | Maker experience | Severity |
|---|---|---|
| A real multi-line textarea | Paste formatted JSON, edit it later, done. This is the §2 product promise. | ✅ |
| A single-line text box | You paste a ~330-character minified one-liner into a narrow box with no formatting and no validation. Editing later means re-pasting the whole string. Ugly, survivable. | ⚠️ |
| Silent truncation on save or on solution export | The header quietly falls back to derived defaults and **nobody knows why**. | 🔴 The dangerous one |

**The de-risking insight (2026-08-22): the fallback is already proven, and it is nearly free.** `of-type="SingleLine.Text"` + `usage="input"` is *known* to work — `title` uses exactly that shape today, and VisualHost's `chartDefinitionId` carries a GUID the same way. Because a static input value lives in form XML rather than a Dataverse column, the 4,000-character column limit does not apply. **The §5.2 example layout, minified, is ~330 characters.** A realistic 6-field header is comfortably under 1 KB.

So the spike is not "does this work at all" — it is "do we get the nice editor or the ugly one":

```
Try  of-type="Multiple"  → nice multi-line editor?  ─── yes ──▶ ship it
                                    │
                                    no
                                    ▼
     of-type="SingleLine.Text"  ← proven today by `title`; minified JSON ~330 chars
```

Either way the resolver, the schema, and every renderer are **identical** — only the manifest `of-type` differs. That reduces D-3 from "the project's central premise is unproven" to "a 15-minute check on authoring ergonomics."

**Full option comparison:**

| | 1. JSON on manifest (current) | 2. Config record only (VisualHost / DataGrid) | 3. Hybrid — record + manifest override |
|---|---|---|---|
| Proven in this repo | ⚠️ Not as `input`; **yes** as `SingleLine.Text` input (fallback) | ✅ Twice (`sprk_chartdefinition`, `sprk_gridconfiguration`) | ✅ |
| New Dataverse surface | None | New table + solution + seed procedure | New table |
| Cross-environment | ✅ Rides the form in the solution (owner-confirmed §13) | Config **records** are data — need a config-data migration step, not just a solution import | Same as 2 |
| Extra query on form load | None | One per form load (cacheable) | One, unless overridden |
| Edit without publishing the form | ❌ | ✅ | ✅ |
| Reuse one layout across several forms | ❌ | ✅ | ✅ |
| Authoring UX | Text box in the form designer | Real multiline column editor + a future maker UI | Both |
| Cost if wrong | Fall back to `SingleLine.Text`, or add tier 2 later — the resolver is already tier-shaped | Table exists forever for ~6 records | Two mechanisms to document and test |
| CLAUDE.md §11 | ✅ No new component | ⚠️ Must name what concretely fails without the table — §5.1 could not | ⚠️ Same, plus a second mechanism |

**Recommendation: stay with option 1, run the spike as a 15-minute ergonomics check, and name `SingleLine.Text` as the fallback in the spec.** The owner's §13 confirmation (forms ship in a solution) makes option 1's main advantage real rather than hypothetical: layout config is authored once in dev and rides the solution downstream with zero seeding, whereas option 2's config *records* would need their own data-migration step. The volume argument also still holds — a handful of configs, ever. Option 3 remains available later without touching a single renderer, because `resolveHeaderConfig` is a pure function over tiers; that reversibility is why committing to option 1 now is cheap.

### 5.1.2 How a maker actually applies the JSON at deploy time

> **Terminology — "header" here does NOT mean the Dataverse form header.** This control is **not** loaded into the form-header strip at the top of a model-driven form (the narrow band beside the record title that holds a few quick-view fields). It is an ordinary **field-bound PCF placed in the first section of the form body**, which visually replaces that section. R1 bound it to `sprk_matternumber` and moved the section's other raw fields aside so they did not render twice — **aside, not off the form**; see the correction at step 6 below, which the shipped R1 `formxml` confirms. "Record Header" is this component's product name, not a Dataverse region. R1's binding note uses the phrase "header-region controls" to mean *the top section of the form body*, and earlier revisions of this document repeated that ambiguity.

The layout is **per form, not per record**, and it is entered in the form designer's control-properties panel — the same panel that already carries `title` and `showVersion`. Following the sequence R1 used ([`matter-form-binding-instructions.md`](../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md)):

1. Import + publish the `RecordHeaderPcf` solution, then **Publish all customizations**.
2. `make.powerapps.com` → Solutions → Tables → *entity* → Forms → the **main** form → ⋯ → **Edit form → Edit in classic**. (R1 used the classic designer because it hit friction binding a PCF in the modern one — see the terminology note below. This is an R1 working preference recorded in its notes, **not a verified platform constraint**; the modern designer may work fine.)
3. Click the field the control will replace (e.g. `sprk_projectnumber` on Project) → **Controls** tab → **Add Control** → "Spaarke Record Header" → **Add**.
4. Select its row, then tick **Web** (and Phone / Tablet) under "Choose format".
5. The control's properties now list as rows beneath it — **Bound field**, **Header title**, **Show version footer**, and the new **Header layout (JSON)**. Each row offers *Bind to static value* vs *Bind to a value on a field*. For `layoutJson` choose **static value** and paste the JSON.
6. **Move the other raw fields out of that section — do NOT delete them from the form.** They must not render twice, but they MUST stay on the form somewhere: inline editing stages through the form buffer via `Xrm.Page.getAttribute(name).setValue(v)`, and `getAttribute` returns `null` for a field that has no control on the form, which makes the control's `requireFormAttribute` throw `Field '<name>' not on form`. Put them in a separate collapsed section, or set the controls to not-visible. Only a field the header **reads without editing** (e.g. `sprk_recordsummary` behind the sparkle) may be absent entirely.
   > **Corrected 2026-08-26.** Earlier revisions of this document — and the five Phase-5 rollout POMLs — said "delete". That was wrong, and the shipped R1 form disproves it: `formxml` for `Matter main form` (`4fa382f2-…`) shows `sprk_matternumber`, `sprk_mattername`, `sprk_mattertype`, `sprk_practicearea` and `sprk_matterdescription` **all still on the form**; only `sprk_recordsummary` (read-only, sparkle) is absent. Following the "delete" instruction breaks inline editing for every field it removes.
7. **Save → Publish.** The pasted JSON is stored inside the form's XML in `customizations.xml`, so it travels with the solution (§13) — authored once in dev, carried downstream.

Worked example — what a maker pastes for Project (fields per the live-verified §9):

```json
{ "_version": "1.0", "title": "Project", "columns": 3,
  "fields": [
    { "name": "sprk_projectnumber",      "span": 1, "required": true },
    { "name": "sprk_projectname",        "span": 2 },
    { "name": "sprk_projecttype_ref",    "span": 1 },
    { "name": "sprk_practicearea",       "span": 1 },
    { "name": "sprk_openeddate",         "span": 1 },
    { "name": "sprk_projectdescription", "span": 3, "maxLines": 10 } ] }
```

Leave the property blank and the control still renders — tier 2 derives a default from form metadata (§5.3). **The §5.1.1 spike is precisely about step 5's editor**: whether `of-type="Multiple"` presents a textarea there or a single-line box.

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
| `summaryField` | no | Field backing the sparkle popover. Sparkle shows whenever the named attribute **exists in metadata**, even with zero populated records, and the popover renders "No summary yet" (§9, owner 2026-08-24). Omitted, or naming an attribute that does not exist → sparkle hidden. |
| `fields[].name` | yes | Logical name. For lookups, the **lookup attribute** name (`sprk_mattertype`), not `_sprk_mattertype_value`. |
| `fields[].span` | no | `1`–`3`. Default derived from renderer (textarea → `columns`, else `1`). **Clamped to `columns` by the resolver** — `FieldGrid` does not validate, and an over-wide span silently creates an implicit extra grid track (§4). |
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

The control is **form-embedded**, and the R1 write path already requires every editable *text* field to be present on the form (`getXrmPage().getAttribute(name)` → `throw new Error("Field '…' not on form")`, [MatterHeaderView.tsx:185](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L185)). That existing constraint is an asset: the form context can supply almost everything config would otherwise have to state — **at zero network cost**.

> **Correction (2026-08-22)**: the invariant is weaker than stated. Only `saveText` throws; `saveLookup` ([`:220-227`](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L220-L227)) `console.warn`s and silently no-ops on a missing attribute. The generic control **MUST unify these on the throwing path** — a config referencing a field that is not on the form is an authoring error and must surface loudly, not silently drop the user's edit.

| Needed | Primary source (no network) | Fallback |
|---|---|---|
| Entity logical name | `context.mode.contextInfo.entityTypeName` | `context.page.entityTypeName` |
| Record id | `context.mode.contextInfo.entityId` | `context.page.entityId` |
| Field label | `formContext.getControl(n).getLabel()` | metadata `displayName` → humanized logical name |
| Attribute type → renderer | `getAttribute(n).getAttributeType()` | metadata `attributeType` |
| Option-set options | `getAttribute(n).getOptions()` | metadata `optionSet` |
| Required marker | `getAttribute(n).getRequiredLevel()` | config `required` |
| Lookup target entity | metadata `Targets[0]` on the lookup attribute | config `fields[].lookup.entity` |
| Lookup id/name fields | target entity's `primaryIdAttribute` / `primaryNameAttribute` | config `fields[].lookup.{idField,nameField}` |

**Citation fix (2026-08-22)**: the `contextInfo` idiom is proven in [`VisualHostRoot.tsx:246-253`](../../src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx#L246-L253) and documented in [`pcf-build-scaffold.md` gotcha #3](../../.claude/patterns/pcf/pcf-build-scaffold.md). [`TrackingFieldTrio/index.ts:337-348`](../../src/client/pcf/TrackingFieldTrio/index.ts#L337-L348) demonstrates a **different** untyped surface — `context.page.entityTypeName` / `context.page.entityId` — and is cited above as the fallback, not as `contextInfo` evidence. Both require a type cast; neither is in `@types/powerapps-component-framework`.

#### Metadata access — reuse `IDataverseClient`, do not build a new path

The prior draft routed lookup resolution through `EntityDefinitions/ManyToOneRelationships`. That endpoint is reachable only by **raw same-origin `fetch`** — `Xrm.WebApi` cannot query it — as the cited precedents show ([`PolymorphicResolverService.ts:479-484`](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts#L479-L484), [`TodoRegardingUpdateBuilder.ts:291-295`](../../src/client/shared/Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts#L291-L295); both use a host-relative URL + `credentials: 'include'`, so no `getClientUrl()` is needed). It is also the wrong endpoint for this job: `ReferencingEntityNavigationPropertyName` matters for `@odata.bind` **writes**, and RecordHeader writes through the form buffer.

**Decision (2026-08-22): use the existing shared-library contract instead.**

[`IDataverseClient.retrieveEntityMetadata(entityName)`](../../src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts#L171) already returns exactly the shape this design needs — `{ primaryIdAttribute, primaryNameAttribute, attributes: Record<string, { attributeType, format, displayName, isPrimaryName, isPrimaryId, optionSet }> }` — and [`XrmDataverseClient`](../../src/client/shared/Spaarke.UI.Components/src/services/XrmDataverseClient.ts) implements it over `Xrm.Utility.getEntityMetadata` + `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', …)`. Note the second call proves `Xrm.WebApi` **can** reach `EntityDefinition` for the DisplayName expand ([`:224-251`](../../src/client/shared/Spaarke.UI.Components/src/services/XrmDataverseClient.ts#L224-L251)) — so this path stays inside the project's "`Xrm.WebApi` / `Xrm.Page` only" MUST, while raw-`fetch` `ManyToOneRelationships` would have needed an explicit carve-out.

**One gap, one small extension**: `EntityAttributeMetadata` does not project lookup `Targets`. `Xrm.Utility.getEntityMetadata` **does** return it — proven in shipped code at [`FieldUpdateReconcileTab.tsx:91, 149`](../../src/client/shared/Spaarke.Communication.Components/src/components/ReconcileTabs/FieldUpdateReconcileTab.tsx#L149) (`const targets = attr?.Targets ?? attr?.targets`). R2 adds `targets?: string[]` to `EntityAttributeMetadata` and one line to `projectAttribute` in `XrmDataverseClient`. Per CLAUDE.md §11 this is **extending an existing component**, not a new one — and DataGrid benefits too.

**This is what deletes `LOOKUP_META`.** Matter's lookups point at non-conventional `*_ref` entities (`sprk_mattertype_ref` / `sprk_mattertype_refid` / `sprk_mattertypename`), which is exactly why R1 hard-coded them — but those three values are `Targets[0]` plus that target's own `primaryIdAttribute` / `primaryNameAttribute`, two `retrieveEntityMetadata` calls away.

**Call budget + caching (new NFR)**: Matter's shape (2 lookups, 1 target entity each) costs **1 host-entity metadata call + 1 per distinct lookup target ≈ 3 cold calls** on first render. `XrmDataverseClient.retrieveEntityMetadata` has **no cache today** and itself issues two network calls per invocation. R2 MUST add a module-level, page-session metadata cache keyed by entity logical name — mirroring the `_navPropCache` in [`PolymorphicResolverService.ts:451`](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts#L451) — and MUST restore R1's TTI budget (**≤300 ms cached / ≤800 ms cold**, R1 NFR-01), which this design had dropped while adding pre-render network calls.

> ✅ **Discovery task CLOSED — live-verified 2026-08-24 on `spaarkedev1`.** `sprk_matter.sprk_mattertype` → target `sprk_mattertype_ref`, `PrimaryIdAttribute = sprk_mattertype_refid`, `PrimaryNameAttribute = sprk_mattertypename`. `sprk_matter.sprk_practicearea` → target `sprk_practicearea_ref`, `sprk_practicearea_refid`, `sprk_practiceareaname`. **Both match R1's hard-coded `LOOKUP_META` exactly** ([`MatterHeaderView.tsx:89-100`](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L89-L100)) — it can be deleted with confidence, and the `fields[].lookup` escape hatch is **not** needed. Keep it out of the v1.0 schema; §5.2 stays as written.
>
> The same query confirmed the convention is **non-uniform** (`sprk_projecttype_ref` and `sprk_eventtype_ref` both use `sprk_name`), which is precisely why this must come from metadata rather than a naming rule.

---

### 5.4.1 D-5 analysis — the two metadata paths, and what actually differs

**Both options answer the same question.** When the header renders a lookup cell — say Matter's *Matter Type* — the control must know three things that are not in the record data: which entity the lookup points at, and that entity's id and name columns. Without them the cell **renders blank and the type-ahead search returns nothing**. That is the concrete failure mode, and it is identical under both options.

**What the user sees: no difference.** Both produce the same header. This is purely an engineering-path choice.

**How they differ in practice:**

| | Option 1 — reuse `IDataverseClient` (recommended, currently written into §5.4) | Option 2 — raw `fetch` `EntityDefinitions/ManyToOneRelationships` |
|---|---|---|
| How it runs | `Xrm.Utility.getEntityMetadata` + `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', …)`, behind the existing `XrmDataverseClient` | `fetch('/api/data/v9.0/EntityDefinitions(…)/ManyToOneRelationships', { credentials: 'include' })` |
| Complies with this project's "`Xrm.WebApi` / `Xrm.Page` only" MUST | ✅ Yes | ❌ **No** — `Xrm.WebApi` cannot query that endpoint. Needs a documented CLAUDE.md §6.5 path-A exception |
| Calls needed to fill the whole §5.4 table | **One call per entity** — the same response carries attribute types, display labels, option sets, primary id/name, and (with the `targets` extension) lookup targets | **Two kinds of call** — `ManyToOneRelationships` returns *only* relationship info, so a separate metadata call is still required for types, labels and option sets |
| New code | `targets?: string[]` on `EntityAttributeMetadata` + one line in `projectAttribute` + a page-session cache | A third copy of metadata-fetch logic (`PolymorphicResolverService` and `TodoRegardingUpdateBuilder` each already have one) |
| Who else benefits | DataGrid inherits both the `targets` field and the cache | Nobody |
| Returns data we do not need | No | Yes — `ReferencingEntityNavigationPropertyName` exists for `@odata.bind` **writes**; RecordHeader writes through the form buffer and never uses it |
| Failure mode when unavailable | Clear thrown error ("requires `Xrm.Utility`") | Same-origin cookie auth; can fail as an opaque 401/403 in some iframe contexts |

**Recommendation: option 1.** Fewer network calls before first paint (which matters — §5.4 adds a new TTI risk), less new code, no rule exception to write and defend, and the one small extension improves an existing shared contract that DataGrid also consumes. Option 2's only argument is consistency with two older services — and those two are consistent with each other precisely because they need the navigation-property name for writes, which this control does not.

---

## 6. Shared-library work

These are required regardless of config mechanism — and note that **the withdrawn four-PCF plan needed most of them too**: its own §5.2 Invoice field list (currency amount, due date, status) cannot render correctly with today's renderer set. Today a Money value renders as `12500` and a DateTime as `2026-08-21T00:00:00Z`, because `TextField` does `String(value)`.

### 6.1 New / extended field renderers

**Re-scoped 2026-08-24 against the live-verified §9 field lists** — the "Confirmed consumer" column is the CLAUDE.md §11 cost-of-doing-nothing test. All four renderers now have real consumers:

| Renderer | Covers | Confirmed consumer (live-verified) | Verdict |
|---|---|---|---|
| `DateField` | `DateTime` / `DateOnly` | Invoice `sprk_invoicedate`; WA `sprk_responseduedate`; Event `sprk_duedate` / `sprk_finalduedate`; Project `sprk_openeddate` / `sprk_closeddate` | ✅ **In** — all four entities |
| `DateField` — **datetime** mode | `DateTime` / `DateAndTime` | Event `sprk_plannedstart` / `sprk_plannedend` (and `sprk_actualstart` / `sprk_actualend`) — **confirmed `DateAndTime` in metadata** | ✅ **In** — resolved; fold into `DateField` keyed off the metadata `Format`, not a separate component |
| `NumberField` | `Integer`, `Decimal`, `Double`, `Money` | Invoice **`sprk_totalamount` (Money)**; Event `sprk_estimatedminutes` (Integer) | ✅ **In** — currency is the driver |
| `OptionSetField` **(extend)** | `Picklist`, `Status`, `State` | Invoice `sprk_invoicestatus` + `sprk_visibilitystate`; Event `sprk_eventstatus` + `sprk_priority`; WA `sprk_priority`; Project `statuscode` | ✅ **In** — all four |
| `BooleanField` | `Boolean`, `TwoOptions` | 🔺 **Reversal — it does have consumers.** Live metadata shows `sprk_highpriority` and `sprk_monitor` on **all four** entities, plus `sprk_issecure` (Project / WA / Invoice), `sprk_workspaceflag` (Project / Invoice), `sprk_isurgent` (Event). `sprk_monitor` in particular drives the Navigator's Monitored list, so surfacing it in a header is a real use | ✅ **In** — the 2026-08-22 "no consumer" call was wrong; it was based on the seed's field list, not the schema |

`OptionSetField` currently is display-only ([OptionSetField.tsx](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/OptionSetField.tsx)); add edit via Fluent `Dropdown` fed by `getOptions()`. `NumberField` takes currency symbol + precision from metadata and right-aligns per `defaultAlignFor`.

> **Note on Event's "type" field**: the seed assumed an option set. It is a **lookup** (`sprk_eventtype_ref`), so it needs no new renderer — it routes to the existing editable `LookupField`. One less renderer than the seed implied.

**[`TextField.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/TextField.tsx) is the canonical contract every new renderer copies** (verified 2026-08-22):

- Props `{ label, value, span: 1|2|3, required?, onSave?, disabled? }`; the renderer applies its own `<div style={{ gridColumn: \`span ${span}\` }}>` — `FieldGrid` never does
- Editability gate: `const editable = typeof onSave === 'function' && disabled !== true` — omitting `onSave` yields read-only, backward-compatibly
- State machine: `editing` / `draft` / `saving`; `useEffect` re-syncs `draft` from the external value **only when not editing**; `commit()` no-ops when unchanged; **on rejection, revert the draft and stay in edit mode**
- Keyboard: Enter commits / Escape cancels (TextareaField uses **Ctrl/Cmd+Enter** because plain Enter inserts a newline); blur commits
- Fluent surface: `appearance="filled-lighter" size="small"`, `autoFocus`, `disabled={saving}`, trailing `<Spinner size="tiny">`; module-scope `makeStyles`; semantic tokens only (ADR-021); React 16/17-safe hooks only (ADR-022)
- Read-mode value cell: `colorNeutralBackground3` + `borderRadiusMedium` + `minHeight: 2em` (OOB input parity, v1.0.3); label `fontSizeBase300` / `colorNeutralForeground1` (v1.0.4) — copy **TextField's** label styles, not `OptionSetField`'s stale ones

**Two conventions — settled 2026-08-24:**

1. **Empty-string handling — ✅ ADOPTED.** Em-dash `''` everywhere. `TextField` currently renders `''` as an empty box while `TextareaField` and `OptionSetField` em-dash it; align `TextField` to the majority. Touches a shipped component Matter consumes, so parity QA must cover it.
2. **Required marker — ⬜ NOT adopted in R2.** Only `TextField` renders the `*` today, and it stays that way. **Known consequence**: once `required` is config- and metadata-driven, a required date / number / option-set / boolean / lookup cell shows **no** visual required indicator, so required-ness is invisible on every renderer except text. Dataverse still enforces it on save, so this is a discoverability gap, not a data-integrity one. Revisit if UAT flags it.

### 6.5 Lookup editing — use the OOB picker (owner decision 2026-08-25)

R1's header renders a **custom** inline type-ahead for lookups. Every other lookup in the app uses the native Dataverse picker. R2 adopts the OOB one.

**Mechanism** — the proven in-repo pattern, already used by [`CommunicationActionsApp.tsx:405-413`](../../src/client/pcf/CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx#L405-L413) and `CommunicationConnectionsApp`:

```ts
const results = await xrm.Utility.lookupObjects({
  entityTypes: [targetEntity],     // from metadata Targets[0] (§5.4)
  allowMultiSelect: false,
});
// -> [{ id, name, entityType }] — exactly the form-buffer setValue payload shape
```

The returned shape is already what `saveLookup` stages into the form buffer, so the write path is unchanged.

**What this buys**

- Native fidelity for free: *Records / Recent records* tabs, entity icons, secondary text, **+ New**, **Advanced** — plus Dataverse security trimming and view configuration, none of which the custom control implements
- Consistency with every other lookup surface in the product
- **Deletes code.** The custom OData `$filter`/`$top=10` search builder leaves the header path entirely — so it drops out of the §6.2 hoist list rather than being generalized over metadata. Net simplification of the largest remaining piece of machinery.

**What it costs**

- The picker is a **modal**, not inline type-ahead. One extra click for a known value.
- ⚠️ **This changes Matter's behaviour**, so the pixel-parity criterion must be qualified: *identical except that lookup cells now open the OOB picker instead of an inline dropdown.* Stated explicitly so the parity QA does not flag it as a regression.
- `entityTypes` must come from metadata `Targets` (§5.4) — one more reason FR-21's `targets` projection is load-bearing.

**Read-only lookups** still render via the display-only `fields/LookupField`; only the editable path changes.

#### The picker's "+ New" — modal or navigate-away?

**It can be a modal, but the PCF does not control it.** `lookupObjects` has no option for this. The behaviour is set by the **target** entity:

| Target entity config | What "+ New" does |
|---|---|
| `IsQuickCreateEnabled = true` **and** a published Quick Create form | ✅ Opens the **quick-create flyout panel** — user stays on the record, no navigation |
| Either missing | ❌ **Navigates away** to the full form, losing form context |

**Live status of every lookup target in the §9 layouts (2026-08-25):**

| Target | Used by | `IsQuickCreateEnabled` | Quick Create form | "+ New" today |
|---|---|---|---|---|
| `sprk_matter` | Agreement → Regarding Matter | ✅ **True** | ✅ "Matter quick create" | ✅ **Flyout** |
| `contact` | WA → Assigned To | ❌ False | ✅ "Contact Quick Create" *(exists but inactive)* | ❌ Navigates |
| `sprk_mattertype_ref` | Matter → Matter Type | ❌ False | ❌ none | ❌ Navigates |
| `sprk_practicearea_ref` | Matter → Practice Area | ❌ False | ❌ none | ❌ Navigates |
| `sprk_projecttype_ref` | Project → Type | ❌ False | ❌ none | ❌ Navigates |
| `sprk_eventtype_ref` | Event → Type | ❌ False | ❌ none | ❌ Navigates |
| `sprk_agreementtype` | Agreement → Type | ❌ False | ❌ none | ❌ Navigates |

**Recommendation — split the targets by kind, rather than blanket-enabling:**

- **`contact`** — enable quick create (flip `IsQuickCreateEnabled`; the form already exists). Users legitimately create contacts mid-task, and this is a one-setting change with an immediate payoff.
- **`sprk_matter`** — already correct, nothing to do.
- **The five `*_ref` / type tables** — these are **admin-managed taxonomies** (matter type, practice area, project type, event type, agreement type). Letting any user mint a new "Matter Type" from a header lookup pollutes the taxonomy and is arguably a governance problem, not a UX win. **Recommend leaving quick create off**, and accepting that "+ New" navigates away — it is rarely the right action on these fields anyway.

**Not R2's work either way.** These are Dataverse configuration changes on tables R2 does not otherwise touch. Recorded here so the decision is explicit rather than discovered during UAT. If the owner wants the `contact` flag flipped, it is a one-line admin change that needs no code.

### 6.2 Hoist the generic machinery out of the view

Move from `MatterHeaderView.tsx` into the shared library so it exists once:

- Form-buffer staging (`saveText` / `saveLookup` via `Xrm.Page.getAttribute().setValue()`) — the R1 v1.0.7 dirty-state pattern, which must be preserved exactly (it exists because writing straight to Dataverse re-rendered the whole PCF on every edit)
- The pending-changes buffer and `pendingX[name] ?? values?.[name]` display resolution
- `projectLookup()` (`_field_value` + `@OData.Community.Display.V1.FormattedValue` → `ILookupItem`)
- ~~The lookup OData search builder~~ — **removed from the hoist 2026-08-25**: §6.5 replaces the custom type-ahead with `Xrm.Utility.lookupObjects`, so there is no search builder left to generalize. The largest single piece of machinery on this list is deleted rather than moved.

Plus a lookup grid cell that owns its own `span` (the R1 view hand-rolled one at [`MatterHeaderView.tsx:290-309`](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L290-L309) because the editable `LookupField` had no `span` prop).

Proposed home: `hooks/useRecordHeaderFields.ts` + `components/RecordHeader/RecordHeaderFields.tsx`. Neither name exists in `src/` today (grep-verified 2026-08-22). Exact split is a `/design-to-spec` decision.

**This breaks no library boundary.** `.claude/constraints/pcf.md` says shared components MUST NOT reference PCF-specific APIs — `Xrm.Page` is a *host form* API, not a PCF API, and the shared lib already uses it: [`services/FieldMappingHandler.ts:478-512, 552-561`](../../src/client/shared/Spaarke.UI.Components/src/services/FieldMappingHandler.ts#L552-L561) has its own `getXrmPage()` walking `window.Xrm || window.parent.Xrm`, and `useRecordFieldValues` / `useRelatedCount` call `Xrm.WebApi` directly via `getXrm()`. The established split is: **components are props-driven; Xrm access lives in `hooks/` and `services/` behind a window/parent walker** — exactly where §6.2 puts this code. ADR-022 governs React API surface, not Xrm.

**Deduplicate while hoisting**: `MatterHeaderView.getXrmPage()` ([`:132-142`](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L132-L142)) and `FieldMappingHandler.getXrmPage()` are near-identical. Land **one** shared `getXrmPage()` and migrate both. Note [`utils/xrmContext.ts`](../../src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts)'s `XrmContext` interface exposes `WebApi / Navigation / Utility / App` but **no `Page`** — it needs the member.

### 6.3 Config resolver

`resolveHeaderConfig(manifestJson, formMetadata) → ResolvedHeaderConfig` — pure, no React, no I/O, exhaustively unit-testable. Deliberately mirrors [`configResolution.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts), which is the proven in-repo shape for this problem. Confirmed 2026-08-22:

- **Validation** mirrors [`isValidDataGridConfiguration`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts#L479) — a shallow, **non-throwing** discriminator guard (`_version === '1.0'` + `Array.isArray(fields)`) living beside the schema type; caller `console.warn`s and falls through
- **Output** is fully resolved, every field non-optional after merge: `{ title, columns, summaryField?, fields: Array<{ name, label, span, renderer, readOnly, required, lookup? }> }` — the analogue of `ResolvedColumn`
- **Per-field merge chain** mirrors `buildResolvedColumn`: `config override ?? form-control/metadata value ?? humanizeLogicalName(name)`
- **Renderer derivation** mirrors [`rendererFromAttributeType`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts#L197) (`Money → currency`, `DateTime → date | datetime` by `format`, `Picklist/Status/State`, `Lookup`, `Boolean`), retargeted to the header renderer vocabulary
- **§5.3's skip list already matches** `synthesizeColumnsFromMetadata`'s `skipSet` **verbatim** ([`:361-371`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts#L361-L371)) — no divergence to reconcile
- **Must clamp `span = min(span, columns)`** — `FieldGrid` does not (see §4)

### 6.4 Toolbar slot auto-hide

`sprk_todo` supports 11 parents; `sprk_memo` supports 6. On an entity with To Dos but no Memo lookup (Contact, Document, Organization, Analysis, Communication), the annotation icon currently still renders and opens a Notepad that cannot save. `useRecordHeaderToolbarActions` must omit the slot when [`buildMemoFilterForParent`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L130) returns `null` — it already returns `null` for unsupported parents, so this is a small hook change, not new logic.

**Scope correction (2026-08-22)**: the sparkle half is **already done at the component layer** — `HeaderToolbar` renders `AiSummaryPopover` only when `aiSummary` is passed ([`HeaderToolbar.tsx:154`](../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx#L154)), so the control just omits the prop. Only the annotation slot needs hook work.

**Sparkle visibility rule (owner, 2026-08-24) — see §9 for the full table.** The trigger is **attribute existence in metadata, not value population**: show the sparkle whenever `summaryField` names an attribute that exists on the entity, even if every record is currently empty, and render an explicit "No summary yet" state in the popover. Summary columns are being populated by a separate project; R2 must not hide the affordance because the data has not landed yet.

**Also settled (2026-08-24)**: the To Do checkmark on entities outside the 11-parent map has the same defect as the annotation slot. Treat it identically — `buildTodoFilterForParent` already returns `null` for unsupported parents, so omit the slot on `null`.

---

## 7. Structural items from the 2026-07-05 seed — both dropped

### 7.1 DEF-06 (`exports` field + `moduleResolution: bundler`) — DROPPED

The seed's rationale was "four new PCFs land at once, so do the migration once for all of them." R2 now ships **one** PCF, so the leverage is gone while the cost — a repo-wide `pcf-scripts/tsconfig_base.json` bump requiring every PCF solution ZIP to be rebuilt and smoke-tested — is unchanged. R1 already attempted and reverted this (see [plan-extension.md](../record-header-and-notepad-r1/plan-extension.md) task 063). It should be its own migration project when someone wants it, not a passenger here.

**Consequence**: R2 keeps R1's `@spaarke/ui-components/dist/*` deep-path import convention, which is also **mandatory** for bundle size per the authoring guide's optimization triad (`featureconfig.json` + `webpack.config.js` + deep-path imports — ~40 KB vs 1.6 MB without). Do not "clean up" those imports. Deep paths work because the shared lib builds with plain `tsc` (`outDir: dist`, per-file ES-module emission mirroring `src/`) and its `package.json` has **no `exports` field** — verified 2026-08-22.

> ⚠️ **Booby trap for the implementer**: [`MatterHeaderView.tsx:53-62`](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L53-L62) (a v1.0.12 comment) states "the shared lib's `package.json` now defines an `exports` map." **It does not** — the comment survived the task-063 revert. Delete it during the migration.

> ⚠️ **Binding build obligation**: any PCF on deep `dist/*` imports MUST wire the `prebuild` / `prebuild:prod` guard `node ../../shared/Spaarke.UI.Components/scripts/ensure-dist-fresh.js` (binding since 2026-07-07, [`pcf-deploy/SKILL.md:124`](../../.claude/skills/pcf-deploy/SKILL.md)). The new/renamed `RecordHeader` PCF folder must carry it forward from `MatterHeader/package.json`.

### 7.2 DEF-08 (promote `useSprkMemoRepository`) — DROPPED

The seed made promotion conditional on "does any PCF render memo content inline?" The answer for a configurable header is **no** — it launches the Notepad, it does not embed it. No second consumer, so per CLAUDE.md §11 the promotion is unjustified. The trigger remains where R1 left it: whenever `EventDetailSidePane/MemoSection.tsx` is next touched (DEF-04).

---

## 8. Control identity + migration

**Corrected identity (2026-08-22)** — the prior draft conflated two names:

| Thing | Value | Where |
|---|---|---|
| Control namespace + constructor | `Spaarke.Records` / `MatterHeader` v1.0.20 | [`ControlManifest.Input.xml:3`](../../src/client/pcf/MatterHeader/control/ControlManifest.Input.xml#L3) |
| **Control schema name** (what forms bind to) | `sprk_Spaarke.Records.MatterHeader` | `Solution/customizations.xml:16`, `Solution/solution.xml:88` |
| **Solution unique name** | **`MatterHeaderPcf`** (unmanaged; publisher `Spaarke` / prefix `sprk`) | `Solution/solution.xml:7,14-27` |

The design previously called the solution `sprk_Spaarke.Records.MatterHeader`. That is the control schema name.

Changing `constructor=` creates a **new** control with a new schema name and orphans the form binding — it is not a rename. **Three** options, not two:

| Option | Real cost | Verdict |
|---|---|---|
| **A.** Keep `constructor="MatterHeader"`, change `display-name-key` → "Spaarke Record Header" | **Zero migration.** Display names are manifest-driven and refresh on solution upgrade, so makers see "Spaarke Record Header" in Add-Control on every entity. Only the *internal* schema name and the solution name stay Matter-flavoured. Adding `layoutJson` is additive and upgrade-safe. | ❌ Not chosen |
| **B.** Ship `Spaarke.Records.RecordHeader`, re-bind Matter, retire `MatterHeader` | One form edit, plus parity `layoutJson` authoring, baseline capture, pixel-parity QA, and a two-step old-control retirement | ✅ **CHOSEN — owner, 2026-08-22** |
| **C.** Keep `constructor="MatterHeader"` unchanged in every respect | Control reads "MatterHeader" to makers on Invoice and Event forever | ❌ |

The 2026-08-21 decision picked B on the premise that A meant living with "MatterHeader" in the maker UI. That premise was wrong — A fixes the maker-visible name without migration. **Re-presented to the owner 2026-08-22 with the corrected trade; B reaffirmed.** Clean identity is worth the one-time migration, and because forms are solution-transported in this org (§13, owner-confirmed 2026-08-22), the Matter re-bind genuinely is **once**, not once-per-environment.

**Retirement timing (owner, 2026-08-24)**: retire `MatterHeaderPcf` **as soon as this project is delivered** — no dormant-release rollback window. The pixel-parity QA at step 4 is the gate that earns that.

**Migration sequence** — longer than "one form edit, once", but bounded:

1. New repo Solution artifacts: `Controls/sprk_Spaarke.Records.RecordHeader/`, new `solution.xml` UniqueName (e.g. `RecordHeaderPcf`), same publisher block, new `pack.ps1` constants (`$solutionName`, `$controlSchemaName`, `$version` — `pack.ps1:22-24`). No new publisher needed.
2. `pac solution import --publish-changes`.
3. Form edit per [`matter-form-binding-instructions.md`](../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md): classic designer, remove the old control from `sprk_matternumber`'s control list, add "Spaarke Record Header", re-enter `boundField` / `title` / `showVersion` / paste `layoutJson`, re-tick Web/Phone/Tablet, save + publish. ~20–30 min maker task. **Done once in dev and carried downstream by the solution** (§13, owner-confirmed) — R1's per-environment framing does not apply here.
4. Pixel-parity QA vs v1.0.20; version footer is the in-UI check that the swap took.
5. Retirement (on delivery, per D-4) is **two ordered steps**: remove every form reference and publish, *then* delete the CustomControl component. Deleting the unmanaged `MatterHeaderPcf` solution container does **not** delete the control, and dependency tracking blocks deletion while a form still references it.

**No data loss either way** — `boundField` is `usage="bound"` on `sprk_matternumber`; the control only replaces rendering.

**Rollback**: none held open past delivery, per D-4. The rollback window exists only *between* step 3 (re-bind) and step 5 (retirement) — during it, re-adding the old control to the field's control list reverts instantly with no redeploy. Step 4's pixel-parity QA is what earns closing that window on delivery.

**Version sync is 5 locations, not 4** (`src/client/pcf/CLAUDE.md` says 4; `pcf-deploy` adds `pack.ps1`): `ControlManifest.Input.xml`, `control/version.ts`, `Solution/solution.xml`, `Solution/Controls/.../ControlManifest.xml`, `pack.ps1`.

**Rename surface beyond the manifest**: `package.json` `"name"`, `control/version.ts`, `data-testid="matter-header-version"` (asserted in tests), and the whole Matter-fixtured test suite (see §14).

Matter is therefore both the last migration and the strongest regression test: R1's live-QA behaviors — form-buffer dirty state with no re-render flash, 25%×35% Notepad modal, `openTodos` SmartTodo filter, `sprk_mattersummary` sparkle, dark/high-contrast theming — must all survive unchanged.

---

## 9. Per-entity requirements

> ### ✅ LIVE-VERIFIED 2026-08-24 against `spaarkedev1` — discovery for §9 is CLOSED.
>
> Queried directly via the Dataverse Web API (`EntityDefinitions` + record counts). The 2026-07-05 seed's field lists were substantially wrong: **six drafted fields do not exist at all**, one entity's primary name attribute was wrong in the seed *and* in the 2026-08-22 offline pass, and one entity's "type" field is a lookup rather than an option set. Every row below is now schema truth, not inference.
>
> Two live-code defects surfaced in the process — see §9.1.

**Verified per-entity field lists** (⚠️ = differs from the seed; 🔺 = differs from the 2026-08-22 offline pass too):

| Entity | Primary name | Verified fields | Renderers needed |
|---|---|---|---|
| `sprk_project` | 🔺 **`sprk_projectnumber`** (required) — **not** `sprk_projectname`, though that also exists as a plain String | `sprk_projectname`, `sprk_projectdescription` (Memo), `sprk_projecttype_ref` / `sprk_mattertype` / `sprk_practicearea` / `sprk_assignedattorney1` (Lookup), `ownerid`, `statuscode` · ⚠️ **no custom status Choice** — the only Picklist is `sprk_accesspermission` · ⚠️ **no start / target-end date**; the real date fields are `sprk_openeddate` / `sprk_closeddate` / `sprk_lastreviewdate` / `sprk_nextreviewdate` (all **DateOnly**) · Booleans: `sprk_highpriority`, `sprk_monitor`, `sprk_issecure`, `sprk_workspaceflag` · 🔺 **`sprk_description` does not exist** | **date**, optionset, boolean |
| `sprk_workassignment` | `sprk_name` (required) | `sprk_workassignmentnumber`, `sprk_description` (Memo), `sprk_priority` (Choice 100000000 Low / 001 Normal / 002 High / 003 Urgent), `sprk_assignedto` + `sprk_regardingmatter` (Lookup), `sprk_responseduedate` (**DateOnly — the only DateTime on the entity**), `statuscode` · ⚠️ **no custom status Choice**, **no start date**, **no estimated-hours field** · Booleans: `sprk_highpriority`, `sprk_monitor`, `sprk_issecure` | **date**, optionset, boolean |
| `sprk_invoice` | `sprk_name` (required) | `sprk_invoicenumber`, `sprk_description` + `sprk_aisummary` (Memo), **`sprk_totalamount` (Money)**, `sprk_invoicedate` (**DateOnly — the only DateTime on the entity**), `sprk_invoicestatus` (Choice: ToReview / Reviewed), `sprk_visibilitystate` (Choice ×6 — the richer lifecycle), `sprk_matter` / `sprk_project` / `sprk_document` / `sprk_vendororg` (Lookup), `ownerid` · ⚠️ **confirmed: NO due-date field exists** · Booleans: `sprk_highpriority`, `sprk_monitor`, `sprk_issecure`, `sprk_workspaceflag` | **currency, date**, optionset, boolean |
| `sprk_event` | ⚠️ **`sprk_eventname`** (required) | `sprk_eventnumber`, `sprk_description` (Memo), `sprk_eventstatus` (Choice 0 Draft → 7 Archived), ⚠️ **`sprk_eventtype_ref` is a LOOKUP**, `sprk_priority` (Choice 100000000–003), `sprk_assignedto` (Lookup) · **dates**: `sprk_duedate` / `sprk_finalduedate` / `sprk_basedate` / `sprk_completeddate` (DateOnly) and 🔺 **`sprk_plannedstart` / `sprk_plannedend` / `sprk_actualstart` / `sprk_actualend` (DateAndTime — confirmed present)** · Booleans: `sprk_highpriority`, `sprk_isurgent`, `sprk_monitor` · 🔺 **`sprk_location` does NOT exist**; nor do `scheduledstart` / `scheduledend` / `sprk_startdate` | **date + datetime**, optionset, boolean |
| `sprk_matter` | as R1 v1.0.20 | unchanged | none |

**Option-set values** (for config authoring and test fixtures): `sprk_invoicestatus` `100000000=ToReview, 100000001=Reviewed` · `sprk_visibilitystate` `…000=Invoiced, 001=InternalWIP, 002=PreBill, 003=Paid, 004=WrittenOff, 005=Approved` · `sprk_eventstatus` `0=Draft, 1=Open, 2=Completed, 3=Closed, 4=On Hold, 5=Cancelled, 6=Reassigned, 7=Archived` · `sprk_priority` (Event **and** Work Assignment, identical) `100000000=Low, 001=Normal, 002=High, 003=Urgent`.

### Summary fields + the sparkle (owner decision 2026-08-24)

**Live population counts on `spaarkedev1`:**

| Entity | Summary column | Populated |
|---|---|---|
| `sprk_matter` | `sprk_mattersummary` | **1 of 55** |
| `sprk_matter` | `sprk_recordsummary` | **0 of 55** (exactly the R1 trap) |
| `sprk_invoice` | `sprk_aisummary` | **0 of 10** |
| `sprk_project` | `sprk_financialsummary` · `sprk_performancesummary` · `sprk_tasksummary` | **0 of 18** each |
| `sprk_workassignment` · `sprk_event` | **no summary column exists at all** | n/a |

**Owner decision (2026-08-25, superseding the 2026-08-24 "R2 creates the columns" decision): standardize on `sprk_recordsummary` everywhere, and the owner has ALREADY created the columns.**

**Rationale for the name**: `sprk_aisummary` collides conceptually with Microsoft's OOB "AI summary" features. `sprk_recordsummary` is unambiguous and vendor-neutral.

**Live verification 2026-08-25** — `sprk_recordsummary` (Memo) exists on **all six** rollout entities, 0 populated on each:

| Entity | `sprk_recordsummary` | `sprk_aisummary` | `sprk_mattersummary` |
|---|---|---|---|
| `sprk_matter` · `sprk_project` · `sprk_workassignment` · `sprk_event` · `sprk_invoice` · `sprk_agreement` | ✅ EXISTS (0 populated) | ❌ absent (400) | ❌ absent (400) |

**Consequences:**

- **FR-22 changes from "create three columns" to "verify + remediate residual references".** No `dataverse-create-schema` work; R2 returns to a pure client surface plus one Dataverse **data** fix (below).
- **The shared lib already has the right constant.** [`RECORDSUMMARY_FIELD = 'sprk_recordsummary'`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L90) has existed since R1. R2's `summaryField` default should be that constant; per-entity `layoutJson` need not set `summaryField` at all.

#### 🔴 Two live breakages caused by the column deletions — must be fixed

| # | What | Impact | Fix |
|---|---|---|---|
| **RS-1** | [`MatterHeaderView.tsx:83`](../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx#L83) includes `sprk_mattersummary` in the `useRecordFieldValues` `$select` | 🔴 **The shipped `MatterHeaderPcf` v1.0.20 is broken right now.** The `$select` names a column that no longer exists → HTTP 400 → **the entire header fails to load on every Matter record**, not just the sparkle | Point at `RECORDSUMMARY_FIELD`. R2 fixes this by construction, but Matter's header is broken *until R2 ships* — consider a v1.0.21 hotfix |
| **RS-2** | `sprk_aitopicregistry` row **"Matter Summary"** (`sprk_topicname=matter-summary`, `sprk_playbookname=chat-summarize`) is **enabled** with `sprk_targetfield=sprk_mattersummary` | 🔴 The BFF OutputRouter `work_product` disposition leg writes to a column that no longer exists | **Dataverse data fix**: set `sprk_targetfield` = `sprk_recordsummary` on that row |

**Verified NOT broken** (checked 2026-08-25, so nobody re-investigates):

- The sibling registry row **"Matter Health Insight"** targets `sprk_performancesummary` — that column still exists on `sprk_matter` (HTTP 200), as do `sprk_financialsummary` and `sprk_tasksummary`.
- `InvoiceExtractionJobHandler.cs:384` mentions `sprk_aisummary` **only in a comment**. The generated text goes to a context variable (`extraction.aiSummary`, [`:236`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs#L236)), not a direct column write — so there is no code break. Fix the stale comment; separately confirm whatever consumes that variable targets `sprk_recordsummary`.
- All other `sprk_mattersummary` references are inside `MatterHeader` and its tests — i.e. entirely within the surface R2 rewrites.

> **Handoff unchanged**: R2 reads `sprk_recordsummary` and renders "No summary yet"; it does **not** write. Whoever owns summary generation populates it — now against one uniform field name on every entity, which is exactly what the standardization buys.

That changes §6.4's rule:

| Condition | Behaviour |
|---|---|
| `summaryField` configured **and** the attribute exists on the entity | ✅ **Show the sparkle** — even if every record is currently empty |
| Attribute exists, value is null/empty for this record | ✅ Sparkle visible; popover shows an explicit **"No summary yet"** empty state |
| No `summaryField` configured, or the named attribute does not exist in metadata | ❌ Hide the sparkle (`HeaderToolbar` already does this when `aiSummary` is omitted) |

This is the honest version of the R1 fix. R1's bug was a sparkle pointing at a column that held nothing while a *different* column held the data — a silent lie. A sparkle over a real, currently-empty column that says "No summary yet" is not a lie, and it is forward-compatible with the populating project.

### Proposed per-entity layouts (owner-confirmed 2026-08-24)

Starting layouts for each form's `layoutJson`, built from the live-verified field lists. `columns: 3` throughout. Makers can tune these without a code change — that is the point of the design — so treat them as the shipped default, not a contract.

`summaryField` is `sprk_recordsummary` on **every** entity (2026-08-25 standardization), so it can be omitted from `layoutJson` entirely and defaulted from `RECORDSUMMARY_FIELD`.

| Entity | `fields[]` (name · span) |
|---|---|
| `sprk_project` | `sprk_projectnumber`·1 *(req)* · `sprk_projectname`·2 · `sprk_projecttype_ref`·1 · `sprk_openeddate`·1 · `sprk_highpriority`·1 · `sprk_projectdescription`·3 |
| `sprk_workassignment` | `sprk_workassignmentnumber`·1 · `sprk_name`·2 *(req)* · `sprk_priority`·1 · `sprk_assignedto`·1 · `sprk_responseduedate`·1 · `sprk_highpriority`·1 · `sprk_description`·3 |
| `sprk_invoice` | `sprk_invoicenumber`·1 · `sprk_name`·2 *(req)* · `sprk_totalamount`·1 · `sprk_invoicedate`·1 · `sprk_invoicestatus`·1 · `sprk_highpriority`·1 · `sprk_description`·3 |
| `sprk_event` | `sprk_eventnumber`·1 · `sprk_eventname`·2 *(req)* · `sprk_eventtype_ref`·1 · `sprk_eventstatus`·1 · `sprk_plannedstart`·1 · `sprk_plannedend`·1 · `sprk_highpriority`·1 · `sprk_description`·3 |
| **`sprk_agreement`** *(added 2026-08-25)* | `sprk_name`·2 *(req)* · `sprk_agreementtype`·1 · `sprk_effectivedate`·1 · `statuscode`·1 · `sprk_regardingmatter`·1 · `sprk_agreementdescription`·3 — **no Boolean cell; see §9.2** |
| `sprk_matter` | as R1 v1.0.20 — unchanged |

**`sprk_highpriority` is deliberate**: it is what gives `BooleanField` a real consumer (§6.1 / §11). Without it in at least one layout, the renderer would be built for a field type nothing displays — the exact scope-creep CLAUDE.md §11 exists to catch. It is also genuinely useful on a header: a flag users set and read constantly.

Every layout exercises at least one new renderer, so the rollout doubles as the renderer test matrix: Project → date + boolean + optionset; WA → date + boolean + optionset; Invoice → **currency** + date + boolean + optionset; Event → **datetime** + date + boolean + optionset + lookup.

> **Main form GUIDs — live-verified 2026-08-24** (dev `spaarkedev1`): Matter `4fa382f2-c273-f011-b4cb-6045bdd6a665` · Project `5aa00242-5212-f111-8342-7ced8d1dc988` · Work Assignment `7e578eef-761d-f111-88b3-7c1e520aa4df` · Invoice `93aa1c69-0406-f111-8406-7c1e525abd8b` · Event `eaf22dcb-9aff-f011-8406-7c1e525abd8b` (**the entity has 10 forms — 8 are side-pane/modal/assign-work variants; bind only the "Event main form"**). Each entity also has a legacy "Information" form — do not bind that one. Environment-specific GUIDs: fine for dev acceptance criteria, not portable.

> **Finding that settles §5.4**: `sprk_mattertype` → `sprk_mattertype_ref` (Id `sprk_mattertype_refid`, Name `sprk_mattertypename`) and `sprk_practicearea` → `sprk_practicearea_ref` (Id `sprk_practicearea_refid`, Name `sprk_practiceareaname`) — **both match R1's hard-coded `LOOKUP_META` exactly, so it can be deleted.** And the convention is confirmed **non-uniform**: `sprk_projecttype_ref` and `sprk_eventtype_ref` both use **`sprk_name`** as their primary name. A hard-coded naming convention would break on two of the four rollout entities; reading `primaryNameAttribute` from metadata is required, not over-engineering.

### 9.2 `sprk_agreement` — added 2026-08-25 (owner)

Live-verified: PK `sprk_agreementid` · primary name **`sprk_name`** · entity set `sprk_agreements` · display name "Agreement" · **0 records** in `spaarkedev1`.

**Available fields**: `sprk_agreementdescription` + `sprk_recordsummary` (Memo) · `sprk_effectivedate` (**DateOnly — the only DateTime on the entity**) · `sprk_agreementtype`, `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingdocument`, `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2` (Lookup) · `sprk_accesspermission` (Picklist) · `statuscode` / `statecode` · `sprk_activesignalcount` (Integer) · `ownerid`.

**Owner resolved two of the three blockers on 2026-08-25 by changing Dataverse** — both verified live:

1. ✅ **Main form created.** Bind **"Agreement main form"** `59d88274-a1a0-f111-aaac-000d3a99d1d7`. (The legacy `Information` form `e009a1da-…` is **not** the target, consistent with every other rollout entity.)
2. ✅ **To Do + Notepad now supported.** `sprk_regardingagreement` (Lookup → `sprk_agreement`) now exists on **both** `sprk_todo` and `sprk_memo`. Agreement therefore gets the **full** toolbar — sparkle, To Do badge, Notepad badge — like the other five.
   - **Code change this requires**: add `sprk_agreement → sprk_regardingagreement` to **both** [`SUPPORTED_TODO_PARENTS`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L150) (11 → 12) and [`SUPPORTED_MEMO_PARENTS`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L105) (6 → 7). This is the **first** map change in R2 — §1 finding 1's "zero new work" claim now has exactly one exception.
3. ⚠️ **Still true — no Boolean fields at all.** No `sprk_highpriority`, `sprk_monitor`, or `sprk_issecure` on Agreement. Its layout carries no boolean cell; `BooleanField`'s justification rests on the other four entities, which is sufficient.

**Consequence for FR-16**: Agreement is no longer the auto-hide acceptance test. FR-16 still has real consumers — `contact`, `sprk_document`, `sprk_organization`, `sprk_analysis`, `sprk_communication` are To Do parents but **not** Memo parents, so the Notepad slot must hide on those. Test FR-16 against one of them instead.

**Also note**: Agreement has **0 records** in `spaarkedev1`, so acceptance testing needs a seeded record.

### 9.1 Schema-drift defects found during verification — **NOT in R2 scope; documented separately**

The verification pass found live code referencing **seven columns that do not exist**. **Owner direction 2026-08-24: capture each as a standalone issue document, grouped by record / component type, for evaluation as focused fix projects** — rather than folding them into R2 or dropping them into a defer list.

📄 **The authoritative write-ups now live in [`notes/issues/`](notes/issues/README.md)** — one per area, each self-contained enough to become a project brief:

| Issue | Area | Failure mode |
|---|---|---|
| [Event](notes/issues/ISSUE-event-schema-drift.md) | side pane · shared `EventTypeService` · one AI node doc string | Side pane cannot load any event |
| [Daily Briefing](notes/issues/ISSUE-daily-briefing-schema-drift.md) | `DailyBriefingCollector` | **Silent** — flagged Projects and Events vanish from every briefing |
| [Work Assignment](notes/issues/ISSUE-work-assignment-schema-drift.md) | create endpoint | HTTP 500 when a matter or due date is supplied |

The table below is retained as R2's **discovery record**. Two entries were empirically confirmed as hard failures by executing the exact queries against `spaarkedev1`:

| # | Site | Bad columns | Verified impact | Fix |
|---|---|---|---|---|
| **SD-1** | [`EventDetailSidePane/src/types/EventRecord.ts`](../../src/solutions/EventDetailSidePane/src/types/EventRecord.ts) `EVENT_FULL_SELECT_FIELDS` (`:186-188`) + interface (`:29,31,33`) | `scheduledstart`, `scheduledend`, `sprk_location` | 🔴 **HTTP 400** — `"Could not find a property named 'scheduledstart'"`. The side pane's full event load **fails outright**. Same list minus these three returns 200 | → `sprk_plannedstart` / `sprk_plannedend`; **drop `sprk_location`** (no equivalent column exists — see the open question below) |
| **SD-2** | [`EventDetailSidePane/src/services/eventService.ts`](../../src/solutions/EventDetailSidePane/src/services/eventService.ts) `getDirtyFields` editable list (`:278-280`) | same three | Writes to non-existent columns on save | same |
| **SD-3** | [`Spaarke.UI.Components/src/services/EventTypeService.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/EventTypeService.ts) field-visibility catalog (`:47-48,51`) + doc block (`:167-169`) | same three | Per-event-type visibility config references fields that cannot render | same |
| **SD-4** | [`DailyBriefingCollector.cs:408`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs#L408) | Project `sprk_description` | 🔴 **HTTP 400** confirmed — Project has only `sprk_projectdescription` | → `sprk_projectdescription` |
| **SD-5** | [`DailyBriefingCollector.cs:424`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs#L424) | Event `sprk_eventdescription` | 🔴 **HTTP 400** confirmed — Event has only `sprk_description` | → `sprk_description` |
| **SD-6** | [`WorkAssignmentEndpoints.cs:79,82`](../../src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs#L79) | `sprk_matterid`, `sprk_duedate` | Both writes are conditional, so the endpoint works only while the caller omits MatterId **and** DueDate; supplying either throws | → `sprk_regardingmatter`, `sprk_responseduedate` |
| **SD-7** | [`CreateNotificationNodeExecutor.cs:145,789`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L145) | `{{item.scheduledend}}` in an author-facing parameter description | Low — but playbook authors copy it | → `{{item.sprk_plannedend}}` |

**Verification method**: `az account get-access-token` → Dataverse Web API, executing the shipped `$select` lists verbatim. Evidence is reproducible.

**Open design questions** (carried into the issue docs, not resolved here): `sprk_location` has no replacement column on `sprk_event` — drop it from code or create the column? And SD-6 should probably also populate the ADR-024 resolver fields alongside `sprk_regardingmatter`, which may require a server-side resolver helper that does not yet exist.

**Scope note**: SD-1/SD-2 touch `src/solutions/EventDetailSidePane/**`, which R1 placed behind a MUST NOT and R2 inherits. **That constraint stands for R2** — a separate fix project would need it lifted for those two files (which would *not* reopen DEF-04, the `MemoSection` refactor).

**Guard against recurrence**: all seven share one root cause — column names hard-coded in TypeScript/C# with nothing verifying they exist. The metadata layer R2 builds (§5.4) is the structural answer for the header path; the issue docs propose the equivalent check for these six sites.

**✅ Code-verified 2026-08-22 — no longer a discovery item.** All five entities appear in **both** maps ([`toolbarLaunchDefaults.ts:105-112, 150-162`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts#L105-L162)): `sprk_matter`, `sprk_project`, `sprk_event`, `sprk_invoice`, `sprk_budget`, `sprk_workassignment` are the 6 Memo parents; the 11 To Do parents add `sprk_analysis`, `sprk_communication`, `contact`, `sprk_document`, `sprk_organization`. So no toolbar map changes for this rollout — §6.4's auto-hide is for entities added later. (Caveat: the maps encode a 2026-07 MCP schema verification; they are code truth, not live-schema truth.)

**Per-form acceptance criteria** (each binding):

- Renders configured fields at configured spans; inline edit stages to the form buffer and goes dirty without a PCF re-render
- Toolbar identical to Matter's: title, sparkle, To Do badge, Notepad badge
- To Do opens SmartTodo filtered to this record; Notepad opens scoped to this record
- Bundle ≤250 KB minified (R1 shipped 62.4 KiB — confirmed 63,812 bytes on disk; shared-lib footprint dominates, so the ceiling should hold — but §10 tracks it as the one thing that could regress)
- **TTI ≤300 ms warm / ≤800 ms cold** (restored R1 NFR-01 — R2 adds pre-render metadata calls, so this must be measured, not assumed; see §5.4 caching)
- Malformed / absent `layoutJson` degrades to derived defaults, never blank, never thrown
- A `layoutJson` field that is **not on the form** fails loudly (consistent throw across text and lookup paths — §5.4)
- Version footer present

---

## 10. Risks

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| **Single control = shared blast radius.** A regression breaks every bound entity at once. | High | Med | Partly pre-existing — all header PCFs already share one library, so a shared-lib bug breaks all of them today regardless. Mitigate with staged form binding (wave 1 → soak → wave 2), version footer, and Matter migrated **last**. |
| **Bundle growth from four new renderers** breaches the 250 KB ceiling. | Med | Low–Med | Measure per wave, not at the end. The optimization triad (§7.1) is mandatory and must not be disturbed. |
| ~~Metadata-derived lookups fail on a non-conventional target~~ | — | — | ✅ **CLOSED 2026-08-24.** Live metadata matched `LOOKUP_META` exactly (§5.4); the `fields[].lookup` escape hatch is not needed and stays out of the v1.0 schema. |
| **Summary columns are empty on every entity** — `sprk_aisummary` 0/10 on Invoice, `sprk_mattersummary` 1/55 on Matter, and R2 now *creates* three more that nothing writes yet (§9). | Med | **Certain** | Sparkle visibility keys on attribute **existence**, not population, and the popover renders an explicit "No summary yet" state. The failure mode is an honest empty state, not R1's silent lie. Depends on the separate populating project to become useful. |
| **Layout change requires a form publish** (the accepted cost of JSON-on-manifest). | Low | Certain | Accepted 2026-08-21. Reversible via the resolver's tier design (§5.1). |
| **`FieldGrid` caps at 2–3 columns, span 1–3.** If a form wants a 4-column header or explicit row breaks, config cannot express it. | Low | Low | Not in R2. Revisit only if a real form needs it. |
| **`layoutJson` editor ergonomics unproven** — no `of-type="Multiple"` + `usage="input"` property exists in this repo; the form designer may render a single-line box for static values. | **Low** (downgraded 2026-08-24) | Med | ✅ De-risked: `SingleLine.Text` + `usage="input"` is proven by `title`, form-XML storage has no 4000-char column limit, and a realistic layout minifies to ~330 chars. Spike is a 15-min ergonomics check that changes only the manifest `of-type` (§5.1.1). |
| **Metadata calls regress TTI.** §5.4 adds ~3 cold network calls before first paint; `retrieveEntityMetadata` itself issues two per invocation and has no cache. | Med | Med | Page-session metadata cache (§5.4) + restored TTI acceptance criterion (§9). Measure per wave. |
| ~~Layout portability depends on form transport~~ | — | — | ✅ **CLOSED 2026-08-22** — owner confirmed forms ship inside a transported solution (§13). |
| **New Dataverse columns (§9) widen the blast radius** beyond a pure client project — schema changes are harder to reverse than code. | Med | Low | Three additive, nullable Memo columns; no data migration, no existing consumer. Ship them in their own solution slice and verify import into a clean environment (§13). |
| Four renderer tasks in parallel race on `fields/index.ts` (R1 hit this). | Low | Med | Serialize the barrel edit or split the file — encode in `/project-pipeline` task sequencing, not in review. |
| Form binding needs maker access the dev session lacks. | Low | Low | Replicate R1's maker checklist ([`matter-form-binding-instructions.md`](../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md)) per entity. |

---

## 11. Component justification (CLAUDE.md §11)

| New surface | Existing overlap | Why not extend it | Cost of doing nothing |
|---|---|---|---|
| `RecordHeader` control | `MatterHeader` PCF | This **is** the extension — same control, generalized, old one retired (§8) | Four more PCF solutions to version, deploy, bind, and fix in parallel; ~82 lines of reusable machinery duplicated four times (corrected count, §1.3) |
| **`sprk_aisummary` column** on Project / WA / Event | Matter `sprk_mattersummary`; Invoice `sprk_aisummary`; Project's three specialised summaries | Cannot extend — the column simply does not exist on those three entities, and Project's three are structured insight-card fields, not narrative prose | Owner requires the sparkle on every rollout entity (§9). With no column, the sparkle can never render on Work Assignment or Event — the affordance is dead, not merely empty. Live-verified 2026-08-24. |
| `DateField` / `NumberField` | `TextField` | `TextField` does `String(value)` ([`:117`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/TextField.tsx#L117)) — it cannot format a Money value by currency/precision or a DateTime by user locale | Invoice `sprk_totalamount` renders `12500`; `sprk_invoicedate` renders `2026-08-21T00:00:00Z`. Invoice ships broken. |
| `BooleanField` | `TextField` | `TextField` would render a Boolean as `true` / `false` | `sprk_highpriority` and `sprk_monitor` exist on all four rollout entities and would display as raw `true`/`false` instead of a Yes/No or toggle. Live-verified 2026-08-24. |
| `OptionSetField` edit mode | `OptionSetField` | Direct extension of the existing component | Project / Work Assignment / Invoice status become read-only, unlike every other field in the header |
| `layoutJson` property | `title`, `showVersion` properties | Same manifest surface, one more input | Field payload stays compiled in; a new entity needs a code change and a redeploy |
| `resolveHeaderConfig` | `configResolution.ts` (DataGrid) | Different domain object (fields/spans vs columns/views); deliberately mirrors its structure and test approach | Config precedence spreads through the render path untested |
| ~~Metadata access path~~ | [`IDataverseClient`](../../src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts) / [`XrmDataverseClient`](../../src/client/shared/Spaarke.UI.Components/src/services/XrmDataverseClient.ts) | **No new component.** `retrieveEntityMetadata` already returns `primaryIdAttribute` / `primaryNameAttribute` / per-attribute `attributeType`, `format`, `displayName`, `optionSet`. R2 **extends** it with `targets?: string[]` (one field + one line in `projectAttribute`) and adds a page-session cache — DataGrid gets both for free | Building a parallel raw-`fetch` `ManyToOneRelationships` path would duplicate an existing contract, need an explicit carve-out from the "`Xrm.WebApi` only" MUST, and leave two metadata code paths to keep in sync |
| Shared `getXrmPage()` | [`FieldMappingHandler.getXrmPage()`](../../src/client/shared/Spaarke.UI.Components/src/services/FieldMappingHandler.ts#L552) + `MatterHeaderView.getXrmPage()` | These are already near-identical duplicates; §6.2 lands one and migrates both | A third copy ships with the generic control; `utils/xrmContext.ts` keeps lying about the available Xrm surface (no `Page` member) |
| ~~`sprk_headerconfiguration` table~~ | — | **Rejected** — §5.1. No concrete failure without it. | n/a |

---

## 12. ADR posture

Inherited and unchanged: **ADR-006** (PCF for form-bound UI), **ADR-012** (shared library), **ADR-021** (Fluent v9 semantic tokens only), **ADR-022** (React 16/17-safe shared components), **ADR-024** (`sprk_memo` Path C dual-field), **ADR-038** (testing strategy). **ADR-028** is N/A — host-context `Xrm` only, no `@spaarke/auth`, no BFF.

**No ADR conflict, and no §6.5 escalation.** Worth stating explicitly because R1's project CLAUDE.md paraphrases ADR-011 as "typed components > runtime schemas," which reads like a blocker for a configuration-driven control. [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) contains no such rule; its actual MUSTs — "reuse shared components from `@spaarke/ui-components`", "MUST NOT duplicate UI primitives" — point toward this design. The repo's own configuration-driven frameworks (VisualHost `sprk_chartdefinition`, DataGrid `sprk_gridconfiguration`) are established precedent for the pattern.

Also unchanged: **NFR-07** (no BFF) and **NFR-09** (Notepad launch-contract URL params are external API — do not rename).

---

## 13. Cross-environment portability (binding)

R1 shipped clean on this axis and R2 must preserve it: no literal record GUIDs, environment names, tenant/subscription ids, or user/contact/business-unit ids in any shipped bundle. `window.SPAARKE_*` globals and build-time-inlined `.env` values remain unacceptable.

R1's shipped artifacts are **verified clean** on this axis (2026-08-22 scan of `bundle.js`, `solution.xml`, `customizations.xml`): zero literal GUIDs, zero environment URLs, zero `SPAARKE_*` globals. Baked-in *logical* names (`sprk_smarttodo`, `sprk_notepad`, `sprk_mattersummary`) are portable by design and protected by NFR-09.

The JSON-on-manifest decision is claimed to **strengthen** this position: layout configuration lives in form XML and travels with the solution, so there are no config records to seed per environment.

> ✅ **Condition confirmed (owner, 2026-08-22): main forms ARE transported between environments inside a solution.** The claim holds as written and §5.1's comparison table is correctly scored. Consequences: (a) `layoutJson` is authored once in dev and rides the solution downstream — no per-environment paste; (b) §8's Matter re-bind is genuinely once; (c) R1's per-environment maker-task framing ([`matter-form-binding-instructions.md:12`](../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md)) described that project's working practice, not a constraint on this one. **Binding assumption** — if form transport ever changes, §5.1's decision must be re-scored.

Portability check per deliverable: import the solution ZIP into a fresh environment and verify the header renders, toolbar actions launch, and badges fetch — with no additional environment-specific configuration beyond the form binding itself.

---

## 14. Rough effort

| Work | Estimate |
|---|---|
| **`layoutJson` ergonomics spike** (§5.1.1) — `Multiple` vs the proven `SingleLine.Text` fallback | 0.25 d |
| Generic view + entity self-detection + config resolver (§5, §6.3) | 1–2 d |
| Hoist generic machinery to shared lib + unify `getXrmPage()` (§6.2) | 1–1.5 d |
| Renderers (§6.1) — `DateField` (date + datetime modes), `NumberField`, `BooleanField`, plus `OptionSetField` edit mode & typography fix | 2–3 d |
| **Three `sprk_aisummary` columns** (§9) — `dataverse-create-schema` + solution packaging + fresh-env import check | 0.25–0.5 d |
| Metadata-driven lookup resolution — extend `EntityAttributeMetadata` with `targets` + page-session cache (§5.4) | 0.5–1 d |
| **Rewrite the Matter-fixtured test suite** against the generic control (`__tests__/MatterHeaderView.test.tsx` asserts Matter labels, the 6-field payload, `entity === 'sprk_matter'`, `sprk_mattersummary` sparkle body) | 0.5–1 d |
| Control migration to `RecordHeader` + parity QA (§8, option B — owner-confirmed) | 0.5–1 d |
| Per-entity config + form binding + QA | 0.5 d × 4 |
| Guide rewrite from shipped code + pattern refresh (§3.1) | 0.5–1 d |
| **Total** | **~9.25–13.5 dev-days** |

The §9.1 schema-drift defects are **excluded** — they are scoped separately in [`notes/issues/`](notes/issues/README.md) (~1–2 d combined if all three are approved).

Note the estimate is roughly flat versus the 2026-08-21 figure despite ~10 new work items, because the corrected §9 removed more renderer work than the code review added elsewhere.

The withdrawn four-PCF plan estimated 4–6 h × 4 ≈ 3 days — but that number excluded the renderer work it also needed (§6), and left five controls to maintain instead of one.

---

## 15. Next steps

### 15.1 Open owner decisions (blocking `/design-to-spec`)

| # | Decision | Status |
|---|---|---|
| **D-1** | §8 control identity: A (rename display name only) vs B (new `RecordHeader` control + re-bind) | ✅ **RESOLVED 2026-08-22 — option B.** Re-presented with the corrected trade; owner reaffirmed the clean identity. |
| **D-2** | How main forms move between environments | ✅ **RESOLVED 2026-08-22 — forms ship inside a transported solution.** §13 portability argument holds; §8 re-bind is once. |
| **D-3** | Config mechanism — JSON-only vs config record | ✅ **RESOLVED 2026-08-24 — JSON-only on the manifest.** Spike runs as a 15-minute ergonomics check (`Multiple` vs the proven `SingleLine.Text` fallback), not a gate on the design. §5.1.1. |
| **D-4** | Retire `MatterHeaderPcf` timing | ✅ **RESOLVED 2026-08-24 — retire as soon as this project is delivered.** No dormant-release rollback window; the parity QA in §8 step 4 is the safety net. |
| **D-5** | §5.4 metadata access path | ✅ **RESOLVED 2026-08-24 — reuse `IDataverseClient`**, extended with `targets`. §5.4.1. |
| **D-6** | §9 field lists | ✅ **RESOLVED 2026-08-24 — live-verified against `spaarkedev1`; §9 rewritten.** Sparkle + summary fields **kept** per owner (populated by a separate project). `BooleanField` **stays in** — the 2026-08-22 "no consumer" call was wrong. |

Four further decisions were taken during the `/design-to-spec` interview on 2026-08-24:

| # | Decision | Status |
|---|---|---|
| **D-7** | Per-entity `layoutJson` layouts | ✅ **RESOLVED** — proposed layouts confirmed (§9). `sprk_highpriority` added to all four so `BooleanField` has a real consumer. |
| **D-8** | Sparkle on entities with no summary column | ✅ **RESOLVED — R2 creates `sprk_aisummary` (Memo, 5000) on Project, Work Assignment and Event** (§9). This is the one place R2 leaves the pure client surface. |
| **D-9** | `RecordHeaderShell` skeleton column mismatch | ✅ **RESOLVED** — add an optional `columns` prop, default `3` (§4). |
| **D-10** | Renderer conventions | ✅ **Em-dash `''` everywhere — adopted.** ⬜ **Required marker on every editable renderer — NOT adopted**; consequence documented in §6.1. |

**All owner decisions are closed.** Remaining gate is the §5.1.1 ergonomics spike, which cannot change the design — only the manifest `of-type`.

### 15.2 Sequence

1. ✅ **Discovery — COMPLETE** (2026-08-24, live against `spaarkedev1`). §9 field lists, option-set values, summary-field population, main-form GUIDs, and the §5.4 lookup-metadata check are all resolved. The toolbar-map item was closed from code.
2. **`/design-to-spec`** on this document → numbered FRs/NFRs with per-entity acceptance criteria.
3. **`/project-pipeline`** → worktree + task list + `projects/INDEX.md` registration.
4. **Ergonomics spike** — `layoutJson` static `Multiple` vs `SingleLine.Text` in the classic form designer (§5.1.1). Schedule it as the first implementation task; it cannot change the design, only the manifest `of-type`, so it no longer blocks spec authoring.
5. Review the three schema-drift issue docs in [`notes/issues/`](notes/issues/README.md) and decide which become focused fix projects. Independent of R2 — no sequencing dependency either way.
6. Sequence tasks: shared-lib renderers and the resolver land **before** any form binding; the renderer tasks must not edit `fields/index.ts` concurrently (§10); Matter migrates **last**, and `MatterHeaderPcf` is retired on delivery (D-4).

---

## 16. Related deferrals

- **DEF-01** (sparkle refresh → BFF regen endpoint) — absorbed by the future Insights Engine / AI Summary project. Not R2.
- **DEF-03** (VisualHost `CardChrome` → `HeaderToolbar`) — in-code pointer; R2B when someone touches VisualHost.
- **DEF-04** (EventDetailSidePane `MemoSection`) — in-code pointer; R2B when someone touches it. Also the trigger for DEF-08.
- **DEF-06** (`exports` field migration) — dropped from R2 (§7.1); standalone migration project when wanted.
- **DEF-08** (`useSprkMemoRepository` promotion) — dropped from R2 (§7.2); trigger stays on DEF-04.

---

*Re-scoped 2026-08-21 from the 2026-07-05 four-PCF seed, per owner decision: one configurable control; Project + Work Assignment first; Invoice explicitly required; JSON over config table.*
