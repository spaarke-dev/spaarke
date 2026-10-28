# Task 098: (a′) on sprk_invoice, hiding invoices with an isolated Submit Invoice source from external callers

Branch `task/ebill-098-external-marker`, based on `786d1ea17a`. Rigor FULL, opus tier. One adversarial-review pass (operator direction; the POML asks for two).

## What was built

- **The predicate**: `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs` (`DecideAsync`, `HiddenFamilyRowsAsync`). It is a thin composition: task 188's `ExternalDocumentNarrowing` decides the source for rules 2–4, and it adds the roots term (`ChildRowRoots` plus the live-parent walk, then effective root flags). Rule 1 (root grants) is never applied.
- **The marker option**: `ExternalDocumentNarrowing.DecideAsync(..., honourSubmissionMarker)` defaults to `false`. When it is `true`, `SkipsOwnLeg` skips only hop 0, and only when the row's own value is Restricted and the row carries `sprk_restrictedbysubmission` (ExternalDocumentNarrowing.cs:322). Ancestors, the communication rule and the thread rule still apply. All 188 tests pass unchanged with the option at its default.
- **Seams** (`ExternalDataService`, registered in `ExternalAccessModule.cs:129-132`):
  - `HideInvoicesForAsync` (ExternalDataService.cs:1929) is caller-aware, exposed as `ExternalInvoiceHiding`.
    - An internal systemuser hides nothing and causes no reads.
    - An unresolvable caller, or any fault, gives `ReadFailed`, which hides every asked invoice.
  - `HideInvoiceFamilyRowsAsync` (ExternalDataService.cs:1962) is exposed as `AppOnlyInvoiceFamilyHiding` for the AI tools. It maps family rows to their invoice. It throws on a fault, and the tool then answers with an error and no data.

## Surfaces (a)–(d)

| Surface | Where | Behaviour for a hidden invoice | Fail-closed path |
|---|---|---|---|
| (a) Module grid fetch over any family table | ExternalModuleDataEndpoints.cs:404-406, `HideInvoiceRowsAsync` :480 | Row absent, even when the grid does not ask for its id (the id is projected server-side and stripped) | Fault or no predicate: every row is hidden and `Truncated=true`. An unreadable id: that row is hidden and `Truncated` stays false (ExternalModuleDataEndpoints.cs:509 sets it only on a fault; corrected in review round 4a) |
| (a) Single-record route on the invoices module | Unchanged | Already 403 for every invoice id, with no reads (pinned by test) | n/a |
| (b) Invoice thread: list, post, delete-own | ExternalMessageEndpoints.cs `EvaluateRecordGateAsync` invoice block (~810-827) | Same answer as a non-existent or cross-tenant invoice id, with no message read and no row written | Fault or no predicate: refused the same way |
| (b) Messages on a matter or project thread regarding an invoice, and the notification feed | ExternalMessageEndpoints.cs:514 (`SecureRootsFilter`), `RegardingInvoicesVisibleAsync` :532, ExternalNotificationFeedEndpoints.cs | Withheld from the list, the feed and the count. One predicate call per page | Fault or malformed regarding: withheld |
| (b′) Documents filed to an invoice: list, content, versions, upload | ExternalProjectDataEndpoints.cs:1415, `RightsForInvoiceAsync` → `InvoiceVisibleToAsync` :1350 | 403 with the same body as a missing invoice; nothing touched | Fault: refused the same way |
| (c) Submission read | ExternalInvoiceSubmissionAuthorizationFilter.cs:309 | `UniformRecordNotFound`, the same 404 as a missing submission | Fault or no predicate: 404 |
| (c) Submission list | ExternalInvoiceSubmissionEndpoints.cs:253/266 `WithoutHiddenInvoicesAsync` | Row omitted; the others are kept | Fault: the row is omitted |
| (d) AI `read_query` | DataverseReadQueryHandler.cs:191-212, 369-418 | Invoice or line rows dropped. One seam call per table per page | The seam throws: error with no data. A row without an id is hidden |
| (d) AI `search_data` | DataverseSearchDataHandler.cs:358-375 | Hidden hits dropped and the count adjusted. No warning reveals that anything was dropped | The seam throws: error with no data |
| (d) AI `describe` | DataverseDescribeHandler.cs:378-453 | Same `NotFound` and message as a missing record | The seam throws: error with no data |

