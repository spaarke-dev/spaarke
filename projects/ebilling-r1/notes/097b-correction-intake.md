# Task 097b: corrected-invoice intake

Part two of task 097 (`tasks/097-corrected-invoice.poml`), split for size. Decisions: EB-D-179 (owner, binding), EB-D-174. Base `ababcfd57b`; branch `task/ebill-097b-correction-intake`. Parts 097a (the Superseded status), 097c (the persister and the Superseded close, the undo) and 097d (Signals) are separate. Rigor FULL (tags include auth; code change).

## What changed

| Area | File | Change |
|---|---|---|
| From-document contract | `Api/Finance/InvoiceSubmissionEndpoints.cs` | `SubmitInvoiceFromDocumentRequest` gains optional `mode` (`New` \| `Correction`; absent is `New`) and `invoiceId`, with the from-email route's rules: `New` with `invoiceId` is 400 `invoiceId`; `Correction` without a valid one is 400 `invoiceId`; another mode is 400 `mode`. A body without them behaves as before. |
| The Write check | `InvoiceSubmissionEndpoints.PrepareCorrectionAsync` (shared by both routes) | The caller's own Write on the invoice (OBO, the gate variant of the probe). Without it, or for an invoice that does not exist, `FinanceAuthorizationFilter.UniformRecordNotFound` (404 reasonCode `sdap.access.deny.record_unavailable`). Only then the app-only read of the invoice (firm, state, status, core lookups). A read fault is 503 `record_unavailable`. Runs before the source document is read, so a caller learns nothing about the invoice or the source. |
| The content guards | `InvoiceSubmissionService.CheckCorrectionAsync` | Reads only. In order, each 409 `ebilling.submission.correction_not_allowed` with `reason`: the invoice's firm (`different_firm`); its one core record (`different_core_record`; none or several is refused the same way); its status: only In Review on an active row (`invoice_status`, with `invoiceStatus` = the status name, absent for a value the lifecycle does not know, such as Superseded before 097a); its own submission, found by `sprk_invoice` = the invoice AND id deriving the invoice (`InvoiceIdFor`), else `no_submission` (D1); that submission's firm and core record (as above); the firm invoice number, trimmed and ignoring case as the tracking sequence keys it (`different_invoice_number`, D3/D-37). A fault reading the submission is 503 `record_unavailable`. |
| From-document intake | `InvoiceSubmissionService.SubmitExistingDocumentAsync` | Step 4a: the guard runs after the replay decision and before step A (so a refused correction files nothing) and writes nothing. Step 5 writes `sprk_resubmissionof` = the old submission. The replay's "same request" check also compares the correction target (`SameCorrectionTarget`): a retry naming another target, or none, is 409 `document_already_submitted`. |
| From-email intake | `FromEmailSubmissionService` | Step 3 runs the Write check (it answers through `FromEmailRouteAnswerException`, as the route already does for another route's answer). Step 7b, after the replay and the filing check and before the save, runs the guard. The target goes to the operation (`FromEmailSubmitInput.ResubmissionOf`). `reason: not_available` is no longer sent. |
| From-email operation | `SubmitInvoiceFromEmailOperation` | Writes `sprk_resubmissionof`. Its wiring check: `New` names neither `InvoiceId` nor `ResubmissionOf`; `Correction` names both and `InvoiceIdFor(ResubmissionOf) == InvoiceId`. |
| Refusal extensions | `InvoiceSubmissionContracts.cs`, `InvoiceSubmissionHttp.cs` | `InvoiceSubmissionOutcome.Extensions` (optional) carried to the top level of the ProblemDetails, as the from-email route's `reason` already is. Only the new 409 sets it. |
| The job's snapshot | `SubmissionEvaluationOutcome.cs` (`SubmissionSnapshot`) | `Columns` reads `sprk_resubmissionof`; `ResubmissionOfSubmissionId`; `ReadCorrectedInvoiceAsync` resolves the old invoice from that submission's `sprk_invoice` and accepts it only when it equals `InvoiceIdFor(old submission)`; a missing row, no invoice or another invoice is `Unresolved` (fail closed); a read fault propagates. |
| Evaluation | `InvoiceSubmissionProcessor.cs`, `Signals/Billing/BillingFactLoader.cs` | A correction is not a Duplicate of the invoice it replaces (section "Duplicate exclusion"). The processor's public constructor takes `IGenericEntityService` (already registered). |
| Contract doc | `notes/095-from-email-contract.md` section 6 (rows, "Correction order"), OP-7, G3; `notes/094-submit-invoice.md` (118 body) | Changed 2026-10-30 (097b), for word-add-in-r1's agreement. |

**New codes:** the from-document route now answers 409 `ebilling.submission.correction_not_allowed` (the code the from-email route already declared) and 404 reasonCode `sdap.access.deny.record_unavailable`. New `reason` values: `different_invoice_number`, `no_submission`. No new endpoint, schema change, Dataverse write outside the submission create, or App Service setting.

**Additive tightening (noted):** a from-document body that sends `invoiceId` without `mode` was accepted with the field ignored (it did not exist); it is now 400 `invoiceId`. No client sends it today (word-add-in-r1's 118 body has no such field).

## Peer edge (`SecureChildLineage.cs:198`)

Not tripped. `sprk_invoicesubmission.sprk_resubmissionof -> sprk_invoicesubmission` is a lineage edge, and `sprk_invoicesubmission` is not an ownership parent (`RecordOwnershipResolver.OwnershipParentEntities`), so ownership of the new row ignores it. The share and external walks would add the old submission's roots, but the guard requires the old submission (and its invoice) to be under the same core record and firm as the new one, so the edge adds no new root. Tests: `Refused_TheSubmissionIsUnderAnotherCoreRecord`, `Refused_TheSubmissionIsAnotherFirms`.

## Component justification (§11)

- **`mode` / `invoiceId` on `SubmitInvoiceFromDocumentRequest`.** Existing: the from-email request's same two fields (095). Extension: they are that shape, added to the existing record; no new type. Cost of doing nothing: EB-D-179 item 2 requires the from-document route to accept a correction; without the fields 118 cannot send one.
- **`CheckCorrectionAsync`, `PrepareCorrectionAsync`.** Existing: `PrepareSourceAsync` (the route's caller check plus app-only read) and the intake's firm and core-record checks. Extension: both are added beside those, in the same classes, and shared by both routes; no new service or DI registration. Cost of doing nothing: model 9.1 "Existing invoice" and EB-D-179 items 1 and 3 have no enforcement, so a correction could replace another firm's or another record's invoice.
- **`InvoiceSubmissionOutcome.Extensions`, `InvoiceSubmissionView.ResubmissionOfId`, `SubmissionSnapshot.ReadCorrectedInvoiceAsync`, `FromEmailSubmitInput.ResubmissionOf`.** Extensions of existing types. Cost of doing nothing: the 409's `reason` cannot reach the from-document client; the replay cannot tell two targets apart; 097c has no read of the target.

## BFF hygiene (§10)

Placement: in the BFF, in the existing submission intake and its routes (the only write path for submissions); no new package, endpoint, DI registration or background work. Publish size and CVE: see "Gate".

## Tests

New: `tests/unit/.../Submission/InvoiceCorrectionIntakeTests.cs` (the guard, allowed and every refusal; the from-document entry; the snapshot), with the shared `CorrectionSeed`. Changed: `InvoiceSubmissionServiceTests.Harness` (invoices, prior submissions by invoice), `FromEmailSubmissionServiceTests` (the `not_available` test replaced by the correction tests), `SubmitInvoiceFromEmailOperationTests` (the target written; inconsistent inputs are defects), `InvoiceSubmissionEndpointsContractTests` (from-document: 202 with the lookup, unchanged without the fields, uniform 404, each 409 reason, 400s), `InvoiceSubmissionFromEmailContractTests` (the `not_available` test replaced: uniform 404, 202 with the target, 409 `different_invoice_number`).

Beyond the brief's list, with the reason: the replay tests (a retry is not refused after the old invoice moved on; a retry naming another target is 409), because the guard's placement after the replay is the behaviour 097c relies on; the operation's wiring check, because it is the second line against a wrong target.

**Fail before.** At the first commit, with `FromEmailSubmissionService.cs` reverted to `ababcfd57b` (it compiles against the new code), 11 of the 54 `097b` tests then present fail (every from-email correction test: the old step 3 refused all corrections). The other new tests do not compile against the base (the members they call are new).

**Seeding proof (the two guards the brief names).**
- Write check disabled (`if (false && !rights.HasFlag(AccessRights.Write))`): 3 fail, the three uniform-404 tests (from-email unit, from-email contract, from-document contract). Restored: pass.
- Firm and core-record checks disabled (the four conditions in `CheckCorrectionAsync`): 11 fail (every `different_firm` / `different_core_record` test in the unit, from-email and from-document suites). Restored: pass.

## Known limits

- K1: the external multi-root rule withholds a submission whose `sprk_resubmissionof` cannot be resolved (it fails closed). The guard puts the old submission under the same root, so this happens only if the old submission is removed later (for example by an undo); the row is then withheld, never shown to another party.
- K2: the guard's submission query has no row cap (a cap could leave the invoice's own submission out when other rows point at the invoice). The test harness does not model a cap, so this is reasoned, not tested.
- K3: a correction does not yet supersede anything: the old invoice stays In Review until 097c's persister closes it. Two corrections of one invoice before 097c lands are two submissions naming the same target; 097c's persister re-check decides.

## What 097c needs from this branch

- `SubmissionSnapshot.ResubmissionOfSubmissionId` and `ReadCorrectedInvoiceAsync(entities, ct)` (`CorrectedInvoiceRead`: `None`, `Resolved(submission, invoice)`, `Unresolved(submission)`; a read fault throws).
- The persister must re-check the old invoice at commit (In Review, same firm and core record) because the intake's check is not a lock; this branch checks only at intake.
- A retry after 097c's commit is answered by the replay (both routes) before any guard runs, so the Superseded old invoice does not turn a retry into a 409.
- `InvoiceSubmissionService.ResubmissionOfColumn` and `CorrectionInvoiceColumns`.
- The job already passes `ReplacedInvoiceId` to the evaluation (section "Duplicate exclusion"); 097c needs no further evaluation change for the first judgement. Acceptance item: a field correction on a committed correction raises no Duplicate against the superseded invoice (relies on 097a's Superseded exclusion; review finding 2).

## Duplicate exclusion for a correction (added in scope at the coordinator's direction; for ontology-r1 review)

**Why.** The job judges a correction before its commit supersedes the old invoice, so at evaluation the old invoice is still In Review and carries the same firm invoice number (D3 requires it). The Duplicate rule counted it as a prior and flagged the correction; with a Reject consequence the correction would be created Rejected.

**Change** (`BillingFactLoader.cs` is ontology-r1's code; **ontology-r1 reviews this change**):
- `BillingEvaluationRequest` gains `ReplacedInvoiceId` and `SubmittedFirmInvoiceNumber`.
- **Condition 1 (server-derived only).** `InvoiceSubmissionProcessor` sets both, and nothing else does:
  - `ReplacedInvoiceId` comes from the submission's `sprk_resubmissionof`, then that submission's `sprk_invoice`, accepted only if it equals `InvoiceIdFor(that submission)` (`SubmissionSnapshot.ReadCorrectedInvoiceAsync`).
  - `SubmittedFirmInvoiceNumber` comes from the submission row's typed `sprk_firminvoicenumber`.
  - No request body or client field reaches either.
  - A target that does not resolve gives no exclusion (warning; the correction is judged against every prior), and a read fault propagates (the job retries).
- **Condition 2 (`ExcludeReplacedInvoice`, after the priors are read and before lines and secure roots).**
  - Removes exactly the priors whose invoice is that id, and only when two things hold. First, the invoice is this firm's: its own `sprk_vendororg`, read only when a replaced id is passed, and its submission already comes from the firm's query. Second, the prior submission's typed number equals the judged submission's typed number (trimmed, ignoring case, as the tracking sequence and the intake's D3 check compare).
  - Any other id is ignored with a warning (ids only in the log) and the priors are unchanged.
- No other loader behaviour changes. For a New submission the query, columns and priors are as before.

**Tests** (`BillingFactLoaderTests`, `InvoiceSubmissionProcessorTests`):

| Test | Expectation |
|---|---|
| Must fire: predecessor still In Review with the same number (case and spaces differ) | No Duplicate finding, no warning |
| Must not fire: another In Review invoice with the same number on the same correction | Still flagged; the same request without a replaced id excludes nothing |
| Condition 2, both directions: an id of another firm, another number, or no prior at all | Ignored; the prior still counts (Duplicate still fires); one warning naming only the id |
| Review F1: read number differs from the typed one (`INV-100-R`, or none read) | Still excluded |
| Review F1: read number equals the prior's but the typed one differs | Not excluded |
| Processor | A correction carries the derived id and the typed number (with a read number that differs); a New submission reads nothing; a target naming another invoice gives no id |

**Seeding proof.**
- Exclusion disabled: 5 loader tests fail.
- Firm and number conditions disabled: the two condition tests fail (another firm, another number).
- Number source switched back to the read number: the two review-F1 tests fail.
- Restored: all pass.

## Review (one adversarial pass, Opus)

| # | Class | Finding | Resolution |
|---|---|---|---|
| 1 | F1 | The exclusion compared the read number (`FirmInvoiceNumber`, from the PDF) with the prior's typed number. A misread number on the corrected PDF would keep the replaced invoice as a prior, and a line-level Duplicate would flag every repeated line. | Fixed: compared on `SubmittedFirmInvoiceNumber` (typed); tests above. |
| 2 | F1/K | `InvoiceCorrectionReevaluation` (a field correction on the committed corrected invoice) builds a request without `ReplacedInvoiceId`, so before the old invoice is Superseded it would count as a prior. | Not changed here. After 097c supersedes the old invoice at commit, 097a's `StatusOf` drops a Superseded invoice from the priors (097a branch, `InvoiceStatusCodes.Superseded => null`), so the re-run is correct without the id. Before 097c there is no committed correction to re-evaluate. **097b must not be released without 097a and 097c.** Acceptance item for 097c: a field correction on a committed correction raises no Duplicate against the superseded invoice. |
| 3 | F3 | No test proved the Write check runs before a replay. | Fixed: from-email (Accepted and Received attempts of K without Write: uniform 404, no receipt, no requeue) and from-document (an existing correction submission for the document, no Write: uniform 404, the earlier submission not read). The Write-disabled seed makes all 3 fail. |
| 4 | K | The from-email `X-Idempotency-Key` cache (per user, 24 h) returns a caller's own cached 202 after they lose Write. | Known limit K4 below. |
| 5 | K | A probe fault in the Write check answers the uniform 404, not 503, as every gate-variant check does. | Known limit K5 below; contract row says "cannot Write". |

## Gate

Measured on head `8a1c40fc5b` (before the notes commit).

| Step | Result |
|---|---|
| Build, 5 test projects (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests) | 0 warnings, 0 errors each |
| Unit filter `Finance\|Ebilling\|Billing\|Invoice\|Submission\|FromEmail\|FromDocument` | 7147 passed, 0 failed, 5 skipped |
| ArchTests | 1858 passed, 0 failed (an earlier run under load timed out one ISS-005 bounded-time test; it passed alone) |
| Sprk.Bff.Api.IntegrationTests | 199 passed, 0 failed, 8 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |
| Conflict check against 097a (`origin/task/ebill-097a-superseded-status` 25262739f6) | `git merge-tree` clean; both touch `BillingFactLoader.cs` in separate hunks |

**Publish size (§10).**
- Convention: `dotnet publish -c Release -r linux-x64 --self-contained false`, base `ababcfd57b` (a detached worktree) against head, both zipped with `Compress-Archive -CompressionLevel Optimal`.
- Delta: **+70,138 B** zipped; `Sprk.Bff.Api.dll` +20,992 B.
- No package or project file changed.
- The absolute sizes from the long scratch paths (147 MB zipped, both sides) include a nested copy of the source tree in the output, so they are not comparable with 099's short-path export; the delta is.

## Known limits (from review)

- K4: a per-user idempotency-cache replay of the caller's own earlier 202 after losing Write (nothing created or revealed).
- K5: a fault of the caller-rights probe in the Write check is the uniform 404 (the gate variant answers no rights), not 503.
- K6 (ontology-r1, 2026-10-30): `sprk_resubmissionof` and `sprk_firminvoicenumber` are ordinary columns. An internal user with Write on `sprk_invoicesubmission` could edit them in Dataverse before a reprocess and point the duplicate exclusion at another invoice of the same firm and number. Worst case a missed Duplicate flag, using Write the user already holds. No code change.

**Agreed (2026-10-30).** ontology-r1: the loader exclusion is OK (6b9ca5e390, 8a1c40fc5b), on condition that 097b reaches master only together with 097c, stated in the PR. word-add-in-r1: the 095 contract changes are agreed; the add-in keeps Correction off until 097b, 097c and 097e are on master.

rework: fix-round-1 task 097b
