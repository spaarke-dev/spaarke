# Task 050 — BFF error responses now carry `application/problem+json` (GitHub #975 / ADR-019)

**Status**: Fix applied, tested, gated. Worktree `agent-a1b2ce4181d0166bd`, not yet merged.

## 1. The defect

`UseSpaarkeMiddleware` (`src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs`,
the global `UseExceptionHandler` handler) did:

```csharp
ctx.Response.ContentType = "application/problem+json";
...
await ctx.Response.WriteAsJsonAsync(new { type, title, detail, status, correlationId, extensions });
```

`HttpResponseJsonExtensions.WriteAsJsonAsync`'s parameterless overload delegates to the 4-arg overload
with `contentType: null`, which unconditionally sets `Response.ContentType = contentType ??
"application/json; charset=utf-8"` — it never reads the header already on the response. So every
`SdapProblemException`-driven error (and the MSAL / Graph / unhandled-exception branches in the same
handler) was served as `application/json`, not `application/problem+json`, contrary to ADR-019 (MUST
return ProblemDetails, RFC 7807 media type) — even though the BODY was already the correct ProblemDetails
shape.

First observed and left deliberately unasserted by task 016's `DocumentIdentityContractTests` (see §2
below) — that comment is the origin of GitHub issue #975 / ISS-003.

## 2. Reproduction (test written and shown failing BEFORE the fix)

New file: `tests/integration/regression/Issue975_ProblemJsonContentTypeTests.cs` (ADR-038 §1 KEEP path
+ `Issue{N}_*Tests.cs` naming convention for "every bug = regression test"). Reuses the existing
`DocumentIdentityTestWebAppFactory` fixture and the exact malformed-URL repro
`DocumentIdentityContractTests` already uses (`DocumentUrlIdentityFilter.ParseDocumentUrl` throws
`SdapProblemException(invalid_id, 400)`, uncaught, all the way to `UseSpaarkeMiddleware`'s handler — no
new fixture, no new repro mechanism, per CLAUDE.md §11).

Two tests:
1. `ResolveIdentity_ForAMalformedUrl_SdapProblemExceptionResponse_CarriesProblemJsonContentType` — the
   defect reproduction + regression guard.
2. `ResolveIdentity_ForASuccessfulRequest_ContentTypeRemainsApplicationJson` — negative control (POML
   §3): a 2xx response's content type must stay `application/json`.

**Before the fix** (`dotnet test --filter FullyQualifiedName~Issue975_ProblemJsonContentTypeTests`):

```
Failed Sprk.Bff.Api.Tests.Integration.Regression.Issue975_ProblemJsonContentTypeTests
  .ResolveIdentity_ForAMalformedUrl_SdapProblemExceptionResponse_CarriesProblemJsonContentType [6 s]
  Error Message:
   Expected response.Content.Headers.ContentType!.MediaType to be "application/problem+json" ...
   but "application/json" has a length of 16, differs near "jso" (index 12).

Failed!  - Failed: 1, Passed: 1, Skipped: 0, Total: 2
```
(Test 2, the negative control, already passed — confirming the bug is scoped to the exception-handler
branch, not a blanket serialization issue.)

**After the fix**: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

Also restored the content-type assertion (and replaced the "NOT fixed here" comment) in the original
defect-discovery test, `DocumentIdentityContractTests.ResolveIdentity_ForAMalformedOrEmptyUrl_Returns400ProblemDetails_NotA500`
— it deliberately omitted this assertion in task 016 pending this fix. Now green with the assertion
restored (verified: 6/6 tests in that file pass).

## 3. The fix

`MiddlewarePipelineExtensions.cs`: pass the content type explicitly to `WriteAsJsonAsync` — the
framework's own mechanism, per the POML's stated preference — rather than hand-rolled header juggling
or switching to a different ProblemDetails type (which would risk a body-shape change, the task's second
escalation trigger):

```csharp
await ctx.Response.WriteAsJsonAsync(
    new { type = ..., title, detail, status, correlationId = traceId, extensions = ... },
    options: (JsonSerializerOptions?)null,
    contentType: "application/problem+json");
```

`options: null` reproduces exactly what the removed parameterless call did internally (both resolve to
the same DI-registered `JsonOptions` fallback) — the JSON body is unchanged. Only the `contentType`
argument is new. No change to the anonymous object's shape, property names, or values — the body is
byte-for-byte what it was (verified: the existing body-shape assertions in both the new test and the
untouched `DocumentIdentityContractTests` test continue to pass unmodified).

