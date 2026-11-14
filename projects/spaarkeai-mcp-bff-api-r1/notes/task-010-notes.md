# Task 010 notes — MCP caller-path verification (option C)

> Run 2026-11-14 as an Opus subagent at effort high. Branch `worktree-agent-a6c9a2774788c19a6`, cut from `727ed0382e`. Rigor FULL (tags auth, integration-test; new files under `tests/**`). Only test files and this note were added; nothing under `src/` changed.

## Escalation verdict

**No trigger fired.**
- Test 1: inside an MCP tool on the production `AddMcpModule`/`MapMcpEndpoints` host, `IHttpContextAccessor.HttpContext` is non-null. It is `ReferenceEquals` to the instance a middleware captured for that request (the TraceIdentifier matches too), and the tool's `IServiceProvider` is that context's `RequestServices`.
- Test 3: one MCP commit asks WhoAmI exactly once.
- Option C's premise (FR-10) holds on SDK 2.3.1.

## What shipped

**`tests/integration/auth/Mcp/McpCallerPathTests.cs`** (tests 1, 2 and 5).
- `McpCallerPathHost` is the production `AddMcpModule` + `MapMcpEndpoints` on Kestrel loopback, plus:
  - JwtBearer configured as `McpTestHost`;
  - an optional test-only authentication handler that gives the A10 claims with **no** `Authorization` header (negative a);
  - a middleware that records `HttpContext`, `TraceIdentifier` and `RequestServices`.
- Two test-only probe tools, both `[McpArea("legal-operations")] [Authorize]`, read-only and titled.
- `DataverseRig` runs the real `DataverseUserClient` over the real `OrderedCredentialClientProvider`. Its wire is a hand-written `HttpMessageHandler` subclass (`DataverseWire`), and a counting `IClientAssertionProvider` stands in for the credential.
- OBO without a tenant: the MSAL user-token cache is seeded with one access token keyed by `user_assertion_hash = base64url(SHA256(assertion))`. `AcquireTokenOnBehalfOf` returns it from the cache, so the test needs no network and no assertion mint.

**`tests/integration/data-mutation/Mcp/McpCommitCallerPathTests.cs`** (tests 3 and 4).
- It is a further `partial` of `DecisionCommitServiceTests`, so it reuses the private `Rig` and T170's `WireCountingProbe`; that is why its namespace is `...DataMutation.Signals`, as with 024.
- The production plan and commit tools are served on `McpCallerPathHost` over the real `DecisionCapability`, `DecisionCommitService`, `DecisionPlanReadStamp`, `SignalCoreRecordAccess` and `DecisionPlanService`. The worklist and suppression reads the plan tool also makes answer empty; they are not on the stamp or commit path.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Accessor context = middleware's instance; provider = RequestServices | pass | `Test1_InsideATool_...` |
| 2 | Real client over MCP succeeds as the caller | pass | `Test2_TheRealDataverseUserClient_...`: userId is the caller's systemuserid. The wire saw exactly one `GET /api/data/v9.2/WhoAmI()` with the opaque test bearer. The tool asked twice, and the per-request memo answered the second. A second MCP request adds exactly one more wire call. No assertion was minted. |
| 3 | No bearer / no HttpContext → UserContextRequired; zero wire calls; no app-only | pass | `Test2_Negative_AnAuthenticatedMcpCallerWithNoBearerToken_...`: isError, `DATAVERSE_USER_CONTEXT_REQUIRED`, wire empty, 0 assertion mints. `Test2_Negative_TheClientWithNoHttpContext_...`: same outcome, with the accessor null outside a request. "No app-only" was first shown by 0 mints, which was too weak: a cached app token is served with no assertion mint. Now (2026-11-14 fix round F1) the rig's app-only cache holds a distinct app token, so a fallback would succeed offline; the proof is the empty Dataverse wire (see "Review fix round 2"). |
| 4 | One commit over MCP: WhoAmIs == 1, every rights question asked | pass | `Mcp010_Test3_...`: CallerQuestions > 1, `WhoAmIs == 1`, `RightsAsked == RightsQuestions > 0`, only the caller is asked about. |
| 5 | Plan stamp over MCP satisfies a later MCP commit; negative refusal writes nothing | pass | `Mcp010_Test4_APlanRead...`: Recorded with step warning `reclosed_after_system_resolution`, so the stamp was read. `Mcp010_Test4_Negative_...`: `signal_not_open`, no writes, no records. |
| 6 | Stateless == true, PerSessionExecutionContext == false; seeding proof recorded | pass, **with a finding** | Both test 5 cases pass. The seeding proof (below) shows test 5 goes red; test 1 does **not**. |
| 7 | Existing suites pass unedited | pass | Counts below. |
| 8 | No `src/` diff | pass | `git diff --stat HEAD -- src/` is empty. |
| 9 | uac-r2 review and two adversarial passes | main session | Not run here, per the brief. |
| 10 | Scope beyond the ACs is justified | see below | |

