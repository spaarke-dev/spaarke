# Non-BFF AI Touchpoints Inventory — S5/S6/S7

> **Project**: agent-framework-fit-assessment-r1 · **Task**: 002
> **Captured at**: 2026-06-03
> **Executor**: Claude Code (task-execute, STANDARD rigor)
> **Purpose**: Inventory the three Spaarke AI surfaces that live **outside** the BFF process model — S5 Foundry Agent Service overlap, S6 M365 Copilot / Declarative Agent surface, S7 Insights Engine MCP server. Feeds Task 004 (per-surface decision matrix) and Task 005 (deployment-model recommendations).
> **Source-of-truth discipline**: Spaarke code is ground truth; project SPECs/designs/READMEs are next; the Task 000 baseline ([`00-primary-source-baseline.md`](./00-primary-source-baseline.md)) is the Agent Framework reference. Where information is genuinely unknown, the cell reads `UNKNOWN (no current source)` — no invention.

---

## §1. Scope and method

This inventory captures, for each surface, the 6 fields required by the Task 002 POML constraints:

1. **Current implementation status** — production / in-flight / planned / curated-only
2. **Primary spec/design docs** to cite (path-linked)
3. **Microsoft.\*** abstractions involved (or planned)
4. **Deployment model** — same process as BFF / separate Azure resource / external manifest / etc.
5. **Integration seam with BFF** — how this surface talks to `Sprk.Bff.Api` (or doesn't)
6. **HITL / durability requirements**

The inventory is **read-only across all surfaces** per the POML constraints.

### Carry-forward from Task 000 baseline (relevant to S5 analysis)

Two findings from [`00-primary-source-baseline.md`](./00-primary-source-baseline.md) materially affect S5's overlap framing:

- **Workflow HITL primitive (`RequestPort` / `RequestInfoEvent`) is in Agent Framework workflows themselves**, not exclusively in Foundry. ([Page 12 in §4 of the baseline](./00-primary-source-baseline.md): `learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop`, updated 2026-03-31.) Spaarke can get pause/resume HITL via Agent Framework alone without taking a Foundry dependency.
- **Tool Approval is also a framework feature now** (baseline §4 P5 + P9 + P12). The `RequireApproval` MCP setting and the workflow `RequestPort` share a unified event model. This means the "Foundry distinguishes itself by HITL primitives" framing is partly obsolete — Foundry's S5 differentiation is now narrower: durable VM-isolated agent sessions, per-agent Entra identity, A2A endpoint exposure, and Foundry-hosted MCP/memory tools.

For S5, the fit assessment must distinguish "what Agent Framework alone gives you" vs. "what Foundry **adds on top**."

### Critical correction discovered during grep — Spaarke already has Foundry code in BFF

The Task 002 POML and SPEC §3 both describe S5 as "no Spaarke production code yet — inventory is from `knowledge/foundry-agent-service/` + ADR-013 references." **A grep of `src/` contradicts this assumption** (see §2.1 below). Spaarke has shipped an in-BFF `Services/Ai/Foundry/` namespace using the `Azure.AI.Projects` SDK that wraps a pre-provisioned Foundry Agent. The S5 inventory below treats this as the actual current state and notes the discrepancy with the SPEC.

---

## §2. S5 — Foundry Agent Service overlap

### 2.1 Negative-result greps — and one positive surprise

| Symbol searched | Result | Files |
|---|---|---|
| `FoundryAgentClient`, `AIProjectClient`, `Microsoft.Agents.AI.Foundry`, `Azure.AI.Agents`, `McpTool`, `HostedAgent` | **No matches in `src/`** | — |
| `Foundry` (case-insensitive substring) | **27 files in `src/`** | predominantly `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` namespace (5 .cs files: `AgentServiceClient.cs`, `AgentServiceOptions.cs`, `BingGroundingOptions.cs`, `CodeInterpreterBridge.cs`, `CodeInterpreterOptions.cs`, `FeatureDisabledException.cs`, `ConcurrencyLimitExceededException.cs`) plus consumers in `Services/Ai/Chat/`, `Services/Ai/Nodes/`, `Infrastructure/DI/`, `Models/Ai/Chat/`, and `Configuration/` |
| `Azure.AI.Projects` / `AgentsClient` | **6 files** | `Sprk.Bff.Api.csproj` + all 4 `Services/Ai/Foundry/*.cs` files + `Services/Ai/Chat/Tools/LegalResearchTools.cs` |
| `Microsoft.Agents.Builder` / `Microsoft.Agents.Core` | **1 file** | `Api/Agent/SpaarkeAgentHandler.cs` — M365 Agents SDK (S6, not S5) |

### 2.2 What the in-BFF Foundry code actually is

[`Services/Ai/Foundry/AgentServiceClient.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs) is a singleton wrapper around `Azure.AI.Projects.AgentsClient` (the Azure AI Projects SDK). Per its doc-comment, it:

- Connects to a pre-provisioned Foundry Agent at `AgentServiceOptions.Endpoint` (a Foundry project endpoint URL) using `AgentServiceOptions.AgentId`.
- Manages threads + runs via the SDK, with Redis-cached thread IDs keyed by `agent-thread:{tenantId}` (ADR-009).
- Bounded concurrency via `SemaphoreSlim` (ADR-016) with 30s timeout → `ConcurrencyLimitExceededException` (HTTP 429 equivalent).
- Kill-switched by `AgentServiceOptions.Enabled` (ADR-018) — defaults to `false`, opt-in only.
- Data-governance compliant per ADR-015 — only thread/run IDs + timing logged, never message content.
- **Additive per ADR-013** — explicitly does NOT modify the existing direct-pipeline code paths.

[`Services/Ai/Nodes/AgentServiceNodeExecutor.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs) routes JPS playbook nodes of `ActionType.AgentService` (value 60) to this client.

[`Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs) (labelled AIPU-072) is an outer middleware in the SprkChat agent pipeline that classifies messages via keyword/pattern matching and routes to `AgentServiceClient` for code-analysis / chart-generation / legal-research intents, otherwise falls through to the existing `DirectOpenAiAgent`.

**Implication for the S5 fit assessment**: Spaarke's current Foundry integration is the **"Foundry as a backend behind an in-BFF facade"** pattern — not the canonical Foundry use case ("durable / HITL / A2A / multi-day legal workflows" from the SPEC and `knowledge/foundry-agent-service/NOTES.md`). The actual canonical use cases described in NOTES.md remain **planned + curated, not implemented**. The fit assessment for S5 needs to evaluate both:

- The **existing in-BFF Foundry wrapper** — should this be lifted to Agent Framework primitives? (`ChatClientAgent` with a Foundry provider, or `Microsoft.Agents.AI.Foundry` once that namespace solidifies.)
- The **planned durable/HITL legal-workflow surface** that NOTES.md sketches as TODO and which the SPEC §3 row "S5 Foundry Agent Service overlap" actually maps to.

### 2.3 S5 — six required fields

| Field | Value |
|---|---|
| **(a) Status** | **MIXED**: <br>• An in-BFF Foundry Agent Service wrapper has shipped and is consumed by SprkChat middleware + JPS playbook nodes (5+ .cs files; default-OFF kill switch per ADR-018). <br>• The canonical "Foundry hosting durable legal workflows" use case named in NOTES.md is **curated-only / planned** — no graph-based workflow, HITL gate, A2A surface, or `wait_for_external_event` primitive has shipped in Spaarke. |
| **(b) Primary spec/design docs** | • [`knowledge/foundry-agent-service/NOTES.md`](../../../knowledge/foundry-agent-service/NOTES.md) — stub, all senior-engineer annotation marked TODO. <br>• [`knowledge/foundry-agent-service/SOURCE.md`](../../../knowledge/foundry-agent-service/SOURCE.md) — curated 2026-05-14 with file-by-file provenance and a "Gaps" table calling out: graph workflow DSL = GAP, `wait_for_external_event` = PARTIAL/GAP, A2A composition = GAP, MCP server tool binding = COVERED. <br>• [`knowledge/foundry-agent-service/docs/overview.md`](../../../knowledge/foundry-agent-service/docs/overview.md), [`workflows.md`](../../../knowledge/foundry-agent-service/docs/workflows.md), [`hosted-agents.md`](../../../knowledge/foundry-agent-service/docs/hosted-agents.md), [`GAP-memory.md`](../../../knowledge/foundry-agent-service/docs/GAP-memory.md). <br>• [`.claude/adr/ADR-013-ai-architecture.md`](../../../.claude/adr/ADR-013-ai-architecture.md) — names "Hosted Foundry Agent" as a permitted deployment-model exception when ALL four extraction criteria are met (no latency coupling, no transactional coupling, bounded surface, no duplication). <br>• Task 000 baseline §4 P11 (A2A) + P9 (Hosted MCP Tools) + P12 (Workflow HITL) and Devblog D4/D8/D9 for current Foundry-at-scale state. |
| **(c) Microsoft.\* abstractions** | <br>• **In use today (shipped)**: `Azure.AI.Projects.AgentsClient` (the pre-Agent-Framework SDK). NO `Microsoft.Agents.AI.*` abstractions yet. <br>• **Planned per NOTES.md (curated-only)**: `azure.ai.agents.models.McpTool`, `SubmitToolApprovalAction`, `ToolApproval` for HITL gate (canonical Python sample at `samples/mcp-tool-binding/`); per-agent Entra identity for Hosted-agent sessions; A2A endpoint pattern `{project_endpoint}/agents/{name}/endpoint/protocols/a2a`. <br>• **Candidate per Task 000 baseline**: `Microsoft.Agents.AI` Workflows (`RequestPort` / `RequestInfoEvent` HITL — Baseline §4 P12) — these are in **Agent Framework itself**, NOT exclusive to Foundry. This is the key carry-forward for the S5 fit decision: Agent Framework Workflows would deliver HITL pause/resume without taking a Foundry dependency. |
| **(d) Deployment model** | <br>• **Shipped**: In-process within `Sprk.Bff.Api` (the wrapper is just a singleton client targeting an external Foundry project). Spaarke doesn't deploy Foundry — Foundry is a hosted Azure platform; Spaarke provisions an Agent ID + endpoint and calls it. <br>• **Planned canonical** (per NOTES.md and ADR-013 §"Hosted Foundry Agent" exception): Foundry-hosted agent sessions running in Foundry's VM-isolated sandboxes (per [`docs/hosted-agents.md`](../../../knowledge/foundry-agent-service/docs/hosted-agents.md): per-session `$HOME` + `/files`, idle → resume within 15 min, per-agent Entra identity, A2A endpoint exposed). This is OUT-OF-PROCESS relative to BFF — Foundry is the hosting platform. |
| **(e) Integration seam with BFF** | <br>• **Shipped**: BFF → Foundry (outbound). `AgentServiceClient` is BFF-internal; `AgentServiceNodeExecutor` exposes Foundry capability via JPS playbook `ActionType.AgentService = 60`; `AgentServiceRoutingMiddleware` exposes it via the SprkChat agent pipeline as a routing branch. ADR-013-compliant: additive, doesn't disturb the direct-OpenAI pipeline. <br>• **Planned canonical**: bidirectional. BFF would still call Foundry (outbound), but Foundry-hosted agents would call back to Spaarke MCP servers (S7) and potentially A2A-peer with other Foundry-hosted agents. NOTES.md flags this as TODO. |
| **(f) HITL + durability requirements** | <br>• **Shipped**: NONE. The in-BFF Foundry wrapper is a synchronous request/response over Redis-cached thread IDs (sliding 60min expiry). No `wait_for_external_event`, no resume after process restart beyond Redis cache TTL, no approval gate wiring. <br>• **Planned canonical**: HIGH. NOTES.md §1 §2 explicitly lists the three legal scenarios as durable + HITL territory — full-matter diligence (multi-day), NDA negotiation chain (multi-week, term-acceptance gates), regulatory monitoring (continuous, publish-to-firm gates). NOTES.md §2 names two candidate HITL surfaces in current Foundry SDK: (1) workflow-level "Human in the loop" template (UI builder, YAML export), (2) tool-level `mcp_tool.set_approval_mode("prompt")` + `SubmitToolApprovalAction`. <br>• **Carry-forward from Task 000 baseline**: Agent Framework Workflows themselves now ship `RequestPort` / `RequestInfoEvent` HITL primitives, which can run in BFF without Foundry. This narrows the "must use Foundry for HITL" claim. |

---

## §3. S6 — M365 Copilot / Declarative Agent surface

### 3.1 S6 — six required fields

| Field | Value |
|---|---|
| **(a) Status** | **In-flight** — active project [`projects/ai-m365-copilot-integration/`](../../../projects/ai-m365-copilot-integration/) targeting June 2026 launch. README progress 0%; spec is "Ready for Implementation." <br>• **Already in `src/`**: `src/server/api/Sprk.Bff.Api/Api/Agent/` directory contains 14 .cs files including [`SpaarkeAgentHandler.cs`](../../../src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs) (M365 Agents SDK `ActivityHandler`), `AgentEndpoints.cs`, `AgentTokenService.cs`, `AdaptiveCardFormatterService.cs`, `HandoffUrlBuilder.cs`, `PlaybookInvocationService.cs`. SpaarkeAgentHandler is a thin TODO scaffold per its constructor (only `ILogger` injected; comments enumerate services to inject "when wired"). <br>• **Not yet in `src/`**: Declarative Agent manifest (`declarativeAgent.json`), API Plugin manifest (`spaarke-api-plugin.json`), OpenAPI spec (`spaarke-bff-openapi.yaml`), Adaptive Card templates, Bot Service Bicep, BYOK templates. |
| **(b) Primary spec/design docs** | • [`projects/ai-m365-copilot-integration/README.md`](../../../projects/ai-m365-copilot-integration/README.md) — overview, scope, key decisions. <br>• [`projects/ai-m365-copilot-integration/spec.md`](../../../projects/ai-m365-copilot-integration/spec.md) — 20 functional requirements + 8 NFRs + 8 use cases (UC-M1…UC-M8) + Adaptive Card schema 1.5 constraint + platform-limitations table. <br>• [`projects/ai-m365-copilot-integration/design.md`](../../../projects/ai-m365-copilot-integration/design.md) — Tier 1/2/3/4 architecture, MCP server discussion at lines 244-275 (Tier 3, deferred to R2), Copilot-vs-SprkChat positioning (SprkChat is repositioned to Analysis Workspace only). <br>• [`projects/ai-m365-copilot-integration/plan.md`](../../../projects/ai-m365-copilot-integration/plan.md) — 4-phase implementation plan with parallel execution groups. <br>• [`projects/ai-m365-copilot-integration/CLAUDE.md`](../../../projects/ai-m365-copilot-integration/CLAUDE.md) — applicable ADRs (001, 008, 010, 013, 014, 015, 016, 019) + MUST/MUST NOT rules. |
| **(c) Microsoft.\* abstractions** | <br>• **In use today**: `Microsoft.Agents.Builder`, `Microsoft.Agents.Builder.Compat`, `Microsoft.Agents.Core.Models` — the **M365 Agents SDK** (formerly Bot Framework), per [`SpaarkeAgentHandler.cs`](../../../src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs) line 2-4 `using` statements. This is the Bot Framework `ActivityHandler` pattern under the new SDK namespace. The Compat namespace provides the familiar routing pattern on top of the new SDK. <br>• **Planned per spec/design**: M365 Copilot Declarative Agent (3 manifest files), API Plugin functions reading the OpenAPI spec, Adaptive Card schema 1.5, Azure Bot Service (Azure resource, not a .NET namespace), `ConversationFileReference` (Spike 1), `Action.Submit` (Spike 2). <br>• **NOT planned for R1**: `Microsoft.Agents.AI.*` (the Agent Framework). The README and spec.md describe the gateway as "thin adapter facades over existing BFF services — no new AI orchestration logic." Agent Framework hosting of this surface is not in the R1 scope. <br>• **NOT planned for R1 but for R2**: MCP server (Tier 3 — see design.md §244-275 + project README "Out of Scope"). |
| **(d) Deployment model** | <br>• **External manifests + Azure Bot Service** for the agent-channel side: Declarative Agent + API Plugin manifests deployed to **M365 org app catalog** via M365 Agents Toolkit; Custom Engine Agent registered with **Azure Bot Service** with channel config for Copilot (and Teams for testing). <br>• **BFF endpoints** (in-process) for the adapter side: agent gateway endpoints (`POST /api/agent/message`, `GET /api/agent/playbooks`, `POST /api/agent/run-playbook`) land in `Sprk.Bff.Api`. <br>• **BYOK customer-hosted**: Bicep templates for BFF + Azure OpenAI + AI Search in customer's own Azure subscription. <br>• **No separate AI process** — the design.md explicitly chose Path A (direct API Plugin + manifests) over Copilot Studio, with the BFF as "the brain." |
| **(e) Integration seam with BFF** | <br>• **Inbound to BFF**: M365 Copilot → API Plugin (HTTPS over OpenAPI spec) → BFF agent gateway endpoints → existing BFF AI services (Chat, Search, Playbooks, Analysis, RAG, Documents, Workspace, Communications). <br>• **Auth**: SSO token flow (M365 → OBO → BFF → Graph/Dataverse) per FR-13. <br>• **Adapter principle (binding)**: agent gateway endpoints are THIN ADAPTERS — MUST reuse existing BFF services per spec.md §"MUST NOT create new AI orchestration logic" and CLAUDE.md MUST rule "use existing BFF services — agent endpoints are adapters only." <br>• **SPE discoverability**: `discoverabilityDisabled = true` — Copilot never sees SPE containers directly; all document access flows through BFF with per-matter authorization (NFR-01). |
| **(f) HITL + durability requirements** | <br>• **Durability**: LOW for the R1 scope. The agent is a request/response surface over the side-pane chat — no multi-day session state in the agent itself (session state lives in BFF). For long-running playbooks, the chosen strategy is **async pattern OR deep-link to Analysis code page** (FR-15) rather than durable in-Copilot state. <br>• **HITL**: NOT a primary design driver for R1. The user-confirmation flow that exists (`Action.Submit` button on Adaptive Cards for playbook selection — UC-M2/UC-M3) is interactive UI affordance, not a durable HITL gate. Spike 2 explicitly validates whether `Action.Submit` even works in API plugin responses inside MDA Copilot. <br>• **Platform timeout**: API plugin response timeout is "exact limit TBD" (spec Unresolved Question); assumed <30s. Long playbooks deflect to async/deep-link, not to a wait-for-event primitive. |

---

## §4. S7 — Insights Engine MCP server

### 4.1 S7 — six required fields

| Field | Value |
|---|---|
| **(a) Status** | **Planned (design phase) — MCP IMPLEMENTATION DEFERRED TO PHASE 2**. <br>• Project [`projects/ai-spaarke-insights-engine-r1/`](../../../projects/ai-spaarke-insights-engine-r1/) is in "Design — pre-implementation" status (README). <br>• Phase 1 SPEC §3.1 D-A20 = **"MCP server contract document"** ([`projects/ai-spaarke-insights-engine-r1/mcp-contract.md`](../../../projects/ai-spaarke-insights-engine-r1/mcp-contract.md)) — contract is in scope for Phase 1; **implementation is deferred to Phase 2**. <br>• **No `src/` code yet**: grep for `Insights\.Mcp` or `ModelContextProtocol` returns zero matches in `src/`. The Insights Engine itself is also pre-implementation — Phase 1 ships substrate + envelope + Insights Agent shell (D-A1..D-A14, D-A22..D-A27); the MCP server contract document is design-only. |
| **(b) Primary spec/design docs** | • [`projects/ai-spaarke-insights-engine-r1/README.md`](../../../projects/ai-spaarke-insights-engine-r1/README.md) — overview, four-tier taxonomy (Fact / Observation / Precedent / Inference), substrate decisions. <br>• [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../../projects/ai-spaarke-insights-engine-r1/SPEC.md) — Phase 1 deliverables D-A1..D-A27. Specifically: **D-A20** is the MCP server contract document, and §3.6 "Explicitly NOT in scope" excludes the MCP server implementation. <br>• [`projects/ai-spaarke-insights-engine-r1/design.md`](../../../projects/ai-spaarke-insights-engine-r1/design.md) — 13-section comprehensive design (1268 lines). §5.1 commits to "custom BFF agent, not Foundry-hosted" for the Insights Agent itself. <br>• [`projects/ai-spaarke-insights-engine-r1/decisions.md`](../../../projects/ai-spaarke-insights-engine-r1/decisions.md) — D-39..D-45 backing D-A15..D-A21 (architecture-doc r2); D-46..D-51 backing D-A22..D-A27 (LAVERN-derived). D-41 is the canonical decision behind the MCP contract. <br>• [`projects/ai-spaarke-insights-engine-r1/mcp-contract.md`](../../../projects/ai-spaarke-insights-engine-r1/mcp-contract.md) — **UNKNOWN (no current source)**: SPEC names this file as a Phase 1 deliverable but glob shows it does not exist yet. The file will be created during Phase 1 W1.5 per §8 wave structure. |
| **(c) Microsoft.\* abstractions** | <br>• **In use today**: NONE in `src/` for the Insights Engine. Existing reused: `Microsoft.Extensions.AI.IChatClient` + `UseFunctionInvocation` + tool framework (the same Zone A primitives SprkChat uses) — but this is for the in-BFF Insights Agent (D-A9), not for the MCP server. <br>• **Planned for MCP contract (D-A20)**: per SPEC §3.1 the contract names "tool signatures (`predict_matter_cost`, `find_comparable_matters`, `assess_matter_risks`, `summarize_matter_closure`), resource URIs, prompt fragments, OBO auth flow." The actual `Microsoft.*` host abstraction is **UNKNOWN (no current source)** — neither the SPEC nor the README names a specific MCP server library (e.g., `ModelContextProtocol.AspNetCore`, `Microsoft.Agents.AI` MCP tools hosting). <br>• **Anti-pattern explicitly rejected**: SPEC §3.5 forbids `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.OpenAI.*`, and direct `IChatClient` in Zone B (`Services/Insights/`). The Insights Agent + tools must live in Zone A (`Services/Ai/Insights/`) and be consumed via the `IInsightsAi` facade. The MCP server, when built, will likewise need to wrap the facade, not bypass it. |
| **(d) Deployment model** | <br>• **Insights Engine itself**: hybrid. The synthesis Agent + endpoint live IN BFF (per SPEC §4 "Synthesis: custom Insights Agent in `Sprk.Bff.Api/Services/Ai/Insights/`"). The sync/extraction pipelines (Track B, blocked on Phase C auth) live in **Azure Functions** (per SPEC §4 "Azure Functions on Flex Consumption + Service Bus topic"). This already exercises ADR-013's permitted exception (Functions for sync/extraction is the canonical example named by ADR-013). <br>• **MCP server**: **UNKNOWN (no current source)** — SPEC and design.md do not commit a deployment model. The contract document (D-A20) is intended to define this. Candidates are: (1) separate `Sprk.Insights.Mcp` deployable per ADR-013's "MCP server exposing AI capabilities to external consumers" example, or (2) MCP endpoints embedded in BFF. The choice will turn on whether the MCP server's consumers (e.g., M365 Copilot Tier 3 — see S6 §3.1 (b)) require external network exposure independent of BFF. |
| **(e) Integration seam with BFF** | <br>• **Insights Engine sync (Track B → BFF data shape)**: Azure Functions write `InsightArtifact` envelopes into AI Search indexes (`insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`, `insight-precedents`) + Cosmos NoSQL graph + Dataverse `sprk_precedent` entity. BFF reads from these substrates via `InsightsResolverService` (D-A8). <br>• **Insights Engine query path (Phase 1 Track A)**: `POST /api/insights/ask` endpoint on BFF (D-A11). `InsightsResolverService` orchestrates `IInsightGraph` + `LiveFactResolverService` + AI Search + `IInsightsAi` facade. <br>• **MCP server → BFF**: **UNKNOWN (no current source)** for the seam — contract not yet written. Most likely shape (inferable from SPEC §3.1 D-A20 tool names): MCP server tools wrap calls to `/api/insights/ask` or directly to `IInsightsAi`, with OBO auth flow. Until the contract document exists, this is design intent only, not a spec. |
| **(f) HITL + durability requirements** | <br>• **Insights Engine sync (Track B)**: HIGH durability — Service Bus topic + Functions + idempotent reconciliation (D-B4 TimerTrigger) explicitly designed to survive failures. NO HITL (sync is automated). <br>• **Insights Engine query path**: LOW durability (synchronous request/response over Redis-cached substrates). NO HITL — the `IDeclineToFindTool` (D-A24) returns a structured `DeclineResponse` rather than waiting for a human; insufficient-evidence is a deterministic exit, not a pause. <br>• **MCP server**: **UNKNOWN (no current source)** — depends on consumer pattern. If consumers are agentic (M365 Copilot, Foundry-hosted agents, external A2A peers), the MCP tools themselves are likely request/response (per the canonical Foundry MCP sample at [`knowledge/foundry-agent-service/samples/mcp-tool-binding/`](../../../knowledge/foundry-agent-service/samples/mcp-tool-binding/)). Tool-level HITL (the `mcp_tool.set_approval_mode("prompt")` pattern, see [`knowledge/foundry-agent-service/samples/mcp-approval-gate/`](../../../knowledge/foundry-agent-service/samples/mcp-approval-gate/)) is a possible design choice for destructive Insights write-back paths, but those write-back paths are themselves Phase 2+ (per SPEC §3.6 "Full GateResolver consumption — Phase 2+"). |

---

## §5. Cross-cutting observations

### Observation 1 — Two of three surfaces are external consumers of BFF capability; one is durable/HITL territory

S6 (M365 Copilot) and S7 (Insights MCP, when built) are both fundamentally **external-consumer adapter surfaces** that expose BFF capability to a different AI runtime (Copilot's host, or an MCP-consuming agent). Their primary design driver is **bounded, well-defined integration surface** — the ADR-013 exception criterion #3. Neither is durable, neither is HITL-centric (S6 explicitly defers long-running playbooks to async / deep-link; S7's `IDeclineToFindTool` is a deterministic exit, not a pause).

S5 (Foundry Agent Service overlap) is **the opposite** — its canonical use cases per `knowledge/foundry-agent-service/NOTES.md` are durable multi-day legal workflows with HITL gates, where the surface IS the durability + HITL story. The Spaarke code that exists today (`Services/Ai/Foundry/AgentServiceClient.cs`) does NOT live in that canonical space — it's a request/response wrapper that uses Foundry as an alternative chat backend, not as a durable workflow host.

**Implication for the assessment**: S5's fit decision is bimodal. The shipped wrapper and the planned canonical surface are different problems — the assessment must evaluate them separately. The shipped wrapper is a candidate for "lift to `Microsoft.Agents.AI.ChatClientAgent` with a Foundry provider" (same fit question as S1). The planned canonical surface is the actual Agent-Framework-Workflows vs. Foundry-Hosted-Agents question.

### Observation 2 — Agent Framework's HITL primitives shrink S5's exclusivity claim, but Foundry retains the hosting + identity differentiation

Per the Task 000 baseline (§4 P12 + carry-forward in §1 of this inventory), Agent Framework Workflows ship `RequestPort` / `RequestInfoEvent` and tool-approval as framework features. This means **HITL pause/resume is no longer a Foundry-exclusive capability** — Spaarke could ship a durable workflow with HITL gates running in BFF (or, more likely per ADR-013, in an Azure Function for the long-running portion) using Agent Framework alone.

What Foundry still uniquely brings (per the curated `docs/overview.md` + `docs/hosted-agents.md`):

- **Per-session VM-isolated sandboxes** with `$HOME` + `/files` persistence and active → idle (15 min) → resume-with-state-restored lifecycle.
- **Per-agent Entra identity** for cross-system auth (relevant to A2A composition and to MCP-server auth-as-the-agent rather than auth-as-the-user).
- **A2A endpoint exposure** at `{project_endpoint}/agents/{name}/endpoint/protocols/a2a` (preview surface, no curated sample as of 2026-05-14).
- **Foundry-hosted MCP tools and Foundry memory** (memory is preview; concept doc still GAP per `docs/GAP-memory.md`).

**Implication for the assessment**: the S5 Adopt/No/Partial decision narrows to: do Spaarke legal workflows actually need per-session VM isolation + per-agent Entra identity + A2A exposure? If yes, Foundry is the home and the assessment recommends adopting `Microsoft.Agents.AI.Foundry` (or the Azure AI Projects SDK) for that surface. If no, Agent Framework Workflows in a BFF-or-Function home delivers the HITL + durability story without the Foundry preview-risk + cost tax.

### Observation 3 — All three surfaces are governed by ADR-013's four-criterion exception test; none have explicitly applied it yet

ADR-013 binds: separate deployable is permitted only when ALL of (no latency coupling, no transactional coupling, bounded surface, no duplication of latency-sensitive components) hold.

- **S5 shipped wrapper**: in BFF today, additive to the SprkChat pipeline, conforms to ADR-013 by default. The planned canonical durable Foundry-hosted surface would meet criteria 1-3 trivially (it's by definition out-of-process and bounded); criterion 4 (no duplication) needs to be verified against any latency-sensitive Spaarke-side components (RAG, safety, session) the workflow consumes.
- **S6 M365 Copilot**: agent gateway endpoints live in BFF (in-process); Declarative Agent + API Plugin manifests + Azure Bot Service live outside BFF. The split passes the four criteria for the external manifests (no latency coupling — the user-perceived latency is between Copilot and the user, not between agent and BFF state), but the spec/design do NOT explicitly cite the ADR-013 criteria in their placement justification. Per root CLAUDE.md §10, projects adding BFF code must do so — S6 has not yet, though its CLAUDE.md does list ADR-013 in applicable ADRs.
- **S7 Insights MCP server**: SPEC explicitly defers the MCP server implementation and contract to D-A20, so the four-criterion test for the MCP server boundary is **pending the contract document**. The Insights Engine sync pipelines (Track B) DO explicitly invoke ADR-013's "Functions for sync/extraction" exception; they are the canonical example.

**Implication for the assessment**: Task 004's per-surface decision matrix should call out which of the four ADR-013 criteria each surface materially satisfies, and which are deferred / unstated. For S6 and S7, the assessment can recommend that any future SPEC update include an explicit "Placement Justification" section per root CLAUDE.md §10.

---

## §6. UNKNOWNs (no invention)

| # | Item | Why UNKNOWN |
|---|---|---|
| U1 | S7 MCP server's specific `Microsoft.*` host abstraction (e.g., `ModelContextProtocol.AspNetCore` vs. `Microsoft.Agents.AI` MCP host) | SPEC names the contract document deliverable (D-A20) but does not specify the host library. The contract document itself does not yet exist in `projects/ai-spaarke-insights-engine-r1/`. |
| U2 | S7 MCP server's deployment model (separate `Sprk.Insights.Mcp` vs. embedded in BFF) | Same — contract not yet written. Spec §3.6 explicitly defers implementation to Phase 2. |
| U3 | S7 MCP server's BFF integration seam (wraps `/api/insights/ask` vs. directly wraps `IInsightsAi`) | Same — contract not yet written. |
| U4 | Whether the existing `Services/Ai/Foundry/AgentServiceClient` wrapper will be lifted to `Microsoft.Agents.AI` Foundry abstractions in any planned R-series project | No R-series project currently scopes this lift. The Task 000 baseline §3 catalog notes `02-agents/AgentsWithFoundry` and `04-hosting/FoundryHostedAgents` as upstream samples relevant here; no Spaarke project currently consumes these patterns. |
| U5 | Whether the planned S5 canonical durable legal workflows (NDA negotiation chain, full-matter diligence, regulatory monitoring) will be authored as Agent Framework Workflows (BFF-hosted or Function-hosted) or as Foundry-hosted agents | `knowledge/foundry-agent-service/NOTES.md` §1 is a TODO for senior engineer; no Spaarke project has decided this yet. This IS the question the fit assessment is being written to answer (Task 004 / 006). |
| U6 | API plugin response timeout limit in M365 Copilot (affects whether some playbooks fit inline vs. require deep-link/async) | S6 [`spec.md`](../../../projects/ai-m365-copilot-integration/spec.md) Unresolved Questions — assumed <30s; exact limit TBD pending Spike 1/2/3. |

---

## §7. Sign-off

This inventory satisfies Task 002 acceptance criteria:

- ✅ `projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md` exists with sections for **S5** (§2), **S6** (§3), **S7** (§4).
- ✅ Each surface has all **6 fields** populated (status / docs / abstractions / deployment / BFF seam / HITL+durability).
- ✅ Unknown information is explicitly marked `UNKNOWN (no current source)` — see §6 for the consolidated list. No invention.
- ✅ **Cross-cutting observations** identifies **3 patterns** (§5).
- ✅ Negative-result greps recorded — §2.1 documents the absence of `FoundryAgentClient` / `AIProjectClient` / `Microsoft.Agents.AI.Foundry` / `McpTool` / `HostedAgent` / `Insights.Mcp` / `ModelContextProtocol` in `src/`.
- ✅ Positive-result grep recorded — §2.1 also documents the unexpected presence of an in-BFF `Services/Ai/Foundry/` namespace using `Azure.AI.Projects.AgentsClient`, with §2.2 explaining what it is and §2.3 (a) flagging the discrepancy with the project SPEC's assumption that "S5 has no Spaarke code yet."

Downstream consumers (Task 003 feature mapping, Task 004 decision matrix, Task 005 deployment recommendations) should treat the S5 bimodal framing (§5 Observation 1) and the S7 MCP-contract gaps (§6 U1-U3) as material inputs to per-surface conclusions.
