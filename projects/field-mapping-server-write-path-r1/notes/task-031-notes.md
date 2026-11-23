# Task 031 notes - typed routes (events, analysis) create through RecordCreationService

Branch `task/fm-031`. Spec FR-19, NFR-01, NFR-07, ADR-008 path A, ADR-028, ADR-010.

## What changed (production)

| File | Change |
|---|---|
| `Api/Events/EventEndpoints.cs` | `CreateEventAsync` creates through `RecordCreationService` as the caller (`RecordCreationPrincipal.ForCaller`). The owner, I-1 stamp, creator person, field mapping and owner membership event are the service's (step 8, the search index, is a recorded no-op since task 041). `OwnershipContextFor` and `CreateEventInDataverseAsync` are deleted. The audit-log row is still written by the route after a successful create. |
| `Api/Ai/AnalysisEndpoints.cs` | `CreateAnalysis` (now `internal static` so the seam tests can call it) creates through the service. `AssociateScopesAsync` runs only after a successful create. |

Route filters (`.RequireAuthorization()`, the record-route access filter, the analysis create filter) are untouched and the
`RouteAuthorizationGuardTests` ledger is unchanged (ADR-008 path A: payload-dependent checks live in `CallerWriteAuthorization`,
called by the service; the endpoints add no inline authorization).

## Refusal contract (NFR-07) - nothing changed

| Refusal | Status / code / words | Held by |
|---|---|---|
| No owner resolves (event) | 409, the route's `OwnerRefusalProblem` body and reason code | `EventEndpointsAuthorizationContractTests`, `SecureChildOwnershipEndpointTests`, `TypedRouteCreateInvariantsTests` |
| No owner resolves (analysis) | 409, `AnalysisOwnerRefusal` | `SecureChildOwnershipWriterTests`, `TypedRouteCreateInvariantsTests` |
| I-1 stamp underivable / unavailable / unbindable (event) | 500 `events.regarding_stamp_failed` | `EventEndpointsAuthorizationContractTests` (CoreStampFailure); unbindable: `RecordCreationServicePipelineSeamTests.EventRoute_WhenTheTablesMetadataCannotBindTheDerivedStamp_KeepsTheRoutes500RegardingStampFailed` (fix round 1, F3-low) |
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

- `DataverseWebApiService.CreateEventAsync` (the `IEventDataverseService` seam) had no caller after this task: deleted in fix
  round 1 (finding 5 below), with its census prose.
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
4. No search-index value is written on either route: step 8 is a recorded no-op since task 041 (FR-40), so no default is applied.
   (Corrected in fix round 1; the first version of this note said a default was written.)
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

## Fix round 1 (adversarial review, 2026-11-04)

Commits on `task/fm-031`: `5ab498dbc1` (findings 1 and 2), `7f0739b48e` (finding 3), then the dead-code commit (finding 5) and
the commit holding this section (finding 4).

### Finding 1 (F1, HIGH): analysis create with a playbook was a 500 for ordinary users

Cause: the service's step 5 asked AppendTo on `sprk_playbook` (a caller-supplied lookup); only Spaarke AI Analysis Admin holds
`prvAppendTosprk_analysisplaybook` (Core, Basic and AI Analysis User: Read, Global). The route filter already makes the
playbook-use decision (public, or Read on the row); the old route never asked AppendTo.

Fix (owner decision 2026-11-04, "exempt in code"):

- `RecordCreationRequest.RouteAuthorizedLookups` (column to the decided record id, `IReadOnlyDictionary<string, Guid>?`, default
  null; columns normalised like `ServerSetLookupColumns`; first shipped as `RouteAuthorizedLookupColumns`, a set of columns:
  see "UAC-r2 approval change" below). Step 5 (`RecordCreationService.cs`, the lookup check only) asks each such lookup's Read as the
  caller (RetrievePrincipalAccess: missing, unreadable or unanswered is the uniform not-found an AppendTo failure gives) and
  hands the AppendTo checks a skip set of the server-set columns plus exactly those columns. Only a lookup whose column AND
  target table are on the list, and whose record in the merged row is the caller's own (fill-only), is skipped.
