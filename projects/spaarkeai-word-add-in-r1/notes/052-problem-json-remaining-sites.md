# Task 052 — `problem+json` content-type defect at the three remaining hand-rolled sites (GitHub #975)

**Status**: Fix applied, tested, gated. Worktree `agent-a0d7af3eaa71666e8`, base commit `12a0b4258`, not yet merged.

## 1. The three sites (re-located by symbol, not line number)

Task 050 fixed the global exception handler and, in its blast-radius survey, found the identical
anti-pattern (`Response.ContentType = "application/problem+json"` immediately followed by
`WriteAsJsonAsync(...)` with no explicit content type) at three more hand-rolled sites, all out of
that task's declared scope. This task re-located and fixed them:

| # | Site | Method | Current symbol location |
|---|---|---|---|
| 1 | `ChatEndpoints.cs` | `SendMessageAsync` — attachment-validation 4xx | the `if (attachmentValidationError is { } err)` block |
| 2 | `OfficeEndpoints.cs` | `GetJobStatusStreamAsync` — SSE-stream 401 | the `if (string.IsNullOrEmpty(userId))` block (errorCode `OFFICE_009`) |
| 3 | `OfficeEndpoints.cs` | `GetJobStatusStreamAsync` — SSE-stream 404 | the `if (initialStatus is null)` block (errorCode `OFFICE_008`) |

All three still exhibited the defect before this task's fix (confirmed by the failing tests in §3).

## 2. The SSE judgment call — both OfficeEndpoints.cs sites classified IN SCOPE

The POML's first escalation trigger required determining, for each SSE site, whether the error is
written **before** any SSE framing begins (a plain error response) or is a frame emitted **inside** an
already-started stream (in which case it must be left alone).

Reading `GetJobStatusStreamAsync`'s full body settles this with direct evidence:

- The 401 check (`userId` empty) and the 404 check (`initialStatus is null`) both `return` **before**
  the line that sets `context.Response.StatusCode = StatusCodes.Status200OK; context.Response.ContentType
  = "text/event-stream";` and before any write to `context.Response.Body`. `HasStarted` is false at both
  points — nothing has been sent yet.
- The **mid-stream** case is a wholly separate code path further down: the method's generic `catch
  (Exception ex)` block checks `context.Response.HasStarted` and, only when true, uses
  `Services.Office.SseHelper.FormatError` to write an SSE `data:` frame. That frame is a DIFFERENT
  branch, never touched by this fix.

**Conclusion**: both sites are plain, pre-framing error responses — in scope for the same fix task 050
established. Neither the stream's own content type nor any `data:` frame was changed.

### Reachability note (recorded for completeness; does not change the in-scope classification)

Both inline checks turn out to be **defense-in-depth duplicates** on the live HTTP surface:

- `AddOfficeAuthFilter()` (an endpoint filter that runs before this handler) already 401s an
  unauthenticated/unresolvable caller via ASP.NET Core's own `Results.Problem` — the framework's
  already-correct path (errorCode `OFFICE_AUTH_002`).
- `AddJobOwnershipFilter()` (also before this handler) already 404s an unknown job, also via
  `Results.Problem`, using the **same** errorCode `OFFICE_008` as this handler's own duplicate check.

So on the real route, `GetJobStatusStreamAsync`'s own `OFFICE_009`/`OFFICE_008` branches fire only if
the handler is ever invoked without those filters — which is exactly why the new tests (§3) drive it
directly via reflection: it is the only way to exercise the lines at all today. They remain correct to
fix: defense-in-depth code that is itself broken defeats the point of having it, and any future
filter-chain reordering or reuse of this handler would immediately re-expose the header bug on the live
path.

## 3. Reproduction (tests written and shown failing BEFORE the fix, per site)

Two new files, both under `tests/integration/regression/` (compiled into `Sprk.Bff.Api.Tests` via that
project's `RegressionTests` `Compile` glob — see §5 for why a WebApplicationFactory round trip through
the existing `ChatEndpointsTestFixture` was not an option):

- `Issue975_ChatAttachmentValidationProblemJsonTests.cs` — 1 defect test (site 1).
- `Issue975_OfficeJobStreamProblemJsonTests.cs` — 2 defect tests (sites 2 and 3) + 1 negative control.

**Before the fix** (`dotnet test --filter "FullyQualifiedName~Issue975_ChatAttachmentValidationProblemJsonTests|FullyQualifiedName~Issue975_OfficeJobStreamProblemJsonTests"`):

```
[FAIL] Issue975_OfficeJobStreamProblemJsonTests.GetJobStatusStream_WhenUserIdCannotBeDetermined_401Response_CarriesProblemJsonContentType
[FAIL] Issue975_ChatAttachmentValidationProblemJsonTests.SendMessage_WithTooManyAttachments_RejectionResponse_CarriesProblemJsonContentType
[FAIL] Issue975_OfficeJobStreamProblemJsonTests.GetJobStatusStream_WhenJobDoesNotExist_404Response_CarriesProblemJsonContentType
Failed! - Failed: 3, Passed: 1, Skipped: 0, Total: 4
```

All three failures: `Expected ... ContentType to be "application/problem+json" ... but "application/json;
charset=utf-8" ...` — exactly the defect. The 4th test (the success-path negative control) already
passed before the fix, confirming the bug is scoped to the three rejection branches.

