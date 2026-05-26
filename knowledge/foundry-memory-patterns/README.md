# Foundry memory + agent patterns (reference) (2026-05)

> **Status**: Researched 2026-05-19 for `projects/ai-spaarke-insights-engine-r1/`.
> **Curator**: researcher subagent (auto-research, not yet senior-engineer-reviewed).
> **Refresh cadence**: monthly per [`knowledge/REFRESH-PROCEDURE.md`](../REFRESH-PROCEDURE.md).
> **Relationship to existing knowledge**: This document is the *reference* for Foundry concepts as they apply to the Insights Engine's custom-agent design. See also [`knowledge/foundry-agent-service/`](../foundry-agent-service/) for the broader Foundry Agent Service curation and [`docs/GAP-memory.md`](../foundry-agent-service/docs/GAP-memory.md) — which is **superseded by this document** (Microsoft published the memory docs after that gap was logged).

The Insights Engine builds a custom Insights Agent in the BFF, *not* on Foundry Agent Service. This document captures what Foundry memory and Foundry agent patterns *do* in 2026 so the Insights Engine team can borrow patterns deliberately and decline complexity deliberately.

---

## Status as of 2026-05

Foundry agent memory (the managed long-term memory primitive) **moved from undocumented-preview to documented-public-preview in 2026-04**, with concept and how-to pages published on Microsoft Learn (2026-04-06 and 2026-04-10). Memory is implemented as a managed Cosmos DB store partitioned by `scope` (typically `{tenantId}_{userId}`), holds two memory types (user profile and chat summary), and is accessed via the `memory_search` tool attached to a prompt agent or via low-level Memory Store APIs. Foundry Agent Service itself supports three agent types (Prompt — GA; Workflow — preview; Hosted — preview), with multi-agent composition via workflows (UI-first, YAML-exportable) and A2A protocol on Hosted agents. The clear pattern: Foundry-hosted is the right choice when you need managed identity, scaling, tracing, durable workflows, and A2A composition — but it's the wrong choice when you need tight integration with your own BFF's data plane and ADR-governed retrieval logic. The Insights Engine sits squarely in the latter case.

## Key URLs consulted (2026-05-19)

