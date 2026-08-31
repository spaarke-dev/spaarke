# ISSUE — Event: code references three columns that do not exist on `sprk_event`

> **Status**: Open, unassigned. Candidate for a focused fix project.
> **Discovered**: 2026-08-24 during `record-header-and-notepad-r2` §9 schema verification.
> **Environment verified**: `spaarkedev1` (Dataverse Web API, live).
> **Severity**: **High** — the Event side pane cannot load a full event record.
> **Owner**: unassigned — for review.

---

## Summary

Three column names used across the Event surface **do not exist** on the `sprk_event` table:

| Referenced | Exists? | Real column |
|---|---|---|
| `scheduledstart` | ❌ No | `sprk_plannedstart` (DateAndTime) — *semantics need confirming* |
| `scheduledend` | ❌ No | `sprk_plannedend` (DateAndTime) — *semantics need confirming* |
| `sprk_location` | ❌ No | **none — there is no location column on `sprk_event`** |

`scheduledstart` / `scheduledend` are OOB column names on the Dataverse `activitypointer` / `appointment` tables. The most likely root cause is that `sprk_event`'s TypeScript model was authored from an appointment-shaped schema and never reconciled against the custom table.

---

## Evidence (reproducible)

The shipped `$select` list executed verbatim against `spaarkedev1`:

```
GET /api/data/v9.2/sprk_events?$top=1&$select=<EVENT_FULL_SELECT_FIELDS as shipped>
  -> HTTP 400
  {"error":{"code":"0x80060888","message":
    "Could not find a property named 'scheduledstart' on type 'Microsoft.Dynamics.CRM.sprk_event'."}}

Same list with scheduledstart / scheduledend / sprk_location removed
  -> HTTP 200
```

Full `sprk_event` `DateTime` inventory from `EntityDefinitions` (2026-08-24):

```
DateOnly     sprk_approveddate, sprk_basedate, sprk_completeddate, sprk_duedate,
             sprk_finalduedate, sprk_meetingdate, sprk_tododuedate
DateAndTime  sprk_actualstart, sprk_actualend, sprk_plannedstart, sprk_plannedend,
             sprk_reassigneddate, sprk_remindat, sprk_rescheduleddate
```

No `sprk_location`, no `sprk_startdate`, no `scheduledstart` / `scheduledend`.

---

## Affected code

| # | File | Lines | What it does |
|---|---|---|---|
| 1 | [`src/solutions/EventDetailSidePane/src/types/EventRecord.ts`](../../../../src/solutions/EventDetailSidePane/src/types/EventRecord.ts) | `29`, `31`, `33` (interface members) · `186-188` (`EVENT_FULL_SELECT_FIELDS`) | The `$select` used to load a full event |
| 2 | [`src/solutions/EventDetailSidePane/src/services/eventService.ts`](../../../../src/solutions/EventDetailSidePane/src/services/eventService.ts) | `278-280` (`getDirtyFields` editable-field list) | Decides which fields are written back on save |
| 3 | [`src/client/shared/Spaarke.UI.Components/src/services/EventTypeService.ts`](../../../../src/client/shared/Spaarke.UI.Components/src/services/EventTypeService.ts) | `47-48`, `51` (field-visibility catalog) · `167-169` (doc block) | Per-event-type field visibility config, **shared library** |
| 4 | [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs) | `145`, `789` | Author-facing parameter description uses `{{item.scheduledend}}` as the worked example |

---

## Observed behaviour

`loadEvent()` calls `webApi.retrieveRecord(EVENT_ENTITY, id, '?$select=' + EVENT_FULL_SELECT_FIELDS)` at [`eventService.ts:158-162`](../../../../src/solutions/EventDetailSidePane/src/services/eventService.ts). The 400 **is caught** ([`:168-177`](../../../../src/solutions/EventDetailSidePane/src/services/eventService.ts)) and returned as `{ success: false, event: null, error }`.

So the side pane does not crash — it **fails to load the event and surfaces an error state**, on every event, every time. Degraded rather than exploded, but non-functional.

Item 4 is cosmetic-but-contagious: it is the example text playbook authors copy when writing notification templates, so it propagates a dead field name into configuration.

---

## Proposed fix

1. `scheduledstart` → `sprk_plannedstart`, `scheduledend` → `sprk_plannedend` across files 1–3, and `{{item.scheduledend}}` → `{{item.sprk_plannedend}}` in file 4.
2. **`sprk_location` — decision required** (see below).
3. Add a test asserting each shipped `$select` list resolves against live entity metadata, so the next drift fails in CI rather than in production.

---

## Open questions for review

1. **Is `sprk_plannedstart` / `sprk_plannedend` the intended semantic replacement?** The UI labels these "Scheduled Start / Scheduled End". `sprk_event` also has `sprk_actualstart` / `sprk_actualend`. "Planned" is the natural analogue for "scheduled", but this is a product judgement, not a mechanical rename — confirm before applying.
2. **What should happen to Location?** There is no location column on `sprk_event`. Either (a) drop the field from the three files — restores function immediately, removes a Location field the side pane currently offers, or (b) create a `sprk_location` column — a schema change with its own migration and form implications. (a) is the smaller, reversible step.
3. **Is the Event side pane currently deployed and in use?** That determines urgency. If it is live, this is a production defect; if it is not yet rolled out, it is a pre-release fix.
4. **Does `EventTypeService`'s field-visibility catalog have other stale entries?** Only these three were checked. The whole catalog should be validated against metadata in one pass.

---

## Blast radius

File 3 is in `@spaarke/ui-components`, so changing it rebuilds the shared library and every PCF that consumes it. Files 1–2 are confined to the `EventDetailSidePane` solution. File 4 is a string change in the BFF with no behavioural coupling.

**Rough effort**: 0.25–0.5 day for the rename + the metadata test, once questions 1 and 2 are answered.

---

## Cross-references

- Discovery context: [`../discovery-checklist.md`](../discovery-checklist.md) §F · [`../../design.md`](../../design.md) §9.1
- Sibling issues from the same sweep: [`ISSUE-daily-briefing-schema-drift.md`](ISSUE-daily-briefing-schema-drift.md) · [`ISSUE-work-assignment-schema-drift.md`](ISSUE-work-assignment-schema-drift.md)
- Note: `src/solutions/EventDetailSidePane/**` sits behind a MUST NOT in `record-header-and-notepad-r1` and `-r2`. A focused fix project would need that constraint lifted for files 1–2 (it does **not** reopen DEF-04, the `MemoSection` refactor).