`InvoiceSubmissionView.InvoiceId` is new and marked `[JsonIgnore]`. It is read only so the server can decide; the response contract is unchanged, and a test pins that GET never returns it.

### Surfaces outside the list (classification)

- **Single-record route of the invoices module**: SAFE. It already answers 403 for every invoice id, pinned by `InvoicesModule_SingleRecordRead_IsRefusedForEveryInvoiceId...`.
- **Guard `NotInvoiceSurfaces`**. Each entry was reviewed and has a reason in the code:
  - Configuration or tables that read no row: the message roots `.cctor`, `DataverseConfirmedEntitySet`, the WorkforceRoleCeilingGate `.cctor` and `SubmitInvoiceTo`, search `ToTableLogicalName`, and the EmailDraftToolHandler `.cctor` and `.ctor`.
  - Inputs or consequences of an applied predicate: `RegardingInvoicesOf`, `RightsFromGate`, `InvoiceFamilyKeyAttributes`.
  - Reads that return no family row: `DenyUnlinkedFirmAsync`, `FirmRecordsAsync`, `SubmitAsync` (a new submission has no invoice), and `ExternalUserContextEndpoint.HandleAsync`.
- **090's internal file route and upload**: `StoredFileAccessFilter.InvokeAsync` and `StoredFileService.UploadForCallerAsync` refuse every caller that step 0 refuses, flagged-external included. The guard checks this by transitive reach to `FinanceAuthorizationFilter.EvaluateStepZeroAsync`.
- **Later tasks (093, 130, 152)**: listed in `PendingSurfaces`. They are keyed by the guard's primitives (a family-reader call from the external-plane namespace, or a named external type), so each one fails the guard when it lands until it reaches the predicate.
- **Event, memo and analysis records regarding an invoice**: not in model 9.1's closed taint list (`FamilyTables`). Not changed.
- **Internal-plane routes**: internal callers are never hidden by this rule (must-not-fire).

## Query cost and worst case

The cost per call to `DecideAsync` for N invoices:
- Stored File reads: ⌈N/50⌉, each with `$top=2001`. More than 2000 rows hides the whole chunk (`SourcesTruncated`).
- Submission reads (review round 1): ⌈N/50⌉, the same cap and the same fail-closed rules.
- Source document reads: ⌈S/50⌉ for S distinct sources.
- 188 decision: at most 3 hop reads, plus the communication and thread reads.
- One live-parent walk.
- Effective root flag reads: at most 3, one per root type.

Each surface calls the predicate at most once per page or request, and the tests pin the call counts. Invoice upload reads the predicate twice: once in the rights gate and once in the upload path's gate (see Deviations). The logs carry counts only (ADR-015).

## Cache statement

Nothing is cached. Every call reads the current state, so a source that is lowered after hiding is visible again on the next read (tested). The only cache on the path is the identity resolver's existing caller cache.

## Mutation and seeding proofs

Each seed changed one source line, then rebuilt and ran the targeted tests, and the file was restored afterwards (`098_seeds.py` in the session scratchpad). Every seed made its targeted tests fail.

