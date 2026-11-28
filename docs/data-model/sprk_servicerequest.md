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
| `sprk_requesttype` | Choice (local), not required | Which Legal Front Door intake wizard produced the request: NDA Assessment `100000000`, Invention Submission `100000001`, Policy Question `100000002`, Trademark Search `100000003`. Written by the BFF at intake submit (R3 task 020, FR-05). |
| `sprk_intakedata` | Multiline text, max 1,048,576 chars | The submitter's wizard answers as a JSON document. Written by the BFF at intake submit; **not** a human summary (that is `sprk_recordsummary`). **Field-secured** (`IsSecured` 1), like `sprk_createdbyperson` (owner decision 2026-10-11): `Spaarke BFF-Managed Field Readers` read-only (every business unit default team), `Spaarke BFF-Managed Field Writers` read/create/update (the BFF application users only); see `Other/FieldSecurityProfiles.xml`. A user outside the Readers profile does not see the column in the model-driven app. (R3 task 020, FR-05.) |

| `sprk_assignedtointernal` | Lookup → `contact` | The assigned attorney for an intake request (reused, spaarke-intake-pipeline-r1, owner decision 2026-11-27; no new attorney column). It targets **contact**, not systemuser, matching the matter convention (`sprk_assignedattorney1` → contact). On claim or approve the BFF resolves the acting systemuser to its contact (`ContactUserRepresentation.ResolveContactForUserAsync`); no linked contact, or several, is a refusal, never a guess. |

### Intake pipeline columns (spaarke-intake-pipeline-r1, task 010, FR-05)

> **Status**: created on **dev** (`spaarkedev1`) on 2026-11-27 by [`scripts/Add-ServiceRequestIntakeColumns.ps1`](../../scripts/Add-ServiceRequestIntakeColumns.ps1) (idempotent; `-WhatIf` and `-Verify` modes). Not yet on any other environment, and not yet exported into the SpaarkeMaster solution source (`Entities/sprk_ServiceRequest/Entity.xml`); the solution-sync deploy step carries it. Names and option values were agreed with `spaarke-ontology-platform-r1` on 2026-11-27.

| Column | Type | Values | Written by |
|---|---|---|---|
| `sprk_intakechannel` | Choice (local) | Front Door `100000000`, Console `100000001`, Email `100000002`, Copilot `100000003` | BFF only, at create (the door code). |
| `sprk_intakestage` | Choice (local), **column-secured** | Submitted `100000000`, In review `100000001`, Needs triage `100000002`, Awaiting approval `100000003`, Returned `100000004`, Closed `100000005` | BFF only. Field security: `Spaarke BFF-Managed Field Writers` read/create/update (the BFF application users only); `Spaarke BFF-Managed Field Readers` read-only (every business unit default team). A form or `Xrm.WebApi` write by an ordinary user is refused. |
| `sprk_currentrun` | Lookup → `sprk_processingjob` (relationship `sprk_processingjob_servicerequest_currentrun`, Delete = RemoveLink) | — | BFF only (the orchestrator). The run currently driving the request. Deleting a run clears the lookup; the request stays. |
| `sprk_sendemailupdates` | Boolean, **column default No** | Yes `1` / No `0` | BFF only. See the per-channel default rule below. |
| `sprk_correlationid` | Single line text, max length 36 | Canonical lowercase hyphenated GUID text (ADR-044) | BFF only, at create. The same format and value the BFF writes to `sprk_processingjob.sprk_correlationid`. |

**Single writer = the BFF** (ADR-002: no plugin, no client write path). Only `sprk_intakestage` is enforced by column security, because it is the one column whose forgery changes what happens next (approval, return, emails); the other four are written only by the BFF by convention, and nothing keys a security decision on them. The platform System Administrator role bypasses field security and cannot be narrowed; that residual is the same one accepted for every column in the BFF-Managed pair (unified-access-control-r2, owner decision F4).

**Per-channel default for `sprk_sendemailupdates`.** The column default is No. The door code sets the value at create: **ON for email-door requests, OFF for Front Door and Console requests** (Front Door has the portal and bell; spec Assumptions). The spec states no default for Copilot, so a Copilot-door request takes the column default (No) until the owner decides otherwise. The value is applied by the BFF at create, never by the column default, is changeable on the request, and a status email goes to the requester at each status change only when it is on.

**Outbound rows carry none of the five columns.** `spaarke-ontology-platform-r1` writes outbound budget-inquiry rows into this table (`sprk_direction` = Outbound, written by `BudgetInquiryExecutor`; `sprk_disposition` patched by `InquiryDispositionService`) with **all five intake columns null** (`sprk_sendemailupdates` reads null as No). **Every intake read, queue, view, triage list and stage-based query must therefore exclude rows where `sprk_intakestage` is null** (and outbound rows). A row with no stage is not an intake request.

**Ontology-owned, never touched by intake:** `sprk_direction`, `sprk_disposition`, `sprk_responseduedate`. Intake code never creates, renames, alters or writes them.

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
