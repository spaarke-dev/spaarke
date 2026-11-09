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
