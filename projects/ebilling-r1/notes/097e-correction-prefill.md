# Task 097e: correction prefill read

Closes 095 contract item G3 (deferred to 097, split out by the owner, EB-D-179). Branch `task/ebill-097e-prefill`, base `2ef3ee971c`. The wire contract is `notes/095-from-email-contract.md` section 10 (pinned with word-add-in-r1 on 2026-10-31, `replacedBy` added the same day).

## What changed

| Area | File | Change |
|---|---|---|
| Route | `Api/Finance/InvoiceSubmissionEndpoints.cs` | `GET /api/finance/invoices/{invoiceId}/correction-prefill`: `AddFinanceAuthorizationFilter("ebilling.invoice.decide", sprk_invoices, "invoiceId", UniformNotFound)` then `RequireEbillingEnabled`, inside the `/api/finance` group (step 0). `IsNotFound` made `internal` (shared). |
| Handler | `Api/Finance/InvoiceSubmissionFromEmailHttp.cs` | `GetCorrectionPrefillAsync` (beside the 095 `GetFirmsAsync`), the firm/record name reads, and `FindReplacementAsync` (`replacedBy`). |
| The shared guard | `Services/Finance/Submission/InvoiceSubmissionService.cs` | The intake guard body of `CheckCorrectionAsync` is now `JudgeCorrectionAsync(..., OwnSubmissionRead?)`, and the read of the invoice's own submission is `ReadOwnSubmissionAsync`; `CheckCorrectionAsync` is a one-line call of the former with no pre-read row, so the intake behaves as before. `JudgeCorrectionPrefillAsync` reads the own submission once, hands the SAME row to the guard, and maps its refusal to the prefill's three reasons. `CorrectionSubmissionColumns` gains `sprk_trackingnumber` (read, not judged). `CoreRecordKindOfTable` made `internal`. |
| The Read check | `Api/Filters/FinanceAuthorizationFilter.cs` | The Record path's rights decision moved into `CallerHoldsRightsAsync` (no behaviour change); the filter and the replacement check both call it. |
| Response types | `Services/Finance/Submission/FromEmailSubmissionContracts.cs` | `CorrectionPrefillResponse` and its three small parts. |
| Route ledger | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | The governed-file text of `InvoiceSubmissionEndpoints.cs` names the new route. No new endpoint file, so `ExpectedEndpointFileCount` stays 150. |
| Tests | `tests/integration/contract/Api/Finance/InvoiceCorrectionPrefillContractTests.cs` (new); `InvoiceSubmissionServiceTests.Harness` | 34 cases (below). The harness answers a query by `sprk_resubmissionof` and can fault it. |
| Contract | `notes/095-from-email-contract.md` | Section 10; G3 in section 7 points to it. |

## Decisions

- **`canCorrect` is the intake's guard, not a copy.** The prefill asks `JudgeCorrectionAsync` with the invoice's own firm, core record and the submission's firm invoice number. The firm, record and number checks therefore pass by construction; the invoice-status, no-submission and in-progress checks decide, in the intake's order. A test asks the intake (`CheckCorrectionAsync`) and the route for each refusal reason and requires the same answer.
- **"The invoice's own submission"** is the intake's read (`ReadOwnSubmissionAsync`): the submission whose `sprk_invoice` is the invoice and whose id derives the invoice's (`InvoiceSubmissionPersister.InvoiceIdFor`). Both callers use the one method.
- **Reasons outside the prefill's three.** If the invoice and its submission disagree on firm, record or number (data the intake would refuse as `different_*`), the answer is `canCorrect: false`, `reason: "no_submission"`: the invoice has no coherent submission of its own. The set the add-in sees stays closed. Not seed-proofed (data anomaly); tested once (`SubmissionOfAnotherFirm_IsNoSubmission`).
- **An invoice with no single firm or core record** is 503 (nothing coherent to prefill; the intake refuses it too). Never a partial answer.
- **The gate** reuses the existing `ebilling.invoice.decide` key (Write on the invoice), whose own comment says the invoice-level writes of tasks 101-104 share it. No new operation key. The intake's own Write check (`PrepareCorrectionAsync`) and this filter ask the same right of the caller through two seams that already coexist in the codebase.
- **Names are read app-only after the Write passed**: the firm's `sprk_organizationname`, the core record's primary name, the invoice's `sprk_name` (`RegardingNameFields`, the one map). Access to the core record reaches the invoice by inheritance (013, ADR-034), so a caller with Write on the invoice already has the record in scope; the body shows nothing the intake routes do not (they carry the same firm, record and number).
- **`replacedBy` is optional, so its faults do not fail the call (deliberate, agreed with word-add-in-r1).** A fault while finding the replacement, reading it or reading the caller's rights on it gives `replacedBy: null` and a Warning log; it does not turn the whole call into a 503. Every other field is still judged exactly as above. A refusal (no Read) is null with no log. The replacement is read app-only only to choose it; it enters the answer only after the caller's own Read through `CallerHoldsRightsAsync` (`finance.read`), the same seam as the main gate, so a record the caller cannot open is never named.
- **`replacedBy` lookup:** the new submission's `sprk_invoice` where its `sprk_resubmissionof` is the old invoice's own submission. A single match is taken as it is (the direct replacement, even if it was itself superseded later; no chain is followed). Several: the one not Superseded; still not exactly one: null and a Warning.

