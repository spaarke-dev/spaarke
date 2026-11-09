# 130-0: external invoices follow the caller's own firms

**Rule (design.md:414):** a partner sees only invoices of its own firms, on the records it can access. External users "see only their firm's submissions and invoices."

Rigor: FULL (tenant isolation). Reviewed by unified-access-control-r2.

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs` | New hide reason `FirmNotCallers` (code `firm_not_callers`). `DecideAsync` step (0b) hides an invoice whose `sprk_vendororg` is not one of `InvoiceVisibilityReaders.CallerFirms`. `ReadFirmHeldInvoicesAsync` now selects `sprk_vendororg` in the same app-only batch as `sprk_firmhold` and returns `InvoiceFirmRead` (the held set and each invoice's firm). Adds `CallerFirms` (three states), `CallerFirmsState` and the `CallerFirmReader` delegate. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` | `HideInvoicesForAsync` takes a `CallerFirmReader`. After the internal-systemuser check, it reads the caller's firms once per call: Known means the rule applies, NoFirm means every invoice is hidden (`firm_not_callers`, not a fault), and Unknown, a throw or a null answer return `InvoiceVisibilityResult.ReadFailed`. `InvoiceVisibilityReadersOver` carries the firms; the other seams pass none. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` | The `ExternalInvoiceHiding` binding passes a reader over `ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync`, mapped through `CallerFirms.From`. |

Edit scope (approved by uac-r2, option 1): only these three files outside the tests. No other file under `Infrastructure/ExternalAccess/**`, and no decorator.

## Where the firm lists come from

- **The caller's firms:** `ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync`, the one definition of "the caller's firms" that the submission routes validate `firmId` against and `/me` lists (tasks 044/051). It returns the contact's conferring organization memberships, and nothing for a contact that is not Active. It is read once per call of the seam. `CallerFirms.From` maps it as follows: unreadable or a null list is **Unknown**; no usable id is **NoFirm**; otherwise **Known**.
- **The invoice's firm:** `sprk_invoice.sprk_vendororg`, read app-only through the `IGenericEntityService` firm-hold reader, in the same query (one per 50 invoices). An invoice with no firm, or one that does not come back, is not one of the caller's.

## Decisions

- **Owner decision 2026-11-08 (relayed by uac-r2):** a caller known to be a member of no firm sees no invoices. This is the firm-match policy. It is decided in exactly one place: the `CallerFirmsState.NoFirm` branch of `ExternalDataService.HideInvoicesForAsync`, tested by `OwnFirms_ACallerWithNoFirm_SeesNoInvoice_AndItIsNotAFault`. "No firm" and "unknown" stay distinct. Both hide every invoice, but no firm is `firm_not_callers` with `Faulted = false`, while unknown is `read_failed` with `Faulted = true`. The logs and tests tell them apart.
- **Order.** The firm hold decides first, so a held invoice keeps `firm_missing` exactly as before. The own-firms rule runs next, before any source read.
- **Internal systemuser.** Nothing is hidden and no firm is read: the firm read happens after the internal check.
- **Seams that do not apply the rule here.** The submission routes stay on `ExternalInvoiceSubmissionHiding`, whose filter scopes by firm. The assistant tools' `AppOnlyInvoiceFamilyHiding` is out of scope here and becomes task 130-0b.

## Paths covered (each through the real seam over `InvoiceWorld`)

| Path | Test |
|---|---|
| Module list (`/data/fetch`, invoices module) | `ExternalMultiRootListReadTests.InvoicesModuleFetch_OwnFirms_AnotherFirmsInvoiceIsAbsent_TheCallersOwnIsListed` (must fire and must not fire) |
| `/data/record/sprk_invoice/{id}` | `ExternalMultiRootListReadTests.InvoicesModule_SingleRecordRead_OwnFirms_AnotherFirmsInvoiceIsRefused_BeforeAnyRead`. The route refuses every invoice id before any read (task 098 classification), so the seam is not on this path and the rule holds there without it. |
| Project-data child reads (`/invoices/{id}/documents`, content, versions) | `ExternalRecordDocumentRoutesTests.Task130_OwnFirms_AnotherFirmsInvoiceOnASharedMatter_HasNoDocuments_TheCallersOwnInvoiceLists` (must fire and must not fire) |
| Messages (`/{root}/{id}/messages`) | `ExternalMessageEndpointsTests.Task130_OwnFirms_AnotherFirmsInvoice_ItsThreadIsRefused_AndItsMessageIsWithheldFromTheMattersThread` |
| Notification feed (`/notifications`) | `ExternalNotificationFeedTests.Task130_OwnFirms_AMessageRegardingAnotherFirmsInvoice_NeverAppears_NorCounts` |

What follows the invoice on these paths is its documents (project-data) and its messages (messages, feed). No external route serves invoice lines (no `sprk_billingevent` or invoice-line surface exists under `Api/ExternalAccess`), so there is no line to hide there.

Seam tests (`ExternalInvoiceVisibilityTests.OwnFirms.cs`) cover these cases:
- another firm's invoice is hidden and the caller's own stays visible (the seeded must-fire);
- a multi-firm caller;
- an invoice with no firm (a blank firm, and the firm-missing hold);
- a caller with no firm;
- unknown firms (a throw, an unreadable answer, a null answer);
- one firm read per call, with the firm read in the firm-hold batch;
- an internal systemuser, which makes no firm read;
- an external caller, which reads its firms once;
- the `CallerFirms` states;
- the submission seam unchanged.

A production-wiring test, `TheInvoiceSeam_IsRegisteredByAddExternalAccess_WithTheCallersFirms`, drives the binding registered by `AddExternalAccess`. The caller's firms come from the membership read, called once.

## Verification

- **Filtered unit and contract suites** for the touched classes (`ExternalInvoiceVisibilityTests`, `ExternalMultiRootListReadTests`, `ExternalRecordDocumentRoutesTests`, `ExternalMessageEndpointsTests`, `ExternalNotificationFeedTests`, `InvoiceSubmissionPersisterTests`, `InvoiceSubmissionEndpointsContractTests`, `InvoiceSubmissionRerunTests`, `ExternalLiveParentRuleTests`, `ExternalMessageSendTests`, `ExternalModuleColumnAllowListTests`, `ExternalUserContextFirmsTests`): 1757 passed, 0 failed.
- **Seeding proof.** The hide in `DecideAsync` step (0b) was dropped. 9 must-fire tests failed: the seam must-fire test (contact and flagged systemuser), the multi-firm caller, the invoice with no firm, the production wiring, and the module-list, project-data, messages and feed paths. The file was then restored byte-identical (same SHA-256), and all 19 named tests passed again. The `/record` test stays green under the seed, as expected, because that route refuses every invoice before the seam.
- **ArchTests (full):** 2181 passed, 0 failed.
- **Publish-size delta against master:** not measured; a fresh master publish was too slow on the loaded machine. The change is source-only within existing files, with no package and no new assembly, so the delta is the IL of about 140 lines.

## Submission routes: firm filter tests already in place

- List: `InvoiceSubmissionEndpointsContractTests.External_list_excludes_a_submission_with_no_firm` (another firm's row `INV-OTHER` is excluded; the query is `sprk_vendororg In [caller's firms]`), and `External_QueryNamingAnotherFirm_Is403_AndThePrecheckReadsNothing`.
- Single read: `InvoiceSubmissionEndpointsContractTests.External_get_of_another_firms_submission_is_the_same_404_as_a_missing_one`.

No test was added for these two reads; both already had one.

## For 130-0b (the assistant tools' seam)

`AppOnlyInvoiceFamilyHiding` receives no caller. An external systemuser in the assistant has a `systemuserid`, not the SPA's `CallerPrincipal` with a contact id. `ReadCallerFirmsAsync` needs a `CallerPrincipal` with `ContactId` (and `ContactUnreadable`). 130-0b therefore needs one of two things: the systemuser's linked contact (the workforce-link mapping), or a firm read keyed by systemuser. It also needs the same three-state answer (`CallerFirms`) passed into the seam.

## Component justification (root CLAUDE.md section 11)

1. **Existing.** `ExternalInvoiceVisibility` is the one invoice predicate; `ReadCallerFirmsAsync` is the one definition of the caller's firms; `ReadFirmHeldInvoicesAsync` is the one app-only invoice read. The new surface is one hide reason, one delegate (`CallerFirmReader`), one three-state result (`CallerFirms`) and one read result (`InvoiceFirmRead`), all inside the existing predicate file.
2. **Extension.** All three existing components are extended. The firm column rides on the existing firm-hold query, and the caller's firms reuse the existing read. No new service, query, registration or package. The delegate exists because the predicate is static and cannot resolve `ExternalParticipationService`. The three-state type exists because "no firm" and "unknown" must not share an empty list.
3. **Cost of doing nothing.** The design.md:414 rule (a partner sees only invoices of its own firms) would hold on the submission routes only. The invoice paths above would scope invoices by record access alone, not by firm.

## Existing tests changed

The calls to `HideInvoicesForAsync` gained the world's caller-firm reader: `ExternalInvoiceVisibilityTests.cs` (3), `ExternalInvoiceVisibilityTests.OwnSubmission.cs` (1) and `InvoiceSubmissionPersisterTests.cs` (3). `InvoiceWorld` answers every `sprk_invoice` read with the invoice's firm, `InvoiceWorld.Firm` by default, the same firm its seeded submissions carry, and the caller's firms default to that firm. No expectation changed. The `ExternalInvoiceVisibility` entry in `InvoiceFirmHoldAppOnlyGuardTests` records the `sprk_vendororg` read in the same batch (comment only; the type was already listed).

## Fix round 1

**Rule:** a partner sees only invoices of its own firms, and what belongs to them. A to-do, an event, a document or a submission that belongs to an invoice is shown only when that invoice is visible to the caller. A row with no invoice link is unaffected.

Rulings applied (uac-r2, 2026-11-08):
- Every linked invoice gets the full decision. A fault hides every invoice-linked row.
- A row is hidden when ANY invoice it links to is not visible, or when its link cannot be read.
- A child row is decided by its invoice even when it is also filed under a shared root.
- A root is decided by its grant; its invoice link is shown only when that invoice is visible.
- Which columns are invoice links comes from `InvoiceLinks` (130-0b): `LinkColumnsOf`, `IsRoot`, `IsRootWithInvoiceLink`. There is no hand list.

### What changed

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | The shared filter `HideInvoiceLinkedRowsAsync`. It makes one seam call per page over the distinct linked invoices, and none when no row links one. No seam, a throw or a faulted decision hides every invoice-linked row; `Truncated` is set when that dropped a row. `InvoiceLinksOf` covers both row shapes (Web API `_col_value` and FetchXML). `WithInvoiceLinkColumns` adds the link columns to the to-do, event and document list `$select`. The single-document read selects them too. `MapTodo`, `MapEvent` and `MapDocument` carry the links. |
| `Api/ExternalAccess/Dtos/ExternalProjectDtos.cs` | `InvoiceLinks` on the to-do, event and document DTOs: `[JsonIgnore] internal`, server-only and never serialized. |
| `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | The to-do, event and document list adapters filter each page (`HideInvoiceLinkedAsync`). The single-document download and versions routes put the document's links to the seam inside `IsDocumentReadableUnderAsync`; a refusal is the existing not-under-the-record 403. K1 is on the invoice-document routes (below). |
| `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` | Child modules (documents): the fetch projects the link columns server-side and filters the page. Roots (work assignments): `RedactRootInvoiceLinksAsync` blanks every form of a link whose invoice is not visible, on `/data/fetch` and `/data/record`: the lookup, `_col_value`, `{col}name` and their annotations. |
| `Api/ExternalAccess/ExternalMessageEndpoints.cs` | K1 on the invoice thread (GET and POST). |
| `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs`, `ExternalInvoiceSubmissionAuthorizationFilter.cs` | Submission parity: the list and the single read also hide a submission whose invoice is not visible: one call per page, inline in `VisibleSubmissionsAsync`, which stays the one decision function of both reads (the ArchTest `TheSubmissionListAndRead_CallOneDecisionFunction`). The filter's firm methods are untouched. The two read routes declare Read on `sprk_invoice` as well (`WorkforceRoleCeilingGate.ReadInvoiceSubmissionsWithInvoice`). |
| `Api/ExternalAccess/WorkforceRoleCeilingGate.cs` | One set, `ReadInvoiceSubmissionsWithInvoice` (Read on `sprk_invoicesubmission` and `sprk_invoice`), next to `ReadInvoiceSubmissions`. The gate's evaluation is unchanged. |

