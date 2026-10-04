# Task 159 — Events API record authorization and the regarding shape (#1098)

> Branch `task/uac-r2-159` from `work/unified-access-control-r2` @ `6b243f092`. Rigor FULL.
> **Two commits, on purpose.** Commit 1 (`65248b8c3`) fixes all eight routes. Commit 2 then **deletes four of them**
> (PUT /{id}, DELETE /{id}, POST /{id}/cancel, GET /{id}/logs) under the binding owner round 10 item 1 amendment —
> no caller in the repo and not in any published API description (§5). If the main session reads round 10
> differently, dropping commit 2 restores the four routes already gated and tested. §1-§6.2 describe commit 1;
> §5 (deletions), §6.3 and §7 onward describe the final state.

## 0. Step 0 — branch state and live metadata (read-only, spaarkedev1, 2026-10-03)

### 0.1 Sibling tasks on this branch

| Task | On this branch? | Consequence |
|---|---|---|
| 146 (owner on child create / re-parent) | No — POML `pending` | This task writes no owner logic. Whichever of 146/159 lands second rebases onto the other. |
| 156 (re-stamp an intermediate's children) | No — POML `pending` | The PUT re-parent path is recorded in §6 for 156's inventory. |
| 167 (every-route guard + Pending waivers) | No — POML `pending`, `RouteAuthorizationGuardTests` has no `EventEndpoints.cs` entry | Per the binding amendment: no waiver, no ledger entry, no `GovernedFiles` entry is committed. The "Route authorization ledger input" table (§5) is the hand-off. **167 not on branch; main session reconciles the eight /api/v1/events waivers at integration.** |

### 0.2 Live metadata (spaarkedev1 Web API, GET only, operator token via `az account get-access-token`)

**(a) `sprk_event.sprk_regardingrecordtype`** is a **Lookup** to `sprk_recordtype_ref` (ManyToOne, navigation property `sprk_RegardingRecordType`). Escalation trigger 1 does **not** fire.

**(b) Navigation properties of every `sprk_regarding*` lookup on `sprk_event`** (`EntityDefinitions(LogicalName='sprk_event')/ManyToOneRelationships`):

| Lookup attribute | ReferencingEntityNavigationPropertyName | Target entity | Target EntitySetName |
|---|---|---|---|
| sprk_regardingaccount | `sprk_RegardingAccount` | account | `accounts` |
| sprk_regardingagreement | `sprk_RegardingAgreement` | sprk_agreement | `sprk_agreements` |
| sprk_regardinganalysis | `sprk_RegardingAnalysis` | sprk_analysis | **`sprk_analysises`** |
| sprk_regardingbudget | `sprk_RegardingBudget` | sprk_budget | `sprk_budgets` |
| sprk_regardingcommunication | `sprk_RegardingCommunication` | sprk_communication | `sprk_communications` |
| sprk_regardingcontact | `sprk_RegardingContact` | contact | `contacts` |
| sprk_regardingevent | `sprk_RegardingEvent` | sprk_event | `sprk_events` |
| sprk_regardinginvoice | `sprk_RegardingInvoice` | sprk_invoice | `sprk_invoices` |
| sprk_regardingmatter | `sprk_RegardingMatter` | sprk_matter | `sprk_matters` |
| sprk_regardingorganization | `sprk_RegardingOrganization` | sprk_organization | `sprk_organizations` |
| sprk_regardingproject | `sprk_RegardingProject` | sprk_project | `sprk_projects` |
| sprk_regardingrecordtype | `sprk_RegardingRecordType` | sprk_recordtype_ref | `sprk_recordtype_refs` |
| sprk_regardingreportcard | `sprk_RegardingReportCard` | sprk_reportcard | `sprk_reportcards` |
| sprk_regardingservicerequest | `sprk_RegardingServiceRequest` | sprk_servicerequest | `sprk_servicerequests` |
| sprk_regardingworkassignment | `sprk_RegardingWorkAssignment` | sprk_workassignment | `sprk_workassignments` |

The typed-lookup **family** is therefore **14** lookups (all of the above except `sprk_regardingrecordtype`). `TaskActionCore.RegardingFieldByEntity` lists 13 of them — it has no `sprk_regardingagreement` (not changed here; recorded for the owner of that map).

Every navigation property is PascalCase; **none** equals its logical name. Today's create path binds `{logicalName}@odata.bind` = `/{logicalName}s(id)` — wrong on both halves for every type (and `sprk_analysiss` is not a set).

**(c) Entity sets** — in the table above; also `sprk_event` → `sprk_events`, `sprk_eventlog` → `sprk_eventlogs`.

**(d) Denormalized columns on `sprk_event`**: `sprk_regardingrecordid` (String), `sprk_regardingrecordname` (String), `sprk_regardingrecordurl` (String), `sprk_regardingrecordnumber` (String) and `sprk_regardingrecordtypelogicalname` (String) all **exist**.

**(e) `statuscode` / `statecode`** — they do **not** match `EventDto.cs:117-141` (1-7):

| statuscode | Label | statecode |
|---|---|---|
| 1 | Draft | 0 Active |
| 659490001 | Open | 0 Active |
| 659490002 | Completed | **0 Active** |
| 659490003 | Closed | 0 Active |
| 659490006 | On Hold | 0 Active |
| 659490007 | Reassigned | 0 Active |
| 2 | No Further Action | 1 Inactive |
| 659490004 | Cancelled | 1 Inactive |
| 659490005 | Transferred | 1 Inactive |

Live rows (`GROUP BY statecode, statuscode`): Draft/Active 49, Open/Active 17, **Completed/Active 7**, Cancelled/Inactive 1. There is no "Planned" and no "Deleted". Escalation trigger 2 fires and is **answered by the binding amendment** ("fix the mapping in this task … not an owner question") — see §3.

**(f) Create privilege on `sprk_event`**: **`prvCreatesprk_Event`** (accessright 32). (Read `prvReadsprk_Event`, Write `prvWritesprk_Event`, Append `prvAppendsprk_Event`, AppendTo `prvAppendTosprk_Event`, Delete `prvDeletesprk_Event`.)

**(g) Does the current read shape work live?** **No — GET /, GET /{id} and every /{id} route are failing in dev today**, for more reasons than the POML predicted:

| URL the code sends | Live answer |
|---|---|
| `sprk_events?$select=sprk_regardingrecordtype&$top=1` | **400** `Could not find a property named 'sprk_regardingrecordtype'` |
| the list URL as built at `DataverseWebApiService.cs:349` | **400** `Could not find a property named 'sprk_eventtype_ref'` — the `$expand` uses the logical name; the navigation property is `sprk_EventType_Ref` |
| any query with `$skip=0` | **400** `Skip Clause is not supported in CRM` — the list ALWAYS sent `$skip={skip}` |
| the get-by-id URL as built at `:378` | **400** — and with the two names above corrected, still **400** `Could not find a property named '_sprk_relatedevent_value'` (`sprk_event` has no `sprk_relatedevent` lookup) |

So GET / and GET /{id} have been 500 in dev, and because every /{id} handler starts with the same `GetEventAsync` existence read, GET /{id}/logs, PUT, DELETE, complete and cancel were 500 too. `TodoGenerationService` (app-only, `skip = 0` → `$skip=0`) has also been failing its event query on every run. With the corrected shape (§2) every probe returns **200** (list with filters, `_sprk_regardingrecordtype_value eq {ref}` → 60 rows, `sprk_regardingrecordid eq '{id}' and _sprk_regardingmatter_value eq {id}` → 9 rows).

**(h) `sprk_recordtype_ref` rows** (active), one per API regarding type:

| API type | Logical name | sprk_recordtype_refid |
|---|---|---|
| 0 Project | sprk_project | ca68b3bb-8600-f111-8407-7c1e520aa4df |
| 1 Matter | sprk_matter | e8547bb4-8600-f111-8407-7c1e520aa4df |
| 2 Invoice | sprk_invoice | 8e4a04f1-8600-f111-8406-7c1e525abd8b |
| 3 Analysis | sprk_analysis | d568b3bb-8600-f111-8407-7c1e520aa4df |
| 4 Account | account | 849840bb-3b01-f111-8407-7ced8d1dc988 |
| 5 Contact | contact | ca6d46d0-8600-f111-8406-7c1e525abd8b |
| 6 Work Assignment | sprk_workassignment | e8e1608c-781e-f111-88b3-7ced8d1dc988 |
| 7 Budget | sprk_budget | 934a04f1-8600-f111-8406-7c1e525abd8b |

These are per-environment ids: the code never hard-codes them; it resolves them through `QueryRecordTypeRefAsync`.

**Also found (not in the POML, recorded, NOT changed):** `sprk_event.sprk_priority` is a picklist **100000000 Low / 100000001 Normal / 100000002 High / 100000003 Urgent**, while the API validates and writes 0-3. A create or update carrying a priority therefore writes an invalid option value, and the list's `priority` filter matches nothing. This is outside #1098 and outside every amendment; fixing it changes the API's priority contract, so it is reported to the main session as a finding (see §7), not silently folded in.

### 0.3 Escalation-trigger ledger

| # | Trigger | Fired? | Disposition |
|---|---|---|---|
| 1 | regarding type is not a lookup | No | It is a lookup to `sprk_recordtype_ref`. |
| 2 | live statuscode values differ | **Yes** | Answered by the binding amendment: fixed in this task (§3). |
| 3 | a shipped consumer depends on the unscoped list / non-GUID id | No | The only shipped consumer is the Copilot plugin (`spaarke-bff-openapi.yaml:440-552`), which calls with the user's delegated token, documents `regardingRecordId` as "ID of the related record (matter or project)" (a GUID), and never relies on a non-GUID value. A caller with no systemuser getting the whole tenant was the defect, not a feature. The 403/400 are the fail-closed defaults. |
| 4 | live gate denies an honest user | n/a in development | Main-session gate. |
| 5 | new map / EntitySetByType entry / FinanceAuthorizationFilter declaration types / upload-overload change / ExternalAccess edit | No | See §1: two overloads on the existing filter, each "(entity set, id, operation) check plus one optional table privilege"; no new type. |
| 6 | task 146 landed and conflicts | No | 146 is not on the branch. |

## 1. What changed — per route

| Route (guard spelling) | Before | After |
|---|---|---|
| `GET /api/v1/events` | app-only query; owner clause only when the oid resolved; caller text in `$filter` | query runs **as the caller** (`QueryEventsAsCallerAsync`, MSCRMCallerID = resolved systemuserid); unresolved caller → 403 `sdap.access.deny.caller_unresolved` + `correlationId`, no query; `regardingRecordId` must be a GUID (400 otherwise); owner narrowing kept with the same id |
| `GET /api/v1/events/{id:guid}` | sign-in only | filter `"read"` on `sprk_events({id})` |
| `GET /api/v1/events/{id:guid}/logs` | sign-in only | filter `"read"` on the event; the log query stays app-only |
| `PUT /api/v1/events/{id:guid}` | sign-in only; integer written into a lookup | validation filter → `"write"` on the event → `"event.reparent"` (Write\|Append on the event, only when the body carries a regarding) → `"event.attach_regarding"` (AppendTo on the NEW regarding); a re-parent writes the ADR-024 set and clears every other typed lookup |
| `DELETE /api/v1/events/{id:guid}` | sign-in only; wrote a non-existent statuscode 7 | filter `"write"` (amendment: a soft delete is a status update); sets **Cancelled** + a `Deleted` log row |
| `POST /api/v1/events/{id:guid}/complete` | sign-in only; non-existent statuscode 5 with statecode 1 | filter `"write"`; live Completed (659490002, statecode **0**) |
| `POST /api/v1/events/{id:guid}/cancel` | sign-in only; non-existent statuscode 6 | filter `"write"`; live Cancelled (659490004, statecode 1) |
| `POST /api/v1/events` | sign-in only; app-only bind of a caller-chosen regarding | validation filter → `"event.attach_regarding"` with table privilege `prvCreatesprk_Event` → app-only create with the ADR-024 set + FR-26 stamps |

Denial shapes: on the six `/{id}` routes, rights without Read (None, an absent record, a probe fault) → the uniform 404 (`ProblemDetailsHelper.UniformRecordNotFound`, byte-identical to the handlers' own not-found branch, no id); Read without the route's right → 403 `sdap.access.deny.insufficient_rights`. On the body checks: a missing privilege → 403 `sdap.access.deny.insufficient_privilege`; an unknown regarding id, a denied one, a set-lookup fault and a probe fault → the SAME 403 `sdap.access.deny.insufficient_rights` with one fixed detail.

Order on every route: (1) shape validation (no I/O; its own endpoint filter on POST / and PUT /{id}, the same messages as before plus "type and id together or not at all"); (2) the authorization filter(s); (3) any Dataverse read or write, including the handlers' existence reads and the transition checks (`CanCompleteEvent`/`CanCancelEvent` now run only after the filter, so they cannot disclose the state of an event the caller cannot see).

## 2. The regarding read/write shape (#1098 second half)

* **Write** (`BuildCreateEventPayload`, new `BuildUpdateEventPayload`, shared `AddRegardingWriteSet`): (i) `{navigationProperty}@odata.bind = "/{liveEntitySet}({id:D})"` — the set comes from `IGenericEntityService.GetEntitySetNameAsync`, the SAME call the authorization resolver made, so the record authorized is the record bound; (ii) `sprk_RegardingRecordType@odata.bind = "/sprk_recordtype_refs({row:D})"`, left out (warning) when the environment has no row; (iii) `sprk_regardingrecordid` (lowercase "D"), `sprk_regardingrecordname` (server-resolved for matter/project after the AppendTo check, otherwise the request's), `sprk_regardingrecordurl` (`TodoRegardingBuilder.BuildRecordUrl`), `sprk_regardingrecordnumber` (matter/project; written as null on a re-parent); (iv) the FR-26 stamps from `CoreAncestorResolver.DeriveForHostAsync("sprk_event", …)`, each by its navigation property and its live set; an Error outcome (or a stamp whose set cannot be read) refuses the write — 500 `events.regarding_stamp_failed`, no id, nothing written. On a re-parent every other member of the 14-lookup family is cleared with `{nav}@odata.bind = null` in the same PATCH. No integer is written to `sprk_regardingrecordtype`, no `_x_value` key is written, no set name is pluralized.
* **Read** (`BuildEventQueryUrl`, `EventListSelect`/`EventGetSelect`, `MapToEventEntity`): `$select` names `_sprk_regardingrecordtype_value` and the eight typed `_sprk_regarding*_value` columns; `$expand=sprk_EventType_Ref(...)` (navigation property); no `$skip` (the Web API rejects it — the page window is `$top = skip + top`, the first `skip` rows dropped in-process, `skip + top ≤ 5000` else 400); the regarding filters are `sprk_regardingrecordid eq '{id:D}'` (+ `_{lookup}_value eq {id:D}` when typed) or, for a type alone, `_sprk_regardingrecordtype_value eq {row:D}` (the handler resolves the row; none → empty page, no query); the mapper derives the API's 0-7 type from the typed lookup whose value equals `sprk_regardingrecordid`, so a core stamp beside it is not mistaken for the regarding.
* **Live statuses** (§0.2 (e), amendment): `EventStatusCode` is now the live option set; `GetEventStateCode` writes the matching statecode with every status write (create default Open, PUT StatusCode, complete, cancel, delete); `TodoGenerationService`'s two "exclude completed/cancelled" filters use the live values (they excluded nothing before). The API's StatusCode contract changes (1-7 → the live values); the Copilot plugin uses only the `status=open|completed|cancelled` alias, which keeps working.

## 3. Decisions made under existing decisions (reversible; recorded for the main session)

1. **Soft delete sets Cancelled** (659490004) plus a `Deleted` event-log action. The live option set has no "Deleted" and no "Planned"; the route's own published description already said "setting its status to Canceled". "No Further Action" (2, Inactive) was the alternative; Cancelled was chosen because it is the documented intent.
2. **Completable / cancellable statuses**: Draft, Open, On Hold (the old set minus the non-existent Planned). Reassigned, Closed, Transferred and No Further Action are not transitionable through the API — unchanged in effect (none of those values was ever accepted).
3. **The `$skip`, `sprk_EventType_Ref` and `_sprk_relatedevent_value` repairs** are not named in the POML. They are in the same URL builder / `$select` the read-shape constraint governs, and without them GET /, GET /{id} and every /{id} existence read stay 400/500 in dev, so the live gate could not run.
4. **`ResolveRegardingWriteAsync` posture** follows TodoRegardingBuilder: the record-type row and the name/number read are non-fatal (warning; key left out / request name kept); the core stamp is fatal.
5. **The Delete requirement on PUT StatusCode "Deleted" is gone**, per the binding amendment (a soft delete costs Write). There is no Deleted status left to test for; the POML's seed "the Delete check on PUT StatusCode Deleted" is therefore N/A, replaced by `Put_StatusUpdateNeedsOnlyWrite_TheSoftDeleteStatusIncluded`.

## 4. Placement and component justification (CLAUDE.md §10 / §11)

**Placement (`.claude/constraints/bff-extensions.md`):** in the BFF — these are the existing `/api/v1/events` routes on their existing Dataverse client; no new endpoint, service, DI registration, option, job, column or package. No Dataverse plugin (ADR-002): every check and every write is in the BFF.

| New surface | Existing (grep evidence) | Extension | Cost of doing nothing |
|---|---|---|---|
| Two overloads on `RecordRouteAccessAuthorizationFilterExtensions` (fixed set + route key; declared target + optional table privilege) | `RecordRouteAccessAuthorizationFilter.cs` (task 076: route keys fixed to `{entityLogicalName}`/`{recordId}`, short reasonCodes, "add content" detail); `FinanceAuthorizationFilter` (finance-named, frozen by the amendment) | Extends the existing class; the upload overload is untouched (its tests pass unchanged); no new filter class, no declaration types copied | The eight routes keep authorizing nothing (6 critical + 2 high sweep findings) |
| `IEventDataverseService.QueryEventsAsCallerAsync` (+ RED-4 B fail-loud stub) | `QueryEventsAsync` (app-only); `RetrieveMultipleImpersonatedAsync` (no `$count`) | One method beside the app-only one, sharing ONE URL builder; an optional parameter on the old method would let the endpoint reach the app-only query by omission | GET / stays a tenant-wide read |
| `OperationAccessPolicy` keys `event.attach_regarding` (AppendTo), `event.reparent` (Write\|Append) | `finance.attach_invoice`, `finance.link_invoice`, `entity.associate_document` | No existing key names these acts without misdescribing them in every deny log | No key to gate the regarding checks — an unregistered string denies every caller |
| `ProblemDetailsHelper.UniformRecordNotFound` / `RecordUnavailableReasonCode` (moved) | `FinanceAuthorizationFilter.UniformRecordNotFound` | Moved, bytes unchanged; the Finance members forward (task 130 tests unchanged) | A second copy of the uniform 404 in the events filter |
| `RegardingRecordType` sprk_event navigation-property helper + family list (Models.cs) | `GetLookupFieldName`; InvoiceReviewService's per-entity bind constants | Beside `GetLookupFieldName` (the constraint-named location); attribute → navigation property only, NOT a logical-name → set map (sets come from live metadata) | The create keeps binding `{logicalName}@odata.bind` to `/{logicalName}s(...)`, which the Web API rejects |

No entry was added to `EntityAccessFilter.EntitySetByType` or `SemanticSearchAuthorizationFilter.AuthorizableEntitySets`; no file under `Api/ExternalAccess/**` or `Infrastructure/ExternalAccess/**` changed. `RegardingWrite` is a private record inside `EventEndpoints` (a carrier between the resolver and the two handlers), not a service or a map.

## 5. Route authorization ledger input (for task 167's ledger — main session records it at integration)

167 is not on this branch, so nothing was added to `RouteAuthorizationGuardTests` (no `GovernedFiles` entry, no waiver). **167 not on branch; main session reconciles the eight /api/v1/events waivers at integration.** Route keys are spelled as the guard spells them (its scanner keeps the `:guid` constraint). `C` = `Sprk.Bff.Api.Tests.Api.Events.EventEndpointsAuthorizationContractTests`. One row per route changed or deleted (final state, after commit 2):

| Route key | Mechanism that now decides | Deny test |
|---|---|---|
| `GET /api/v1/events` | **handler decision: query runs as the caller** (impersonated `QueryEventsAsCallerAsync`; an unresolved caller is refused 403) | `C.List_UnresolvedCaller_Is403CallerUnresolved_AndNoEventQueryRuns`; `Sprk.Bff.Api.Tests.AccessControl.DataverseWebApiServiceImpersonationTests.QueryEventsAsCallerAsync_SendsExactlyOneMscrmCallerIdHeader_AndCountsTheTrimmedSet` |
| `GET /api/v1/events/{id:guid}` | filter `AddRecordRouteAccessAuthorizationFilter("read", "sprk_events", "id")` (CallerRecordAccessProbe RPA over OBO) | `C.IdRoute_CallerWithoutRead_GetsTheUniform404_AndNoEventServiceIsCalled(route: "get")` |
| `POST /api/v1/events/{id:guid}/complete` | filter `"write"` on `sprk_events({id})` | `C.WriteRoute_CallerWithReadOnly_Gets403InsufficientRights_AndNothingIsWritten(route: "complete")`; `C.IdRoute_CallerWithoutRead_GetsTheUniform404_AndNoEventServiceIsCalled(route: "complete")` |
| `POST /api/v1/events` | filter `AddRecordRouteAccessAuthorizationFilter("event.attach_regarding", …, "prvCreatesprk_Event")` (Create privilege + AppendTo on the regarding) | `C.Create_WithoutTheCreatePrivilege_Is403InsufficientPrivilege_AndNothingIsWritten`; `C.Create_WithThePrivilegeButNoAppendToOnTheRegarding_Is403_AndNoEventOrLogRowIsWritten` |
| `PUT /api/v1/events/{id:guid}` | **DELETED** (round 10 item 1) | `C.DeletedRoutes_AreNotMapped_AndReachNothing(verb: "PUT", …)` |
| `DELETE /api/v1/events/{id:guid}` | **DELETED** (round 10 item 1) | `C.DeletedRoutes_AreNotMapped_AndReachNothing(verb: "DELETE", …)` |
| `POST /api/v1/events/{id:guid}/cancel` | **DELETED** (round 10 item 1) | `C.DeletedRoutes_AreNotMapped_AndReachNothing(verb: "POST", path: ".../cancel")` |
| `GET /api/v1/events/{id:guid}/logs` | **DELETED** (round 10 item 1) | `C.DeletedRoutes_AreNotMapped_AndReachNothing(verb: "GET", path: ".../logs")` |

**Main-session edit to `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` at integration with 167** (exact text; proven green locally against the final file, §6.3) — add to `GovernedFiles` (unless 167 already did):
```csharp
        new GovernedFile("Api/Events/EventEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/events/* — GET /{id} and POST /{id}/complete carry RecordRouteAccessAuthorizationFilter on "
            + "sprk_events({id}) (no Read → uniform 404; Read without the route's right → 403); POST / carries the same "
            + "filter with the Create privilege and AppendTo on the regarding record; GET / runs its query as the caller "
            + "(task 159, #1098). PUT /{id}, DELETE /{id}, /{id}/cancel and /{id}/logs were deleted (round 10 item 1)."),
```
Record `GET /api/v1/events` in 167's ledger as "handler decision: query runs as the caller" (the amendment: NOT a Permanent waiver), and delete 167's Pending waivers for all eight routes — including the four deleted ones (the stale-waiver rule reports a waiver for an absent route). The census count is unchanged: no endpoint FILE was deleted. Local proof used a temporary Permanent waiver for GET / only so Rule A could run before 167's ledger exists; it was not committed.

### 5.1 Routes deleted (owner round 10 item 1, binding amendment)

**Rule:** delete a route when it has NO caller in the repo AND is NOT in any published API description; fix it otherwise.

**Published descriptions searched:** Glob `src/**/{*openapi*,ai-plugin*,declarativeAgent*,*plugin*.json}` and `src/solutions/**/*manifest*.json` → `CopilotAgent/declarativeAgent.json`, `CopilotAgent/spaarke-api-plugin.json`, `CopilotAgent/spaarke-bff-openapi.yaml`, `CopilotAgent/appPackage/manifest.json`. The BFF serves no runtime OpenAPI document (no `AddOpenApi`/`MapOpenApi`/`UseSwagger` in `Sprk.Bff.Api`). The Copilot API plugin (`spaarke-api-plugin.json:16-84`, functions `listEvents`, `getEvent`, `createEvent`, `completeEvent`, over `spaarke-bff-openapi.yaml:440-552`) is the ONLY description naming `/api/v1/events`, and it names GET /, GET /{id}, POST / and POST /{id}/complete — those four are kept and fixed.

**Callers searched:** Grep `v1/events` over `src` (all file types) → only the BFF's own files and the OpenAPI document; Grep `["'`/]events["'`/]|/events\$\{|events/\$\{` over `src/**/*.{ts,tsx,js,cs,json,yaml}` → no client builds a `/api/v1/events/...` path (`/api/workspace/events/{id}/scores` and `/api/v1/external/events/:id` are other routes); Grep `cancelEvent|deleteEvent|updateEvent|getEventLogs` outside `src/server`, `tests`, `projects` → `Spaarke.Events.Components/useEventsBulkActions.ts` cancels through **`Xrm.WebApi.updateRecord`**, not the BFF; docs mention `/api/v1/events/*` generically (not a published description).

| Deleted route | No-caller evidence | Not-published evidence |
|---|---|---|
| `PUT /api/v1/events/{id:guid}` | no `src` hit outside the BFF; no client path builder | not in `spaarke-bff-openapi.yaml` / `spaarke-api-plugin.json` |
| `DELETE /api/v1/events/{id:guid}` | same | same |
| `POST /api/v1/events/{id:guid}/cancel` | same; the client's bulk cancel uses `Xrm.WebApi` | same |
| `GET /api/v1/events/{id:guid}/logs` | same | same |

**Deleted with them** (only those routes used them — the amendment's "delete the services, filters and options that only the deleted routes used"): the four handlers and their helpers (`UpdateEventInDataverseAsync`, `CreateUpdatedEventDto`, `SoftDeleteEventAsync`, `CanCancelEvent`, `GetValidStatusesForCancellation`, the log mapper), the PUT validation filter and its two authorization resolvers, the API DTOs `UpdateEventRequest`, `EventLogDto`, `EventLogListResponse`, `EventStatusCode.All`/`IsDefined`; in `Spaarke.Dataverse` the `IEventDataverseService.UpdateEventAsync` and `.QueryEventLogsAsync` members with their implementations, stubs and `MapToEventLogEntity`, the `UpdateEventRequest` and `EventLogEntity` models, `BuildUpdateEventPayload`, the re-parent clearing and `RegardingRecordType.EventRegardingLookups`; the `OperationAccessPolicy` key `event.reparent` (with its InlineData rows). Kept: `CreateEventLogAsync` (create and complete still write log rows) and `EventLogAction`.

**Consequences for the POML:** the acceptance criteria for PUT, DELETE, cancel and logs, the `event.reparent` key, the re-parent payload, and live-gate item (e) are superseded by the deletion; their commit-1 proofs (§6.1-§6.2) remain in history. Re-adding any of these routes needs a caller, a gate, and a ledger row.

## 6. Guard seeding (each guard removed, its named test seen failing, then restored and touched)

Scripts: `seed_route.py` / `seed.py` / `run_seeds.ps1` in the session scratchpad. Every seeded file was restored from a byte copy taken before the run (verified with `cmp`) and touched. `C` = `Sprk.Bff.Api.Tests.Api.Events.EventEndpointsAuthorizationContractTests`.

### 6.1 Rule A (`RouteAuthorizationGuardTests`) — local-only `GovernedFiles` entry + a temporary GET / waiver

With `Api/Events/EventEndpoints.cs` added to `GovernedFiles` (Scope.RouteLevelGate) and a temporary Permanent waiver for `GET /api/v1/events`, the whole `RouteAuthorizationGuardTests` class passed (**15/15** — Rule A, Rule B, the census, the stale-waiver rule). Removing each route's filter line(s) from the source text made `EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver` **fail naming that route**:

| Seed (filter removed) | Rule A | Route named in the failure |
|---|---|---|
| `"read"` on GET /{id} | Failed 1/1 | `GET /api/v1/events/{id:guid}` |
| `"write"` on DELETE /{id} | Failed 1/1 | `DELETE /api/v1/events/{id:guid}` |
| `"event.attach_regarding"` + privilege on POST / | Failed 1/1 | `POST /api/v1/events` |
| all three on PUT /{id} | Failed 1/1 | `PUT /api/v1/events/{id:guid}` |
| `"write"` on complete | Failed 1/1 | `POST /api/v1/events/{id:guid}/complete` |
| `"write"` on cancel | Failed 1/1 | `POST /api/v1/events/{id:guid}/cancel` |
| `"read"` on logs | Failed 1/1 | `GET /api/v1/events/{id:guid}/logs` |

The temporary entry and waiver were then removed: `RouteAuthorizationGuardTests.cs` is **unchanged** in this task's diff (amendment: 167 not on branch).

### 6.2 Contract and unit tests

| Seed (what was removed) | Test run | Result | Failure (first message) |
|---|---|---|---|
| GET /{id} filter | `C` (62) | **3 failed** — `IdRoute_CallerWithoutRead…("get")`, `IdRoute_Absent…ByteIdentical…("get")`, `FailClosed_NoBearerToken…("get")` | expected 404, found 500 |
| logs filter | `C` | **3 failed** (the same three, `"logs"`) | expected 404, found 500 |
| PUT filters (all three) | `C` | **7 failed** — the four /{id} theories for `"put"` + `Put_Reparent_NeedsAppend…`, `Put_Reparent_UnknownAndDenied…`, `Put_AuthorizedReparent…` | expected 404, found 500 |
| DELETE filter | `C` | **4 failed** (`"delete"`: without-Read, read-only 403, byte-identical, no-token) | expected 404, found 500 |
| complete filter | `C` | **5 failed** (`"complete"` ×4 + `CompleteAndCancel_InvalidTransition…("complete")`) | expected 404, found 500 |
| cancel filter | `C` | **5 failed** (`"cancel"` ×4 + `CompleteAndCancel_InvalidTransition…("cancel")`) | expected 404, found 500 |
| POST / filter | `C` | **6 failed** (the Create_* privilege/AppendTo/identical-403 tests + `FailClosed_NoBearerToken…("create")`, `FailClosed_PrivilegeCheckThrows…`) | expected 403, found 500 |
| impersonation argument on `QueryEventsAsCallerAsync` (passed `null`) | `DataverseWebApiServiceImpersonationTests` (8) | **1 failed** — `QueryEventsAsCallerAsync_SendsExactlyOneMscrmCallerIdHeader…` | `sent.Values("MSCRMCallerID")` empty |
| the 403 for an unresolved list caller (status swapped to 200) | `C.List_UnresolvedCaller…` (6) | **6 failed** | expected 403, found 200 |
| the GUID parse on `regardingRecordId` | `C.List_NonGuidRegardingRecordId…` (3) | **3 failed** | expected 400, found 500 |
| the re-parent Write\|Append check (`"event.reparent"` line) | `C.Put_Reparent_NeedsAppend…` | **1 failed** | `noAppend` expected 403, found 500 |
| the AppendTo check on create (resolver returns null) | `C.Create_WithThePrivilegeButNoAppendTo…` | **1 failed** | expected 403, found 500 |
| the Create-privilege check (argument removed) | `C.Create_WithoutTheCreatePrivilege…` | **1 failed** | reasonCode `insufficient_rights`, expected `insufficient_privilege` |
| an integer write of `sprk_regardingrecordtype` (added to the builder) | `EventRegardingPayloadTests` (38) | **17 failed** | payload contains `sprk_regardingrecordtype` "because the record type is a lookup, never a number" |
| the sibling-lookup clearing on update (`if (isReparent && regardingType < 0)`; the first attempt `if (false)` did not compile — CS0162 under warnings-as-errors) | `EventRegardingPayloadTests.UpdatePayload_Reparent…` (8) | **8 failed** | payload does not contain key `sprk_RegardingAccount@odata.bind` |
| the core-stamp refusal (`return null` removed) | `C.CreateAndReparent_CoreStampFailure…` | **1 failed** | `NullReferenceException` (no `errorCode` — the write went ahead) |
| the Delete check on PUT StatusCode Deleted | — | **N/A** | removed by the binding amendment (soft delete costs Write; §3.5) |

### 6.3 After the deletions (commit 2)

| Proof | Result |
|---|---|
| Rule A on the trimmed file (temporary `GovernedFiles` entry + GET / waiver, then removed again) | **15/15 pass** — the four kept routes need no waiver beyond GET /'s handler decision; nothing stale |
| Seed: re-map `GET /api/v1/events/{id:guid}/logs` (`() => Results.Ok("seed")`) | `C.DeletedRoutes_AreNotMapped_AndReachNothing(GET, …/logs)` **failed** ("expected one of 404/405, found 200"); the other three cases passed; restored, hash-verified |
| The commit-1 route seeds for GET /{id}, complete and POST / | unchanged code on the kept routes — §6.1/§6.2 results stand |

## 7. Found, not fixed (reported to the main session)

1. **`sprk_event.sprk_priority` is 100000000-based live** (Low 100000000 / Normal 100000001 / High 100000002 / Urgent 100000003) while the API validates and writes 0-3. A create carrying a priority writes an invalid option (the create fails), and the list's `priority` filter matches nothing. Fixing it changes the API's priority contract (and the Copilot description, which lists 100000000-based values with the wrong labels), so it is outside #1098 and not decided here. **Recommend a GitHub issue.**
2. **The kept owner narrowing hides app-created events.** `POST /api/v1/events` creates app-only, so those events are owned by the BFF application user and never match `_ownerid_value eq caller`; the Copilot "my tasks" list will not show events the user created through the API. Unchanged by this task (constraint: keep the narrowing; escalation trigger 4 / owner round 3 D1 territory). Task 146 (owner on create) changes who owns new events.
3. **`EventDetailSidePane` (client) maps Completed → statecode 1**, but live metadata puts Completed (659490002) in statecode 0 (7 live rows are Completed/Active). If the side pane writes that pair, Dataverse refuses it. Not this task's surface; recorded for the owner of `src/solutions/EventDetailSidePane`.
4. **`TaskActionCore.RegardingFieldByEntity` lacks `sprk_regardingagreement`** (the live family has 14 lookups; that map has 13). Not this task's surface.
5. **Performance note:** `DataverseServiceClientImpl.GetEntitySetNameAsync` is uncached, and a create with a regarding asks it 2-3 times (filter, handler, core-stamp set). Metadata calls are cheap but repeated; a per-process cache there would serve every caller.

## 8. Integration-suite expectation changes (`tests/integration/Spe.Integration.Tests/EventEndpointsTests.cs`)

All removals follow the route deletions (commit 2); commit 1 changed nothing in this file (its expectations — "not 404", "400 before auth", "401 unauthenticated" — all hold under the new filters).

| Change | Reason |
|---|---|
| Deleted region `PUT /api/v1/events/{id}` (3 tests) | route deleted |
| Deleted region `DELETE /api/v1/events/{id}` (3 tests) | route deleted |
| Deleted region `POST /api/v1/events/{id}/cancel` (4 tests) | route deleted |
| Deleted region `GET /api/v1/events/{id}/logs` (3 tests) | route deleted |
| Deleted `UpdateEvent_HandlesInvalidStatusCode_WhenAuthenticated` | PUT deleted |
| `EventEndpoints_ReturnProblemDetails_ForUnauthorized`: dropped `/{id}/logs` from its list | an unmapped path is 404, not 401 |
| `PostEndpoints_ReturnProblemDetails_ForUnauthorized`: dropped `/{id}/cancel` | same |
| `EventGroup_UsesCorrectApiVersion`: dropped `/{id}/logs` | an unmapped path's 404 has no body |
| `EventEndpoints_RequireCorrectHttpMethod`: dropped `/{id}/cancel` | the route no longer exists to reject GET |
| `EventEndpoints_RejectPostOnGetEndpoints`: `/{id}/logs` → `/{id}` | `/{id}` is GET-only now that PUT/DELETE are gone; the test keeps a real subject |
| Class remarks list the remaining routes and the deletion | documentation |

## 9. Quality gates (Step 9.5)

### 9.1 code-review (coverage-first; severity / confidence)

| # | Sev | Conf | Finding | Disposition |
|---|---|---|---|---|
| 1 | Warning | high | `EventEndpoints` (CRUD) injects `ICallerSystemUserResolver`, which lives in `Services/Ai/Context` — an AI-namespace type in CRUD code (refined ADR-013 spirit). | Pre-existing in this file (task 152, `CreateEventAsync`) and in the POML's mandated reference (`OfficeEndpoints` task 062); it is an identity resolver, not an AI capability, and `ADR013_AiBoundaryTests` does not list it. Recommend relocating the resolver out of `Services/Ai` in a follow-up; no change here. |
| 2 | Warning | med | ADR-024 "do not duplicate resolver logic": `ResolveRegardingWriteAsync` + `AddRegardingWriteSet` is another per-writer copy of the resolver-field population (TodoRegardingBuilder, DataverseServiceClientImpl analysis stager, InvoiceReviewService). | The POML prescribes exactly this split (values resolved in the BFF handler, a static builder in `Spaarke.Dataverse` that only shapes); TodoRegardingBuilder writes an SDK `Entity` for `sprk_todo` only. Consolidation is a follow-up candidate. |
| 3 | Warning | med | Deep pages cost more: no `$skip` in the Web API, so page N reads `N × pageSize` rows (≤ 5000, else 400). | Bounded and validated; the alternative (skiptoken paging) changes the API's page contract. |
| 4 | Warning | high | Priority 0-3 vs live 100000000+ (§7.1). | Reported, not fixed (scope). |
| 5 | Suggestion | high | `GetEventsAsync` is long (validation, caller resolution, record-type resolution, query, mapping). | Each block is a short, commented section; extraction would add indirection for one caller. |
| 6 | Suggestion | med | `QueryEventsCoreAsync` keeps the pre-existing catch-log-rethrow. | Pre-existing pattern preserved verbatim. |
| 7 | Suggestion | med | The live statecode mapping is held twice: `DataverseWebApiService.GetEventStateCode` (literals) and the API's `EventStatusCode` constants. | Both pinned by `EventRegardingPayloadTests.LiveNames_EventStatusCodes_…`; a single source would need a new shared constants class (out of the "nothing else new" constraint). |
| 8 | Suggestion | low | `TodoGenerationService` (Services layer) now references the API DTO `EventStatusCode`. | Same assembly; the alternative was repeating the literals a third time. |
| 9 | Info | high | `RecordRouteAccessAuthorizationFilter.cs` 233 → 485 lines (three target shapes on one cohesive filter); `EventEndpoints.cs` 1246 → ~1040 after the deletions. | Cohesive; task 170 renames the finance filter, not this one. |

Security review: every route fails closed (no token / OBO failure / probe fault / resolver fault / set-lookup fault → 404 or 403, never app-only); no denial body carries an id; the GET / injection is closed (GUID-only, every `$filter` clause from a typed value); the server-side regarding name read happens only after AppendTo is proven and is never returned to the caller. No secrets, no new packages (§10: `dotnet list package --vulnerable --include-transitive` → no vulnerable packages). **No Critical findings.**

### 9.2 adr-check

| ADR | Result |
|---|---|
| ADR-001 Minimal API | ✅ routes stay in `MapEventEndpoints` |
| ADR-002 no plugins / WP-1 | ✅ checks and writes in the BFF; the regarding set + FR-26 stamp have one server-side owner per write |
| ADR-003 fail closed / deny codes | ✅ machine-readable `sdap.access.deny.*` codes; every error path denies |
| ADR-007 Graph isolation | ✅ no Graph types touched |
| ADR-008 endpoint filters | ✅ per-route filters, visible to Rule A; no middleware |
| ADR-009 caching | ✅ no new cache (the probe stays uncached, as built) |
| ADR-010 DI minimalism | ✅ no new interface, registration or service; one interface MEMBER added (and two removed) |
| ADR-013 AI boundary | ⚠️ Warning — code-review #1 (pre-existing `ICallerSystemUserResolver` use) |
| ADR-019 ProblemDetails | ✅ every failure is ProblemDetails with `reasonCode`/`errorCode` and a correlation id |
| ADR-024 polymorphic resolver | ⚠️ Warning — "only ONE entity-specific lookup" vs the FR-26 core stamp (an event regarding an analysis also carries its matter stamp). This is the project's existing FR-26 reconciliation (TodoRegardingBuilder `:202-206`, TaskActionCore `:159-185`), not a new exception; plus code-review #2 (duplicated population logic). |
| ADR-028 auth | ✅ OBO via `CallerRecordAccessProbe`; impersonation only through `DataverseWebApiService`'s existing `impersonateSystemUserId` (no `CallerId` assignment) |
| ADR-038 testing | ✅ no `Mock<HttpMessageHandler>` (hand-written recording handler), no DI-registration or ctor-null tests; every new guard seeded |
| ADR-052 workload placement | ✅ no background work added |

**No violations**, so no §6.5 challenge path is required; the two ADR Warnings are pre-existing patterns the POML itself prescribes.

## 10. Test runs (final tree, before commit 2)

**Affected tests first** (each filtered run confirmed to select tests): commit-1 state 298/298 (events contract 62, payload 38, impersonation 8, membership-publishing 11, TodoGeneration, policy completeness/characterization, finance contract); upload overload + OBO upload tests 259 passed / 1 skipped (pre-existing skip) — **the existing upload overload's tests pass with no expectation change**. After the deletions: 275/275 on the same filter set.

**Full suites, once, at the end** (the machine was running ~286 dotnet/testhost processes from concurrent agents):

| Suite | Result |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests` | 14275 passed, **13 failed**, 54 skipped (14342) in 39 m 28 s. All 13 failures were 1.7-18.8-minute `TaskCanceledException`/client-abort timeouts in unrelated areas (Compose save/refresh seams, AnalysisChatContext, ScopePersonas, HandlerEndpoints, DocumentProfile, ExternalProjectDocumentUpload, Insights ask/search, Office quick-create, WorkspaceLayout, the PredictMatterCost eval harness). **Re-run in isolation: 13/13 passed** (1 m 2 s) → contention, not a regression. None touches the events surface. |
| `tests/Spaarke.ArchTests` (NetArchTest) | **346/346 passed** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104/104 passed** |
| `tests/integration/Spe.Integration.Tests` | **389 passed, 25 skipped, 0 failed** (414); its `EventEndpointsTests` ran (26/26, none skipped) |

`dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` → "no vulnerable packages"; no `.csproj` / package change in the diff. Publish size: skipped per the run instructions.

**Tests beyond the acceptance criteria, each with its one-line reason:**
* `C.DeletedRoutes_AreNotMapped_AndReachNothing` — the deny proof for the four deleted routes' ledger rows (seeded: re-mapping one fails it).
* `EventRegardingPayloadTests.LiveNames_*` — the constraints require every live navigation property, set name, privilege and status value to be "pinned by a test".
* `C.Unauthenticated_EveryRouteIs401` — the AC's 401 criterion; the Spe suite's 401 tests are `SkippableFact` and skip when their environment is absent.
* `C.Complete_WriterGets200…` / `C.GetById_ReaderGets200…` — the ACs' positive "as today" halves.

`scripts/check-task-status-drift.ps1` (task-execute Step 10) is expected to report 159 as drifted (POML `completed`, TASK-INDEX not yet updated): the run instructions reserve `TASK-INDEX.md` for the main session.

## 11. Manual live gate (main session; dev; existing non-admin users in their current BU) — updated for the deletions

Prerequisites: deploy the BFF; the BFF application user must hold `prvActOnBehalfOfAnotherUser` (System Administrator holds it per the 2026-09-30 live facts) or GET / errors (fail closed, never app-only). Read-only confirmation first (RetrievePrincipalAccess as each user): user A has NO Read on event E; user B has Read+Write on event E2 and AppendTo on matter M; B holds `prvCreatesprk_Event`.

| Item | Request | Expect |
|---|---|---|
| (a) | `GET /api/v1/events` as A | E absent; TotalCount does not count it |
| (a) | `GET /api/v1/events/{E}`, `POST /api/v1/events/{E}/complete` as A | the same 404 body as `GET /api/v1/events/{random GUID}`; E's `modifiedon`/`statuscode` unchanged; no new `sprk_eventlog` row |
| (a′) | `PUT`/`DELETE /api/v1/events/{E}`, `POST …/{E}/cancel`, `GET …/{E}/logs` | 404/405 (routes deleted) |
| (b) | `GET /api/v1/events/{E2}` as B; `POST …/{E2}/complete` as B (E2 in Draft/Open/On Hold) | 200; E2 becomes statuscode 659490002 with statecode **0** |
| (c) | `POST /api/v1/events` as A regarding a matter A cannot append to | 403 `insufficient_rights` (or `insufficient_privilege` if A lacks Create); no `sprk_event` row |
| (d) | `POST /api/v1/events` as B, `{subject, regardingRecordType: 1, regardingRecordId: M}` | 201; read-back: `_sprk_regardingmatter_value = M`, `_sprk_regardingrecordtype_value = e8547bb4-…` (the Matter row), `sprk_regardingrecordid`/`name`/`url`/`number` filled, statuscode 659490001 |
| (e) | ~~PUT re-parent~~ | superseded — PUT deleted |
| (f) | `GET /api/v1/events?regardingRecordId=x%27%20or%20sprk_regardingrecordid%20ne%20%27zz` | 400 |

Exact read-back command for (d): `GET {org}/api/data/v9.2/sprk_events({id})?$select=_sprk_regardingmatter_value,_sprk_regardingrecordtype_value,sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordurl,sprk_regardingrecordnumber,statuscode,statecode` (operator token, read-only). Record results (ids to their first 8 characters) in this note.

## 12. Reconciliation notes

* **Task 146** (owner on child create and re-parent): this task writes no owner logic. `CreateEventInDataverseAsync` now builds the request with the regarding write set; 146's owner assignment slots into the same request object. The PUT re-parent path 146's census lists **no longer exists** (deleted). Whichever lands second rebases.
* **Task 156** (re-stamp an intermediate's children): the event re-parent path that would have needed 156's cascade is deleted, so there is nothing to hand to 156 from this API. The create path stamps the event itself (FR-26) and has no children at create time.
* **Task 167**: §5 — add the `GovernedFiles` entry, record GET / as a handler decision, delete the eight Pending waivers (four for deleted routes).
* **Task 170** (rename FinanceAuthorizationFilter): only forwarding members changed in that file; `CreateEventPrivilege` lives in `EventEndpoints`, not beside `CreateInvoicePrivilege`, because the amendment freezes FinanceAuthorizationFilter apart from the forwarding.

## 13. `.claude/**` edits needed

None.
