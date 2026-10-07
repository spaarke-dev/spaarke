# Task 024 — grammar extension for the Do lane: progress

> Kept here, NOT in `current-task.md` (the main session owns that file).

| Field | Value |
|---|---|
| Rigor | FULL (POML-declared; `bff-api`; `.cs` + tests) |
| Tier | opus @ high · steps mode directional |
| Status | **STOPPED on escalation trigger 2** (2026-10-07). No code changed yet. |

## Step 0: coordination checks (owner constraint, 2026-10-07)

- `origin/master` @ `dbc58d139` (2026-10-07). uac-r2 files re-read there: `Services/Dataverse/CoreAncestorResolver.cs`
  (last change `d254d7166`, #1312), `Services/Access/SecureChildLineage.cs`, `config/secure-record-owner-role.json`,
  `Services/Dataverse/RecordOwnershipResolver.cs`, `Api/Filters/RecordRouteAccessAuthorizationFilter.cs`,
  `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs`, `Infrastructure/ExternalAccess/ExternalCallerContext.cs`,
  `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`, ADR-034.
- **Nothing there invalidates this task's plan.** `CoreAncestorResolver.CoreRecordEntities` = project, matter, work
  assignment, service request; `sprk_event` / `sprk_todo` are CHILD entities carrying all four `sprk_regarding{core}`
  lookups. This task adds NO grouping derivation: the compiler projects only the subject id, and core-record
  derivation (D-34/D-36/D-37) stays with `CoreAncestorResolver`, consumed by the evaluator/writer (031/037).
- Open PRs touching the six task files: only #1111 (this branch). uac-r2 #1353 / #1342 touch none of them, nor any
  uac-r2-owned file this task reads. uac-r2 worktree @ `bbb6cfde0`: no overlapping active work. `/conflict-check`:
  soft warn only (BFF hot path shared with uac-r2, no file overlap).

## Step 1: writer read depth: **all Global, so trigger 1 does NOT fire**

Writer `# mi-ontology-writer-dev` (`3121bf1b-…`), spaarkedev1, 2026-10-07, security-roles.md §9.2 method
(user privileges ∪ every team's privileges, then RetrievePrincipalAccess on real rows):

| Table | User-side Read | Root team `Spaarke` Read | RetrievePrincipalAccess | Rows admin / writer (`MSCRMCallerID`) |
|---|---|---|---|---|
| `sprk_event` | **Global** | Deep | Read on rows in 2 BUs | 79 / 79 |
| `sprk_todo` | **Global** | Deep | Read on rows in 2 BUs | 53 / 53 |
| `sprk_workassignment` | **Global** | Deep | Read | 22 / 22 |
| `sprk_servicerequest` | Global | — | (0 rows) | 0 / 0 (stays refused per POML) |

## Subject schema (live metadata, 2026-10-07): facts the implementation will need

| Column | Format | Behavior |
|---|---|---|
| `sprk_event.sprk_duedate` | DateOnly | **DateOnly** |
| `sprk_todo.sprk_duedate` | DateOnly | **UserLocal** (⚠️ not converted by 098; the stored value is the user's local midnight in UTC; matters for D-25 in 031) |
| `sprk_workassignment.sprk_responseduedate` | DateOnly | **TimeZoneIndependent** |

Status values: event Open = `659490001` (collector); To Do Open = 1, In Progress = `659490001`; work assignment
Active = statecode 0 / statuscode 1. Event type Task = `sprk_eventtype_ref` `124f5fc9-98ff-f011-8406-7c1e525abd8b`.

## 🔔 Escalation trigger 2 fired: "task due within 3 days" is not expressible in the D-16 grammar

Of the three collector rules, two fit the extended grammar as a single subject-only `when` filter:

- **Overdue task**: `sprk_event`, `sprk_eventtype_ref = Task`, `statuscode = 659490001`, `sprk_duedate < now-5d`
  (D-27 removes the collector's `sprk_duedate OR sprk_finalduedate`, which the grammar could not express either).
- **Work assignment past due**: `sprk_workassignment`, `statecode = 0`, `sprk_responseduedate < now`.

**"Task due within 3 days" needs a two-sided range on ONE field** (`sprk_duedate >= now AND <= now+3d`; the collector
uses `NextXDays`). The grammar cannot say that: a field maps to ONE value or ONE operator (`maxProperties: 1` in the
schema, "exactly one operator" in the compiler), and duplicate keys are refused. With only the upper bound
(`sprk_duedate <= now+3d`) the rule matches every overdue task too.

Live, 2026-10-07: 17 open Task events. The collector's "upcoming" set = **3** (due 10-07, 10-08, 10-08). An
upper-bound-only rule returns **17**: the 12 that the overdue rule also covers, plus 2 due 10-04 (which neither
collector rule covers today), plus the 3. So two Signals would be raised for each overdue task.

Options for the owner:

- **(A) Allow a two-bound range object** (recommended): `{">=": "now", "<=": "now+3d"}`, meaning one lower bound
  (`>` or `>=`) plus one upper bound (`<` or `<=`), and nothing else. It compiles to two conditions in the SAME
  AND filter, so it is still one Dataverse filter (CM-3) with no join and no OR. Cost: one schema `$def` and about 20
  compiler lines plus tests. It widens the grammar beyond D-16's four named items, which is why it needs the owner.
- **(B) Drop the lower bound**: `sprk_duedate <= now+3d` with "overdue" taken out by rule precedence in the
  evaluator. That is new evaluator logic (031) and makes one rule's meaning depend on another's.
- **(C) Defer "due within 3 days"** from the Do-lane rule set: ship overdue task and work assignment only, and leave
  "upcoming" to the Briefing narrative (Know, not Do).

Rejected: a `VerifiedJoins` entry or a self-join (forbidden by the POML); FetchXML `next-x-days` (truncates in the
caller's time zone, which the compiler deliberately avoids; see the `PredicateCompiler` remarks).

**Nothing else is blocked.** On an answer, the plan is: schema (`all` minItems 0 when `when` has ≥1 condition; refuse
an empty `when` with zero clauses; `quietWindowDays` integer ≥ 0, default 14), `now+Nd`, `sprk_workassignment` added to
`EvaluatorGlobalReadableEntities`, a live-verified Date Only column catalog surfaced on `CompiledPredicate` for the
`when` fields (field → Dataverse behavior), `QuietWindowDays` on `CompiledPredicate`, three fixtures, unit tests and
seam tests.
