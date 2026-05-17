# SOURCE — `agent-framework`

> Provenance for everything in this folder. Do not modify curated samples without updating this file.

## Source repository

| Field | Value |
| --- | --- |
| Repo | [`microsoft/agent-framework`](https://github.com/microsoft/agent-framework) |
| Clone URL | `https://github.com/microsoft/agent-framework` |
| Branch | `main` |
| Commit SHA | `3256550c5503daef28510d9cbf3f6662f5c1c86c` |
| Commit date | 2026-05-14 17:10:27 +0000 |
| Commit subject | `.NET: fix: allow naming handoff workflows (#5799)` |
| Pulled | 2026-05-14 |
| Method | `git clone --depth 1` |
| Description | A framework for building, orchestrating and deploying AI agents and multi-agent workflows with support for Python and .NET. |

The repo contains both `dotnet/` and `python/` sample trees. Spaarke's BFF is .NET 8, so we curate from `dotnet/samples/` only.

## Curated samples

All paths are relative to this folder. Each is copied verbatim from the upstream tree at the SHA above, preserving the upstream directory layout under `dotnet/samples/`.

| Path | Upstream path | What it demonstrates |
| --- | --- | --- |
| `dotnet/samples/01-get-started/02_add_tools/` | `dotnet/samples/01-get-started/02_add_tools/` | Simple single-agent loop with tool calling. `ChatClientAgent` over Azure OpenAI, one `[Description]`-attributed function tool wired via `AIFunctionFactory.Create`, demonstrates both non-streaming `RunAsync` and streaming `RunStreamingAsync`. |
| `dotnet/samples/02-agents/Agents/Agent_Step05_Observability/` | `dotnet/samples/02-agents/Agents/Agent_Step05_Observability/` | OpenTelemetry tracing wired up. Builds a `TracerProvider` with console + (optional) Azure Monitor exporters, then composes `AIAgent` with `.AsBuilder().UseOpenTelemetry(sourceName: ...).Build()` to emit spans. |
| `dotnet/samples/03-workflows/_StartHere/01_Streaming/` | `dotnet/samples/03-workflows/_StartHere/01_Streaming/` | Workflow with streaming responses. Two custom `Executor<string, string>` nodes chained by an edge, executed via `InProcessExecution.RunStreamingAsync` + `WatchStreamAsync`, observing `ExecutorCompletedEvent`/`WorkflowErrorEvent`/`ExecutorFailedEvent`. |
| `dotnet/samples/03-workflows/Orchestration/Handoff/` | `dotnet/samples/03-workflows/Orchestration/Handoff/` | Multi-agent workflow with handoffs. `AgentRegistry.cs` defines an intake agent + four expert agents; `Program.cs` builds a handoff workflow via `AgentWorkflowBuilder.CreateHandoffBuilderWith(...).WithHandoffs(...).EnableReturnToPrevious().Build()` and runs it as an interactive streaming console loop. |

Each sample folder also contains its original `.csproj` file (preserved for accurate reference; not built locally).

## Reference docs snapshot

`docs/` contains markdown snapshots of the Microsoft Learn reference pages listed in the directive. Each has a YAML frontmatter block with the source URL and the date fetched.

| Path | Source URL | Notes |
| --- | --- | --- |
| `docs/overview.md` | `https://learn.microsoft.com/en-us/agent-framework/overview` | Framework overview, agents vs workflows decision table. |
| `docs/agents.md` | `https://learn.microsoft.com/en-us/agent-framework/agents/` | Agent types, providers, SDK options. |
| `docs/workflows.md` | `https://learn.microsoft.com/en-us/agent-framework/workflows/` | Workflow concepts, functional vs graph API, core concepts. |

## GAPs

- **404**: `https://learn.microsoft.com/en-us/agent-framework/concepts/agents` (URL listed in directive). Substituted with the canonical concept page at `https://learn.microsoft.com/en-us/agent-framework/agents/` per the link target on the overview page. Recorded in `docs/agents.md` frontmatter.
- **404**: `https://learn.microsoft.com/en-us/agent-framework/concepts/workflows` (URL listed in directive). Substituted with `https://learn.microsoft.com/en-us/agent-framework/workflows/`. Recorded in `docs/workflows.md` frontmatter.

## Why this curation, briefly

The directive specified four patterns ("single-agent loop with tool calling", "multi-agent workflow with handoffs", "streaming responses", "OpenTelemetry tracing wired up"). Each curated sample maps 1:1 to one of those patterns, was selected for being the smallest self-contained `.NET` example in the upstream tree that demonstrates the pattern, and lives in the upstream `samples/` directory tree under the numbered category that groups it (01-get-started, 02-agents, 03-workflows). No whole-folder dumps; total curated source is well under the 300 KB budget.
