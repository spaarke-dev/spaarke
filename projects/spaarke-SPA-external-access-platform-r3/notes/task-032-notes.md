# Task 032 — Delete own records on the external plane (FR-26)

**Status (2026-10-11):** done for documents, to-dos and events, with code and tests. Wave sub-agent (Opus, rigor FULL). Base: `db91ced2b`.

**Service requests are escalated and not built.** POML escalation trigger 1 fired. See "Owner decision needed" below.

## Owner decision needed: service-request delete (escalation trigger 1)

Every relationship that hangs off `sprk_servicerequest` is `CascadeDelete=RemoveLink` (`src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_ServiceRequest.xml`):

- `sprk_analysis`, `sprk_communication`, `sprk_communicationthread`, `sprk_document` (`sprk_relatedservicerequest`), `sprk_event`, `sprk_memo`, `sprk_signal`, `sprk_todo`.

So deleting a request does not delete staff messages, staff documents, analyses or to-dos filed to it. Dataverse unlinks them and leaves them as orphans: a staff document whose only parent was the request stays in SPE and Dataverse with no parent. The POML says to stop and ask whether such a delete is refused or cascades. Options:

- **A. Refuse** when any related row exists that the caller did not create (or any related row at all). This is the smallest and safest option, but it needs one read per relationship (8 tables) before the delete.
- **B. Cascade in the BFF**: delete the caller's own related rows, and refuse if any staff-created row exists. This is A plus deletes, with an ordering and partial-failure design like the document one below.
- **C. Allow only while the request is still in its first status** (for example, "Submitted", before staff touch it), and refuse otherwise. This needs the status vocabulary decided.

**Recommendation: A**, refusing when any related row exists, with a 409 that names nothing. Once decided, the change is one `DeletableKind.ServiceRequest` row (stamp `sprk_requestedby`, no parent), one related-rows read and one route plus its ledger row.

## What changed

- **`ExternalProjectDataEndpoints`**: three routes in the `/api/v1/external` group, which inherit the ExternalCollaboration policy and `CallerPrincipalAuthorizationFilter`:
  - `DELETE /documents/{id:guid}` → `DeleteOwnDocument`
  - `DELETE /todos/{id:guid}` → `DeleteOwnTodo`
  - `DELETE /events/{id:guid}` → `DeleteOwnEvent`
- **`AuthorizeDeleteOwnAsync` is THE one check.** Every handler calls it before anything is deleted. It requires both halves:
  1. **The stamp.** `ExternalDataService.IsCreatedByCaller` runs over the row's two task-030 stamps (owner decision 2026-10-11, option (b) widened):
     - a contact principal matches `sprk_createdbycontact` = its contact;
     - a systemuser principal matches `sprk_createdbyperson` = its systemuser, OR `sprk_createdbycontact` = its server-resolved linked contact (`CallerPrincipal.ContactId`, never request input).
     - An unstamped row is nobody's. No access level (FullAccess included) can stand in for the stamp.
  2. **Read on every parent this plane authorizes**, through the existing `RightsForDocumentParentAsync`. For a project, matter or work assignment that is `RightsForRoot`; for a service request it is the requester scope. The parents consulted are the lookups `ExternalDataService` models (`Deletables`): a document's `sprk_project`, `sprk_matter`, `sprk_workassignment`, `sprk_relatedservicerequest`; a to-do's or event's `sprk_regardingproject`, `sprk_regardingmatter`, `sprk_regardingworkassignment`. Other lookups on the row (for example `sprk_invoice`, `sprk_relatedagreement`, `sprk_relatedcontact`, `sprk_parentdocument`, `sprk_email`) are not consulted. A row filed to none of the modelled parents is refused.
  - **No existence oracle.** Absent, not-own, unstamped, no-parent and no-Read all return the same 403 (`reasonCode` `sdap.access.deny.delete_own`, same detail).
  - **Faults are not denials.** A target read that failed returns 503 `sdap.access.delete_own_unavailable`; a failed service-request requester read returns the existing 503 `sdap.access.service_request_unreadable`. Neither returns 403, and nothing is deleted.
- **`ExternalDataService`**:
  - `DeletableKind` (Document, Todo, Event) and the `Deletables` binding table. Documents use the four task-031 `DocumentParents` lookups; to-dos and events use the three `RootBindings` lookups.
  - `GetDeleteTargetAsync` (virtual): an app-only single-row read of both stamps and every parent lookup. It goes through `GetSingleOrThrowAsync`: 404 → null; any other failure → `DataverseUnavailableException`.
  - Pure helpers: `BuildDeleteTargetUrl`, `ParseDeleteTarget`, `IsCreatedByCaller`.
  - `DeleteRecordAsync` (virtual): an app-only DELETE for a to-do or event. 404 → false; other failures → `DataverseUnavailableException`. It refuses `Document`.
  - Review 031 K2: the document, to-do and event list `$select`s, the row types and the mappers now carry `_sprk_createdbyperson_value` and `_sprk_createdbycontact_value` (`CreatorStampSelect`).
