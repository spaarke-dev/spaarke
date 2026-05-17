---
description: Design or modify an Azure AI Foundry agent or workflow (durable, HITL, A2A)
tags: [foundry, agent, workflow, hitl, a2a, ai]
techStack: [azure-ai-foundry, csharp, python, mcp]
appliesTo: ["**/Services/Ai/**/*.cs", "**/agents/**/*.py", "**/workflows/**/*.yaml"]
alwaysApply: false
---

# foundry-agent

> **Category**: Development
> **Last Updated**: 2026-05-14

---

## Purpose

Anchor server-side agent work in current Microsoft idioms across **three runtimes**: Microsoft Agent Framework (in-process .NET loops), Azure AI Foundry Agent Service (durable, HITL, A2A), and Foundry IQ (managed knowledge bases for grounding). Choosing the wrong runtime — or hand-rolling orchestration over Agent Framework when Foundry's durable workflow fits — costs weeks. This skill loads the canonical samples so generated code uses the actual platform primitives.

Without this skill, generated code drifts toward training-data patterns that predate `McpTool`, `SubmitToolApprovalAction`, the Foundry IQ knowledge layer split (some pages now live under `/azure/search/`), and the Agent Framework 1.0 .NET sample idioms.

---

## Applies When

- Designing a new agent or workflow that runs server-side (not in Copilot Chat — that's `declarative-agent`)
- Choosing between Microsoft Agent Framework (in-process) and Foundry Agent Service (durable)
- Adding HITL approval gates, A2A protocol composition, or knowledge base wiring
- Creating or modifying a Foundry IQ knowledge base or knowledge source
- Wiring an MCP server (Spaarke MCP, Dataverse MCP) into a server-side agent
- **NOT applicable** for: Copilot Chat declarative agents (use `declarative-agent`), individual tool handler code (use `mcp-tool-handler`), MCP App widgets (use `widget-design`)

---

## Workflow

### Step 1: Load knowledge context (mandatory)

Read in this order:

1. **`knowledge/foundry-agent-service/NOTES.md`** — Spaarke's multi-step legal workflows and Foundry mapping.
2. **`knowledge/agent-framework/NOTES.md`** — When Agent Framework is right (in-process, single-agent loop, fast iteration).
3. **`knowledge/foundry-iq/NOTES.md`** — Knowledge base vs direct AI Search; Spaarke's three-retrieval-surface pattern.
4. Then the relevant samples per the runtime decision (Step 2).

### Step 2: Runtime decision (this is the highest-leverage decision in agent work)

| Use case | Runtime | Why |
|---|---|---|
| Short-lived, in-process, BFF event-driven | **Microsoft Agent Framework** | No durable state needed; iteration speed; OTel native |
| Multi-day, HITL gates, A2A, durable resumption | **Foundry Agent Service** | Built-in durability, approval primitives, A2A protocol |
| Server-side knowledge retrieval with citations | **Foundry IQ KB** + either runtime | Managed indexing, semantic reranking, permission filtering |
| Quick lookup against an existing AI Search index | **Direct AI Search query** (not Foundry IQ) | Lower latency, fewer moving parts |

After deciding, load the matching samples:

| Runtime | Samples to study |
|---|---|
| Agent Framework | `knowledge/agent-framework/dotnet/samples/01-get-started/02_add_tools/` (tool calling), `02-agents/Agents/Agent_Step05_Observability/` (OTel), `03-workflows/Orchestration/Handoff/` (handoffs), `03-workflows/_StartHere/01_Streaming/` (streaming) |
| Foundry Agent Service | `knowledge/foundry-agent-service/samples/mcp-tool-binding/` (canonical MCP binding), `samples/mcp-approval-gate/` (HITL approval), `samples/semantic-kernel-mcp/` (alternative via SK) |
| Foundry IQ | `knowledge/foundry-iq/samples/kb-over-ai-search/` (KB over Search), `kb-over-blob-and-web/` (Blob source), `kb-with-sharepoint-remote/` (SP remote KS), `agent-grounding-wiring/` (wiring into agent config) |

### Step 3: Apply Spaarke contracts (ADR-013, ADR-001, ADR-009)

- **ADR-013 (AI Architecture)**: Extend the BFF in-process — no separate AI microservice. Agent Framework loops live alongside `IAiToolHandler` implementations in `Sprk.Bff.Api/Services/Ai/`.
- **ADR-001 (Minimal API + BackgroundService)**: For long-running but non-durable work (e.g., batch indexing), use `BackgroundService`. For genuinely durable (days/weeks) work with HITL: Foundry Agent Service.
- **ADR-009 (Redis-first caching)**: Cache Foundry IQ retrieval results in Redis when query stability + cost matter. Do not introduce L1 in-memory cache unless profiling proves need.

### Step 4: HITL approval semantics

**Current state (per knowledge base, captured 2026-05-14)**: `wait_for_external_event` HITL primitive is not in public SDK samples. The closest available pattern is:

- `McpTool.set_approval_mode("prompt")` + `SubmitToolApprovalAction` event handling (see `knowledge/foundry-agent-service/samples/mcp-tool-binding/` and `samples/mcp-approval-gate/`)
- For UI-driven approval surfaces, route through MCP App widgets (use `widget-design` skill)

When the workflow needs explicit human approval mid-execution: use approval-mode prompts; do NOT hand-roll a polling loop.

### Step 5: Knowledge base composition (when Foundry IQ applies)

Spaarke's default Foundry IQ pattern:

- **Golden documents** (playbooks, exemplar contracts, legal research): Foundry IQ KB over Azure Blob with curated content
- **Application-code queries**: direct AI Search index (faster, more control)
- **Matter documents**: SPE substrate index via SharePoint knowledge source (no manual indexing)

See `knowledge/foundry-iq/samples/agent-grounding-wiring/` for the agent → KB binding pattern.

### Step 6: Code review checklist (before declaring done)

- [ ] Runtime choice documented in the code or PR with rationale
- [ ] Tool bindings use `McpTool` idiom (or Agent Framework type-safe attribute style for in-process)
- [ ] Approval modes explicit for any state-changing tools
- [ ] OTel traces emitted (Agent Framework has it native; Foundry traces flow to App Insights)
- [ ] If using Foundry IQ: cite the KB ID in code or config, not magic strings
- [ ] No fabricated A2A or `wait_for_external_event` code (these are platform gaps as of 2026-05-14)

---

## Conventions

- Foundry workflows: YAML graph definitions live under `src/server/api/Sprk.Bff.Api/agents/workflows/`
- Agent Framework loops: C# in `src/server/api/Sprk.Bff.Api/Services/Ai/` alongside tool handlers
- Foundry IQ KB IDs: stored in `appsettings.json` (or Key Vault for production secrets) — never inline

## Resources

| Resource | Purpose |
|----------|---------|
| `knowledge/foundry-agent-service/NOTES.md` | Spaarke's legal workflow mapping; runtime choice rationale |
| `knowledge/agent-framework/NOTES.md` | When AF in-process is right; intersection with `IAiToolHandler` |
| `knowledge/foundry-iq/NOTES.md` | KB vs direct AI Search; three-retrieval-surface composition |
| `knowledge/foundry-agent-service/docs/` | Microsoft Learn snapshots (overview, workflows, hosted-agents) |

## Output

When this skill completes, expect:
- A runtime choice documented in a code comment, PR description, or ADR (if novel)
- Agent/workflow code following platform idiom (not hand-rolled orchestration)
- Tool bindings via `McpTool` or Agent Framework attribute style
- Spaarke-specific notes added to `knowledge/foundry-agent-service/NOTES.md` if a new pattern emerges (queue for senior engineer)
