# `sprk_event` Field Verification — Spec Assumption #1

> **Task**: 003 — Verify `sprk_event` schema field names via MCP `describe_table`
> **Date**: 2026-05-27
> **Source**: `mcp__dataverse__describe_table` on `sprk_event` (live Dataverse schema)
> **Blocks (resolves prerequisite for)**: Tasks 032 (FR-DV-03), 033 (FR-DV-04), 034 (FR-DV-05)

---

## §Spec Assumption Table

Spec.md Assumption #1 (line 501) — verification result per field:

| Spec-Assumed Field | Spec-Assumed Type | Actual Logical Name | Actual Type | Status |
|---|---|---|---|---|
| `sprk_regardingmatter` | lookup | `sprk_regardingmatter` | `LOOKUP (GUID)` → `sprk_matter` | ✅ matches |
| `sprk_finalduedate` | date | `sprk_finalduedate` | `DATE ONLY` | ✅ matches |
| `sprk_eventstatus` | choice | `sprk_eventstatus` | `CHOICE` (8 options) | ✅ matches |
| `actualstart` | date (activity standard) | `sprk_actualstart` | `DATETIME` | ⚠️ corrected name — actually `sprk_actualstart` (custom field, not activity standard) |
| `completeddate` | date | `sprk_completeddate` | `DATE ONLY` | ⚠️ corrected name — actually `sprk_completeddate` (custom field) |

**Summary**: 3 of 5 spec assumptions match exactly. 2 fields exist but with the `sprk_` prefix (they are custom fields on `sprk_event`, not activity-base standard fields — `sprk_event` is a fully custom entity, not an Activity entity derivative).

---

## §Choice Option Values — `sprk_eventstatus`

From `describe_table` output:

```
sprk_eventstatus CHOICE (Options:
  Draft (0),
  Open (1),
  Completed (2),
  Closed (3),
  On Hold (4),
  Cancelled (5),
  Reassigned (6),
  Archived (7))
```

**For FetchXML predicate `eventstatus != Completed`** → use numeric value **`2`**.

### Related: `statuscode` (different field — for reference only)

The standard `statuscode` field on `sprk_event` uses different (high-range) values: `Open (659490001)`, `Completed (659490002)`. The chart-def FetchXML in this project should use **`sprk_eventstatus`** (the choice column), NOT `statuscode`.

---

## §FetchXML Snippets

Snippets target the Spaarke Visual Host current-record binding. The Visual Host substitutes `@currentMatter` (or equivalent token — confirm exact placeholder in Visual Host docs during task 032/033/034) with the active Matter record GUID at runtime. If the Visual Host uses Power Apps style binding, the literal token may be `{currentRecordId}` or similar — adjust per Visual Host setup.

### 1. Overdue tasks (FR-DV-03)

**Definition**: tasks regarding the current Matter where `sprk_finalduedate < today` AND `sprk_eventstatus != Completed`.

```xml
<fetch aggregate="true">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" alias="overdue_count" aggregate="countcolumn" />
    <filter type="and">
      <condition attribute="sprk_regardingmatter" operator="eq" value="@currentMatter" />
      <condition attribute="sprk_finalduedate" operator="lt" value="@today" />
      <condition attribute="sprk_eventstatus" operator="ne" value="2" />
    </filter>
  </entity>
</fetch>
```

> **Note**: Dataverse supports `today` operator natively without a value: `<condition attribute="sprk_finalduedate" operator="on-or-before" value="today" />` will not work for "strictly before today" — use `operator="lt"` with a runtime-substituted `@today` placeholder OR use Dataverse's `olderthan-x-hours` / explicit datetime in the Visual Host's query layer. Confirm during task 032.

### 2. Upcoming tasks — next 30 days (FR-DV-03)

**Definition**: tasks regarding the current Matter where `sprk_finalduedate BETWEEN today AND today+30` AND `sprk_eventstatus != Completed`.

```xml
<fetch aggregate="true">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" alias="upcoming_count" aggregate="countcolumn" />
    <filter type="and">
      <condition attribute="sprk_regardingmatter" operator="eq" value="@currentMatter" />
      <condition attribute="sprk_finalduedate" operator="next-x-days" value="30" />
      <condition attribute="sprk_eventstatus" operator="ne" value="2" />
    </filter>
  </entity>
</fetch>
```

> **Note**: `next-x-days` is a Dataverse-native operator that includes today through `today+N`. Use this in preference to explicit `between` for portability.

### 3. Recent activity — last 7 days (FR-DV-05)

**Definition**: tasks regarding the current Matter where EITHER `sprk_actualstart >= today-7` OR `sprk_completeddate >= today-7`.

```xml
<fetch aggregate="true">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" alias="recent_count" aggregate="countcolumn" />
    <filter type="and">
      <condition attribute="sprk_regardingmatter" operator="eq" value="@currentMatter" />
      <filter type="or">
        <condition attribute="sprk_actualstart" operator="last-x-days" value="7" />
        <condition attribute="sprk_completeddate" operator="last-x-days" value="7" />
      </filter>
    </filter>
  </entity>
</fetch>
```

> **Note**: `last-x-days` covers strictly the last N days excluding today; if "last 7 days including today" is required, use `last-x-days` with value `7` plus a separate "today" predicate. Confirm semantics during task 034.

---

## §Notes — Downstream Impact on FR-DV-03/04/05 Tasks

### Corrections to apply in Phase 3 chart-def tasks (032, 033, 034)

1. **Task 032 (FR-DV-03 — Matter Task Overview chart def)**:
   - Use `sprk_finalduedate` (✅ matches spec)
   - Use `sprk_eventstatus = 2` for "Completed" filter
   - All other fields match assumptions

2. **Task 033 (FR-DV-04 — Matter Status chart def)**:
   - No new field corrections required if it uses only `sprk_regardingmatter` + `sprk_eventstatus` (both ✅)

3. **Task 034 (FR-DV-05 — Matter Recent Activity chart def)**:
   - ⚠️ **Use `sprk_actualstart` (NOT `actualstart`)** — `sprk_event` is a custom entity, not an Activity derivative; no `actualstart` activity-base column exists.
   - ⚠️ **Use `sprk_completeddate` (NOT `completeddate`)** — same reason.

### Recommended spec.md amendment

Update spec.md Assumption #1 (line 501) text to reflect the verified names. Suggested replacement:

> **`sprk_event` filter fields** — verified via task 003 (2026-05-27): `sprk_regardingmatter` (lookup → `sprk_matter`), `sprk_finalduedate` (date), `sprk_eventstatus` (choice — `Completed = 2`), `sprk_actualstart` (datetime, custom), `sprk_completeddate` (date, custom). All required fields confirmed present.

(Spec amendment is OUT OF SCOPE for this task — flag for project owner in handoff.)

### Bonus finding — `sprk_duedate` exists separately from `sprk_finalduedate`

The schema has BOTH `sprk_duedate` (DATE ONLY) and `sprk_finalduedate` (DATE ONLY). The spec uses `sprk_finalduedate` for overdue / upcoming calculations. If business semantics later require "original due date" vs "final/escalated due date", that distinction is available — task authors should default to `sprk_finalduedate` per spec until told otherwise.

---

*Spike complete. No code changes. Ready for Phase 3 chart-def tasks to proceed using verified names.*