## Component justification (§11)

New surface: one route, one handler with two helpers, four small response records, one filter helper, one service method pair.

- **The route + handler.** Existing: the 095 firms route (same gate, same uniform 404) and the Correction intake. Extension: neither returns the invoice's own firm, record, status, numbers and "may I correct it now"; the firms route is by record, and the intake only refuses at submit time, after the add-in has built a request. The handler sits in the existing 095 HTTP file and the route in the existing endpoints file (no new endpoint file). Cost of doing nothing: the add-in cannot prefill a Correction (G3); it would send its own guess of firm, record and number and learn only from a 409, with no way to show "cannot be corrected" up front.
- **`JudgeCorrectionAsync` / `ReadOwnSubmissionAsync` / `OwnSubmissionRead`.** Existing: the body of `CheckCorrectionAsync`. Extension: this IS that body, extracted; the intake calls it unchanged. Cost of doing nothing: a copied guard would drift from the intake, the failure the owner ruled out.
- **`JudgeCorrectionPrefillAsync`.** Existing: `CheckCorrectionAsync`. Extension: it needs the submission row (tracking and firm invoice numbers) as well as the verdict, and the prefill's own three-reason mapping; a second read of the same row would let the shown row and the judged row differ. Cost of doing nothing: the route would carry this logic itself, outside the service that owns the guard.
- **`CallerHoldsRightsAsync`.** Existing: the filter's private Record path. Extension: made callable, behaviour unchanged. Cost of doing nothing: `replacedBy` would re-implement a rights check beside the filter's, the disclosure guard drifting from the gate.
- **`CorrectionPrefillResponse` and parts.** Existing: none for this shape. Cost of doing nothing: an anonymous object with no compile-time contract for a binding client shape.
- **No new column, service, DI registration, package or background work.** `sprk_trackingnumber` is added to a column list of an existing read.

## BFF hygiene (§10)

Placement: `Sprk.Bff.Api`, `Api/Finance` and `Services/Finance/Submission`, where the Correction intake already lives (the BFF is the single backend for the add-in; ADR-008 filter, ADR-001). No new DI registration, package or background work; no AI facade involved. Publish size: see the report of the task (measured against a fresh master build at short paths). No package change, so no new CVE.

## Tests (34 in `InvoiceCorrectionPrefillContractTests`)

The POML scope: 200 with the prefill for an In Review invoice with Write (canCorrect true); canCorrect false with the intake's reason for Approved, Sent to AP, Paid, Rejected, Returned to Firm, Cancelled, Superseded, no submission and an unfinished submission; the identical 404 for no Write, a missing invoice, step 0 and a rights-read fault; 503 for a read fault; 503 `ebilling.disabled`; and the agreement test with the intake guard (4 scenarios).

The coordinator's `replacedBy` cases: present (Superseded, Read on the replacement); null (cannot Read); null (not Superseded); and the seed proof of the Read check. Beyond that, one-line justifications:

- `UnknownStatus_IsNull`: the closed-set rule "absent for an unknown statuscode".
- `NoTrackingNumber_IsNull`, `OtherCoreRecords_UseTheIntakeVocabulary` (2 cases): the agreed shape's nullable tracking number and `entityType` vocabulary.
- `SubmissionOfAnotherFirm_IsNoSubmission`: the reason mapping above.
- `InvoiceGoneAfterTheCheck_IsTheUniform404`, `FirmReadFault_Is503`: the handler's own two refusals.
- `Superseded_ReplacementReadFault_IsNull_NotA503` and `..._ReplacementRightsFault_...`: the deliberate fault choice.
- `Superseded_SeveralReplacements_...` and `..._TheOnlyReplacementIsSuperseded_IsStillNamed`: the agreed tie-break and "direct only".
- `RightsAreAskedAsTheCaller_ForBothInvoices`: both rights reads carry the caller's token (a disclosure guard must not run app-only).

**Seed proofs** (a mutation, the test turned red, the mutation reverted):

- The Write gate: the route's operation `ebilling.invoice.decide` changed to `finance.read` (Read only): `WithoutWrite_IsTheUniform404_SameAsAnUnknownInvoice` fails (1 failed, 33 passed).
- The Read check on the replacement: `if (!mayRead)` changed to `if (!mayRead && false)`: `Superseded_WithoutReadOnTheReplacement_IsNull_AndRevealsNothing` fails (1 failed, 33 passed).

## Known limits and things noticed

- K: the agreed contract takes a replacement from the new submission's `sprk_invoice` as it is. The intake's own-submission read additionally requires that the submission's id derive the invoice's; the replacement lookup does not, because the caller's own Read on the replacement is what stops a mis-pointed column from disclosing anything.
- K: an invoice with no firm or no single core record is 503 rather than a body with null parts, because the agreed shape has no null firm or record.
- Noticed, not changed: `OperationAccessPolicy` has no operation key named for "correct an invoice"; the shared `ebilling.invoice.decide` is Write on the invoice, which is the right the intake asks.
