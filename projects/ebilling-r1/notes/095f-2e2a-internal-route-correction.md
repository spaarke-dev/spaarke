# 095f-2e2a: the internal upload route accepts a Correction (EB-D-243, EB-D-245, option B′)

Task: `projects/ebilling-r1/tasks/095f-2e2a-internal-route-correction.poml`. Design: `notes/095f-part2-decision-notes-design.md` §16 (option B′), §15 Q5.
Branch: `task/ebilling-095f-2e2a`, from `work/ebilling-r1` at 10bf8854c0. Rigor FULL (submission write path, invoice supersede).

## What changed (server and tests only; no client change, no package change)

| File | Change |
|---|---|
| `Api/Finance/InvoiceSubmissionEndpoints.cs` | The internal multipart handler (`SubmitAsync`, used by the matter, project and work assignment routes) reads `mode`, `invoiceId` and `correctionNote` from the parsed form, refuses a Correction with no note with from-document's coded 400, calls the existing `PrepareCorrectionAsync`, and sets `InvoiceSubmissionRequest.CorrectedInvoice` and `CorrectionNote`. New `InvoiceSubmissionCorrectionFields`: the field rules, now one source for the from-document body (`SubmitInvoiceFromDocumentRequest.Validate` calls it; its rules and messages are unchanged) and the multipart form (`ReadForm`). Class remarks document the fields. |
| `Api/Finance/InvoiceSubmissionHttp.cs` | `WithSubmitContract(coreNoun, acceptsCorrection = false)`: the internal routes pass `true`, which appends `CorrectionContract` to the description and lists 404. The external route passes nothing, so its contract is unchanged. |
| `Services/Finance/Submission/InvoiceSubmissionService.cs` | The Correction check block of `SubmitExistingDocumentAsync` (step 4a) moved into one private `CheckRequestedCorrectionAsync`, which `SubmitAsync` now calls too (new step 2c, after the reader/store check and before the first write). `WriteAsync` writes `sprk_resubmissionof` from its answer and passes the note to `BuildJob`. One private `JobCorrectionNote` (note only when `CorrectedInvoice` is set) replaces the two inline copies on the from-document path. `SubmitAsync` throws `ArgumentException` for a `CorrectedInvoice` on any channel but Internal (caller defect). |
| `Services/Finance/Submission/InvoiceSubmissionContracts.cs` | Doc comments of `CorrectedInvoice` and `CorrectionNote` name the second reader. |

Unchanged: `CheckCorrectionAsync` / `JudgeCorrectionAsync`, `PrepareCorrectionAsync`, `SubmissionCommitGate`, the persister's supersede path and note write, the job handler, the external route and `ReadSubmissionFormAsync`.

## Form-field contract (for 095f-2e2b, the wizard)

Route: `POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions?firmId={firm}`, `multipart/form-data`. Existing fields unchanged: `file` (PDF), `firmInvoiceNumber` (required), `poNumber`.

| Field | Values | Rule |
|---|---|---|
| `mode` | `New` (the default when absent) or `Correction`, exact case | Any other value, including an empty one, is a field error. |
| `invoiceId` | GUID of the invoice being corrected | Required for `Correction` (missing or `Guid.Empty`: "invoiceId is required for mode Correction."; not a GUID: "invoiceId must be a GUID."). Not sent for `New` ("invoiceId is not sent for mode New."). |
| `correctionNote` | Why the invoice is corrected | Required for `Correction`; trimmed; at most 2000 characters; no control characters other than line break and tab. Not sent for `New`, even empty ("correctionNote is not sent for mode New."). |

Each field is sent at most once ("{field} is sent at most once."). A present field counts as sent whatever its value, as a non-null JSON member does on from-document. The client should send nothing for New, and all three for Correction. `firmInvoiceNumber` must be the target's own number. The firm (`firmId`) and the route's record must be the target's.

Answers, in the order the route decides them:

