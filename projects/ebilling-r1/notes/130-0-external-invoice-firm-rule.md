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
