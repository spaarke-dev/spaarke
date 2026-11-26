# How to Add an MCP Tool or Area

> **Last Updated**: 2026-11-25
> **Purpose**: Add a tool to the `legal-operations` area of Spaarke's MCP server, or add a new area (`/mcp/{area}`), with the tests each needs.
> **Audience**: A developer changing `Sprk.Bff.Api`.

---

## Prerequisites

- [ ] Read [`SPAARKE-MCP-SERVER-ARCHITECTURE.md`](../architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md) once: the auth rule, the area model and the limits.
- [ ] Read `.claude/constraints/bff-extensions.md` and answer root `CLAUDE.md` §11's three questions (existing, extension, cost of doing nothing) for the new surface. A new tool is new surface even inside an existing folder.
- [ ] The logic the tool exposes already lives in a service the REST route uses. If it does not, write the service first; the tool is only an adapter.
- [ ] .NET 10 SDK. For a tool that changes the widget: Node 20 or later.

The agent-facing summary of these rules is `.claude/skills/mcp-tool-handler/SKILL.md`. When this guide and the code disagree, the code wins.

## Quick Reference

| Item | Value |
|---|---|
| Server | One, in the BFF. A new product area is a new route, never a second server. |
| Route and area constants | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpEndpoints.cs` (`LegalOperationsRoute`, `LegalOperationsArea`) |
| Tool base class and attribute | `McpToolDefinition`, `[McpArea]` in `Api/Mcp/McpToolCatalog.cs` |
| Where tools live | `src/server/api/Sprk.Bff.Api/Api/Mcp/{Area}/{Group}/{Verb}{Object}Tool.cs` |
| Tool name | `spaarke_{verb}_{object}`, lower snake case, unique across areas |
| Startup validation | `Api/Mcp/McpToolAuthorization.cs` `ValidateTool` (a breaking tool stops the app from starting when `Mcp:Enabled` is true) |
| Route and tool ledger | Rules, `McpRoutes` and the `McpTools` concatenation: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`. Tool rows: one partial per area, `RouteAuthorizationGuardTests.Mcp.{Area}.cs` (`Decisions`, `Search`, `Documents`, `Playbooks`, `Analysis`, `Tasks`, `Portfolio`, `Briefing`, `MatterSummary`, `Finance`, `Email`) |
| Error result | `McpToolResults.FromError(McpToolError, correlationId)` in `Api/Mcp/McpToolResults.cs`; decision tools pass a `DecisionToolError` to the overload that maps it |
| Work over 30 s | `McpLongRunning.Started` / `McpLongRunning.Status` in `Api/Mcp/McpLongRunning.cs` (budget `McpLongRunning.Budget`) |
| Copy from | read-only, model-visible: `ShowDecisionsTool.cs`; app-only read: `ListDecisionPageTool.cs`, `GetDecisionPlanTool.cs`, `SearchDecisionLookupTool.cs`; app-only write: `CompleteDecisionTool.cs` (all under `Api/Mcp/LegalOperations/Decisions/`) |

## Procedure: add a tool to `legal-operations`

### Step 1: Decide the shape

- **Read or write.** A read-only tool sets `ReadOnly = true`. Any tool that writes sets `ReadOnly = false` and **must** be app-only: the model never commits. Startup refuses a non-read-only tool that is not app-only.
- **Visible to the model or only to the widget.** A tool only the widget calls is app-only: `Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } }`. A model-visible tool that renders a widget adds `["resourceUri"] = "ui://…"` and visibility `["model", "app"]`.
- **Model-visible output is minimal.** Counts and slim rows only; anything privileged goes to the widget through an app-only tool, as the caller (ADR-015).

### Step 2: Write the tool class

Create the file under `Api/Mcp/LegalOperations/{Group}/`. Copy the nearest tool and keep this shape:

