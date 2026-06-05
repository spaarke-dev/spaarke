# Task 022 — sprk_gridconfiguration Record ID (Invoice)

> **Created**: 2026-06-01 by task 022
> **Verified**: record retrieved from Dataverse post-create; all fields match authored configjson

---

## Record created

| Field | Value |
|---|---|
| `sprk_gridconfigurationid` | `d021827b-9b5e-f111-ab0c-7c1e521545d7` |
| `sprk_name` | `Invoice Matter Budget Performance` |
| `sprk_entitylogicalname` | `sprk_invoice` |
| `sprk_isdefault` | `true` |
| `sprk_sortorder` | `100` |
| `sprk_configjson` | (full v1.0 JSON — see `022-invoice-configjson.json` for the pretty-printed source) |

---

## Linked savedquery

- `source.savedQueryId` = `b9f6d045-9a5e-f111-ab0c-7c1e521545d7` (Invoice - Matter Context, per `020-savedquery-ids.md`)

---

## Configjson highlights (v1.0)

- **Source**: `savedquery` (single savedquery, not savedquery-set — final design defaulted to single per parallel-task pattern)
- **Display**: title "Invoices", compact density, custom empty-state
- **CommandBar**: standard `newRecord` + `refresh` + `exportExcel`; no custom commands (Mark Paid deferred to R2 per `022-mark-paid-decision.md`)
- **RowOpen**: `navigateToForm` (open Invoice form on row activation)
- **SecondaryActions**: 2 — Ask Sprkchat (ai-assistant), Review (playbook `invoice-review-default`); both `requiresSelection: "single"`, unconditional visibility
- **Behavior**: multi-select, pageSize 100, sorting + resize + keyboard nav enabled
- **ParentContextFilter**: `attribute=sprk_matter`, `parentContextKey=matterId`, `operator=eq` — framework overlays Matter filter at runtime

---

## Downstream consumers

- **Task 023** (Custom Page host for `sprk_invoicespage`) — reads this record ID to fetch the configjson and render the Invoice grid
- **Task 025** (Power Apps Maker handoff) — user may refine column choices in Maker (savedquery edits); the configjson is independent of column changes

---

## Verification

Record successfully retrieved via:

```sql
SELECT sprk_gridconfigurationid, sprk_name, sprk_entitylogicalname,
       sprk_configjson, sprk_isdefault, sprk_sortorder
FROM sprk_gridconfiguration
WHERE sprk_gridconfigurationid = 'd021827b-9b5e-f111-ab0c-7c1e521545d7'
```

Returned record's `sprk_configjson` exactly matches the authored JSON string (byte-for-byte equivalent).
