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

## Fix round 1

Review of 3ffb780a76. Source and test changes are on `task/ebilling-iss043` (the commit after 3ffb780a76).

### F2 - the regression theory proved nothing (tests/integration/regression/Ebilling/RateCardImportMalformedMultipartReturns400Tests.cs)

- Changed: the theory `Import_AMalformedMultipartBody_Returns400ProblemDetails_NotA500` posts to `ValidImportUrl` (a valid `procedureVersionId`), so the
  route reads the body. The unused `ImportUrl` field is removed. A helper `AssertTheBodyWasRefused` asserts the problem has exactly one error and
  `errors[0].column == "file"` (a query problem names its own key, so a query 400 cannot satisfy it); the theory, the no-closing-boundary test and the
  failing-stream test use it.
- Fail-before: with the route's `catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)` disabled (filter temporarily
  `when (ex is InvalidDataException && false)`), the theory FAILS for both cases (`empty-multipart`, `section-without-content-disposition`):
  `Failed!  - Failed: 2, Passed: 5, Total: 7` for the class. With the catch restored the class passes (7 of 7, including the two tests added below).

### F1 - the 400 for a server-side temp-disk fault was silent (four routes)

- Changed: each `catch (IOException ...)` now logs a Warning, `A multipart request body could not be read ({ExceptionType}, HResult 0x{HResult:X8}); answered 400.`,
  with `ex.GetType().Name` and `ex.HResult` only (the exception object and its message are not logged). The 400 is kept.
  - `Api/Finance/ProcedureEndpoints.cs` (`PutVersionFile`): uses the handler's existing `ILogger<StoredFileService> logger`.
  - `Api/Finance/StoredFileEndpoints.cs` (`UploadInvoiceFile`, the task-123 reference): uses the handler's existing `logger`.
  - `Api/Finance/RateCardImportEndpoints.cs` (`ImportRateCard`): the handler had no logger; added the parameter `ILogger<RateCardImportService> logger`
    (minimal API DI; no registration, no other caller of the handler).
  - `Api/Finance/InvoiceSubmissionHttp.cs` (`ReadSubmissionFormAsync`, shared by both channels): a static helper with no logger and a signature two routes
    share; takes the logger from `request.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InvoiceSubmissionHttp).FullName!)`,
    as `InvoiceSubmissionEndpoints.SourceServices` already does. No signature change.
- Tests (one per route family; each asserts level Warning, the message contains `IOException` and `HResult`, does not contain the IOException's message
  text, and `Exception` is null):
  - `ProcedureEndpointsTests.File_ABodyStreamThatFails_LogsAWarning_WithoutTheExceptionMessage`
  - `RateCardImportMalformedMultipartReturns400Tests.Import_ABodyStreamThatFails_LogsAWarning_WithoutTheExceptionMessage`
  - `InvoiceSubmissionEndpointsContractTests.A_body_stream_fault_logs_a_warning_without_the_exception_message` (both channels)
  - `InvoiceFileUploadTests.BodyStreamFault_400_LogsAWarning_WithoutTheExceptionMessage` (stored-file route)
  Test plumbing: `MalformedMultipartBodies.LogCapture` (an `ILoggerProvider` that records level, formatted message and exception) and `FaultMessage`; each host
  registers it; the StoredFile host also gained the `OnRequest` hook the other three hosts already had.

### K2 - a client connection reset is not a malformed body

- Changed: all four catches are `catch (IOException ex) when (!ct.IsCancellationRequested && ex is not Microsoft.AspNetCore.Connections.ConnectionResetException)`,
  so a reset propagates as before.
- Tests (the body stream throws `ConnectionResetException` on its first read, the token is NOT cancelled; the request throws instead of answering 400 and
  nothing is stored or written; `MalformedMultipartBodies.ApplyConnectionReset`):
  - `ProcedureEndpointsTests.File_AConnectionReset_IsNotA400`
  - `RateCardImportMalformedMultipartReturns400Tests.Import_AConnectionReset_IsNotA400`
  - `InvoiceSubmissionEndpointsContractTests.A_connection_reset_is_not_a_400` (both channels)
  - `InvoiceFileUploadTests.ConnectionReset_IsNotA400` (stored-file route)

### Verification

- Fail-before for the new F1 and K2 tests: with the four source files reverted to 3ffb780a76, all 10 new test cases FAIL (Procedure 2, rate card 2,
  stored file 2, submission 4 across both channels); with the fix applied they pass.
- `Sprk.Bff.Api.Tests` filtered to RateCardImportMalformedMultipartReturns400Tests, RateCardImportEndpointsTests, ProcedureEndpointsTests,
  InvoiceSubmissionEndpointsContractTests, InvoiceFileUploadTests, InvoiceFileUploadContractTests, StoredFileEndpointsTests,
  MalformedMultipartUploadReturns400Tests, ExternalInvoiceFileRoutesTests (`xUnit.MaxParallelThreads=4`): 750 passed, 0 failed.
- `Spaarke.ArchTests` filtered to RouteAuthorizationGuardTests and ExternalFileStoreRouteCensus: 114 passed, 0 failed.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