| Status | Code | When |
|---|---|---|
| 403 / 503 | (Finance filter, kill switch) | As today: the core record and firm checks run first. |
| 400 | `ebilling.submission.firm_invalid` | `firmId` missing or not a GUID (unchanged). |
| 413 / 400 | `too_large` / `file_required` | Body too large, or not multipart (unchanged). |
| 400 | ValidationProblem, `errors.mode` / `errors.invoiceId` / `errors.correctionNote` | A field error above. No `errorCode`. Nothing read. |
| 400 | `ebilling.submission.correction_note_required`, detail "A correction needs a note saying why the invoice is corrected." | A Correction with no note, an empty one or a blank one (from-document's code, status and words, `FromEmailSubmissionErrors`). Nothing read. |
| 404 | `reasonCode: sdap.access.deny.record_unavailable` | The invoice is not one the caller can Write, or does not exist (the uniform answer, identical bytes). |
| 503 | `ebilling.submission.record_unavailable` | The invoice could not be read. |
| 400 / 413 / 403 / 422 / 503 | existing codes | The service's own checks, unchanged (number, file, PDF, firm, record, firm link, owner, reader, store). |
| 409 | `ebilling.submission.correction_not_allowed` with `reason` (`invoice_status` plus `invoiceStatus`, `different_firm`, `different_core_record`, `different_invoice_number`, `no_submission`, `submission_in_progress`) | The shared intake check refuses. Nothing staged, created or queued. |
| 202 | `{submissionId, jobId, trackingNumber, statusUrl}` | Received. `trackingNumber` is the next `.n` of the same firm invoice number (`{number}.{n}`, D-37). |

On 202: the submission names the replaced invoice's own submission in `sprk_resubmissionof`; the job payload carries `correctionNote`; at commit the persister writes `sprk_correctionnote` on the new invoice, links it (`sprk_supersedesinvoice`) and closes the old invoice Superseded in the same transaction, re-checked by the commit gate (unchanged code). Prefill for the wizard: `GET /api/finance/invoices/{invoiceId}/correction-prefill` (unchanged).

The external (SPA) route reads none of these fields: a `mode`, `invoiceId` or `correctionNote` sent to it is ignored, as before (must-not-fire test). The service throws if a Correction ever reaches it on the SPA channel.

## The shared check

`InvoiceSubmissionService.CheckRequestedCorrectionAsync(request, number, ct)`: no read for a New request; otherwise `CheckCorrectionAsync(request.CoreRecord, request.FirmId, number, request.CorrectedInvoice, ct)`. Both intake entries call it: `SubmitAsync` (upload, step 2c) and `SubmitExistingDocumentAsync` (from-document, step 4a). From-email still calls `CheckCorrectionAsync` directly. All three reach the same single eligibility check, and no copy of its logic exists. The from-document refactor is a pure move: the same call, at the same point, with the same arguments, and its suites pass unchanged.

Order on the upload entry: payload validation, then `DecideAsync` (firm active, record, firm link, owner), then the reader/store check, then the Correction check, then the stage, the create (with `sprk_resubmissionof`) and the job (with the note). This is from-document's order (its 4-reader before 4a).

## Tests

All named `095f-2e2a ...`:

- **Contract** (`tests/integration/contract/Api/Finance/InvoiceSubmissionEndpointsContractTests.cs`):
  - A Correction is 202. It records `sprk_resubmissionof` and the Internal channel, allocates the target's firm and number (receipt `INV-7.2`), puts the note on the job, stages the PDF, and asks for Write as the caller.
  - A 2000-character note with line breaks and a tab rides the job trimmed.
  - No, empty or blank note: the coded 400, with no invoice read, no Write asked and nothing written.
  - Without Write, or for an unknown invoice: the uniform 404, byte-identical, with nothing written.
  - Six 409 reasons (the brief's another firm, another core record, not In Review and different number, plus no submission and submission in progress): nothing written.
  - Nine field-error shapes, a note on New (mode New or absent), and each field sent twice: field errors, with nothing asked and nothing written.
  - Must-not-fire: New with no mode, or with mode New, is unchanged (no `sprk_resubmissionof`, no invoice asked, no note, no submission query).
  - Must-not-fire: the external route ignores all three fields.
  - OpenAPI: the internal routes name the fields and list 404; the external route's contract has neither.
- **Intake** (`tests/unit/.../InvoiceCorrectionIntakeTests.cs`), on `SubmitAsync`:
  - An allowed Correction records the target, allocates the same number and carries the note.
  - A refused Correction (status, another number) stages, creates and queues nothing.
  - New is unchanged.
  - A SPA Correction throws.
- **Persister** (`tests/unit/.../InvoiceSubmissionPersisterCorrectionNoteTests.cs`): an Internal upload-shaped Correction (no source document) commits with `sprk_supersedesinvoice`, the old invoice Superseded in the one transaction, and the note on the new invoice.

**Fail-before:** the tests were written and run against the unchanged source (base 10bf8854c0): 36 tests, **31 failed, 5 passed**. The 5 that passed pin existing behaviour: New unchanged (2 contract, 1 intake), external ignores the fields, and the persister commit. After the change all 36 pass.

**Results:**
- `Sprk.Bff.Api.Tests` filtered `FullyQualifiedName~InvoiceSubmission|Correction|FromDocument|FromEmail|SubmissionCommitGate|InvoiceSubmissionPersister`: **2,222 passed, 0 failed**.
- `Spaarke.ArchTests` filtered `Invoice|RouteAuthorization`: **204 passed**.
- `Finance|Submission|Ebilling|Correction`: **119 passed**.

## Component justification (CLAUDE.md section 11)

No new endpoint, service, DI registration, package, column or file surface. New code is two private methods and one internal static helper class:

- `CheckRequestedCorrectionAsync` is the move the task asks for.
- `JobCorrectionNote` replaces two inline copies.
- `InvoiceSubmissionCorrectionFields`:
  1. **Existing:** the from-document body's mode and note rules in `Validate`.
  2. **Extension:** those rules are extracted, not copied, and from-document now calls the extract.
  3. **Cost of doing nothing:** the two routes would carry two copies of the same field rules, and they would drift.

## BFF hygiene (section 10)

- **Placement:** existing internal upload route, existing request contract and existing service; nothing new in DI, no package, no background work.
- **Publish-size delta:** compared `dotnet publish -c Release` of the change and of the base (the four source files reverted to 10bf8854c0) on the same machine. Total went from 140,504,165 to 140,514,249 bytes, **+10,084 bytes (about 10 KB)**. `Sprk.Bff.Api.dll` grew +9,216 bytes, mostly the OpenAPI description and field-error strings. Far under +5 MB. The base is the branch tip, not a fresh master build, so the comparison isolates this task's delta.
- **CVE:** `dotnet list ... package --vulnerable --include-transitive` reports "has no vulnerable packages". No package changed.
- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` succeeded.

## Known limits

- **K1, service-level note rule (as 2e1's K2).** `SubmitAsync` and `SubmitExistingDocumentAsync` accept a `CorrectedInvoice` with no note. Their routes refuse that before the service. Enforcing it in the shared method would change the from-document service's contract (existing intake tests call it without a note), so it was left out of scope.
- **K2, no retry identity on the upload.** Each upload is a new submission id. Two Corrections of the same invoice can both be received. The commit gate re-checks the target at commit, so only the first to commit supersedes it and the second is refused at its commit (the target is no longer In Review). This is the from-email channel's behaviour too. The wizard should disable resubmit while a request is in flight.
- **K3, order of checks.** The field errors and the coded note 400 come after the route filter's record and firm checks and after the form is buffered (the multipart body must be read to see the fields). The tests prove the handler's order, not the filter's.
- **K4, the 404 is checked before the service's own validation.** A Correction with a missing file or number still asks for Write on the invoice first, then gets the service's 400. This is from-document's order (its Write check comes before the service), and nothing is written.
