# Task 071 - disposition accrual (2026-10-09, round 2 under D-111)

## What shipped
- `Services/Signals/Actions/InquiryDispositionService.cs`: `RecordDispositionAsync(serviceRequestId, replyCommunicationId, disposition)`, `GetByMatterAsync`, `GetByOutsideFirmAsync(firmId?)`.
- `Services/Signals/Actions/InquiryReplyTodoCreator.cs`: when an incoming reply is associated with an open outbound service request, creates ONE "Record the outcome of the budget inquiry" To Do. Hooked into `IncomingCommunicationProcessor` as step 4.9 (after association 4.5 and the arrived event 4.8; per-message scope; never throws).
- `Api/InquiryEndpoints.cs`: `POST /api/v1/inquiries/{serviceRequestId}/disposition` body `{ replyCommunicationId, disposition }` (disposition = the option value 100000000..100000003). `RequireAuthorization`, `RecordRouteAccessAuthorizationFilter("write", "sprk_servicerequests", "serviceRequestId")`.
- `Services/Signals/Actions/PolicyActionRateService.cs`: H-7 action rate.
- `IDataverseUserClient`: one additive default member `PatchAsync(path, body, expectedVersion, ct)` (If-Match `W/"version"`); `DataverseUserClient` implements it. Default throws NotSupported, so no fake breaks and nothing can write unconditionally by accident.
- Ledger/census (uac-r2 guards): `GovernedFile` for the route + count 119 -> 120; `CensusEntry` + `OwnerWriteEntry` for the To Do create; `RunAsUserWritesThatFileNothing` entry for the scalar caller PATCHes.

## Recording (the route), step by step
1. Filter: no Read on the service request = uniform 404; Read without Write = 403.
2. Reads AS THE CALLER: the reply (`sprk_direction` Incoming 100000000, `_sprk_regardingservicerequest_value` == route id, `sprk_associationstatus`), the service request (Outbound, no disposition, active, `versionnumber`). 403/404 on either read = one NotFound answer.
3. PATCH the service request AS THE CALLER with `If-Match: W/"version"`: `sprk_disposition`, `statecode` 1, `statuscode` 2. A 403 = Denied (no Write). A 412 re-reads the row once and retries only if the disposition is still empty; otherwise AlreadyResolved. A missing `versionnumber` = Failed, nothing written.
4. Completes the To Do (deterministic id, `statecode` 1 / `statuscode` 2 Completed / `sprk_completedon`) as the caller; 404 = there was none.
5. A Suggested association of the reply (100000003) becomes Resolved (100000000): the human's choice confirms it.
Steps 4-5 run after the disposition is recorded and are best effort (logged); the response carries `todoCompleted`.

## The To Do
- Name "Record the outcome of the budget inquiry"; id = a **version-5 UUID** derived from the service request id (ADR-024 derivation, no schema change), so a second reply (or a race) hits a duplicate key and is "already exists": exactly one per inquiry, the route finds it by id, and a client can recompute the id to recover the link.
  - Namespace (`InquiryDispositionService.TodoIdNamespace`): `6f1c0a52-3b7e-4d1a-9c2e-5a8d4b7e1f30`. Name: `"inquiry-outcome-todo:" + serviceRequestId` (lower-case hyphenated). id = SHA-1(namespace as 16 network-order bytes ++ UTF-8 name), first 16 bytes, byte 6 = (b & 0x0F) | 0x50, byte 8 = (b & 0x3F) | 0x80, read as an RFC 4122 UUID (standard `uuid.v5(name, namespace)`).
  - Known answers: `11111111-2222-3333-4444-555555555555` -> `231eac72-8c74-5a3d-be96-949700bd959b`; `00000000-0000-0000-0000-000000000001` -> `864cb0f1-b3f1-5de3-909b-cdbe9d6398f9`.
  - The `sprk_notes` first line (`serviceRequestId=`) is a HINT only.
- Regarding the MATTER (builder: lookup + ADR-024 pair + core-ancestor stamp). Assigned To = the matter's `sprk_assignedtointernal` contact, else `sprk_assignedattorney1` (AssignedToDefaults, task 152), else blank with an `inquiry_todo_unassigned` warning. Due = the assignee's today (DataverseRecipientDays, D-25), else UTC.
- Owner = `IRecordOwnershipResolver.ForChild` over the matter (secure-if-any), exactly as `TaskActionCore` owns a server-created task.

