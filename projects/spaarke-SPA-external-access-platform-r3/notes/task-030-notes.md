# Task 030 — Creator stamping on every external create (FR-25)

**Status (2026-10-10):** the code and unit tests are done, run as a wave sub-agent (Opus, effort high, rigor FULL). The live behaviour waits for the `sprk_createdbycontact` import (task 020's owner-gated step) and the deploy (task 038).

## What changed
- **`ExternalDataService.StampCreator(body, CallerPrincipal)`** (internal static) is the one stamping method. It sets exactly one of:
  - `sprk_CreatedByPerson@odata.bind = /systemusers({id})` when the principal carries `SystemUserId`, written through the existing `RecordCreatorPersonColumn.BindIfKnown`;
  - `sprk_CreatedByContact@odata.bind = /contacts({id})` for a contact principal (a workforce contact or a CIAM partner). The navigation property comes from task 020's `Other/Relationships/Contact.xml`.
- **The kind decides, not the plane.** A systemuser that also carries a linked contact is a systemuser. This is the existing `CallerPrincipal.IsContactPrincipal` rule (task 140/141), so the escalation trigger "both ids" does not fire: the codebase already defines it.
- **It fails closed.** A principal with neither kind, or with an empty systemuser id, throws before anything is posted. The decision is made before the payload is changed.
- **Client values never land.** Before stamping, the method removes any payload key that names `sprk_createdbyperson`, `sprk_createdbycontact`, `sprk_requestedby`, `createdby` or `createdonbehalfby`, in any casing, plus their `@…` and `_x_value` forms (`NamesCreatorColumn`).
  - The first line of defence is binding. `CreateExternalTodoRequest` and `CreateExternalEventRequest` are closed DTOs, so creator fields in a body are dropped there.
  - The upload body is the file only.
  - The strip keeps this true if a builder ever passes fields through.
- **The seams take the creator.** `CreateTodoAsync`, `CreateEventAsync` and `CreateDocumentAsync` gained a required `CallerPrincipal creator` parameter. Each calls `StampCreator` on the finished payload, just before the POST. The three routes in `ExternalProjectDataEndpoints` pass the resolved `CallerPrincipal`. No handler branches on plane, and there is no OBO: the creates stay app-only.
- **`sprk_requestedby` is stripped, not stamped.** Task 035 (the service-request intake) adds the requester stamp by extending this same method.

## Deviation: four test files outside the POML's file list
Changing the three virtual seam signatures breaks compilation of code that overrides or calls them. The unit test project links `tests/integration/**`, so the suite does not build without these edits. They are compile-only and mechanical:
- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipExternalTests.cs`: three overrides gain the parameter.
- `tests/integration/auth/UnifiedAccessControl/ExternalTodoScopeTests.cs`: one override gains the parameter.
- `tests/integration/regression/Issue963_ExternalDataPagingTests.cs`: four `CreateTodoAsync` calls pass a contact `Creator`.
- `tests/integration/Sprk.Bff.Api.IntegrationTests/Events/EventRoutesLiveTests.cs`: three live (opt-in) creates pass the operator as a systemuser creator. The live stamp is then `sprk_createdbyperson`, which needs no new column.

**Alternatives rejected:**
- An optional creator parameter (`null` = no stamp) would allow an unstamped create (AC 4).
- A new overload would bypass the test overrides, so those tests would hit real HTTP.
- A server-only property on the client request DTO would mix identity into client input.

## Tests (`tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalDataServiceTests.cs`)
- `StampCreator_WritesExactlyOneStamp_ForThePrincipalKind`: four cases (systemuser, systemuser with linked contact, workforce contact, CIAM contact). Covers AC 1 and 2 at the method level.
- `StampCreator_RemovesEveryClientSuppliedCreator_AndWritesTheCallers`: two cases. Covers AC 3.
- `StampCreator_PrincipalOfNeitherKind_ThrowsAndLeavesThePayloadUntouched` and `StampCreator_EmptySystemUserId_Throws`: the fail-closed edges.
- `NamesCreatorColumn_MatchesCreatorColumnsOnly`: 15 cases, with must-fire and must-not-fire keys.
- `EveryExternalCreatePath_PostsTheCallersStamp`: nine cases (to-do, event, document × systemuser, CIAM contact, workforce contact). It drives the REAL seam through a capturing `HttpMessageHandler` and asserts on the POSTed JSON. Covers AC 1, 2 and 4.
- `ARequestBodyNamingAnotherCreator_IsStoredWithTheCallersStamp`: four cases (to-do and event × two kinds). A JSON body naming another creator in every form is bound with web JSON defaults and posted by the real seam. Covers AC 3.
- `EveryExternalCreatePath_WithAnUnattributablePrincipal_PostsNothing`: three cases. Covers AC 4: no unstamped row.

**Test scope beyond the criteria:**
- The `SystemUserWithLinkedContact` case pins the escalation-trigger rule.
- `NamesCreatorColumn` has its own cases because the strip is a guard, so it needs must-not-fire controls.

**Fail-before (mutation; the old file cannot compile against the new seam signatures):**
- Removing the three seam `StampCreator` calls: 16 of 46 tests fail.
- Disabling the strip loop: the 2 strip tests fail.

The real file was restored after each run.

## Acceptance criteria that need the live column (deferred to task 038)
- AC 2, and AC 3 and 4 for contact callers, need `sprk_createdbycontact` imported into the target environment. Until then, a contact-principal create answers Dataverse 400 (unknown navigation property).
  - **Deploy order: import task 020's schema BEFORE deploying this BFF build.**
  - For an upload, the 400 comes after the bytes are in SPE, which leaves an orphan file. That is the route's documented recoverable direction.
- AC 1 relies on `sprk_createdbyperson` on `sprk_todo`, `sprk_event` and `sprk_document` (task 146 c1-r1, `Set-ChildRecordCreatorPersonSchema.ps1`). I did not verify it live here (no live reads in this wave).

## §11 component justification (new surface: one internal method, one internal helper, two constants, one parameter on three existing seams)
1. **Existing:** `RecordCreatorPersonColumn.BindIfKnown` (the person bind, reused), `RecordCreatorPerson.NamesColumn` (person-only key detection) and `RecordOwnerResolution.StampCreatorOn` (systemuser only, from `RequestedBy`).
2. **Extension:** those helpers serve the internal plane and know only systemusers. The contact stamp exists only on the external plane, and the POML and ADR-010 place it on `ExternalDataService`. The person half reuses `BindIfKnown` rather than copying it.
3. **Cost of doing nothing:** SPA creates carry no author (auth record L-5), and FR-26 delete-own (task 032) has no stamp to compare, so no external user could delete a record they created in error.

No new service, DI registration, endpoint, package or column.

## BFF hygiene (§10)
- **Placement:** `.claude/constraints/bff-extensions.md`. This is CRUD on the existing external write path, in the existing `ExternalDataService`. It is request-scoped, with no AI, no background work and no package.
- **Publish size** (2026-10-10): `dotnet publish -c Release`, zipped with `Compress-Archive`, both from fresh short-path worktrees.

  | Side | Commit | Worktree | Files | Zip size |
  |---|---|---|---|---|
  | Master | `61c9e80a1` (`origin/master`) | `C:\wt030m` | 192 | 38,214,648 B (36.44 MB) |
  | This task | `433e21d00` | `C:\wt030c` | 192 | 38,217,094 B (36.45 MB) |

  The delta is **+2,446 B**. The file counts match, and the total is under 55 MB. Both temp worktrees were removed afterwards.
- **CVE:** no package change, so no new CVE.

## Found while doing the task (not fixed here)
- `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs:1852` lists `ExternalDataService.cs` under `PersonLessWriters` with the reason "the person is a CONTACT, and sprk_createdbyperson names a systemuser". After this task that reason is stale: the file now records the person.
  - The entry should move to `PersonBearingWriters` with member `StampCreator`. `StampCreator` contains `RecordCreatorPersonColumn`, which the census `PersonToken` already matches.
  - The ArchTests file is outside this task's file list. The test still passes as written, because it checks only that the file exists.

## Review results and fixes (2026-10-10)

Two adversarial review passes found no F-class defects. The following known-limit items were fixed in a follow-up commit.

1. **Upload ordering.** `UploadDocument` now refuses an unattributable principal (403 ProblemDetails) before the SPE write, so the fail-closed stamp can no longer leave an orphaned file. The rule is shared: `ExternalDataService.CanStampCreator(CallerPrincipal)` (also used by `EnsureCanStampCreator` / `StampCreator`).
   - Test: `SecureChildOwnershipExternalTests.ExternalDocumentUpload_ByAnUnattributablePrincipal_Is403_AndNeverReachesSpe_AndCreatesNoRow` (failed before: 201, bytes uploaded; passes after). Predicate: `ExternalDataServiceTests.CanStampCreator_*`.
2. **To-do seam ordering.** `CreateTodoAsync` calls `EnsureCanStampCreator` first, before the token, root-name lookup and record-type lookup (event and document already stamped before the token).
   - Test: `EveryExternalCreatePath_WithAnUnattributablePrincipal_PostsNothing` now also asserts no HTTP request at all (To-do case failed before with `{GET, GET}`; passes after).
3. **Route to seam principal handoff.** The recording fixtures keep the creator the seam was handed and assert `BeSameAs` the resolved principal: `ExternalTodoScopeTests` (`LastCreateCreator`) and, for to-do, event and document, `SecureChildOwnershipExternalTests` (`TodoCreators` / `EventCreators` / `DocumentCreators`).
4. **DTO closure vs strip.** `ARequestBodyNamingAnotherCreator_IsStoredWithTheCallersStamp` renamed `ARequestBodyNamingAnotherCreator_IsDroppedByTheClosedDto_AndTheCallersStampIsStored` (it pins DTO closure). New `ACreatorKeyInjectedIntoTheBuiltPayload_IsStrippedByStampCreator_AndTheCallersStampWins` takes the real builder output for each path, injects creator keys, stamps it and asserts the caller's stamp wins.
5. **Live read-back.** `EventRoutesLiveTests` (opt-in) reads each external create back and asserts `_sprk_createdbyperson_value == operatorId` (event, event with legacy date, to-do). Compiles; not run live.
6. **Census.** `ExternalDataService.cs` moved from `PersonLessWriters` to `PersonBearingWriters` as `StampCreator` ("the resolved external principal"). `PersonToken` matches `RecordCreatorPersonColumn`.

Known limits not fixed:
- Deploy order: task 020's `sprk_createdbycontact` import must land before task 038 deploys this BFF. A Dataverse 4xx on the create surfaces as a generic 500; consider a ProblemDetails mapping later.
- Delete-own identity drift (FR-26): owner decision recorded in the task 032 POML.