1. A class that derives from `McpToolDefinition`, tagged `[McpArea(McpEndpoints.LegalOperationsArea)]` and `[Authorize]` (bare, or `Policy = "SystemAdmin"`; no other policy, no authentication scheme, no anonymity). Not `partial`.
2. `Create(IServiceProvider services)` returns `McpServerTool.Create(<static method group>, new McpServerToolCreateOptions { Services = services, … })`. Pass the handler as an unqualified method group of the same class (the SDK enforces the handler's declaring class's `[Authorize]`), and do not set `Metadata`.
3. Set explicitly: `Name`, `Title`, `Description` (use for / not for, at most 1,024 characters), `ReadOnly`, and `Idempotent`, `Destructive`, `OpenWorld` where they apply. A missing `ReadOnly` or title stops startup. Without a `ReadOnly` hint Copilot treats a tool as a write and asks for approval on every call.
4. State the input schema (`tool.ProtocolTool.InputSchema = …`) with `additionalProperties: false` and closed enums, and read the arguments against the same set with `DecisionToolCall.TryReadSingleArgument` (or the SDK binding for typed parameters), so an invented argument is refused, not ignored.
5. The handler is `static` and takes what it needs as parameters: `IHttpContextAccessor`, the capability or service, `TimeProvider`, `ILogger<T>`, `CancellationToken`. It holds no state between calls and registers nothing in DI.
6. Pass exactly `IHttpContextAccessor.HttpContext` to the service. A null context or an unauthenticated caller is the service's `UserContextRequired` (use `PlanAndLookupToolSupport.HasCaller` or `DecisionCapability.HasSignedInCaller`). No fallback to an app-only client.
7. Map every failure to an `McpToolError(reasonCode, message, status, partial?)` and return `McpToolResults.FromError(error, http?.TraceIdentifier)`, so a domain failure is an `isError` result with `structuredContent` `{ reasonCode, message, status, correlationId?, partial? }`, never a thrown or transport error (ADR-019). Use the REST route's `reasonCode` and HTTP status for the same failure, and a message that says how to fix the call with no record text (ADR-015). `partial` is for what a failed write left behind (codes, ids and flags only). Decision tools keep their mapper: `DecisionResultMapper.ToToolError` gives a `DecisionToolError`, and the `FromError` overload that takes it maps it field for field, so their bytes did not change.
8. **Answer within 30 seconds** (`McpLongRunning.Budget`, FR-14). Work that can take longer starts the run through its service and returns `McpLongRunning.Started(runId, McpRunState.Queued or Running, consoleLink, oneLineSummary)`: `structuredContent` `{ runId, status, consoleLink }`. A separate status tool returns `McpLongRunning.Status(runId, state, progress?, result?, summary?)`: `{ runId, status (queued, running, completed or failed), progress?, result? }`. The helper only builds the two results; it starts, polls and stores nothing (ADR-052), so the run and its record belong to the service. Build `consoleLink` with an existing deep-link builder (`HandoffUrlBuilder` or `IDeepLinkBuilder`), never by hand; `Started` refuses anything but an absolute https URL. `HandoffUrlBuilder` is in `Services/Ai/**`, so a tool cannot inject it (ADR-013): take the link from the public-contract seam the tool already calls. A refusal to start is an error result (item 7); a run that failed is a successful status call with status `failed`.
9. Wrap the call in `DecisionToolCall.LogAroundAsync` (or write the same one line): tool, outcome, row count, elapsed time, correlation id. Log no argument, token, claim or record text.
10. Keep the result under 64 KiB at worst case. Bound anything with unbounded text the way `PlanAndLookupToolSupport.BoundRow` does.

No DI registration and no shared registration file is edited: the catalog finds the class by attribute. If the work needs a new service, interface or package, that is §10 and §11 territory; do it and justify it.

### Step 3: Add the ledger entry

In your area's ledger partial, `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.{Area}.cs` (for example `McpToolsSearch` in `RouteAuthorizationGuardTests.Mcp.Search.cs`), add one `McpTool`. Do not edit the list in `RouteAuthorizationGuardTests.Mcp.cs`: it concatenates every partial, so tool tasks running in parallel never edit the same list. An empty partial starts as `Array.Empty<McpTool>()`; replace it with `new[] { … }`.

```
new McpTool("{ClassName}", "", "{ISeamItCalls}", "{reason of 60 or more characters}")
```

The policy is `""` for bare `[Authorize]` or `"SystemAdmin"`. The seam is the service the tool calls as the caller. The test fails on any `[McpArea]` class missing from the ledger (including a row in a partial that is not concatenated), a class listed twice, any ledger entry with no class, a `partial` tool class, a missing `ReadOnly` or title, and any injected type declared under `Services/Ai/**` outside the public contracts (ADR-013). It reads text, so a seam that is named but never called passes; a reviewer reads what the tool does with it.

