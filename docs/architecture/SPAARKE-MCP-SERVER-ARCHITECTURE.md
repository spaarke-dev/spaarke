# Spaarke MCP Server Architecture

> **Last Updated**: 2026-11-24
> **Purpose**: How the BFF hosts Spaarke's MCP server (`/mcp/{area}`), who may call it, and how the Decide lane and Decision wizard reach Microsoft 365 Copilot Chat and Cowork through it.

---

## Overview

Spaarke exposes one MCP server, and it lives inside the BFF (`Sprk.Bff.Api`). Microsoft 365 Copilot Chat and Cowork call it through a Copilot connector ("Spaarke MCP"). v1 serves one product area, **legal-operations**, at `POST /mcp/legal-operations`. It lets a user see the Work Items waiting for their decision (the Decide lane) and review and complete them in a widget that renders inside the host. A later area (billing review, Patents) is a new `/mcp/{area}` route on the same host, never a second MCP server or service (ADR-013 placement). Agents never get their own connector or server: a practice-area agent reaches its area's route through its own MCP action. Which surface gets which packaging is set in [SPAARKE-AI-SURFACES-AND-PACKAGING.md](SPAARKE-AI-SURFACES-AND-PACKAGING.md).

The key design decision is that a tool is a thin adapter. Every tool runs **as the signed-in caller** (on-behalf-of, the same as the Console) and calls the service the REST route already calls. It adds no second validation, billing refusal or write path. The route is **stateless**, which is what keeps `IHttpContextAccessor.HttpContext` populated for each tool call and so keeps the caller's identity intact.

The server accepts only ADR-028 Amendment A10 tokens: a delegated `user_impersonation` token for a user in the Spaarke tenant, issued for the BFF app. The connector signs the user in with OAuth 2.0 (not Entra SSO). Registering it is covered in [`SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md`](../guides/SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md).

## Component Structure

| Component | Path | Responsibility |
|---|---|---|
| Route mapping | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpEndpoints.cs` | One `MapMcp` per area; the endpoint filter chain; the 256 KiB request limit; `McpEnabledFilter` (kill switch) |
| Options | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpOptions.cs` | `Mcp:Enabled`, default `false` |
| DI module | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/McpModule.cs` | Stateless transport; kill-switch swap; per-request tool and resource sets |
| Tool catalog | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpToolCatalog.cs` | `McpAreaAttribute`, `McpToolDefinition`, `McpAreaMetadata`; finds tools by attribute and partitions them by area; `NullMcpToolCatalog` |
| Authorization | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpToolAuthorization.cs` | The A10 token rule (`McpConnectorTokenFilter`); the per-tool rule (`ValidateTool`) |
| Error results | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpToolResults.cs` | Turns a decision failure into an `isError` tool result |
| Result mapper | `src/server/api/Sprk.Bff.Api/Api/Signals/DecisionResultMapper.cs` (and `DecisionResultMapper.Lookup.cs`) | The one place decision-slice results map to a tool error, with the REST route's `reasonCode` |
| Decision capability | `src/server/api/Sprk.Bff.Api/Services/Signals/IDecisionCapability.cs`, `DecisionCapability.cs`, `DecisionCapability.Commit.cs` | List the lane, page it, read a plan and suppression, commit a decision, all as the caller |
| Lookup search | `src/server/api/Sprk.Bff.Api/Services/Signals/IDecisionLookupSearch.cs`, `DecisionLookupSearch.cs` | The wizard's person, budget and firm search |
| Tools | `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/` | `ShowDecisionsTool.cs`, `ListDecisionPageTool.cs`, `GetDecisionPlanTool.cs`, `SearchDecisionLookupTool.cs`, `CompleteDecisionTool.cs` |
| Tool helpers | same folder | `DecisionToolCall.cs` (closed-argument check, telemetry line), `PlanAndLookupToolSupport.cs` (row bounds), `DecideLaneCategories.cs`, `DecisionsTextSummary.cs` |
| Widget resource | `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/DecisionsWidgetResource.cs` | Serves `ui://spaarke/decisions.html` through `resources/read` |
| Widget source | `src/client/widgets/spaarke-decisions/` | The single-file React widget (lane and wizard) |
| Rate limit | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/RateLimitingModule.cs` | The `mcp` policy |
| Plugin package | `src/solutions/CopilotPlugins/spaarke/` | Skills only; carries the `spaarke-decisions` skill |
| Route and tool guard | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs` | The `McpRoutes` and `McpTools` ledgers |

## The route and area model

