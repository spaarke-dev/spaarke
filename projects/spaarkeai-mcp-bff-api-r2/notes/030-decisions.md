# Task 030 — playbook run tools: STOPPED before implementation (2026-11-26)

Status: no code written. The POML stays `in-progress`; the main session owns TASK-INDEX and `current-task.md`.

## Why it stopped

POML constraint (1) (from 007's verifier pass 2) says the run must not use the MCP request's `HttpContext` after the tool
returns. The main session's instruction: if meeting it needs a change in `PlaybookOrchestrationService` or an executor,
stop and report the options instead of choosing one. Every way to meet it hits either that condition or another
binding rule.

### Facts (code, this branch @ 35e0f1529b merged)

- REST `POST /api/ai/playbooks/{id}/execute` runs the whole run **inside its request**: the handler drains
  `IPlaybookOrchestrationService.ExecuteAsync(request, httpContext, ct)` as SSE (`PlaybookRunEndpoints.cs:54`,
  `RequireRateLimiting("ai-stream")`). REST never detaches a run.
- The MCP host is stateless (`McpModule.cs:56-58`, `HttpServerSessionMode.Stateless`): the tool result ends the request.
  FR-14/NFR-01 want the start result in 5 s, so the run must outlive the MCP request.
- `ExecuteAsync` stores the request's `HttpContext` on `PlaybookRunContext` (`PlaybookOrchestrationService.cs:139-151`).
  The engine reads it for the trace id (:351, :1992) and hands it to the legacy orchestrator, which uses it for OBO
  (:917-928). The orchestration service is Scoped (`_activeRuns`, :62), so its dependencies come from the request scope,
  which is disposed when the request ends.
- `SendEmailNodeExecutor.cs:230` reads `IHttpContextAccessor.HttpContext` for OBO. About 45 other services that node
  executors reach read `IHttpContextAccessor` too (`DataverseUserClient`, `AnalysisDocumentLoader`, `RagService`,
  `RetrievalAccessTrim`, `UserPrivilegeChecker`, ...). After the request ends they get null, or a disposed scope.
- `ExecuteAppOnlyAsync` is the only entry point that runs without an `HttpContext`. It runs as the managed identity.
- ADR-052 MUST NOT (`docs/adr/ADR-052-workload-placement.md:188`): do not let the caller's token leave the BFF request
  that holds it. Acting for a user outside that request is allowed only through ADR-028 A5's conditional Dataverse
  impersonation, which is not usable until #988–#990 land.
- spec.md:249 says "ADR-004 / ADR-052: no new background work. Playbook runs keep their in-process execution". The spec
  never covers a run that outlives an MCP request.

## Options (for the owner; none chosen)

| # | Option | Engine edit? | Breaks |
|---|---|---|---|
| 1 | **Tool-side detach, house precedent `Services/Compose/ComposeProfileDispatcher.cs`.** Before returning, the tool captures the caller's principal and `Authorization` header, opens a new DI scope, builds a detached `DefaultHttpContext` (`RequestServices` = new scope, `User`, the header, `TraceIdentifier`), suppresses `ExecutionContext` flow, and sets the accessor inside the detached flow. It resolves `IPlaybookOrchestrationService` from the new scope and drains `ExecuteAsync` on a task with `ApplicationStopping`. It returns the run id from the `RunStarted` event, bounded to 5 s. The run never sees the MCP request's `HttpContext`. | No | **ADR-052 MUST NOT**: the caller's token leaves its request. **spec.md:249**: new in-process background work. A run in flight is lost on restart or scale-in and stays stored as Running (D-005-1 / #3178). Needs an ADR §6.5 Path A exception with auth sign-off. |
| 2 | **Engine change: a supported detached Person-run entry** on `PlaybookOrchestrationService`. It captures what the run needs at start and, outside the request, acts for the caller through ADR-028 A5 impersonation. | **Yes** | Fails D-030-adr039 (no engine edit), so it is an ADR-039 conflict for the owner. A5 needs #988–#990 and covers Dataverse only: SPE/Graph reads as the caller have no impersonation path. |
| 3 | **App-only run through the existing `ExecuteAppOnlyAsync`** (`StartedBy=Person`, `RunUserId` from 007). Authorization still happens first through 007's method. | No | **ADR-028 A10**: no app-only fallback for a user-initiated action. Node reads (documents, Dataverse queries) run as the managed identity, wider than the caller. It also breaks D-030-adr039's "same execute entry path as REST". |
| 4 | **Keep the run inside the MCP request.** The tool drains `ExecuteAsync` exactly as REST does, up to a bound under 30 s. Inside the bound it returns the completed result. Past it, it cancels the run (recorded Cancelled through 005's store) and returns an `isError` that gives the Console link. | No | Meets every ADR and D-030-adr039. **Spec FR-14 / NFR-01**: no 5 s start, and Copilot cannot complete runs over about 25 s. Changes FR-05 scope, which is the owner's call. `spaarke_get_playbook_run` still ships unchanged (durable reads). |

Not reached, because each comes after the run-start design is settled:

- idempotency storage. The fit found is `ITenantCache` (the Redis wrapper behind `IdempotencyFilter`), keyed on tenant + oid + key, with its in-flight lock. That is no new store.
- the A10 no-row resolution.
- the input caps.
- #3198.
- the PublicContracts run-link member.

## Found in passing (filed as D-030-1 in defer-issues.md)

`ComposeProfileDispatcher.RunAsync` sets `accessor.HttpContext = detachedContext` inside `Task.Run`, and does not
suppress `ExecutionContext` flow first. `HttpContextAccessor`'s setter clears the holder it inherits from the flow,
which is the save request's own holder. That nulls `IHttpContextAccessor.HttpContext` for the save request while the
request is still running (`ComposeService.cs:2323` and the steps after it). The same code also carries the bearer token
out of its request (ADR-052 MUST NOT).

rework: none task 030