## Seeding proof (AC 6)

I mutated `McpCallerPathHost` temporarily, then restored it (hash-checked; nothing committed).

| Mutation | Test 1 | Test 5 (test host) | Others |
|---|---|---|---|
| A. `PerSessionExecutionContext = true` (stateless kept) | **passes** | fails ("found True") | 2, 3 and 4 pass |
| B. `Stateless = false` + `PerSessionExecutionContext = true` | fails (HTTP 400: no session without `initialize`) | fails ("Stateless … found False") | 2 (wire cases) fail the same way |

**Finding for the owner.** The POML's premise that PerSessionExecutionContext=true makes test 1 fail is false in stateless mode, and so is the `<cost-of-doing-nothing>` claim that a flip "would null the accessor".
- In stateless mode each request carries its own short-lived session. The context that session captures is the request's own, so the accessor still resolves the request's `HttpContext`. The SDK source confirms it: `StreamableHttpHandler.cs:551` and `:629` are the flag's only two reads, both on the stateful path (`notes/decisions.md`, 2026-11-14 entry; supersedes this run's "reading of the observed result").
- The guard against the flip is therefore test 5's pin, not test 1. Test 1 guards the actual property: same instance, RequestServices.
- A stateful flip is caught by both.
- I left the tests as they are: test 1 asserts true behaviour and cannot be made to fail on a setting that does not change it. I also rewrote test 5's `because` text so it does not overstate.

## Test 6: existing suites, unedited

| Run | Result |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests` with filter `Mcp\|Decision\|Signal\|CallerWhoAmIMemo\|Dataverse` | 9384 passed, 8 skipped, 0 failed (total 9392; includes T170, `CallerWhoAmIMemoIsolationTests`, the chat `Dataverse*HandlerTests`, the DecisionCommitService and Signal suites, and the 9 new tests) |
| `tests/Spaarke.ArchTests` | 2538 passed, 0 failed |
| `Sprk.Bff.Api.IntegrationTests` with filter `ContractBaseline\|Mcp\|Signal\|Decision` (011 baselines) | 167 passed, 2 skipped, 0 failed (total 169) |

One false alarm during the run: after the hash-checked restore, a re-run showed 7 failures. `Copy-Item` had kept the backup's older timestamp, so the incremental build still carried mutation B. Touching the file and rebuilding gave 0 failures.

## Scope beyond the ACs (one line each)

- `Test5_TheRealBffHost_...` builds the real BFF `Program` (Mcp:Enabled) through the production assembly scan and DI. It asserts the same transport pin plus each legal-operations tool's exact input-schema properties. The brief asked for it, and it closes wave 6's known limit "no test through the production assembly scan": an unregistered caller-path service would be published as a tool argument, and the call would no longer run as the request's caller.
- `Test2` asks WhoAmI twice in one tool call and makes a second MCP request. This is the only way to show the memo is per request, not per host, on the client test 1 proves is the request's.

## For 025 (not in 010's ACs)

- Wave 6 known limit for 023: unknown arguments are ignored, and `openWorldHint` is unset on that tool. Neither is a caller-path property, so 010 does not test it.

## Techniques worth reusing

- **MSAL cache seed for OBO tests.** See "What shipped": one AccessToken entry whose `user_assertion_hash` is `Base64UrlEncoder.Encode(SHA256(assertion))`, loaded through `UserTokenCache.SetBeforeAccess`. `UserAssertion.AssertionHash` is not public in MSAL 4.87.
- **MCP9006.** Reading `PerSessionExecutionContext` is obsolete with a DiagnosticId. Each of the two reads carries a line-scoped `#pragma warning disable/restore MCP9006` with a one-line reason, as `Directory.Build.props` allows. There is no file-wide pragma and no NoWarn, and CS0618 is untouched.