### K1: a faulted invoice decision on an invoice-scoped route

The project-data invoice routes (`/invoices/{id}/documents`, content, versions, upload) and the invoice message thread (GET and POST) answer the existing `InvoiceUnreadableReasonCode` 503 when the invoice decision faults. The 503 comes only after the record check: the invoice exists, and the caller holds Read through one of its parents. A missing invoice, and one under a record the caller cannot read, still get the uniform 403. A hidden invoice is still the 403.

The submission single read answers its existing retryable 503 (`ReadUnavailable`), reached only after the submission's own record check; a missing submission stays the 404.

### Census: external readers of each table

| Table | External REST reader | Through the seam? | Why / test |
|---|---|---|---|
| `sprk_todo` | `GET /{projects\|matters\|workassignments}/{id}/todos` | Yes, one call per page | `ExternalInvoiceLinkedChildListTests.Todos_*` |
| `sprk_todo` | `POST /{root}/{id}/todos` | No | Creates a row; reads no existing row. |
| `sprk_todo` | `PATCH /todos/{id}` | No | Returns 204, no row data. Open question in the fix-round report: it writes without an invoice check. |
| `sprk_todo` | `DELETE /todos/{id}` | No | Creator only; returns no row. There is no `GET /todos/{id}`. |
| `sprk_event` | `GET /{projects\|matters\|workassignments}/{id}/events` | Yes, one call per page | `ExternalInvoiceLinkedChildListTests.Events_*` |
| `sprk_event` | `POST /projects/{id}/events`, `DELETE /events/{id}` | No | Create; delete is creator only. Neither returns another row. There is no `GET /events/{id}`. |
| `sprk_document` | `GET /{projects\|matters\|workassignments\|servicerequests\|invoices}/{id}/documents` | Yes, one call per page; both `sprk_invoice` and `sprk_relatedinvoice` | `ExternalInvoiceLinkedChildListTests.Documents_*` |
| `sprk_document` | `GET .../documents/{documentId}/content` and `/versions` | Yes, one call for the document | `ExternalInvoiceLinkedDocumentRouteTests` |
| `sprk_document` | `/data/fetch` (documents module) | Yes, one call per page | `ExternalInvoiceLinkedChildListTests.DocumentsModuleFetch_*` |
| `sprk_document` | `/data/record/sprk_document/{id}` | Not needed | Refuses every document id before any read (`ExternalMultiRootListReadTests.DocumentsModule_SingleRecordRead_IsRefusedForADocumentId`). |
| `sprk_document` | `POST .../documents`, `DELETE /documents/{id}` | No | Upload creates; delete is creator only. There is no thumbnail, preview or count route. |
| `sprk_communication` | `GET`/`POST /{root}/{id}/messages` | Yes (130-0) | Invoice thread: the invoice gate, plus K1. Other roots: a message regarding an invoice the caller may not see is withheld (130-0). |
| `sprk_communication` | `GET /notifications` | Yes (130-0) | `ExternalNotificationFeedTests.Task130_*`. `POST /notifications/seen` writes the caller's marker only. |
| `sprk_analysis`, `sprk_memo` | None returns a row | No | The only external read is the service-request related-records probe, which checks existence to refuse a delete and returns no row. Ruling (d): nothing joins. |
| `sprk_workassignment` | `/data/fetch` and `/data/record` (work-assignments module) | Root: never hidden; the link is redacted through the seam | `ExternalInvoiceLinkedChildListTests.RootRead_*`, `WorkAssignmentRecordRead_*`. The module allow-list already strips `sprk_regardinginvoice`, so the redaction is the guard for any future column. |
| `sprk_workassignment` | `GET /invoice-submissions/firm-records` | No | Returns record ids only, no invoice link. |
| `sprk_invoicesubmission` | `GET /invoice-submissions`, `GET /invoice-submissions/{id}` | Yes (fix 1), one call per page | `InvoiceSubmissionEndpointsContractTests.Task130_0_*` |
| `sprk_invoicesubmission` | `POST /{root}/{id}/invoice-submissions` | No | Creates a submission. |
| Every table | Assistant tools | 130-0b | Owned by 130-0b. |

### Known limits

- A matter document a firm could already see, which is later linked to another firm's invoice as support, disappears for the first firm; that is the price of failing closed. Ruling (c) extends this to every invoice-linked child: a to-do, event or document filed under a shared record, which later gains a link to an invoice the caller may not see, disappears from that record's lists.
- A root is decided by its grant; its invoice link is shown only when that invoice is visible.
- K2: the caller's firms are read once per seam call, not cached per request. A request that runs several surfaces reads them once per surface.
- K3: the surface tests drive the real seam over `InvoiceWorld`, which doubles the app-only reads the seam makes. Each test's fidelity is that double's: live Dataverse is not exercised. The decision itself is tested over the same reads in `ExternalInvoiceVisibilityTests`.
- K4: closed in fix1: submissions also follow their invoice.

### Tests

The tests use the real seam over `InvoiceWorld` (`InvoiceHidingStub.Real(world)`), the real `ExternalDataService` over `FakeDataverse`, and the real routes:
- `ExternalInvoiceLinkedChildListTests`
- `ExternalInvoiceLinkedDocumentRouteTests`
- the four `Task130_0_*` submission tests
- the three K1 tests: `ExternalMessageSendTests.Task130_0_APredicateReadFault_IsTheInvoiceUnreadable503_AfterTheRecordCheck`, `ExternalMessageEndpointsTests.Task130_0_APredicateReadFault_IsTheInvoiceUnreadable503_OnlyAfterTheRecordCheck` and `ExternalRecordDocumentRoutesTests.Task130_0_APredicateReadFault_IsTheInvoiceUnreadable503_OnEveryDocumentRoute`
- `WorkforceCeilingRouteFamilyTests`, with two rows for Read on `sprk_invoice`

Seeding proof:
- The three new filters were disabled together: `HideInvoiceLinkedRowsAsync`, the submission invoice step (then a helper, since inlined into `VisibleSubmissionsAsync` unchanged) and `RedactRootInvoiceLinksAsync`. 29 of the 42 new tests failed, every must-fire and fault case among them. The files were restored byte-identical (same SHA-256).
- Pointing the two submission read routes back at `ReadInvoiceSubmissions` failed the two new ceiling rows. The file was restored byte-identical.

Existing tests changed because they encoded the old fault answer (a fault is now the 503 after the record check):
- `ExternalMessageSendTests.Task098_AnInvoiceThePredicateHides_IsRefusedAsANonexistentInvoice_AndCreatesNoRow`: the `fault` case moved to the K1 test.
- `ExternalMessageEndpointsTests.Task098_APredicateReadFault_RefusesTheInvoiceThread_AsANonexistentInvoice_FailClosed`: replaced by the K1 test.
- `ExternalRecordDocumentRoutesTests.Task098_AnInvoiceThePredicateHides_DeniesEveryDocumentRoute_AsAMissingInvoice_AndTouchesNothing`: the `fault` case moved to the K1 test.

The `InvoiceSubmissionEndpointsContractTests` host now registers `ExternalInvoiceHiding` over its `InvoiceWorld`, as `AddExternalAccess` does. No expectation changed.

ArchTests: `ExternalInvoiceVisibilityGuardTests.NotInvoiceSurfaces` lists `ExternalModuleDataEndpoints::InvoiceLinkedChildAttributes`. It projects a child module's link columns for the predicate, and reads no row.

### Component justification (root CLAUDE.md section 11), fix round 1

New surface:
- the helpers in `ExternalDataService`: `HideInvoiceLinkedRowsAsync`, `InvoiceLinksOf` and `WithInvoiceLinkColumns`;
- the server-only `InvoiceLinks` DTO property;
- `HideInvoiceLinkedAsync`, `RedactRootInvoiceLinksAsync` and `InvoiceUnreadableProblem`;
- the invoice step in `VisibleSubmissionsAsync`;
- the `ReadInvoiceSubmissionsWithInvoice` ceiling set.

1. **Existing.** `ExternalInvoiceHiding` is the one caller-aware decision, and `InvoiceLinks` is the one list of invoice link columns. `SecureRootsFilter` is the per-page filter pattern followed here. `InvoiceUnreadableReasonCode` is the existing 503. `WorkforceRoleCeilingGate` holds every route's privilege set.
2. **Extension.** Each piece extends these. There is one filter for every child surface, not one per route. The 503 helper replaces an inline copy. The ceiling set follows the file's own `Set(...)` pattern. The filter runs at the endpoint, not inside the data service, because the data service cannot resolve the scoped seam and its public virtual reads are overridden by many test doubles.
3. **Cost of doing nothing.** The rule would cover the invoice rows but not what belongs to them. The to-do, event, document and submission reads would be scoped by record access alone, and a work-assignment read would show its invoice link whatever the invoice's firm. A transient firm-read fault on an invoice route would also answer "no such invoice" instead of "try again".

No package was added. Publish-size delta against master: not measured; a fresh master publish was too slow on the loaded machine. The change is source-only, within existing files plus tests.

## 130-0b step 1: the assistant tools' seam applies the firm rule

**Rule:** a partner sees only invoices of its own firms, on the assistant's `dataverse.search_data`, `dataverse.read_query` and `dataverse.describe` too.

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs` | `AppOnlyInvoiceFamilyHiding` takes an `InvoiceFamilyCaller` (one per tool call) first. `InvoiceFamilyCaller(Guid? SystemUserId)` remembers the caller's firms, so they are read at most once per tool call; no resolved systemuser is no firm, with nothing read. `HiddenFamilyRowsAsync` (submission rows): with the caller's firms, a submission is also hidden when its own firm is not one of them. |
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | `HideInvoiceFamilyRowsAsync(caller, table, rowIds, rootFlags, systemUserFirms)`: reads the caller's firms through the caller object. Known firms are passed to the predicate (the firm hold first, `firm_missing`; then `firm_not_callers`). No firm, and unknown firms (a throw, a null answer, `Unknown`), hide every asked row with no row read. |
| `Infrastructure/DI/ExternalAccessModule.cs` | The binding reads the firms through `ExternalInvoiceSubmissionAuthorizationFilter.ReadSystemUserFirmsAsync`, mapped by `CallerFirms.From`. |
| `Services/Ai/Handlers/DataverseSearchDataHandler.cs`, `DataverseReadQueryHandler.cs`, `DataverseDescribeHandler.cs` | Each handler keeps the verdict of `CallerExternality.ClassifyCallerAsync` (one classification, as before) and builds one `InvoiceFamilyCaller` from its `SystemUserId` for the tool call. search_data passes the same caller to every family table on the page. An internal systemuser never reaches the seam, so no identity or firm is read. |

**Submissions on this plane (130-0b).** A submission row is hidden when its invoice is not visible OR its own firm (`sprk_invoicesubmission.sprk_vendororg`, read in the same app-only read as its invoice link) is not one of the caller's firms. This is the most restrictive reading and fails closed. It differs from the REST submission routes, which scope by the submission's own firm only.

**Child rows:** step 2 (below). **Roots:** step 3.

### Component justification (root CLAUDE.md section 11)

`InvoiceLinks` (added earlier in 130-0b; the answers also live in its class comment):
1. **Existing.** `ExternalInvoiceVisibility.ReadInvoicesOfAsync` maps the closed invoice family only, with one hand-listed link per table; the edge registries list every invoice edge but read nothing.
2. **Extension.** Widening `ExternalInvoiceVisibility.FamilyTables` would change the closed taint list and every guard that scans for its table names; `InvoiceLinks` reads the registries instead, beside the predicate.
3. **Cost of doing nothing.** A to-do, event, document or message about another firm's invoice reaches a partner through the assistant tools and the external routes.

`InvoiceFamilyCaller` (the per-tool-call caller):
1. **Existing.** `CallerExternalityVerdict` carries the systemuser but no firms; `CallerFirmReader` is keyed by the SPA's `CallerPrincipal`, which an assistant caller does not have; `ReadSystemUserFirmsAsync` reads the firms but remembers nothing.
2. **Extension.** The seam's delegate is extended with it (no new seam, service or registration). It cannot be the verdict itself, because the verdict lives in `Services.Ai.Context` and the firm memo must stay inside `Infrastructure.ExternalAccess`, where `CallerFirms` is internal.
3. **Cost of doing nothing.** Without a caller the seam cannot apply the firm rule at all. Without the memo, search_data reads the caller's firms once per invoice-family table on the page instead of once per tool call.

### Tests

The handler tests drive the seam as `AddExternalAccess` registers it (`InvoiceFamilyFirmWorld`, over `InvoiceWorld`, with the identity resolution and the contact reads mocked). For each of search_data, read_query and describe:
- another firm's invoice is hidden and the caller's own firm's invoice is shown (must fire and must not fire);
- no linked contact, a contact-binding fault and an inactive linked contact each hide every invoice row, with no row read;
- an internal systemuser's results are unchanged, and no identity, membership or app-only read happens.

Also:
- **One firm read per tool call.** search_data with invoice, submission and Signal hits makes three seam calls and one firm read (one identity resolution, one membership read).
- **Submission.** read_query: another firm's submission whose invoice is visible is hidden; the caller's own firm's submission is shown.

The existing direct calls of `HideInvoiceFamilyRowsAsync` in `ExternalInvoiceVisibilityTests*.cs` pass a new caller and `InvoiceWorld.SystemUserFirms` (the world's firm by default); no expectation changed. The ArchTest controls that call the seam pass a caller.

## 130-0b step 2: child rows are decided by their invoices

**Rule:** on the assistant's `dataverse.search_data`, `dataverse.read_query` and `dataverse.describe`, a row of a child table with an invoice link is hidden from an external caller when ANY invoice it links to is not visible to that caller, or cannot be decided. Each linked invoice gets the full invoice decision of step 1 (firm hold, `firm_not_callers`, (a′)). A child row is decided by its invoice even when it is also filed under a shared root.

**Tables.** Every table `InvoiceLinks` lists that is neither a root nor in the invoice family (`ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`); no hand list. Today that includes to-do, event, document (both `sprk_invoice` and `sprk_relatedinvoice`), communication, communication thread, analysis and memo. A table added to the registries is covered without a code change.

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs` | `IsInvoiceLinkedChildTable`; the seam delegate's comment states the child rule and its fault rule. |
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | `HideInvoiceFamilyRowsAsync` sends a child table to the new sibling `HideInvoiceLinkedChildRowsAsync`: one app-only link read through `InvoiceLinks.ReadLinkedInvoicesAsync` (chunked by the helper), then ONE invoice decision over the union of the linked invoices, with the firms from step 1's `InvoiceFamilyCaller` (no second firm read). The firms are not read when no asked row links to an invoice. |
| `Infrastructure/DI/ExternalAccessModule.cs` | Comment only; the same registration. |
| `Services/Ai/Handlers/DataverseSearchDataHandler.cs` | Child hits are grouped by table with the invoice-family hits: one seam call per table, the same caller. |
| `Services/Ai/Handlers/DataverseReadQueryHandler.cs` | A child table is gated after the read by row id, on top of its other guards (document narrowing, derived sources, the communication rule). read_query reads one table (no JOIN, no expand), so the queried table is the only one. |
| `Services/Ai/Handlers/DataverseDescribeHandler.cs` | A hidden child record answers exactly as a hidden invoice-family record (`RecordNotVisible`); a real not-found on a child table answers the same. |

