# 095f-2e1: from-document requires a note on a Correction (EB-D-237)

Task: `projects/ebilling-r1/tasks/095f-2e1-from-document-correction-note.poml`. Design: `notes/095f-part2-decision-notes-design.md` §6.1, §15.
Branch: `task/ebilling-095f-2e1`, from `work/ebilling-r1` at 69745e926d. Rigor FULL (a Correction without a note becomes a 400 on a submission route).

## What changed (server and tests only; no client, no internal multipart route)

| File | Change |
|---|---|
| `Api/Finance/InvoiceSubmissionEndpoints.cs` | `SubmitInvoiceFromDocumentRequest.CorrectionNote` (+ internal `CheckedCorrectionNote`); `Validate` adds part 1's rules word for word (note on New, over 2000, control characters: field errors on `correctionNote`); the handler refuses a Correction with a missing or blank note as 400 `ebilling.submission.correction_note_required`, after Validate and before `PrepareCorrectionAsync`; the note goes into `InvoiceSubmissionRequest.CorrectionNote`; route description updated. |
| `Services/Finance/Submission/InvoiceSubmissionContracts.cs` | `InvoiceSubmissionRequest.CorrectionNote` (read only by `SubmitExistingDocumentAsync`). |
| `Services/Finance/Submission/InvoiceSubmissionService.cs` | `TrySubmitJobAsync` takes the note and passes it to `BuildJob(..., correctionNote)`; both callers (a new attempt, and the re-queue of a still-Received retry) pass `request.CorrectionNote` when the request is a Correction. |
| `Services/Finance/Submission/FromEmailSubmissionContracts.cs`, `FromEmailSubmissionService.cs` | New constant `FromEmailSubmissionErrors.CorrectionNoteRequiredDetail`; the two from-email refusals and the new from-document one use it, so the detail text has one source (a literal moved, no behaviour change). |

The persister and the job handler are unchanged: they already carry and write the note for any Correction (`InvoiceSubmissionPersister.cs:605-610`, `:1687-1690`; `InvoiceSubmissionProcessingJobHandler` parses `correctionNote`).

## Order of checks on the route

