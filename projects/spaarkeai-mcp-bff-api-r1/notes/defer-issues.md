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

### ISS-005 — Rate-limit OnRejected log writes the user's name (a claim value) on every 429

| Field | Value |
|---|---|
| **Status** | Open (pre-existing on master; not caused by this project) |
| **Urgency** | soon (MCP routes now reach the handler) |
| **Filed** | 2026-11-12 |
| **Source** | Task 020 adversarial review |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2756 |

**Description**

`RateLimitingModule.cs` `OnRejected` (~L321-326) logs `User.Identity.Name` and the client IP at Warning on every 429. ADR-015 / NFR-05 allow ids, sizes, timings and reason codes, not claim values. Concrete failure: a Copilot client over the 60/min `mcp` limit writes the user's UPN to App Insights on each rejected call.

**Suggested fix**

Log the opaque caller key the limiter partitions by (`oid`, else `sub`), or no user at all; add a regression test that the rejected-request log has no UPN or email. Shared code: the BFF platform owner fixes it, not task 020.

---

### ISS-004 — The decision commit does not require a prior plan read, although the spec says it must

| Field | Value |
|---|---|
| **Status** | Closed 2026-11-07: intended behaviour (ontology ruling). A missing stamp means "review starts now", the fail-safe direction. Spec FR-08, NFR-06 and the MUST rule are amended; the pinning test is kept. |
| **Urgency** | soon (before task 024 ships the MCP complete tool) |
| **Filed** | 2026-11-07 |
| **Source** | Task 014 (B-1 commit capability), acceptance criterion "a commit with no prior plan read returns the existing refusal reason code" |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2628 |

**Description**

NFR-06, FR-08's negative case, the MUST "fail closed on … a missing plan read" and design §11 say a commit needs a prior plan read. `DecisionCommitService` does not refuse a commit that has no plan-read stamp: it takes the review start as now (`DecisionCommitService.cs:672-674`). The only refusal that a missing plan read causes is `ontology.decision.signal_not_open` (409), for a Signal the system closed before the commit.

Concrete failure: a client, or an MCP host after task 024, that sends a confirmed review with the plan's action codes and never reads the plan gets the decision recorded while the Signal is open. The commit is still as the caller, rights-checked, confirmed and idempotent; only the "plan read first" condition is not enforced.

**Suggested fix**

Owner and ontology choose: (1) amend the spec text (the stamp sets the review start and is not a gate), or (2) ontology adds a refusal for "no stamp" in `DecisionCommitService`, which is a REST contract change with a new 011 baseline case. Do not gate only the MCP tool (a second rule outside the single owner; ADR-002 WP-1, ontology condition 2). Task 014 could not do either: the spec forbids changing `DecisionCommitService` validation and B-1 adds no rule.

**Estimated effort**: option 1, one spec edit; option 2, half a day in ontology plus a baseline case
**Blockers**: owner decision; option 2 needs ontology-platform
**Related**: task 014 (`DecisionCommitServiceTests.Capability.cs` pins today's behaviour), task 024, FR-06, FR-08, NFR-06

---

## In Progress

### ISS-003 — PrecedentAdminEndpoints background task may use a disposed request-scoped service (unconfirmed)

| Field | Value |
|---|---|
| **Status** | Open (found in passing; not confirmed) |
| **Urgency** | soon |
| **Filed** | 2026-11-07 |
| **Source** | Sibling scan for ISS-002 (#2620 / PR #2622) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2623 |

**Description**

`Api/Insights/PrecedentAdminEndpoints.cs:294` starts a fire-and-forget `Task.Run` that uses `IPrecedentProjectionSync`. That service is request-scoped (`Infrastructure/DI/InsightsModule.cs:67`). If the task outlives the request, it can use disposed services. This is not caused by this project; the Precedents/Insights owners decide the fix (a fresh scope inside the task, or an ADR-004 job).


### ISS-002 — ComposeProfileDispatcher may null the originating request's HttpContext

| Field | Value |
|---|---|
| **Status** | Fix in review: draft PR #2622 (`30b887c710`), defect CONFIRMED 2026-11-07 by a test that fails before the fix and passes after; awaiting Compose owners' review |
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
