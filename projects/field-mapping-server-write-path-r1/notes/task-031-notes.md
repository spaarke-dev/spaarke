# Task 031 notes - typed routes (events, analysis) create through RecordCreationService

Branch `task/fm-031`. Spec FR-19, NFR-01, NFR-07, ADR-008 path A, ADR-028, ADR-010.

## What changed (production)

| File | Change |
|---|---|
| `Api/Events/EventEndpoints.cs` | `CreateEventAsync` creates through `RecordCreationService` as the caller (`RecordCreationPrincipal.ForCaller`). The owner, I-1 stamp, creator person, field mapping, search-index default and owner membership event are the service's. `OwnershipContextFor` and `CreateEventInDataverseAsync` are deleted. The audit-log row is still written by the route after a successful create. |
| `Api/Ai/AnalysisEndpoints.cs` | `CreateAnalysis` (now `internal static` so the seam tests can call it) creates through the service. `AssociateScopesAsync` runs only after a successful create. |

Route filters (`.RequireAuthorization()`, the record-route access filter, the analysis create filter) are untouched and the
`RouteAuthorizationGuardTests` ledger is unchanged (ADR-008 path A: payload-dependent checks live in `CallerWriteAuthorization`,
called by the service; the endpoints add no inline authorization).

## Refusal contract (NFR-07) - nothing changed

| Refusal | Status / code / words | Held by |
|---|---|---|
| No owner resolves (event) | 409, the route's `OwnerRefusalProblem` body and reason code | `EventEndpointsAuthorizationContractTests`, `SecureChildOwnershipEndpointTests`, `TypedRouteCreateInvariantsTests` |
| No owner resolves (analysis) | 409, `AnalysisOwnerRefusal` | `SecureChildOwnershipWriterTests`, `TypedRouteCreateInvariantsTests` |
| I-1 stamp underivable / unavailable (event) | 500 `events.regarding_stamp_failed` | `EventEndpointsAuthorizationContractTests` (CoreStampFailure) |
| Any other service refusal (event) | the route's generic 500 "An error occurred while creating the event" | `TypedRouteCreateInvariantsTests` |
| Any other service refusal (analysis) | the route's generic 500 "Failed to create analysis record." | `TypedRouteCreateInvariantsTests` |
| Filter denials (401/403/400) | unchanged, the service is not reached | `AssertServiceNotReached()` in both contract suites |

No assertion on a status, code or body had to change, so no NFR-07 escalation.

## AC3: "contract tests pass unchanged"

Read as: the ASSERTIONS are unchanged and the CAPTURE SEAM moved. The old suites captured the create at
`IEventDataverseService.CreateEventAsync` / `IAnalysisDataverseService.CreateAnalysisAsync`. The route no longer calls those, it
creates through the service, whose one create is the app-only `IFieldMappingDataverseService.UpdateRecordFieldsAsync` body. The hosts
now register `RecordCreationService` over `RouteRecordCreation.Over/Default` with a scripted caller Dataverse
(`SecureChildOwnershipAiToolTests.ScriptedUserClient`) and read the same facts (owner, stamp, anchor, person, status, body) from
that body. Added: both contract suites assert the service is never reached when a route filter denies
(`AssertServiceNotReached()`: no owner requests, no caller Dataverse call, no app-only create, no published event).

## Existing-test edits (each with its reason)