- **DTOs**: `ExternalDocumentDto`, `ExternalTodoDto` and `ExternalEventDto` gain `_sprk_createdbyperson_value` and `_sprk_createdbycontact_value`.
- **`ExternalAccessModule`**: the documents module `ReadableColumns` gains `sprk_createdbyperson` and `sprk_createdbycontact` (review 031 K2).
- **`DocumentCheckoutService`** (the internal delete, reused rather than forked):
  - The new overload `DeleteAsync(id, correlationId, DocumentFileDeletePolicy, ct)` shares one body (`DeleteCoreAsync`) with the existing 3-argument `DeleteAsync`. The 3-argument method is unchanged in behaviour (`BestEffort`) and still `virtual`, so the internal `DELETE /api/documents/{id}` and its test override are untouched.
  - New `FileNotDeletedDeleteResult(Code, FailureStatusCode, CorrelationId)` and `FileNotDeletedCode = "document_file_not_deleted"`.
- **Docs**:
  - `docs/data-model/sprk_event-related-tables.md` gains the `sprk_createdbycontact` row. Task 020 added the column in source; `EventReadPathTests` requires every selected event column to be documented there.
  - `docs/data-model/sprk_createdbycontact.md`: the caller table now states the (b)-widened systemuser rule.
- **ArchTests**:
  - `RouteAuthorizationGuardTests.Ledger.cs` gains three `HandlerDecision`s. Each has seam `RightsForRoot` and hops `AuthorizeDeleteOwnAsync` → `RightsForDocumentParentAsync`.
  - `SpeWriteSinkContainerProvenanceGuardTests.cs` gains `DocumentCheckoutService.cs DeleteFileAsync #2`, ServerDerivedRecord (ADR-003, ADR-007).

## Delete ordering and partial failure (documents)

`DocumentFileDeletePolicy.VerifiedFileFirstKeepRowOnFailure`:

1. The external check (stamp + Read on every modelled parent). Nothing has been touched before this.
2. `DocumentCheckoutService` reads the row. A checked-out document returns 409 `reasonCode` `document_locked`; the lock holder's name is not shown on this plane. Nothing is deleted.
3. The pointer check: `RecordContainerResolver.EnsureDocumentPointerContainerAsync`, the check every app-only download runs. A refusal returns 409 with the check's code and keeps the row; the file is never touched.
4. The SPE file is deleted app-only through `SpeFileStore.DeleteFileAsync` → `SpeContainerOwnershipGuard`. "Already gone" (false) counts as deleted. Any other failure returns 503 `document_file_not_deleted` and keeps the row.
5. The Dataverse row is deleted. A failure here returns 500 `reasonCode` `document_row_not_deleted`.
6. A row that disappeared between steps (Dataverse NotFound) returns 204, because that is the end state the caller asked for.

**Why file first:** a kept row can always be retried or cleaned by staff. The reverse order (row first) would leave an unreachable SPE file that nothing points at, which is invisible and so worse.