- The closed list and the validation live in a new partial, `Services/RecordCreation/RecordCreationService.RouteAuthorized.cs`,
  to keep the edit in `RecordCreationService.cs` small for the 042c merge (the request property, one validation call next to
  `ValidateReferenceLookupColumns`, and step 5's two `CheckCallerMayCreateAsync` arguments; the core-pair code is untouched).
  List: `(sprk_analysis, sprk_playbook, sprk_analysisplaybook)`. `ArgumentException`, before anything is read: a system
  principal; a filing lookup (`IsFilingLookup`: lineage, FilingParentOf, a root's filing column); a column not on the list; a
  listed target that is an ownership parent; a column also server-set or an optional reference.
- `ServerSetLookupColumns` is not reused: the request property is untouched; only the AppendTo-skip argument handed to
  `CallerWriteAuthorization.CheckCallerMayCreateAsync` (whose only use of it is that skip) is the union.
- `AnalysisAuthorizationFilter` (AnalysisCreate): the decision is unchanged. It records the decision for the request's own
  playbook id in `HttpContext.Items` under a private object key, inside the `next` delegate the evaluator calls only after
  every check passed. `AnalysisEndpoints.CreateAnalysis` sets `{ "sprk_playbook" }` only when that record matches its
  `PlaybookId`. RouteAuthorizationGuardTests ledger unchanged.
- **Events: no exemption.** `POST /api/v1/events` makes no route-level decision on the event type (its filters are the shape
  filter and the record-route access filter on the regarding record), so the event type keeps its AppendTo: review K1.
  Shipped roles: Core User `prvAppendTosprk_EventType_Ref` Global, Office Add In User Deep, **Basic User none** (Read Deep
  only). A Basic-only user creating an event with an event type is refused (generic 500). Pinned by
  `TypedEventRoute_ABasicUser_WithoutAppendToOnTheEventType_IsRefused_KnownLimitK1`; the fix belongs to the owner's
  roles-alignment task.

Component justification (CLAUDE.md section 11) for `RouteAuthorizedLookups`:
1. Existing: `ServerSetLookupColumns` (skips AppendTo for lookups that name the caller), `ReferenceLookupColumns` (drops an
   unusable optional reference), and the filter's playbook-use decision.
2. Extension: neither option can be extended. Server-set means "names the caller", is never mapped and is guarded as never
   filing; a reference that fails is dropped, not authorised. A playbook is the caller's own choice of a record the route has
   already authorised, which needs its own, narrower meaning.
3. Cost of doing nothing: every user but AI Analysis Admin gets a 500 from `POST /api/ai/analysis/create` with a playbook
   (the review's F1).

### UAC-r2 conditions (binding, 2026-11-04)

| Condition | How it is met | Evidence (tests) |
|---|---|---|
| (a) Route code sets it; never bound from body, query or headers | Property only on the internal `RecordCreationRequest`; no DTO carries it; the handler sets it from the filter's record | `Create_BodyNamingRouteAuthorizedLookups_IsIgnored` (contract: a body with the old and new names, both casings; the document's AppendTo is still asked; 500, nothing created); `RouteAuthorizedLookupsCallSiteTests.NoBoundTypeCarriesTheOption` |
| (b) Closed allow-list of (route, table, column) in code; ArchTest ratcheted at the call sites | Service list `RecordCreationService.RouteAuthorizableLookups`; IL scan of the init accessor | `RouteAuthorizedLookupsCallSiteTests.OnlyTheClosedListSetsRouteAuthorizedLookups` (only `AnalysisEndpoints.CreateAnalysis`; a stale entry fails too), `TheServicesClosedListIsTheAnalysisPlaybookOnly`, `Detector_MustFireAndMustNotFire` |
| (c) For THIS caller and THIS record id; the filter refusal is uniform; nothing distinguishes "exists" from "not authorised" | The filter records the id it authorised, only after every check passed; only the create filter may record it (IL scan). This route's uniform filter refusal is its existing 403 `insufficient_rights` "Access denied" (kept under NFR-07, not turned into a 404); a missing and a not-shared playbook give byte-identical bodies. A Read failure in the service is the uniform not-found, rendered as the route's generic 500 for missing and unreadable alike | `OnlyTheCreateFilterRecordsThePlaybookUseDecision`, `TypedAnalysisRoute_TheFilterAuthorisedAnotherPlaybook_ThisOnesAppendToIsAsked_AndIsRefused`, `Create_PrivateUnsharedAndNonexistentPlaybook_AreTheSameRefusal_ServiceNotReached`, `Create_PrivatePlaybookSharedWithTheCaller_Is201`, `TypedAnalysisRoute_AnAuthorisedPlaybookTheCallerCannotRead_IsTheSameRefusal_NothingCreated` (missing, unreadable) |
| (d) Skip only AppendTo; keep existence and Read | Read asked as the caller (RetrievePrincipalAccess) before the skip; bind spelling, table check and mapping unchanged | `TypedAnalysisRoute_AnOrdinaryUser_ReadButNoAppendToOnThePlaybook_..._201` (asserts the Read question), `..._CallerCannotRead_...` |
| (e) A filing lookup or an ownership-parent table throws ArgumentException, a test per table class | Validation before anything is read | `RouteAuthorisedLookups_AnythingButTheListedPlaybook_IsAnArgumentError` (lineage `sprk_todo.sprk_regardingmatter`; ownership-parent filing `sprk_event.sprk_regardingevent`; root filing `sprk_workassignment.sprk_regardingmatter`; off-list `sprk_event.sprk_eventtype_ref`, `sprk_analysis.sprk_actionid`), `RouteAuthorisedLookups_AListedLookupToAnOwnershipParentTable_IsAnArgumentError`, `RouteAuthorisedLookups_TheListedPlaybook_IsAccepted_ButNeverForASystemPrincipal_NorAlsoServerSet`, `RouteAuthorisedLookups_TheServiceThrowsBeforeAskingAnything` |
| (f) Must-not-fire | The same column without the option keeps AppendTo; another column with the option is still checked | `TypedAnalysisRoute_ThePlaybookWithoutTheFiltersRecord_KeepsItsAppendTo_AndIsRefused`, `TypedAnalysisRoute_WithThePlaybookAuthorised_AnotherLookupIsStillAskedItsAppendTo` |
| (g) Ordinary user with a public playbook gets 201 (failing first); a private playbook not shared gets the uniform refusal | See failing-first below | `Create_OrdinaryUser_PublicPlaybook_NoAppendToOnIt_Is201`, `TypedAnalysisRoute_AnOrdinaryUser_..._201`, the existing `Create_WithTheG5Rights_Is201AndAnchorsTheDocument`; `Create_PrivateUnsharedAndNonexistentPlaybook_...`, `Create_UnusablePlaybook_Is403` |

### Finding 2 (F3, HIGH): the test doubles hid permission gaps

`SecureChildOwnershipAiToolTests.ScriptedUserClient` granted every right on every record. It now has `TableRights` (per entity
set), defaulting to the shipped role grants for the configuration targets these routes send: `sprk_analysisplaybooks` = Read
only. `sprk_eventtype_ref` (entity set added) and `sprk_recordtype_ref` keep AppendTo because the fixture's caller is a Core
User (Core holds both, Global). The Basic-User case is an explicit negative test (K1 above) and the Core case an explicit
positive one (`TypedEventRoute_ACoreUser_CreatesWithAnEventType`).

### Finding 3 (F1, low): the stamp bind failure lost `events.regarding_stamp_failed`

`EventEndpoints.RefusalResult` now takes the outcome's step records. It maps `Undecided` / `record_creation.no_decision` to the
500 `events.regarding_stamp_failed` only when the last step record is `StampAndInheritance` / `Refused`: the service's existing
step marker, so no new service signal. The same code at any other step stays the generic 500 without `errorCode`. Tests:
`RecordCreationServicePipelineSeamTests.EventRoute_WhenTheTablesMetadataCannotBindTheDerivedStamp_KeepsTheRoutes500RegardingStampFailed`
(a real service refusal rendered by the route adapter) and
`EventRoute_TheGenericNoDecisionRefusalAtAnyOtherStep_StaysTheRoutesGeneric500WithoutAnErrorCode` (must not fire: CallerChecks,
Owner).

### Finding 4: behaviour change 4 corrected

Step 8 is a recorded no-op since task 041, so no search-index value is written on either route. Corrected above (the "What
changed" row and behaviour change 4).

### Finding 5: dead code

Deleted: `IEventDataverseService.CreateEventAsync`, `DataverseWebApiService.CreateEventAsync`, the
`DataverseServiceClientImpl.CreateEventAsync` stub, the test fake's implementation with its never-populated `Creates` list (two
`Creates.Should().BeEmpty()` assertions that could no longer fail; `AppOnlyCreates` is asserted next to each), and one
`Verify(CreateEventAsync, Times.Never)`. Census prose updated (`RecordOwnerAssignmentCensusTests`: the sprk_event seam entry,
the EventEndpoints and AnalysisEndpoints unscanned-writer rows, the builder rows' reasons; the `Models.cs` OwningTeamId doc).

**Not deleted, and why:** `DataverseWebApiService.BuildCreateEventPayload` (with `AddRegardingWriteSet`) and the
`CreateEventRequest` fields it reads. grep finds five BFF unit-test files that call the builder and pin payload shapes
(`EventRegardingPayloadTests`, `EventDateOnlyTests`, `EventStatusWritePathTests`, `EventReadPathTests`,
`EventEndpointsMembershipPublishingTests`), so the brief's condition ("no callers, including tests") does not hold. It has no
production caller now. Follow-up: re-point those tests at `EventEndpoints.BuildCreatePayload`, then delete the builder, its
helper and the fields. `ExternalDataService.CreateEventAsync` is untouched: external-access-r3's inventory exception.

### Failing-first evidence (fix round 1)

Seeded on the finished code (`/c/tmp/fm031/fix1/failing-first-seeded.log`): the analysis handler's exemption removed
(`RouteAuthorizedLookupColumns = null`) and the event adapter's new stamp arm removed, then the affected classes run:
**5 failed / 369 passed**. The failures: `TypedAnalysisRoute_AnOrdinaryUser_..._201`,
`Create_OrdinaryUser_PublicPlaybook_NoAppendToOnIt_Is201`, `Create_PrivatePlaybookSharedWithTheCaller_Is201`, the existing
`Create_WithTheG5Rights_Is201AndAnchorsTheDocument` (it now runs as an ordinary user), and
`EventRoute_WhenTheTablesMetadataCannotBindTheDerivedStamp_...`. Both files were restored and the set is green again.

### Other findings (reported, not fixed)

- Pre-existing (present at `5f443eaae0`): `AnalysisEndpoints.CreateAnalysis` reads `request.SkillIds/KnowledgeIds/ToolIds.Length`
  with no null check, outside the handler's try. A body that omits them throws `NullReferenceException`: an unhandled 500, not
  the route's problem body. The new contract tests send empty arrays.

### Verification (fix round 1, tip `9c3df450c2` plus this notes commit)

| Check | Result |
|---|---|
| `dotnet build` (BFF + test projects) | 0 warnings, 0 errors |
| Focused classes (Event/Analysis endpoints, typed routes, SecureChildOwnership, parity, guard, event payload classes) | 987 / 0 failed |
| `verify-suites.sh` filter, run in 7 chunks (same terms) | 4318 + 1726 (7 skipped: pre-existing Office contract skips) + 243 + 919 + 248 + 737 + 134 = 8325 passed, 0 failed. Chunks overlap where terms match the same class, so the sum is not a unique-test count |
| ArchTests (full) | 1916 / 1916 (1911 + 5 in `RouteAuthorizedLookupColumnsCallSiteTests`) |
| Failing first (seeded) | 5 failed / 369 passed, then restored and green (above) |
| Publish delta (`publish-delta.ps1`, master `0d4aa87ee0` vs `9c3df450c2`) | 41213283 to 41214255 bytes: +972 bytes; 39.3 MB, 192 files |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |

Chunk 2 (`Office|SearchIndex|...`) took 33 m 35 s this time, against about 7 minutes before, because another worktree's test hosts were using about 11 GB each on the same machine. It was slow, not hung.

### Fix round 1 follow-ups (coordinator, 2026-11-04)

- **Omitted scope ids (the existing NRE above): fixed** in `63af06492d`. `CreateAnalysis` treats a null `skillIds`, `knowledgeIds` or `toolIds` as empty, as the filter already did. Test: `Create_BodyOmittingSkillKnowledgeAndToolIds_Is201_WithNoScopeAssociation`, which failed first with the NullReferenceException; now 201 with no association call.
- **Uniform 403 (UAC-r2 accepted it, 2026-11-04):** `Create_PrivateUnsharedAndNonexistentPlaybook_AreTheSameRefusal_ServiceNotReached` now compares the status, every header the route controls (all but Date and correlation/trace ids; it asserts `content-type: application/problem+json` is among them) and the normalised body.
- **Inactive playbook: not treated as unusable by the filter (reported, filter not changed).** `PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync` decides on `IsPublic` from `PlaybookService.GetPlaybookAsync`, whose `$select` has no `statecode`; `PlaybookResponse` carries no state. So an inactive public playbook passes the filter, and an inactive private one goes to the caller's Read on its row, as an active one does. A deleted playbook is a 404 from that read (`null`), which is the existing "nonexistent" case. The filter's unusable states are therefore exactly "not public and no Read" and "not found", and the test covers both. No inactive case was added.
- `BuildCreateEventPayload` moves to task 063 (dead code).

### UAC-r2 approval change (2026-11-04): (column, id), not the column alone

UAC-r2 approved 031 on one condition: the option carries the record id the route decided, and the service exempts only that
record.

- `RecordCreationRequest.RouteAuthorizedLookupColumns` (a set of columns) became `RouteAuthorizedLookups`, an
  `IReadOnlyDictionary<string, Guid>?` of column to the decided record id. Columns are normalised as before. One column named
  with two different ids is an `ArgumentException` (`RecordCreationService.NormaliseRouteAuthorizedLookups`), and so is an
  empty id (`ValidateRouteAuthorizedLookups`). The service's closed list was renamed `RouteAuthorizableLookups` to keep the
  two names apart; its content is unchanged.
- Step 5 (`RecordCreationService.RouteAuthorized.cs:140`) exempts a caller lookup only when its record equals the decided id,
  in addition to every earlier condition: the closed list, the target table, the same record in the merged row, and the
  caller's Read. Any other record in that column keeps its AppendTo question.
- `AnalysisAuthorizationFilter.IsPlaybookUseAuthorized(ctx, id)` became `AuthorizedPlaybookId(ctx)`, which returns the id the
  filter recorded. `AnalysisEndpoints.CreateAnalysis` (`AnalysisEndpoints.cs:210`) passes `{ sprk_playbook: <that id> }`, the
  filter's id and not the body's, so the id comparison happens in the service.
- The ArchTest class was renamed `RouteAuthorizedLookupsCallSiteTests` and now ratchets `set_RouteAuthorizedLookups`. The
  allow-list is unchanged (`AnalysisEndpoints.CreateAnalysis` only).
- Must-not-fire: `TypedAnalysisRoute_TheFilterAuthorisedAnotherPlaybook_ThisOnesAppendToIsAsked_AndIsRefused`. The option
  carries A, the payload carries B, and B is readable. B's rights are asked, nothing is asked of A, and the create is refused
  (500, nothing created). Failing first: with the service's id comparison removed (`ContainsKey` only), this test is the one
  failure (1 failed / 18 passed, `/c/tmp/fm031/fix1/ab-seeded.log`); restored.
- `TypedRouteCreateInvariantsTests`' validation test also covers an empty id ("no record") and two ids for one column ("two
  different records").
- `Create_BodyNamingRouteAuthorizedLookups_IsIgnored` (renamed) now sends the old and the new property names, in both
  casings, including a map that names the document and the playbook. The document's AppendTo is still asked and refused.
- UAC-r2 conditions (a) to (g) above hold under the new names: read `RouteAuthorizedLookupColumns` as `RouteAuthorizedLookups`
  and `RouteAuthorizedLookupColumnsCallSiteTests` as `RouteAuthorizedLookupsCallSiteTests`. The failing-first and
  verification rows above are historical and keep the old names.

### Event route: the linked contact is the caller's, resolved on the server (UAC-r2 question, 2026-11-04)

Confirmed: the event create's server-set contact comes from the caller's identity, never from the body.

- **The column:** `sprk_assignedto` (`EventEndpoints.AssignedToColumn`, `EventEndpoints.cs:899`). It is the only contact
  lookup the create writes, and the only server-set lookup on the route.
- **Where its value comes from:**
  - `EventEndpoints.cs:744` calls `ResolveActingUserContactAsync(callerResolver, identity, httpContext, ...)`, defined at
    `EventEndpoints.cs:1025`.
  - That resolves the caller from `httpContext.User` (`:1034`).
  - It then reads `identity.ResolveAsync(systemUserId).ContactId` (`:1039`): the caller's LINKED contact, never an email match.
  - If the caller has no link, the column is left blank.
- **Where it is written:** `BuildCreatePayload` writes it at `EventEndpoints.cs:936`, and only from that argument.
  `ServerSetLookupColumns` names the column only when that value exists (`EventEndpoints.cs:760`).
- **What the body can carry:** the bound DTO, `Dtos/CreateEventRequest.cs:20-31`, has no contact field: subject, description,
  event type, regarding id/name/type, the dates and priority. Unknown JSON properties are dropped at binding.
- **Test:** `EventEndpointsAuthorizationContractTests.Create_ABodyNamingAContact_IsIgnored_TheRowNamesTheCallersLinkedContact`
  (new). The body carries `assigneeContactId`, `assignedToContactId`, `sprk_assignedto` and `sprk_AssignedTo@odata.bind` naming
  contact X. The row's `sprk_AssignedTo@odata.bind` is the caller's linked contact, and X appears nowhere in the create body.
  `TypedEventRoute_CreatesThroughTheService_...` already pins the linked contact.

**A contact supplied in the body is still asked for AppendTo.**

- On create, a body cannot supply a contact at all (above).
- If field mapping fills `sprk_assignedto` (a caller with no linked contact, so no server-set column), that value goes through
  step 5's merged-payload AppendTo check like any mapped lookup.
- When the caller does have a linked contact, the column is already filled and mapping is fill-only. A mapped copy of the
  server-set value into another column becomes server-set too (`RecordCreationService.cs:524-533`), because it names the same
  caller contact.
- The one route where a body names a contact is `PATCH /api/v1/events/{id}/due-assignee`. There `AssigneeContactId` is bound
  (`EventDueAssigneeWrite.cs:107`) and the PATCH runs AS THE CALLER (`user.PatchAsync`, `EventDueAssigneeWrite.cs:162`), so
  Dataverse asks the caller's AppendTo on that contact itself. Nothing is skipped, and task 031 does not touch that route.

Verification (UAC-r2 change):

- Build: BFF, Sprk.Bff.Api.Tests and ArchTests, each with 0 warnings and 0 errors.
- Classes: the Analysis and Events contract and filter classes, `TypedRouteCreateInvariantsTests` (the typed-route, route-authorised and secure-child classes) and the pipeline seam tests: 984 passed, 0 failed.
- `RouteAuthorizedLookupsCallSiteTests`: 5/5.
- ArchTests (full): 1916/1916.