## 4. Consumer / blast-radius survey (per task constraint — every consumer checked)

This is the exact set of places in the repo that branch on a response's `Content-Type` header for error
handling. Found via `grep -rn "headers.get('content-type'" src/` (all response-side checks) plus direct
reads of the add-in's own `ApiClient.ts`, Compose widgets, and SpaarkeAi imports.

| Consumer | What it does with Content-Type | Verdict |
|---|---|---|
| `src/client/office-addins/shared/services/ApiClient.ts` (the add-in's own client, named explicitly in the task) | `handleErrorResponse` branches on `response.ok` (HTTP status) only; calls `response.json()` unconditionally regardless of the `Content-Type` header. Never inspects the header. | **Safe** — content-type-agnostic. |
| `src/client/shared/Spaarke.Compose.Components/src/widgets/*.ts(x)` (Compose) | Every checked call site (`useComposeWordShuttle.ts`, `useComposeReanchor.ts`, `ComposeWorkspace.tsx`) branches on `response.ok`/`response.status` for the error path; several go through `authenticatedFetch` (next row) for their error object. None inspect `Content-Type`. | **Safe**. |
| `src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts` (`tryParseProblemDetails`) — the canonical `@spaarke/auth` fetch wrapper; per `Sprk.Bff.Api/CLAUDE.md`'s "Client contract" section this is the shared mechanism for PCFs, Code Pages, and **SpaarkeAi** | `contentType.includes('application/json') \|\| contentType.includes('application/problem+json')` — an **OR** check that already accepts the RFC 7807 media type. | **Safe** — already written to accept both; `application/problem+json` matches the second branch. |
| `src/solutions/SpaarkeAi/**` | No direct `Content-Type` check anywhere in its own source (checked with a broad case-insensitive grep for `headers[...]`/`headers.get(...)`/`contentType` comparisons — zero hits). Imports `@spaarke/auth` in 15 files, i.e. relies entirely on `authenticatedFetch` above. | **Safe** (via the row above). |
| `src/client/shared/Spaarke.UI.Components/src/services/communicationTimelineApi.ts`, `communicationThreadListApi.ts`, `communicationApi.ts` | Identical OR pattern: `ct.includes('application/problem+json') \|\| ct.includes('application/json')`. | **Safe**. |
| `src/client/shared/Spaarke.UI.Components/src/services/BffDataverseClient.ts` (`tryParseProblemDetails`) | Same OR check, inverted form (`if (!includes(json) && !includes(problem+json)) return null;`). | **Safe**. |
| Test helper `AssertProblemDetailsAsync` (`tests/integration/contract/Integration/Workspace/WorkspaceEndpointsContractTests.cs`) | Already asserts `contentType.Should().Contain("problem+json")`. Ran `dotnet test --filter WorkspaceEndpointsContractTests` **before** touching production code: **34/34 passed** — these routes use `Results.ValidationProblem`/`Results.Problem` directly (the already-correct ASP.NET Core path, same class as `DocumentAuthorizationFilter`'s 403), not the buggy global handler. Unaffected either way. | **Safe / unaffected**. |
| Other C# test assertions of `MediaType.Should().Be("application/json")` (`StandaloneChatContextEndpointsTests`, `AnalysisChatContextEndpointsTests`, `HealthAndHeadersTests`, `WorkspaceLayoutEndpointContractTests`, `PipelineHealthTests`, `InsightsAssistantEndpointStreamingContractTests`) | Read each site: every one asserts on a **200 OK** response. The fix only touches the `UseExceptionHandler` branch — success responses are untouched by construction. | **Safe / unaffected**. |

**No shipped client checks for `application/json` exactly on an error path.** The escalation trigger
("if any shipped client would break, STOP and report") does **not** fire — every consumer either ignores
`Content-Type` entirely or already accepts `application/problem+json` via an OR check (several of these
were plausibly already written defensively anticipating this exact fix). No client code was changed; per
the task's own conditional wording ("if you change any client file, run that package's gates") the
office-addins jest/tsc/build gates were **not** re-run, since no client file was touched — the survey
above is read-only source analysis, not test execution. (Note for context: `ApiClient`'s own jest suite is
already one of the 10 pre-existing known-failing suites in the office-addins baseline, unrelated to this
defect.)

## 5. Related-but-out-of-scope finding (NOT fixed in this task — flagged for the owner)

The identical anti-pattern (`response.ContentType = "application/problem+json"` immediately followed by
`WriteAsJsonAsync(...)` with no explicit content type) also exists at three more sites, none of which are
the global handler the POML scoped this task to:

- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:536-537` (attachment-validation 4xx path)
- `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs:739-748` (SSE-stream 401 path)
- `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs:764-772` (SSE-stream 404 path)

These three are **not fixed here** — out of this task's declared scope (`<relevant-files role="modify">`
names only `MiddlewarePipelineExtensions.cs`; the `<goal>` says "every error response **the global
handler** writes"). `OfficeEndpoints.cs` is also adjacent to task 031's concurrent Office-area work in a
different worktree, which is a second reason not to touch it here. Recommend a follow-up
issue/task — same shape as how #975 itself originated from `defer-issues.md`.

Four other sites matched the same `ContentType = "application/problem+json"` grep but are **not** affected
— they call `response.WriteAsync(JsonSerializer.Serialize(...))` (a plain string write that does not
touch `ContentType`), not `WriteAsJsonAsync`: `WorkspaceMatterEndpoints.cs` (×2),
`WorkspaceFileEndpoints.cs`, `SummarizeSessionEndpoint.cs`, `ChatDocumentEndpoints.cs`.

## 6. Placement justification (root CLAUDE.md §10 / bff-extensions.md)

Modifies existing middleware only — no new endpoint, service, DI registration, or package. Per CLAUDE.md
§11, a modification to an existing file needs no new-component justification; noted here for the record
per the BFF hygiene checklist.

## 7. Gate results

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 Warning(s), 0 Error(s) (confirmed twice) |
| New regression test (before → after) | Before: `Failed: 1, Passed: 1` (header assertion fails: expected `application/problem+json`, got `application/json`). After: `Passed: 2, Failed: 0` (confirmed on 2 separate runs). |
| `DocumentIdentityContractTests` (defect's origin file) | 6/6 passed |
| Documents test group (`FullyQualifiedName~.Documents.`) | 68/68 passed |
| Office test group (`FullyQualifiedName~.Office.`) | 261 passed, 10 skipped (pre-existing, unrelated), 0 failed |
| Combined Documents + Office group (re-run) | 329 passed, 10 skipped, 0 failed, 339 total |
| ArchTests | **191 passed, 0 failed, 191 total** — matches branch baseline exactly (confirmed on 2 separate runs) |
| Full `Sprk.Bff.Api.Tests` suite | **12,316 passed, 0 failed, 56 skipped, 12,372 total** (branch baseline: 12,314 passed / 0 failed / 56 skipped). The +2 passed vs. baseline is exactly the 2 new tests in `Issue975_ProblemJsonContentTypeTests.cs`; skip count unchanged; zero failures. Duration 38m34s. |
| `dotnet list package --vulnerable --include-transitive` | "has no vulnerable packages given the current sources" — no new HIGH CVE (confirmed twice) |
| Publish size vs fresh `origin/master` build | master (`e0a6f87c4`, PowerShell `Compress-Archive -CompressionLevel Optimal`, incl. PDBs) = 45.35 MB (47,556,010 bytes); branch = 45.41 MB (47,614,896 bytes); **delta +0.06 MB** — far under the +5 MB single-task escalation threshold and the 60 MB hard ceiling. Confirmed on 2 separate measurements (byte-identical branch zip both times). Temporary worktree at `C:\wt-master-050` removed after each measurement. |
| Step 9.5 `code-review` (synchronous, this session) | 0 Critical / 0 Warning / 0 Suggestion across all dimensions (security, performance, style, AI-code-smell ×5, ADR, BFF Hygiene, Component Justification). Quality direction: Improved (stale misleading comment removed, coverage restored). |
| Step 9.5 `adr-check` (synchronous, this session) | 0 Violations / 0 Warnings across 9 directly-implicated ADRs + ADR-038 testing rules + BFF Hygiene §A. One pre-existing, out-of-diff observation noted (Graph type reference elsewhere in the unchanged part of the file) — not attributable to this change. |
| Client gates (tsc/build/jest) | Not run — no client file was modified (see §4); POML's client-gate instruction is conditional on a client-file change. |

## 8. Deviations from the POML

- Also updated `tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs`: restored the
  content-type assertion that task 016 had deliberately left out (with a long "NOT fixed here" comment),
  and replaced that comment with a short pointer to this fix + the new regression test. This is a
  modification to an existing test file (no new-component justification needed per CLAUDE.md §11) and
  keeps the codebase's comments accurate now that the defect they described no longer exists.
- Found three additional instances of the same anti-pattern outside this task's scope — documented in §5
  above rather than fixed, per the POML's explicit scoping to the global handler.