| # | Seed | Result |
|---|---|---|
| S1 | (a) Module fetch: predicate call skipped | 4/4 failed: hides, fault, no predicate, unreadable id |
| S2 | (b) Invoice thread gate removed | 5/5 failed: thread hidden, fault, no predicate, post hidden and post fault |
| S3 | (b) Regarding-invoice term removed from `SecureRootsFilter` | 4/5 failed: matter-thread hidden and fault, feed hidden and fault. The malformed-regarding test still passes: the invoice is a live parent of the message, so 187's multi-root rule already withholds a message whose link cannot be resolved. The (a′) term is a second fail-closed layer there |
| S4 | (b′) Invoice documents: `InvoiceVisibleToAsync` skipped | 2/2 failed (hidden, fault) |
| S5 | (c) Submission read: the `view.InvoiceId` block skipped | 3/3 failed (hidden, fault, no predicate) |
| S6 | (c) Submission list: decision ignored | 2/2 failed (hidden, fault) |
| S7 | (d) `read_query` guard off | 4/5 failed. The internal-caller test passes, as expected |
| S8 | (d) `search_data` hidden hits kept | 1/1 failed |
| S9 | (d) `describe` guard off | 2/4 failed: hidden and fault. Visible and internal pass, as expected |
| S10 | Marker exception removed (`SkipsOwnLeg` always false) | 3/3 failed: the marked-.eml test for contact and for flagged-systemuser, and the option-default test |
| S11 | Roots term removed (`RootRestricted` never returned) | 4/4 failed: source and live parent, for both callers |
| S12 | Guard reach check removed (`IsReach` never adds) | 2 failed: the negative control (lambda or helper no longer found) and the main surface test |

## Tests

New tests:
- `ExternalInvoiceVisibilityTests` (74 tests): must-fire, must-not-fire, the option default, the selected columns, and the taint closure per family table, including rows linked through a line.
- Task098 tests added to the existing surface test classes: `ExternalMultiRootListReadTests`, `ExternalMessageEndpointsTests`, `ExternalMessageSendTests`, `ExternalNotificationFeedTests`, `ExternalRecordDocumentRoutesTests`, `InvoiceSubmissionEndpointsContractTests`, and the three AI handler test classes.
- `ExternalInvoiceVisibilityGuardTests` (ArchTests), with must-fire and must-not-fire controls.

Run results: see "Verification".

## Verification

All of these ran on commit `1ca81502a6`.

**Builds**: six test projects built with 0 errors: Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Spaarke.Core.Tests and Spaarke.Scheduling.Tests.

**Test runs**:

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| ArchTests, full | 1561 | 0 | 0 |
| Integration (Sprk.Bff.Api.IntegrationTests) | 199 | 0 | 8 |
| Unit, broad filter plus the touched classes | 14345 | 0 | 7 |

The touched classes added to the unit filter were DataverseDescribe, DataverseReadQuery, DataverseSearchData, DataverseToolNameFreeze, DocumentNarrowing, ExternalLiveParentRule, StoredFileEndpointsTests and StoredFileServiceTests.

**Publish size**: `dotnet publish -c Release -r linux-x64 --self-contained false`, zipped, from clean `git archive` trees.

| Build | Zipped size |
|---|---|
| origin/master `de38296499` | 38,664,321 B (36.87 MB) |
| Base `786d1ea17a` | 39,505,518 B (37.68 MB) |
| This task `1ca81502a6` | 39,529,637 B (37.70 MB) |

- This task's delta is +24,119 B (+0.02 MB) against its base.
- The branch is +0.83 MB against master; nearly all of that is earlier ebilling work.
- Total size is well under the 55 MB review threshold.
- No package was added; no new CVE surface.

**/conflict-check**:
- No overlap with origin/master since the merge base, with work/ebilling-r1, or with any of the 25 open PRs.
- Soft overlap with the parallel task branches 092, 094 and 095-from-email on `InvoiceSubmissionContracts.cs`, `InvoiceSubmissionService.cs` and `InvoiceSubmissionEndpointsContractTests.cs`. This task's edits there are additive only: the `[JsonIgnore] InvoiceId` property, one select column with its mapping line, and new tests at the end of the class.
- No overlap with 089.

## uac-r2 conditions

