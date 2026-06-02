# Task 021 — sprk_gridconfiguration record ID (for task 023)

> **Created**: 2026-06-01 by task 021
> **Verified**: record retrieved from Dataverse post-create; `sprk_configjson` round-trips byte-for-byte (no Dataverse-side normalization)

---

## Record created

| Field | Value |
|---|---|
| `sprk_gridconfigurationid` | `3019a06e-9b5e-f111-ab0c-7c1e521545d7` |
| `sprk_name` | `KPI Assessment Matter Health` |
| `sprk_entitylogicalname` | `sprk_kpiassessment` |
| `sprk_isdefault` | `true` |
| `sprk_sortorder` | `100` |

References savedquery: `a3f6d045-9a5e-f111-ab0c-7c1e521545d7` (KPI Assessment - Matter Context, per `020-savedquery-ids.md`).

---

## Usage in task 023 (Custom Pages)

Task 023 will wire the Matter Health drill-through Custom Page with:

```tsx
<DataGrid
  configId="3019a06e-9b5e-f111-ab0c-7c1e521545d7"
  parentContext={{ matterId: matterIdFromUrl }}
/>
```

The framework will:
1. Resolve the `sprk_gridconfiguration` record by `configId`
2. Parse `sprk_configjson` → `DataGridConfiguration` v1.0
3. Resolve the savedquery at `source.savedQueryId` (KPI - Matter Context)
4. Overlay the parent-context filter from `behavior.parentContextFilter` — inject `<condition attribute='sprk_matter' operator='eq' value='{matterId}'/>` into the savedquery's FetchXML before fetching
5. Render with `densityDefault: 'compact'` + filter chips (`mode: auto`) + secondary action "Ask Sprkchat"

---

## configjson v1.0 — full body

See `021-kpi-configjson.json` (draft) or the live record's `sprk_configjson` column. Both are identical (verified via round-trip).
