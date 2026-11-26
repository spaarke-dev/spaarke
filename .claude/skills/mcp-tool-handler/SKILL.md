---
description: Implement or modify an MCP tool handler for the Spaarke MCP server
tags: [mcp, ai, tool-handler, api]
techStack: [aspnet-core, csharp, mcp]
appliesTo: ["**/Api/Mcp/**/*.cs", "**/Infrastructure/DI/McpModule.cs", "**/RouteAuthorizationGuardTests.Mcp.cs", "**/RouteAuthorizationGuardTests.Mcp.*.cs", "**/McpTool*.cs"]
alwaysApply: false
---

# mcp-tool-handler

> **Category**: Development
> **Last Updated**: 2026-11-25 (rewritten by spaarkeai-mcp-bff-api-r1 task 051 from the implemented server)

---

## Purpose

Add or change a tool on Spaarke's MCP server, the one hosted in the BFF at `/mcp/{area}`. Microsoft 365 Copilot Chat and Cowork call it through a Copilot connector (v1: "Spaarke MCP" → `/mcp/legal-operations`). The conventions below are what the code does; when this file and the code disagree, the code wins (root CLAUDE.md §2) and this file gets fixed.

Architecture: `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md`. Step-by-step guide with tests: `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md`.

## Applies When

- Adding a tool to an existing area, or a new area (`/mcp/{area}`).
- Changing a tool's schema, description, annotations, visibility, authorization or result shape.
- **Not for:** the widget a tool renders (`widget-design`); the Copilot packages (`declarative-agent`); Dataverse's own MCP server (`dataverse-mcp-usage`). The older `IAiToolHandler` / `AiToolService` chat tools are a different, in-BFF agent surface, not MCP.

## Rules (binding)

