# TaskOverview.pbix — Build Instructions

> **Report Name**: Task Overview
> **File**: `reports/v1.0.0/TaskOverview.pbix`
> **Category**: Operational (sprk_category = 2)
> **sprk_name value**: `Task Overview`
> **Status**: PLACEHOLDER — requires Power BI Desktop to create
> **Created**: 2026-03-31

---

## Purpose

Provides a comprehensive view of task status, completion rates, assignment distribution, and
SLA adherence. Enables team leads and operations managers to monitor workload balance and
identify overdue or at-risk tasks across matters.

---

## Data Source

**Connection Type**: Dataverse (Import mode)
**Environment URL**: Configured per deployment — do NOT hardcode

### Dataverse Tables to Import

| Table Logical Name | Display Name | Purpose |
|-------------------|--------------|---------|
| `task` or `sprk_task` | Task / To-Do | Task records (OOB `task` or custom entity) |
| `sprk_matter` | Matter | Parent matter context |
| `businessunit` | Business Unit | BU hierarchy for RLS |
| `systemuser` | User | Task owner / assigned to |

> Spaarke uses a custom smart-todo entity. Use `sprk_todo` or `task` depending on the actual
> schema. Check the dev environment entity list and adapt column names accordingly.

### Key Columns to Include

From `task` or `sprk_todo`:
- `subject` or `sprk_name` — Task title
- `statecode` — Status: Active (0) / Completed (1)
- `scheduledend` or `sprk_duedate` — Due date
- `actualend` or `sprk_completeddate` — Completion date
- `prioritycode` or `sprk_priority` — Priority level
- `regardingobjectid` — Related matter (lookup)
- `ownerid` — Assigned user
- `businessunitid` — Business unit

---

## Relationships

- `task[ownerid]` → `systemuser[systemuserid]`
- `task[regardingobjectid]` → `sprk_matter[sprk_matterid]`
- `systemuser[businessunitid]` → `businessunit[businessunitid]`

---

## Calculated Measures (DAX)

```dax
-- Completion Rate %
Task Completion Rate % =
    DIVIDE(
        CALCULATE(COUNTROWS(task), task[statecode] = 1),
        COUNTROWS(task),
        0
    ) * 100

-- Overdue Tasks (open and past due date)
Overdue Tasks =
    CALCULATE(
        COUNTROWS(task),
        task[statecode] = 0,
        task[scheduledend] < TODAY()
    )

-- Avg Days to Complete
Avg Days to Complete =
    AVERAGEX(
        FILTER(task, task[statecode] = 1 && NOT(ISBLANK(task[actualend]))),
        DATEDIFF(task[createdon], task[actualend], DAY)
    )

-- SLA Adherence % (completed on or before due date)
SLA Adherence % =
    DIVIDE(
        CALCULATE(
            COUNTROWS(task),
            task[statecode] = 1,
            task[actualend] <= task[scheduledend]
        ),
        CALCULATE(COUNTROWS(task), task[statecode] = 1),
        0
    ) * 100
```

---

## BU RLS Role

| Role Name | Table | DAX Filter |
|-----------|-------|------------|
| `BusinessUnitFilter` | `systemuser` | `[domainname] = USERNAME()` |

Filters tasks to those owned by users within the current user's business unit hierarchy.

---

## Visualizations

### Page 1: Task Status Overview

| Visual | Type | Data |
|--------|------|------|
| Open Tasks | KPI card | Count of active tasks vs prior period |
| Overdue Tasks | KPI card | Overdue Tasks measure (alert if > 0) |
| Completion Rate | Gauge | Task Completion Rate % (0–100%) |
| SLA Adherence | Gauge | SLA Adherence % (target: 90%+) |
| Task Status Breakdown | Donut chart | Active vs Completed vs Cancelled |

### Page 2: Workload Distribution

| Visual | Type | Data |
|--------|------|------|
| Tasks by Owner | Bar chart | Count per assigned user (open tasks only) |
| Priority Breakdown | Stacked bar | Tasks per user split by priority |
| Tasks by Matter | Bar chart | Top 20 matters by open task count |
| Overdue by Owner | Table | Owner, overdue count, oldest overdue date |

### Page 3: Trend Analysis

| Visual | Type | Data |
|--------|------|------|
| Tasks Created Over Time | Line chart | New tasks per week |
| Avg Days to Complete | Line chart | Rolling 4-week average |
| Completed vs Created (Rate) | Dual-axis line | Completion vs creation rate |

### Page 4: Task Detail

| Visual | Type | Data |
|--------|------|------|
| Slicers | Dropdown | Matter, assigned user, priority, date range |
| Task List | Table | All open tasks: name, matter, owner, due date, days overdue |

---

## Conditional Formatting

In the Task List table visual, apply conditional formatting on "Days Overdue":
- 0 days: no fill
- 1–7 days: Spaarke Amber background (`#F59E0B`)
- 8+ days: Spaarke Red background (`#DC2626`)

---

## Formatting

- **Canvas background**: Transparent (100% transparency)
- **Colors**:
  - Active / in progress: Spaarke Blue (`#2563EB`)
  - Completed (on time): Spaarke Teal (`#0D9488`)
  - Overdue: Spaarke Red (`#DC2626`)
  - At-risk: Spaarke Amber (`#F59E0B`)
- **Report title**: "Task Overview" — Spaarke Navy, 20pt Segoe UI Semibold

---

## Dataverse Record (After Publishing)

| Field | Value |
|-------|-------|
| `sprk_name` | `Task Overview` |
| `sprk_category` | `2` (Operational) |
| `sprk_iscustom` | `false` |
| `sprk_pbi_reportid` | `<GUID from PBI Service URL>` |
| `sprk_workspaceid` | `<workspace GUID>` |
| `sprk_datasetid` | `<dataset GUID>` |
| `sprk_description` | `Task status, completion rates, SLA adherence, and workload distribution across matters.` |

---

*Spaarke Reporting Module R1 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