1. **Nothing new is stored.** No column, row or cache is added. The marker `sprk_document.sprk_restrictedbysubmission` is only read. It is field-secured and refused in `ParentLineage.RefuseAppOnlyWrite` (ParentLineage.cs:1208-1210); nothing in this task writes it.
2. **Unknown fails closed.** An unreadable source, own value, marker, chain, communication, root or flag hides the invoice, and so does an unresolvable caller or a truncated Stored File page. Unknown is tested separately from Restricted on every axis and is never treated as false.
3. **The decision is server-side only.** Every surface gives a hidden invoice the same answer as a non-existent or cross-tenant id. Tests compare full ProblemDetails bodies (thread, documents), the 404 body with only the correlationId stripped (submission), and the error code and message (describe). No hidden-specific read or write follows.
4. **Caller rights.** The surfaces keep their existing gates, which already use `GetCallerRightsForAuthorizationGateAsync` (unknown and denied answer the same). This task adds no new caller-rights probe. The predicate itself reads app-only, inside the BFF, and returns only a hide set.
5. **One predicate.** Rules 2–4 are 188's `ExternalDocumentNarrowing.DecideAsync`, called through the single app-only seam `InvoiceVisibilityReaders.DecideSources`. There is no route-local check. The guard enforces that every external invoice-family surface reaches `ExternalInvoiceVisibility` and that only `ExternalAccessModule` and the three AI handlers take the family seam.

## Known limits

- **K: a message whose own regarding is unset** but whose thread regards an invoice. The invoice thread itself is gated at the record gate. A matter-thread message is checked by its own `sprk_regardinginvoice` only.
- **K: delete-own of a message on an invoice thread** is covered through the shared `RightsForInvoiceAsync` / record gate and the guard's `WiredReaches` pin (`AuthorizeDeleteOwnAsync`). It has no dedicated route test, because the delete fixture lacks invoice-parent support.

## Deviations from the POML

- The seams are injected as nullable `[FromServices]` parameters on the existing handlers, not as new required constructor dependencies. A missing registration fails closed (no predicate means hidden), and existing callers and tests keep compiling.
- The tests were added to the existing surface test classes rather than new files, so they reuse each class's fixture.
- Invoice upload reads the predicate twice: once in the rights gate and once in the upload gate. The extra read only adds cost; correctness is unaffected.
- The guard's pending 093, 130 and 152 entries are keyed by the guard's primitives (reader types, named external types, the external-plane namespace), not by method names, because those methods do not exist on this branch yet.
- One adversarial-review pass instead of two, as the operator directed.

## Review round 1, 2026-10-28

One adversarial pass (Opus) found three fix-now findings (F1, F2, F3), one minor test defect (F4) and three known limits (K1–K3). F2 was fixed and then reverted at uac-r2's direction (see the known limits below). Fixed in scope:

| Finding | Fix | Tests |
|---|---|---|
| F1: confirm-route invoices never hidden (the source is only on `sprk_invoicesubmission.sprk_document`) | The predicate also reads `_sprk_document_value` of the invoice's submissions (filtered by `_sprk_invoice_value`) in the same batched pass, under the same rules. It only ever hides more. A fault hides every asked invoice; more than 2000 rows hides the chunk | `ConfirmRoute_*` (5) in `ExternalInvoiceVisibilityTests` |
| F3: the guard missed calls through `IInvoiceSearchService` | A call through a BFF interface that a listed reader type implements is a surface | Controls `InvoiceGuardControlCallsAFamilyReaderThroughItsInterface` (must fire), `InvoiceGuardControlCallsAnUnrelatedInterface` (must not) |
| F4: the internal-systemuser theory ignored its flag | It drives the real `SystemUserIdentityResolver` with `sprk_isexternal` false and blank | `AnInternalSystemuser_IsNeverHiddenByThisRule_AndNothingIsRead` |
| K1 (cheap): a Stored File with a source communication or attachment but no document counted as "no source" | Both columns are selected; such a row hides the invoice (`source_link_unreadable`) | `AStoredFileWithASourceCommunicationOrAttachment_ButNoDocument_HidesTheInvoice` |

