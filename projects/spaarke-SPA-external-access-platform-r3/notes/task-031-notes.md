# Task 031 — Document routes on every core record (FR-24)

**Status (2026-10-10):** done for project, matter, work assignment and service request, with code and unit tests. It ran as a wave sub-agent (Opus, effort high, rigor FULL).

**Invoice is escalated and not built.** POML escalation trigger 2 fired. See "Owner decision needed" below.

**Update (2026-10-12):** the owner decided both open items on 2026-10-11, and both are now built. See "Owner decisions 2026-10-11" at the end. That section supersedes the invoice escalation, the SR-container known limit and the two OWNER DECISION items below.

## Owner decision needed: invoice (escalation trigger 2)

Invoices are not roots in the caller's accessible set. `CallerPrincipal` carries rights maps for projects, matters and work assignments only (`ProjectAccess`, `MatterAccess`, `WorkAssignmentAccess`), and `ExternalGrantRootType` has those three values. An invoice is a child record. The `invoices` module descriptor makes one visible when its `sprk_matter` OR `sprk_project` is in the caller's Read set (`ExternalAccessModule.cs:396-417`). So there is no Tier-2 answer for an invoice id, and in particular no Create answer for an upload.

Options:
- **A. Derive from the invoice's parent.** Read the invoice's `sprk_matter` / `sprk_project` app-only, then use the caller's rights on that parent: Read lists, downloads and reads versions; Create uploads. This mirrors the invoices module. It needs a rule for an invoice with BOTH parents: either the union of the two parents' rights, or the matter's rights only. A "rights from the parent" rule also has to be stated for FR-26 delete-own.
- **B. Make invoices a grant root.** Add an invoice rights map in the evaluator and the grant model (UAC-r2 territory: evaluator, grant rows, `ExternalGrantRootType`). This is larger, and it is not this project's to decide.
- **C. Defer invoice documents** to a later release, and record it in `spec.md` FR-24.

**Recommendation: A**, with the union of both parents' rights. That is the same "visible through ANY accessible parent" rule the invoices module and the documents module already apply. Once decided, the change is small:
- one row in `ExternalDataService.DocumentParents` (`sprk_invoice`, `sprk_invoices`, navigation property `sprk_Invoice`, lookup `sprk_invoice`, all confirmed in `sprk_Document/Entity.xml:3450` and `Other/Relationships/sprk_Invoice.xml`);
- one arm in `RightsForDocumentParentAsync`;
- four routes, four adapters and four ledger rows.

## What changed

- **`ExternalDataService`**
  - New `DocumentParentKind` (Project, Matter, WorkAssignment, ServiceRequest) and the `DocumentParentBinding` table: entity, entity set, navigation property and lookup. The columns were checked against the solution source:
    - Project: `sprk_project` / `sprk_Project` (`Entity.xml:4466`).
    - Matter: `sprk_matter` / `sprk_Matter` (`:4095`).
    - Work assignment: `sprk_workassignment` / `sprk_WorkAssignment` (`:5606`).
    - Service request: `sprk_relatedservicerequest` / `sprk_RelatedServiceRequest` (`:4888`, `Other/Relationships/sprk_ServiceRequest.xml:108-141`).
  - The three document seams take the parent kind:
    - `GetDocumentsAsync(kind, id)`, with a pure `BuildDocumentListUrl`. The project URL is byte-identical to before (pinned by a test).
    - `GetDocumentParentAndNameAsync(kind, documentId)` replaces `GetDocumentProjectAndNameAsync`. It reads only that kind's lookup.
    - `CreateDocumentAsync(kind, id, …)` and `BuildDocumentCreatePayload(kind, id, …)` bind `{NavigationProperty}@odata.bind = /{EntitySet}(id)`.
  - New `GetServiceRequestRequesterAsync(id)`: an app-only read of `_sprk_requestedby_value`.
