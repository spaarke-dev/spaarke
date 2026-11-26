# Task 030 — playbook run tools: implementation record (2026-11-26)

`spaarke_run_playbook` (model-callable reversible write) and `spaarke_get_playbook_run` (read-only), built on the owner's
decision **D-030-run-model**: a detached caller run, under the ADR-052 amendment of 2026-11-26 (proposed; commit 55db14d39c).
The earlier STOPPED note and its options are kept below under "History".

## What was built

| Piece | File | Role |
|---|---|---|
| The one detached-run helper | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRuns/DetachedPlaybookRunner.cs` | Authorizes with 007, pre-warms OBO, launches the engine's person-run entry on a detached context, keeps a process-wide run table |
| PublicContracts facade | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IPlaybookRunAi.cs` | `IPlaybookRunAi` (start, read, run link), `PlaybookRunLimits`, `PlaybookRunReasonCodes`, `PlaybookRunView` |
| DI | `Infrastructure/DI/AnalysisServicesModule.cs` | One unconditional singleton (`IPlaybookRunAi` → `DetachedPlaybookRunner`) |
| Tools | `Api/Mcp/LegalOperations/Playbooks/RunPlaybookTool.cs`, `GetPlaybookRunTool.cs` | Thin adapters; caps, idempotency, result shape, the interrupted mapping |
| Ledger + list | `McpToolAuthorization.ModelCallableWrites`, `RouteAuthorizationGuardTests.Mcp.Playbooks.cs` | `spaarke_run_playbook` listed; both tools' ledger rows |
| Guard | `tests/Spaarke.ArchTests/DetachedHttpContextGuardTests.cs` | Only the helper (plus a shrinking baseline) builds a `DefaultHttpContext` |
| Graph TTL fix | `Infrastructure/Graph/GraphClientFactory.cs` (commit 90bfaacffa; ceiling in "Review fixes") | OBO Graph token Redis TTL = min(token expiry − 5 min, 55 min) (was a fixed 55 min) |
| #3198 + run link | `Services/Ai/ToolFrameworkExtensions.cs`, `Services/Ai/Handoff/HandoffUrlBuilder.cs` (commit 9362d12d2f) | No default Dataverse environment; `BuildConsoleUrl` |

## The seven conditions, where each is met

