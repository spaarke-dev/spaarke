# Deferred work and found issues — External Access Platform R4

Each entry here has a matching GitHub issue (project rule, `CLAUDE.md` §2 Process). An entry marked
**Issue: PENDING** was recorded by a sub-agent that may not file issues; the main session files it and replaces the
marker with the issue number.

| # | Found | Item | Severity | Issue |
|---|-------|------|----------|-------|
| D-1 | 2026-11-11, task 002 review pass 2 (F1, pre-existing) | External event create does not write the ADR-024 resolver fields | Defect (user-visible) | #2686 |
| D-2 | 2026-11-11, task 002 review pass 2 (K1, pre-existing) | External event create's `Location` header names a route that does not exist | Defect (contract) | #2687 |
| D-3 | 2026-11-11, task 002 review pass 2 (K4) | Documents-list `canCreate` ceiling is tested for service requests only | Test gap | #2688 |

## D-1 — External event create does not write the ADR-024 resolver fields

**Where.** `ExternalDataService.BuildEventCreatePayload` (`src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs`),
used by `POST /api/v1/external/{projects|matters|workassignments}/{id}/events`.

**What.** The payload binds only the root lookup (`sprk_RegardingProject|Matter|WorkAssignment@odata.bind`). It does
not write `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl` or
`sprk_RegardingRecordType@odata.bind`, though `sprk_event` has them (`docs/data-model/` sprk_event, lines 96-101) and
the internal create writes them (`Api/Events/EventEndpoints.cs` ~`:955-958`).

**Effect.** An event created from the external SPA is missed by any read keyed on the resolver fields, e.g.
`GET /api/v1/events?regardingRecordId={matterId}` in the internal app. Pre-existing on the project route since R3;
task 002 extends the same payload to matters and work assignments.

**Why deferred.** Task 002's contract is "the project route behaves exactly as before"; fixing it changes the write
path for all three roots (record-type lookup resolution, name/url derivation) and the payload assertions in
`ExternalDataServiceTests`. The code remark that claimed the fields were a `sprk_todo`-only construct was corrected
in task 002.

**Fix shape.** Reuse the resolver-field writer the internal `EventEndpoints` uses (no new helper), add the four
fields in `BuildEventCreatePayload`, extend `ExternalRootEventCreateContractTests` to assert them, and add a
data-mutation test. Owner of the decision: R4 (candidate for a P1 follow-up task) or the event/write-path owner.

## D-2 — External event create's `Location` header names a route that does not exist

**Where.** `CreateEventForRoot` in `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` (inherited from the project
route): `Results.Created($"/api/v1/external/{segment}/{id}/events/{eventId}", …)`.

**What.** No `GET …/events/{eventId}` route is mapped on the external plane; a client that follows `Location` gets
404. The SPA does not follow it today. `ExternalRootEventCreateContractTests` pins the current value so a change is
deliberate.

**Fix shape.** Either point `Location` at an existing read (the list route) or drop it and answer 201 with the body
only — a contract change for all three roots, so it needs the owner's choice.

## D-3 — Documents-list `canCreate` ceiling is tested for service requests only

**Where.** `UploadCeiling` map in `ExternalProjectDataEndpoints.cs`;
`tests/integration/contract/Api/ExternalAccess/ExternalRecordDocumentRoutesTests.cs`
(`List_ForAWorkforceContact_CanCreate_FollowsTheUploadRoutesCeiling`).

**What.** Task 002 made the documents list's `canCreate` apply the upload route's workforce ceiling. The over-the-wire
test covers the service-request root (the only root the document-route fixture serves); project, matter and work
assignment use the same map entry shape but are not driven over HTTP.

**Fix shape.** Extend the document-route fixture to serve a project/matter/WA document list for a workforce contact
and add the three cases.
