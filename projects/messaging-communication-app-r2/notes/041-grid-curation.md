# 041 — Grid Filter-Chip Curation for config `e1826c4c-…`

> **Task**: R2-041 (W4, FR-11) — curate the "Active Communications" grid config so the DataGrid
> framework surfaces the four intended filter-chip families: **channel / date / regarding / person**.
> **Rigor**: STANDARD (dataverse, config). **Date**: 2026-07-19. **Environment**: spaarkedev1.
> **Config record**: `sprk_gridconfiguration` GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05`
> ("Active Communications (Workspace)"), entity `sprk_communication`.
> **Bound savedquery**: `2bf1c5a5-0eca-4f37-92df-2e3c386dee98` (OOB "Active Communications", `querytype=0`).

---

## 🚨 LIVE APPLY DEFERRED (deploy gate)

**Dataverse MCP is unavailable this session.** This document is the **curation specification** — the
authored deliverable. It is complete and ready to apply, but the **live edit of the config record is
deferred**:

> **LIVE APPLY DEFERRED: the owner updates config record `e1826c4c-9575-f111-ab0e-7ced8ddc4a05`'s
> `sprk_configjson` (add the `filterChips.explicit` block in §4 below) — and optionally the bound
> savedquery's columns (§5) — in Dataverse, then verifies chips render on task 040's page / task 030's
> widget.** No second config, no second default view is created (NFR-05). Curate in place.

Authoring is unblocked; only the live Dataverse write + visual verification are gated. The apply is a
**single field edit** on one existing record (paste the §4 JSON into `sprk_configjson`) plus optional
view-column tidy (§5). Two owner-confirm items are flagged inline (⚠️): each entity's
`PrimaryNameAttribute` for the lookup `nameField`, and the live attribute types.

---

## 1. Critical finding — how chips ACTUALLY derive (grounded on `chipDiscovery.ts`)

The task premise (and `notes/r2-resource-investigation.md` §3, and the `chipDiscovery.ts` **file header
comment**) states: *"Choice→optionset, Lookup→lookup, DateTime→daterange … a chip only appears if its
column is in the view's layoutXml."* That is **only half true**. Reading the actual **code** (source of
truth per CLAUDE.md §2):

| Discovery mode | optionset (Choice) | daterange (DateTime) | **lookup (Lookup)** |
|---|---|---|---|
| `auto` (current default — config has no `filterChips` block) | ✅ derives from layoutXml columns | ✅ derives from layoutXml columns | **❌ SILENTLY SKIPPED** |
| `allowlist` | ✅ | ✅ | **❌ SILENTLY SKIPPED** |
| `denylist` | ✅ | ✅ | **❌ SILENTLY SKIPPED** |
| `explicit` (author each chip) | ✅ | ✅ | **✅ — but ONLY with a `valueSource`** |

**Why**: in `auto`/`allowlist`/`denylist`, chips are built by `buildAutoDescriptor()`, which contains:

```ts
if (kind === 'lookup') {
  // No metadata-derived lookup target available in the projected
  // EntityAttributeMetadata shape (see TODO above). Skip silently.
  return undefined;
}
```

There is a `TODO(R2)` in `discoverExplicit` noting Xrm metadata exposes lookup `targets` but the framework
doesn't project them onto `EntityAttributeMetadata` yet. So the framework **cannot know the target entity
of a lookup from metadata alone** — it needs the target spelled out via `ExplicitFilterChip.valueSource`.
`resolveLookupTarget()` (the only code path that emits a lookup chip) returns `undefined` unless
`ex.valueSource` is set.

### Consequence for this task

- **channel** (`sprk_communicationtype`, Choice) and **date** (`sprk_sentat`, DateTime) **would**
  auto-derive today from being in the view's layoutXml — the task premise holds for these two.
- **regarding** and **person** are **Lookups** → they will **NEVER** appear under `auto` mode, no matter
  how the layoutXml is curated. Adding lookup columns to the view is **necessary-for-display but
  insufficient-for-chips**.

**Therefore the single correct lever that surfaces all four facet families is a `filterChips.explicit`
block in the config's `sprk_configjson`** (§4). Switching to `explicit` replaces auto-derivation entirely,
so the explicit list must **also** re-declare channel + date (they no longer auto-derive once `mode` ≠
`auto`). This is a directional-mode adaptation of the POML's step 3 ("curate the view's layoutXml"): the
layoutXml is still curated for column *display* (§5), but the *chips* are authored in configjson.

This is not an ADR conflict — it is an implementation reality of the framework's current chip-derivation
code. No §6.5 escalation needed; noted here for the record and for task 030/040 authors.

---

## 2. Target facet → column → chip mapping (verified against `chipDiscovery.ts`)

| Facet | Column(s) on `sprk_communication` | Attribute type | Chip kind (`FilterChipKind`) | `ChipKind` emitted | Derivable under `auto`? |
|---|---|---|---|---|---|
| **channel** | `sprk_communicationtype` | Choice (Picklist) | `optionset-multi` | `optionset` | ✅ yes |
| **date** | `sprk_sentat` (primary); `createdon` (fallback) | DateTime | `date-range` | `daterange` | ✅ yes |
| **person** | `sprk_sentby` (→ `systemuser`) | Lookup | `lookup-multi` | `lookup` | ❌ needs explicit + `valueSource` |
| **person / regarding** | `sprk_regardingperson` (→ `contact`) | Lookup | `lookup-multi` | `lookup` | ❌ needs explicit + `valueSource` |
| **regarding** | the 11-entity `RegardingFieldMap.All` family (below) | Lookup | `lookup-multi` | `lookup` | ❌ needs explicit + `valueSource` |

Mapping verified line-by-line against `deriveChipKindFromMetadata()` / `mapConfigKind()` in
`src/client/shared/Spaarke.UI.Components/src/components/DataGrid/filterChips/chipDiscovery.ts`:
`Picklist/Status/State→optionset`, `DateTime→daterange`, `Lookup→lookup`; config kinds
`optionset-multi→optionset`, `date-range→daterange`, `lookup-multi→lookup`.

### The 11-entity regarding family (from `RegardingFieldMap.All`, verbatim)

`src/server/api/Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs` — ADR-024 priority order.
`sprk_regardingperson` (contact) is a member of BOTH the regarding family and the person family (do not
double-author its chip).

| # | Regarding field (Lookup on `sprk_communication`) | Target entity | Lookup `nameField` ⚠️ owner-confirm PrimaryNameAttribute |
|---|---|---|---|
| 1 | `sprk_regardingmatter` | `sprk_matter` | `sprk_name` |
| 2 | `sprk_regardingproject` | `sprk_project` | `sprk_name` |
| 3 | `sprk_regardinginvoice` | `sprk_invoice` | `sprk_name` |
| 4 | `sprk_regardingservicerequest` | `sprk_servicerequest` | `sprk_name` |
| 5 | `sprk_regardingworkassignment` | `sprk_workassignment` | `sprk_name` |
| 6 | `sprk_regardingevent` | `sprk_event` | `sprk_name` |
| 7 | `sprk_regardingbudget` | `sprk_budget` | `sprk_name` |
| 8 | `sprk_regardinganalysis` | `sprk_analysis` | `sprk_name` |
| 9 | `sprk_regardingorganization` | `sprk_organization` | `sprk_name` |
| 10 | `sprk_regardingaccount` | `account` | `name` |
| 11 | `sprk_regardingperson` | `contact` | `fullname` |

⚠️ **`nameField` note**: the table lists best-guess primary-name attributes. Spaarke custom entities
conventionally use `sprk_name`; OOB `account`/`contact` use `name`/`fullname`. The owner should confirm
each target's `PrimaryNameAttribute` at apply time (a wrong `nameField` renders the chip's value picker
with blank labels but does not break the grid). This is one of the two live-apply gates.

---

## 3. Chip-count consideration (curation recommendation)

Authoring **all** of channel + date + 11 regarding lookups + `sprk_sentby` = **14 chips** is a lot for one
filter strip and may overwhelm the workspace-widget-width surface (task 030). Two options:

- **Option A — full set (satisfies POML acceptance criteria literally).** Author all 14. Use this if the
  owner wants every regarding facet filterable from the grid. Provided as the **primary** JSON in §4.
- **Option B — lean high-value subset (recommended for the widget UX).** channel + date + `sprk_sentby`
  (person) + `sprk_regardingmatter` + `sprk_regardingperson` (the two dominant regarding targets). Provided
  as a **variant** in §4. The remaining 9 regarding lookups stay filterable via the grid's OOB column-header
  chevron / advanced-filter even without a dedicated chip.

Both keep the **single** default config + view (NFR-05). The owner picks A or B at apply time. Default
recommendation: **B for the rich widget (030), A acceptable for the full-page standalone (040)** — but since
both surfaces read the *same* config `e1826c4c-…`, one choice applies to both. **Recommend Option B** for
strip legibility unless the owner requires all-11 chip parity.

---

## 4. The deliverable — `filterChips.explicit` block to add to `sprk_configjson`

**Apply**: paste the `filterChips` block into config `e1826c4c-…`'s `sprk_configjson`, keeping the existing
`_version` / `source` / `display` / `rowOpen` keys intact. This is additive — no other key changes.

### Option A — full set (all 14 chips)

```jsonc
{
  "_version": "1.0",
  "source": {
    "type": "savedquery",
    "savedQueryId": "2bf1c5a5-0eca-4f37-92df-2e3c386dee98"
  },
  "display": { "title": "Active Communications" },
  "rowOpen": { "type": "formDialog" },

  "filterChips": {
    "mode": "explicit",
    "showClearAll": true,
    "explicit": [
      { "field": "sprk_communicationtype", "kind": "optionset-multi", "label": "Channel" },
      { "field": "sprk_sentat",            "kind": "date-range",      "label": "Sent" },
      { "field": "sprk_sentby",            "kind": "lookup-multi",    "label": "Sent by",
        "valueSource": { "type": "systemusers" } },
      { "field": "sprk_regardingperson",   "kind": "lookup-multi",    "label": "Person",
        "valueSource": { "type": "entity", "entity": "contact", "nameField": "fullname" } },
      { "field": "sprk_regardingmatter",   "kind": "lookup-multi",    "label": "Matter",
        "valueSource": { "type": "entity", "entity": "sprk_matter", "nameField": "sprk_name" } },
      { "field": "sprk_regardingproject",  "kind": "lookup-multi",    "label": "Project",
        "valueSource": { "type": "entity", "entity": "sprk_project", "nameField": "sprk_name" } },
      { "field": "sprk_regardinginvoice",  "kind": "lookup-multi",    "label": "Invoice",
        "valueSource": { "type": "entity", "entity": "sprk_invoice", "nameField": "sprk_name" } },
      { "field": "sprk_regardingservicerequest", "kind": "lookup-multi", "label": "Service Request",
        "valueSource": { "type": "entity", "entity": "sprk_servicerequest", "nameField": "sprk_name" } },
      { "field": "sprk_regardingworkassignment",  "kind": "lookup-multi", "label": "Work Assignment",
        "valueSource": { "type": "entity", "entity": "sprk_workassignment", "nameField": "sprk_name" } },
      { "field": "sprk_regardingevent",    "kind": "lookup-multi",    "label": "Event",
        "valueSource": { "type": "entity", "entity": "sprk_event", "nameField": "sprk_name" } },
      { "field": "sprk_regardingbudget",   "kind": "lookup-multi",    "label": "Budget",
        "valueSource": { "type": "entity", "entity": "sprk_budget", "nameField": "sprk_name" } },
      { "field": "sprk_regardinganalysis", "kind": "lookup-multi",    "label": "Analysis",
        "valueSource": { "type": "entity", "entity": "sprk_analysis", "nameField": "sprk_name" } },
      { "field": "sprk_regardingorganization", "kind": "lookup-multi", "label": "Organization",
        "valueSource": { "type": "entity", "entity": "sprk_organization", "nameField": "sprk_name" } },
      { "field": "sprk_regardingaccount",  "kind": "lookup-multi",    "label": "Account",
        "valueSource": { "type": "entity", "entity": "account", "nameField": "name" } }
    ]
  }
}
```

### Option B — lean subset (recommended; 5 chips)

```jsonc
"filterChips": {
  "mode": "explicit",
  "showClearAll": true,
  "explicit": [
    { "field": "sprk_communicationtype", "kind": "optionset-multi", "label": "Channel" },
    { "field": "sprk_sentat",            "kind": "date-range",      "label": "Sent" },
    { "field": "sprk_sentby",            "kind": "lookup-multi",    "label": "Sent by",
      "valueSource": { "type": "systemusers" } },
    { "field": "sprk_regardingperson",   "kind": "lookup-multi",    "label": "Person",
      "valueSource": { "type": "entity", "entity": "contact", "nameField": "fullname" } },
    { "field": "sprk_regardingmatter",   "kind": "lookup-multi",    "label": "Matter",
      "valueSource": { "type": "entity", "entity": "sprk_matter", "nameField": "sprk_name" } }
  ]
}
```

**Notes on the JSON**:
- `sprk_communicationtype` optionset options are hydrated from entity metadata automatically
  (`attrMeta.optionSet` in `discoverExplicit`) — no need to enumerate values.
- `sprk_sentby` uses the built-in `{ "type": "systemusers" }` shortcut (resolves to
  `systemuser` / `fullname` in `resolveLookupTarget`).
- `date-range` on `sprk_sentat`; if the owner prefers the fallback, swap to `createdon` (both DateTime →
  `daterange`). Only one date chip is recommended to avoid two near-identical daterange chips.
- Chip FetchXML conditions are overlaid at query time by `chipFetchXml` (composition order
  `base → parentContext → hostFilters → chips`) — they filter regardless of whether the column is a
  *displayed* layoutXml column.

---

## 5. Optional — view column (layoutXml/FetchXML) guidance for DISPLAY

The chips in §4 do not require the columns to be in the bound view's layoutXml. But for **column display +
sortability** the "Active Communications" view (`2bf1c5a5-…`) should carry the facet columns. Per the
shipped config note, the OOB view already selects the spec §5.4 column set
(subject/summary, communication type, direction, sender, recipient(s), sent-on, regarding, status), so
**channel (`sprk_communicationtype`), date (`sprk_sentat`), sender (`sprk_sentby`), and a regarding column
are already present** — no view edit is strictly required for MVP.

If the owner wants explicit columns for the additional regarding lookups shown in the grid body, curate the
**existing** view `2bf1c5a5-…` in place (do NOT clone it — NFR-05):

- Add `<cell name="sprk_regardingmatter" .../>` etc. to the `<row>` in **layoutXml**, AND the matching
  `<attribute name="sprk_regardingmatter"/>` to the **FetchXML** `<entity>` (both must agree, per the
  framework's resolve step).
- Keep it the **single default** system view for `sprk_communication` used by this config. Do not set a
  second `sprk_isdefault` config or a second default savedquery.

Editing an OOB Microsoft-provided view is acceptable (it becomes a customized layer in the solution); the
lower-friction alternative is to leave the OOB view untouched and rely on the §4 chips for filtering, which
is the recommended MVP path.

---

## 6. Acceptance-criteria disposition

| POML criterion | Status |
|---|---|
| Config includes channel/date/regarding/person facets (`sprk_communicationtype`, `sprk_sentat`/`createdon`, regarding lookups incl. `sprk_regardingperson`, `sprk_sentby`) | ✅ **Specified** in §4 (Option A = full 14; Option B = lean 5). Delivered via `filterChips.explicit`, not layoutXml — see §1. Live apply deferred. |
| Each facet's column type maps to expected chip per `chipDiscovery.ts` (channel→optionset, date→daterange, regarding/person→lookup) | ✅ **Verified** line-by-line against `chipDiscovery.ts` (§2). Key correction: lookups require `explicit` + `valueSource`; auto mode skips them. |
| Chips auto-derive + appear on task 040 page / task 030 widget | ⏳ **DEFERRED** — verify after owner applies §4 JSON (both surfaces read config `e1826c4c-…`). |
| No second `sprk_gridconfiguration` default + no second default view (NFR-05) | ✅ **Honored** — spec curates config `e1826c4c-…` + its bound view `2bf1c5a5-…` in place; no new records. |
| No code / no BFF changes (root §10 N/A) | ✅ **Honored** — deliverable is a configjson edit + this note; zero source files touched. |

---

## 7. Decisions recorded

1. **Chips surface via `filterChips.explicit` in configjson, NOT via layoutXml curation alone** — because
   `chipDiscovery.ts` silently skips Lookup chips in `auto`/`allowlist`/`denylist` modes (only `explicit`
   + `valueSource` emits a lookup chip). Adapted the POML's directional step 3 accordingly.
2. **`explicit` mode re-declares channel + date** — switching off `auto` disables their auto-derivation, so
   they are authored explicitly alongside the lookups.
3. **Two options provided** — full 14-chip (A, literal criteria) vs lean 5-chip (B, recommended UX). Owner
   picks at apply. Both keep the single default config + view (NFR-05).
4. **Regarding family sourced from `RegardingFieldMap.All`** (11 lookups, ADR-024 order) — no second
   regarding mechanism (ADR-024/046). `sprk_regardingperson` counted once (person ∩ regarding).
5. **`nameField` per regarding target flagged for owner confirmation** (PrimaryNameAttribute) — one of two
   live-apply gates; a wrong value degrades chip labels only, not the grid.
6. **Coordination with task 040 / 030** — both consume config `e1826c4c-…`; this curation drives the chips
   on both surfaces. 040 is already built (shell + deploy-script registration); 030 (rich widget) not yet.