- **`ExternalProjectDataEndpoints`**
  - There is one implementation each of list, download, versions and upload (`ListDocumentsForParent`, `DownloadDocumentForParent`, `ListDocumentVersionsForParent`, `UploadDocumentForParent`), and thin per-route adapters. This is the task 029 to-do shape.
  - The project handlers keep their names, route names, templates, statuses and every ProblemDetails detail string. The noun is "project" for the project kind.
  - `RightsForDocumentParentAsync`:
    - project, matter and work assignment use `RightsForRoot`, the same rights the to-do routes use;
    - a service request uses `RightsForServiceRequestAsync`: the registered `service-requests` module (`FindByEntity("sprk_servicerequest")`) and `IsRecordAccessible(caller, requester)`. The result is Read|Create for the requester only, and None otherwise. A missing request, someone else's request and a missing descriptor all deny the same way, so there is no existence oracle.
  - Twelve new literal routes are mapped in the `/api/v1/external` group, next to the project routes: `/{matters|workassignments|servicerequests}/{id}/documents` (GET list, POST upload), `…/documents/{documentId}/content` (GET) and `…/documents/{documentId}/versions` (GET). They inherit the ExternalCollaboration policy and `CallerPrincipalAuthorizationFilter`. They are not mapped through a helper, so the route-authorization ArchTest reads each template.
- **Upload ordering** is unchanged for every kind:
  1. Read, then Create (403 before anything else);
  2. the task-030 `CanStampCreator` refusal (403 before SPE);
  3. the non-empty file check;
  4. the owner, resolved from the parent entity (`IRecordOwnershipResolver`);
  5. the container, resolved from the parent entity (`RecordContainerResolver`, never from the request; 422 when unusable);
  6. the app-only SPE write through the `ISpeFileOperations` facade with `ConflictBehavior.Fail`;
  7. the row create, which stamps the creator through `StampCreator` (task 030).
- **No OBO.** Every Dataverse read and write is the existing app-only `ExternalDataService` path. SPE is the existing app-only facade (`SpeFileStore` → `SpeContainerOwnershipGuard`). There is no new secret, package or DI registration.

## Decisions and deviations

- **Service requests have no ViewOnly level.** The POML (step 3) gives the requester Read and Create, so AC 2 ("a ViewOnly caller's upload → 403") applies to the three roots. For service requests the denial cases are AC 5: non-requester, partner with the switch off, and a missing request.
- **Scoping denial stays 403.** It is not 404, matching the existing project contract (POML "404/403"). Not-in-record and nonexistent look the same.
- **Download and versions scope through the route type's own lookup.** A document linked to matter M is not downloadable through `/projects/M/…`, even when the ids coincide.
- **Five test files outside the POML file list** changed, for compile reasons only. The document seam signatures changed, and these files override or call them:
  - `ExternalAccessContractTests.cs` (stub overrides; the recorded read string is unchanged);
  - `SecureChildOwnershipExternalTests.cs` (override and builder call);
  - `ExternalDataServiceTests.cs` (two calls);
  - `Issue963_ExternalDataPagingTests.cs` (one call).

  `RouteAuthorizationGuardTests.Ledger.cs` changed too:
  - the three project document GET entries now name seam `RightsForRoot` through the hop `ExternalProjectDataEndpoints.RightsForDocumentParentAsync`;
  - the project POST entry keeps seam `AccessRights`, with its line updated;
  - twelve entries were added: `RightsForRoot` or `AccessRights` for matter and work assignment, and `IsRecordAccessible` for service request.

  `ExternalProjectDocumentUploadContractTests.cs` (listed as "modify") needed no change. It passes unchanged, which is AC 6.