- [What is Memory? - Microsoft Foundry](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-memory) — concept; last updated 2026-05-19
- [Create and Use Memory - Microsoft Foundry](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/memory-usage) — how-to; last updated 2026-05-18 (Python/C#/TypeScript/REST examples)
- [What is Microsoft Foundry Agent Service?](https://learn.microsoft.com/en-us/azure/foundry/agents/overview) — overview (already snapshotted in `knowledge/foundry-agent-service/docs/overview.md`)
- [Microsoft Foundry blog: User-Scoped Persistent Memory](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/microsoft-foundry-unlock-adaptive-personalized-agents-with-user-scoped-persisten/4505622) — announcement post
- [Hosted agents (preview)](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents) — Hosted agent model (cross-reference)
- Existing curation: [`knowledge/foundry-agent-service/`](../foundry-agent-service/) — broader Foundry surface

## Findings

### 1. Foundry's long-term memory primitive

**What it is**: A managed, long-term memory store layered on Cosmos DB, partitioned by a `scope` parameter that the developer controls. The memory store automates *extraction*, *consolidation*, and *retrieval*:

- **Extraction**: After each agent turn (with a configurable `update_delay` — default 5 minutes of inactivity), the system uses an LLM to extract durable signals (user preferences, recurring intent, summarized outcomes) from the conversation. Raw transcript is *not* what's stored; the extracted signals are.
- **Consolidation**: Duplicate or overlapping memories are merged. Conflicting facts (e.g., "I'm allergic to dairy" later contradicted by "I had cheese yesterday") are resolved by the consolidation logic.
- **Retrieval**: Two retrieval modes — *static* (user profile memories returned at the start of a conversation, regardless of query) and *contextual* (chat summary + relevant user-profile memories ranked by similarity to the current user message).

**What it stores**:

| Type | Description | Config flag |
|---|---|---|
| `user_profile_details` | "Static" facts about the user — preferences, dietary restrictions, language. Retrieved at conversation start. | `user_profile_enabled: true` + `user_profile_details: "..."` prompt to guide extraction |
| `chat_summary` | Distilled summary of each topic/thread covered in a session. Lets the user resume a prior conversation. | `chat_summary_enabled: true` |

**Scoping**: Each memory store has independent *scopes* (logical partitions). Set `scope` to a stable string per partition. Recommended patterns:
- `{{$userId}}` — system auto-resolves to `tenantId_objectId` from the caller's Entra token or the `x-memory-user-id` header.
- A custom string (UUID, team identifier) for per-team or per-project memory.

**Retention model**: Memories are durable until explicitly deleted (`delete_scope` for one user's memories, `delete` for the entire store). No automatic TTL exists in the GA-track preview as of 2026-05. **This means GDPR / data-subject-access workflows must explicitly call `delete_scope`** — not relying on automatic expiry.

**Quotas** (per current preview):
- 100 scopes per memory store
- 10,000 memories per scope
- 1,000 req/min search throughput
- 1,000 req/min update throughput

**Cost**: Memory is in public preview; you pay for the underlying chat + embedding model token usage (extraction and retrieval). The memory store API itself is currently free during preview.

### 2. Foundry agent tool calling pattern

Tools attach to a Prompt agent definition. The shapes:

```python
PromptAgentDefinition(
    model="gpt-5.2",
    instructions="...",
    tools=[
        MemorySearchPreviewTool(memory_store_name=..., scope="{{$userId}}", update_delay=300),
        # Other built-in tools: web_search, file_search, code_interpreter, mcp_server, ...
        # Custom tools via MCP server URLs
    ]
)
```

Key observations:

- **Tool descriptions are part of the prompt.** The model decides which tool to call based on the tool's name + description. Vague descriptions yield wrong tool choice. Write descriptions like you're writing API docs.
- **MCP server tools are first-class.** A Foundry agent can call an MCP server hosted on Azure Functions, on a remote endpoint, or via Foundry's hosted MCP catalog. Auth modes: key, Microsoft Entra (managed identity or OBO), unauthenticated.
- **Tool approval gates** (`mcp_tool.set_approval_mode("prompt")`) put the run into a `requires_action` state until the caller submits approval via `SubmitToolApprovalAction`. This is the closest current primitive to durable HITL.
- **Memory search is itself a tool.** When the model wants to recall, it calls `memory_search`. The infrastructure injects retrieved memories as tool output for the model to use.

This pattern is *not* unique to Foundry — it maps cleanly to:
- **Microsoft Agent Framework** in the BFF (`AIFunctionFactory.Create(...)` with `[Description]`-attributed methods).
- **OpenAI Assistants API** function calling.
- **MCP** in any host (Claude Desktop, GitHub Copilot CLI, VS Code).

So the *pattern is portable*. What's not portable from Foundry is the *managed infrastructure* — the agent runtime, scaling, identity, and the memory store. That's the trade-off.

### 3. HITL, durable, A2A — what we're NOT building

These three are first-class in Foundry Agent Service. Spaarke's custom Insights Agent will **not** implement them in r1 — they're not requirements for Memory + Inference over derived legal knowledge. Documenting them here helps the team push back if scope creeps in this direction.

- **HITL via Workflow agents (preview)**: Workflows include a "Human in the loop" template that pauses execution awaiting user input. UI-first builder, YAML-exportable. Insights Engine *queries* derived knowledge; it doesn't *coordinate* multi-day human-approval flows. Skip.
- **Durable, multi-day execution via Hosted agents (preview)**: Hosted agents are containerized code-based agents with session resume across days/weeks. Required for matter-diligence-style workflows that span many human interactions. Insights Engine is request/response; queries return in seconds. Skip.
- **A2A (Agent-to-Agent) protocol on Hosted agents (preview)**: First-class protocol for cross-agent composition; endpoint pattern `{project_endpoint}/agents/{name}/endpoint/protocols/a2a`. Insights Engine doesn't compose with external agents — it serves grounded answers to UI surfaces. Skip in r1; revisit if downstream surfaces want to call the Insights Agent as a peer.

### 4. When Foundry-hosted is the right choice vs. custom agent in BFF

Decision rubric for Spaarke:

| Capability needed | Foundry-hosted | Custom BFF agent |
|---|---|---|
| Long-running orchestration (multi-hour, multi-day) | ✅ Hosted agents + workflows | ❌ BFF instances are not persistent across days |
| Built-in HITL gates | ✅ Workflow template | ❌ Build yourself |
| A2A composition with external agents | ✅ Native protocol | ❌ Build yourself |
| Managed identity + RBAC + tracing without code | ✅ Out of the box | 🟡 Wired through `Sprk.Bff.Api` already |
| Managed long-term memory (Cosmos + extraction) | ✅ Memory primitive | ❌ Build yourself |
| Tight coupling to your own data plane and retrieval | 🟡 Possible but you're round-tripping through Foundry | ✅ Direct |
| Custom retrieval orchestration (ADR-009 cache, custom filter composition, custom evidence shaping) | ❌ Limited; you adapt to Foundry's surface | ✅ Direct control |
| Single-request synthesis (query → grounded answer with confidence + comparable set) | 🟡 Works, but indirection | ✅ Natural fit |
| Subscription cost predictability | 🟡 Per-token + per-tool-call charges | ✅ Standard Azure OpenAI billing only |

**Insights Engine sits in the bottom four rows.** Custom BFF is the right call. Foundry would be the right call for the *downstream* multi-day agentic workflows that consume Insights Engine outputs — which is a separate product surface and not in r1 scope.

### 5. Patterns the Insights Agent SHOULD borrow from Foundry memory

Even though we're not using Foundry's memory primitive, the *pattern* is worth borrowing for the Insights Engine's caching and personalization layer:

- **Two-tier memory**: static (per-user preferences, e.g., "this attorney always wants comparable matter sets ranked by recency") + contextual (per-conversation summaries that survive across requests in the same session). Map to Redis hash sets with appropriate TTLs.
- **Scope as the partition primitive**: Insights Engine queries should always be scoped to `{tenantId, userId, [matterId]}`. Never let the agent see results from a different scope.
- **Extraction by LLM, not raw storage**: when capturing user preferences from interaction signals (clicks, accepted suggestions), use a lightweight LLM extraction step to distill durable preferences. Don't store raw event streams as memory.
- **Consolidation**: when two preferences conflict (older vs. newer), the newer wins. Make this explicit, not implicit.
- **Static-then-contextual retrieval pattern**: load static user preferences into the prompt at the start of each Insights Agent invocation; retrieve contextual snippets keyed to the current query.

## Implications for Spaarke Insights Engine

1. **Don't use Foundry agent service for the Insights Agent.** The custom BFF agent is the correct architecture given Insights Engine's tight data-plane coupling and ADR-009 caching requirements.
2. **Borrow Foundry's two-tier memory pattern (static + contextual)** for the Insights Agent's personalization. Implement with Redis-backed scopes; no need to take a dependency on Foundry's memory primitive.
3. **Borrow Foundry's tool-calling shape** but implement via Microsoft Agent Framework (`AIFunctionFactory.Create`) for the BFF's tool surface. ADR-013's `IAiToolHandler` already aligns with this pattern.
4. **Document `GAP-memory.md` as resolved.** The Foundry memory documentation that was missing in 2026-05-14 is now published (2026-04-06 and 2026-04-10). The gap entry should be updated to point here.
5. **Watch the Foundry A2A protocol** as a future integration surface. Insights Engine could expose an A2A endpoint for downstream Foundry-hosted matter-diligence agents to call. r1 doesn't need this, but design the BFF endpoints to be A2A-mappable (clean request/response, no streaming peculiarities) so the future integration is straightforward.
6. **Foundry memory has explicit GDPR-style scope-deletion** but no auto-expiry. If we adopt a similar pattern in Spaarke, plan explicit delete paths (matter close-out → delete matter scope; user offboarding → delete user scope).

## Open questions

- **Is the Foundry memory cost model viable for legal-tech scale?** Preview pricing is "underlying model tokens only" with no memory-API fee. If GA introduces a per-operation fee, the calculus might change for downstream surfaces that *do* use Foundry. Watch for GA pricing.
- **Foundry Hosted agents (preview) for the *separate* multi-day matter-diligence product surface** — when those land, they'd consume Insights Engine via A2A or MCP. Worth a future investigation closer to that workstream.
- **Cross-tenant memory leakage risk in Foundry**: Microsoft documents scope-based isolation but the multi-tenant security model isn't fully detailed in the public docs. For Insights Engine's custom path this doesn't apply (we don't store memory in Foundry), but if we ever do, this question is load-bearing.
- **Confluence with Foundry IQ knowledge bases** — `knowledge/foundry-iq/` is already curated. Foundry IQ can parent an Azure AI Search index. Could the Insights Engine's index *also* serve as a Foundry IQ knowledge source for downstream Foundry-hosted surfaces? Likely yes; worth a design spike when downstream surfaces materialize.