| File | Edit | Reason |
|---|---|---|
| `contract/Api/Events/EventEndpointsAuthorizationContractTests.cs` | Host registers `IDataverseUserClient` (counting wrapper over a scripted caller) and `RouteRecordCreation.Over(...)`; `CaptureCreates()` captures the app-only create body; `DataverseWebApiService.BuildCreateEventPayload(created)` became `created.Payload`; `RegardingEntitySetName` became a StartsWith check on the bind path; `RegardingRecordType == null` became "no `sprk_regarding*` key"; `InCallerCatalog` registers the catalog row with the scripted caller (the core's pair rule reads record types as the caller); `AssertServiceNotReached()` on the denied paths; CoreStampFailure asserts nothing created, no app-only create, no membership event | Capture seam moved (AC3). Intent of every assertion kept. |
| `contract/Api/Ai/AnalysisEndpointsAuthorizationContractTests.cs` | Host registers `IDataverseUserClient` and `RouteRecordCreation.Default`; `VerifyCreateWroteNothing` also asserts the service was not reached; 201 test asserts the document and playbook binds on the app-only create and that `CreateAnalysisAsync` is never called | Capture seam moved; the new assertion is the filter-denies-so-service-not-invoked check the owner asked for |
| `data-mutation/RecordOwnership/SecureChildOwnershipWriterTests.cs` | `AnalysisHost` registers the scripted caller and service; the three AnalysisCreate tests read owner / person / anchor from the app-only create body. The person is the scripted caller's `systemuser` | Capture seam moved. The service takes the creator person from WhoAmI on the caller's client, not from the route's resolver |
| `data-mutation/RecordOwnership/SecureChildOwnershipEndpointTests.cs` | `EventsHost` registers scripted caller and service; the three EventCreate tests read owner / person from the app-only create body; refusal test also asserts nothing created and no log row | Capture seam moved. Found red after the previous agent's list |
| `data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs` (shared harness) | `ScriptedUserClient` gains the `sprk_analysisplaybook` entity set and the lookups `sprk_analysis.{sprk_documentid,sprk_playbook}`, `sprk_event.{sprk_regardinganalysis,invoice,workassignment,budget,account,contact; sprk_eventtype_ref}` | The service validates and maps these binds as the caller |
| `unit/Api/Events/EventEndpointsMembershipPublishingTests.cs`, `EventOwnershipContextTests.cs` | Re-pointed to the service seam; one `RetrieveAsync` Verify narrowed to owner-column reads | The service reads `sprk_eventnumber` back after the create |
| `Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` | PersonBearingWriter rows `EventEndpoints.cs/OwnershipContextFor`, `EventEndpoints.cs/CreateEventInDataverseAsync`, `AnalysisEndpoints.cs/CreateAnalysis` removed; row `EventEndpoints.cs/WriteCreatedAuditLogAsync` added | The three removed methods no longer act for a person (the service does); the audit-log row still names the caller whose create it records |

New test files: `data-mutation/RecordOwnership/TypedRouteCreateInvariantsTests.cs` (9 tests: owner, I-1 stamp, creator
person, membership event once after the create, no-owner 409, caller-lacks-privilege fail closed, scope association only after a
successful create) and `seam/RecordCreation/RecordCreationServiceMappingSeamTests.TypedRoutes.cs` (6 tests, a partial of the
mapping seam harness: Matter to Event profile through the real route handler, fill-only, no source, no profile, and the analysis
route's document source).

## Failing-first evidence

1. `/c/tmp/fm031/failing-first.log` (previous agent): the new route tests did not compile against the old endpoints (4 errors, the
   endpoint signatures).
2. Behavioural, this round (`/c/tmp/fm031/failing-first-behavioural-old-endpoints.log`): the endpoints restored from `5f443eaae0`
   (the old create), the four tests that call the new handler signatures set aside, the re-pointed contract and secure-child
   suites run: **21 failed / 157 passed (178)** (the re-pointed hosts read the service's app-only create body, which the old
   routes never make: the regarding-number family, the stamp / AppendTo / restricted-in-body tests, the person / owner tests, the
   analysis anchor test). With the new endpoints restored: **0 failed**, 857 passed on the focused set below. The restore was
   `git checkout HEAD -- <the two endpoint files>`.

## Finding that needs an owner decision (not fixed, out of scope)

**FR-19 mapping on the analysis route cannot take a Document to Analysis profile today.** The service reads the mapping source as
the caller through `MappingSourceReadProbe.CheckCallerReadAsync`, which resolves the source's entity set with
`EntityAccessFilter.TryResolveEntitySet`. That map is the document-ASSOCIATION lockstep map (contact, matter, project, invoice,
work assignment, event, todo) and has no `sprk_document` entry, so a document source is `EntityTypeNotAuthorizable`: one warning,
nothing mapped, the document never read app-only. The route is still correct and fail closed, and the profile read does happen
(the source is inferred from the `sprk_DocumentId` bind). `TypedAnalysisRoute_AnchoredToADocument_AsksForTheProfile_...` pins that
outcome. Making Document to Analysis work needs `sprk_document` as an authorizable mapping source, which touches a lockstep map
(`AssociationTypeLockstepTests`) and is a design decision, not this task. Events filed under a Matter are mapped (proved).

## Other findings (not fixed)

- `DataverseWebApiService.CreateEventAsync` (the `IEventDataverseService` seam) has no caller on this route now: dead code. Census
  prose rows that mention it are stale. Follow-up to delete it.
- The broad `verify-suites.sh` Sprk.Bff.Api.Tests step hung once (testhost above 9 GB, no output) on the first run; see Verification.

## Placement justification (CLAUDE.md section 10)

No new endpoint, service, DI registration, package, Dataverse column or file in `src/`. Both routes stay where they are in
`Sprk.Bff.Api/Api/`; the work moves their create onto the existing `RecordCreationService`. Publish-size and CVE results below.

## Component justification (CLAUDE.md section 11)

Only new surface is the test partial `RecordCreationServiceMappingSeamTests.TypedRoutes.cs`. 1. Existing: the mapping seam
tests in `RecordCreationServiceMappingSeamTests`. 2. Extension: it IS an extension, a partial of that class, reusing its
harness (real profile reader, engine, probe, caller checks). 3. Cost of doing nothing: no test would show that these two routes
carry field mapping (AC1), and the only way to prove it is through the real route handlers over that harness.

## ADR-008 path A

Applied as the spec's ADR Tensions describes: route filters stay; the only new authorization runs in `CallerWriteAuthorization`
through the service. The RouteAuthorizationGuardTests ledger is unchanged (suite green below).

## Behaviour changes for release notes

1. Owner of an event / analysis row: decided by the service's record-first owner question (same owner the old route computed;
   the refusal is the same 409).
2. Field mapping is new on both routes: an event filed under a Matter (proved; any source the caller-read check authorizes)
   gets the profile's values, fill-only and read as the caller. An analysis requests it, but a document source maps nothing yet
   (finding above).
3. The I-1 core-ancestor stamp and the Access Permission come from the service on both routes.
4. A new sprk_event row gets the search-index default the service applies (task 039).
5. The owner membership event for a new event is published by the service (once, after the create).
6. The creator person (`sprk_CreatedByPerson`) is the WhoAmI of the caller's token; the event's audit-log row still uses
   `ICallerSystemUserResolver`.
7. Nothing else a caller can see: statuses, error codes and words of every refusal are the same.

## Verification (tip = the commit holding this file; code commit `1c75169f1b`)

| Check | Result |
|---|---|
| `dotnet build Spaarke.sln` | 0 Warning(s), 0 Error(s) |
| Focused: `EventEndpoints \| AnalysisEndpoints \| TypedRouteCreate \| TypedEventRoute \| TypedAnalysisRoute \| SecureChildOwnership \| WriterBuilderOwnershipParity \| RouteAuthorizationGuard` | 857 passed, 0 failed |
| The brief's filter `EventEndpoints \| AnalysisEndpoints \| TypedRouteCreate` | included in the focused set above (green) |
| `Spaarke.ArchTests` (whole project) | 1911 / 1911 passed (the first run failed `EveryAppCreateWriterThatActsForAPersonNamesThem`: census rows; fixed above) |
| `verify-suites.sh` Sprk.Bff.Api.Tests filter, run as 7 chunks of 6 filter terms (`/c/tmp/fm031/verify3/`) | 3669 + 1809 + 598 + 613 + 685 + 339 + 583 passed (a test can match several chunks), 7 skipped (Office contract, pre-existing), 0 failed |
| Publish size (`publish-delta.ps1`, master `0d4aa87ee0` vs `1c75169f1b`) | 41213252 -> 41213176 bytes, delta -76 bytes, 39.3 MB total, 192 files |
| `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api) | none |

Note on the broad run: run as ONE filter, `verify-suites.sh`'s Sprk.Bff.Api.Tests step printed nothing for more than 10 minutes
(the log is buffered; the `Office|SearchIndex|...` chunk alone takes 6 m 52 s and a testhost near 5 GB) and was killed twice, once
because two copies of my own script were running at once. It is slow, not hung: chunked, every chunk completes. (The chunk that
ended with exit 1 had its testhost killed by me after its `Passed!` line was printed: 0 failed.) Suggest raising the script's
patience or chunking it; not changed here.
