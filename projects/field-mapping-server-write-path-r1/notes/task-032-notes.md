# Task 032 notes - invoice extraction onto RecordCreationService

Status: **not implemented; escalated.** No production or test code changed. Branch `task/fm-032` carries this note only.

## Finding 1 - the task's target no longer exists on this branch

The POML converts `InvoiceReviewService.CreateInvoiceRecordAsync` (with `BuildInvoiceCreateFields`, `RemoveInvoiceIfCreatedAsync`,
`LinkDocumentOrCompensateAsync`, `RefileDocumentUnderInvoiceAsync`). None of those symbols exists in `src/` or `tests/` (grep, base
`dc08e14990`). Since ebilling-r1 task 050a (D-134) `InvoiceReviewService` confirm creates no invoice: it submits the document through
`InvoiceSubmissionService.SubmitExistingDocumentAsync` and marks the document ConfirmedInvoice. Its only record writes are conditional
document updates (`UpdateRecordFieldsIfUnchangedAsync`). The class header documents this. `w2-coordination.md` already said "025 / 032:
re-scope at task start".

The create-then-link compensation the POML tells us to preserve is gone with the create, so the acceptance criteria about
`RemoveInvoiceIfCreatedAsync`, `OwnerTeamUnresolved` from a service refusal on the review path and "no link written" have no code to
attach to. `tests/integration/data-mutation/FinanceInvoiceReview/` holds only `ConditionalLinkWriteTests` and `InvoiceReviewWritePathTests`
(the conditional document writes).

## Finding 2 - the only invoice creator now is InvoiceSubmissionPersister, and moving it is a design decision

`InvoiceSubmissionPersister` (ebilling-r1 task 049 part C) creates the invoice, its lines, the approval portion, the Stored File rows and the
submission link in ONE `IGenericEntityService.ExecuteTransactionAsync`, with ids derived from the submission id (`InvoiceIdFor`), owner
resolved once through `IRecordOwnershipResolver` and applied to every row, and core-record fields from `InvoiceFamilyCoreRecordGuard`.
It runs in `InvoiceSubmissionProcessingJobHandler` (an ADR-004 job), not inside an HTTP request.

`RecordCreationService.CreateAsync` cannot take that create today:

1. It creates one row per call. It has no fixed-id input and no pre-built transactional batch (the persister's rows must commit or fail
   together; ebilling-r1's answer of 2026-10-19 allows routing it through the service "only if the service takes a pre-built transactional
   batch with fixed ids").
2. A job has no caller token. `RecordCreationPrincipal.SystemPrincipal` refuses `Mapping`, `ReferenceLookupColumns` and
   `PostMappingDefaults` (service lines ~151-170: mapping is applied "as the caller"). So even with a batch input, a system-principal create
   gets no Matter mapping, which was the point of FR-20.
3. ebilling-r1's binding rule is "single creator": invoices are created only by the submission operation. Re-pointing must not open a second
   creator.

This matches both POML escalation triggers (caller-supplied id without changing the service's contract; no caller token so caller vs system
principal is the human's call). Nothing was guessed.

## Options for the owner

A. **Drop 032** and record `InvoiceSubmissionPersister` as a named exception in the task 060 ownership matrix (ebilling's recommendation;
   `w2-coordination.md` line 67). FR-20's "invoice carries mapping from its Matter" is then not met for extraction; FR-20 would be amended
   to say invoices are submission-created and unmapped, or mapping is deferred.
B. **New design task** (Opus, not a Sonnet conversion): add to the service a fixed-id, multi-row transactional create plus a way to apply
   Matter mapping under a system principal (for example the submitter's identity, which the persister already reads as
   `sprk_createdbyperson`, as a recorded-caller read-only mapping source). This changes the service's authorization model and needs
   UAC-r2 and ebilling-r1 review; sizing well beyond the 5-7 h estimate.
C. **Narrow variant**: apply only the fill-only mapping step (`RecordCreationService.Mapping`) to the persister's invoice `Entity` before the
   transaction, leaving the transaction and owner path unchanged. Smallest change, but it is a second implementation path next to the
   service's create (the thing the project is removing), so it needs an explicit owner decision.

## Recommendation

A, unless the owner wants mapping on extracted invoices, in which case B as a new design task. Not C by default.

## Not done / checks not run

No build, tests, publish-size delta or CVE check, since nothing changed. Existing invoice review tests were not re-run.