**After the fix**: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

## 4. The fix (identical to task 050's established pattern, applied at all three sites)

Pass the content type explicitly to the framework's own 4-arg `WriteAsJsonAsync` overload:

```csharp
await response.WriteAsJsonAsync(
    payload, options: (JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken);
```

`options: null` reproduces exactly what the removed 2-arg call resolved to internally — the JSON body is
byte-for-byte unchanged (same anonymous object, same property names/values in the same order); only the
`contentType` argument is new. `OfficeEndpoints.cs` needed one new `using System.Text.Json;` (it had none
before; `ChatEndpoints.cs` already had it). Each of the three call sites' original `cancellationToken`
argument is preserved (passed positionally after `contentType`), so behavior under cancellation is
unchanged too.

## 5. Technique — why reflection, not a WebApplicationFactory round trip

Both `SendMessageAsync` and `GetJobStatusStreamAsync` are `private static`. The only existing HTTP-level
fixture for the Chat route (`ChatEndpointsTestFixture`) lives in the **`Spe.Integration.Tests`** project,
which `Sprk.Bff.Api.Tests` does not reference (no `ProjectReference`) — and `tests/CLAUDE.md` binds every
regression test to `tests/integration/regression/Issue{N}_*Tests.cs`, compiled into `Sprk.Bff.Api.Tests`
(confirmed via that project's `.csproj` `RegressionTests` `Compile` glob; confirmed `Spe.Integration.Tests`
has no such glob, so no ambiguity). Standing up a second `WebApplicationFactory<Program>` host to duplicate
`ChatEndpointsTestFixture`'s ~150 lines of DI stubs for one assertion would itself be exactly the
"5 components that overlap" anti-pattern CLAUDE.md §11 warns against.

