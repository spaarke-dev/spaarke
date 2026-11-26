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
| Graph TTL fix | `Infrastructure/Graph/GraphClientFactory.cs` (commit 90bfaacffa) | OBO Graph token Redis TTL = token expiry − 5 min (was a fixed 55 min) |
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
3. **Bounded**: `PlaybookRunLimits.MaxRunDuration` = 50 min (a `CancellationTokenSource` on the injected `TimeProvider`),
   `IHostApplicationLifetime.ApplicationStopping`, and an abandon token (the start gave up) are linked into the run's token.
   The Graph (`IGraphClientFactory.ForUserAsync`) and Dataverse (`IDataverseUserClient.GetAsync("WhoAmI")`) OBO tokens are
   obtained before the tool answers; any failure refuses the start (503 `playbook.run-auth-unavailable`, nothing runs). An
   authentication failure that escapes the engine mid-run (MSAL UI-required, a claims challenge, `invalid_grant`,
   AADSTS500133, anywhere in the inner-exception chain) ends the run `Failed`/`playbook.run-auth-expired`; nothing is retried
   and nothing calls `ExecuteAppOnlyAsync`.
4. **Graph token-cache TTL defect** fixed (minimal change, regression test fails before: "expected ≤ 15m but found 55m").
5. **Authorized first** by 007's `PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync` inside the request; the POML caps and
   refusals are kept (the tool adds input caps only; every refusal carries 007's REST reasonCode and status).
6. **Visible**: the runner's run table (process-wide, because the engine's `_activeRuns` is per Scoped instance), then
   `GetRunStatusAsync` (in memory, then 005's durable record by `StartedByOid`). A durable record still Running more than
   `MaxRunDuration + InterruptedMargin` (10 min) after it started reads as `failed` / outcome `interrupted` /
   `playbook.run-interrupted` — in the tool only; the REST status route's bytes are unchanged.
7. **ArchTest guard**: `DetachedHttpContextGuardTests` scans the IL of every `src/server` production assembly for `newobj
   Microsoft.AspNetCore.Http.DefaultHttpContext::.ctor`. Allowed: `DetachedPlaybookRunner`; baseline
   `ComposeProfileDispatcher`, `DecisionRouteCores`, `AssignTaskNotifier`. A stale baseline entry fails (ratchet). Must-fire
   fixtures: direct, initializer, target-typed `new()`, lambda, async method; must-not-fire: reading a context, building an
   `HttpContextAccessor`.

### Dataverse scope check (condition 3, reported)

The pre-warm's Dataverse token has the scope the run's caller-context Dataverse calls use. `DataverseUserClient`
(`AcquireTokenOnBehalfOf`, line 198) and `CallerRecordAccessProbe` (line 935) both request `{url}/.default` with
`url = DataverseOptions.EnvironmentUrl ?? Dataverse:ServiceUrl`, trailing slash trimmed, through the one confidential client
from `OrderedCredentialClientProvider`; so the WhoAmI pre-warm fills MSAL's OBO cache (keyed by the assertion) for exactly that
scope, and the run's later exchanges are cache hits until MSAL's 5-minute early-expiry window. Deployed config sets
`EnvironmentUrl` and `ServiceUrl` from one value. If they ever differ, a node reaching the other host makes a fresh OBO with the
same assertion — still inside the 50-minute cap, so still valid. Graph: `GraphClientFactory` (line 290) is pre-warmed by
`ForUserAsync`, and its Redis TTL now follows the token's own expiry (condition 4).

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
- **No `userContext` argument** (007 decides the run's user; an extra argument is refused).
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
- **Q15 rate limit**: none added (the REST execute route's `ai-stream` limiter is not applied to the tool; the MCP host has
  its own request limits).

## Known limits

- A run in flight is lost on restart or scale-in. Its durable record stays Running; the status tool reads it as interrupted
  after cap + margin (60 min). Between the loss and that point the tool says "running".
- When the cap or shutdown cancels a run, the detached scope is disposed while the engine's own background task may still be
  persisting the final record; the record can stay Running (covered by the interrupted mapping).
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