SPA uploads (corrected in round 3): after 092, uploads carry no submission document. Both the SPA upload and the internal upload store their file as a Stored File, so an upload invoice is decided by its `InvoicePdf`. A sourceless `InvoicePdf` leaves it visible. On this branch's base, which predates 092, an SPA upload still writes `sprk_invoicesubmission.sprk_document`. Round 3 ignores that link for the Spa channel.

Recorded, not fixed here:
- **Undo ordering (report K1 #6)** is handed to 094. The step-5 undo deletes the Stored File before the invoice, so a partial undo leaves a visible invoice with no source. 094 reorders the undo (invoice before its Stored File) or hides invoices of Failed submissions. With F1 in place, the submission's `sprk_document` still names the source while the submission row exists.
- **AI retrieval surfaces (report K2).** `SemanticSearchService.cs:673,785` returns `InvoiceName` for documents filed to an invoice, and `IRetrievalAccessTrim` checks Read on the invoice only, with no (a′) check. Both tables are outside model 9.1's closed taint list. Raised with uac-r2 and the owner.
- **Timing (report K3).** A hidden invoice costs more reads than a missing or out-of-scope one: the thread gate runs the predicate after the scope check, and the invoice-documents path runs it before the rights check. Response bodies are identical and nothing is written. 130's guard entry depends on the type names `FirmOutcomeEndpoints` and `FirmFindingsProjection`, which 130's POML calls directional.
- **The `read_query` row-cap oracle (report F2), on documents and invoices alike.** The "reached the row cap" warning is measured on the raw page, before hidden rows are removed (task 188 F1-5). For an external caller, a hidden match under TOP can therefore be told apart from no match.
  - Round 1 first fixed the invoice side by counting only visible rows (`81071dd45c`). That fix was reverted (`git revert`) at uac-r2's direction, because uac-r2 owns `read_query`'s posture.
  - uac-r2's reasoning: when the raw page hits TOP but fewer visible rows come back, more visible rows can exist beyond the page, so a visible-only count makes a truncated answer look complete. That is a correctness regression, traded for an oracle that is no escalation, because flagged users can read `sprk_invoice` directly in Dataverse until 191 closes it.
  - The invoice and document paths keep one identical posture: the raw-page cap.
  - Owner: uac-r2, #2369, covering both documents and invoices. The fix is to page until TOP visible rows, within a budget.

Seeding proofs for this round: skipping the submission read failed 8 tests (the `ConfirmRoute_*` must-fire, fault and truncation tests and the batched-reads test); ignoring the unknowable columns failed the 4 K1 cases; dropping the interface match failed the guard's negative-control test.

## Review round 2, 2026-10-28

uac-r2 re-checked `41a0ec7a6d`.

Fixed (narrowed by round 3 to the Internal channel): a submission whose document link is empty hides the invoice (`source_link_unreadable`). This is safe for real data: the submission evaluation refuses a submission with no document before any invoice exists (`SubmissionEvaluationOutcome.cs:128-131`, `DocumentMissing`), and the persister is the only writer of a submission's invoice.
- Tests: `ConfirmRoute_ASubmissionWhoseDocumentLinkIsEmpty_HidesTheInvoice` and `ConfirmRoute_ADocumentLinkEmptiedAfterTheInvoiceWasCreated_HidesTheInvoice`, for both callers. `ConfirmRoute_ASubmissionsReadableDocument_LeavesTheInvoiceVisible` now covers only the readable document. The taint-closure fixture gives each submission row a document, as a real submission has.
- Seeding proof: with the old submission filter restored, the two new tests failed for both callers (4 failures); the file was restored.

Known limits, recorded:
- **K1.** `sprk_invoicesubmission.sprk_document` is not write-protected. Tracked in #2372 (field-secured, BFF-only write), which waits for 191.
- **K2. Guard blind spots.** The guard does not see a family read through:
  - an adapter or extension-method class outside the external-plane namespaces;
  - a DI-bound `Func<>`;
  - an abstract base of a reader type.

  The interface branch added in round 1 is proven only by its fixture today; no real external-plane caller uses it yet.
- **K3. Cost.** The submission read adds ⌈N/50⌉ reads per call. The `[EXT-INVOICE-VISIBILITY]` cost line logs it as `submissionReads`.

## Review round 3, 2026-10-28

The submission read also selects `sprk_channel`, mapped through the shared `InvoiceSubmissionChannel` enum. The Stored File read selects `sprk_purpose` in the same batch. Both reads now return every row of the invoice, and the 2000-row cap per chunk counts all of them. Neither adds a request. The rules:

1. **Spa channel:** never a document source. The invoice's Stored Files decide.
2. **Internal channel with a document:** decided by that document, as before.
3. **Internal channel with an empty document link:** hidden, unless the invoice has a Stored File with purpose `InvoicePdf` and no source at all (no document, communication or attachment identity). That file is the internal upload's own invoice PDF. A sourceless `InvoiceSupportingFile` does not count, and neither does an `InvoicePdf` that carries any one source column.
4. **A channel that is unknown, unreadable or not in the enum:** hidden (`source_kind_unknown`). So is a value added to the enum later, until it is decided here.

Tests, each for both callers:
- Must not fire: `SpaUpload_NoSubmissionDocument_AndASourcelessInvoicePdf_IsVisible`, `InternalUpload_NoSubmissionDocument_AndASourcelessInvoicePdf_IsVisible`.
- Must fire:
  - `Internal_AnEmptyDocumentLink_AndNoStoredFile_IsHidden`;
  - `Internal_AnEmptyDocumentLink_AndOnlyASourcelessSupportingFile_IsHidden`;
  - `AnUnknownOrUnreadableChannel_HidesTheInvoice` (999, null, a string);
  - `AnEmptySubmissionLink_PlusAStoredFileWithARestrictedSource_IsHidden_DecidedByTheFile`.
- `InternalUpload_AnInvoicePdfCarryingAnySource_IsNotTheUploadsOwnPdf`: one case per source column (hidden) and the all-empty case (visible).
- The round 2 tests still pass, because a confirm-route submission is Internal with no Stored File.

Seeding proofs (each restored afterwards):
- Counting any sourceless file as the upload's PDF failed the supporting-file test (2).
- Dropping the unknown-channel hide failed the unknown-channel theory (6).
- Dropping the Spa exemption failed the SPA upload test and the decided-by-the-file test (4).

Known limits, recorded:
- **A write-time source-kind marker** on the submission would remove the remaining data-fault class. Filed under #2372 (schema, owner OK, tied to 191).
- **The writer guard** (an `InvoicePdf` is created only in the same commit as its invoice) is built on the 094 branch, not here: this base does not include 092's persister.
- **Legacy SPA submissions** (before 092) still carry `sprk_document`; rule 1 ignores it, so such an invoice is shown whatever that document is. Accepted by the main session and uac-r2 (2026-10-28): it is the firm's own upload, never an isolated internal source; dev data is recreated, not migrated (O-13); after 092 an SPA submission cannot carry a document.

## Review round 4a, 2026-10-28 (rule 3 rework)

**The defect.** Rule 3 left an Internal submission with an empty document link visible only when the invoice had a fully sourceless `InvoicePdf`. 095's from-email route writes exactly that submission, but its `InvoicePdf`'s `sprk_sourcedocument` is the marked `.eml`. Every from-email invoice was therefore hidden from its own firm. The round 3 theory case "document" pinned that outcome; it is replaced.

**Writer shapes** (authoritative; from 094 head `8e7d8a847b` and 095's contract). Every predicate fixture below carries its route's real submission row and real Stored File rows (`InvoiceWorld.SpaUpload`, `InternalUpload`, `FromDocument`, `FromEmail`, `SubmissionEml`, `WriterStoredFile`).

| Route | Submission channel | Submission `sprk_document` | Invoice's Stored Files |
|---|---|---|---|
| SPA upload (092/094) | Spa | empty (094 refuses a document on Spa) | InvoicePdf at ForUpload(submissionId), no source, FirmVisible Yes |
| Internal upload (092/094) | Internal | empty | InvoicePdf at ForUpload(submissionId), no source, FirmVisible No |
| Confirm (050a), legacy on this base | Internal | the document | none |
| Confirm / from-document on 094 | Internal | the document (the submission id is derived from it) | InvoicePdf copy, `sprk_sourcedocument` = that document, FirmVisible No |
| From-email 095 (marked or unmarked `.eml`) | Internal | EMPTY | InvoicePdf at ForUpload(submissionId), FirmVisible No; source document = the `.eml`, source communication = the email's communication, attachment identity `eml-part-{n}`, source version and hash. Plus InvoiceSupportingFile rows at ForAttachment(submissionId, `eml-part-{n}`) with the same source columns |

A marked and an unmarked `.eml` differ only in the `.eml`'s own `sprk_restrictedbysubmission`.

**One source decision.** `ExternalInvoiceVisibility.DecideSourceDocumentsAsync(IReadOnlyCollection<Guid> sourceIds, InvoiceVisibilityReaders readers, InvoiceVisibilityCost? cost, CancellationToken ct)` returns a `SourceDocumentDecision` (`HiddenReason(id)`, `IsVisible(id)`). It holds the code that was inline in `DecideAsync`: the source read by id, the 188 narrowing with the marker-aware rule 2, the roots term and the effective flags. `DecideAsync` calls it, and the submission routes (4b) are to call the same function. An id the function was not asked about, `Guid.Empty`, an unreadable link and a source that does not come back are all hidden. A failed read throws.

**The Stored File rules:**
- A row whose `sprk_purpose` reads null hides the invoice (`source_link_unreadable`). This covers a row masked by field security: purpose and the source columns are secured (088).
- A row with a source document is decided by that document, even when its communication or attachment identity is also set. The marker exception applies, so a marked `.eml` leaves the invoice visible to its firm (model 9.1 §625).
- A row with a communication or attachment identity but no document hides the invoice.
- A sourceless row of a purpose other than InvoicePdf is not a source. A supporting file never counts as the upload's own PDF; a supporting file with a source is decided by that source.

**Rule 3** (Internal, empty link) is decided by the invoice's InvoicePdf:
- sourceless (the upload's own file): visible;
- with a source document: decided by that document;
- communication or attachment only: hidden;
- no InvoicePdf: hidden.

Rules 1 (Spa), 2 (Internal with a document) and 4 (an unknown channel hides) are unchanged. A failed Stored File read, submission read or invoice-link read hides (`Faulted`) or throws on the AI path, as before.

**Tests** (`ExternalInvoiceVisibilityTests`; each for a contact and a flagged systemuser unless noted):
- Must not fire:
  - `SpaUpload_NoSubmissionDocument_AndASourcelessInvoicePdf_IsVisible` and `InternalUpload_…_IsVisible`, now on the writer shapes;
  - `WriterShape_FromDocument_AReadableDocument_IsVisible`;
  - `WriterShape_FromEmail_AMarkedEml_IsVisibleToItsFirm`.
- Must fire:
  - `WriterShape_FromEmail_AnUnmarkedRestrictedEml_IsHidden`;
  - `WriterShape_FromEmail_NoInvoicePdf_OnlySupportingFiles_IsHidden`;
  - `WriterShape_FromEmail_ASupportingFileWithARestrictedSource_HidesTheInvoice`;
  - `WriterShape_AStoredFileWhosePurposeReadsNull_HidesTheInvoice`;
  - `WriterShape_FromEmail_AFieldSecurityMaskedRow_HidesTheInvoice` (visible first, then one row masked);
  - `Internal_AnEmptyDocumentLink_AndOnlyASourcelessSupportingFile_IsHidden` (round 3, kept).
- Fail closed:
  - `WriterShape_FromEmail_AFailedStoredFileRead_HidesTheInvoice`;
  - `WriterShape_FromEmail_AFailedInvoiceRead_HidesItsRows` (Fact). The rows' invoice-link read fails and the call throws; a link that is not a GUID hides the row; the predicate's own submission read fails and the invoice is hidden.
- `Rule3_AnInternalEmptyLink_IsDecidedByTheInvoicePdf` replaces round 3's `InternalUpload_AnInvoicePdfCarryingAnySource_IsNotTheUploadsOwnPdf`. Its cases:
  - sourceless: visible;
  - a readable document: visible;
  - a readable document with a communication and part: visible;
  - a Restricted document: `source_hidden`;
  - communication only, attachment only, and both: `source_link_unreadable`.
- `Rule2_ASubmissionsOwnDocument_HonoursTheMarker` pins existing code: legacy confirm shape, marked `.eml` visible, unmarked hidden.
- `DecideSourceDocuments_DecidesEachSource_AndHidesOneItWasNotAskedOrCouldNotRead` (Fact) drives the extracted function directly.
- Fixture correction: the taint-closure `sprk_storedfile` family row now carries a purpose (supporting file), as every written row does.

**Seeding proofs.** Each seed was one change, rebuilt and run against the class, then restored (`4a_seeds.py` in the session scratchpad).

| Seed | Failed |
|---|---|
| S0, the HEAD source (before) | 17: the Rule 3 document cases (6), every from-email writer shape (marked visible, unmarked and supporting-file reasons, masked row, failed invoice read), and null purpose |
| S1a, marker path: an InvoicePdf with a source document not counted for rule 3 | 15, including `WriterShape_FromEmail_AMarkedEml_IsVisibleToItsFirm` and the Rule 3 document cases |
| S1b, the marker not read by the source decision | 9: the marked-`.eml` tests, including `Rule2_ASubmissionsOwnDocument_HonoursTheMarker` and the from-email marked shape |
| S2, a communication- or attachment-only InvoicePdf counted as readable | 10: the three only-cases of the Rule 3 theory and the round 1 K1 theory |
| S3, the null-purpose hide dropped | 4: the null-purpose and masked-row tests |
| S4, the no-InvoicePdf hide dropped | 10: the empty-link, no-Stored-File, supporting-file-only and from-email no-PDF tests |

**Correction to surface (a).** The surfaces table said an unreadable id sets `Truncated`. `ExternalModuleDataEndpoints.cs:509` sets it only on a fault (`decision.Faulted` with rows dropped) or when there is no predicate. An unreadable id hides its row and `Truncated` stays false. The table row is corrected.

**Known limit K1.** `sprk_invoicesubmission.sprk_channel`, the submission's `sprk_invoice` link and submission Delete are security inputs too, like `sprk_document`. Changing one changes which rule decides the invoice. They are filed under #2372 (field-secured, BFF-only write) and tied to 191.

**Observed, not changed.** A purpose value outside `StoredFilePurpose` (non-null) is treated as "not an InvoicePdf". When it is sourceless it is not a source; when it has a source, it is decided by that source. No writer creates such a value.

## Escalation

- **A second app-only exception for the AI tools.** The AI handlers' rule is User-OBO reads. `AppOnlyInvoiceFamilyHiding` maps family rows to invoices and decides them app-only. It returns only a hide set and never row data, which matches the existing 188 document-narrowing exception. uac-r2 needs to accept it explicitly.