### Step 4: Tests a tool needs

| Kind | Where | What it proves |
|---|---|---|
| Route and tool guard | the ledger entry above, run by `dotnet test tests/Spaarke.ArchTests/` | the tool is declared, authorized and annotated |
| Auth | `tests/integration/auth/Mcp/` (`McpAuthTests.cs`, `McpCallerPathTests.cs`) | no token gives `401`; a refused token type runs no tool; the tool reads as the caller and fails closed with no caller; a policy-restricted tool is hidden from, and refused to, an outsider |
| Contract | `tests/integration/contract/Api/Mcp/` (`ShowDecisionsToolTests.cs`, `PlanAndLookupToolTests.cs`, `McpHostContractTests.cs`; host `DecisionToolsTestHost.cs`) | the `tools/list` entry (name, title, hints, visibility, schema), the happy path, an invalid argument as an `isError` result, and that the real BFF host lists the expected set (update `RealBffHost_ToolsList_WhenEnabledWithAValidToken_IsExactlyTheFiveV1Tools`) |
| Result size | `tests/integration/regression/Mcp/McpResultSizeTests.cs` with the fixtures in `McpFiveToolFixtures.cs` | the result is a success under 64 KiB at the worst-case seed (200 Work Items) |
| Writes only | `tests/integration/data-mutation/Mcp/` (`CompleteDecisionToolTests.cs`, `McpCommitCallerPathTests.cs`) | idempotency or replay, nothing written on a refusal, the failed-commit facts |
| Privilege reduction (model-visible tools) | `tests/integration/auth/Mcp/DecisionsPrivilegeReductionTests.cs` | privileged items never reach the model text |
| Conformance | CI job `mcp-conformance` in `.github/workflows/ci-tier1-blocking.yml`; locally `bash tests/integration/Mcp.Conformance.Host/run-conformance.sh` | the MCP protocol suite still passes. Update the host's fixtures or `conformance/conformance-baseline.yml` only for a known, recorded reason |

If the model should call the new tool, also update the plugin skill under `src/solutions/CopilotPlugins/spaarke/skills/`, then run `cd src/solutions/CopilotPlugins/spaarke && npm test`.

### Step 5: Build and run the guards

```bash
dotnet build src/server/api/Sprk.Bff.Api/
dotnet test tests/Spaarke.ArchTests/
dotnet test tests/integration/auth/ --filter "FullyQualifiedName~Mcp"
```

Then the contract, regression and data-mutation MCP tests you touched. Per §10, measure the publish-size delta against a fresh master build and check for new HIGH CVEs. Auth-touching changes go to the uac-r2 reviewer; changes under `Services/Signals/**` or `Api/Signals/**` go to the ontology project.

## Procedure: add a new area

A new area is a new route on the same host, for example `/mcp/billing-review`. Do not create a second MCP server, host or service: that would need its own auth, deploy and registration (ADR-013 placement).

