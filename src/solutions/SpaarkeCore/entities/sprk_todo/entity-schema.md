# sprk_todo Entity Schema

> **Entity Purpose**: First-class custom entity representing a To Do (task) in Spaarke.
> Decoupled from `sprk_event` per smart-todo-decoupling-r3 (D-1, D-3). Mirrors the
> `sprk_communication` 11-lookup + 4-resolver pattern for multi-entity association
> (ADR-024). Optional bidirectional sync to Microsoft Graph `/me/todo`.
>
> **Source**: `projects/smart-todo-decoupling-r3/design.md` §4.1
> **Solution**: SpaarkeCore (unmanaged) — tenant-portable via solution export/import.

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_todo |
| **Display Name** | To Do |
| **Plural Display Name** | To Dos |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | User or Team |
| **Has Activities** | No |
| **Has Notes** | No (rich notes live in `sprk_notes` memo field) |
| **Is Activity** | No (custom entity per D-1, NOT a Dataverse Activity) |
| **Description** | A To Do (task) in Spaarke. May stand alone or be associated with one parent record drawn from a fixed set of supported entities (multi-entity resolution per ADR-024). Optional bidirectional mirror to Microsoft Graph /me/todo. |

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_todoid | To Do | Uniqueidentifier | Auto | - | Primary key |
| sprk_name | Name | String | Yes | 200 | Card title shown on the kanban board |

### Core / Detail Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_description | Description | Multiline (Text) | No | 4000 | Plain-text description |
| sprk_notes | Notes | Multiline (Text) | No | 100000 | Rich-text notes (replaces former `sprk_eventtodo.sprk_todonotes`) |
| sprk_assignedto | Assigned To | Lookup → systemuser | No | - | User assignee. Teams own via standard `ownerid` (per D-1 revised 2026-06-07). |

### Kanban Behavior Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_todocolumn | To Do Column | Choice | No | Today / Tomorrow / Future |
| sprk_todopinned | Pinned | Boolean | No | Locks column assignment against auto-reassign |
| sprk_priorityscore | Priority Score | Integer (0-100) | No | Independent from `sprk_event.sprk_priorityscore` |
| sprk_effortscore | Effort Score | Integer (0-100) | No | Independent from `sprk_event.sprk_effortscore` |
| sprk_duedate | Due Date | DateTime (DateOnly) | No | - |
| sprk_completedon | Completed On | DateTime | No | Set on transition to Completed |

### Regarding (Multi-Entity Resolution per ADR-024)

At most ONE specific regarding lookup is populated at a time. The four resolver fields are populated atomically by `PolymorphicResolverService.applyResolverFields` whenever a specific lookup is set or changed.

| Logical Name | Display Name | Type | Target Entity | Description |
|--------------|--------------|------|---------------|-------------|
| sprk_regardingmatter | Regarding Matter | Lookup | sprk_matter | |
| sprk_regardingproject | Regarding Project | Lookup | sprk_project | |
| sprk_regardingevent | Regarding Event | Lookup | sprk_event | |
| sprk_regardingcommunication | Regarding Communication | Lookup | sprk_communication | |
| sprk_regardingworkassignment | Regarding Work Assignment | Lookup | sprk_workassignment | |
| sprk_regardinginvoice | Regarding Invoice | Lookup | sprk_invoice | |
| sprk_regardingbudget | Regarding Budget | Lookup | sprk_budget | |
| sprk_regardinganalysis | Regarding Analysis | Lookup | sprk_analysis | |
| sprk_regardingorganization | Regarding Organization | Lookup | sprk_organization | |
| sprk_regardingcontact | Regarding Contact | Lookup | contact (OOB) | OOB Contact. Design.md row 97 says "sprk_contact OOB" — actual OOB logical name is `contact`. |
| sprk_regardingdocument | Regarding Document | Lookup | sprk_document | |
| sprk_regardingrecordtype | Regarding Record Type | Lookup | sprk_recordtype_ref | Resolver: which entity type |
| sprk_regardingrecordid | Regarding Record Id | String | - | Resolver: normalized GUID (100 chars) |
| sprk_regardingrecordname | Regarding Record Name | String | - | Resolver: display name (200 chars) |
| sprk_regardingrecordurl | Regarding Record URL | URL | - | Resolver: clickable link (500 chars) |

### Graph Sync State Fields

| Logical Name | Display Name | Type | Max Length | Description |
|--------------|--------------|------|------------|-------------|
| sprk_graphtodolistid | Graph To Do List Id | String | 100 | `/me/todo/lists/{id}` the todo mirrors into |
| sprk_graphtodotaskid | Graph To Do Task Id | String | 100 | Mirrored `todoTask` id |
| sprk_lastsyncedutc | Last Synced UTC | DateTime | - | Last successful sync |
| sprk_synchash | Sync Hash | String | 64 | Short content hash for loop detection (SHA-256 truncated) |
| sprk_syncerror | Sync Error | Multiline (Text) | 2000 | Last sync error message (if any) |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| ownerid | Owner | Owner (User/Team) | Standard ownership |
| owningbusinessunit | Owning Business Unit | Lookup → businessunit | Standard |
| statecode | Status | State | 0 = Active / 1 = Inactive |
| statuscode | Status Reason | Status | See "statuscode (Status Reason)" below — 4 values (Open / In Progress / Completed / Dismissed) per FR-24 |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup | User who created the record |
| modifiedby | Modified By | Lookup | User who last modified the record |

## Choice Values

### sprk_todocolumn (To Do Column)

| Value | Label |
|-------|-------|
| 100000000 | Today |
| 100000001 | Tomorrow |
| 100000002 | Future |

### statuscode (Status Reason)

Customized per smart-todo-decoupling-r3 task 009. Four values that map bidirectionally to
Microsoft Graph `todoTask.status` per FR-24. Mirrors the `sprk_communication` Spaarke
convention: OOB defaults (1, 2) renamed in place; additional values use the `659490001+`
custom range.

| Value | Label | statecode | Graph `todoTask.status` |
|-------|-------|-----------|-------------------------|
| 1 | Open | 0 (Active) | `notStarted` |
| 659490001 | In Progress | 0 (Active) | `inProgress` |
| 2 | Completed | 1 (Inactive) | `completed` |
| 659490002 | Dismissed | 1 (Inactive) | `deferred` |

**Deployment**: `scripts/Customize-SprkTodoStatuscode.ps1` (idempotent; scoped to SpaarkeCore
solution via `SolutionUniqueName` parameter on `InsertStatusValue`/`UpdateOptionValue`).

## Notes

- **Naming**: `sprk_regardingcontact` (NOT `sprk_regardingperson` like `sprk_communication`) per task 002 prompt instructions. Target is OOB `contact`.
- **Ownership**: User or Team. The `sprk_assignedto` lookup is User-only (per design D-1 revised). Teams "assign" by being the owner.
- **No backward compat**: Per design D-12 and OS-2, no shims or migration from `sprk_eventtodo`.
- **Resolver pattern**: Use `PolymorphicResolverService.applyResolverFields` (D-2 / ADR-024). Never set resolver fields directly.
- **Deployment**: Created via `scripts/Deploy-SprkTodoEntity.ps1` (Web API + PowerShell — PAC CLI has no `pac table create`).
- **Solution**: SpaarkeCore (unmanaged). Tenant-portable via export/import.
