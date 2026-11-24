# ISS-043 (#2978) + ISS-035 (#2889): multipart input

Task `iss043-multipart-malformed-body`. Branch `task/ebilling-iss043` (base `f5ec8b6a20`).

## What changed

### ISS-043: a malformed multipart body is the route's 400, not a 500

`ReadFormAsync` throws `System.IO.IOException` for a body with no closing boundary, truncated part headers, or a non-multipart body under a
multipart content type. Three routes caught only `InvalidDataException` and `BadHttpRequestException`, so the IOException reached the global
handler (500 `server_error`, logged at Error). Each route now has `catch (IOException) when (!ct.IsCancellationRequested)` after its existing
catches, answering with that route's existing 400 (the pattern task 123's `StoredFileEndpoints` upload route already uses). Changed in place;
no shared helper, because the three 400 shapes differ and a helper would be a new surface.

| Route | File | 400 shape (unchanged) |
|---|---|---|
| Invoice submission, internal and external (shared reader) | `Api/Finance/InvoiceSubmissionHttp.cs` `ReadSubmissionFormAsync` | `file_required` problem |
| Procedure Version file `PUT .../versions/{v}/file` | `Api/Finance/ProcedureEndpoints.cs` | `procedure_version.file_missing` |
| Rate card import `POST /api/finance/ratecards/import` | `Api/Finance/RateCardImportEndpoints.cs` | `ratecard.import_invalid` (field `file`) |

A client disconnect (the request's token is cancelled while the body is read) is not caught: it passes through like the cancellation it is,
never a 400 at Error. `BadHttpRequestException` derives from `IOException`; the existing 413 and 400 catches precede the new one, so their
behaviour is unchanged.

### ISS-035: a repeated `firmInvoiceNumber` or `poNumber` is refused

`ReadSubmissionFormAsync` read both with `form[...].ToString()`, so a field sent twice became `"A,B"`. The correction fields' "sent at most
once" rule was a local function inside `InvoiceSubmissionCorrectionFields.ReadForm`. It is now one internal helper,
`InvoiceSubmissionHttp.SingleFormValue(form, name, errors)`, used by both the reader and `ReadForm` (one convention, not a second).
It lives in `InvoiceSubmissionHttp.cs`, not `InvoiceSubmissionEndpoints.cs`, because the route-authorization scan follows the files an
external route file names, and `InvoiceSubmissionEndpoints.cs` carries the internal Finance filter.

A repeated field returns `ProblemDetailsHelper.ValidationProblem` (400, `errors.{field} = ["{field} is sent at most once."]`), the same shape
the internal route's correction field errors already use, before the file is copied or anything is written. A field sent once, or not sent,
behaves as before (an absent field reads as an empty string, as `ToString()` did, so the existing `firm_invoice_number_required` is unchanged).

Wire note: on the external SPA route this 400 has no top-level `errorCode` (the validation-problem shape). It applies only to the new
refusal; every other refusal keeps its `errorCode`. spa-r4 reviews the external route per ISS-035.

## Tests

New helper `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/MalformedMultipartBodies.cs` (a body with no closing boundary; a body stream that throws
IOException, optionally cancelling the request token first). `ProcedureEndpointsTests.Host`, `RateCardImportEndpointsTests.Host` and
`InvoiceSubmissionEndpointsContractTests` gained an `OnRequest` hook (replaces the body and token before the route runs).

| Criterion | Test (DisplayName prefix) | File |
|---|---|---|
| 400 not 500, no closing boundary: submission (internal + external) | `ISS-043 must-fire: a multipart body with no closing boundary is 400 file_required on both channels` | `tests/integration/contract/Api/Finance/InvoiceSubmissionEndpointsContractTests.cs` |
| 400 not 500, no closing boundary: Procedure file | `ISS-043 must-fire: PUT /file whose multipart body has no closing boundary is 400 file_missing` | `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/ProcedureEndpointsTests.cs` |
| 400 not 500, no closing boundary: rate card import | `ISS-043 must-fire: a multipart body with no closing boundary is a 400 ratecard.import_invalid` | `tests/integration/regression/Ebilling/RateCardImportMalformedMultipartReturns400Tests.cs` |
| IOException on the body stream is a 400 (all three) | `ISS-043 must-fire: a body stream that fails with an IOException (token not cancelled) ...` | same three files |
| Cancelled request is not a 400 (all three) | `ISS-043 must-not-fire: a body that fails because the client abandoned the request ...` | same three files |
| Repeated `firmInvoiceNumber` / `poNumber`: 400 field error, nothing written (both channels) | `ISS-035 must-fire: a firmInvoiceNumber or poNumber sent twice ...` | `InvoiceSubmissionEndpointsContractTests.cs` |
| Single value succeeds as before (both channels) | `ISS-035 must-not-fire: a firmInvoiceNumber and a poNumber sent once are used as sent (202)` | `InvoiceSubmissionEndpointsContractTests.cs` |

Run: `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "DisplayName~ISS-0" -- xUnit.MaxParallelThreads=4`.

### Fail-before evidence

Source files restored to base `f5ec8b6a20` (`git checkout HEAD -- the four src files`), tests kept, the same command run:

```
Failed!  - Failed:    12, Passed:   169, Skipped:     0, Total:   181
```

The 12 failures: no closing boundary x4 (submission internal, submission external, Procedure, rate card), body-stream IOException x4
(submission x2, Procedure, rate card), repeated field x4 (`firmInvoiceNumber` and `poNumber`, each on both channels). The three
must-not-fire "abandoned request" tests and the two "single value" tests pass before and after (they are controls: they prove the
fix did not widen the catch and did not change single values).

After the fix: `Passed!  - Failed: 0, Passed: 181, Skipped: 0, Total: 181` for the same filter. The wider affected classes
(`ProcedureEndpointsTests`, `RateCardImport*`, `InvoiceSubmission*` contract and service tests, `InvoiceFileUploadContractTests`,
`StoredFileEndpointsTests`, `InvoiceCorrectionPrefill*`): 993 passed, 0 failed.

## Found in passing (not fixed here)

- `RateCardImportMalformedMultipartReturns400Tests` (existing) uses `ImportUrl` with `firmId` and `name` but no `procedureVersionId`, so the
  route answers its query 400 before it reads the body. Its two existing cases assert 400 and `ratecard.import_invalid`, which that query 400 also
  satisfies, so they do not prove the malformed-body handling. The new tests in the same file use `ValidImportUrl`.
