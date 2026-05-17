---
description: Implement or modify an MCP tool handler for the Spaarke MCP server
tags: [mcp, ai, tool-handler, api]
techStack: [aspnet-core, csharp, mcp]
appliesTo: ["**/Services/Ai/**/*.cs", "**/IAiToolHandler*.cs", "**/AiToolService*.cs", "**/McpTool*.cs"]
alwaysApply: false
---

# mcp-tool-handler

> **Category**: Development
> **Last Updated**: 2026-05-14

---

## Purpose

Anchor implementation work on Spaarke's MCP tool surface in current Microsoft platform idioms. The Spaarke BFF exposes tools to AI agents (Foundry Agent Service, Microsoft Agent Framework, declarative agents) via an MCP server. Tool handlers wire imperative document operations and Dataverse-aware skills into that surface. Without this skill, generated tool handlers drift toward training-data patterns that predate MCP Apps, approval modes, and the current tool-binding contract.

---

## Applies When

- Creating a new MCP tool handler (implementing `IAiToolHandler` or adding to `AiToolService` registration)
- Modifying tool definitions, tool schemas, or tool approval semantics
- Wiring Spaarke MCP tools into a Foundry agent, declarative agent, or Agent Framework loop
- Designing a new agent-callable verb the team wants to expose via MCP
- **NOT applicable** for: built-in Dataverse MCP tools (use `dataverse-mcp-usage` skill), widget UI for tool results (use `widget-design` skill)

---

## Workflow

### Step 1: Load knowledge context (mandatory before generating code)

Read in this order:

1. **`knowledge/mcp-apps/NOTES.md`** — Spaarke-specific commentary on widget/tool composition (may be a stub pre-Phase-4; still read it for the heading scaffold).
2. **`knowledge/foundry-agent-service/NOTES.md`** — When tools attach to Foundry agents, how approval-mode wiring works (`McpTool.set_approval_mode("prompt")`, `SubmitToolApprovalAction`).
3. **`knowledge/foundry-agent-service/samples/mcp-tool-binding/`** — The canonical `McpTool` + approval flow pattern.
4. **`knowledge/foundry-agent-service/samples/mcp-approval-gate/`** — Config-driven approval modes (closest current HITL primitive).
5. **`knowledge/mcp-apps/trey-research/` and `knowledge/mcp-apps/approvals-box/`** — If the tool returns rich data that may render in a widget, study the host-bridge hook (`useMcpApp`) for the contract.

### Step 2: Identify the integration target

Determine which agent surface will call the tool:

| Calling surface | Knowledge to consult |
|---|---|
| Foundry agent (durable, HITL) | `knowledge/foundry-agent-service/` |
| Microsoft Agent Framework (in-process BFF loop) | `knowledge/agent-framework/` |
| Declarative agent in Copilot Chat | `knowledge/declarative-agents/` + `knowledge/m365-copilot/` |
| Direct widget invocation (MCP App) | `knowledge/mcp-apps/` |

### Step 3: Apply Spaarke contracts (ADR-013, ADR-007)

- **ADR-013 (AI Architecture)**: Use the existing `IAiToolHandler` pattern in `src/server/api/Sprk.Bff.Api/Services/Ai/`. The `AiToolService` is the orchestrator — register handlers in DI per ADR-010 (≤15 non-framework registrations; check the budget before adding).
- **ADR-007 (SpeFileStore facade)**: If the tool reads/writes SharePoint Embedded content, route through `SpeFileStore`. Do NOT inject `GraphServiceClient` directly into the handler.
- **ADR-001 (Minimal API)**: No Azure Functions; tool handlers are .NET 8 services hosted in `Sprk.Bff.Api`.
- **ADR-008 (Endpoint filters)**: Authorization for tool invocation goes through endpoint filters on `AiToolEndpoints`, not global middleware.

### Step 4: Tool schema and approval mode

- Define the tool schema with the type-safe attribute-based idioms shown in `knowledge/agent-framework/dotnet/samples/01-get-started/02_add_tools/`.
- Decide approval mode explicitly: `auto`, `prompt`, or `never`. Document the choice in the handler XML doc comment.
- For destructive operations (delete, mass-update): default to `prompt` (manual confirmation) until a stable use case justifies `auto`.

### Step 5: Streaming and telemetry

- If the tool result is large or progressive, stream — see `knowledge/agent-framework/dotnet/samples/03-workflows/_StartHere/01_Streaming/`.
- Emit OpenTelemetry traces — pattern in `knowledge/agent-framework/dotnet/samples/02-agents/Agents/Agent_Step05_Observability/`. Spaarke routes OTel to Application Insights (the customer's instance).

### Step 6: Code review checklist (before declaring done)

- [ ] Tool registered via DI; does not exceed ADR-010 budget
- [ ] No Graph SDK types in the handler signature (use `SpeFileStore` facade)
- [ ] Approval mode chosen and documented
- [ ] Tool schema is type-safe (no string-bag inputs unless schema-validated)
- [ ] Endpoint filter authorization applied at `AiToolEndpoints` level
- [ ] OTel tracing wired (at minimum: handler invocation, downstream call)
- [ ] Unit test covers the happy path and an authorization failure

---

## Conventions

- Tool handlers live under `src/server/api/Sprk.Bff.Api/Services/Ai/`
- File naming: `{ToolName}ToolHandler.cs`; class name matches
- Tool ID (the string clients call): `kebab-case` (e.g., `summarize-document`, `extract-entities`)
- Do NOT extend the BFF with a separate AI microservice — per ADR-013, extend in-process

## Resources

| Resource | Purpose |
|----------|---------|
| `knowledge/mcp-apps/NOTES.md` | Spaarke perspective on widget+tool composition |
| `knowledge/foundry-agent-service/NOTES.md` | When/how to bind tools to Foundry agents |
| `knowledge/foundry-agent-service/samples/mcp-tool-binding/` | Canonical `McpTool` + approval flow |
| `knowledge/mcp-apps/trey-research/src/mcpserver/widgets/src/hooks/useMcpApp.tsx` | Host-bridge contract if tool result renders in widget |

## Output

When this skill completes, expect:
- A new `{ToolName}ToolHandler.cs` (or modifications to existing) following ADR-013
- A DI registration in `Program.cs` or the AI module's extension method
- Unit tests for the handler
- A reference in `NOTES.md` of the relevant knowledge folder if the work establishes a new Spaarke-specific pattern (note for senior engineer to incorporate at next annotation pass)