- **Not done here (process steps outside this agent's brief):** the `TASK-INDEX.md` / POML status update (step 7), `/conflict-check` (run before the PR), and the live checks.

## §11 component justification (new surface)

The new surface is twelve route registrations and sixteen thin adapters, four generalized handlers, `RightsForDocumentParentAsync`, `RightsForServiceRequestAsync`, `DocumentParentKind`, `DocumentParentBinding`, `BuildDocumentListUrl` and `GetServiceRequestRequesterAsync`.

1. **Existing:**
   - the project document routes and handlers (`ExternalProjectDataEndpoints`);
   - `TodoRootKind` / `TodoRootBinding`, whose to-do lookups are `sprk_regarding*` and so cannot serve `sprk_document`'s `sprk_project` / `sprk_matter` / `sprk_workassignment` / `sprk_relatedservicerequest`;
   - the `service-requests` module descriptor, which holds the submitter scope;
   - `ExternalModuleDescriptor.IsRecordAccessible`.
2. **Extension:** the project handlers were extended over a parent-kind map instead of being copied per type. Root rights reuse `RightsForRoot`, and service-request rights reuse the registered module's `IsRecordAccessible`. A separate binding table is needed because `sprk_document`'s lookups and navigation properties differ from the to-do regarding columns (`TodoRootBinding` carries `sprk_regarding*`). Adding service request to `TodoRootKind` would also change the to-do PATCH and root-classification behaviour.
3. **Cost of doing nothing:** external users cannot list, download or upload documents on matters, work assignments or service requests in the SPA. This is FR-24, an owner requirement of 2026-10-10.

## Tests

### New file: `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalRecordDocumentRoutesTests.cs`

34 tests, all through the real routes and handlers. Doubles sit at module boundaries only. The `RecordContainerResolver` is real, and the stored rows are the real builder's payloads.

| Test | Cases | Covers |
|---|---|---|
| `Upload_ThenDownload_ReturnsTheSameBytes_AndListsTheDocument` | ×4 | AC 1. Also checks the Location header, the parent bind, owner resolution on the parent entity, and that the list returns the document. |
| `Upload_StampsTheResolvedCallerAsCreator` | ×4 | AC 7. The stamp is `sprk_CreatedByContact`, and the creator is the same object as the resolved principal. |
| `Upload_ByAViewOnlyCaller_Is403_AndWritesNothing` | ×3 roots | AC 2. Includes a positive list control. |
| `RecordOutsideTheCallersSet_Is403_OnEveryDocumentRoute_AndTouchesNoDocumentOrSpe` | ×4 | AC 3: list, download, versions and upload. No document read and no SPE call. |
| `DocumentOfAnotherRecord_IsNotDownloadable_ThroughThisRecordsRoute` | ×4 | AC 4: content and versions, with a positive control. |
| `DocumentLinkedToAMatter_IsNotDownloadable_ThroughAProjectRouteWithAnotherLookup` | 1 | AC 4 across types. |
| `ServiceRequest_OfAnotherRequester_Is403_OnListAndUpload` | 1 | AC 5. |
| `ServiceRequest_ACiamPartnerRequester_WithThePartnerSwitchOff_Is403` | 1 | AC 5 plus FR-06. |
| `ServiceRequest_ThatDoesNotExist_DeniesExactlyLikeAnotherRequestersOne` | 1 | No existence oracle. |
| `ParentBinding_FiltersAndBindsThroughThatTypesOwnLookup` | ×4 | The list filter and the create bind use each type's own lookup. |
| `DocumentParentRead_ReturnsTheParentThroughThatTypesOwnLookup` | ×4 | The real scoping seam over a transport that returns only the `$select` columns. |
| `DocumentParentRead_OfADocumentNotLinkedThroughThatLookup_HasNoParent` | 1 | The real scoping seam, negative case. |
| `ServiceRequestRequesterRead_ReturnsTheRequester_AndNullForAMissingRequest` | 1 | The real requester seam. |
| `ProjectListUrl_IsUnchanged` | 1 | AC 6 at the query level. |

**Test scope beyond the criteria:**
- The partner-switch case is there because the rights reuse that module's set.
- The no-oracle case is a project rule.
- The real-seam reads are there because the route tests stub those seams, and a scoping mutation was invisible without them.

### Fail-before evidence

The old files cannot compile against the new seam signatures, so I used mutations. After each run I restored the real files, checked them byte-identical with `cmp`, and rebuilt.

| Mutation | Failed / total |
|---|---|
| The 12 new routes unmapped (the pre-change routing surface) | 17 / 28 (the only tests passing were the project cases and the pure tests) |
| Scoping always reads the project lookup (the pre-change read) | 4 / 34 |
| Service-request rights granted without the requester check | 4 / 34 |
| Upload's Create gate disabled | 3 / 34 (ViewOnly, all three roots) |
| The create payload always binds the project (the pre-change payload) | 10 / 34 |

### Command results (2026-10-10)

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/` | 0 errors, 0 warnings |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~External"` | 2213 / 2213 pass. Includes the unchanged project document contract tests, `SecureChildOwnershipExternalTests` and `Issue963`. |
| `dotnet test tests/Spaarke.ArchTests/` | 940 / 940 pass |
| Full `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | 19512 pass, 54 skipped, 1 failed. The failure is `ChatMessageStreamingContractTests.SendMessage_ModelThrowsAfterTwoTokens_EmitsTypingEndThenSafeErrorAndNothingAfter` (AI chat streaming, untouched by this task). It took 2 m 25 s under full-suite load and passes on its own (6 / 6): a timing flake. |

## BFF hygiene (§10)

- **Placement** (`.claude/constraints/bff-extensions.md`): CRUD on the existing external data plane, in the existing `ExternalProjectDataEndpoints` and `ExternalDataService`. It is request-scoped, with no AI, no background work, no new DI registration and no package.
- **Publish size** (2026-10-10): `dotnet publish -c Release`, zipped with `Compress-Archive`. Both trees were extracted fresh with `git archive` under `C:\tmp`.

  | Side | Tree | Files | Zip size |
  |---|---|---|---|
  | Master | `origin/master` `43b4eae92` | 192 | 38,210,513 B (36.44 MB) |
  | This task | branch `2ea2600a7` plus the task-031 source files | 192 | 38,219,734 B (36.45 MB) |

  - The delta is **+9,221 B**. It includes the rest of the R3 branch since master, not only this task. `Sprk.Bff.Api.dll` grew by 25,088 B.
  - The file counts match. The total is under 55 MB.
  - The temporary trees and zips were removed afterwards.
- **CVE:** no package change, so no new CVE.

## Known limits

- **Live checks are deferred to task 038.** Live checks need the deploy:
  - The SR container decision: `RecordContainerResolver` has no `sprk_servicerequest` row in `ChildAncestorLinks` and the entity is not securable, so an SR document ALWAYS goes to the owning business unit's shared container (the `owningbusinessunit` fallback), whatever the request regards. No ancestor-link read happens. The live check is that the owning business unit has `sprk_containerid` stamped; without it the resolver returns no container and the upload is refused.
  - Owner resolution for `sprk_servicerequest`: it is in the resolver's table (`RecordOwnershipResolver.cs:756`).
  - The contact stamp needs task 020's `sprk_createdbycontact` import first. This is the task-030 deploy order.
- **Only the primary lookups count.** A document linked to a parent only through the secondary "related" lookups (`sprk_relatedproject`, `sprk_relatedmatter`, `sprk_RelatedWorkAssignment`) is not listed or downloadable through these routes. Only the primary lookups named in the POML are used.
- **Partner switch-on is not tested here.** With `PartnerServiceRequestsEnabled` on, a CIAM requester qualifies through the module's own predicate. That predicate is already tested by the module's switch tests (task 011).

## Review results and fixes (2026-10-11)

Two adversarial passes found no F-class defects. Four items were applied as fixes (test: `ExternalRecordDocumentRoutesTests`):

1. **Test realism (pass 1 K3).** The container-resolver double answered "securable, secure, own container" for every entity, so the SR upload ran through the secure branch. Production takes the business-unit fallback. The double now answers `NotSecurable` for `sprk_servicerequest`, returns a row with `owningbusinessunit` for it, and a `businessunit` row with `sprk_containerid`. `Upload_ThenDownload_ReturnsTheSameBytes_AndListsTheDocument` now asserts the container of the SPE upload: the business unit's for `servicerequests`, the record's own for the other three. Fail-before: with the registry double reverted to "securable for all", the `servicerequests` case fails (1 failure). `RecordContainerResolver` is unchanged.
2. **SR requester read faults (pass 1 K3).** `GetServiceRequestRequesterAsync` relied on `GetSingleAsync`, which turns a 404, any non-success and any exception into `default`, so a throttle or outage became 403 "no access". It now uses a new `GetSingleOrThrowAsync`: a genuine 404 is `null` (uniform 403, no oracle); any other failure throws `ExternalDataService.DataverseUnavailableException`, and the routes return 503 `sdap.access.service_request_unreadable` (the route handler `RightsForDocumentParentAsync` catches it). `GetSingleAsync` is unchanged for its other callers. Tests: `ServiceRequestRequesterRead_OnANon404Failure_ThrowsRatherThanReportingNoRequest` (500, 429, 503, 401), `ServiceRequestRequesterRead_OnATransportFault_Throws_AndOnA404ReturnsNull` (timeout, connection reset, 404 to null), `ServiceRequest_ARequesterRead_ThatFails_Is503_NotA403_AndTouchesNothing` (list, download, versions and upload all 503; no SPE call, no document read, no write; recovers when the fault clears), and the existing `ServiceRequest_ThatDoesNotExist_DeniesExactlyLikeAnotherRequestersOne` (404 is 403 identical to a foreign request). Fail-before: with the service reverted to `GetSingleAsync` and the catch disabled, 6 of these fail (the 4 theory cases, the transport/404 fact and the 503 route test).
3. **Short-circuit (pass 1 K4).** `RightsForServiceRequestAsync` now denies before the requester read when the Service Requests module's accessible set is empty for the caller (a CIAM partner with `PartnerServiceRequestsEnabled` off, or a principal with no contact). Test: `ServiceRequest_ACiamPartnerRequester_WithThePartnerSwitchOff_Is403` now asserts `RequesterReads == 0`. Fail-before: with the check disabled it fails (1 read).
4. **Notes.** The Known limits entry on the SR container decision said SR uploads read the request's ancestor links first. That was wrong and is corrected above.

### Known limits and open items

- **OWNER DECISION: SR documents land in the business unit's shared container, even when the SR regards a Secure matter.** Option A: add a `sprk_servicerequest` row to `ChildAncestorLinks` so a secure regarding root wins. Option B: keep them business-unit scoped and document it. `RecordContainerResolver` was not changed.
- **OWNER DECISION: invoices.** Option A: inherit access from the matter or project. Option B: grantable on their own. Option C: defer.
- **Document reads carry no creator stamp columns.** Task 054's delete-visibility rule needs them. Assigned to task 032.
- **The Location header points at a URL with no GET** (`.../documents/{id}`). This shape is pre-existing.
- **SR documents are reachable only through the new route.** The documents module's `ScopeDimensions` lack `sprk_relatedservicerequest`, so task 054 must use the route.
- **`$select` keeps `_sprk_project_value`.** This is policy, consistent with the documents module.
- **Dual-linked documents.** A document linked to both a Secure matter and a related SR is downloadable by the requester, as the POML specifies. Owner to confirm.

## Owner decisions 2026-10-11

Built 2026-10-12 in worktree `agent-a4ea01ad185e8d68f`, on top of `980e850f1`.

### Decision 1: invoice documents use option A, "inherit"

**What was built**
- **Four routes** in the `/api/v1/external` group (`ExternalProjectDataEndpoints.cs:345-386`):
  - `GET /invoices/{id}/documents`
  - `POST /invoices/{id}/documents`
  - `GET /invoices/{id}/documents/{documentId}/content`
  - `GET /invoices/{id}/documents/{documentId}/versions`

  They are thin adapters over the four existing generalized handlers, which pass `DocumentParentKind.Invoice`. Read lists, downloads and reads versions. Create uploads. The list returns `canCreate` from the same rights the upload checks.
- **Rights.** `RightsForInvoiceAsync` (`ExternalProjectDataEndpoints.cs:886`) is reached from the Invoice arm of `RightsForDocumentParentAsync` (`:850`). It returns the union of `RightsForRoot` on the invoice's `sprk_matter` and its `sprk_project`. This is the "any accessible parent" rule that the invoices module and `ExternalMessageEndpoints.RightsThroughParents` already apply.
  - A caller who holds Read on no matter and no project is denied before any invoice read.
  - A missing invoice and an invoice under records the caller cannot read are denied identically, so there is no existence oracle.
  - A failed read of the invoice's parents returns 503 `sdap.access.invoice_unreadable`, not 403.
- **`ExternalDataService`**
  - `DocumentParentKind.Invoice = 5` (`:333`).
  - A binding row (`:375`): `sprk_invoice`, `sprk_invoices`, navigation property `sprk_Invoice`, lookup `sprk_invoice`. These are confirmed in `sprk_Document/Entity.xml:3452` and `Other/Relationships/sprk_Invoice.xml`.
  - `GetInvoiceParentsAsync` (`:710`), which uses `GetSingleOrThrowAsync`: a 404 returns null, and any other failure throws.
- **Delete-own (task 032).** It is built from `DocumentParents.Values`, so a document's `_sprk_invoice_value` is now selected, and Read on that invoice is required like any other parent. `sprk_relatedinvoice` is still not consulted.
- **Route ledger.** Four `HandlerDecision` rows were added to `RouteAuthorizationGuardTests.Ledger.cs`, after the service-request rows.

**Task 034 review item, confirmed.** A message posted on an invoice stamps `sprk_regardingproject`, so partners on that project can read it. Under option A this is consistent and needs no change: the same partners already read the invoice and its documents through the union rule.

**§11 answers for the new surface** (four routes and adapters, `RightsForInvoiceAsync`, `GetInvoiceParentsAsync`, the `Invoice` parent kind):
1. **Existing.**
   - The task-031 generalized document handlers and the `DocumentParentBinding` table.
   - `RightsForRoot`.
   - `ExternalMessageEndpoints.RightsThroughParents`, which applies the same union for invoice posts but returns a message-specific result.
   - The invoices module descriptor, which decides visibility only and has no Create answer.
2. **Extension.** The parent-kind table and the handlers were extended by one kind, with no new handler. The rights helper reuses `RightsForRoot`. `RightsThroughParents` was not reused because it is private to the message endpoints and takes the message's parent shape; sharing it would be a refactor outside this brief.
3. **Cost of doing nothing.** External users cannot list, download or upload documents on an invoice in the SPA. That is FR-24 under the owner's option A.

### Decision 2: secure records

**The container rule as implemented**
- A document's container is decided by `RecordContainerResolver.ResolveForRecordAsync(parentEntity, id)` (`RecordContainerResolver.cs:184` / `:198`). It is called once by every external upload path: project, matter, work assignment, service request and invoice (`ExternalProjectDataEndpoints.cs:1235`).
  - A secure root, or a secure root reached through the regarding chain, gives that record's own container.
  - Otherwise the result is the owning business unit's container.
  - No path creates or chooses a per-type container.
- **The fix is in the service request**, made in `RecordContainerResolver` rather than through a parallel mechanism.
  - `ChildAncestorLinks["sprk_servicerequest"]` is new (`RecordContainerResolver.cs:1876-1884`).
    - Its regarding project, matter and work assignment are roots.
    - Its regarding communication, invoice and to-do are intermediates that are **followed** live (`followed:`), so a non-secure chain reaches the business unit container instead of a held 409.
    - The service request is core and carries no denormalized copies, so there is no staleness to guard.
    - The polymorphic pair (`sprk_regardingrecordid` / `sprk_regardingrecordtype`) and its party columns are read too.
    - The SR stays `Intermediate` in `KindByEntity` (`:1713`). A record filed UNDER a service request is still held, so that path is unchanged.
  - `CoreAncestorResolver.PartyRegardingColumns["sprk_servicerequest"]` (`CoreAncestorResolver.cs:289`) lists `sprk_regardingcontact`, `sprk_regardingorganization` and `sprk_regardingaccount`, for pair rule 3. This also makes `OwnedChildWrite.PairAgreementColumns` cover service-request writes, consistent with the resolver.
  - Schema: every regarding column used exists in `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ServiceRequest/Entity.xml`. No schema change was needed.
- **Invoices** already resolved through their matter and project (`ChildAncestorLinks["sprk_invoice"]`). An invoice is not securable (`SecurableEntityRegistry.FlagIsNotASecurityInput`), so an invoice document under a secure matter lands in the matter's container.
- **Reads, downloads and deletes resolve the same container.** Each passes through `EnsureDocumentPointerContainerAsync` (`RecordContainerResolver.DocumentPointer.cs:183`):
  - download and versions through `DocumentStorageResolver.cs:107`;
  - delete through `DocumentCheckoutService.cs:926` (`VerifiedFileFirstKeepRowOnFailure`).

  The strict rule derives the expected container with `DeriveDocumentContainersAsync` / `DeriveCoreAsync` (`:516` / `:582`). That calls the same `ResolveForRecordAsync` for each document link, including `sprk_relatedservicerequest` and `sprk_invoice`.

  The interim rule (the default) serves an item the BFF uploaded app-only only when that strict test passes (`:319-331`). This is the external upload case.

  `SpeContainerOwnershipGuard` is the customer-marker (tenant) guard and is unaffected by which container is chosen.

### Tests

**`ExternalRecordDocumentRoutesTests`** runs through the real routes, with the real `RecordContainerResolver` and the real pointer check.
- **Invoice coverage.** The `invoices` segment is added to every parameterized route test: upload then download, creator stamp, ViewOnly upload 403, outside the caller's set 403, scoping, and `canCreate` (ViewOnly false, Collaborate true, FullAccess true). The four new invoice tests are:
  - `Invoice_UnderTwoParents_TakesTheUnionOfTheCallersRightsOnThem`
  - `Invoice_UnderRecordsTheCallerCannotRead_AndAMissingInvoice_DenyIdentically_AndTouchNothing`
  - `Invoice_ForACallerWhoReadsNoMatterOrProject_IsDeniedBeforeAnyInvoiceRead`
  - `Invoice_AParentReadThatFails_Is503_NotA403_AndTouchesNothing`
- **Container coverage**:
  - `ServiceRequest_RegardingASecureMatter_StoresInTheMattersContainer_AndDownloadAndDeleteResolveTheSameContainer`. The upload goes to the secure container. The route download passes the real pointer check. `EnsureDocumentPointerContainerAsync` accepts the secure container and refuses the business unit container.
  - `ServiceRequest_RegardingANonSecureMatter_StoresInTheBusinessUnitsContainer`
  - `ServiceRequest_RegardingAnInvoiceUnderASecureMatter_StoresInTheMattersContainer`
  - `InvoiceDocument_UnderASecureMatter_IsStoredInTheMattersContainer_AndServedFromIt`
  - `NonSecureParents_OfEveryType_ShareTheBusinessUnitsOneContainer_ThereIsNoContainerPerType`. All five types land in the one business unit container.

**`ChildRecordContainerResolutionTests`** has four new resolver cases for a service request regarding:
- a secure matter;
- a non-secure matter;
- an invoice under a secure matter, which is followed;
- a to-do under a secure project, which is followed.

It also has the SR column-read sweep row. That row comes from the solution source, not a live sweep.

**`ExternalDeleteOwnTests`.** The pinned delete-target `$select` now ends in `_sprk_invoice_value`.

**Fail-before evidence.** After each run I restored the real files and confirmed them byte-identical with `cmp`.

| Mutation | Failed / total |
|---|---|
| `RecordContainerResolver.cs` and `CoreAncestorResolver.cs` reverted to `980e850f1` (the pre-change resolver) | 6 / 400 (the four "secure" SR resolver and route cases, plus the sweep row). The non-secure cases pass, as expected: the old fallback was the business unit. |
| The four invoice routes unmapped (the pre-change routing surface) | 14 / 139 |
| `RightsForInvoiceAsync` ignores the project parent (no union) | 1 / 69 (the union test) |

### Measurements (2026-10-12)

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ExternalAccess\|FullyQualifiedName~RecordContainer\|FullyQualifiedName~DocumentCheckout"` | 2450 / 2450 pass |
| The same, plus `ChildRecordContainer\|CoreAncestor\|RecordKeyedUpload\|OfficeSave` | 2725 / 2725 pass |
| `dotnet test tests/Spaarke.ArchTests/` | 935 / 940 on the first run. The 5 failures were boot-fixture tests that timed out acquiring a Redis managed-identity token (`TaskCanceledException`, machine load). Re-run alone, `FullyQualifiedName~RouteAuthorizationGuard` passed 86 / 86, including those 5 and the invoice ledger rows. |

**Publish size** (`.claude/rules/bff-hygiene.md` item 4). I ran `dotnet publish -c Release` on trees extracted fresh with `git archive` under `C:\tmp`, zipped the output with `Compress-Archive`, and removed the trees afterwards.

| Side | Tree | Files | Zip | `Sprk.Bff.Api.dll` |
|---|---|---|---|---|
| Master | `origin/master` `9695a4ca7` | 192 | 38,232,304 B | 17,108,992 B |
| This branch | `cf1200f1d` | 192 | 38,257,973 B | 17,186,304 B |

- The zip delta is **+25,669 B** (about 0.03 MB). It includes all of the R3 branch since master, not only this change.
- The file counts match, and the total is 36.5 MB, under 55 MB.
- **CVE:** no package, project or props file changed, so there is no new HIGH CVE.

### Known limits (owner decisions 2026-10-11)

- **Legacy service-request documents.** An SR document already stored in a business unit container while the request regards a secure record is now "misplaced" under the strict rule.
  - The interim rule (the default) still serves it, through its business unit subtree arm.
  - The legacy migration derives the target through the same resolver, so it will move such files.
  - If the migration's `-Apply` ran before this change deploys, re-run `-Verify` before the strict flip.
  - External SR routes are not deployed yet (task 038).
- **Two secure parents refuse.** An invoice, or a service request whose chain reaches two DIFFERENT secure records, is refused by the resolver as `container_ancestor_ambiguous` (409, fail closed). This is the existing resolver rule, not new behaviour. The union rights test uses non-secure parents for that reason.
- **Inconsistent SR data refuses.** The polymorphic-pair rules now apply to service-request rows. An SR whose pair disagrees with its typed regarding lookups refuses its document uploads with 409 rather than guessing.
- **Dual-linked documents** (see the open item above). A document linked to both a secure matter and a service request regarding a different secure record now derives a second, different container through the SR link. That shape was not tested here, and how the pointer check treats two derived containers is not verified. A reviewer should check it.
