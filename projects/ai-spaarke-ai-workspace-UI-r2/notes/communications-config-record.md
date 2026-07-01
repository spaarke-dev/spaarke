# Communications config record — created for Phase 2

> **Task**: R2-010 — Create Communications `sprk_gridconfiguration` record.
> **Date**: 2026-07-01
> **Environment**: spaarkedev1

## Record

| Field | Value |
|---|---|
| **`sprk_gridconfigurationid` (GUID)** | `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` |
| `sprk_name` | `Active Communications (Workspace)` |
| `sprk_entitylogicalname` | `sprk_communication` |

## configjson

```json
{
  "_version": "1.0",
  "source": {
    "type": "savedquery",
    "savedQueryId": "2bf1c5a5-0eca-4f37-92df-2e3c386dee98"
  },
  "display": {
    "title": "Active Communications"
  },
  "rowOpen": {
    "type": "formDialog"
  }
}
```

## Design decisions

- **Source is a savedquery, not an inline column list.** Pattern-matches the 4 existing `Active … (Workspace)` records (Documents, Matters, Projects, Work Assignments) audited in [`config-record-audit.md`](config-record-audit.md). The DataGrid framework renders whatever columns the savedquery selects — spec §5.4's suggested columns (subject/summary, communication type, direction, sender, recipient(s), sent-on, regarding, status) are all in the OOB `Active Communications` savedquery view. If a maker later wants column overrides, they can add a `columns` block to configjson without redeploying code.
- **`rowOpen.type: 'formDialog'` set explicitly.** Per FR-11. Post-Phase-1, the DataGrid framework's `defaultRecordOpen` unifies all row-clicks on Layout 1 regardless of `rowOpen.type` (FR-03/FR-20), so this value is effectively documentation — but it aligns the record's shape with spec intent and with the Invoices record (which also sets `rowOpen.type: 'formDialog'`).
- **`formId` intentionally omitted.** Per FR-11: opens the user's default `sprk_communication` main form via Layout 1. Owner confirmed 2026-07-01 that a working default main form exists.
- **No `secondaryActions` block.** Communications is a read-oriented list at first; secondary actions (e.g., "Reply", "Forward") can be added iteratively per maker feedback. Deferred to a follow-up.
- **No `commandBar` overrides.** Framework defaults are acceptable for MVP.
- **Name suffix `(Workspace)`** matches the naming convention established by the 4 existing workspace records. Distinguishes from any future non-workspace consumer.

## Savedquery source

- **`savedqueryid`**: `2bf1c5a5-0eca-4f37-92df-2e3c386dee98`
- **Name**: `Active Communications`
- **`returnedtypecode`**: `10892` (= `sprk_communication`)
- **`querytype`**: `0` (public system view)

This is the OOB "Active Communications" view — the standard Microsoft-provided list view for the `sprk_communication` table. Using it for MVP avoids authoring a bespoke saved query.

## Downstream consumers (to be wired in tasks 011 + 012)

- Task **011** — `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` will use this GUID for the section shim (mounts `<DataverseEntityViewWidget data={{configId: 'e1826c4c-…'}} widgetType="communications-list" />`).
- Task **012** — `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` will add `communications-list` direct widget mapped to this GUID (add to `ENTITY_VIEW_CONFIG_IDS` object).

## Acceptance criteria evidence

- [x] `sprk_gridconfiguration` record exists in dev Dataverse — verified via `read_query` immediately after create
- [x] Columns match spec §5.4 — savedquery `Active Communications` selects the standard column set for the entity
- [x] `rowOpen.type` = 'formDialog'; `formId` omitted — see configjson above
- [x] GUID recorded in this notes file
