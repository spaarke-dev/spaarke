# ISS-049 (#3057): multipart input on the chat document, visualization and SPE admin upload routes

Task `iss049-multipart-other-routes`. Branch `task/ebilling-iss049` (base `72f2e54503`, the tip of `work/ebilling-r1`).

## What changed

Same handling as iss043 (notes/iss043-multipart-input.md): a truncated multipart body is the route's 400, logged at Warning with the exception
type and HResult only; a client disconnect or cancellation is neither a 400 nor a fault log. Only the read-form error handling changed on each
route (no auth, limit or success-path change, no new endpoint, service, DI registration, package or option).

| Route | File | Before | After |
|---|---|---|---|
| `POST /api/ai/chat/sessions/{sessionId}/documents` | `Api/Ai/ChatDocumentEndpoints.cs` | `IOException` from `ReadFormAsync` reached the global handler: 500, logged at Error | `catch (IOException) when (!RequestAborted.IsCancellationRequested && not ConnectionResetException)` after the existing `InvalidDataException`/`BadHttpRequestException` catch: the route's existing 400 ProblemDetails ("Request body is not valid multipart/form-data."), Warning with type and HResult |
| `POST /api/ai/visualization/related-from-content` | `Api/Ai/VisualizationEndpoints.cs` | same | same catch (token: the handler's `cancellationToken`) |
| `POST /api/spe/containers/{containerId}/items/upload` | `Api/SpeAdmin/ContainerItemEndpoints.cs` | one catch-all (every exception but `SdapProblemException`): an abandoned upload (`OperationCanceledException`) was a 400 and a Warning with a stack trace | new `catch (IOException)` with the iss043 filter: the route's existing 400 `ValidationError`, Warning with type and HResult only (no exception object, no stack trace); the catch-all now excludes `OperationCanceledException` and `IOException`, so cancellation, an abandoned/reset IOException pass through; every other exception keeps the catch-all (400, Warning with the exception) |

## Reuse decision (CLAUDE.md §11)

- **Existing**: iss043's catch is an inline `catch (IOException ex) when (!ct.IsCancellationRequested && ex is not ConnectionResetException)` in each
  Finance route (no shared helper: `StoredFileEndpoints`, `ProcedureEndpoints`, `RateCardImportEndpoints`, `InvoiceSubmissionHttp`), because each route's 400 shape and logger
  differ. There is nothing to call.
- **Extension**: a shared helper would be new surface in `Api/Finance` (or a new shared file) for three AI/SPE-admin routes owned by other projects, with three more 400 shapes.
  So the filter is repeated inline, with the same text, per iss043's precedent. `Api/Finance` code is untouched (Finance behaviour byte-identical).
- **Cost of doing nothing**: an authenticated caller with a truncated upload gets a 500 and the BFF logs an Error for a client fault (chat, visualization); an abandoned SPE admin upload is logged with a stack trace and answered 400.

## Tests

| Criterion | Test (DisplayName prefix `ISS-049`) | File |
|---|---|---|
| 400 not 500 + Warning (type, HResult, no file name, no exception object): no closing boundary, chat + visualization | `must-fire: a multipart body with no closing boundary is a 400 (not a 500) ...` | `tests/integration/regression/MalformedMultipartUploadReturns400Tests.cs` |
| 400 + Warning without the message for an IOException body (token not cancelled), chat + visualization | `must-fire: a body stream that fails with an IOException ...` | same |
| Abandoned (token cancelled) or reset: not 400, no multipart Warning, chat + visualization | `must-not-fire: a body that fails because the client abandoned the request, or reset the connection ...` | same |
| Well-formed upload still read | `must-not-fire: a well-formed multipart upload is read as before ...` | same |
| SPE admin: same four, plus a cancelled read (`OperationCanceledException`) is not a 400 and logs no Warning | `ContainerItemUploadMultipartTests` (6 tests) | `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/ContainerItemUploadMultipartTests.cs` |

Test plumbing: `MalformedMultipartUploadReturns400Tests`' full-host fixture gained a `LogCapture` provider and a header-driven `IStartupFilter`
(`X-Test-Body-Fault: io | abandoned | reset`) that replaces the body and token first in the pipeline (reusing `MalformedMultipartBodies` from iss043). No SPE admin upload route
test existed; the new class maps `ContainerItemEndpoints.MapContainerItemEndpoints` on a bare `/api/spe` group over `TestServer` (the iss043 `ProcedureEndpointsTests` host pattern). The
Graph and audit services are never reached on the tested paths (the form is read first), so they are registered as uninitialized instances.

Run: `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~ContainerItemUploadMultipartTests|FullyQualifiedName~MalformedMultipartUploadReturns400Tests" -- xUnit.MaxParallelThreads=4`.

### Fail-before evidence

Three source files reverted to base `72f2e54503`, tests kept: `Failed!  - Failed: 9, Passed: 11, Total: 20`.
The 9: chat and visualization no-closing-boundary (2) and IOException body (2), each a 500 `server_error`; SPE admin no-closing-boundary, IOException body (no Warning of the new shape), abandoned
IOException, cancelled read, connection reset (5; the abandoned/cancelled/reset ones were answered 400 by the catch-all). The 11 passes are the controls
(chat/visualization abandoned and reset: they already propagated; well-formed uploads; SPE well-formed). After the fix: `Passed! - Failed: 0, Passed: 20, Total: 20`.

## Known limits

- K1: on the chat and visualization routes (full host) an abandoned or reset request still reaches the global handler as before (an escaped `IOException` is a 500 with an Error log there); only the 400-and-Warning mapping was
  changed, as the brief says. The test asserts "not 400 and no multipart Warning".
- K1: a body whose `ReadFormAsync` raises an `IOException` other than via cancellation or `ConnectionResetException` is a 400 even when it is a server-side temp-disk fault; that is why the Warning (type and HResult) exists (same limit as iss043 F1).
- K1: SPE admin: a `BadHttpRequestException` (an `IOException`) now takes the new catch (400, Warning without the exception object) rather than the catch-all's (400, Warning with the exception); the response is unchanged.
- The SPE admin well-formed control stops at the route's own empty-file refusal: a non-empty upload goes on to Graph, which needs a full SPE test host and is out of scope for a read-form change.
