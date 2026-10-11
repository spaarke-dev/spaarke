# Task 035 — Legal Front Door intake submit endpoint (FR-05 server, FR-06 intake half)

Date: 2026-10-11. Rigor FULL (tags bff-api, auth, testing). An implementer agent built this in an isolated worktree. The review passes and the task bookkeeping (TASK-INDEX, POML status) belong to the main session.

## Two findings for the owner (read first)

1. **POML escalation trigger fired: a licensed workforce systemuser CAN have no contact on a real path.**
   - `WorkforcePrincipalResolver.cs:190` starts the derived contact as null. `:218` tries the inline link (task 141). `TryLinkSystemUserAsync` returns null without linking whenever `IdentityLink:Reconciliation:WritesEnabled` is not `true` (`:438`; `ContactIdentityBinder.cs:147-154`, default off). It also returns null when a link attempt was refused within the last 10 minutes, or when the link throws.
   - `CallerPrincipalResolver.cs:672` then sets `ContactId = Guid.Empty`.
   - Such a caller **cannot submit**: the route answers 403 `sdap.external.intake.no_contact`, with the detail "Your account is not linked to a contact record, so a request cannot be filed in your name. Ask your administrator to link your account to a contact." This is the behaviour the constraints already require. The task does not decide whether licensed staff with no contact should be able to submit; that is the owner's call. The Service Requests module would not show such a row to them either (`ExternalAccessModule.cs` descriptor: `ContactId != Guid.Empty`).
   - Options for the owner:
     - (a) keep the 403 and turn on the link writes in each environment;
     - (b) create or link a contact at submit time;
     - (c) give a systemuser requester another attribution column.
2. **The intake row has no owner rule (new; not in the POML).**
   - The row is created app-only with no `ownerid`, so the BFF application user (root business unit) owns it.
   - Task 146's rule for this surface is that every create carries the owner derived from its parent (`ExternalDataService.RequireOwner`). A service request is a root and is filed under nothing, so that rule has no parent to derive from.
   - The resolver's "unfiled" fallback is the acting user's business-unit team (`RecordOwnershipResolver.cs`, `UnfiledOwnership.ActingUserTeam`). It **refuses** when there is no systemuser, which is every workforce contact and every CIAM partner. Applying it would therefore break FR-05/FR-06 for exactly the populations the owner named.
   - R2 task 043 ("Legal handoff: MDA routing/assignment") was the planned owner of routing, and it was never built. R3 does not scope it.
   - Consequences today:
     - The requester's own reads are unaffected, because they are app-only and keyed on `sprk_requestedby`.
     - Legal staff reach the row in the model-driven app only with a role depth that covers the root business unit.
     - Task 031's documents on a service request use the owning business unit's container, which is the root's.
   - Precedent for application-owned unfiled rows: unfiled `sprk_communication` rows keep their creator (escalation E1, accepted in owner round 10, item 8).
   - The choice sits in one place, `BuildServiceRequestCreatePayload`, so it can change with one edit.
   - Options:
     - (a) leave it application-owned until routing exists;
     - (b) a configured intake owner team;
     - (c) the acting user's team for systemusers, plus (a) or (b) for contacts.

## What it does

`POST /api/v1/external/servicerequests` creates one `sprk_servicerequest` from a wizard submission and returns 201 with `{ "sprk_servicerequestid": "…" }`. There is no `Location` header, because no GET serves `/servicerequests/{id}`.