**Faults (fail closed, no throw on a child table).** A failed link read hides every asked row of that table (which of them link to an invoice is unknown). Firms that are not known, and a failed or faulted decision, hide every asked row that links to an invoice; an unlinked row stays.

**Unchanged.** Rows with no invoice link. Invoice-family rows (step 1's path; not processed twice). Roots (today the work assignment): not hidden in this step; root link redaction is step 3. Internal systemusers: no link and no firm read. search_data never returns a communication or communication thread to an external caller (dropped earlier, ISS-010); read_query and describe refuse the communication thread.

**Coverage by table and tool.**

| Table class | search_data | read_query | describe |
|---|---|---|---|
| to-do, event, memo, analysis | covered | covered | covered |
| document via `sprk_invoice`, via `sprk_relatedinvoice` | covered | covered | covered |
| communication | n/a (hit dropped, ISS-010) | covered | covered |
| communication thread | n/a (dropped) | n/a (refused) | n/a (refused) |

**Known limit.** A row a firm could already see disappears once it is linked to another firm's invoice; that is the price of failing closed.

**Roots:** step 3.

### Tests (step 2)

Per tool and table class (theories over `InvoiceLinkedCase`, each class a separate case): another firm's invoice hides the row (must fire); the caller's own firm's invoice and no link show it (must not fire; no link reads no firm); a link-read fault and a decision fault each hide the invoice-linked row. Also: an unreadable link (not a GUID; a row the app-only read does not return) hides the row; a document with one visible and one hidden invoice is hidden; a root is unchanged and never reaches the seam; an internal caller reads no link and no firm; search_data with an invoice, a to-do and a document makes one firm read. `InvoiceLinkedChildRowsTests` runs the real seam over every child table `InvoiceLinks` derives. The three handler test classes now default to `InvoiceFamilyHidingStubs.ChildTablesShown` (an invoice-family table still may not reach it; a child table is shown), so the earlier document, analysis and communication tests keep testing their own guard.

## 130-0b step 3: a root's invoice link is shown only when that invoice is visible

**Rule:** a root is decided by its grant; its invoice link is shown only when that invoice is visible. On the assistant's `dataverse.search_data`, `dataverse.read_query` and `dataverse.describe`, a root row is never hidden by its invoice link. When the linked invoice is not visible to an external caller, cannot be decided, or the link cannot be read, every form of that link is blanked in what the tool returns; the row itself is returned.

**Tables.** Every root `InvoiceLinks` lists with an invoice link (`InvoiceLinks.IsRootWithInvoiceLink`); no hand list. Today that is the work assignment (`sprk_regardinginvoice`). A root added to the registries is covered without a code change.

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | `HideInvoiceFamilyRowsAsync` sends a root table to the new sibling `RootRowsWithInvoiceLinkBlankedAsync`; for a root the returned ids are the rows whose link is blanked, never rows to hide. The sibling makes step 2's decision with step 2's reads (one app-only link read through `InvoiceLinks.ReadLinkedInvoicesAsync`, then ONE invoice decision with the firms from step 1's `InvoiceFamilyCaller`); the body is shared with `HideInvoiceLinkedChildRowsAsync` (`RowsLinkedToAnInvoiceNotVisibleAsync`, which differs only in the log wording). Faults fail closed without throwing. |
| `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs` | `BlankRootInvoiceLink(table, fields)` and `RegardingTargetColumns`; the seam delegate's comment states the root rule. |
| `Services/Ai/Handlers/DataverseSearchDataHandler.cs` | Root hits go through the same seam call per table, with the same caller as the invoice-family and child hits. A hit the seam returns is kept, with `content` set to null. |
| `Services/Ai/Handlers/DataverseReadQueryHandler.cs` | A root table is classified like a child table; after the read, one seam call for the page; each returned row (or a row without a readable id) is kept, with its link forms blanked. |
| `Services/Ai/Handlers/DataverseDescribeHandler.cs` | A root record is classified the same way; one seam call; the record is returned, with its link forms blanked when the seam returns it. A not-found on a root keeps its ordinary answer (a root is not a hidden record). |

**Fields blanked (set to null, key kept) on a returned root row.** The link columns come from `InvoiceLinks.LinkColumnsOf(table)`.
- The lookup, in its plain form (`sprk_regardinginvoice`, which read_query maps the value back to) and its value form (`_sprk_regardinginvoice_value`).
- Its name columns (`sprk_regardinginvoicename`, `sprk_regardinginvoiceyominame`), if a tool returns them.
- Every annotation on any of these (`key@…`: `@OData.Community.Display.V1.FormattedValue`, `@Microsoft.Dynamics.CRM.lookuplogicalname`, `@Microsoft.Dynamics.CRM.associatednavigationproperty`, and any other). describe already drops the `@OData.Community` annotations itself.
- The regarding columns derived from the target: `sprk_regardingrecordid`, `sprk_regardingrecordname` and `sprk_regardingrecordurl` (on the work assignment in `docs/data-model/sprk_workassignment.md`). They are shared by every regarding target, so they are kept only when the row shows they hold ANOTHER target: a regarding id that reads as a GUID that is not one of the row's linked invoices. When that cannot be told (no regarding id in the row, or no invoice link in the row), they are blanked (fail closed).
- Not blanked: `sprk_regardingrecordtype`, a lookup to the record-type table that names the kind of target, not the invoice.

**Search snippet decision.** A search hit carries `path`, `entity`, `recordId`, `score` and `content` (up to three highlight snippets from any matching field); no lookup column is projected. For a root hit whose invoice link is not visible, faulted or unreadable, `content` is null and the hit is kept. Index finding: in the repo's solution, the work assignment's Quick Find view (`SavedQueries/{37b5d1c9-…}.xml`, the find columns Dataverse search indexes) has `sprk_name` as its only find column. `sprk_regardinginvoice`, `sprk_regardingrecordname`, `sprk_regardingrecordurl` and `sprk_regardingrecordid` are `IsSearchable=0` in `Entity.xml`. So today the invoice lookup name is not a searchable field of the work assignment. The blanking stays: it costs nothing and holds if the index changes.

**Unchanged.** A root with no invoice link (no firm read). Internal systemusers (no link read, no firm read). Child rows and invoice-family rows (steps 1 and 2). One firm read per tool call: search_data shares one caller across every family, child and root table on the page; read_query and describe make one seam call.

### Component justification (root CLAUDE.md section 11)

`ExternalInvoiceVisibility.BlankRootInvoiceLink` (with `RegardingTargetColumns`):
1. **Existing.** The tools strip `@odata` keys (read_query) and `@OData.Community` annotations (describe), and step 2's seam answers "hide this row"; nothing removes one link's forms from a row that stays.
2. **Extension.** It sits beside `IsInvoiceLinkedChildTable` in the predicate file and reads the link columns from `InvoiceLinks`; it cannot live in `InvoiceLinks` (not edited by this step) or in one handler (read_query and describe both use it).
3. **Cost of doing nothing.** A work assignment shared with a partner names another firm's invoice (its id, number and URL) in read_query and describe.

`RootRowsWithInvoiceLinkBlankedAsync` (the seam's root branch): no new delegate, registration or service; the existing seam is extended with a branch, and the decision body is step 2's.
1. **Existing.** `HideInvoiceLinkedChildRowsAsync` makes the same decision but means "hide".
2. **Extension.** The body is shared; the root branch exists so a root is never sent down the family path (which would read it as a family table) or the child path (whose name and logs say "hidden").
3. **Cost of doing nothing.** The tools cannot tell which root rows carry a link to an invoice the caller may not see.

**Publish-size delta:** not measured: source-only, no package.

### Tests (step 3)

Per tool, on a work-assignment root row, through the seam as `AddExternalAccess` registers it (`InvoiceFamilyFirmWorld`):

| Case | search_data | read_query | describe |
|---|---|---|---|
| must fire: another firm's invoice, row kept, every link form blank (search: `content` null) | `S3_ARootHitOfAnotherFirmsInvoice_IsKeptWithContentBlank` | `S3_ARootRowOfAnotherFirmsInvoice_IsShownWithEveryLinkFormBlank` | `S3_ARootOfAnotherFirmsInvoice_IsReturnedWithEveryLinkFormBlank` |
| must not fire: own firm's invoice, link shown | `S3_ARootHitOfTheCallersOwnInvoice_KeepsItsContent` | `S3_ARootRowOfTheCallersOwnInvoice_ShowsTheLink` | `S3_ARootOfTheCallersOwnInvoice_ShowsTheLink` |
| fault: link read, decision (theory), link blank (search: `content` null) | `S3_AFault_KeepsTheRootHitWithContentBlank` | `S3_AFault_ShowsTheRootRowWithTheLinkBlank` | `S3_AFault_ReturnsTheRootWithTheLinkBlank` |
| unreadable link: not a GUID, not returned (theory), blank | `S3_AnUnreadableLink_BlanksTheContent` | `S3_AnUnreadableLink_IsBlank` | `S3_AnUnreadableLink_IsBlank` |
| must not fire: no invoice link, unchanged, no firm read | `S3_ARootHitWithNoInvoiceLink_KeepsItsContent` | `S3_ARootRowWithNoInvoiceLink_IsUnchanged` | `S3_ARootWithNoInvoiceLink_IsUnchanged` |
| internal caller: unchanged, no link or firm read | `S3_AnInternalCaller_IsUnchanged_AndReadsNothing` | `S3_AnInternalCaller_IsUnchanged_AndReadsNothing` | `S3_AnInternalCaller_IsUnchanged_AndReadsNothing` |

Also: one firm read per tool call (`search_data S3_TheCallersFirmsAreReadOncePerToolCall`: invoice, to-do and root hits; the must-fire and must-not-fire tests of each tool assert one firm read); the regarding columns kept for another target and blanked when they cannot be told apart (`read_query S3_TheRegardingColumns_AreBlankedOnlyForTheInvoice`); and the seam over every root `InvoiceLinks` lists (`InvoiceLinkedChildRowsTests.EveryRoot_ReturnsTheRowWhoseLinkIsBlanked_AndOnlyThatRow`).

**Existing tests changed.** Step 2's three "a root is unchanged in this step (the seam is not called)" tests (one per tool) are replaced by the step 3 tests above; the rule they pinned was temporary.

**Open question.** A root's own text columns (for example `sprk_name`) are root content decided by the grant and are returned by read_query and describe even when the linked invoice is not visible. search_data blanks the whole snippet, because a snippet's source field is unknown.

## Fix round 2

**Write rule:** a caller cannot change what it cannot see. An external update or delete of a to-do, event or document that links to an invoice goes ahead only when every linked invoice is visible to the caller. The links come from `InvoiceLinks.LinkColumnsOf(table)`; for a document both `sprk_invoice` and `sprk_relatedinvoice` count. Otherwise the route gives its existing refusal and writes nothing. A faulted decision gives the route's existing unavailable answer, and only after the record and rights checks.

### Write census

| Route | Decision | Refusal / fault answer | Tests |
|---|---|---|---|
| `PATCH /todos/{id}` | After Write on the to-do's parent and the live-parent check: the to-do's links are read (`GetDeleteTargetAsync`), then the full seam decision | `DenyTodoWrite` 403 / `TodoWriteUnavailable` 503 | `ExternalInvoiceLinkedTodoUpdateTests` (must-fire with read-back of the PATCH log, must-not-fire, fault 503, no firm, no Write → no seam call) |
| `DELETE /todos/{id}`, `/events/{id}`, `/documents/{id}` | `AuthorizeDeleteOwnAsync` step 4, after creator, Read on every parent and live parents. The delete target selects every link column of its table | `DenyDeleteOwn` 403 / `DeleteOwnUnavailable` 503 | `ExternalInvoiceLinkedDeleteTests` (theory over the three tables; someone else's row is refused before the seam; links not read are refused; a document's hidden `sprk_relatedinvoice` is refused) |
| `POST /{root}/{id}/todos`, `POST /projects/{id}/events`, `POST .../documents` | Not applicable: the request types carry no invoice link property and no extension data, and the upload takes no free-form field map | — | `ExternalWriteRequestInvoiceLinkTests` |
| `PATCH /todos/{id}` body | Not applicable: `UpdateExternalTodoRequest` has no link property, so no update can set a link | — | `ExternalWriteRequestInvoiceLinkTests` |
| `POST /{root}/{id}/messages` | Not applicable: the post takes the body and attachments only; `sprk_regardinginvoice` is set only by the server from the route's own record | — | — |

### Descendants census

| Descendant | Can it bypass its parent's invoice decision on REST? |
|---|---|
| `sprk_fileversion` / document versions | No. `GET .../documents/{documentId}/versions` runs `IsDocumentReadableUnderAsync`, the same decision as the content route. |
| child document (`sprk_parentdocument`) | Open. Every document reader decides on the document's OWN invoice links. A child document that is filed under a shared record but does not carry its parent document's invoice link is not hidden by the parent's decision. Whether every attachment writer stamps the parent's invoice link was not established. Closing it needs the invoice links of the parent chain (the narrowing's bounded walk does not collect them): a design change, raised for a ruling. |
| `sprk_analysisoutput` | No external REST reader. |
| `sprk_eventlog` | No external REST reader. |

### F2: message reads and a faulted decision

The thread read and the notification feed capture a faulted invoice decision (a `StrongBox<bool>` passed to `SecureRootsFilter`) and OR it into `Truncated`. `unreadCount` still counts the rows served. Tests: `ExternalMessageEndpointsTests.Task130_0_F2_AFaultedInvoiceDecision_OnAMattersThread_SaysTruncated` (fault and no fault), `ExternalNotificationFeedTests.Task098_*` (the feed fault case asserts `Truncated`).

### Fault ruling (uac-r2, option B)

- Lists and collection reads (record lists, the documents-module fetch, threads, the feed): 200, every invoice-linked row hidden, `Truncated` true; rows with no invoice link are served.
- A decided "no firm" hides every invoice-linked row, with no error and no `Truncated`.
- A per-invoice read failure with the firms known hides that row only.
- A single invoice-linked document (content, versions) whose invoice decision faulted: the `InvoiceUnreadableReasonCode` 503, after the record check. A document not under the record is still the uniform 403, and a document with no invoice link is served.
- Invoice routes (K1) and the writes (F1) stay 503.

Change from fix round 1: the content and versions routes turned a firm fault into the uniform 403; they now answer the 503 (`ExternalInvoiceLinkedDocumentRouteTests.WhenTheFirmReadFails_*`).

### K3: the work assignment parent (D-28)

Invoice routes decide rights through the invoice's core-record parent: matter, project or work assignment (D-28). The invoice-route rights union and the message gate include the invoice's `sprk_regardingworkassignment`.

The invoices module scopes through matter, project and work assignment (D-28): a `ScopeDimension` on `sprk_regardingworkassignment` / `GetAccessibleWorkAssignmentIds()`, and a root lookup. `GetAccessibleWorkAssignmentIds()` is the Read-gated view; a secure work assignment needs a named direct grant, and an inactive or unreadable one confers nothing.

`sprk_regardingworkassignment` was added to the invoices module `ReadableColumns` (`ValidateReadableColumns` requires every dimension attribute to be readable). It is projected server-side, stripped when not requested, returned only for rows that pass the secure-roots rule, and filters and sorts as `sprk_matter` does, in the same single form (no extra name or URL form).

### Root redaction

`RedactRootInvoiceLinksAsync` takes its columns from `RootInvoiceLinkRedaction` (130-0b): the link forms of the table are blanked when a link is hidden, the regarding forms only when the regarding target is the hidden invoice, and an annotation is matched by its column. On REST the module allow-list admits no invoice link or regarding column, and the fetch guard refuses a filter or sort on a column outside it, so the redaction guards any future column.

### Known limits

- The deleted or deactivated work assignment case is enforced where the caller's work assignments are built; at the route it is a work assignment outside the caller's set, which is what the route tests cover.
- (Fix round 3 closed two limits listed here in round 2: the `PATCH /todos/{id}` missing id is now the uniform 403, and a per-invoice read failure on a single invoice-linked read is now the 503.)

### Tests

New:
- `ExternalInvoiceLinkedWriteTests.cs`: `ExternalInvoiceLinkedTodoUpdateTests`, `ExternalInvoiceLinkedDeleteTests`, `ExternalWriteRequestInvoiceLinkTests`.
- `ExternalInvoiceLinkedDocumentRouteTests.WhenTheFirmReadFails_*`: the single-document 503 (must fire), a document with no invoice link served (must not fire), and a document elsewhere still the uniform refusal.
- `ExternalInvoiceLinkedChildListTests`: the three F3 `WorkAssignmentRecordRead_*` tests (through a test registry that admits the link column), `RootRead_EveryFormOfTheRedactionSet_*` and `RootRead_ThePairNamingAnotherRecord_*`, the three `InvoicesModuleFetch_*` K3 tests, `Todos_ForACallerInNoFirm_*` and `TheFilter_APerInvoiceFault_HidesThatRow`.
- `ExternalRecordDocumentRoutesTests.Task130_0_*`: Read before the invoice decision, `ARowWhoseLinksWereNeverSet_IsHidden`, and the K3 work assignment cases (served through it, a grant on another work assignment, a fault after Read through it).
- `ExternalMessageEndpointsTests` / `ExternalMessageSendTests`: F2 (fault and no fault) and the K3 thread GET and POST through the work assignment.

Existing tests changed because the behaviour changed on purpose:
- `ExternalInvoiceLinkedDocumentRouteTests.WhenTheFirmReadFails_AnInvoiceLinkedDocument_IsRefused`: now the 503 (fault ruling).
- `ExternalDeleteOwnTests.DeleteTargetUrl_SelectsBothStampsAndEveryParentLookup`: the select adds each table's invoice link columns.
- `ExternalMessageEndpointsTests.InvoiceUnderAnAccessibleMatter_ReturnsItsThread`: the gate also reads `sprk_regardingworkassignment`.
- `ExternalMultiRootListReadTests.InvoicesModule_RequiresReadOnSecureRoots_AndReadsItsAgreement` and `DocumentsModule_DeclaresTheThreeRelatedTwins_AsRootLookups`: the invoices module's work assignment dimension and root lookup.
- `ExternalModuleColumnAllowListTests`: `sprk_regardingworkassignment` in the invoice allow-list pins (both sides of the shrink comparison).
- `ExternalNotificationFeedTests.Task098_*`: the fault case asserts `Truncated`.

ArchTests: `ExternalInvoiceVisibilityGuardTests.NotInvoiceSurfaces` drops `ExternalMessageEndpoints::RegardingInvoicesOf`. It now takes the communication's invoice links from the registry (K2) and no longer names an invoice-family table, so the ratchet reported it stale.

Seeding proofs (each file restored byte-identical, same SHA-256):
- `PATCH /todos/{id}` with the write-side decision removed: 4 of the 5 `ExternalInvoiceLinkedTodoUpdateTests` failed (must fire, no firm, fault, and the must-not-fire case, which pins the seam call); the no-Write case passed.
- `/data/record` returning the unredacted row: 2 of the 3 F3 tests failed (both that a hidden link is blanked); the own-firm case passed.
- The single-document decision with the fault read as a refusal: the 4 must-fire 503 cases failed; the other 16 passed.

### Component justification (root CLAUDE.md section 11), fix round 2

New surface:
- `ExternalProjectDataEndpoints.InvoiceLinkWriteDecisionAsync` and `LinksNotRead`;
- the `InvoiceLinks` parameter of `ExternalDataService.DeleteTarget`, and the third element of `GetInvoiceParentsAsync`;
- the optional `invoiceFault` parameter of `SecureRootsFilter` and `RegardingInvoicesVisibleAsync`;
- `ExternalMessageEndpoints.InvoiceWorkAssignmentColumn`;
- the invoices module work assignment dimension, root lookup and readable column.

1. **Existing.** `HideInvoiceLinkedRowsAsync` is the one invoice decision for rows; the delete-own `DeleteTarget` read is the one record read before a delete; `SecureRootsFilter` is the message filter; the module descriptor already scopes through matter and project.
2. **Extension.** Each piece extends these: the write decision calls the row filter on one row; the delete target read selects the link columns it already lacked; the fault flag is a BCL `StrongBox<bool>` parameter, not a new type; the module gains one dimension of the existing kind.
3. **Cost of doing nothing.** A partner could edit or delete a to-do, event or document of another firm's invoice by id; a faulted firm read on a thread or feed would look complete; an invoice under a work assignment would be unreachable to a caller who reads it only through that work assignment.

## Fix round 3

### Q2: a single invoice-linked read whose decision faulted is the 503

A single-record read of an invoice-linked row answers the `InvoiceUnreadableReasonCode` 503 when the invoice decision could not be made, for the caller's firm read and for the per-invoice decision (`InvoiceVisibilityResult.IsFaultedFor(invoice)`: the whole result faulted, or that invoice's own `ReadFailed`). A decided hidden invoice keeps the route's uniform refusal. Collections keep hiding the row (and say `Truncated` when the whole read faulted).

`ExternalDataService.DecideInvoiceLinkedRowAsync` is the one row decision (Visible, Hidden, Faulted) and shares the single seam ask (`AskInvoiceDecisionAsync`) with the list filter. A row is Hidden when any link is unreadable or decided hidden, and Faulted only when every non-visible link is a failed decision.

Single-record invoice-linked read routes (all changed or confirmed):
- `GET /{projects|matters|workassignments|servicerequests}/{id}/documents/{documentId}/content` and `/versions`: `IsDocumentReadableUnderAsync` now uses the row decision.
- `GET /invoices/{id}/documents` (list), `.../{documentId}/content`, `.../versions`, and `POST /invoices/{id}/documents`: `InvoiceVisibleToAsync` reports the per-invoice fault, so the invoice-unreadable 503 follows the record and rights checks.
- `GET`/`POST /invoices/{id}/messages`: the invoice gate treats the per-invoice fault as the 503.
- `GET /invoice-submissions/{id}`: unchanged; it shares the list read and already answers its 503 on a faulted result. The production seam only ever reports a failed decision as a faulted result, so the per-invoice-only shape is reachable by a test stub, not by the live seam.
- Writes (to-do PATCH, delete-own) are outside this ruling and unchanged.

### Uniform refusal for single to-do routes

`PATCH /todos/{id}` with an id that does not exist answered 404 while every other refusal is the uniform 403. A missing id now answers `DenyTodoWrite`, byte-identical to the answer for a to-do outside the caller's set. `GET /projects/{id}` answered a distinct 404 for a stale grant; it now answers the same 403 as no Read.

Route census (single-record routes in `ExternalProjectDataEndpoints.cs`):

| Route | Missing id vs an id outside the caller's set | Changed |
|---|---|---|
| `PATCH /todos/{id}` | was 404 vs 403 | Yes, both 403 |
| `GET /projects/{id}` | was 404 (stale grant) vs 403 | Yes, both 403 |
| `DELETE /todos/{id}`, `/events/{id}`, `/documents/{id}` | uniform delete-own 403 | No (`ExternalDeleteOwnTests.AbsentRow_AnswersExactlyAsSomeoneElsesRow`) |
| `DELETE /servicerequests/{id}` | uniform delete-own 403 | No (`ExternalDeleteServiceRequestTests`) |
| `GET .../documents/{documentId}/content`, `/versions` (every root and invoices) | uniform 403 | No |
| `POST` creates, record lists | out-of-scope parent is 403 | No |

The `.ProducesProblem(404)` metadata that remains on other routes in the file documents no response the code returns. `/data/record/{entity}/{id}` answers 404 only for an id inside the caller's own accessible set that no longer exists; it was not changed.

### An absent link column is not "no link"

`ExternalDataService.InvoiceLinksOf(table, Web API row)`: a link column that is ABSENT from the row reads as `UnreadableLink` (never visible); an explicit JSON null is "no link". The read's `$select` is no longer the boundary. The FetchXML overload keeps "absent = no link" (FetchXML omits a null attribute) and is pinned: `ExternalInvoiceLinkedChildListTests.EveryFetchXmlChildModule_ProjectsEveryRegistryLinkColumn` runs every registered child module with invoice links through the real fetch and asserts the executed FetchXML names every `InvoiceLinks.LinkColumnsOf(table)`.

The thread read selects every invoice link column of a communication: `CommunicationThreadReadService.ExternalSelect` appends `InvoiceLinks.Tables["sprk_communication"].ValueColumns`. Test doubles now answer a selected lookup that is not set as an explicit null (`FakeDataverse`, `AppOnlySelectProjection`), and can mask a column (`MaskedColumns`, an omit set) to prove a column that was not returned hides the row.

### Root typing

`ExternalModuleRegistry.RootTypeOfScopeAttribute` is the one mapping from a root lookup to its root type; `RootLinks` uses it, and now types `sprk_regardingworkassignment`. `Register` refuses (`ValidateRootDimensionsAreTyped`) a module with the secure-roots rule that scopes by, or declares as an extra root lookup, a lineage root lookup that `RootLinks` does not type. The invoices module's duplicate `sprk_regardingworkassignment` root lookup is removed: it is a scope dimension and is listed once in `RootRuleAttributes`; projection, validation and the secure work assignment test are unchanged (tests below).

### Known limit

- A deactivated work assignment is excluded where the caller's set is built (`AccessibleRecordSetService.cs:629-641`); at the route it is a work assignment outside the caller's set, which the out-of-set route tests cover.

### Hygiene

- `RedactRootInvoiceLinksAsync` carries the invariant comment that the regarding forms are stripped only when the pair names a hidden invoice.
- `ExternalMessageEndpointsTests` thread fault test: the ordered assertion is restored (the route documents oldest first, so the order is a contract).
- `ExternalInvoiceLinkedChildListTests`: the filter test that drove a whole-result fault under a per-invoice name is renamed `TheFilter_AFaultedDecision_HidesThatRow_AndSaysTruncated`.
- The `ExternalDataService` document-parent doc comment names matter, project and work assignment.

### Tests (fix round 3)

New:
- `ExternalInvoiceLinkedDocumentRouteTests`: `APerInvoiceFault_OnAnInvoiceLinkedDocument_IsThe503` (must fire), `ADecidedHiddenInvoice_OnADocument_KeepsTheUniformRefusal` (must not fire), `ADocumentReadWithoutALinkColumn_IsRefused` (must fire, a masked link column).
- `ExternalRecordDocumentRoutesTests.Task130_0_Q2_*` (invoice document routes) and `ExternalMessageEndpointsTests.Task130_0_Q2_*` (invoice thread, 503 and the uniform refusal).
- `ExternalInvoiceLinkedTodoUpdateTests.AMissingTodoId_IsTheSameRefusal_AsATodoOutsideTheCallersSet`; `ExternalTodoScopeTests.PatchExternalTodo_WhenTodoDoesNotExist_*` now expects the 403.
- Absent link column: `ExternalInvoiceLinkedChildListTests.TheFilter_AnAbsentLinkColumn_*`, `ExternalInvoiceLinkedDeleteTests.ARowReadWithoutALinkColumn_*`, `ExternalMessageEndpointsTests.Task130_0_Fix3_*` (the thread read), plus the FetchXML pin `EveryFetchXmlChildModule_ProjectsEveryRegistryLinkColumn`.
- `ExternalModuleRootTypingTests` (registration guard: must fire for an untyped root dimension and for a wrongly typed extra lookup; must not fire for the work assignment dimension, a declared extra lookup, a module without the rule, and the production registry).

Test doubles: `InvoiceHidingStub.PerInvoiceFault`; `FakeDataverse` and `AppOnlySelectProjection` answer a selected unset lookup as an explicit null and can mask a column; the in-memory document service and the message-send read-back set explicit nulls.

Results: the filtered external-access suites (7130 tests) pass; `tests/Spaarke.ArchTests` passes in full (2181 of 2181).

Seeding proofs (each file restored byte-identical, same SHA-256):
- The five production files (`ExternalDataService`, `ExternalInvoiceVisibility`, `ExternalProjectDataEndpoints`, `ExternalMessageEndpoints`, `CommunicationThreadReadService`) set back to the round 2 version: 15 of 15 new must-fire tests fail (single-record 503s, the missing to-do 403, the absent link cases, the thread select).
- `ExternalModuleRegistry` set back to the round 2 version: 5 of 8 guard and typing tests fail.

## Fix round 3b

### A document root link column that was not read is unreadable

`ExternalDataService.DocumentRootLinks` is the one input to the secure-roots rule for a document's six root link columns. Before this round a root link column that was ABSENT from a Web API row read as "not filed to that root", so a read whose select left a column out skipped that root's check. Now an absent column reads as `DocumentRootLinks.Unreadable` (a value that is not a GUID, which the secure-roots rule already withholds); an explicit JSON null is still "not filed".

- `DocumentRootLinks.From` (single-row Web API reads): an absent column is `Unreadable`.
- The list path deserialises into the typed `DocumentRow`, where absent and null were indistinguishable. Each root link setter on `DocumentRow` now records that its column was present (an explicit null included), and `DocumentLinksOf` reads an unrecorded column as `Unreadable`. No new type or service.
- FetchXML: no FetchXML path feeds `DocumentRootLinks` (`From` and `DocumentLinksOf` are the only producers, and every caller reads Web API JSON; the documents module reads take their root links from `ExternalModuleDescriptor.RootLinks`, which `EnsureProjected` already projects server-side). So no FetchXML pin is needed for this type; the select pins below are on the Web API reads.

Every caller and its select (all select all six columns, so visible documents do not start failing; none needed a fix):

| Caller | Read | Select source | Pinned by |
|---|---|---|---|
| `GetDocumentsAsync` (document list, every parent kind) | Web API list | `BuildDocumentListUrl` names the six columns | `TheListSelect_NamesEveryRootLinkColumn` (5 parent kinds) |
| `GetDocumentParentAndNameAsync` (content and versions routes) | Web API single row | `DocumentRootLinks.SelectColumns` | `TheScopingRead_SelectsEveryRootLinkColumn`, through the real fetch (5 parent kinds) |
| `CheckLiveParentsAsync` (document delete-own) | Web API single row | `BuildLiveParentCheckUrl` adds `DocumentRootLinks.SelectColumns` | `TheDeleteOwnLiveParentRead_SelectsEveryRootLinkColumn`, through the real fetch |
| `ExternalInvoiceVisibility.DecideSourceDocumentsAsync` (invoice source documents) | Web API by id | `SourceSelectColumns` includes `DocumentRootLinks.SelectColumns` | `TheInvoiceSourceDocumentRead_SelectsEveryRootLinkColumn` (and the existing `TheSourceRead_SelectsEveryColumnTheRuleReads`) |
| `CreateDocumentAsync` response row | `return=representation` | not a rule input (mapped to the wire DTO only) | n/a |

Test doubles that hand-built a document row now return the root link columns as explicit nulls, as the Web API does for a selected unset lookup: `InvoiceWorld.Document` and `ExternalDataServiceTests.DocumentRowJson`.

### Uniform refusal on `GET /api/v1/external/api/dataverse/record/{entity}/{id}`

A record inside the caller's own accessible set that no longer exists answered 404 (`DV_RECORD_NOT_FOUND`); an id outside the set answered 403 (`sdap.external.module.record_not_in_accessible_set`). The `RecordNotFoundException` branch of `GetScopedRecordCoreAsync` now answers the same `ProblemDetailsHelper.Forbidden(DenyRecordNotAccessible)` as the Tier-2 gate, so a missing id, a record gone from the set and an out-of-set id have one status and one body. A faulted read stays the distinct 500; a 503 for a faulted invoice decision is not part of this route (a root's invoice link is redacted there, not refused) and is unchanged everywhere. The `.ProducesProblem(404)` on the route is removed.

Census of `ExternalModuleDataEndpoints.cs` (the only file with a `/data/record` route; the to-do and project routes were censused in round 3):

| Route | Before | After | Changed |
|---|---|---|---|
| `GET /api/dataverse/record/{entity}/{id}` out of the set | 403 `record_not_in_accessible_set` | same | No |
| `GET /api/dataverse/record/{entity}/{id}` missing id (outside the set) | 403 (same as above) | same | No |
| `GET /api/dataverse/record/{entity}/{id}` in the set, record gone | 404 `DV_RECORD_NOT_FOUND` | 403 `record_not_in_accessible_set` | Yes |
| `GET /api/dataverse/record/{entity}/{id}` entity with no module | 403 `module.not_registered` | same | No (names an entity, not a record) |
| `GET /api/dataverse/record/{entity}/{id}` column outside the allow-list | 400 | same | No (input check, identical for every caller) |
| `GET /api/dataverse/record/{entity}/{id}` read fails | 500 | same | No (distinct by design) |
| `POST /api/dataverse/fetch` | rows scoped to the set (never a per-record 404) | same | No |
| `GET /api/dataverse/metadata/{entity}` | 404 only for a registered module whose entity has no metadata | same | No (schema, not a record) |
| `GET /api/dataverse/savedquery/{id}`, `/savedqueries/{entity}` | one uniform 404 for unknown, unregistered and denied views | same | No (view definitions, already uniform) |

External SPA (`src/client/external-spa`): no caller depends on a 404 from the record route. `RecordDetailModal` reads a record through the id-filtered module fetch (`RecordDetailFields.tsx` notes the record route is not used) and treats no row as denied. The only path to the route is `gridDataverseClient.retrieveRecord` (a delegating adapter over `BffDataverseClient.retrieveRecord`, which throws the mapped error for any non-2xx); no external-spa source calls it, and its tests (`backend-selection.test.ts`) assert only the URL. The 404 handling in `RecordDetailModal`, `RecordMessages` and `ProjectPage` belongs to other routes (messages, project). The `notfound` UI state in `RecordDetailModal` is now reachable only by a malformed or missing route id, not by this route.

### One notion of an invoice decision fault (A)

Production sets a per-invoice `InvoiceHiddenReason.ReadFailed` only together with `InvoiceVisibilityResult.Faulted`, but the result type allows one without the other, and the sites that asked the whole-result `Faulted` would have read a per-invoice failure as "hidden". `InvoiceVisibilityResult.AnyFaulted` (the whole result faulted, or any invoice's own decision could not be made) is now the one question. A decided "not visible" invoice is not a fault.

| Site | Reads | Before | After |
|---|---|---|---|
| `ExternalDataService.HideInvoiceLinkedRowsAsync` (lists of invoice-linked children) | `Truncated` | whole-result fault only | also a row dropped for an invoice whose own decision failed (`IsFaultedFor`) |
| `ExternalMessageEndpoints` thread filter (`invoiceFault`) | `Truncated` | `Faulted` | `AnyFaulted` |
| `ExternalModuleDataEndpoints.HideInvoiceRowsAsync` (invoices module list) | `Truncated` | `Faulted` | `AnyFaulted` |
| `ExternalInvoiceSubmissionEndpoints` submission read and list, submission decision and invoice decision | 503 | `Faulted` | `AnyFaulted` |
| `ExternalModuleDataEndpoints.RedactRootInvoiceLinksAsync` | link shown | visible and not whole-result faulted | visible and not `AnyFaulted` |

Left on the per-invoice question already: single invoice-linked reads (`DecideInvoiceLinkedRowAsync`, `IsFaultedFor`, round 3). Left on the whole-result question on purpose: `AskInvoiceDecisionAsync` (it classifies the ask, not a row) and the assistant plane.

### `/data/record/{entity}/{id}` gate (B)

`ExternalModuleDescriptor.IsRecordAccessible` (`ExternalModuleRegistry.cs:288-295`) passes only an id that is in one of the module's accessible dimension sets, and the route calls it before any read (`ExternalModuleDataEndpoints.cs:739`). The sets hold root ids (matter, project, work assignment) and the doc comment says a child's own id is never in one (fails closed). So this route serves root tables only, an invoice or an invoice-linked child id never reaches the read, and no per-invoice decision is needed there. A root row it returns goes through `RedactRootInvoiceLinksAsync` (row above). No code change.

### Secure-roots modules and the lineage map (C)

`ValidateRootDimensionsAreTyped` returned early for a table that is not a key of `SecureChildLineage.Children`, so a secure-roots module on such a table was never checked. The early return is now only for a module without the rule. A secure-roots module on a table outside the lineage map is refused at registration: the rule cannot be applied to such a table anyway (`ChildRowRoots.Of` refuses it on the first row), so this moves a per-row failure to registration. Every production module with the rule is in the map (the production registry test builds them all).

### Known limit

- A live-parent lookup column (`ExternalMultiRootRule.LiveParentsOf`, Web API overload) that is absent from a row still reads as "not filed there". Every Web API read that feeds it selects `LiveParentSelect(table)`, but it is not pinned the way the root links now are. Not changed in this round (outside the two requested fixes); reported for a follow-up.

### Tests (fix round 3b)

New:
- `ExternalDocumentRootLinkReadTests` (integration/contract): single document refused when a root link column is not returned (30 cases: 3 routes by content and versions by the 5 non-route columns, each with a served control); explicit nulls served (6); list hides the rows when a column is not returned (15, each with a listed control); explicit nulls listed; `DocumentRootLinks.From` absent versus null; the four select pins.
- `ExternalModuleRecordRefusalTests` (integration/auth): missing, gone and out-of-set give an identical status and body (and the read counts); an existing in-set record is returned (must not fire); a failed read stays 500 (must not fire).

Existing tests changed because the behaviour changed on purpose:
- `ExternalModuleDataContractTests.ModuleRecord_WhenSelectIsReadable_PassesTheColumnCheckAndReachesTheRead` and `ModuleRecord_MatterGrantRowWithALevel_PassesTheTier2Gate`: used the 404 as the proof that the request reached the read. They now expect the 403 and assert the read count on the fixture's `IDataverseService` double (1 past the gate); the no-level test also asserts 0 reads.
- `ExternalDataServiceTests.DocumentRowJson` and `InvoiceWorld.Document`: explicit null root links (above).

Also new in this round, for the fault notion and the registry check:
- Per-invoice decision fault (`InvoiceHidingStub.PerInvoiceFault`, the result not faulted as a whole), each with a decided-hidden control: the submission read and list (503 on both), the matter thread (`Truncated`), the invoices module list (`Truncated`), and the invoice-linked child filter (`Truncated`).
- `ExternalModuleRootTypingTests`: a secure-roots module on a table outside the lineage map is refused; the same module without the rule registers.
- `Issue963_ExternalDataPagingTests`: its document rows now carry the root link columns as explicit nulls (test double only).

Counts: BFF build 0 errors, 0 warnings. `Spaarke.ArchTests` (full): 2181 passed, 0 failed. Broad filtered run of `Sprk.Bff.Api.Tests` (external access, unified access control, finance, document, dataverse, invoice submission): 17513 total, 17504 passed, 5 skipped, 4 failed. Three of the four were 3-minute client timeouts under machine load (two document identity tests and the external-access admin route characterization test); the fourth was the Issue963 test double above. The re-run of those classes together with every new and changed class (706 tests) passed.

Seeding proofs (production files restored to `ed11b30f95`, plus one `Unreadable` constant so the tests compile; 104 tests of the new and changed classes: 55 failed, 49 passed). Failed before, pass after: single document with a root column not returned (30), document list with a column not returned (15), `DocumentRootLinks.From` absent versus null, `/record` missing, gone and out-of-set (1 refusal test and 3 contract tests that expect 403), root typing outside the lineage map, and each per-invoice fault test (thread, child filter, module list, submission). The must-not-fire controls passed in both states. Files restored byte-identical (SHA-256 checked) and touched:

| File | SHA-256 |
|---|---|
| `ExternalDataService.cs` | `179f0f3a4ffae8d81b2386d07f95384a7344fddb599517a0b7a040c78a146676` |
| `ExternalInvoiceVisibility.cs` | `3790a84e4fe660e9468e77ca3ab648645a717dd5306eb1c1abe6f9ddbfe4bfd4` |
| `ExternalModuleRegistry.cs` | `bb0547ba4c1adb673d458ba5614985de1f9970782cb6fdbfed6feb61f40f6450` |
| `ExternalModuleDataEndpoints.cs` | `0711f22c429b86344ca8b142cf1b7d3c2e6d465fb8360d9c58d8bd1342e40753` |
| `ExternalMessageEndpoints.cs` | `825ee8132ad0d2c31313e70636e692927c1e0ba7649ac94b3892d6d7a2a4cb87` |
| `ExternalInvoiceSubmissionEndpoints.cs` | `a7d98508ed3492c2fc6913fa4accf112ca2cdbadf0bd1fcbff15ea56dc25e980` |

## Fix round 3c

### A live-parent lookup column that was not read is unreadable

`ExternalMultiRootRule.LiveParentsOf` (the Web API / `JsonElement` overload) is the one reader of a child row's parent lookups (`LiveParentLookupsOf(table)`, as `_<column>_value`). Before this round a column that was ABSENT from the row (or a `null` row) read as "not filed there", so a read whose select left a column out skipped that parent's live-root check. Now an absent column enters as `ExternalMultiRootRule.UnresolvedParent` (not a GUID, the same value a malformed lookup takes), which the secure-roots rule already withholds; an explicit JSON null is still "not filed". No new path: the existing malformed-link handling does the refusing.

- Single reads and writes (`CheckLiveParentsAsync` for to-do PATCH and to-do, event and document delete-own; the document download and versions scoping read): the row is not readable, so the route gives the same refusal it gives for a document or row that is not under the record.
- Lists (to-dos, events, documents, messages): the row is hidden.
- FetchXML overload (module rows): unchanged, "absent = not filed", because the SDK omits a null attribute so absence cannot be told from null. It is reached only with the columns projected: `ExternalModuleDataEndpoints.cs:351` (the module fetch) and `ExternalMessageEndpoints.cs:803` (the record gate) project `ExternalModuleDescriptor.RootRuleAttributes`, which includes `LiveParentAttributes` (`ExternalModuleRegistry.cs:205`, from `LiveParentLookupsOf(RecordEntity)`). Pinned for every secure-roots module.

Callers and their selects (every select already named every column; none needed a fix; all are now pinned by `EveryCaller_SelectsEveryLiveParentColumn_OfItsTable`, whose expected set is derived from `LiveParentLookupsOf(table)`, so a lookup added to the lineage map breaks the pin until the select carries it):

| Caller (`LiveParentsOf` use) | Table | Select source | Behaviour test |
|---|---|---|---|
| `GetTodosAsync` (`ExternalDataService.cs` ~1725, `ChildRowRoots.Of` ~1727) | `sprk_todo` | `BuildTodoListUrl` (`LiveParentSelectSuffix`) | `TodoList_ColumnNotReturned_HidesTheRow` (per column) |
| `ReadableEventsAsync` (~1844, ~1846) | `sprk_event` | `BuildEventsQuery` (`LiveParentSelectSuffix`) | `EventList_ColumnNotReturned_HidesTheRow` |
| `GetDocumentsAsync` (`DocumentRootLinks.LiveParents`, `RuleInput`) | `sprk_document` | `BuildDocumentListUrl` (`LiveParentSelectSuffix`) | `DocumentList_ColumnNotReturned_HidesTheRow` |
| `GetDocumentParentAndNameAsync` then `IsDocumentReadableUnderAsync` (`ExternalProjectDataEndpoints.cs` ~1583) | `sprk_document` | inline column list (`LiveParentSelect`) | `SingleDocument_ColumnNotReturned_IsNotReadableUnderTheRecord`, and the content / versions routes (`ExternalLiveParentColumnDocumentRouteTests`) |
| `CheckLiveParentsAsync` (~2659, ~2660) | `sprk_todo`, `sprk_event`, `sprk_document` | `BuildLiveParentCheckUrl` | `LiveParentCheck_ColumnNotReturned_IsWithheld`, PATCH to-do, DELETE own to-do / event |
| `ExternalMessageEndpoints.SecureRootsFilter` (`:530`, `:606`) | `sprk_communication` | `CommunicationThreadReadService.ExternalSelect` (thread read and notification feed; now `internal` so the pin calls it) | `MessageFilter_ColumnNotReturned_IsWithheld_ExplicitNullsAreListed` |
| `ExternalInvoiceVisibility.DecideSourceDocumentsAsync` (`:338`, `:345`) | `sprk_document` | `SourceSelectColumns` | `Unreadable_ALiveParentColumnTheSourceReadDidNotReturn_HidesTheInvoice` |
| `ExternalModuleRegistry.LiveParents` / `ChildRowRoots.Of` FetchXML (`:259`, `:374`) | module `RecordEntity` | `RootRuleAttributes` (server-side projection) | `EveryFetchXmlModule_ProjectsEveryLiveParentAttribute`; `FetchXmlOverload_AbsentAttribute_IsNotFiled` (must not fire) |

Test doubles changed to return explicit nulls for unset live-parent lookups: `ExternalLiveParentRuleTests.TodoUnder` and the new `CommunicationRow` helper (the message-filter rows), and `InvoiceWorld.Document` in `ExternalInvoiceVisibilityTests`. `ExternalRootEventReadTests.FakeDataverse` already answered a selected unset lookup as an explicit null (round 3), so the route-level tests needed no change.

### Known limits (reported, not changed)

- The stamp columns (`_sprk_regardingproject_value` and the other two root stamps on a to-do, event or communication) are still read with "absent = not filed" (`ExternalDataService.CheckLiveParentsAsync` `RawLookup`, `CommunicationThreadReadService.RootStamp`, and the typed `TodoRow` / `EventRow` properties). Every read selects them; a pin for them is a follow-up in the same class.

### Tests (fix round 3c)

New, all in `ExternalLiveParentColumnReadTests.cs` (integration/contract, compiled into `Sprk.Bff.Api.Tests`) unless noted:
- `LiveParentsOf`: an absent column is unreadable, one column at a time, for to-do, event, document and communication, and a null or empty row reads every column as unreadable (must fire); explicit nulls name no parent, a GUID is kept, a non-string is still malformed (must not fire).
- Lists: a to-do, event or document row whose read did not return one live-parent column is hidden, per column (7, 6 and 8 columns), each with a listed control.
- Single and write paths: `CheckLiveParentsAsync` for to-do, event and document is withheld for every column; PATCH to-do and DELETE own to-do and event answer 403 and write nothing, with a control that applies with explicit nulls; the document scoping read is not readable under the record (per column); the content and versions routes answer the one "not under this record" refusal (`ExternalLiveParentColumnDocumentRouteTests`, per column and route, with a served control and an explicit-null control).
- Message filter (the thread read and the notification feed share it): a communication row without a live-parent column is withheld; explicit nulls are listed.
- Invoice source documents (`ExternalInvoiceVisibilityTests.Unreadable_ALiveParentColumnTheSourceReadDidNotReturn_HidesTheInvoice`): a source whose read left out one column hides the invoice (`RootUnresolved`); a source with explicit nulls is visible.
- Pins: `EveryCaller_SelectsEveryLiveParentColumn_OfItsTable` drives every Web API caller through its real URL builder or fetch (to-do and event lists by root kind, document list and scoping read by parent kind, the three live-parent checks, both `ExternalSelect` forms, the invoice source columns) and asserts the select contains `LiveParentLookupsOf(table)` as `_<column>_value`; `EveryFetchXmlModule_ProjectsEveryLiveParentAttribute` does the same for every secure-roots module's `RootRuleAttributes`; `FetchXmlOverload_AbsentAttribute_IsNotFiled` is the must-not-fire control for the FetchXML overload.

Test doubles that hand-built rows now return explicit nulls for unset live-parent lookups: `ExternalLiveParentRuleTests` (`TodoUnder`, `CommunicationRow`), `InvoiceWorld.Document`, `ExternalDataServiceTests.DocumentRowJson`, `Issue963_ExternalDataPagingTests` (documents, to-dos, events), `ExternalMessageSendTests.AsDataverseRow` (the thread lookup is left to `WithOpenThread`), `ExternalRecordDocumentRoutesTests.NoLinks`, and two `DocumentRootLinks` literals in `ExternalMultiRootDocumentRouteTests` and `ExternalDocumentRootLinkReadTests`.

Counts: BFF build 0 errors, 0 warnings. `Spaarke.ArchTests` (full): 2181 passed, 0 failed. Filtered run of `Sprk.Bff.Api.Tests` (external, multi-root, live-parent, communication, document, invoice, unified access, finance, Issue963): 19339 total, 19330 passed, 9 skipped, 0 failed. (A first run before the last test-double fixes failed 27 and an earlier one 137, all test doubles that returned rows without the live-parent columns; none was a timeout.)

Seeding proof: `ExternalMultiRootRule.cs` restored to `17a283c00c` and touched; the new tests (69 incl. the invoice source test): 54 failed, 15 passed. The passing 15 are the select pins (the selects were already complete), the FetchXML controls and the explicit-null / all-present controls. After restoring the committed file (SHA-256 `5e18ca8a9eb74616ff0d1bd4db2230d1854161cd6d4c0b42364e6154594b1242`) all pass.

## Fix round 3d: class closure

The closing sweep for one class: a lookup column (`_<name>_value`) read from a Web API (JSON) row that feeds an access decision for an external caller.

### The rule

A Web API row lists a lookup it was asked for as an explicit JSON `null` when it is not filed. A column the row does not carry was not read, which says nothing about whether it is filed. An absent column is treated as unreadable, using the decision's own fail-closed value; an explicit null is "not filed". SDK and FetchXML rows omit a null attribute, so there absence and null cannot be told apart; "absent = not filed" is kept there only where the query projects the attribute (cited below) and a test pins the projection. A typed row records whether each column was read (the property setter runs for a present property, an explicit null included) and reads an unread column as unreadable.

Shared helpers (`ExternalMultiRootRule.cs:100-123`, no new type or service): `UnreadableLookup` (`"unreadable-lookup"`, not a GUID, so the existing malformed-link handling of the multi-root rule withholds the row), `LookupOf(row, column)` (null row or absent column: unreadable; explicit null: not filed; string: the value; anything else: unreadable) and `StampOf(columnRead, value)` for the typed rows.

### Census A: changed in this round (Web API rows)

| Site (file:line) | Columns | Decision it feeds | Absent before | Absent now |
|---|---|---|---|---|
| `ExternalDataService.TodoRow` (`:196`, `RootStamps()` `:221`) and `GetTodosAsync` (`:1768`) | the three root stamps (`_sprk_regardingproject/matter/workassignment_value`) | to-do list: the multi-root rule over the row's roots | not filed | unreadable stamp: the row is withheld from the list |
| `ExternalDataService.EventRow` (`:251`, `RootStamps()` `:272`) and `ReadableEventsAsync` (`:1883`) | the three root stamps | event list: same rule | not filed | the row is withheld from the list |
| `ExternalDataService.GetTodoRootAsync` (`:1683`, stamps at `:1703`) | the three root stamps | which root a single to-do belongs to (content, PATCH and delete scoping) | classified from the stamps that were present | `TodoRootKind.Ambiguous` (the existing deny state, refused on every route; never a 500) |
| `ExternalDataService.CheckLiveParentsAsync` (`:2666`, reads `:2690-2692`) | the three root stamps | live-parent check before PATCH to-do and DELETE own to-do or event | not filed | `LiveParentDecision.Withheld`: 403, nothing written |
| `ExternalDataService.ParseDeleteTarget` (`:3419`, select `BuildDeleteTargetUrl` `:3407`) | the table's parent lookups | delete-own scoping of a to-do, event or document | no parent | `HasUnreadableParentLink`: the delete is refused (blank or explicit null is still "no parent") |
| `ExternalDataService.GetInvoiceLookupsAsync` (`:1295`, `:1314`) | `_sprk_matter_value`, `_sprk_project_value`, `_sprk_regardingworkassignment_value` of an invoice | the record gate's invoice parents | omitted (not filed) | passed on as `UnreadableLookup`; only an explicit null is omitted |
| `ExternalInvoiceVisibility.LinkOf` (`:1134`) and `IsSet` (`:889`) | invoice source document, communication, submission and attachment links | invoice visibility (source documents, submissions, stored files) | no link | `ExternalDocumentNarrowing.UnreadableLink`, the existing "undecidable" value (the invoice is hidden); an absent column counts as set |
| `ExternalDocumentNarrowing.JsonLink` (`:214`) | related communication and parent document links | assistant-plane document narrowing | no link | `UnreadableLink` |
| `ExternalCallerCommunicationGuard.LinkOf` (`:242`) | the source links of an analysis, and communication and attachment ids | assistant-plane source-document decision | no link | `UnreadableLink` (the row is undecidable and hidden) |
| `NoAccessListReader.ParseRows` (`:810`, from `QueryChunkAsync` `:789`) | `RowSelect` (subject and object columns of a deny entry) | the deny-list veto over an external caller's accessible set | an entry read without a column enforced less | a row that lacks any `RowSelect` column fails the chunk closed (the existing `NoAccessListResult.FailClosed`) |
| `CommunicationThreadReadService.RootStamp` (`:525`) | the three root stamps of a message | message and thread visibility (`RootsOf` `:507`) | not filed | `"malformed"`, which the existing filter withholds |

### Census B: already closed before this round (verified, no change)

`DocumentRow` (`ExternalDataService.cs:147`, round 3b: records which root links were read); `DocumentRootLinks.Raw` (3b); `ExternalMultiRootRule.LiveParentsOf`, Web API overload (3c); `InvoiceLinksOf` (`:2011`, round 3); `GetDocumentParentAndNameAsync` (`:996`, 3c: an absent parent id is "not under the record"); `OrganizationReferenceRow` (`ExternalParticipationService.cs:1420`, `EveryLookupReturned` `:1455`, task 051, also what `InvoiceSubmissionService.LinkOf` `:2062` consumes); `GetNonSecureRootsAsync` (`:2745`: an asked root with no row or no boolean flag counts as secure and the read is not `Complete`); `CommunicationThreadReadService.ReadVisibleCommunicationAsync` (`:1321`: an absent thread id is empty, not in the open-thread set, so the message is hidden).

### Census C: reads that only add a match, a right or a grant

An absent column there gives fewer matches, rights or grants, never more access. `AssignedAccessStore.cs` (ledger row, `LinkCandidateRow`, `ReadRootAsync`: assigned-access grants); `CommunicationThreadReadService` `BuildParticipantClauseAsync` (`:1028`, a positive filter), `LoadAttachmentsAsync` (attachments of already-authorised messages) and `ParseMessageRow` (an absent thread id is empty and so hidden; an absent author never matches "own"); `ContactIdentityStore.cs` (`FindSystemUsersLinkingAsync` `:1726`, `ParseSystemUserRow` `:1481`: an absent contact link is unlinked, fewer bindings); `ExternalDataService.GetInvoiceParentsAsync` (`:1267`), `GetRecentGrantsAsync` (`:1395`), `GetServiceRequestRequesterAsync` (`:1030`), `GetServiceRequestRequesterAndStateAsync` (`:1076`), `GetServiceRequestsRequestedByAsync` (`:1477`) (a requester or parent match grants only to the matching party); `ExternalGrantRow` (`ExternalGrantLifecycle.cs:80`), `ExternalAccessRow` (`ExternalParticipationService.cs:2185`), `ContactOrgRow` (`:2214`), `ColleagueMembershipRow` (`:2240`) (grant and membership derivation).

### Census D: not an access decision (the decision each one sits beside)

- `ExternalDataService.AccessLinkRow` (`:305`) and `ContactRow` (`:283`): the participant list of a record shown to a manager; the decision beside it is the manager's can-manage-access check, which is not read from these rows.
- `ExternalProjectDtos.cs` (`ExternalContactDto`, `ExternalDocumentDto`, `ExternalEventDto`, `ExternalTodoDto`): response DTOs serialised to the SPA; never read back from a Dataverse row.
- `ExternalModuleDataEndpoints.RedactRootInvoiceLinksAsync` (`:516`): redaction of invoice-link values in returned module rows; the decision is the invoice visibility (`HideInvoiceLinkedRows`), which is separately fail-closed; an unreturned pair is not served.
- `ProvisionProjectEndpoint.RootRow` and `UnsecureProjectEndpoint.SecurityRow`: secure-designation provisioning and removal on the management plane behind `DelegationRuleFilter`; an empty flag is refused as "could not tell" (task 150).
- `RecordAccessGateEndpoint.OwnerRow`: owner facts in the can-manage-access answer (informational; `DelegationRuleFilter` is the gate).
- `SecuredColumnReadProof` (`SdkColumnReadPermissionSource`): the field-security proof reads the application user id and column-permission names; not a lookup on a record.
- `GrantExpiryReminderJob.DueGrant`: the expiry reminder job; grants nothing.
- `SecureRecordIsolationCensus`: a diagnostic census; reports, grants nothing.

### Census E: SDK and FetchXML rows (absent = not filed, with the projection proof)

| Read | Projection (file:line) | Pin |
|---|---|---|
| Module rows and the record gate (`ExternalModuleRegistry.LiveParents`, `ChildRowRoots.Of`, `TryGetAttributeId` `:443`) | the module fetch `ExternalModuleDataEndpoints.cs:351` and the record gate `ExternalMessageEndpoints.cs:803` project `RootRuleAttributes` (`ExternalModuleRegistry.cs:195`, which includes the live-parent attributes) | `EveryFetchXmlModule_ProjectsEveryLiveParentAttribute` (3c) and, new, `TheRecordGate_ReadsEveryRuleAttribute_OfEverySecureRootsModule` |
| Documents module that narrows (`ExternalDocumentNarrowing` FetchXML overload) | `ExternalModuleDataEndpoints.cs:348-352` adds `NarrowingAttributes` (`ExternalModuleRegistry.cs:224`) | new: a documents module that narrows projects every attribute the narrowing reads |
| `ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync` (`:1094`) | SDK `ColumnSet` names the hold and the firm; no firm hides the invoice under the own-firms rule | new: `TheFirmHeldInvoiceRead_ProjectsTheHoldAndTheFirm` |
| `ExternalAccessReconciliationJob` grant scan (`PlanGrantChange`) | `BuildGrantScanFetchXml` (`:1170-1181`) selects every root lookup, the organization and the issuer attributes | new: `TheGrantScan_ProjectsEveryLookupItReads` |

### Typed rows (every Web API row type in scope)

- `TodoRow` and `EventRow` (`ExternalDataService.cs:196`, `:251`): changed. Each root stamp's setter records that the column was read; `RootStamps()` returns `UnreadableLookup` for one that was not. The creator stamps (`sprk_createdbyperson`, `sprk_createdbycontact`) read as unmatched when unread, so the row is not "created by" anyone. The effect is per row: a list withholds that row, a single read or write is refused, no path gives a 500. Must-fire and must-not-fire tests for each (below).
- `DocumentRow` (3b) and `OrganizationReferenceRow` (051): already track presence; unchanged.
- `NoAccessEntryRow`: its list fails the chunk closed (`ParseRows`).
- `ExternalGrantRow`, `ExternalAccessRow`, `ContactOrgRow`, `ColleagueMembershipRow`, assigned-access ledger rows, `AccessLinkRow`, `ContactRow`, the provisioning, unsecure and owner rows, `DueGrant`: reviewed (Census C and D); an absent column there cannot widen access.
- `ContactOrganizationRow` (`ExternalOrganizationMembership.cs:168`): reported below.

### Callers and selects pinned (Web API)

`EveryStampReader_SelectsEveryStampColumn` drives every reader of the root stamps through its real URL builder or read and asserts the select contains all three stamp columns: `BuildTodoListUrl` and `BuildEventsQuery` (per root kind), `BuildLiveParentCheckUrl` and `BuildDeleteTargetUrl` (to-do and event), `GetTodoRootAsync`, and `CommunicationThreadReadService.ExternalSelect` (with and without content). `DeleteTargetRead_SelectsEveryLookupItsParseReads` asserts the delete-target select carries the creator stamps, every parent lookup its parse reads (found by removing each selected column and seeing the parse report it unreadable) and every invoice link column of the table. The 3c pin (`EveryCaller_SelectsEveryLiveParentColumn_OfItsTable`) keeps the live-parent columns, and the 3b/3c pins keep the document root links. The expected sets are derived from the registries (`LiveParentLookupsOf`, `LinkColumnsOf`, `RootRuleAttributes`), so a column added there breaks a pin until the select carries it.

### Source-scan guard

`tests/Spaarke.ArchTests/ExternalLookupAbsentReadGuardTests.cs`.

How it recognises a site (line comments stripped) in `Infrastructure/ExternalAccess/`, `Api/ExternalAccess/`, `Services/ExternalAccess/`, `CommunicationThreadReadService.cs` and `ExternalCallerCommunicationGuard.cs`: a call to `TryGetValue`, `TryGetProperty`, `ContainsKey`, `GuidValue`, `TryGuid` or `GuidOf` whose argument holds a `"_..._value"` literal (also `$"_{x}_value"`) or names a `string` constant whose initialiser does (transitively); a `[JsonPropertyName]` whose argument is such a literal or constant (a typed row property); and an SDK read (`GetAttributeValue<EntityReference|Guid|Guid?>`, `Attributes.TryGetValue`, `Attributes.Contains`, `.Attributes[`, `is EntityReference`, `case EntityReference`). Sites are grouped by file and enclosing member (brace-matched). Reads through the sanctioned fail-closed helpers (`LookupOf`, `RootStamp`, `RootLinkOf`, `LinkOf`) are not sites.

Ratchet: each group is in `ReviewedSites` with its exact count, a category (`FailsClosed`, `ProjectedAndPinned`, `PositiveOnly`, `NotAnAccessDecision`, `KnownOpen`) and a one-line reason (51 entries). An unreviewed group, a changed count, a stale entry and a changed number of `KnownOpen` entries (`KnownOpenSites = 4`) each fail.

What it misses (covered by the unit and contract tests above): a column name that reaches the call through a variable, a parameter or a data table rather than a literal or constant (for example the column arrays of `ExternalCallerCommunicationGuard` and the registry's attribute lists); a helper that re-implements `LinkOf` under another name; a file outside the scope list; and whether a reviewed site stays correct after its code changes inside a counted group (it fixes where reads are, not what they do).

Controls: an unreviewed read is caught; each spelling of a lookup read is caught (`Control_EachSpellingOfALookupRead_IsCaught`); a reviewed site passes; a changed count is caught; sanctioned and unrelated code is not a site; a comment is not a site; the enclosing member is the method.

### Known limits (reported, not changed; the four `KnownOpen` baseline entries and two adjacent items)

- `ExternalOrganizationMembership.ContactOrganizationRow` (`:168`, reads `:107`, `:148`): the revoke sweep (admin plane) skips a row that lacks the contact lookup, so a grant may stay for a later sweep. The select is a constant of the class. Follow-up: read an absent column as "cannot tell" and report it.
- `NoAccessEnforcementStore.EntryFrom` (`:108`) and `ReadOwnerAsync` (`:335`): admin-side share removal over deny entries; an entry read without a subject or object column enforces less. The select is a constant of the class.
- `CommunicationThreadReadService` `ThreadRegardingFields` (`:55`, read `:811`): the regarding record of an impersonated (Dataverse-permission) thread read; an absent column reads as a record-less thread. Dataverse applies the caller's own permissions on that path.
- Adjacent, not a lookup: `sprk_accesspermission` (a choice column) absent reads as `DocumentPermission.NotNarrowed` in `ExternalDocumentNarrowing.FromWebApi`. Every caller selects it. Changing it touches about 35 test doubles; recommended as a follow-up of the same shape.
- Outside the guard's scope: the impersonated regarding resolution (`CommunicationThreadReadService.ResolveRegardingFromRow`, `Api/Filters/CommunicationRecordAuthorizationFilter.cs`) and the internal reconciler `Services/Access/ChildAccessPermissionReconciler.cs`.

### Tests (fix round 3d)

New:
- `ExternalLookupAbsentColumnReadTests` (integration/contract): to-do list, event list, `GetTodoRootAsync` and the live-parent check for each stamp column not returned (must fire), each with a control that lists, names or applies the row; PATCH to-do refused with nothing written and applied with explicit nulls; invoice lookups (absent unreadable, explicit null omitted); the pins above.
- `ExternalLookupAbsentColumnUnitTests` (unit): `LookupOf` (must fire and must not fire), `StampOf`, `UnreadableLookup` withheld by the rule, `ParseDeleteTarget` (parents and invoice links one column at a time, a blank or null parent is none, a non-GUID is unreadable, creator stamps unread match nobody), `ExternalInvoiceVisibility.LinkOf`, document narrowing links, communication stamps (`RootsOf`), assistant-plane source documents, and the deny-list `ParseRows`.
- `ExternalDeleteOwnTests`: `ParseDeleteTarget_AParentColumnNotReturned_IsUnreadable` (the document delete target).
- `ExternalLookupAbsentReadGuardTests` (ArchTests): the guard and its controls.

Existing test changed because the behaviour changed on purpose: `ExternalInvoiceVisibilityTests.Unreadable_ALiveParentColumnTheSourceReadDidNotReturn_HidesTheInvoice` (`:252`). A narrowing link column (the parent document, the related communication) that the source read did not return is now caught by the source's own narrowing first, so the invoice is hidden as `SourceHidden`; any other live-parent column is still `RootUnresolved`. Both hide the invoice.

Test doubles changed to return explicit nulls for unset lookups: `Issue963_ExternalDataPagingTests` (to-do and event rows carry the other root stamps; document rows carry the narrowing columns), `ExternalMessageSendTests.AsDataverseRow` (stamps), `ExternalInvoiceVisibilityTests` (`InvoiceWorld`: source and communication links), `ExternalDocumentNarrowingTests` (related communication and parent document links), `InvoiceLinksTests` (its `Rows` double answers every selected column it was not seeded with as an explicit null), `DataverseDescribeHandlerTests`, `DataverseReadQueryHandlerTests` and `DataverseSearchDataHandlerTests` (the analysis source links: document, output file, regarding document). A first broad run showed 68 failures, all doubles that returned rows without these columns (plus one load timeout); none was a product defect.

Counts: BFF build (`dotnet build src/server/api/Sprk.Bff.Api/`) 0 errors, 0 warnings. `Spaarke.ArchTests` (full, from the worktree path): 2208 passed, 0 failed, 0 skipped (2181 before this round plus the 27 cases of the new guard). Broad filtered run of `Sprk.Bff.Api.Tests` (External, MultiRoot, LiveParent, Communication, Document, Invoice, UnifiedAccess, Finance, Issue963, NoAccess, Todo, Event): 20553 total, 20540 passed, 10 skipped, 3 failed. Two of the three were `InvoiceLinksTests` (`ReadLinkedInvoices_ReadsEveryInvoiceColumn`, `ReadLinkedInvoices_OneReadPerChunk`): their row double omitted selected columns instead of answering explicit nulls; fixed after the run (test double only). The third, `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_FailsLoudlyAndLogsTheContainerId`, was a client timeout under load (1 m 35 s in a 31-minute run). Re-run after the fix of every touched or new class together with `ProvisionProjectIdempotencyTests` (`InvoiceLinksTests`, `ExternalLookupAbsentColumn*`, `ExternalDeleteOwnTests`, `ExternalInvoiceVisibilityTests`, `ExternalDocumentNarrowingTests`, `Issue963_ExternalDataPagingTests`, `ExternalMessageSendTests`, the three `Dataverse*HandlerTests`): 1211 total, 1211 passed, 0 failed. The full broad run was not repeated after the `InvoiceLinksTests` double fix.

Seeding proof (the six production files restored to `be36c99e98`; `ExternalMultiRootRule.cs` kept at the new version because it only adds helpers; `CommunicationThreadReadService.RootsOf` made `internal` and a `ParseRows` stub with the old behaviour added so the tests compile; classes `ExternalLookupAbsentColumn*` and `ExternalDeleteOwnTests`): 212 tests run, 29 failed, 183 passed. Failed before, pass after: to-do list, event list and `GetTodoRootAsync` for each of the three stamp columns (9); live-parent check for to-do and event (2); PATCH to-do with the project and the work-assignment stamp not returned (2); invoice lookups (1); `ParseDeleteTarget` one column at a time for to-do, event and document (3) and the document delete target (1, in `ExternalDeleteOwnTests`); the delete-target pin (3); `LinkOf` (1); document narrowing (1); assistant source documents (1); communication stamps (1); deny list (1). Passed in both states: the explicit-null and all-present controls; the projection pins (the selects and projections were already complete, so those pins hold the line rather than fail before); `ParseDeleteTarget_AnAbsentCreatorStamp_MatchesNobody`; the PATCH variant that masks the matter stamp (the to-do's own root, which the old root resolution already refused); the helper tests of `ExternalMultiRootRule` (kept at the new version). The three `ParseDeleteTarget` must-not-fire cases cannot run against the old parse (they find the parent column by removing it and seeing the parse report it unreadable), so they also fail before. After restoring the committed files all pass.

Files restored byte-identical (SHA-256 checked against the committed tree) and touched:

| File | SHA-256 |
|---|---|
| `ExternalDataService.cs` | `185383bf5e498948a3f77bad08983f75e7649a85d34710cfad72d53bb051b1ec` |
| `ExternalDocumentNarrowing.cs` | `32b1726c25db99b8d5a2838c7acc7f6c7a1d9b21a273c732c89c852b341e3732` |
| `ExternalInvoiceVisibility.cs` | `4e92d061a9f64a06785cd63b30356f2c71d43309d1891d6b38d18d0fa7a6d176` |
| `ExternalMultiRootRule.cs` | `f4b86271938876c0326db5704686ecdacb9a8fb729bac8edffd9eeb8b96eb8f0` |
| `NoAccessListReader.cs` | `15da30935d52366d22bbaac40a261b58c3bb3ed084ba4ca5ce7a5bfb04d2a7b5` |
| `ExternalCallerCommunicationGuard.cs` | `3d3b67108b867b7f87022436f7f13087a599dab8d3f20e19a358f4daecc6c665` |
| `CommunicationThreadReadService.cs` | `f0d7de1052c4dc2c2284cc0c77c07e81017ee68284cf70939319f98238df4812` |

### Is the class closed in the external-access surface?

Yes for the reads that feed an external caller's access decision: every Web API read of a lookup column in that surface now treats an absent column as unreadable (Census A and B), adds only a match or grant (C), is not an access decision (D) or is an SDK/FetchXML read with a cited, pinned projection (E). The guard keeps a new read from appearing unreviewed. Not closed, and listed above: the four `KnownOpen` admin-plane and impersonated sites, the adjacent `sprk_accesspermission` choice column, and code outside the guard's scope.
