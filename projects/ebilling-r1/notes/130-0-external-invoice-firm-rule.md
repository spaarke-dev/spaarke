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
