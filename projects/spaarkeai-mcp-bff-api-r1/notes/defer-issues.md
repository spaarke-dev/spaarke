# Defer / Issue Tracking — spaarkeai-mcp-bff-api-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarkeai-mcp-bff-api-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid deferral reason — refuse to file.

---

## Open (in priority order)

### ISS-001 — April 2026 Copilot agent code: an unregistered handler, two services nothing resolves, and a package the csproj calls "USED"

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-11-05 |
| **Source** | design.md §3 and §8 B-9 (BFF component-model review, 2026-11-05); owner approved filing (Q-11) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2575 |

**Description**

The April 2026 Copilot attempt left a chain of BFF code that no request reaches:
- `SpaarkeAgentHandler` (an `ActivityHandler`) is not registered anywhere: no `AddAgent`, no adapter, no `/api/messages` route. It is the only code that uses `Microsoft.Agents.Hosting.AspNetCore 1.0.1` (through `Microsoft.Agents.Builder` / `.Core`).
- `PlaybookInvocationService` and `EmailDraftService` are registered in `AgentModule` (L55, L58), but no endpoint, handler or test resolves them.
- `AdaptiveCardFormatterService` is registered (`AgentModule.cs:43`), but only those two services inject it. The `/api/agent/*` endpoints carry TODOs saying it is "not available" (`AgentEndpoints.cs:275,538`).

Concrete failure: the csproj comment (`Sprk.Bff.Api.csproj:68-70`) says the M365 Agents SDK package is "USED" by `SpaarkeAgentHandler`. A maintainer reading it keeps and upgrades a package that runs on no path. It still counts toward publish size (§10) and CVE scans, and two DI registrations build services nobody calls.

Not covered by existing issues: #1523 covers `AgentTokenService` / `AgentTokenOptions`; #1579 covers the auth-side dead surfaces and the bot template.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:21`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:43,55,58`
- `src/server/api/Sprk.Bff.Api/Api/Agent/EmailDraftService.cs`, `PlaybookInvocationService.cs`
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:68-70,81`
- `grep -rn "SpaarkeAgentHandler\|EmailDraftService\|PlaybookInvocationService" --include=*.cs src tests`

**Suggested fix**

Decide with the Copilot surface's owner. `spaarkeai-mcp-bff-api-r1` replaces this route with an MCP server, so remove the handler, the two services, the formatter and the package (measure the publish-size delta), and correct the csproj comment. Keep the `/api/agent/*` endpoints while the "Spaarke AI" agent package still calls them.

**Estimated effort**: half a day, plus the publish-size measurement
**Blockers**: none. If `spaarkeai-mcp-bff-api-r1` adopts the declarative-agent fallback (design §5.2), do this together with retiring the April OpenAPI action.
**Related**: #1523, #1579, #1574 (the "remove before production" token logger, also found by this design and already filed there); ADR-010; CLAUDE.md §10

---

## In Progress

### ISS-002 — ComposeProfileDispatcher may null the originating request's HttpContext

| Field | Value |
|---|---|
| **Status** | In Progress (investigation and fix running; owner: "investigate fully, do not defer") |
| **Urgency** | now |
| **Filed** | 2026-11-07 |
| **Source** | Read-only investigation for task 010 (option C analysis), 2026-11-06 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2620 |

**Description**

`ComposeProfileDispatcher.cs:130-134` sets `IHttpContextAccessor.HttpContext` inside `Task.Run`. In ASP.NET Core 10.0.14 the setter clears the existing AsyncLocal holder's context. That holder flowed into the task from the originating Compose save request, so code later in that request may read a null context intermittently. That code comes after `ComposeService.cs:2323`. A null context would make caller-dependent paths fail closed. No test proves it yet.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeProfileDispatcher.cs:88`, `:117-134`
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:2323`

**Suggested fix**

Reproduce it with a test first. If it is real, never mutate the shared holder inside the task: use an explicit detached context, or `ExecutionContext.SuppressFlow()` plus a fresh holder.

**Estimated effort**: 0.5–1 day
**Blockers**: none
**Related**: task 010 option C (owner, 2026-11-06); Compose owners review the fix PR

---


*None.*

---

## Closed (Done / Won't Fix / Superseded)

*None.*

---

## Notes

- IDs are sequential per kind: DEF-001, DEF-002, ... ISS-001, ISS-002, ...
- ID never gets reused after closure — preserves traceability.
- **Already filed elsewhere, not duplicated here:** the "DEBUG" token logger (`MiddlewarePipelineExtensions.cs:121-158`) is #1574 (labels `unified-access-control-r2`, `spaarke-auth-system-of-record-r1`).
