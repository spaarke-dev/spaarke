# `layoutJson` reference — the string to paste into the control property

> **Added**: 2026-08-26 (UAT round 2, question 4) · **Control**: `Spaarke.Records.RecordHeader` v1.1.5
> Field names below are the **live-verified** ones from `design.md` §9 (checked against `spaarkedev1`
> on 2026-08-24), not inferred from a naming convention.

---

## Project (`sprk_project`) — paste this

```json
{"_version":"1.0","title":"Project Information","columns":3,"summaryField":"sprk_recordsummary","fields":[{"name":"sprk_projectnumber","span":1,"required":true},{"name":"sprk_projectname","span":2},{"name":"sprk_projecttype_ref","span":1},{"name":"sprk_openeddate","span":1},{"name":"sprk_highpriority","span":1},{"name":"sprk_projectdescription","span":3,"renderer":"textarea"}]}
```

380 bytes — which is why the property is `of-type="Multiple"`. The classic designer caps a
`SingleLine.Text` static value at **100 characters**; this is nearly four times that.

Readable form (identical content — paste either):

```json
{
  "_version": "1.0",
  "title": "Project Information",
  "columns": 3,
  "summaryField": "sprk_recordsummary",
  "fields": [
    { "name": "sprk_projectnumber",      "span": 1, "required": true },
    { "name": "sprk_projectname",        "span": 2 },
    { "name": "sprk_projecttype_ref",    "span": 1 },
    { "name": "sprk_openeddate",         "span": 1 },
    { "name": "sprk_highpriority",       "span": 1 },
    { "name": "sprk_projectdescription", "span": 3, "renderer": "textarea" }
  ]
}
```

### Two names worth double-checking on your form

| | |
|---|---|
| `sprk_projecttype_ref` | The lookup **attribute** on `sprk_project` — NOT `_sprk_projecttype_ref_value` (the OData alias) and NOT the target table. Take the name from the form designer's field list. |
| `sprk_projectdescription` | Project has **no** `sprk_description` column; that name exists on Work Assignment and Event but not here. Using it yields an em-dash cell. |

A wrong name is now **survivable** — since v1.1.1 the read degrades instead of 400-ing, so you get one
em-dash cell rather than a blank header. It is still wrong; it just no longer takes the form down.

---

## The schema — five top-level keys, closed set

| key | required | notes |
|---|---|---|
| `_version` | ✅ | Must be exactly `"1.0"`. A wrong value falls back to metadata-derived defaults with one `console.warn`. |
| `title` | | Toolbar title. The manifest `title` property outranks it; metadata entity name is the fallback. |
| `columns` | | `2` or `3`. Default `3`. |
| `summaryField` | | Backs the sparkle popover. Defaults to `sprk_recordsummary`. Naming a non-existent attribute hides the sparkle — it does not error. |
| `fields` | ✅ | Ordered list; render order is array order. |

Per field: `name` (required), `span` (1–3, clamped to `columns`), `label`, `renderer`, `required`,
`readOnly`, `maxLines`.

`renderer` accepts: `text` · `textarea` · `lookup` · `optionset` · `date` · `datetime` · `number` ·
`currency` · `boolean`.

**You normally do not need `renderer`** — it is derived from entity metadata. Set it only to override
that derivation, e.g. forcing a Memo column to `text` for a single-line cell. It is also the escape
hatch if a cell ever picks the wrong editor again: `{"name":"sprk_openeddate","renderer":"date"}`
pins it regardless of what metadata reports.

---

## Omitting `layoutJson` entirely

Leave the property blank and the control derives a layout from entity metadata — form order, primary
name first, audit columns excluded. Useful for a quick look at a new entity before authoring a real
layout. It will not match a designed header.

---

## The other five rollout entities

Authored layouts live in [`design.md`](../design.md) §608 (the per-entity layout table). Field names
there carry the same 2026-08-24 live verification. Watch for these, all confirmed:

- **Work Assignment** — only date column is `sprk_responseduedate`; primary name is `sprk_name`.
- **Invoice** — has **no** due-date column; money column is `sprk_totalamount`.
- **Event** — primary name is `sprk_eventname`; the DateAndTime pair is
  `sprk_plannedstart` / `sprk_plannedend`. `scheduledstart`, `scheduledend` and `sprk_location`
  **do not exist** despite other shipped code referencing them.
- **Agreement** — has **no** boolean columns at all, and 0 records; seed one before QA.
- **Matter** — migrates last, as the parity regression test against the R1 baseline.