Both handlers fire the code under test **before** touching most of their other collaborators (confirmed by
reading each method end to end), so both new test files extend the SAME reflection technique
`ChatEndpointsAttachmentsTests.cs` already established for `ChatEndpoints`' private static helpers — one
level up, to the handler itself — reusing existing test infrastructure with no new fixture:
`TestHttpContexts.Authenticated` + `TestSessionOwner` (`tests/integration/Shared`), a real
`ChatSessionManager` over mocked `ITenantCache`/`IChatDataverseRepository` (the exact construction
`ChatSessionManagerTests.cs` already uses), and a mocked `IOfficeService` (the same interface
`OfficeEndpointsContractTests.cs`'s own fixture mocks).

For the Office 401 case specifically, this is also the *only* way to exercise the line at all — see the
Reachability note in §2.

## 6. Negative controls (POML acceptance criteria — no successful/SSE response changes)

- `Issue975_OfficeJobStreamProblemJsonTests.GetJobStatusStream_WhenJobExists_StreamContentTypeRemainsEventStream`
  — a known job with an empty mocked stream still gets `Content-Type: text/event-stream`, unaffected by
  the fix at the sibling 401/404 branches in the same method.
- The **existing** `ChatEndpointsTests` suite (`Spe.Integration.Tests`, the fixture built for exactly this
  route) — re-run in full after the fix: **20/20 passed**, including
  `SendMessage_ForValidSession_ReturnsSseContentType` (the SSE success path — untouched, since the fix is
  scoped to the `attachmentValidationError is { } err` branch, never entered when attachments are
  absent/valid) and `SendMessage_Returns401_WhenUnauthenticated` / `SendMessage_Returns404_WhenSessionNotFound`
  (the route's OTHER pre-existing 401/404 behaviors, also unaffected).
- No `text/event-stream` response's content type changed, and no `data:` frame was touched — confirmed by
  code inspection (§2) and by the passing negative controls above.

## 7. Consumer / blast-radius survey — not repeated

Task 050 (§4 of its own note) already surveyed every repo consumer that branches on error content type
and found none that break: the shared `authenticatedFetch`/`tryParseProblemDetails` (PCFs, Code Pages,
SpaarkeAi) accepts both media types via an OR check; the add-in's own `ApiClient.ts` branches on
`response.ok` only. This task touches no new response surface beyond the three sites already covered by
that survey — cited per the task's own instruction rather than repeated.

## 8. Placement justification (root CLAUDE.md §10 / bff-extensions.md)

Modifies existing endpoint handlers only — no new endpoint, service, DI registration, or package. Per
CLAUDE.md §11, a modification to existing files needs no new-component justification; noted here for the
BFF hygiene checklist record, matching task 050's own note.

## 9. Gate results

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` (before fix) | 0 Warning(s), 0 Error(s) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` (before fix, explicit rebuild) | 0 Warning(s), 0 Error(s) |
| New regression tests (before → after) | Before: `Failed: 3, Passed: 1, Skipped: 0, Total: 4`. After: `Failed: 0, Passed: 4, Skipped: 0, Total: 4` |
| `dotnet build src/server/api/Sprk.Bff.Api/` (after fix) | 0 Warning(s), 0 Error(s) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` (after fix, explicit rebuild) | 0 Warning(s), 0 Error(s) |
| All `Issue975_*` regression tests together (task 050 + task 052) | 6 passed, 0 failed, 0 skipped |
| Chat test group (`FullyQualifiedName~.Chat.`) | 765 passed, 0 failed, 3 skipped (pre-existing) |
| Office test group (`FullyQualifiedName~.Office.`) | 306 passed, 0 failed, 10 skipped (pre-existing) |
| `Spe.Integration.Tests` `ChatEndpointsTests` (the original Chat SSE fixture — extra verification beyond the task's named gates, run because it is the most direct negative control for site 1) | 20 passed, 0 failed |
| ArchTests | **191 passed, 0 failed, 191 total** — matches branch baseline exactly |
| Full `Sprk.Bff.Api.Tests` suite — pre-change baseline | **12,366 passed / 0 failed / 56 skipped** (12,422 total) at base commit `12a0b4258`. Verified two ways: (1) `git diff --name-only e89162f89..12a0b4258` returns zero `.cs` files and nothing under `src/server/` or `tests/` — the BFF test surface is byte-identical to task 014's own merged-tree measurement of 12,366/0/56 at `e89162f89`; (2) task 014's note (`notes/014-xml-part-stamp-decisions.md` §"reconciliation") independently cross-checked the same 12,366/0/56 figure via both a 7-chunk partition AND a separate clean monolithic run. A literal re-run was attempted in an isolated worktree (`C:\wt-base-052`) but was interrupted mid-flight during cleanup (an orphaned `testhost.exe` from that run was found still active and stopped before its significance was understood, producing a misleading partial "162 failed / Test host process crashed" abort artifact — not a genuine finding; not used for anything). Given two independent, unbroken confirmations of the same figure already existed, the baseline was accepted rather than re-attempted a second time. |
| Full `Sprk.Bff.Api.Tests` suite — final, this branch (this worktree, after the fix, single process, explicit rebuild first) | **12,370 passed / 0 failed / 56 skipped** (12,426 total). Run as 6 sequential, non-overlapping, mutually-exclusive `--filter` chunks (namespace-based; the 4-chunk `Services`/`Api`/`Contract`/`Integration` split plus a `Seam` + residual-root-classes catch-all) to stay within the tool's per-call foreground window — cross-checked against task 014's own independently-derived partition (its `Api.` chunk needed the identical 3-way split; its `Services`/`Services.Ai` split summed to the same 6,923 this run's unsplit `Services.` chunk produced directly). Per-chunk results: `Services.` → 6,899/0/24 (6,923); small-namespace batch (AccessControl/Infrastructure/Domain/Eval/Models/Filters/Auth/DataMutation/Diagnostics/Workers/Onboarding/Startup/Mocks/EndToEnd/Regression/SpeAdmin/OptionsValidation/TestInfrastructure) → 1,589/0/2 (1,591); `Contract.` → 237/0/0 (237); `Integration.` → 376/0/3 (379, includes this task's 4 new tests); `Api.` → 1,493/0/23 (1,516); `Seam.` + residual root-level classes → 1,776/0/4 (1,780). Sum: 12,370 passed + 0 failed + 56 skipped = 12,426 total — reconciles exactly to **12,366 (baseline) + 4 (this task's new tests) = 12,370 passed**, skip count unchanged, **zero failures across the full 12,422-test pre-existing suite**. |
| `dotnet list package --vulnerable --include-transitive` (`Sprk.Bff.Api.csproj`) | "has no vulnerable packages given the current sources" — no new HIGH CVE |
| Publish size vs a FRESH build of this task's own base commit (`12a0b4258`, NOT `origin/master`) | Base (`C:\wt-base-052-pub`, PowerShell `Compress-Archive -CompressionLevel Optimal`, incl. PDBs) = **45.42 MB** (47,631,469 bytes); branch = **45.43 MB** (47,632,197 bytes); **delta +728 bytes = +0.0007 MB** — negligible, far under the +5 MB single-task escalation threshold and the 60 MB hard ceiling. Both temp worktrees removed after measurement. |
| Step 9.5 `code-review` (synchronous, this session) | 0 Critical / 0 Warning across security, performance, style, AI-code-smell ×5, ADR, BFF Hygiene, Component Justification. Quality direction: Neutral-to-Improved (comments only, zero new branches; ADR-019 compliance extended to 3 sites). |
| Step 9.5 `adr-check` (synchronous, this session) | 0 Violations / 0 Warnings. ADR-019 is the ADR this change brings INTO compliance (previously partial: correct body, wrong media type at these 3 sites). No BFF Hygiene violations (no new package, no new endpoint, no new CRUD→AI dependency). |

## 10. Deviations from the POML

- The POML's gate list says "publish size vs a fresh build of your own base." Executed via a SECOND,
  independent temp worktree (`C:\wt-base-052-pub`) — avoids the exact `MSB3027` stale-DLL-lock trap root
  CLAUDE.md §0 rule 6 and task 014 documented (a running `testhost.exe` holding the previous build's DLL
  open while a concurrent `dotnet publish` tries to rebuild the same project). Both temp worktrees
  removed after use.
- A literal pre-change baseline re-run was attempted in a first isolated worktree (`C:\wt-base-052`), but
  its `dotnet test` invocation exceeded the tool's single-call foreground window and was auto-backgrounded.
  During later cleanup, an orphaned `testhost.exe` process from that run was found still active and was
  stopped before its significance was understood — the run was, in fact, still making genuine progress
  (its own reported total, 12,420 of an expected 12,422, shows it was substantially complete), and
  stopping it produced a misleading "162 failed / Test host process crashed / Test Run Aborted" abort
  artifact. That figure is NOT a genuine finding and was not used for anything: two independent,
  unbroken confirmations of 12,366/0/56 already existed (task 014's own merged-tree measurement at
  `e89162f89`, cross-checked against this task's base `12a0b4258` via `git diff --name-only` showing zero
  `.cs`/`src/server`/`tests` changes in that range), so the baseline was accepted from those rather than
  re-attempted a second time. Lesson carried forward: never stop a still-running `testhost.exe` found
  during cleanup without first confirming it is genuinely orphaned rather than a job still legitimately
  in flight.
- The FINAL full-suite run (this branch, post-fix) was executed as 6 sequential, mutually-exclusive
  namespace-filtered chunks rather than one monolithic `dotnet test` invocation, specifically to stay
  within the tool's per-call foreground time budget without ever backgrounding a job whose completion the
  task depends on — one chunk (the unsplit `Api.` filter, 1,516 tests) still exceeded the window and
  needed the tool's own auto-background + notification mechanism despite the chunking, matching what task
  014's own note independently found ("Api." needed splitting into `Api.Ai.`/`Api.Office.`/residual for
  the same reason). It was left to complete undisturbed (no process was touched this time) and returned
  cleanly. All 6 chunks were built to be non-overlapping by construction (each targets a distinct
  top-level namespace segment identified via `dotnet test --list-tests`) and the sum cross-checks exactly
  against both the expected total (12,366 baseline + 4 new = 12,370) and task 014's own independently
  derived partition (its `Services`/`Services.Ai` split sums to the same 6,923 this run's unsplit
  `Services.` chunk produced directly; its 3-way `Api.` split sums to the same 1,493/23/1,516 this run's
  unsplit `Api.` chunk produced directly).
- Added one extra verification beyond the task's named gates: re-ran the ORIGINAL `Spe.Integration.Tests`
  `ChatEndpointsTests` fixture (20/20 passed) as the most direct possible negative control for site 1,
  since it is the one existing test suite purpose-built for the exact route being patched.
