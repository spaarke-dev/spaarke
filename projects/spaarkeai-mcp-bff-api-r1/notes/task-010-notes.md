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
| 3 | No bearer / no HttpContext → UserContextRequired; zero wire calls; no app-only | pass | `Test2_Negative_AnAuthenticatedMcpCallerWithNoBearerToken_...`: isError, `DATAVERSE_USER_CONTEXT_REQUIRED`, wire empty, 0 assertion mints. `Test2_Negative_TheClientWithNoHttpContext_...`: same outcome, with the accessor null outside a request. "No app-only" is proven by 0 mints: every CCA token, app-only or OBO, starts with an assertion from the provider. |
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
- In stateless mode each request carries its own short-lived session. The context that session captures is the request's own, so the accessor still resolves the request's `HttpContext`. This is my reading of the observed result; I did not decompile the SDK to confirm it.
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

rework: none task 010
