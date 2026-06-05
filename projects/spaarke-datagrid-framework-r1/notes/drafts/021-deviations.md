# Task 021 — Deviations from POML

> **Status**: 2 minor deviations, both documented + accepted
> **Task outcome**: completed (record created + verified via round-trip)

---

## Deviation 1 — `commandBar` shape: `showDefaultCommands` instead of `actions[]`

**POML background note** (line 31): `commandBar: { actions: ["refresh", "export-excel"] }`

**Actual**: Used the v1.0 schema's `commandBar.showDefaultCommands` object pattern:

```json
"commandBar": {
  "showDefaultCommands": {
    "newRecord": true,
    "refresh": true,
    "exportExcel": true,
    "delete": false,
    "editColumns": false,
    "editFilters": false
  }
}
```

**Reason**: The POML's `actions: [...]` shape was descriptive shorthand from `design.md` Appendix; the actual `CommandBarConfig` interface in `DataGridConfiguration.ts` (lines 154–171) uses `primary?: CommandBarItem[]` / `secondary?: CommandBarItem[]` for custom items, plus a `showDefaultCommands` toggle object for framework defaults. Since the POML only asked for built-in refresh + export-excel + create, the `showDefaultCommands` toggles are the idiomatic path.

**Acceptable per**: parent-task prompt explicitly listed the v1.0 shape with `showDefaultCommands`.

---

## Deviation 2 — `display.densityDefault` is `'compact'` not `'small'`

**POML background note** (line 31): `display: { densityDefault: "small" }`

**Actual**: Used `'compact'`:

```json
"display": { "densityDefault": "compact" }
```

**Reason**: `DisplayConfig.densityDefault` is typed as `'comfortable' | 'compact'` (DataGridConfiguration.ts line 68) — Fluent v9's `<DataGrid size>` accepts these two values. `'small'` would FAIL the type check.

**Acceptable per**: parent-task prompt explicitly listed `densityDefault: "compact"`; the POML's `"small"` was a stale design.md note.

---

## What was NOT deviated

- `_version: "1.0"` — present
- `source.type = "savedquery"` + `savedQueryId` references task 020's KPI savedquery (`a3f6d045-9a5e-f111-ab0c-7c1e521545d7`)
- `filterChips.mode = "auto"` — per POML
- `rowOpen.type = "navigateToForm"` — per POML
- `secondaryActions[0]` — `kind: 'ai-assistant'`, `id: 'ask-sprkchat-kpi'`, `label: 'Ask Sprkchat'` — per POML
- `behavior.parentContextFilter` — `sprk_matter` / `matterId` / `eq` — per parent-task prompt + commit `fe4f675d` framework enhancement
- Dataverse record fields: `sprk_name`, `sprk_entitylogicalname`, `sprk_configjson`, `sprk_isdefault = true`, `sprk_sortorder = 100` — exactly per parent-task prompt
- Verification by retrieval — `sprk_configjson` round-trips byte-for-byte (no Dataverse normalization)

---

## Files updated by this task

- `projects/spaarke-datagrid-framework-r1/notes/drafts/021-kpi-configjson.json` (created — final configjson)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/021-config-record-id.md` (created — record GUID for task 023)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/021-deviations.md` (created — this file)
- `projects/spaarke-datagrid-framework-r1/tasks/021-kpi-assessment-config-record.poml` (`status` → `completed`)

## Records created in Dataverse

- `sprk_gridconfiguration` GUID `3019a06e-9b5e-f111-ab0c-7c1e521545d7` — "KPI Assessment Matter Health"