1. **One shared helper** (copied from `ComposeProfileDispatcher:66-134`, which is untouched): captures the principal, the
   `Authorization` header and the trace id inside the request; opens a new async DI scope and a `DefaultHttpContext` on it;
   launches under `ExecutionContext.SuppressFlow()`; sets `IHttpContextAccessor.HttpContext` only inside the detached flow and
   clears it on exit; resolves `IPlaybookOrchestrationService` from the new scope and drains `ExecuteAsync` like REST (the
   engine's `PlaybookRunRecordStore` writes the durable record, Kind=Engine). The run gets the exact `documentIds` and
   `parameters` instances 007 authorized, `RunUserId = authorization.RunUserId`, `StartedBy = Person`.
2. **Memory only**: no queue, job, `BackgroundService` or store. The bearer header lives on the launch object and the detached
   context only. Logs carry the outcome or reason code, latency and correlation id; exceptions are logged by type only.
3. **Bounded** (amendment condition 5): `PlaybookRunLimits.MaxRunDuration` = 50 min (a `CancellationTokenSource` on the
   injected `TimeProvider`), `IHostApplicationLifetime.ApplicationStopping`, and an abandon token (the start gave up) are linked
   into the run's token. The cap bounds the run; it does not keep the caller's token valid (see "Dataverse scope check"). The
   Graph (`IGraphClientFactory.ForUserAsync`) and Dataverse (`IDataverseUserClient.GetAsync("WhoAmI")`) OBO tokens are
   obtained before the tool answers; any failure refuses the start (503 `playbook.run-auth-unavailable`, nothing runs). An
   authentication failure that escapes the engine mid-run (MSAL UI-required, a claims challenge, `invalid_grant`,
   AADSTS500133, anywhere in the inner-exception chain) ends the run `Failed`/`playbook.run-auth-expired`; nothing is retried
   and nothing calls `ExecuteAppOnlyAsync`. A caller may have at most `PlaybookRunLimits.MaxConcurrentRunsPerCaller` = 3 runs
   running in this instance; a 4th start is refused before authorization (429 `playbook.run-limit`, nothing runs).
4. **Graph token-cache TTL defect** fixed (minimal change, regression test fails before: "expected ≤ 15m but found 55m").
5. **Authorized first** by 007's `PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync` inside the request; the POML caps and
   refusals are kept (the tool adds input caps only; every refusal carries 007's REST reasonCode and status).
6. **Visible**: the runner's run table (process-wide, because the engine's `_activeRuns` is per Scoped instance), then
   `GetRunStatusAsync` (in memory, then 005's durable record by `StartedByOid`). A durable record still Running more than
   `MaxRunDuration + InterruptedMargin` (10 min) after it started reads as `failed` / outcome `interrupted` /
   `playbook.run-interrupted` — in the tool only; the REST status route's bytes are unchanged.
7. **ArchTest guard** (amendment condition 2, "Enforced by"): `DetachedHttpContextGuardTests` scans the IL of every
   `src/server` production assembly three ways, each a ratchet (a stale baseline entry fails) with must-fire and must-not-fire
   controls:
   - `newobj Microsoft.AspNetCore.Http.DefaultHttpContext::.ctor`. Allowed: `DetachedPlaybookRunner`; baseline
     `ComposeProfileDispatcher`, `DecisionRouteCores`, `AssignTaskNotifier`. Must fire: direct, initializer, target-typed
     `new()`, lambda, async method; must not: reading a context, building an `HttpContextAccessor`.
   - `call`/`callvirt` of `set_HttpContext` on `IHttpContextAccessor` / `HttpContextAccessor` (review fix 2a). Allowed: the
     helper; baseline `ComposeProfileDispatcher`.
   - Reads of the caller's `Authorization` header (review fix 2b). Allowed: `TokenHelper`, `GraphClientFactory`,
     `DataverseUserClient`, `CallerRecordAccessProbe`, `DataverseAccessDataSource`, the helper; baseline
     `ComposeProfileDispatcher`, `AuthorizationModule` (the JwtBearer `OnAuthenticationFailed` classifier).
   - The per-caller cap (condition 5) is enforced in the helper and tested in `DetachedPlaybookRunnerTests`.

### Dataverse scope check (condition 3, reported)

The pre-warm's Dataverse token has the scope the run's caller-context Dataverse calls use. `DataverseUserClient`
(`AcquireTokenOnBehalfOf`, line 198) and `CallerRecordAccessProbe` (line 935) both request `{url}/.default` with
`url = DataverseOptions.EnvironmentUrl ?? Dataverse:ServiceUrl`, trailing slash trimmed, through the one confidential client
from `OrderedCredentialClientProvider`; so the WhoAmI pre-warm fills MSAL's OBO cache (keyed by the assertion) for exactly that
scope, and the run's later exchanges are cache hits until MSAL's 5-minute early-expiry window.

**What correctness rests on (corrected at review).** The earlier text said the 50-minute cap keeps the caller's token valid for
every OBO exchange the run makes. That is wrong: the caller's token may have only minutes left when the run starts, so a fresh
OBO exchange late in the run can be refused. Correctness rests on the pre-warmed OBO caches: MSAL's per-assertion OBO cache
(Dataverse) and the Graph Redis cache, whose TTL follows the Graph token's own expiry (condition 4). The cap bounds the run; it
guarantees nothing about the token. The real miss paths, both fail closed (the run ends `Failed`/`playbook.run-auth-expired`
when the failure reaches the runner, or the engine's own `playbook.run-failed`; never retried, never app-only):
- **A confidential-client switch.** `OrderedCredentialClientProvider` can move to another credential mid-run; the new client
  starts with an empty MSAL cache, so the next exchange is a fresh OBO with the caller's assertion.
- **A second Dataverse host.** Deployed config sets `EnvironmentUrl` and `ServiceUrl` from one value. If they ever differ, a node
  reaching the other host makes a fresh OBO with the same assertion, which may already have expired.

Graph: `GraphClientFactory` (line 290) is pre-warmed by `ForUserAsync`; its Redis TTL is min(token expiry − 5 min, 55 min).

## §10 / §11 justification

**Placement (§10).** In the BFF beside the run record store: the run is per-request user work (ADR-052 placement unchanged;
the amendment lets only this run finish after its request). No package, no new host, no queue; one singleton registration.
Publish-size delta: see "Verification".

**`DetachedPlaybookRunner` (§11).** Existing: `ComposeProfileDispatcher` (detached profile; a Compose collaborator, not a
service, with the #3224 accessor defect, no cap, no pre-warm, no run table). Extension: the amendment names one helper; Compose
moves onto it in #3224, not here. Cost of doing nothing: a run started from Copilot dies with the stateless MCP request, or
runs on the disposed request's context (cross-user OBO, 007 verifier F1).

**`IPlaybookRunAi` (§11).** Existing: `IPlaybookOrchestrationService` and `HandoffUrlBuilder` (Services/Ai, unreachable from
Api/Mcp by the ledger guard). Extension: no PublicContracts facade covers runs; this is the one seam, and it carries the
run-link member the long-running tools share (020 verifier F2). Cost of doing nothing: the tools cannot reach the engine
(FR-05) and `consoleLink` has no builder.

**`DetachedHttpContextGuardTests` (§11).** Existing: no guard covers `DefaultHttpContext` construction. Extension: the
IL scanner `IlCallScan.FileUses` is reused unchanged. Cost of doing nothing: a second detached builder (the cross-user OBO
shape) can land unseen, against amendment condition 2.

## Deviations from the POML / brief (each a decision made here)

- **No playbook name in the result**: the run view has the id only (the status read has no name; adding one would be a second
  Dataverse read per poll).
- **"interrupted"** is `McpRunState.Failed` with `outcome: "interrupted"` and reasonCode `playbook.run-interrupted`; no new
  `McpRunState` value.
- **Console link** = the Console scoped to the first document (`HandoffUrlBuilder.BuildConsoleUrl`); the Console has no run
  view, so no run id in the link.
- **No `userContext` argument** (corrected at review). The earlier reason ("007 decides the run's user") was wrong:
  `PlaybookRunRequest.UserContext` is "user-provided context or instructions" for the run (`IPlaybookOrchestrationService.cs`
  ~124-127), not the caller's identity. The tool does not expose it today; an extra argument is refused. Whether to add an
  `instructions` argument that maps to it is an **owner question** (it is model-written text that reaches the playbook's
  prompts), so it is not added here.
- **Caps**: 1–20 documents, ≤ 20 parameters, names ≤ 64 chars (no two differing only in case), values ≤ 2,000 chars, text
  values only; `idempotencyKey` ≤ 200 chars.
- **Pre-warm failure refuses the start** (503 `playbook.run-auth-unavailable`), rather than starting a run that would fail.
- **The facade does the authorization** (the tool adds no check of its own, only input caps).
- **Both tools require a resolvable Entra oid** before anything else (the `GetDocumentTool` pattern), so a principal with no
  oid is `UserContextRequired`, never a not-found.
- **#3198 fix also drops the `DataverseServiceClient:Url` fallback**: only `Dataverse:EnvironmentUrl` (validated options, then
  the config key) is a base; none throws `OptionsValidationException` at resolve.
- **Engine-reported failure codes**: the engine catches every exception and emits `RunFailed` with `playbook.run-failed`, so an
  auth failure the engine itself catches reads as `playbook.run-failed`, not `playbook.run-auth-expired` (only failures that
  escape the drain are classified). Fixing that needs an engine edit (ADR-039) — known limit, not done.
- **008 / A10 kept as-is**: a caller with no systemuser row is 007's `RunUserUnresolved` (403 `sdap.access.deny.insufficient_rights`),
  not `UserContextRequired`. No extra systemuser resolution was added.
- **Q15 rate limit**: no per-minute limiter added (the REST execute route's `ai-stream` limiter, 10 starts per minute, is not
  applied to the tool; the MCP host allows 60 per minute). Since the review, the per-caller cap on detached runs in progress
  (`MaxConcurrentRunsPerCaller` = 3, `playbook.run-limit`) bounds the detached exposure: one caller can no longer keep dozens of
  50-minute runs alive. Whether the tool should also take a per-minute start limit stays an **owner question**.

## Known limits

- A run in flight is lost on restart or scale-in. Its durable record stays Running; the status tool reads it as interrupted
  after cap + margin (60 min). Between the loss and that point the tool says "running". Only a crash (restart, scale-in, a
  process kill) leaves a record Running: a capped or shut-down run ends in the engine as Cancelled. A Running record with no
  `sprk_startedon` has no known start, so it is reported as stored ("running"), never mapped to interrupted (review fix 7).
- **The status flips between memory and the record.** A capped run reads `failed` / `playbook.run-time-limit` from this
  instance's run table, but `cancelled` (no reason code) from the durable record, once the run table entry is past retention
  (1 h) or when the poll lands on another instance. The record is the engine's (ADR-039: no engine edit to carry the code).
- **The cap is cooperative.** It cancels the run's token; a node that ignores cancellation keeps running until it next needs
  the detached scope, which is disposed once the runner's drain ends, and then fails closed.
- When the cap or shutdown cancels a run, the detached scope is disposed while the engine's own background task may still be
  persisting the final record; the record can stay Running (covered by the interrupted mapping).
- **The per-caller cap is per instance.** `MaxConcurrentRunsPerCaller` counts this process's run table only; a caller whose
  starts land on N instances can have 3 × N runs running. No cross-instance counter is built (it would need a shared store,
  which condition 3 keeps the run out of). It counts runs the engine has accepted: a start still between the check and its
  accept (authorization, pre-warm, up to the 5-second accept budget) is not yet in the table, so starts for one caller that
  arrive together can each pass it. Reported to the caller of this fix as an open question (a pending-start reservation would
  close it).
- The run table is per instance: a status poll that lands on another instance reads the durable record (state and counts at
  the last persisted step).
- A retry with the same key that lands on another instance before the first instance's record exists answers the run id as
  running (no read possible yet).

## Verification (2026-11-26)

- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: succeeded, 0 warnings.
- New tests (`PlaybookRunTools*`, `DetachedPlaybookRunner*`): 51 passed, 5 consecutive runs. Graph TTL regression 8/8; #3198 4/4.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "Mcp|Playbook|Graph"`: 2,687 passed, 5 skipped, 1 failed —
  `ProvisionProjectIdempotencyTests…DoesNotSettleIt…(status: 201)` (external-access provisioning, not touched here) timed out
  at 24 s while the full ArchTests and two Release publishes ran alongside; its class passes 55/55 alone.
- Full ArchTests: 2,861 passed, 0 failed (includes `DetachedHttpContextGuardTests` and the ledger).
- `check-changed-cs-format.sh origin/master`: all 199 changed files formatted.
- MCP conformance (`tests/integration/Mcp.Conformance.Host/run-conformance.sh`, widgets built): 92 passed, 38 failed, all 38 in
  the baseline; exit 0.
- Publish (`dotnet publish -c Release`, zipped) vs the pre-task tree 55db14d39c: 42,219,494 → 42,242,644 bytes (+23 KB,
  +0.02 MB; total 42.2 MB, under the 55 MB review line). No package added.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Fail-before (each reverted, run, restored): no `SuppressFlow` → the request's accessor is null after the run (the #3224
  defect); no end-of-stream cancellation branch → a capped run that ends with `RunCancelled` reads Cancelled, not
  time-limit; logging `ex.Message` → the token-leak test fails; no auth classification → `playbook.run-failed` instead of
  `playbook.run-auth-expired`; no cap in the linked token → the cap tests time out; `IsInterrupted` disabled → the interrupted
  tests fail; no oid check on the live read → another user reads the run; no oid check in the get tool → a no-oid caller gets
  the not-found instead of `UserContextRequired`; guard helper renamed → the guard names `DetachedPlaybookRunner`.

## Review fixes (2026-11-26, two opus passes and the unified-access-control-r2 review)

Each code fix has a test that fails without it: the old file (or, where the old file no longer compiles against the new test,
the old function body) was put back, the tests run, and the fix restored.

| # | Finding | Fix | Fail-before proof |
|---|---|---|---|
| 1 | F3: no per-caller cap on detached runs (REST: 10 starts/min and the run dies with its connection; MCP: 60/min, each run up to 50 min) | `DetachedPlaybookRunner.StartAsCallerAsync` counts the caller's Running runs in the run table (by oid, the `StartedByOid` key) BEFORE authorizing; at `PlaybookRunLimits.MaxConcurrentRunsPerCaller` = 3 it refuses (`PlaybookRunStartOutcome.RunLimit`); the tool answers 429 `playbook.run-limit`, "Wait for a run to finish … then try again", keeping no retry key. Per instance (Known limits) | Old runner: `Start_BeyondThePerCallerCap_IsRefused_UntilARunFinishes` and `Tool_BeyondThePerCallerCap_Is429RunLimit` fail (the 4th start runs) |
| 2a | F2: the guard did not cover setting the accessor | `DetachedHttpContextGuardTests.OnlyTheHelperSetsTheAccessorsHttpContext`: IL `call`/`callvirt` of `set_HttpContext` on `IHttpContextAccessor`/`HttpContextAccessor`; baseline `ComposeProfileDispatcher` (the only other setter found) | Controls: interface, class and lambda setters fire; reading the accessor does not. With the baseline emptied the real scan names `ComposeProfileDispatcher+<RunAsync>d__6::MoveNext` |
| 2b | F2: the guard did not cover copying the Authorization header | `OnlyTokenAcquisitionAndTheHelperReadTheAuthorizationHeader`: IL (typed `IHeaderDictionary.get_Authorization`, or the name by `ldstr`/`ldsfld HeaderNames.Authorization` in a body that touches the request headers). IL because a text scan cannot tell the incoming `IHeaderDictionary.Authorization` from the dozens of outgoing `HttpRequestMessage.Headers.Authorization` writes. Additive `IlCallScan.FileLoads` (ldstr / ldsfld); `FileUses` unchanged. Baseline `ComposeProfileDispatcher`, `AuthorizationModule` | Controls: typed property, literal name, `HeaderNames`, `TryGetValue` (lower case), async method fire; a typed write, an outgoing request's header, another header, the bare name do not. With the baseline emptied the real scan names `ComposeProfileDispatcher::Dispatch` and `AuthorizationModule+…<AddAuthorizationModule>b__6>d::MoveNext` |
| 3 | K: false token-lifetime premise | Wording fixed in `IPlaybookRunAi.cs` (`MaxRunDuration`), `DetachedPlaybookRunner` (condition 5 remark, `PrewarmAsync`), this file ("Dataverse scope check", condition 3) | Doc only |
| 4 | K: no Graph TTL ceiling | `GraphClientFactory.OboTokenCacheTtl` = min(expiry − 5 min, 55 min) (`OboTokenCacheMaxTtl`). The factory takes an optional `TimeProvider` (system clock by default) so the TTL is measured on the test's fake clock | Old TTL body: 90-min and 24-h tokens cached 85 min / 1,435 min, not 55 (5 tests fail) |
| 5 | F2: real clock in tests | `PlaybookRunToolsContractTests`: `Stopwatch` assertion dropped (the closed gate proves FR-14); the size test uses a `FakeTimeProvider`. `DetachedPlaybookRunnerTests`: the `Task.Delay(5)` loop became `Time.TimerArmed(AcceptBudget)` (a `FakeTimeProvider` subclass that reports an armed timer) + `Advance(AcceptBudget)`. `PlaybookRunWorld`: `Task.Delay(Infinite, ct)` became a cancellation-only wait. `GraphOboTokenCacheTtlTests`: the factory's TTL runs on a `FakeTimeProvider`; MSAL has no clock of its own and refuses an entry seeded at a fixed or far-future instant (it then calls Entra, AADSTS50027), so the fake clock is pinned once to the system's whole second — the one remaining `UtcNow` read, at MSAL's boundary | Test-only |
| 6 | K: retry key written after the start | `RunPlaybookTool`: the `RetryEntry` is built first and always returned; a fault building the console link or the `Started` result falls back to `GetPlaybookRunTool.StatusResult` for the same run id | Old tool: `Start_ResultBuildingFaults_StillKeepsTheRetryKey` fails (the link fault escapes the tool and nothing is stored) |
| 7 | K: a record with no `StartedOn` read as interrupted at once | `GetPlaybookRunTool.IsInterrupted` applies only when the start time is known (not `MinValue`) | Old tool: `Status_RunningRecordWithNoStartTime_IsNotInterrupted` fails (`failed`, not `running`) |
| 8 | Notes | `userContext` reason, status flip, cooperative cap, Q15, per-instance cap: corrected above | Doc only |

**Verification (review fixes).**
- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: succeeded, 0 warnings.
- 030 tests (`PlaybookRunTools*`, `DetachedPlaybookRunner*`, `GraphOboTokenCacheTtl*`, `Issue3198*`): 72 passed, 3 consecutive runs.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "Mcp|Playbook|Graph|Handoff"`: 2,726 passed, 5 skipped, 1 failed —
  `ProvisionProjectIdempotencyTests…DoesNotSettleIt…(status: 201)` timed out at 28 s under the full run (the same flake the first
  verification recorded; external-access provisioning, not touched here); its class passes 55/55 alone.
- ArchTests filtered to `DetachedHttpContextGuard|RouteAuthorizationGuard|Mcp`: 122 passed, 0 failed.
- Full ArchTests: 2,864 passed, 0 failed (was 2,861: the three new guard tests).
- `check-changed-cs-format.sh origin/master`: all 200 changed files formatted.

Not changed (out of scope, unchanged known limits): the 5-second start bound measured from tool entry (039 latency run); results
content returned to Copilot (owner question); `playbook.run-auth-expired` rarely firing because the engine catches the
exceptions (engine freeze); the not-found timing difference (random GUIDs).

## History — the STOPPED options (2026-11-26, before D-030-run-model)

The task first stopped because the run must outlive the stateless MCP request without using its `HttpContext` after the reply,
and every way to do that hit a binding rule. Options put to the owner:

| # | Option | Engine edit? | Broke |
|---|---|---|---|
| 1 | Tool-side detach on the `ComposeProfileDispatcher` precedent | No | ADR-052 MUST NOT (token leaves its request); spec.md:249 (new in-process background work) |
| 2 | A detached Person-run entry in the engine, acting through ADR-028 A5 impersonation | Yes | ADR-039 (no engine edit); A5 needs #988–#990 and covers Dataverse only |
| 3 | App-only run through `ExecuteAppOnlyAsync` | No | ADR-028 A10 (no app-only fallback for a user action) |
| 4 | Keep the run inside the MCP request, bounded under 30 s | No | FR-14 / NFR-01 (no 5 s start; runs over ~25 s impossible) |

The owner chose option 1 with conditions (D-030-run-model) and the ADR-052 amendment (55db14d39c). The defect found then in
`ComposeProfileDispatcher` (accessor nulled for the save request, D-030-1 / #3224) is reproduced by this task's accessor test
when `SuppressFlow` is removed, and stays open for Compose.

rework: none task 030