## Step 9.5 (self-review and ADR check; adversarial passes left to the main session per the brief)

- **ADR-028.**
  - Tokens are opaque test strings.
  - Assertions are on the test bearer string `test-dataverse-token-for-caller-010` and on test-minted JWTs only; nothing is printed or logged.
  - The test authentication handler exists only under `tests/`.
  - Fail closed is proven three ways: reasonCode, empty wire, zero assertion mints.
- **ADR-038.**
  - KEEP paths: `tests/integration/auth/Mcp/` and `tests/integration/data-mutation/Mcp/`.
  - No `Mock<HttpMessageHandler>` or `Mock<IServiceClient>`; the wire is a hand-written subclass.
  - Mocks are used only at module boundaries (the rig's existing `IDataverseUserClient`/`IGenericEntityService` mocks).
- **ADR-002 / FR-10.** No `ICallerContext`, no scoped caller holder, no app-only fallback.
- **AP-9.** No existing test file was edited; T170 is untouched.
- **Format.** `dotnet format whitespace --verify-no-changes` on both new files is clean (it fixed LF to CRLF). `check-changed-cs-format.sh origin/master` passes.
- **Findings.** None classed F1–F4. The seeding-proof premise is a spec or POML wording issue, not a code defect, and is raised above for the owner. No defer issue was filed.

## Review fix round (2026-11-14, after two adversarial reviews)

Files: `McpCallerPathTests.cs`, `McpCommitCallerPathTests.cs`, a comment in `McpModule.cs`, this note, `notes/decisions.md`, project `CLAUDE.md` §2. Nothing else under `src/` changed.

**Scope of the host (reviewer K4).** Tests 1-4 run on a host composed from the production `AddMcpModule` and `MapMcpEndpoints` (task-020 host shape), not the BFF `Program`. Only test 5's second and third cases build the BFF host, so the caller path is proven on production module code and not on `Program`'s full pipeline (other middleware, the FallbackPolicy).

**F3, no second caller.** New `Test1_TwoCallersInFlightAtOnce_...`: two callers (different oids and tokens), each tool call waits inside the tool until the other is running (`CallerPathRecorder.RequireOverlap`, a barrier that fails after 20 s instead of hanging), then reads the accessor. The probe tool returns the oid, the request-header marker and "my provider is the accessor context's RequestServices" in its own result. Per request, the test asserts the tool's accessor context and provider are `ReferenceEquals` to the middleware's for the same marker, and that the two requests' contexts differ. New `Test2_Negative_ASecondCaller...`: the MSAL cache holds caller A only; caller B gets `DATAVERSE_OBO_EXCHANGE_FAILED` with an empty Dataverse wire; the assertion stub counts the mint attempt (proving a cache miss) and refuses it, so the miss stays offline; caller A then still succeeds with its own token.

**F3, seeding proof for test 1.** The host was temporarily seeded (env switch `MCP010_SEED`, removed again; hash-checked restore) and `Test1_*` was run:

| Seeded fault | Single-caller test 1 | Two-callers test | Red at |
|---|---|---|---|
| `ScopeRequests = true` set in `ConfigureSessionOptions` (a `PostConfigure` of the transport options) | red | red | `ReferenceEquals(ToolServices, MiddlewareRequestServices)` / `toolServicesAreRequestServices` is false |
| `RunSessionHandler` starting `server.RunAsync` under `ExecutionContext.SuppressFlow()` (in `Task.Run`) | red | red | `ToolAccessorContext` null / `oid` null |
| `IHttpContextAccessor` replaced by "last request's context wins" | green (one request) | red | `oid` is the other caller's (`...a2` for caller A) |

All three failed at an assertion, none at an HTTP 400 from the RPC helper: the probe tool returns normally when the accessor is null or the provider differs, and the helper only throws for a transport or JSON-RPC error. The third row is what makes the second caller necessary: a one-caller test cannot see a leak.

Other seeds: miss test (cache seeded for B instead of A) red at "caller B has no OBO exchange" (B got an answer); real-host inferred-schema test (a temporary unregistered `IFormatProvider` parameter on `GetDecisionPlanTool.HandleAsync`, reverted, hash-checked) red at `{"seedExtra","signalId"}` instead of `signalId`; static-schema pin (expected `cursorX`) red at the value; T170 `sprk_confirmedby` assertion (expected a different guid) red at the value (a seeded WhoAmI of another user fails earlier, at the refused commit).

**F3, schema test vacuous for two tools.** `ShowDecisionsTool` and `ListDecisionPageTool` overwrite `ProtocolTool.InputSchema` with a static schema, so their rows could not show a missing service. Chose the cheaper sound option: the real-host test is narrowed to the three inferred-schema tools (`get_decision_plan`, `search_decision_lookup`, `complete_decision`) in its name and comment, and the other two rows moved to a separate test named and commented as a schema pin ("not an injection proof"). A real-host `tools/call` for each of the two was not added: it needs a Dataverse and capability rig on the real host for little gain, since both tools have their own tool tests (`ShowDecisionsToolTests`, `DecisionToolsTestHost`).

**K, PerSessionExecutionContext is inert in stateless mode.** SDK 2.3.1 reads it only in the stateful branch (mutation A above). Test 5's `because` now names Stateless as the guard and the flag as pinned only against a future stateful switch (test renamed `Test5_TheMcpTransport_IsStateless_AndTheExecutionContextFlagStaysOff`). Comment in `McpModule.cs`, project `CLAUDE.md` §2 and `notes/decisions.md` corrected. The earlier line in this note, "the guard against the flip is test 5's pin", is superseded: the flag flip is not a fault in stateless mode.

**K, T170 parity.** `sprk_confirmedby == Caller` restored in `Mcp010_Test3_*`. `WhoAmIs == 1` counts the probe's wire (`WireCountingProbe`); the user client is a mock in that rig, so, as in T170, it does not prove how many times a real `DataverseUserClient` hit Dataverse (test 2 proves that on the real client).

**Hygiene.** The OBO cache seed keeps one `DateTimeOffset.UtcNow` read: fixed values were tried (expires_on 2038 or 2100, cached_at 2026 or 2099) and MSAL then ignored the entry (OBO exchange failed), so it needs the system clock; a one-line comment says so. `MiddlewareRequestServices` is now set before `next`.

**Counts.** `McpCallerPathTests` + `Mcp010_*`: 12 passed (was 9: 6 in `McpCallerPathTests` + 3 commit; this round added 3 in `McpCallerPathTests`: the two-callers test, the cache-miss test, and the static-schema pin split out of the real-host test). `--filter "Mcp|T170_|CallerWhoAmIMemo"` on `Sprk.Bff.Api.Tests`: 185 passed, 1 skipped, 0 failed. BFF build: 0 warnings. `check-changed-cs-format.sh origin/master`: clean.

## Review fix round 2 (2026-11-14, peer review by uac-r2: F1, F2, K1-K4)

Files: `McpCallerPathTests.cs` and this note. Production code unchanged (`DataverseUserClient.cs` hash `b247e26012` before and after every seeded fault; `git status` shows only the test file and this note).

**F1, the no-app-only-fallback proof was vacuous.** `FailEveryMintFromNow()` refused every mint, so a fallback to `AcquireTokenForClient` would also have failed and the wire would have stayed empty.
- The rig now seeds MSAL's APP token cache (`DataverseRig.SeedAppOnlyAccessToken`, `AppOnlyDataverseToken`) with a distinct token for the Dataverse scope, on every Dataverse test including the two negatives that had no client before (no bearer, no HttpContext). A fallback would succeed offline and send that token.
- `Wire.Requests.Should().BeEmpty()` is now the first assertion of the second-caller test, and the mint check is `Mints == mintsBefore + 1`. The negatives compare against `mintsBefore` taken after seeding: `GetClientAsync` itself mints once when it selects the credential, so "0 mints" no longer holds once a client exists.
- `DataverseRig.StartAsync` became `Create` + `SeedCachesAsync(host.Services)`: the credential provider and the client are now registered in the host's DI, so both get the host's loggers (F2).

**F2, "nothing logged or forwarded" (FR-02 check 4) was untested.**
- `McpCallerPathHost` captures logs at Trace with the shared `DecisionToolsTestHost.CapturingLoggerProvider` (`host.Logs`); both clients take `ILogger<T>` from the service provider; `DataverseWire` records every request and content header and the body (`WireRequest`).
- `AssertNothingLeaked` (end of the positive real-client test): the MCP bearer (whole token, payload, signature) and the caller's oid are in no log message or state value and in no outbound header, path or body; the OBO access token is in no log message or state value. Search strings are live: the Dataverse token is on the wire's Authorization header, the tool saw the oid, the bearer is a three-part JWT whose hash keys the OBO cache hit, and the logs contain the client's Debug "memo" line and the credential provider's own entries (so Trace and Debug are captured).
- Limit: the capture records message and state, not the `Exception` object, so a token inside an exception's text is not seen.

**Seeded faults** (production file edited temporarily, restored from a copy, hash-checked `b247e26012493e826d2053942815676874b85230` after each batch):

| Seeded fault in `DataverseUserClient` | Test | Result |
|---|---|---|
| App-only fallback in the `MsalException` catch (`AcquireTokenForClient` for the Dataverse scope) | `Test2_Negative_ASecondCaller...` | red at `Wire.Requests` empty: found `GET /api/data/v9.2/WhoAmI()` with `Bearer test-APP-ONLY-dataverse-token-010` (earlier, with the mint check first, it was red at the mint count: 3 instead of 2; before the app cache was seeded it was red only at the `isError` check, never at the wire) |
| `LogDebug` of the caller's bearer | `Test2_TheRealDataverseUserClient...` | red at "nothing of the caller's MCP bearer or oid is logged" |
| `LogDebug` of the OBO access token | same | red at "the OBO access token is not logged" |
| `LogDebug` of the caller's oid | same | red at the logs check (first attempt used `FindFirst("oid")`, which is null after JwtBearer claim mapping, so it logged nothing and stayed green: a bad seed, not a hole; fixed to the mapped claim type) |
| Extra outbound header `X-Seeded-Caller` = the caller's bearer | same | red at "no outbound header, path or body forwards the caller's bearer or oid" |
| Extra outbound header = the caller's oid | same | red at the same assertion |

**Known limits accepted by the reviewer.**
- K1: tests 1-4 run on a host composed from `AddMcpModule` + `MapMcpEndpoints`, not `Program`. The reviewer checked `UseSpaarkeMiddleware`: nothing replaces `RequestServices` or breaks the context flow; test 5 and `McpAuthTests` cover DI and schemes.
- K2: the decision tools' own app-only reads (`SignalCoreRecordAccess`) are not observed here.
- K3: no negative for another caller's plan read satisfying a commit (the stamp is keyed by oid; outside FR-02).
- K4: two stale lines in this note fixed (the AC 3 "every CCA token starts with an assertion" row, and "did not decompile the SDK", superseded by the `decisions.md` 2026-11-14 entry citing `StreamableHttpHandler.cs:551` and `:629`).

**Counts.** `McpCallerPathTests`: 9 passed. `Sprk.Bff.Api.Tests --filter "Mcp|T170_|CallerWhoAmIMemo"`: 185 passed, 1 skipped, 0 failed. BFF build: 0 warnings, 0 errors. `check-changed-cs-format.sh origin/master`: clean (after `dotnet format whitespace` on the test file, which converts LF to CRLF).

rework: none task 010