**Known limit:** if step 5 fails after step 4, the row points at a deleted file. A retry by the caller was refused by the pointer check (409 `document_storage_unverified`, because the item no longer exists). Staff can delete the row in the model-driven app. The 500 response tells the caller the record could not be removed. **Resolved 2026-10-22 (R3 ISS-004 review F1, #1650):** the delete's pointer check (`RecordContainerResolver.VerifyDocumentPointerForDeleteAsync`) now reports an item that is gone as already deleted when the row's `sprk_graphitemidbound` equals the pointer, the drive is an owned container and Graph answers 404; the retry then deletes the row without touching a file. A mismatched or unbound pointer or an unowned container is still 409; a fault in the check is 503.

**Internal route behaviour is unchanged:** `DELETE /api/documents/{id}` still swallows SPE failures and deletes the row (`BestEffort`), as test `InternalDelete_BestEffort_IsUnchanged_AFileFaultStillDeletesTheRow` records. **Observation, not changed:** that route can leave an orphaned SPE file. Recommend filing it for the owner of the internal plane. **Superseded 2026-10-22 (R3 ISS-004, #1650, owner decision):** the internal route now uses `VerifiedFileFirstKeepRowOnFailure` like the external one (503 keeps the row), and `InternalDelete_BestEffort_IsUnchanged_...` was replaced.

## §11 component justification (new surface)

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `DELETE /api/v1/external/documents/{id}` | `DELETE /api/documents/{documentId}` (internal, `DocumentOperationsEndpoints.cs:80`, `AddDocumentAuthorizationFilter("write")`) | The internal route authorizes by the caller's own Dataverse rights, which a contact has none of; the external principal and creator rule are a different check. The delete body itself IS reused. | FR-26 fails: an external creator cannot remove a document it uploaded by mistake. |
| `DELETE /api/v1/external/todos/{id}` | `PATCH /api/v1/external/todos/{id}` (update only) | A PATCH cannot express a hard delete; adding a "deleted" flag is a soft delete the spec rules out. | FR-26 fails for to-dos. |
| `DELETE /api/v1/external/events/{id}` | None: the internal `/api/v1/events` group (`EventEndpoints.cs:96`) maps no DELETE | Nothing to extend; the external plane needs the app-only caller-principal check anyway. | FR-26 fails for events. |
| `DocumentCheckoutService.DeleteAsync(…, DocumentFileDeletePolicy, …)` overload + `FileNotDeletedDeleteResult` | The 3-argument `DeleteAsync` | It IS the extension: one body, one new policy argument. The old behaviour is kept for the internal route. | AC 8 fails: a failed SPE delete would delete the row anyway. |
| `ExternalDataService.GetDeleteTargetAsync` | `GetTodoRootAsync`, `GetDocumentParentAndNameAsync` | Those read one lookup and no stamp; widening them would change three callers' contracts for one consumer. | The check has no stamp and no full parent set to decide on. |
| `ExternalDataService.DeleteRecordAsync` | None on `ExternalDataService` (it only creates and updates) | It follows the same app-only HTTP shape as `UpdateTodoAsync`. | To-dos and events cannot be deleted on this plane. |
| DTO stamp fields, list `$select`s, documents module columns | — | Pure modification of existing reads (review 031 K2). | The SPA cannot tell which rows to offer Delete on (task 054). |

**Placement:** BFF, `Api/ExternalAccess` + `Infrastructure/ExternalAccess`, next to the routes and data service of the same plane. There is no new DI registration, package, secret or OBO; `DocumentCheckoutService`, `ExternalDataService` and `ExternalModuleRegistry` are already registered.

## Deviations and known limits

1. **The check lives in the endpoint file** (`AuthorizeDeleteOwnAsync`), and the pure stamp rule lives in `ExternalDataService.IsCreatedByCaller`. This puts the one check next to `RightsForDocumentParentAsync`, the existing parent-rights helper it composes, and lets the route ledger verify it.
2. **Service requests are not built** (escalation above).
3. **AC 6 under (b):** a systemuser CAN delete a contact-stamped row when that contact is its own server-resolved linked contact. This is what the owner's decision says. A foreign contact stamp is refused (tested).
4. **Documents module columns are not grid-derived.** `ExternalModuleColumnAllowListTests` pins the two stamps as a deliberate task 032 addition, with a comment.
5. **Until task 020 is imported**, every list `$select` that names `_sprk_createdbycontact_value` returns a Dataverse 400 (the list is flagged truncated), and every delete returns 503 because the target read fails closed. Deploy order: 020 before this BFF build (task 038).
6. **A to-do or event filed only to a service request** (`sprk_regardingservicerequest`) has no parent this plane authorizes it through, so it gets 403 (fail closed). To-do and event routes scope by project, matter or work assignment only (`RootBindings`).
7. **A document filed to two modelled parents needs Read on both** (tested). A document also filed by staff to a modelled record the caller cannot read is not the caller's to destroy. The check covers only the lookups this plane models (see the list under the check above); a document also filed to an unmodelled lookup (`sprk_invoice`, `sprk_relatedagreement`, `sprk_relatedcontact`, `sprk_parentdocument`, `sprk_email`) is judged on its modelled parents alone.
8. **Hard delete.** There is no recycle bin; the POML states hard delete.

## Tests

New file: `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDeleteOwnTests.cs`, 69 cases. The tests go through the REAL routes and handlers, with doubles only at module boundaries:

- the principal;
- `ExternalDataService`'s delete seams (in memory);
- Dataverse's `sprk_documents` endpoint: a hand-written `HttpMessageHandler` under the REAL `DocumentCheckoutService` (ADR-038 B1);
- `SpeFileStore.DeleteFileAsync`.

The pointer check is the REAL `RecordContainerResolver` (`TestRecordContainerResolver.ForBusinessUnitContainers`).

| AC | Evidence |
|---|---|
| 1 | `Creator_Deletes_204_AndTheRowIsGone` (×3 kinds), `Creator_OnAMatter_AndAWorkAssignment_Deletes`, `Creator_WithViewOnly_StillDeletes_ReadOnTheParentIsTheRule` |
| 2 | `Document_DeletesTheSpeFile_ThenTheRow` (step order recorded) |
| 3 | `OtherUser_WithFullAccessOnTheParent_Is403_AndNothingIsDeleted` (×3) |
| 4 | `Creator_WhoLostReadOnTheParent_Is403` (×3), `Document_FiledToTwoParents_NeedsReadOnBoth`, `Row_FiledToNoParent_Is403` |
| 5 | `UnstampedRow_Is403` (×3) |
| 6 | `ContactCaller_CannotDeleteAPersonStampedRow` (×3), `SystemUser_CannotDeleteARowStampedWithSomeoneElsesContact` (×3), `SystemUser_DeletesARowStampedWithItsOwnLinkedContact` (×3), `SystemUser_DeletesItsOwnPersonStampedRow_ButNotAnotherPersons` (×3), `IsCreatedByCaller_FollowsThePrincipalKind` (11 cases) |
| 7 | `TheExternalPlane_MapsExactlyThreeDeleteRoutes` (EndpointDataSource), `NoDeleteRoute_ForMessagesOrServiceRequests` (messages, communications, servicerequests) |
| 8 | `Document_WhenTheSpeDeleteFails_KeepsTheRow_And503`, `Document_WhosePointerIsRefused_KeepsTheRow_And409_AndTheFileIsNotTouched`, `Document_WhoseFileIsAlreadyGone_IsDeleted`, `Document_WhenTheRowDeleteFailsAfterTheFile_Is500_NotASuccess`, `Document_CheckedOut_Is409_WithoutTheLockHoldersName_AndNothingIsDeleted` |
| oracle / faults | `AbsentRow_AnswersExactlyAsSomeoneElsesRow` (×3), `TargetReadFault_Is503_AndNothingIsDeleted` (×3), `RequesterReadFault_OnAServiceRequestParent_Is503_AndNothingIsDeleted`, `RowDeleteFault_Is503` (×2) |
| seams | `DeleteTargetUrl_SelectsBothStampsAndEveryParentLookup` (×3), `DeleteTargetRead_ParsesStampsAndParents_404IsNull_AndAFailureThrows` (400 until 020 → throws), `DeleteRecord_SendsOneDelete_404IsFalse_AndAFailureThrows` (×2), `DeleteRecord_RefusesADocument_WhichMustGoThroughTheFileDelete` |
| K2 | `ListSelects_CarryBothCreatorStamps`, `Dtos_SerializeBothCreatorStamps`, plus the updated pins below |

**Pinned tests updated** (deliberate contract changes):

- `ExternalRecordDocumentRoutesTests.ProjectListUrl_IsUnchanged`: the select gains the two stamps.
- `ExternalModuleColumnAllowListTests`: `DocumentColumns` and `LiveDerivedAllowLists["sprk_document"]` gain the two stamps, with a comment.
- `EventReadPathTests` (unchanged): it passes once the data-model row is added.

**Test scope (AC 11):** one new class through the real HTTP pipeline, because the decision IS the route (the filter, the principal, the check, the delete ordering). There is no live test: the POML forbids live calls, and the target column is not imported (020). There is no new integration-project test, because the integration project compiles the same `tests/integration/**` sources.

### Fail-before proofs

- **Seeding proof (AC 9).** I added `if (DateTime.UtcNow.Year > 0) return true;` at the top of `IsCreatedByCaller`, which accepts any stamp. Result: **26 of 69 failed**, including all three `OtherUser_WithFullAccessOnTheParent_Is403_AndNothingIsDeleted` cases (documents, todos, events), the contact/person kind cases, unstamped, absent-vs-not-own and the stamp table. Restored from a backup copy; 69/69 pass.
- **Ordering proof.** I switched the external route to `DocumentFileDeletePolicy.BestEffort`. Result: **2 failed**, `Document_WhenTheSpeDeleteFails_KeepsTheRow_And503` and `Document_WhosePointerIsRefused_KeepsTheRow_And409_AndTheFileIsNotTouched`. Restored.
- **Pre-change state.** I moved the three `MapDelete` templates off their paths, which is the state before this task. Result: **44 of 69 failed**. The 25 that still pass are the pure and seam tests and the "no route" tests. Restored.

## Verification (2026-10-11)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/`: 0 warnings, 0 errors.
- `dotnet test tests/Spaarke.ArchTests/`: 940 passed, 0 failed.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~External|FullyQualifiedName~Document|FullyQualifiedName~Delete"`: 4511 passed, 0 failed, 9 skipped (4520 total). Before the pin updates: 3 failed (the three pins above).
- **Publish size.** Fresh Release `dotnet publish` of base `db91ced2b` (exported with `git archive`) and of this tree, zipped with `Compress-Archive`:
  - base 38,243,240 B; this tree 38,259,742 B; **delta +16,502 B (+0.016 MB)**; total 36.49 MB, which is under 55 MB.
  - Unzipped: +45,432 B. `Sprk.Bff.Api.dll`: +31,232 B.
- **CVE:** no package, csproj or `Directory.Packages.props` change, so there is no new HIGH CVE.

## Review fix round (2026-10-11)

**Verdict.** The adversarial review approved the change with no F-class findings. This round fixes its nits and one coordinator request.

**Fixed.**

1. **Overclaim in docs.** The endpoint doc/remarks, the route comment, the ArchTest route-ledger reason text and deviation 7 above said "every parent the row is filed to". They now say Read on every parent this plane authorizes (the modelled lookups), and that other lookups on the row are not consulted. The check's behaviour is unchanged. The ledger reasons also cite method names plus the file:line the guard requires.
2. **OCE filter.** The document-read catch in `DeleteOwnDocument` is now `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)` (the `DocumentCheckoutService` pattern). An HttpClient timeout (a `TaskCanceledException` while `ct` is not cancelled) now gives the 503 "nothing was deleted" instead of a bare 500. Test `Document_WhenTheRowReadTimesOut_Is503_AndNothingIsDeleted`: failed before the fix (500, 1 failed of 70), passes after.
3. **`default:` arm.** `case FailedDeleteResult:` now carries the "file was deleted but the record could not be removed" 500. `default:` is a generic 500 (`document_delete_failed`) with a structured error log naming the result type, and makes no claim about the file.
4. **Nits.** The success log no longer says "and its file deleted" (it also fires for `NotFoundDeleteResult`). The `SpeWriteSinkContainerProvenanceGuardTests` provenance table cites the methods (`DeleteAsync`, `DeleteFileOrRefuseAsync`) instead of line numbers that drift.
5. **One problem key (coordinator request).** The delete routes used `reasonCode` (403, 503) and `code` (409, 500, file 503). All problem bodies on the three delete routes now use `reasonCode`. Why `reasonCode` and not `errorCode`: the 403 denial, the delete 503 and the service-request 503 from the shared `RightsForDocumentParentAsync` already use `reasonCode` (the plane's access-denial key), so it is the only single key that needs no change to the shared helper or to the 403 convention the SPA already reads. The message routes' `errorCode` (task 034) is not used here. The older `code` key stays on the upload/download routes (`SdapProblemException`), which this task does not touch. `ProblemCode` in `ExternalDeleteOwnTests` now reads `reasonCode` and asserts `code` and `errorCode` are absent; every code assertion goes through it. If task 054 prefers `errorCode` everywhere, it is a rename of six literals in `ExternalProjectDataEndpoints.cs` plus the 403 and shared 503.

**Known limits left as they were.**

- TOCTOU window: the check and the delete are separate calls; a parent lost between them is not re-checked.
- An unlinked-systemuser outage gives 403 instead of 503: pre-existing resolver behaviour, not changed here.
- No retry after a half-done document delete (file gone, row kept): only a staff delete clears it. **Resolved 2026-10-22 (R3 ISS-004 review F1):** see the Known limit under step 5.
- `DocumentDestroyAuthorizationTestFixture` overrides only the 3-arg `DeleteAsync`.

**Verification.** `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet test tests/Spaarke.ArchTests/`: 940 passed. Filtered Sprk.Bff.Api.Tests (`ExternalAccess|DocumentCheckout|DocumentOperations|EventReadPath`): 2121 passed.

## Verification after the merge (main session, 2026-10-11)

Merge `19b24414e` on `work/spaarke-SPA-external-access-platform-r3`.
- BFF build and unit-test build: 0 warnings, 0 errors.
- ArchTests: 940 passed.
- Full unit suite: 19,795 passed, 54 skipped, 7 failed. All 7 failed the same way: `TaskCanceledException` from the in-process test host's HttpClient (about 100 s each). The run took 47 min against the usual 26, because two other agents were building and testing on the machine.
- None of the 7 is in task-032 code: chat streaming, compose seam, Office to-do, Issue975, grant policy, MI route retirement, document identity.
- Re-run in isolation, all 7 passed (12 cases, 12 s). Recorded as load timeouts, not defects.
