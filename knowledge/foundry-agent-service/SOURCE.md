# SOURCE — foundry-agent-service

> **Curated**: 2026-05-14
> **Curator**: Claude Code (sub-agent, knowledge-base-setup-r1)
> **Refresh cadence**: monthly (first business day)

## Source repositories

| Repo | Commit SHA (2026-05-14) | Notes |
|---|---|---|
| [`Azure-Samples/azureai-samples`](https://github.com/Azure-Samples/azureai-samples) | `b6c2a8ff97fd0944df048ba06d14532aad38506c` | Broad Azure AI samples; primary source for Foundry-relevant subdirectories. |
| [`Azure-Samples/ai-foundry-agents-samples`](https://github.com/Azure-Samples/ai-foundry-agents-samples) | `3bf268ec1d9e2cd847da31e4022e3b6e9a8df606` | **Replacement for the directive's `Azure-Samples/azure-ai-foundry`** (404 on 2026-05-14). Foundry Agent Service-specific MCP and mem0 examples. |
| [`Azure/ai-foundry-isv-mcp-agent`](https://github.com/Azure/ai-foundry-isv-mcp-agent) | `2679db006942e6de7d1599169355aa6836070fbb` | ISV-pattern Foundry agent with MCP integration + YAML-configured approval modes (closest current sample to a HITL primitive). |

## Repos referenced but not curated from

| Repo | Status | Reason |
|---|---|---|
| `Azure-Samples/openai-end-to-end-baseline` | Renamed → `Azure-Samples/microsoft-foundry-baseline` (commit not captured — not cloned) | Per directive, reference architecture only; cite, don't copy whole. |
| `Azure-Samples/azure-ai-foundry` (directive) | **404 on 2026-05-14** | Replaced with `Azure-Samples/ai-foundry-agents-samples` (current canonical Foundry samples repo). |
| `Azure/aifoundry-apps` | Cloned, inspected | App-development meta-sample; no workflow / A2A patterns relevant to this topic. |

## Curated files

```
foundry-agent-service/
├── SOURCE.md                                                   (this file)
├── NOTES.md                                                    (stub — pending senior engineer annotation)
├── docs/
│   ├── overview.md                                             snapshot — Foundry Agent Service overview
│   ├── workflows.md                                            snapshot — workflow runtime (UI-first builder)
│   ├── hosted-agents.md                                        snapshot — Hosted Agents (durable orchestration model)
│   └── GAP-memory.md                                           gap report — memory tool doc not published
└── samples/
    ├── mcp-tool-binding/
    │   ├── README.md                                           (from upstream)
    │   ├── sample_agents_mcp.py                                MCP server tool binding via McpTool + run lifecycle
    │   └── .env_sample
    ├── mcp-approval-gate/
    │   ├── agent.py                                            ISV-pattern agent with YAML-driven approval modes
    │   ├── agent_config_template.yaml                          Approval_Mode: always | never | prompt (HITL primitive)
    │   └── ai_foundry_template.env
    └── semantic-kernel-mcp/
        ├── README.md                                           (from upstream)
        ├── agent_with_sse_server.py                            SSE-based MCP server attached as Semantic Kernel plugin
        └── agent_with_stdio_server.py                          stdio-based MCP server attached as Semantic Kernel plugin
```

## File-by-file provenance

### `docs/overview.md`
- **Source**: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/overview (redirects to canonical `/azure/foundry/agents/overview`)
- **Demonstrates**: Foundry Agent Service product surface — three agent types (Prompt, Workflow, Hosted), tools surface, MCP server connections, enterprise capabilities, A2A protocol (preview) for agent-to-agent.

### `docs/workflows.md`
- **Source**: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/workflows (404) → canonical `/azure/foundry/agents/concepts/workflow`
- **Demonstrates**: Workflow runtime is UI-first (Foundry portal visual builder). YAML export edited in VS Code. Node types: Agent, Logic (if/else, go to, for each), Data transformation, Basic chat. HITL pattern as a built-in template. Power Fx for expressions. Hosted agents are NOT supported in the workflow designer — for code-based orchestration, use Microsoft Agent Framework workflows.

### `docs/hosted-agents.md`
- **Source**: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/durable-orchestration (404) → canonical `/azure/foundry/agents/concepts/hosted-agents` (the durable-orchestration concept page no longer exists separately; durable execution is now part of the Hosted Agents preview surface)
- **Demonstrates**: Per-session VM-isolated sandboxes with `$HOME` and `/files` persistence. 4 protocols (Responses, Invocations, Activity, A2A). Session lifecycle: active → idle (15 min) → resumed with state restored. Per-agent Entra identity.

### `docs/GAP-memory.md`
- **Source URL attempted**: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/memory (404). Two alternate paths also 404 on 2026-05-14.
- **Demonstrates**: Honest gap report. Memory is listed as a preview built-in tool in the overview page, but no dedicated concept doc is published yet.

### `samples/mcp-tool-binding/sample_agents_mcp.py`
- **Source**: `Azure-Samples/ai-foundry-agents-samples` at `3bf268ec1d`, path `examples/mcp/streamable-http/ai-foundry-agent/sample_agents_mcp.py`
- **Demonstrates**: Foundry Agent Service MCP server tool binding using `azure.ai.agents.models.McpTool` with explicit `server_label` + `server_url`. Full run lifecycle including `SubmitToolApprovalAction` / `RequiredMcpToolCall` / `ToolApproval` types and the `mcp_tool.set_approval_mode("never")` toggle. Run polling loop showing `requires_action` → tool approval handling.

### `samples/mcp-approval-gate/agent.py` + `agent_config_template.yaml`
- **Source**: `Azure/ai-foundry-isv-mcp-agent` at `2679db0069`, paths `ai_foundry_agent/agent.py` and `ai_foundry_agent/agent_config_template.yaml`
- **Demonstrates**: ISV-pattern Foundry agent wrapper with **YAML-driven approval modes** — `Approval_Mode: always | never | prompt`. This is the closest current sample to a HITL primitive in Foundry Agent Service. The template includes two real ISV MCP integrations (Snowflake Cortex, MongoDB Atlas) showing per-server allowed-tools scoping and per-server auth tokens. The agent code uses the same `McpTool` / `SubmitToolApprovalAction` / `ToolApproval` flow as the canonical sample.

### `samples/semantic-kernel-mcp/`
- **Source**: `Azure-Samples/azureai-samples` at `b6c2a8ff97`, path `scenarios/Agents/samples/semantic-kernel-mcp/`
- **Demonstrates**: Alternative pattern — attaching an MCP server as a **Semantic Kernel plugin** to an `AzureAIAgent` rather than as a native `McpTool`. Two flavors: SSE-based (`MCPSsePlugin`) and stdio-based (`MCPStdioPlugin`). Useful when the team is already invested in Semantic Kernel agent abstractions and wants MCP tools to participate in SK's plugin discovery.

## Gaps

| Pattern (per directive) | Status | Notes |
|---|---|---|
| Graph-based workflow definition with multiple nodes | **GAP** | Foundry workflow runtime is UI-first; YAML export exists but no code-readable "DSL" sample is published. The closest substitute is the workflow doc snapshot in [`docs/workflows.md`](./docs/workflows.md). For Python-driven graph workflows, the redirect points to **Microsoft Agent Framework workflows** — covered in the sibling `knowledge/agent-framework/` topic, not here. |
| `wait_for_external_event` (HITL gate) | **PARTIAL / GAP** | No `wait_for_external_event` primitive exists in publicly-shipped Foundry SDK samples on 2026-05-14. The closest current primitive is **`mcp_tool.set_approval_mode("prompt")`** + the `SubmitToolApprovalAction` run state — demonstrated in `samples/mcp-tool-binding/` and `samples/mcp-approval-gate/`. The Foundry portal workflow builder has a "Human in the loop" template ([`docs/workflows.md`](./docs/workflows.md)) but emits YAML, not Python. Senior engineer should validate whether `wait_for_external_event` exists in a preview SDK we haven't surfaced. |
| A2A protocol composition | **GAP** | A2A is listed as preview in [`docs/overview.md`](./docs/overview.md) and an A2A endpoint URL pattern is documented in [`docs/hosted-agents.md`](./docs/hosted-agents.md), but **no public Python or .NET sample** demonstrating A2A composition was found in `azureai-samples`, `ai-foundry-agents-samples`, or `ai-foundry-isv-mcp-agent` on 2026-05-14. Watch for `Azure-Samples/ai-foundry-agents-samples/examples/a2a/` at next refresh. |
| MCP server tool binding | **COVERED** | Three samples: `mcp-tool-binding/` (canonical), `mcp-approval-gate/` (ISV pattern with config-driven approvals), `semantic-kernel-mcp/` (alternative via SK plugins). |

## Refresh notes

- **Dead URL fixes**: 3 of 4 directive-listed Microsoft Learn URLs returned 404 on 2026-05-14 due to the `ai-foundry` → `foundry` URL rebrand. Canonical paths are now under `/azure/foundry/agents/`. The fetcher captured the redirects and recorded canonical URLs in each `docs/*.md` frontmatter.
- **Dead repo fix**: `Azure-Samples/azure-ai-foundry` (directive) → use `Azure-Samples/ai-foundry-agents-samples` instead.
- **Next refresh focus**: Foundry workflow YAML samples (if Microsoft publishes a Python equivalent), A2A composition samples, memory tool concept doc.