- **Group.** `ExternalIntakeEndpoints.MapExternalIntakeEndpoints` is called from `ExternalAccessEndpoints.MapExternalUserEndpoints`, inside the `/api/v1/external` group. It inherits `ExternalCollaboration` and `CallerPrincipalAuthorizationFilter`. The handler never branches on plane.
- **Order.** No step reads or writes Dataverse until every earlier step has passed:
  1. **Tier-1, server-side.** The route calls `ModuleEntitlementResolver.IsEntitledAsync(principal, user, "legal-front-door")`. A caller who is not entitled gets 403 `sdap.external.intake.module_not_entitled`. This is the ONE shared check (review 011 K2):
     - it answers from `ResolveAsync`, the set `/me/entitlements` returns;
     - the resolver and the `service-requests` descriptor both read the same bound `IOptions<ExternalAccessOptions>` value;
     - so a CIAM caller is refused while `PartnerServiceRequestsEnabled` is off, and admitted when it is on, with no plane test in the handler.
  2. **Requester.** `principal.ContactId` must be non-empty. Otherwise the answer is 403 `sdap.external.intake.no_contact` (finding 1).
  3. **Validation** (each failure is 400 with an `errorCode`):
     - `requestType` must be one of 100000000 (NDA Assessment), 100000001 (Invention Submission), 100000002 (Policy Question) or 100000003 (Trademark Search). Otherwise `EXT_INTAKE_INVALID_REQUEST_TYPE`.
     - `title` is required and may be at most 850 characters after trimming (`sprk_name` is NVARCHAR(850)). Otherwise `EXT_INTAKE_INVALID_TITLE`.
     - `answers` must be a JSON object of at most 100 KB (102,400 UTF-8 bytes of its raw text). Otherwise `EXT_INTAKE_INVALID_ANSWERS`.
  4. **Create.** `ExternalDataService.CreateServiceRequestAsync` makes one app-only Web API POST (no OBO). It sends `Prefer: return=representation` with `$select=sprk_servicerequestid`, so the field-secured answers are not echoed back. A Dataverse failure is 500 `EXT_INTAKE_INTERNAL_ERROR`.