1. The filter's `ResolveFromDocumentTargets` runs `Validate` (field errors are a 400 before any rights query).
2. Handler: `Validate` again, then the coded 400 for a Correction with no note (nothing read, nothing written, no rights asked beyond the filter's own two record checks).
3. `PrepareCorrectionAsync` (the caller's Write on the invoice), then the rest as before.

## Behaviour before and after

| Request | Before | After |
|---|---|---|
| New (or no mode), no note | 202 | unchanged |
| New (or no mode) with a note, even `""` | note ignored (unknown member) | 400 field error `correctionNote: "correctionNote is not sent for mode New."` |
| Correction, note missing, null or blank | 202, job with no note, persister warns | 400 `ebilling.submission.correction_note_required` ("A correction needs a note saying why the invoice is corrected."), before any read |
| Correction, note 2001 characters or with `\u0007` | 202 | 400 field error on `correctionNote` |
| Correction, note up to 2000 characters | 202, note dropped | 202; trimmed note on the job payload (`correctionNote`); persister writes `sprk_correctionnote` on the new invoice |

Breaking change: a from-document Correction without a note was accepted and is now refused (the owner's decision, EB-D-237). The only client of the route is `emailInvoiceSubmission.ts`, which sends New only.

## Tests

New, in `tests/integration/contract/Api/Finance/InvoiceSubmissionEndpointsContractTests.cs` (all named `095f-2e1 ...`): note on New (mode New and absent) is a field error; New without a note unchanged and its job carries no note (must-not-fire); Correction without, null, empty and blank note gives the coded 400 with zero reads (no invoice read, no rights asked, nothing written); a 2000-character note with line breaks and a tab is accepted whole and rides the job; 2001 characters and a control character are field errors (no `errorCode`); the job payload carries the note; a retry of a still-Received Correction re-queues the job with the note.
New, in `InvoiceSubmissionPersisterCorrectionNoteTests.cs`: a Correction that carries a note never logs the no-note warning (must-not-fire) and a must-fire control proves the warning still fires without one. `CorrectionStore` gained an optional logger parameter for this.
Existing: `CorrectionBody` in the contract tests now sends a note by default (a Correction without one is now a 400).

Fail-before proof: with the five source files reverted to the base, 10 of the 13 new tests fail and 3 pass (New without a note, and the two persister tests, which pin behaviour that already existed). All 13 pass with the change.

## Component justification (CLAUDE.md section 11)

No new service, endpoint, DI registration, package, column or file surface. One property each on two existing request contracts, one constant, one optional parameter. Cost of doing nothing: a from-document Correction would go on reaching the persister with no note (EB-D-237's gap).

## BFF hygiene (section 10)

- **Placement:** existing route, request contract and service; nothing new in DI, no package, no background work.
- **Publish-size delta:** `dotnet publish -c Release` of the base (source files reverted to 69745e926d) against the change, same machine: total 140,400,617 to 140,402,513 bytes, **+1,896 bytes (about 1.9 KB)**, all in `Sprk.Bff.Api.dll` (28,011,520 to 28,013,056). Far under the +5 MB threshold. (The base is the `work/ebilling-r1` tip, not a fresh master build: the delta of this task is what the comparison isolates. The 140 MB total is the self-contained linux-x64 output of this csproj's publish settings, not the figure the 55 MB gate reads.)
- **CVE check:** `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive` against nuget.org: "has no vulnerable packages". No package was added or changed.
- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror`: 0 warnings, 0 errors.

## Test results

- `Sprk.Bff.Api.Tests` filtered to Finance, Submission, InvoiceSubmission and FromEmail: 9,185 tests, 9,180 passed, 4 skipped, **1 failed**. The failure is `FirmOutcomeScopeTests.RejectedSubmission_OfTheCallersFirm_Is200_WithItsFindings` (`130c happy path ...`, `tests/integration/auth/Finance/FirmOutcomeScopeTests.cs:1027`): the firm reason is "A billed rate is higher than the rate approved for this timekeeper." where the test expects "Your rate is above the agreed rate.". **Pre-existing:** it fails identically with the five source files reverted to 69745e926d. It is a firm-outcome reason-text drift, not this task's code; reported, not fixed (scope).
- `InvoiceSubmissionEndpointsContractTests` and `InvoiceSubmissionPersister*`: 514 passed, 0 failed (before the full run).
- `Spaarke.ArchTests`: 2,650 passed, 0 failed.

## Known limits and observations (not changed)

- A retry of a still-Received from-document Correction is answered as before; the "same details?" comparison (`AnswerWithEarlierAsync`) does not include the note, so a retry with a different note is accepted and the re-queued job carries the retry's note (the job's idempotency key is the submission id, so a job that already ran is not run again). From-email handles its retry the same way except that it refuses a retry with no note; here the handler already refuses any Correction with no note before the intake.
- The internal multipart route and the external SPA route still accept New only (§16 option B'); a later task may add Correction there.
- `InvoiceSubmissionEndpoints.cs` documents the route in an OpenAPI description string only; the client type in `emailInvoiceSubmission.ts` is not changed in this task (no client change).

## Review (2026-11-16): one adversarial pass, no F-class finding

Known limits:
- **K1, retry note.** A retry of a still-Received Correction with a different note re-queues the job with the retry's note, but the first job the handler processes wins (processing lock, `IdempotencyKey = submissionId`); a later message finds the submission moved on and does nothing. Both texts are the firm's, and there is no wrong-invoice outcome. From-email's idempotency key excludes the note too (it is only in the response-cache binding), and from-document has no such cache.
- **K2, service-level rule.** `SubmitExistingDocumentAsync` accepts a `CorrectedInvoice` with no note; the route is its only caller and refuses that before the service. A second caller would have to enforce the rule itself (from-email enforces it in its service).
- **K3, "zero reads" wording.** The 400 comes after the route filter's authorization reads (parity with from-email; authorization before validation is correct). The tests prove the handler's order, not the filter's.
