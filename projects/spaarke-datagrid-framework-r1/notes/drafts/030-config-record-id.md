# Task 030 — `sprk_gridconfiguration` record ID for `sprk_event`

> **Created**: 2026-06-03
> **Task**: 030 — Author `sprk_gridconfiguration` record for `sprk_event` (Phase D anchor)
> **Status**: ✅ Created in DEV (spaarkedev1)

---

## Record ID (use this in task 031 + 033)

```
e15c2b93-a05f-f111-a825-70a8a59455f4
```

| Field | Value |
|---|---|
| `sprk_gridconfigurationid` | `e15c2b93-a05f-f111-a825-70a8a59455f4` |
| `sprk_name` | `Event Default` |
| `sprk_entitylogicalname` | `sprk_event` |
| `sprk_isdefault` | `true` |
| `sprk_sortorder` | `100` |
| **Environment** | `spaarkedev1.crm.dynamics.com` (DEV) |
| **Maker URL** | https://spaarkedev1.crm.dynamics.com/main.aspx?forceUCI=1&newWindow=true&pagetype=entityrecord&etn=sprk_gridconfiguration&id=e15c2b93-a05f-f111-a825-70a8a59455f4 |

---

## configjson authored

Per [`design.md` Appendix § sprk_event](../../design.md) verbatim:

```json
{
  "_version": "1.0",
  "source": { "type": "savedquery-set", "entityLogicalName": "sprk_event" },
  "display": { "title": "Events", "icon": "CalendarLtr24Regular" },
  "filterChips": {
    "mode": "allowlist",
    "allowlist": ["_ownerid_value", "sprk_eventtype_ref", "sprk_eventstatus", "sprk_duedate"]
  },
  "commandBar": {
    "primary": [
      { "id": "new",     "label": "+ New",   "icon": "Add24Regular",            "action": "create-form" },
      { "id": "delete",  "label": "Delete",  "icon": "Delete24Regular",         "action": "delete-selected", "requiresSelection": "multi", "privilege": "Delete" },
      { "id": "refresh", "label": "Refresh", "icon": "ArrowClockwise24Regular", "action": "refresh" }
    ]
  },
  "rowOpen": {
    "type": "webResource",
    "webResource": "sprk_eventdetailsidepane.html",
    "dataParams": ["sprk_eventid", "_sprk_eventtype_ref_value"]
  },
  "secondaryActions": [
    { "id": "sprkchat-event", "label": "Ask sprkchat", "icon": "Sparkle24Regular", "kind": "ai-assistant", "visible": "row-hover" }
  ]
}
```

The full body lives at [`030-event-configjson.json`](030-event-configjson.json) (canonical draft, formatted).

---

## Key configjson decisions (verbatim from design.md Appendix)

| Field | Value | Rationale |
|---|---|---|
| `source.type` | `savedquery-set` | Auto-pick `sprk_event` default view at render time. Removes config drift when admin updates the default. |
| `filterChips.mode` | `allowlist` | Only 4 specific chips, matching the legacy EventsPage `AssignedToFilter` / `RecordTypeFilter` / `StatusFilter` + Due Date — preserves existing filter UX. |
| `filterChips.allowlist[0]` | `_ownerid_value` | Maps to legacy `AssignedToFilter` (lookup-multi over systemusers). |
| `filterChips.allowlist[1]` | `sprk_eventtype_ref` | Maps to legacy `RecordTypeFilter` (optionset-multi). |
| `filterChips.allowlist[2]` | `sprk_eventstatus` | Maps to legacy `StatusFilter` (optionset-multi). |
| `filterChips.allowlist[3]` | `sprk_duedate` | New — Due Date chip (date-range) joins as part of the migration. |
| `rowOpen.type` | `webResource` | **Load-bearing** — closes the record-link-not-opening bug (FR-DG-14, FR-MIG-04). Uses `Xrm.Navigation.navigateTo({pageType:'webresource'})` which works in dialog mode. NOT `sidePane`. |
| `rowOpen.webResource` | `sprk_eventdetailsidepane.html` | The existing Event detail side-pane web resource. Reused as-is. |
| `rowOpen.dataParams` | `[sprk_eventid, _sprk_eventtype_ref_value]` | Event id (primary key) + event-type lookup value (drives which detail form to render). |
| `secondaryActions[0].visible` | `row-hover` | Ask-sprkchat surfaces on hover (per legacy UX). |

NO `behavior.parentContextFilter` — EventsPage is a top-level Code Page (not a drill-through). The Calendar widget (task 033) is the consumer that will optionally add a date overlay; the Event configuration record itself stays unfiltered.

---

## How downstream tasks reference this

### Task 031 — Rewrite `EventsPage/App.tsx`

```tsx
const EVENT_CONFIG_ID = "e15c2b93-a05f-f111-a825-70a8a59455f4";

<DataGrid
  configId={EVENT_CONFIG_ID}
  dataverseClient={new XrmDataverseClient()}
  theme={theme}
/>
```

### Task 033 — Calendar widget (SpaarkeAi consumer)

The widget shim references the same `EVENT_CONFIG_ID`, plus pipes the Calendar's date selection through `parentContext={{ dateRange: {...} }}` for an in-memory filter overlay (deferred design — see task 033 POML).

---

## Acceptance criteria (mapped from task 030 POML)

- [x] **Configjson validates**. `_version: "1.0"` ✅, `source.type: "savedquery-set"` ✅ — passes the runtime guard `isValidDataGridConfiguration` (renamed from spec `isValidConfigJson` per task 001 D1).
- [x] **Record retrieved matches authored JSON**. Round-tripped via `mcp__dataverse__create_record` → `mcp__dataverse__read_query`. Confirmed `sprk_gridconfigurationid = e15c2b93-a05f-f111-a825-70a8a59455f4`, `sprk_isdefault: true`, `sprk_entitylogicalname: sprk_event`.
- [x] **`rowOpen.type = "webResource"`** (not `sidePane`). The load-bearing detail — closes the record-link bug.

---

## Smoke test status

Per task 030 POML step 6: smoke test a `<DataGrid configId="..." />` in a harness. **Deferred** to task 031 (the EventsPage rewrite is the real first consumer — wiring a separate harness only to throw away after task 031 is wasted effort). The configuration record validates statically; the real end-to-end smoke is the task 031 deployment + task 035 UAT.
