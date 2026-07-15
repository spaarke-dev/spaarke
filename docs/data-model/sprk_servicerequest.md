# sprk_servicerequest — Data Model

> **Status**: Documented 2026-07-14 (email-communication-solution-r4, task 003 / FR-03)
> **Source of truth**: live Dataverse metadata (`spaarkedev1`), verified via MCP `describe tables/sprk_servicerequest`.
> **Purpose**: First-class Service Request entity. Added to the Communication **Association Engine** as one of the eight association targets (matter, project, invoice, **service request**, work assignment, event, contact/person, organization).

---

## 1. Overview

`sprk_servicerequest` is a Spaarke first-class entity representing a service request. It participates in the ADR-024 polymorphic **regarding** family both as a *parent* (other records regard a service request) and, via its own `sprk_regarding*` lookups, as a *child* that regards other entities.

Prior to R4 the entity existed in Dataverse but had **zero repo footprint** — no data-model doc and no wiring into Spaarke's regarding machinery. Task 003 gives it its first repo presence: this doc plus the additive server-side wiring that makes `sprk_communication → sprk_servicerequest` associations resolve through the existing polymorphic path (no new mechanism — per CLAUDE.md §11).

## 2. Key Columns

| Column | Type | Notes |
|---|---|---|
| `sprk_servicerequestid` | GUID | Primary key (`sprk_documentid`-style PK). |
| `sprk_name` | NVARCHAR(850), **required** | **Primary name** column. Used by `IncomingAssociationResolver.GetPrimaryNameField` → `sprk_name`. |
| `statecode` / `statuscode` | State / Status | Active (0) / Inactive (1); status Active (1) / Inactive (2). |

### Regarding lookups on `sprk_servicerequest` (this entity as a child)

`sprk_regardingaccount` → `account`, `sprk_regardingcommunication` → `sprk_communication`, `sprk_regardingcontact` → `contact`, `sprk_regardinginvoice` → `sprk_invoice`, `sprk_regardingmatter` → `sprk_matter`, `sprk_regardingorganization` → `sprk_organization`, `sprk_regardingproject` → `sprk_project`, `sprk_regardingtodo` → `sprk_todo`, `sprk_regardingworkassignment` → `sprk_workassignment`, plus the denormalized resolver fields (`sprk_regardingrecordid` / `…recordname` / `…recordnumber` / `…recordtype` → `sprk_recordtype_ref` / `…recordtypelogicalname` / `…recordurl`).

## 3. Relationship to `sprk_communication` (the R4 association target)

The association is **bidirectional**:

| Direction | Column | Meaning |
|---|---|---|
| **Communication → Service Request** (R4 forward lookup, created task 001) | `sprk_communication.sprk_regardingservicerequest` → `sprk_servicerequest` | The regarding lookup written by the Association Engine when a communication is associated to a service request. This is the ADR-024 write-on-communication target. |
| **Service Request → Communication** (pre-existing reverse) | `sprk_servicerequest.sprk_regardingcommunication` → `sprk_communication` | A service request that regards a communication. Independent of the forward lookup. |

R4 writes on the **communication** side (`sprk_regardingservicerequest`), consistent with every other communication association target. See [`sprk_communication.md`](sprk_communication.md).

## 4. Association-target wiring (task 003, additive only)

`sprk_servicerequest` resolves end-to-end through the existing regarding machinery — three additive entries, no new mechanism:

- **Outbound write** — `CommunicationService.RegardingLookupMap`: `sprk_servicerequest → (sprk_regardingservicerequest, sprk_servicerequests)`.
- **Inbound resolve priority** — `IncomingAssociationResolver.RegardingFieldPriority`: `(sprk_regardingservicerequest, sprk_servicerequest)` in the business-entity tier (after invoice, before work assignment).
- **Primary-name resolution** — `IncomingAssociationResolver.GetPrimaryNameField`: `sprk_servicerequest → sprk_name`.

### Client-side note

The `sprk_todo` client catalog `TODO_REGARDING_CATALOG` ([`TodoRegardingUpdateBuilder.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts)) is **not** a `sprk_communication` catalog and was intentionally **not** modified — its lookup columns belong to `sprk_todo` (and it uses `sprk_regardingcontact`, whereas communication uses `sprk_regardingperson`). A communication-specific target picker (if needed) belongs to the W4 Code Page "Awaiting Association" review surface (task 042), not to the To Do catalog. See task-003 note in `current-task.md`.

## 5. Consumers

- **BFF** — `Services/Communication/CommunicationService.cs` (outbound regarding write), `Services/Communication/IncomingAssociationResolver.cs` (inbound resolution + polymorphic resolver fields).
- **W1 rungs (tasks 012/013)** — deterministic explicit-reference + participant-correlation matching resolve to this target once wired.