## Deviations (for the coordinator)
1. **Owner is the matter's team, not the contact's system user.** uac-r2 invariant I-6 (OfficeService, TaskActionCore, OwnedChildWrite, root of the census): a server-created child is owned by the resolver's team, never an individual; the PERSON it is for is `sprk_assignedto` (the people-targeting surface, task 152). So "fall back to the matter's owner" is automatic (the matter's business-unit team owns it) and `contact.sprk_systemuser` is not read for ownership. Owning it by the contact's user would need a uac-r2 exception (CLAUDE.md 6.5 path A); say so and it is a few lines in `InquiryReplyTodoCreator`.
2. **The To Do regards the matter, and the service request rides in `sprk_notes` (`serviceRequestId=<guid>`, first line).** sprk_todo allows ONE specific regarding lookup (ADR-024; the builder throws on two), and the live `sprk_regardingservicerequest` lookup cannot be set beside the matter. Filing it against the service request instead would not work either: the service request is itself a core record in the stamp set, so the To Do would carry no matter. A real column for the link would be a schema decision (not made here).
3. The hook creates the To Do only at ingestion. A reply the ladder links LATER (a Suggested association a person confirms in reconciliation) raises no To Do; the person can still call the route from the reply. Known limit.
4. A To Do under a secure matter: the service request itself cannot be created under one (070, Refused), so no secure inquiry exists to reply to.

## UI call needed (not built; for the worklist/To Do task)
On the To Do card for an inquiry (name "Record the outcome of the budget inquiry"): read `serviceRequestId` from the first line of `sprk_notes` (a hint). **Fallback when the line is missing or not a GUID:** list the matter's open outbound service requests (`sprk_servicerequests?$filter=_sprk_regardingmatter_value eq {matterId} and sprk_direction eq 100000001 and statecode eq 0`) and pick the one whose derived v5 id (above) equals this To Do's `sprk_todoid`; list the inquiry's incoming replies (`sprk_communications?$filter=_sprk_regardingservicerequest_value eq {id} and sprk_direction eq 100000000`, newest first); show a four-way picker (Write-off 100000000, Budget Revised 100000001, Scope Approved 100000002, No Action 100000003) next to the reply; on confirm `POST /api/v1/inquiries/{serviceRequestId}/disposition` with `{ "replyCommunicationId": "<reply id>", "disposition": <value> }`. 200 = `{ serviceRequestId, todoCompleted }`; 404 = not found / not readable; 403 = no Write on the inquiry; 409 `reasonCode` `inquiry.already_resolved` | `inquiry.reply_mismatch` | `inquiry.not_a_reply` | `inquiry.not_an_inquiry`. Refresh the card afterwards (the To Do is completed).

## Action rate (H-7)
`PolicyActionRateService.GetAsync()`: Signals resolved (`sprk_resolvedon`) in the last 90 days with `sprk_resolutiontype` Acted (100000000) or Dismissed (100000001) only, grouped by `sprk_policycode`, `sprk_policyversion`, `sprk_lane`. Rate = acted / (acted + dismissed); null with `TooFewToJudge` below 5. Superseded, Policy Retired, Condition Cleared and open Signals are not counted. A group with no lane or policy code is skipped and logged. Never reads Decision Records.

## ADR-038 pairing
Describe of sprk_servicerequest, sprk_communication, sprk_signal, sprk_todo on spaarkedev1 (2026-10-09), plus the GROUP BY shapes run through read_query on sprk_signal and sprk_servicerequest (valid, zero rows). The FetchXML and the create were not run live (read_query is SQL only; no Dataverse writes allowed); live proof of the route, the To Do create and the resolver's answer for a matter-filed To Do belongs to 043/055.

## Mutation proofs (each fails the suite)
No 412 retry; missing version written anyway; reply/inquiry mismatch unchecked; incoming unchecked; Suggested association not confirmed; To Do not completed; random To Do id; existence check dropped; attorney before internal; Condition Cleared counted; 30-day window; threshold 4; lane forced to Do.

## Size
Release publish: 192 files both sides; 127,756,800 -> 127,809,460 bytes (+52.7 KB, whole publish dir) against a fresh build of the project-branch tip ec80b4e80, both from short paths.

## D-106 (not fixed)
Caller-scoped tallies undercount (F-43); the Report Card has no reader yet; the publish-measurement baseline.