| Rule | Where it is enforced |
|---|---|
| **One MCP server.** A new product area is a new `/mcp/{area}` route on this host, never a second server or service (ADR-013 placement). | `Api/Mcp/McpEndpoints.cs` (one `MapMcp` per area, area constant beside `LegalOperationsArea`) |
| **Tool name** `spaarke_{verb}_{object}` (e.g. `spaarke_show_decisions`). Copilot shows it prefixed by the connector (`SpaarkeMCP_`). Not the April camelCase `/api/agent/*` names (Retired 2026-11-25 with that gateway). | naming note `projects/spaarkeai-mcp-bff-api-r1/notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §1 |
| **Shape:** one `McpToolDefinition` subclass per tool, tagged `[McpArea(McpEndpoints.{Area}Area)]`, under `Api/Mcp/{Area}/{Group}/`. `Create(IServiceProvider services)` returns `McpServerTool.Create(<static method group>, new McpServerToolCreateOptions { Services = services, ... })`. The handler is a `static` method taking services as parameters; no state between calls. No reflection (`Type.GetMethods`) and no SDK `WithToolsFromAssembly`. | `Api/Mcp/McpToolCatalog.cs` (discovers by attribute, partitions by area) |
| **Caller identity:** every tool runs as the caller (OBO). Pass exactly `IHttpContextAccessor.HttpContext` to the capability; a null context or unauthenticated caller fails closed, never app-only. The route stays **stateless**: that is what keeps the accessor populated. | `Infrastructure/DI/McpModule.cs`; tests `tests/integration/auth/Mcp/McpCallerPathTests.cs` |
| **Authorization:** `[Authorize]` on the class, naming only the allowed policies (bare `[Authorize]` or `SystemAdmin`). Never `ExternalCollaboration` or any other scheme's policy. The route accepts only ADR-028 **A10** tokens (Spaarke tenant, v2 issuer, scope `user_impersonation`, `aud` = BFF app); CIAM and A9 home-directory tokens are refused before any tool runs. | `Api/Mcp/McpToolAuthorization.cs`; ledger `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs` (`McpTools`) |
| **Metadata:** explicit `Title`, explicit `ReadOnly` hint (true or false, never left out), and a "use for / not for" description of at most 1,024 characters. | Title, `ReadOnly` and "writes are app-only": `McpToolAuthorization` startup validation (a breaking tool stops the app from starting). Description length: the contract tests (`McpHostContractTests.cs`) |
| **Visibility:** a tool only the widget calls is **app-only** (`_meta.ui.visibility = ["app"]`). Any tool that writes (`ReadOnly = false`) must be app-only: the model never commits. Model-visible tools point at their widget with `_meta.ui.resourceUri`. | e.g. `CompleteDecisionTool.cs` (app-only write), `ShowDecisionsTool.cs` (model + app) |
| **Input schema:** stated explicitly, not inferred; closed enums where the value set is closed; `additionalProperties: false`. | `ShowDecisionsTool.cs` (`category` enum from `DecideLaneCategories.Keys`) |
| **Domain logic stays in services.** A tool is a thin adapter over the same service the REST route uses (decisions: `Services/Signals/IDecisionCapability.cs`, `IDecisionLookupSearch.cs`). Never a second validation, billing refusal or write path; writes go through `DecisionCommitService` only. | spec FR-08, FR-09 |
| **Errors:** domain failures come back as a tool result with `isError: true`, mapped by `Api/Signals/DecisionResultMapper.cs` → `Api/Mcp/McpToolResults.cs`, with the same `reasonCode` vocabulary as REST. Transport errors never carry domain failures. | `tests/unit/domain/Mcp/McpToolResultsTests.cs` |
| **Model-visible output is minimal (FR-04 style).** `content`/`structuredContent` of a model-visible tool carries counts and slim rows only; privileged Work Items appear only as "N privileged items — open to review". Detail goes through app-only tools to the widget. | `tests/integration/auth/Mcp/DecisionsPrivilegeReductionTests.cs` |
| **Limits:** every result < 64 KiB at the worst-case seed; finish under 30 s; no background work (ADR-052). Per-user rate limit policy `mcp` (`RateLimitingModule.McpPolicyName`). Kill switch `Mcp:Enabled=false` → 503 `mcp.disabled` via the null-object catalog (ADR-032). | `tests/integration/regression/Mcp/McpResultSizeTests.cs`; `Infrastructure/DI/RateLimitingModule.cs` |
| **Logging:** log `oid`, `appid`, correlation id and outcome; never the token or its claims (ADR-028 A10, NFR-05). Each call logs `MCP tool {tool} completed: outcome …, {n} rows, {ms} ms` (the NFR-01 latency source). | `McpToolAuthorization.cs`, the tool classes |

## Workflow

1. **Placement.** Confirm the tool belongs in an existing area; a new area follows `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md` "Add an area". Answer root CLAUDE.md §11's three questions for the new surface.
2. **Service first.** Find or extend the service the REST route uses. If none exists, the logic goes in a service, not the tool.
3. **Write the tool class** by copying the nearest existing one: read-only and model-visible → `ShowDecisionsTool.cs`; app-only read → `ListDecisionPageTool.cs` / `GetDecisionPlanTool.cs` / `SearchDecisionLookupTool.cs`; app-only write → `CompleteDecisionTool.cs`. Shared argument parsing: `DecisionToolCall.cs`. Errors: `McpToolResults.FromError(new McpToolError(reasonCode, message, status, partial?), http?.TraceIdentifier)` with the REST route's `reasonCode`. Work that can take over 30 s (`McpLongRunning.Budget`): return `McpLongRunning.Started(...)` and add a status tool that returns `McpLongRunning.Status(...)` (FR-14).
4. **Add the ledger entry** in your area's partial, `RouteAuthorizationGuardTests.Mcp.{Area}.cs` (`McpTools{Area}`: policy, caller seam, reason), never in `RouteAuthorizationGuardTests.Mcp.cs` (it concatenates the partials). The ArchTest fails on any tagged tool missing from every partial or listed twice.
5. **Tests** (all required):
   - auth: `tests/integration/auth/Mcp/` (unauthenticated → 401; refused token types; caller path);
   - contract: `tests/integration/contract/Api/Mcp/` (`tools/list` entry: name, title, hints, visibility, schema; happy path; `isError` mapping);
   - size: add the tool to `tests/integration/regression/Mcp/McpResultSizeTests.cs` / `McpFiveToolFixtures.cs`;
   - writes: `tests/integration/data-mutation/Mcp/` (idempotency key, replay, nothing written on refusal);
   - conformance: the CI job `mcp-conformance` in `.github/workflows/ci-tier1-blocking.yml` (host `tests/integration/Mcp.Conformance.Host/`) must stay green.
6. **BFF hygiene** (root CLAUDE.md §10): publish-size delta against fresh master, no new HIGH CVE.
7. **Review:** auth-touching changes go to the uac-r2 reviewer; changes under `Services/Signals/**` or `Api/Signals/**` go to the ontology project.

## Conventions

- Files: `Api/Mcp/{Area}/{Group}/{Verb}{Object}Tool.cs`; class name matches the file. Widget resources: `Api/Mcp/{Area}/{Widget}WidgetResource.cs`.
- Route constants: `McpEndpoints.{Area}Route` = `/mcp/{area}`, `McpEndpoints.{Area}Area` = `{area}` (lower-case, hyphenated; never `core`).
- One connector per product area, not per function (ontology-component-model.md §1.1).

## Resources

| Resource | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Mcp/` | The host, catalog, authorization, result helpers and the five legal-operations tools |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/McpModule.cs` | DI, stateless transport, kill switch |
| `projects/spaarkeai-mcp-bff-api-r1/spec.md` | FR-01–FR-12, NFR-01–NFR-09 (the requirements these rules come from) |
| `.claude/adr/ADR-028-spaarke-auth-architecture.md` (A10) | The connector auth plane |