- **Payload.** `BuildServiceRequestCreatePayload` writes exactly five columns:
  - `sprk_name`;
  - `sprk_requesttype`;
  - `sprk_intakedata` (the answers' raw JSON);
  - `sprk_direction = 100000000` (Inbound; `Entity.xml` optionset `sprk_servicerequest_sprk_direction`);
  - `sprk_RequestedBy@odata.bind = /contacts({caller contact})`. The navigation property is `sprk_RequestedBy` (`Other/Relationships/Contact.xml`, `sprk_servicerequest_RequestedBy_contact`).
- **No creator stamp.** `sprk_servicerequest` has no `sprk_createdbyperson` or `sprk_createdbycontact`. The requester is the row's attribution, so `StampCreator` is not called.
- **Client input never names the requester.** `SubmitServiceRequestRequest` is closed: it binds `requestType`, `title` and `answers` only. A `requestedBy`, `sprk_requestedby`, bind, `ownerid`, creator or direction in the body does not bind.
- **Throttle.** `RequireRateLimiting("dataverse-query")`, the same per-user policy as the task 034 message post.

## Decisions

1. **The entitlement check is in the handler, not a new endpoint filter.** The neighbouring external routes make their decisions in the handler and declare them to the route guard as a `HandlerDecision`; this route does the same. A reusable module filter would be new surface with one caller (§11 question 3 has no answer yet).
2. **`IsEntitledAsync` calls `ResolveAsync`; it does not re-derive the rule.** This is the K2 no-drift requirement. A workforce caller with App-Roles therefore causes the same cached map read that `/me` does. The default modules never depend on that read.
3. **Body JSON names `requestType` / `title` / `answers`,** as the brief states them. The client wizards (tasks 052 and 053) do not exist yet, so this is the contract they are built against.
4. **The answers are stored verbatim (raw JSON text), not re-serialized.** The 100 KB limit is measured on exactly what is stored.
5. **No per-route request-size limit.** Every answers object over 100 KB, up to Kestrel's global body limit, gets the 400 the acceptance criteria name; a route `RequestSizeLimit` would turn some of those into 413. Memory per request is bounded by the global limit and the rate limit (K1).
6. **The route guard's census was updated** (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`), as tasks 033 and 034 did:
   - a `GovernedFiles` entry (GroupGated);
   - the endpoint-file count, 119 → 120, with its log entry;
   - `IsEntitledAsync` admitted to `CallerContextSeams`, with its reason;
   - a `HandlerDecision` for the route.

## Deviations

- POML step 7 (TASK-INDEX and POML status) was not done: the main session owns it.
- `/conflict-check` was not run, because the implementer worktree has no PR. Shared files touched: `Api/ExternalAccess/ExternalAccessEndpoints.cs`, which has one added block after the task 033 message mapping, and `Infrastructure/ExternalAccess/ExternalDataService.cs` and `ModuleEntitlementResolver.cs`, which are additive only.

## §11 component justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `POST /api/v1/external/servicerequests` (`ExternalIntakeEndpoints.cs`) | No intake or service-request submit route exists in `Api/ExternalAccess` (grep 2026-10-10; R2 task 031 never built). | No: there is no service-request write path to extend. The create reuses `ExternalDataService` and the resolver. | The four quick-start wizards cannot create a request (R2 UAT 4B; spec FR-05). |
| `ModuleEntitlementResolver.IsEntitledAsync` + `LegalFrontDoorModule` | `ResolveAsync` (the set) and `MeEntitlementsEndpoint`. | Yes: it is a method on the existing resolver that answers from `ResolveAsync`, not a second rule. | Without it the route would re-derive Tier-1 from the plane and the switch, which is the drift review 011 K2 forbids, or it would rely on the client hiding the card (ADR-028 A3). |
| `ExternalDataService.CreateServiceRequestAsync` + `BuildServiceRequestCreatePayload` + `ServiceRequestTypes` / `ServiceRequestDirectionInbound` / `RequestedByBindKey` | The service's other creates (event, to-do, document). | Yes: a method on the existing app-only service, written in the same idiom (pure builder plus virtual sender). | No row can be written. |
| `SubmitServiceRequestRequest`, `ExternalServiceRequestCreatedDto` | `PostExternalMessageRequest` (closed-DTO pattern). | No: a different body. | No request or response contract. |

No new DI registration, package, option, secret or background work.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

The route lives in the BFF:
- in a new endpoint file inside the existing `/api/v1/external` group;
- with additive methods on the existing `ExternalDataService` and `ModuleEntitlementResolver`.

The external plane's authorization (`CallerPrincipal`, the module entitlement resolver, the module registry) and its app-only Dataverse writer already live in the BFF and are composed in-process. The work is request-scoped CRUD: no AI (`PublicContracts` is not involved), no background work (ADR-052 is not triggered), no new package, options or secret.

## Tests

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalIntakeEndpointsTests.cs` has 26 tests. They run at HTTP level through the REAL group, filter, route and handler (`ExternalCollaborationTestFixture`), in two hosts: switch OFF and switch ON. Doubles sit only at boundaries:
- the caller principal;
- the Dataverse create, held in memory and fed by the REAL `BuildServiceRequestCreatePayload`;
- the resolver's App-Role map read.

The entitlement rule, the bound switch and the `service-requests` descriptor are the host's real ones.

| Acceptance criterion | Test(s) |
|---|---|
| Workforce NDA → 201, five columns correct | `WorkforceSystemUser_SubmitsNdaAssessment_Gets201_AndTheRowCarriesTheFiveIntakeColumns`; `WorkforceContact_SubmitsEachRequestType_…` (×4) |
| Row in the caller's Service Requests read, no other caller's | `SubmittedRow_IsInTheCallersServiceRequestsRead_AndInNoOtherCallers` (the host's own descriptor `ScopeRows` over the row as read) |
| CIAM, switch off → 403 (and `/me` and the descriptor agree) | `CiamCaller_SwitchOff_Gets403_NothingIsCreated_AndMeAndTheDescriptorAgree` |
| CIAM, switch on → 201, visible only to that caller | `CiamCaller_SwitchOn_Gets201_AndTheRowIsVisibleOnlyToThatCaller_AndMeAgrees`; `WorkforceCaller_SwitchOn_IsUnchanged_Gets201` |
| Unentitled → 403 | `WorkforceCaller_TheResolverDoesNotEntitleToLegalFrontDoor_Gets403_…` (proves the route asks the resolver, not the plane) |
| Unknown type → 400 | `UnknownRequestType_Gets400_…` (missing, 0, 100000004, −1) |
| Answers > 100 KB → 400 | `AnswersOver100Kb_Gets400_…`; boundary `AnswersOfExactly100Kb_Gets201` |
| Non-object answers → 400 | `AnswersThatAreNotAJsonObject_Get400_…` (missing, null, array, string, number) |
| Body-named requester ignored | `RequesterOwnerAndCreatorNamedInTheBody_AreIgnored_TheRequesterIsTheCaller` |
| No contact → 403 with a clear reason | `SystemUserWithNoLinkedContact_Gets403_NoContact_…` |
| Title required (brief) | `MissingOrBlankTitle_Gets400_…` (×2); `TitleLongerThanSprkName_Gets400_…` (beyond the AC: without it, an over-long title reaches Dataverse and comes back as a 500) |

`ModuleEntitlementResolverTests` gains 7 cases for `IsEntitledAsync`:
- CIAM follows the switch and equals the `ResolveAsync` set;
- every workforce caller is entitled with no map row, with case-insensitive codes;
- a blank or ungranted code is false.

### Fail-before proofs

- With the route's mapping reverted (`ExternalAccessEndpoints.cs` at `9cfa31e4c`), all **26/26** intake tests fail; they pass after.
- `IsEntitledAsync` did not exist before this task, so the resolver tests fail to compile against the old file.
- Mutation: with the entitlement gate disabled (`if (false && …)`), exactly the 2 entitlement tests fail (CIAM switch-off; resolver-unentitled). Both pass with the gate restored.

## Build, tests, publish size

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings (SDK 10.0.101) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | 0 errors, 0 warnings |
| Intake + resolver tests | 69 / 69 pass |
| `--filter FullyQualifiedName~ExternalAccess` (includes `Infrastructure.ExternalAccess`) | 2,063 / 2,063 pass |
| Full unit project | 19,744 passed, 54 skipped, 21 failed. All 21 were `TaskCanceledException` HTTP timeouts at about 3 minutes each (Office, Insights, Compose, RAG, document identity, FileAccessBroker). The run overlapped three Release publishes and an ArchTests run, so the machine was saturated. Re-run alone, the affected classes pass: 193 pass, 7 skipped, 0 failed, plus `FileAccessBrokerIdentityTests` 24 / 24. **Net: 0 real failures.** |
| `dotnet test tests/Spaarke.ArchTests/` | First run: 3 failures (route-guard census: new file not governed, count 119≠120, route undeclared). After the ledger commit: **940 / 940 pass**. |
| `dotnet list … package --vulnerable --include-transitive` | No vulnerable packages. No package was added. |

**Publish size** (bff-hygiene item 4). Each side was built from a fresh `git archive` of its commit into a short path: `C:\wt035b` is this branch at `f904d4318`, `C:\wt035m` is `origin/master` at `89480bff8`, and `C:\wt035p` is the parent at `9cfa31e4c`.
- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`.
- Build: framework-dependent linux-x64 (the RID in the csproj), Release.
- Zip: `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs **included** (4 PDBs).
- Every publish has 192 files, so the counts match.

| Tree | Zip bytes | MB |
|---|---|---|
| branch (`f904d4318`) | 38,249,033 | 36.48 |
| fresh master (`89480bff8`) | 38,231,982 | 36.46 |
| parent (`9cfa31e4c`) | 38,243,058 | 36.47 |

- Delta vs fresh master: **+17,051 bytes (+0.02 MB)**. The R3 branch's other unmerged work is in this figure too.
- Delta caused by task 035 alone (vs parent): **+5,975 bytes**.
- Total 36.48 MB is under the 55 MB review line and the 60 MB hard stop. BFF Hygiene §10 and NFR-01 are verified.

## Known limits (not fixed here)

- **K1:** there is no per-route request-body cap below Kestrel's global limit (decision 5). Bodies are bounded by that limit and by the per-user throttle.
- **K2:** the response carries no `Location` header, because there is no GET for one service request on this surface. Reads go through the module seam.
- **K3:** findings 1 and 2 above are owner decisions, not defects in this change.

## Files

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalIntakeEndpoints.cs` (new)
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalIntakeDtos.cs` (new)
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalAccessEndpoints.cs` (one mapping)
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` (builder + create)
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs` (`IsEntitledAsync`, `LegalFrontDoorModule`)
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalIntakeEndpointsTests.cs` (new)
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ModuleEntitlementResolverTests.cs`
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`

## Review fix round

Two adversarial reviews of `47fe1f7f6` returned four fix-now items (F1 fail-closed contact read, F2 Dataverse failure mapping, F3 body cap, F2 ledger hardening). I worked from the brief's statement of them, not the full review text. Ownership is unchanged: `BuildServiceRequestCreatePayload` still writes no `ownerid`, pending the owner decision. Fix 3 supersedes K1 above (there is now a per-route cap).

### 1. Contact-read fault reported as "not linked" (F1, fail-closed)

- **Cause.** `WorkforcePrincipalResolver` sets `contactUnreadable` when the derived-contact read throws. `WorkforcePrincipalStrategy` mapped the principal to `ContactId = Guid.Empty` and dropped the flag, so a linked lawyer hit by a Dataverse 429 got 403 `sdap.external.intake.no_contact` ("ask your administrator").
- **Fix.** `CallerPrincipal.ContactUnreadable` (default false) is copied from `WorkforcePrincipal.ContactUnreadable` where the workforce principal is built. The CIAM strategy and every other construction site are untouched, so no other caller changes behaviour. `ExternalIntakeEndpoints` returns 503 `EXT_INTAKE_CONTACT_UNREADABLE` ("...Nothing was submitted; try again shortly.") BEFORE the `Guid.Empty` -> 403 branch.
- **Tests.** `SystemUserWhoseContactCouldNotBeRead_Gets503_NotTheNoContact403_AndNothingIsCreated` and `CallerPrincipalResolverTests.WorkforceStrategy_SystemUser_CarriesTheContactUnreadableFlagOntoThePrincipal` (true and false cases).
- **Fail-before.** With the flag copy and the 503 branch disabled: the endpoint test failed, and the resolver test failed for `contactUnreadable: True` (the `False` case is the control and passes).
- **Other consumers of an empty `ContactId` on the external surface (listed, NOT changed):**
  - `Infrastructure/DI/ExternalAccessModule.cs:693`, `ServiceRequestsModule.AccessibleRecordIds`: an empty contact gives an empty set, so an unreadable contact shows an empty Service Requests tab, not an error.
  - `Api/ExternalAccess/ExternalProjectDataEndpoints.cs:633`: to-do create maps `Guid.Empty` to "unassigned"; an unreadable contact creates the to-do unassigned.
  - `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:294`, `IsContactPrincipal`: false for a systemuser either way.
  - `Api/ExternalAccess/ExternalMessageEndpoints.cs:543`: sender attribution treats `Guid.Empty` as "not the sender".
  - `Infrastructure/ExternalAccess/ExternalDataService.cs:729`: `BuildServiceRequestCreatePayload` rejects an empty requester (backstop).

### 2. Dataverse failures all became 500 (F2)

- **Fix.** `ExternalDataService.CreateServiceRequestAsync` maps `HttpRequestException`, `TaskCanceledException` when `!ct.IsCancellationRequested`, and 429 / 5xx responses to the existing `DataverseUnavailableException` (no new type). Other non-success (4xx) stays `InvalidOperationException`; a caller who cancelled still sees the cancellation. The handler catches it: 503 `EXT_INTAKE_DATAVERSE_UNAVAILABLE`, detail "Nothing was submitted; try again.". Everything else stays 500 `EXT_INTAKE_INTERNAL_ERROR`. Neither response carries exception text.
- **Tests.** Route level: `DataverseUnavailableOnTheCreate_Gets503_WithTheErrorCode_AndNoExceptionText`, `AnyOtherFailureOnTheCreate_Gets500_WithTheInternalErrorCode_AndNoExceptionText`. Service level (new file `Infrastructure/ExternalAccess/ExternalDataServiceServiceRequestCreateTests.cs`): transport failure, HttpClient timeout, caller cancel (stays a cancellation), 429/500/502/503/504, 400/403/404 (plain failure), success.
- **Fail-before.** With the service mapping reverted and the handler catch disabled: the route 503 test failed (got 500 `EXT_INTAKE_INTERNAL_ERROR`) and all 7 service-level mapping tests failed (transport, timeout, 5 status codes). The 500 test and the 4xx / cancel / success tests are controls and pass before and after.

### 3. No per-route body cap (F3)

- **Fix.** `.WithMetadata(new RequestSizeLimitAttribute(ExternalIntakeEndpoints.MaxRequestBodyBytes))`, 256 KB (precedent `ComposeSaveEndpoints.cs:41`). Answers over 100 KB and under the cap keep the AC's 400 (`AnswersOver100Kb_Gets400_...` still passes); a body over the cap is 413. The route description and `ProducesProblem(413)` say so.
- **Test host does not enforce it.** I wrote a 256 KB + 1 body test first; it got the handler's 400, not 413, because TestServer has no `IHttpMaxRequestBodySizeFeature`. I removed it and assert the metadata on the endpoint instead (`TheRoute_CarriesTheRequestSizeLimitMetadata`, `IRequestSizeLimitMetadata.MaxRequestBodySize == 262144`), the same proof the Compose contract test uses. The 413 itself is Kestrel behaviour and is unproven here.
- **Fail-before.** With the `WithMetadata` line commented out, the metadata test failed.

### 4. Ledger hardening (F2)

- **Fix.** `IsEntitledAsync` in `CallerContextSeams` is a Tier-1 module entitlement, not a per-resource decision. `EveryHandlerDecisionIsVerified` now also fails any `HandlerDecision` whose `Seam == "IsEntitledAsync"` has a `Route` containing `{` (`ModuleSeamOnRecordRouteViolations`). The seam's Reason says so. Added a must-fire / must-not-fire control, `ModuleEntitlementSeam_CannotCreditARecordRoute`. The intake route's ledger line citations were moved to the new line numbers.
- **Fires.** I added a temporary entry `GET /api/v1/external/{root}/{id:guid}/messages` with seam `IsEntitledAsync` and ran the `RouteAuthorizationGuardTests`. `EveryHandlerDecisionIsVerified` failed with "IsEntitledAsync is a module-level entitlement and decides nothing about a record..." (Rule A also failed, on the same bogus entry). I removed the entry and the full ArchTests passed.

### Known limits (not fixed here)

- **K-a:** AC 2 is proven at the descriptor predicate (`ServiceRequestsModule.ScopeRows` / `AccessibleRecordIds`). A live dev submit plus a Service Requests tab read is the closing proof.
- **K-b:** a malformed body gives a bare 400 with no problem body (platform binding behaviour, as in task 034). Tasks 052/053 (the wizards) must not parse the error body of a 400 and should treat a bare 400 as "invalid submission".
- **K-c:** the route mixes `reasonCode` (403) and `errorCode` (400/500/503), the same as task 034. The wizards must read both.
- **K-d:** the test-host rate limiter is live (50 per minute per fixture key); a test that posts more than that in one fixture would see 429.
- **K-e:** duplicate keys in `answers` are stored verbatim in `sprk_intakedata` (the raw JSON text is written, not re-serialised).
- **K-f:** the 850-character title boundary is now pinned (`TitleOfExactly850Characters_Gets201`; 851 was already a 400).
- **K-g:** the 413 is unproven in the test host (fix 3).

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ExternalAccess|FullyQualifiedName~CallerPrincipal|FullyQualifiedName~WorkforcePrincipal"`: 2085 passed, 0 failed.
- `dotnet test tests/Spaarke.ArchTests/`: 941 passed, 0 failed.
- The full unit suite was not run (busy machine, per the brief).