- **One route per area.** `McpEndpoints.MapMcpEndpoints` maps `/mcp/legal-operations` (constants `LegalOperationsRoute` and `LegalOperationsArea`). The area is stamped on the endpoint as `McpAreaMetadata`. "core" is not used as an area name because it already means core record (matter, project, work assignment).
- **A tool names its area by attribute.** Each tool is one `McpToolDefinition` subclass tagged `[McpArea(McpEndpoints.LegalOperationsArea)]`. `McpToolCatalog` scans the BFF assembly for tagged classes, so adding a tool edits no shared registration file. A route lists only its own area's tools; an area with none lists none, and never falls back to another area's. Tool names are unique across areas.
- **The tool set is chosen per request.** The transport is stateless, so the SDK runs `ConfigureSessionOptions` on every request. `McpModule` reads the area from the matched endpoint and sets the tool collection (and, for `legal-operations`, the widget resource) there.
- **Why not the SDK's own scan.** `WithToolsFromAssembly` registers one flat set and cannot partition by area, and picking a method by reflection is banned in the BFF.
- **Stateless, no affinity.** No session store, no `Mcp-Session-Id`, no SSE stream (a `GET` answers 405). That fits ADR-001 and ADR-052 (no background work).
- **Unknown area.** An authenticated call to `/mcp/<unmapped>` is 404.

## Data Flow

A chat turn through the connector:

1. Copilot (Chat or Cowork) posts a JSON-RPC call to `/mcp/legal-operations` with a bearer token the connector obtained by OAuth. A turn is two to four stateless requests (`initialize`, `tools/list`, `tools/call`, plus the widget's own calls).
2. `.RequireAuthorization()` runs the default workforce JwtBearer scheme: signature, lifetime, Spaarke-tenant issuer, BFF audience. No token gives `401` with `WWW-Authenticate: Bearer`.
3. `McpConnectorTokenFilter` applies the A10 rule (next section). A refused token gives `401` with `invalid_token` and `errorCode` `mcp.token_not_accepted`; no tool, list or handshake runs.
4. The `mcp` rate-limit policy, the 256 KiB body limit and response-buffering-off apply. Over the limit gives `429` or `413`.
5. `McpEnabledFilter` checks the kill switch. Off gives `503` `mcp.disabled`.
6. The SDK transport dispatches. Its authorization filters check each tool's `[Authorize]` on `tools/list` (hides the tool) and `tools/call` (refuses it).
7. The tool reads `IHttpContextAccessor.HttpContext` and passes exactly that to the capability. A null context or an unauthenticated caller is `UserContextRequired` before any read; nothing falls back to app-only.
8. The capability acts as the caller (OBO to Dataverse). A domain failure comes back as an `isError` tool result, never a transport error.
9. The tool writes one telemetry line and returns `content` (short text) and `structuredContent`.

Route-guard order, outermost first, as built in `McpEndpoints.cs`: authorization, A10 token filter, rate limit, request-size limit, area metadata, buffering off, kill switch. The kill switch runs after authorization, so a switched-off server still answers an anonymous caller `401`.

## A10 auth flow and refusals

The rule is `McpToolAuthorization.A10DenyReason`. The shared workforce scheme also serves other planes, so it accepts more than A10 allows; A10 is enforced explicitly here and the shared scheme is not changed. A call is refused (`401`) unless all of these hold, each read in its short and mapped claim form, and a multi-valued claim passes only when every value passes:

| Check | Refusal reason (logged, never sent to the caller) |
|---|---|
| An authenticated identity | `not_authenticated` |
| Not an A9 home-directory identity | `ciam_or_home_directory_scheme` |
| `AzureAd:TenantId` and `AzureAd:ClientId` are non-zero GUIDs | `spaarke_tenant_or_bff_app_not_configured` (fail closed) |
| `tid` is the Spaarke tenant (this also refuses CIAM tokens) | `tid_not_spaarke_tenant` |
| `iss` is exactly `https://login.microsoftonline.com/{tenant}/v2.0` | `iss_not_exact_v2_for_spaarke_tenant` |
| `aud` is the BFF app (the Copilot audience is refused) | `aud_not_bff_app` |
| `idtyp`, if present, is `user` | `app_token` |
| `scp` contains `user_impersonation` | `user_impersonation_scope_missing` |
| `oid` is a non-zero GUID | `oid_missing_or_malformed` |

A token that passes but resolves to no Dataverse `systemuser` reaches the tool, and the capability refuses it with `UserContextRequired`. The filter logs the reason and nothing from the token. No tool passes the caller's token downstream; each exchanges it on-behalf-of through the existing workforce paths.

**Per-tool rule** (`McpToolAuthorization.ValidateTool`, run on every tool when the catalog is built). The app does not start if a tool breaks it, so a mistake cannot ship:

- `[Authorize]` is on the tool class, and the metadata the SDK enforces equals it (the tool is built from a static method of its own class).
- The policy is in `McpToolAuthorization.AllowedToolPolicies`: bare `[Authorize]` or `SystemAdmin`. No authentication scheme is named and no anonymity metadata is present. A named policy must be registered.
- `ReadOnly` is set explicitly, and a title is set.
- A tool that is not read-only is app-only (`_meta.ui.visibility` is exactly `["app"]`): no model-callable write tool in v1.

`RouteAuthorizationGuardTests.Mcp.cs` checks the same rules in source and by reflection. **Stated limit:** the SDK's per-tool filter evaluates policy requirements on the user the A10 filter admitted; it does not re-run a policy's own authentication schemes.

## The decision capability, lookup search and result mapper

- **`IDecisionCapability`** is the decision slice's host-neutral seam. The tools call `ListDecideAsync` (the whole lane, for counts), `ListDecidePageAsync` (one page, cursor-continued), `GetPlanAsync`, `GetSuppressionAsync` and `CommitAsync`. The REST Signals routes call the same capability, so the two hosts share one rule set.
- **`IDecisionLookupSearch.SearchAsync`** finds the person, budget or firm for a plan parameter, read as the caller. The decision plan, not the caller, names the table and scope. At most 25 candidates (`DecisionLookupSearch.MaxResults`).
- **`DecisionResultMapper`** is the one mapping from a decision result to a `DecisionToolError` (reason code, fix-the-call message, HTTP status, and for a failed commit the `partial` facts). The vocabulary equals the REST route's `reasonCode`. Messages never carry record text (ADR-015).
- **`McpToolResults.FromError`** wraps it as an `isError: true` tool result, with `structuredContent` `{ reasonCode, message, status, correlationId?, partial? }`. A domain failure is a successful JSON-RPC answer carrying a failed tool result (ADR-019), so Copilot reads it and can correct the call.
- **`DecideLaneCategories`** is the server-side partition of the lane into the Console's category cards. It mirrors `laneCountFilters.ts`; both are asserted against one shared corpus, `tests/fixtures/decide-lane-category-parity/cases.json`, so they cannot drift.

## The five tools

All five are in area `legal-operations`, bare `[Authorize]`, and named `spaarke_{verb}_{object}`. Copilot shows them prefixed with the connector name (`SpaarkeMCP_`).

| Tool | Class (in `Api/Mcp/LegalOperations/Decisions/`) | Visibility | `readOnlyHint` | Purpose |
|---|---|---|---|---|
| `spaarke_show_decisions` | `ShowDecisionsTool` | model and app (`_meta.ui.resourceUri` = the widget) | true (idempotent, not destructive, closed-world) | The Decide lane: counts per category and top items; one optional closed `category` argument |
| `spaarke_list_decision_page` | `ListDecisionPageTool` | app only | true | One page (up to 50) of full lane rows by cursor, for the widget |
| `spaarke_get_decision_plan` | `GetDecisionPlanTool` | app only | true | The item's decision plan, suppression and bounded full row; records that the user has read the plan |
| `spaarke_search_decision_lookup` | `SearchDecisionLookupTool` | app only | true (so typing raises no consent prompt) | Wizard type-ahead for a person, budget or firm; at most 25 candidates |
| `spaarke_complete_decision` | `CompleteDecisionTool` | app only | **false** (not idempotent, not destructive) | Records one decision after the person pressed Confirm; writes one Decision Record and closes the Work Item |

Conventions common to the tools:

- **Explicit schema, closed arguments.** `ShowDecisionsTool` states `additionalProperties: false` and a closed `category` enum; `DecisionToolCall.TryReadSingleArgument` makes that true at run time, so an invented argument is refused with a fix, never ignored.
- **Model-visible output is reduced.** `spaarke_show_decisions` returns counts and slim rows. A privileged Work Item is only counted ("N privileged items — open to review"), via `DecisionsTextSummary`. Detail reaches the widget only through the app-only tools, as the caller.
- **Writes are enforced on the server.** `CompleteDecisionTool` calls `CommitAsync`. `DecisionCommitService` stays the single owner of Confirm, idempotency, the invoice-review refusal and the caller's rights. The tool is app-only and not idempotent (a retry that carries a send choice can send an inquiry email again). No behaviour depends on a host consent prompt: the host shows none for widget-initiated writes (Phase 0 T3).
- **Descriptions** are "use for / not for", at most 1,024 characters. The server's own MCP `instructions` field is not relied on: Copilot ignored it in testing (Phase 0 T7).

## The widget and its embedding

- **Source.** `src/client/widgets/spaarke-decisions/` (React, Fluent v9 semantic tokens, host light and dark themes). `src/App.tsx` is the shell; `src/lane/` is the Decide lane; `src/wizard/` is the Decision wizard dialog; `src/mcp/mcpHost.ts` is the connection to the MCP Apps host (built on `@modelcontextprotocol/ext-apps`).
- **It holds no token and opens no connection.** Every read and write is `callServerTool` through the host's `postMessage` bridge, mapped to the app-only tools. The lane reads pages through `spaarke_list_decision_page`, not from `spaarke_show_decisions`, because the latter is reduced for the model.
- **One file.** `npm run build` produces `dist/index.html` and fails above 3 MB, on any sibling file in `dist/`, or on any external load (`scripts/check-bundle-size.mjs`). The aim is under 1 MB.
- **Embedded in the BFF.** `Sprk.Bff.Api.csproj` embeds the file as resource `Sprk.Bff.Api.Widgets.decisions.html`, with `Condition=Exists` so a local `dotnet build` without the widget still succeeds. `DecisionsWidgetResource` serves it through `resources/read` as `ui://spaarke/decisions.html` with MIME `text/html;profile=mcp-app`. Without the widget `resources/read` answers a JSON-RPC error, never a stale page, and startup logs one warning if the server is on. `.github/workflows/publish-bff-artifact.yml` builds the widget first and verifies the published DLL embeds it, so a published artifact always carries it.
- **Empty CSP.** The resource declares `_meta.ui.csp` as `{}` (no network origins), so the widget can talk to nothing but its host. The page is static and carries no data; `McpModule` is the only place a widget resource is sanctioned (the route-guard ledger enforces it).
- **Where it renders.** Chat and Cowork. Word runs the tool without the widget; Outlook does not offer the connector (Phase 0 T1).

## Kill switch, rate and size limits

| Control | Value | Where |
|---|---|---|
| Kill switch | `Mcp:Enabled` (environment variable `Mcp__Enabled`), default `false`. Off: the route stays mapped and answers `503` `mcp.disabled` before any tool runs. Read once at startup; a change needs a restart. | `McpOptions.cs`, `McpModule.cs`, `McpToolCatalog.cs` (`NullMcpToolCatalog`), `McpEndpoints.cs` (`McpEnabledFilter`) |
| Rate limit | Policy `mcp`: 60 requests per minute per caller, sliding window of 6 segments, no queue. Keyed on the caller (`oid`, else `sub`). | `RateLimitingModule.cs` |
| Request size | 256 KiB per request (`McpEndpoints.MaxRequestBodyBytes`), enforced by Kestrel; over it gives `413`. | `McpEndpoints.cs` |
| Result size | Every tool result under 64 KiB (Cowork's inline cap). The plan row is bounded: at most 100 evidence lines and 20,000 bytes of them, clipped text; a plan too large to leave room is the fixed error `mcp.plan_too_large`. | `PlanAndLookupToolSupport.cs`; test `tests/integration/regression/Mcp/McpResultSizeTests.cs` |
| Widget size | Fail above 3 MB | `scripts/check-bundle-size.mjs` |
| Latency targets | p95 3 s for show, page and plan; 1.5 s for lookup; 15 s for complete; all under the host's 30 s ceiling | measured in `projects/spaarkeai-mcp-bff-api-r1/notes/nfr-measurements.md` |

The kill switch follows ADR-032: the SDK services and the route are registered and mapped in both states, so flipping the flag cannot leave a dependency missing. With the flag on, the catalog is built at startup, which is when the per-tool rule runs.

## Telemetry

- **One line per tool call** (`DecisionToolCall.LogCompleted`): tool name, outcome (`ok` or the error's `reasonCode`, or `exception`), row count, elapsed milliseconds and correlation id (the request's `TraceIdentifier`, the same value REST ProblemDetails carry as `correlationId`). It carries no argument, token, claim or record text (ADR-015). This line is the latency source for the NFR measurements.
- **A10 refusals** log `sdap.auth.deny.mcp_connector_token` with the deny reason only (`McpConnectorTokenFilter`).
- **Not logged** on `/mcp/*`: the bearer token or its claim values, beyond the audit enrichment ADR-028 already requires.
- **Application Insights samples** some request rows (25 to 33 percent seen on dev), so a missing row is not proof a call did not happen.

## Integration Points

| Direction | Subsystem | Interface | Notes |
|---|---|---|---|
| Depends on | Decision slice | `IDecisionCapability`, `IDecisionLookupSearch`, `DecisionResultMapper` | Owned by the ontology project; the REST Signals routes call the same seam |
| Depends on | Auth | `AuthorizationModule` default workforce scheme; ADR-028 A10 | The A10 filter narrows the shared scheme without changing it |
| Depends on | Dataverse | `IDataverseUserClient` (OBO) through the capability | Tools never hold an app-only fallback |
| Consumed by | Microsoft 365 Copilot Chat and Cowork | The connector "Spaarke MCP" → `/mcp/legal-operations` | Registered once per tenant |
| Consumed by | Plugin package `spaarke` | Skill `spaarke-decisions` calls `spaarke_show_decisions` | Skills only; no connector in the package |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Where the server lives | Inside the BFF, one route per area | One backend for every client surface; no second service to deploy or secure | ADR-013, ADR-010 |
| Session mode | Stateless Streamable HTTP | Keeps `HttpContext` populated per call; no affinity or background work | ADR-001, ADR-052 |
| Token rule | Explicit A10 filter, not library defaults | The shared scheme accepts more than A10 allows | ADR-028 A10 |
| Connector sign-in | OAuth 2.0, Spaarke-tenant authority, confidential client | Entra SSO issues home-directory tokens that match no `systemuser`; the Developer Portal needs a secret | ADR-028 A10 |
| 401 challenge | Plain `Bearer` (and `invalid_token` on A10 refusals), no RFC 9728 metadata route | The federated connector works without discovery (Phase 0); a metadata route would be a second anonymous route | ADR-028 |
| Disabled state | Null-object catalog, 503 | A feature kill switch that cannot leave a dependency missing | ADR-032, ADR-018 |
| Errors | `isError` tool result with REST `reasonCode` | The model and widget can read and correct; a transport error shows as "connector failed" | ADR-019 |
| Writes | App-only, server-enforced | The host's confirmation cannot be relied on | ADR-028 A10 |
| Model-visible output | Counts and slim rows; privileged items counted only | Record text must not reach the model | ADR-015 |
| AI-internal types | No tool injects a type from `Services/Ai/**` outside its public contracts; the route guard enforces it | Keeps the AI pipeline behind its facade | ADR-013 |

## Constraints

- **MUST** add a new product area as a new `/mcp/{area}` route on this host. **MUST NOT** build a second MCP server.
- **MUST** pass exactly `IHttpContextAccessor.HttpContext` to the capability and fail closed on a missing or unauthenticated caller. **MUST NOT** fall back to an app-only client.
- **MUST** keep the route stateless. **MUST NOT** add session state, background work or an SSE stream.
- **MUST** put `[Authorize]` (an allowed policy), an explicit `ReadOnly` and a title on every tool. **MUST** make any non-read-only tool app-only.
- **MUST NOT** register tools, prompts or resources by any route other than the catalog and the sanctioned widget collection in `McpModule`.
- **MUST NOT** log a token, claim value, argument or record text on `/mcp/*`.
- **MUST** keep every tool result under 64 KiB and every request under 256 KiB.
- **MUST** register the server once per tenant. **MUST NOT** also add an `agentConnectors` entry for the same URL in a plugin package.
- **MUST NOT** use the connector's Entra SSO option.

## Related

- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md), Amendment A10: the connector auth plane.
- [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md): placement and the AI facade.
- [`.claude/adr/ADR-015-ai-data-governance.md`](../../.claude/adr/ADR-015-ai-data-governance.md): no record text in logs or messages.
- [`.claude/adr/ADR-032-bff-nullobject-kill-switch.md`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md): the kill switch.
- [`.claude/adr/ADR-016-ai-rate-limits.md`](../../.claude/adr/ADR-016-ai-rate-limits.md) and [`.claude/adr/ADR-019-problemdetails.md`](../../.claude/adr/ADR-019-problemdetails.md): rate limits and error shape.
- [`HOW-TO-ADD-AN-MCP-TOOL.md`](../guides/HOW-TO-ADD-AN-MCP-TOOL.md): add a tool or an area.
- [`SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md`](../guides/SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md): register, verify, rotate, roll back.
- [`.claude/skills/mcp-tool-handler/SKILL.md`](../../.claude/skills/mcp-tool-handler/SKILL.md): the agent-facing rules.
- Project record: `projects/spaarkeai-mcp-bff-api-r1/` (`spec.md`, `notes/decisions.md`, `notes/phase0-results.md`).
