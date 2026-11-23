# Task 032 — design inputs (2026-11-06)

**Owner ruling (2026-11-06, decisions.md):** extracted invoices get Matter field mapping in R1. `InvoiceSubmissionPersister` stays the single creator of the invoice family. A fill-only mapping step inside it runs before its transaction and calls the same mapping engine as the create path. `RecordCreationService` does not change. Ebilling-r1 and UAC-r2 review the design before any code.

## Conditions from ebilling-r1

1. **Source.** The source is the core record the submission is filed under: a matter, project or work assignment (agreement in R2). It is not a Matter only.
2. **Protected targets are never mapped.** The step runs app-only, so field security does not stop it. Use the existing refusals:
   - `RecordTableRules.MappingProtectedColumns["sprk_invoice"]`: the core lookups, `sprk_reviewdetail`, and the money totals added by EB-D-200 (task 166).
   - `InheritedAccessPermission` AccessColumns for `sprk_invoice`: `sprk_firmhold`, `sprk_vendororg`, and the money totals.

   Also never mapped:
   - the firm (`sprk_vendororg`), which comes only from the submission (D-106);
   - status (statecode/statuscode), the owner, the creator stamp and the fixed id, which belong to the persister.

   A rule that targets any of these is skipped. A test pins this per column.
3. **Partner-readable columns.** Some `sprk_invoice` columns are readable by external partners (`ExternalAccessModule`, the invoice ReadableColumns). Either exclude them as mapping targets, or UAC-r2 approves each one.
4. **Identity.** The source is read as the system principal. The job has no caller token, and for portal submissions "the submitter as caller" does not map to a Dataverse identity. UAC-r2 decides.
5. **Transaction shape.** The invoice entity is built in memory, with its derived id (`InvoiceIdFor(submissionId)`), and committed with its lines in one `ExecuteTransactionAsync`. Filling columns before that call is fine, with two rules:
   - **Re-delivery is consistent.** A mapping read that faults either fails the attempt (retry) or is skipped the same way every time. It never fills different values on different attempts.
   - **A mapping fault never blocks the submission outcome** (D-101). Record it the way after-step refusals are recorded (`sprk_adminattention`, D-133), not by failing the invoice. The design states which of the two it chooses.
6. **Lines** (`sprk_billingevent`) are not mapping targets. Their values are extracted, and the rules judge them.

## Coordination

- No ebilling work in flight touches `InvoiceSubmissionPersister.cs` or `InvoiceSubmissionProcessingJobHandler.cs`.
- Nearby work: 095d-2b-2 (`InvoiceSubmissionRerunJobHandler.cs`; 032 must not touch it), 164a (`ExtractionProvenance` helpers), 165a (`InvoiceBlockedHold`).
- Rebase on `work/ebilling-r1` before building.