1. **Name it.** Lower-case, hyphenated; never "core" (it means core record). One connector per product area, not per function. Check the naming rows in `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §1.1.
2. **Add the route.** In `McpEndpoints.cs` add constants beside `LegalOperationsRoute` and `LegalOperationsArea`, and one more `app.MapMcp(...)` statement with the same chain: `.RequireAuthorization()` (no named policy), `.RequireMcpConnectorToken()`, `.RequireRateLimiting("mcp")` (keep the policy name a literal), the `RequestSizeLimitAttribute`, `new McpAreaMetadata(<area>)`, response buffering off, then `.RequireMcpEnabled()`. Leave the order as it is.
3. **Tag its tools** `[McpArea(McpEndpoints.{Area}Area)]` and put them under `Api/Mcp/{Area}/{Group}/`. The catalog and `McpModule` pick the route's tools up by area with no other change. Tool names must be unique across areas.
4. **If the area has a widget:** add a `{Widget}WidgetResource` class like `DecisionsWidgetResource.cs` (static, empty CSP, no request or caller data), a widget source folder under `src/client/widgets/`, an `EmbeddedResource` line in `Sprk.Bff.Api.csproj` and a step in `.github/workflows/publish-bff-artifact.yml`, and an `if (area == …)` branch in the `ConfigureSessionOptions` of `Infrastructure/DI/McpModule.cs` that sets `ResourceCollection`. That is the only sanctioned place a resource is registered.
5. **Add the route to the ledger.** In `RouteAuthorizationGuardTests.Mcp.cs` add an `McpRoute("POST /mcp/{area}", "{area}", "<reason>")` to `McpRoutes`. `UnlistedMapMcpRoutes` fails on any `MapMcp` not listed. Never credit an MCP route with a waiver. Then add each tool's `McpTools` entry (Step 3 above). If the area's tools do not fit an existing ledger partial, add `RouteAuthorizationGuardTests.Mcp.{Area}.cs` and its line in `McpToolPartials` in `RouteAuthorizationGuardTests.Mcp.cs`.
6. **Tests for the area.** The tool tests above for each tool, plus:
   - an anonymous request to the new route is `401` with `Bearer` whatever `Mcp:Enabled` is, a valid token on an unmapped path is `404` (pattern: `McpHostContractTests.cs`, `McpAuthTests.cs`);
   - the route lists only its own area's tools (`InitializeAndToolsList_WhenEnabledWithAValidToken_Succeed_AndListOnlyTheAreasTools`);
   - the kill switch still gives `503` `mcp.disabled` for the new route;
   - the conformance job runs against the new route (extend `tests/integration/Mcp.Conformance.Host/`).
7. **Register it with Microsoft 365.** One connector per area, registered once per tenant, using the same Entra client and OAuth registration (a second area connector shares the sign-in, as Phase 0 T14 showed in Cowork). Follow [`SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md`](SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md) and tell copilot-platform-coordination (GitHub issue #3006). A plugin package for the area carries skills only.
8. **Deploy dark.** `Mcp:Enabled` already gates every route; turning it on in an environment is the owner's decision per environment.

## Verification

1. `dotnet test tests/Spaarke.ArchTests/` passes, including `EveryMcpRouteAndToolIsDeclared`.
2. With `Mcp__Enabled=true` the app starts. If it does not, the message names the tool and the rule it broke.
3. `tools/list` on the area's route returns the new tool with its title, `readOnlyHint` and visibility as intended, and nothing from another area.
4. The new tool's result is under 64 KiB in `McpResultSizeTests`.
5. In Copilot Chat or Cowork the model calls a model-visible tool, and never an app-only one (ask it to list the Spaarke tools).

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| App does not start with `Mcp__Enabled=true`, "has no [Authorize]" | The attribute is missing from the tool class | Put `[Authorize]` on the class and build the tool from a method of that class |
| "the [Authorize] the SDK enforces … is not the tool class's own" | The handler is in a helper class, or `Metadata` was set | Make the handler a static method of the tool class and pass it as an unqualified method group |
| "does not set ReadOnly explicitly" or "has no title" | Annotation left out | Set `ReadOnly` and `Title` |
| "is not read-only, so it must be app-only" | A write tool visible to the model | Set `visibility` to exactly `["app"]` |
| "names the '…' policy, which is not in AllowedToolPolicies" | A policy other than bare or `SystemAdmin` | Use an allowed policy; widening the set is a separate reviewed production change |
| Arch test: "an MCP tool missing from McpTools" | No ledger entry | Add it (Step 3) |
| Arch test: injects a type from `Services/Ai/**` | A tool reaches into the AI pipeline | Use the public contract facade (ADR-013) |
| `429` in a widget flow | One call per keystroke spends the 60 per minute budget | Debounce and set a minimum length in the widget |
| Copilot asks approval for a read | `ReadOnly` was not set true | Set it explicitly |
| Result dropped by the host | Result is 64 KiB or more | Bound the payload; page it |

## Related

- [`SPAARKE-MCP-SERVER-ARCHITECTURE.md`](../architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md)
- [`SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md`](SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md)
- [`.claude/skills/mcp-tool-handler/SKILL.md`](../../.claude/skills/mcp-tool-handler/SKILL.md)
- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) (A10), [`ADR-013`](../../.claude/adr/ADR-013-ai-architecture.md), [`ADR-015`](../../.claude/adr/ADR-015-ai-data-governance.md), [`ADR-032`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)
- `.claude/constraints/bff-extensions.md`
