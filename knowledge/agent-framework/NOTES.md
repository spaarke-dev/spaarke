> ⚠️ STUB — senior engineer review pending

# NOTES — `agent-framework`

Stub headings only. Substance comes from the Phase 4 senior engineer annotation pass. Do not fabricate guidance in this file; replace each `TODO` with project-specific commentary backed by reading the curated samples and tracing them against Spaarke code (`src/server/api/Sprk.Bff.Api/Services/Ai/`, ADR-013).

## Agent Framework's place in the Spaarke architecture

_TODO: Where Agent Framework fits in the BFF — server-side single-agent loops for event-driven or scheduled agent work, contrasted with the AI Tool Framework (`AiToolService` / `IAiToolHandler`) that fronts the streaming endpoints. Reference `AnalysisOrchestrationService.cs` and ADR-013._

## When to use Agent Framework vs. Foundry Agent Service

_TODO: In-process / ephemeral agent loops vs. durable, multi-day, HITL workflows. Decision rule keyed to: lifetime, durability, HITL gates, A2A composition needs._

## Tool definition idioms

_TODO: `[Description]`-attributed methods + `AIFunctionFactory.Create(...)` for the simple case; the `tools:` parameter on `.AsAIAgent(...)`. Note the type-safe binding and how parameters surface in the LLM tool schema._

## Streaming response handling

_TODO: Agent-level streaming (`RunStreamingAsync` over `AIAgent`) vs. workflow-level streaming (`InProcessExecution.RunStreamingAsync` + `WatchStreamAsync` consuming `WorkflowEvent` subclasses). Map to Spaarke's BFF streaming endpoints._

## OpenTelemetry wiring and Application Insights flow

_TODO: `.AsBuilder().UseOpenTelemetry(sourceName: ...).Build()` integration, source-name conventions, exporter setup (`AddAzureMonitorTraceExporter` from `APPLICATIONINSIGHTS_CONNECTION_STRING`). Note how this lines up with the existing OTel pipeline in `Sprk.Bff.Api` and the customer-tenant Application Insights story._

## Intersection with Spaarke's `IAiToolHandler` (ADR-013)

_TODO: ADR-013 mandates the AI Tool Framework (`IAiToolHandler`, `AiToolService`) as the in-BFF tool-orchestration seam. Agent Framework tool definitions are a parallel surface — when an agent loop is appropriate, do tool implementations get wrapped or duplicated? Resolve the seam clearly so engineers don't end up with two parallel tool registries._

## Multi-agent handoff patterns

_TODO: `AgentWorkflowBuilder.CreateHandoffBuilderWith(...)`, `WithHandoffs`, `EnableReturnToPrevious`. When (if ever) Spaarke needs in-process handoff workflows vs. delegating multi-agent composition to Foundry Agent Service._

## Hosting model notes

_TODO: In-process via `InProcessExecution` for stateless requests; durable hosting via the `04-hosting/` samples (Azure Functions, Durable Tasks) for long-running. ADR-001 prohibits Azure Functions for new work — confirm whether Agent Framework durable hosting is in scope at all, or always defer durability to Foundry Agent Service._

## Gotchas to capture from real implementation

_TODO: Fill from first production use — credential choice in production (`ManagedIdentityCredential` over `DefaultAzureCredential` per the upstream warnings), package versions/prereleases, telemetry cardinality, cost of tool descriptions in prompt._
