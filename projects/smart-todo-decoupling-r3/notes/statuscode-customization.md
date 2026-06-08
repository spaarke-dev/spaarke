# `sprk_todo.statuscode` customization — task 009

> **Date**: 2026-06-07
> **Task**: 009-customize-sprk-todo-statuscode-values
> **Spec authority**: FR-24 (Graph status mapping) + design.md §4.1 + §6.3
> **Deployment script**: [`scripts/Customize-SprkTodoStatuscode.ps1`](../../../scripts/Customize-SprkTodoStatuscode.ps1)

## Background

Task 002 created `sprk_todo` but Dataverse auto-defaulted the `statuscode` option pair to
`Active(1) / Inactive(2)`. Per FR-24 + design §4.1, `statuscode` must support four
Spaarke-side states that map bidirectionally to Microsoft Graph `todoTask.status`:

- **Open** — task is open with no progress recorded
- **In Progress** — task is open with progress recorded
- **Completed** — task is finished
- **Dismissed** — task was abandoned (Graph: deferred)

This task extended the option set to those four values.

## Spaarke convention referenced

**`sprk_communication.statuscode`** (see [`docs/data-model/sprk_communication.md`](../../../docs/data-model/sprk_communication.md))
is the canonical Spaarke entity with a multi-option statuscode customization. Its convention
is mixed:

- **OOB defaults (1, 2)** are renamed in place rather than discarded — preserves the cleanest
  numeric values for the most common states and avoids orphaning the auto-created defaults
  that Dataverse always produces
- **Additional values** use the **`659490001+` custom range** (Microsoft's reserved custom
  option-value range; avoids collision with system options and with `100000000+` range used
  by sprk_todo's own `sprk_todocolumn` choice)

This task follows the `sprk_communication` pattern.

## Numeric values chosen

| Value | Label | statecode parent | Rationale |
|-------|-------|------------------|-----------|
| **1** | Open | 0 (Active) | OOB default value renamed in place — Open is the entry state for any new todo |
| **659490001** | In Progress | 0 (Active) | New value in custom range — Active state, progress recorded |
| **2** | Completed | 1 (Inactive) | OOB default value renamed in place — Completed is the canonical "done" terminal state |
| **659490002** | Dismissed | 1 (Inactive) | New value in custom range — Inactive state, task abandoned (Graph: `deferred`) |

The custom values are sequential (659490001, 659490002) per the `sprk_communication`
convention.

## Microsoft Graph status mapping (FR-24 + design §6.3)

Bidirectional mapping used by the Graph sync handler (task 061):

| Dataverse `statuscode` | Dataverse `statecode` | Graph `todoTask.status` |
|------------------------|------------------------|-------------------------|
| 1 (Open) | 0 (Active) | `notStarted` |
| 659490001 (In Progress) | 0 (Active) | `inProgress` |
| 2 (Completed) | 1 (Inactive) | `completed` |
| 659490002 (Dismissed) | 1 (Inactive) | `deferred` |

**Inbound resolution (Graph → Dataverse)** also handles Graph's transient `waitingOnOthers`
state, which is mapped to **Open** + `notStarted` semantics on the Spaarke side (no separate
local state); design §6.3.

## Web API actions used

The deployment script issues the following Dataverse Web API metadata actions, all scoped to
`SolutionUniqueName = SpaarkeCore` for portability:

1. **`Microsoft.Dynamics.CRM.UpdateOptionValue`** (×2)
   - Rename Value `1` from "Active" → "Open"
   - Rename Value `2` from "Inactive" → "Completed"
2. **`Microsoft.Dynamics.CRM.InsertStatusValue`** (×2)
   - Insert Value `659490001` → "In Progress" under statecode `0` (Active)
   - Insert Value `659490002` → "Dismissed" under statecode `1` (Inactive)
3. **`Microsoft.Dynamics.CRM.PublishXml`**
   - Publish `sprk_todo` customizations

The script is idempotent — re-running it detects existing labels/values and skips updates
that are already applied.

## Verification

Post-deployment `mcp__dataverse__describe tables/sprk_todo` returns:

```
statuscode STATUS (INT) (Valid Options: Open (1), Completed (2), In Progress (659490001), Dismissed (659490002))
```

All four expected option values present, with correct statecode parent mapping.

## Portability

All metadata changes are scoped to the `SpaarkeCore` solution. Schema exports cleanly via
solution export/import; no tenant-specific values; no hardcoded org URLs (the deployment
script accepts `-EnvironmentUrl` as a parameter and falls back to `$env:DATAVERSE_URL`).

## Files updated

- [`src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md`](../../../src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md) — replaced placeholder statuscode table with the customized 4-value list + Graph mapping
- [`scripts/Customize-SprkTodoStatuscode.ps1`](../../../scripts/Customize-SprkTodoStatuscode.ps1) — new idempotent deployment script
- This notes file

## Follow-up

- **Task 061** (Graph sync handler) implements the bidirectional `statuscode` ↔ `todoTask.status`
  mapping in code per the table above.
- No changes needed to entity definition; the statuscode option set extension is metadata-only.
