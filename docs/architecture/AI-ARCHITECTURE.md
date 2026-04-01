# Spaarke AI Architecture

> **Version**: 3.4
> **Last Updated**: March 13, 2026
> **Audience**: Claude Code, AI agents, engineers
> **Purpose**: Technical reference for the Spaarke AI platform component framework
> **Supersedes**: `AI-PLAYBOOK-ARCHITECTURE.md` (v2.0), `AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md` (v2.0)
> **Related ADRs**: ADR-001, ADR-007, ADR-008, ADR-009, ADR-010, ADR-013, ADR-014, ADR-015, ADR-016, ADR-022

---

## Four-Tier Architecture

Spaarke AI is organized into four tiers. Each tier has distinct responsibilities and can evolve independently.

```
 ┌─────────────────────────────────────────────────────────────────────┐
 │  TIER 1: SCOPE LIBRARY (Spaarke IP)                                │
 │  Reusable AI primitives stored in Dataverse                        │
 │  Actions · Skills · Knowledge · Tools · Outputs                    │
 │  Independent of any execution engine                               │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 2: COMPOSITION PATTERNS                                      │
 │  How scopes are assembled and invoked                              │
 │  Playbooks (visual canvas)  ·  SprkChat (conversational)           │
 │  Standalone invocation (API)  ·  Background jobs                   │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 3: EXECUTION RUNTIME                                         │
 │  Where AI logic actually runs                                      │
 │  In-Process (current) → Microsoft Agent Framework (future)         │
 │  PlaybookExecutionEngine · PlaybookOrchestrationService            │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 4: AZURE INFRASTRUCTURE                                      │
 │  Cloud services backing everything                                 │
 │  Azure OpenAI · Azure AI Search · Document Intelligence            │
 │  Redis · Service Bus · AI Foundry (future hosting option)          │
 └─────────────────────────────────────────────────────────────────────┘
```

### Key Architectural Principles

1. **Playbooks are the "frontend"** — the Spaarke-specific composition and management UI for AI workflows. The execution backend is flexible.
2. **Scopes are independent primitives** — consumable by playbooks, SprkChat, standalone API calls, and background jobs without requiring a playbook.
3. **AI nodes are backend-flexible** — a node can execute in-process (current), via Microsoft Agent Framework (future), or as a published AI Foundry agent (future).
4. **Workflow nodes stay Spaarke** — CreateTask, SendEmail, UpdateRecord, Condition, DeliverOutput, DeliverToIndex nodes always run as Spaarke code.
5. **AI Foundry is infrastructure** — it provides model hosting, Foundry IQ knowledge bases, and Agent Service runtime. It does not compete with the scope library.

---

## Tier 1: Scope Library

Scopes are reusable AI primitives stored as Dataverse records. They are the building blocks for all AI composition patterns.

### Scope Types

| Scope | Entity | Purpose | Role in Execution |
|-------|--------|---------|-------------------|
| **Actions** | `sprk_analysisaction` | System prompt templates | Define LLM persona and behavior |
| **Skills** | `sprk_analysisskill` | Prompt fragments | Add specialized instructions to prompts |
| **Knowledge** | `sprk_analysisknowledge` | RAG context sources | Provide domain context to LLM |
| **Tools** | `sprk_analysistool` | Executable handlers | Call LLM and process responses |
| **Outputs** | Playbook field mappings | Field mappings | Map results to Dataverse fields |

### Prompt Composition

Prompts are assembled from either flat text or **JSON Prompt Schema (JPS)** stored in `Action.SystemPrompt`:

```
Final LLM Prompt = Action.SystemPrompt (flat text OR JPS)
                 + Skill.PromptFragment(s)
                 + Knowledge (RAG context or inline)
                 + Document text
                 + $choices-resolved enum constraints (JPS only)
```

**JPS (JSON Prompt Schema)**: A structured JSON format (`$schema: "https://spaarke.com/schemas/prompt/v1"`) that enables:
- Structured instruction sections (role, task, constraints, context)
- Typed output field definitions with `structuredOutput: true` for Azure OpenAI constrained decoding
- Dynamic `$choices` enum injection from Dataverse at render time
- `$ref` scope references for knowledge and skills
- Template parameters (`{{paramName}}`) for runtime customization

**Format detection**: If `SystemPrompt` starts with `{` and contains `"$schema"`, it is parsed as JPS; otherwise it is treated as flat text.

### $choices — Dynamic Enum Resolution

JPS output fields can declare `"$choices"` to auto-inject valid enum values at render time. This constrains the AI model (via JSON Schema `"enum"`) to return only values that exist in Dataverse, eliminating frontend fuzzy matching.

| Prefix | Resolution Source |
|--------|-------------------|
| `lookup:` | Active records from Dataverse reference entity |
| `optionset:` | Single-select choice/picklist metadata labels |
| `multiselect:` | Multi-select picklist metadata labels |
| `boolean:` | Two-option boolean field labels |
| `downstream:` | Downstream UpdateRecord node field mapping options |

### Scope Ownership Model

Every scope has `OwnerType` and `IsImmutable`:

| Prefix | OwnerType | Mutable | Description |
|--------|-----------|---------|-------------|
| `SYS-` | System | No | Spaarke-provided, immutable |
| `CUST-` | Customer | Yes | Customer-created or extended |

Scopes support inheritance via `ParentScopeId` (extends a parent scope) and `BasedOnId` (cloned, SaveAs pattern).

---

## Tier 2: Composition Patterns

### Playbooks (Visual Canvas)

Playbooks are the primary composition pattern — visual node-based workflows stored as Dataverse records (`sprk_analysisplaybook`). They define what AI operations to perform and in what order, with a pluggable execution backend.

> **Full documentation**: See [playbook-architecture.md](playbook-architecture.md) for the complete playbook system including node type system, execution engine, canvas data model, and node executor framework.

**Node type summary**:

| NodeType (coarse) | ActionType (fine) | Examples |
|-------------------|-------------------|---------|
| AIAnalysis (100000000) | AiAnalysis (0), AiCompletion (1) | LLM calls with full scope resolution |
| Output (100000001) | DeliverOutput (40), DeliverToIndex (41) | Output assembly, RAG indexing |
| Control (100000002) | Condition (30), Wait (32) | Flow control |
| Workflow (100000003) | CreateTask (20), SendEmail (21), UpdateRecord (22) | Dataverse/email actions |

### SprkChat (Conversational)

SprkChat provides scope access through conversational AI. Scopes become agent tools. The `IChatContextProvider` pattern resolves scopes to Agent Framework tools at runtime.

### Standalone Invocation

Scopes can be invoked directly via API without a playbook:
- `POST /api/ai/analysis` — execute scopes on document(s)
- `POST /api/ai/search` — semantic search using knowledge scopes
- `GET /api/ai/handlers` — discover available tool handlers

---

## Tier 3: Execution Runtime

> **Dedicated playbook document**: [playbook-architecture.md](playbook-architecture.md) covers the playbook system in depth.

### Playbook Builder

Two builder surfaces are maintained:

| Builder | Path | Stack | Status |
|---------|------|-------|--------|
| **Code Page** (current) | `src/client/code-pages/PlaybookBuilder/` | React 18, @xyflow/react v12, Fluent v9, Zustand | Primary |
| **PCF Control** (legacy R4) | `src/client/pcf/PlaybookBuilderHost/` | React 16, react-flow v10, Fluent v9 | Maintained |

### PlaybookOrchestrationService

Orchestrates node graph execution using Kahn's topological sort for parallel batching. See [playbook-architecture.md](playbook-architecture.md) for execution flow details.

### Tool Handler Framework

Tools are dispatched through a three-tier resolution chain:

```
Tier 1: Configuration (Dataverse)
  sprk_analysistool.sprk_handlerclass → handler name (optional)
         │
         ▼
Tier 2: GenericAnalysisHandler (95% of cases)
  If handlerclass is NULL or not found → GenericAnalysisHandler
  Configuration-driven: operation, prompt_template, output_schema, temperature
  No code deployment required for new tools
         │
         ▼
Tier 3: Custom Handlers (complex scenarios)
  EntityExtractorHandler, SummaryHandler, ClauseAnalyzerHandler, etc.
  Registered in DI at startup via ToolFrameworkExtensions
```

### RAG Pipeline

**Services**: `IRagService`/`RagService`, `ISemanticSearchService`, `IEmbeddingCache` (Redis-backed), `IFileIndexingService`, `ReferenceIndexingService`, `ReferenceRetrievalService`.

**Search flow**: Query → EmbeddingCache (Redis) → Azure OpenAI (embedding) → Azure AI Search (hybrid: BM25 + Vector + Semantic) → Security filter (tenantId) → Semantic reranking → Results.

**Search indexes**:
- `spaarke-knowledge-index-v2` — Customer documents (3072-dim, HNSW, cosine)
- `spaarke-rag-references` — Golden reference knowledge (3072-dim, HNSW, cosine)

### Knowledge-Augmented Execution (R3)

The execution pipeline retrieves tiered knowledge before calling the LLM:

```
AiAnalysisNodeExecutor
  ├── L1: ReferenceRetrievalService — curated domain knowledge (spaarke-rag-references)
  ├── L2: IRagService — similar customer docs (spaarke-knowledge-index-v2, optional)
  ├── L3: IRecordSearchService — business entity metadata (optional)
  └── Merge → KnowledgeContext → Prompt assembly
```

### Dual Output Paths

| Path | Data | Storage |
|------|------|---------|
| **Analysis Output** | `ToolResult.Summary` (text) | `sprk_analysisoutput.sprk_output_rtf` |
| **Document Fields** | AI structured output (JSON) | `sprk_document.*` fields via UpdateRecord node (OData PATCH) |

Document field writes happen **during** playbook execution (UpdateRecord node), not after.

---

## Tier 4: Azure Infrastructure

| Service | Resource Name (Dev) | Purpose |
|---------|--------------------|---------|
| Azure OpenAI | `spaarke-openai-dev` | LLM completions + embeddings |
| Azure AI Search | `spaarke-search-dev` | Vector + hybrid search |
| Document Intelligence | `spaarke-docintel-dev` | PDF/image text extraction |
| Azure Redis Cache | — | Embedding cache, search result cache |
| Azure Service Bus | — | Background job queuing |
| Azure Key Vault | `spaarke-spekvcert` | Secrets management |
| Azure App Service | `spe-api-dev-67e2xz` | BFF API hosting |

### Model Selection (R3)

Model names are configured via `ModelSelectorOptions`, not hardcoded:

| Property | Default | Usage |
|----------|---------|-------|
| `DefaultModel` | `gpt-4o` | GenericAnalysisHandler (playbook AI nodes) |
| `ToolHandlerModel` | `gpt-4o-mini` | Built-in tool handlers (classification, extraction) |

**Resolution chain**: Node ConfigJson `ModelDeploymentId` → `ModelSelector.SelectModel(operationType)` → `ModelSelectorOptions.DefaultModel`.

### AI Foundry (Future Infrastructure)

AI Foundry is an optional infrastructure evolution, not a competing scope library:

| Foundry Component | Spaarke Usage | Timeline |
|-------------------|---------------|----------|
| **Foundry IQ** (knowledge bases) | Complement Knowledge scopes | Post-GA |
| **Agent Service** (managed runtime) | Optional hosting for AI nodes | Post-GA |
| **Published Agent Applications** | Stable endpoints for AI nodes | Post-GA |
| **Model Router** | Dynamic model selection for cost optimization | When GA |

**Key principle**: If we adopt Foundry Agent Service, the Playbook System remains as the configuration/composition layer above it. Foundry provides runtime; playbooks define workflows.

### Deployment Models

| Model | AI Resources | Data Isolation |
|-------|-------------|----------------|
| **Model 1: Spaarke-Hosted** | Spaarke Azure subscription | Logical (tenantId filters) |
| **Model 2: Customer-Hosted** | Customer Azure subscription (BYOK) | Physical (dedicated) |

Feature parity is identical across both models.

---

## Nodes as Agents (Architectural Evolution)

Each playbook node conceptually maps to an AI agent:

| Playbook Concept | Agent Framework Equivalent |
|------------------|---------------------------|
| Node | `AIAgent` (via `IChatClient.AsAIAgent()`) |
| Action (system prompt) | Agent instructions |
| Skills (prompt fragments) | Agent behavioral modifiers |
| Knowledge (RAG context) | Agent context / grounding |
| Tool (handler) | `AIFunctionFactory.Create()` tool |
| Node execution order (edges) | Graph-based workflow |
| PlaybookRunContext | Shared conversation state |
| PlaybookOrchestrationService | Multi-agent orchestrator |

**Evolution Path**: The PlaybookOrchestrationService evolves from a full execution engine to a thin "workflow compiler" that translates playbook canvas definitions into Agent Framework graph-based workflows. The scope library stays as Spaarke IP; the execution engine becomes a translation layer.

---

## ADR Compliance

| ADR | Constraint | Implementation |
|-----|-----------|----------------|
| ADR-001 | No Azure Functions | AI endpoints via Minimal API; indexing via BackgroundService |
| ADR-007 | No Graph SDK leakage | AI services use SpeFileStore for document access |
| ADR-008 | Endpoint filters for auth | AiAuthorizationFilter per-resource checks |
| ADR-009 | Redis-first caching | EmbeddingCache with SHA256 keys, 7-day TTL |
| ADR-010 | DI minimalism (<=15) | Tool handlers registered via ToolFrameworkExtensions |
| ADR-013 | AI Tool Framework | Extensible IAnalysisToolHandler pattern |
| ADR-014 | Dual storage pattern | Analysis Output (RTF) + Document fields (JSON) |
| ADR-015 | AI observability | Application Insights logging at each execution step |
| ADR-016 | Soft failure handling | Per-node error isolation, rate limit backoff |
| ADR-022 | React 16 + Fluent v9 (PCF); React 18 (Code Pages) | PlaybookBuilderHost PCF: React 16; PlaybookBuilder Code Page: React 18; both use Fluent UI v9 |

---

## Related Documentation

| Document | Location | Audience |
|----------|----------|----------|
| **Playbook Architecture** | [`docs/architecture/playbook-architecture.md`](playbook-architecture.md) | Engineers (playbook internals, node executors, execution engine) |
| AI Strategy & Roadmap | `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | Executive/business |
| JPS Authoring Guide | `docs/guides/JPS-AUTHORING-GUIDE.md` | Architects, Engineers (JPS schema, playbook design, scope catalog) |
| Scope Configuration Guide | `docs/guides/SCOPE-CONFIGURATION-GUIDE.md` | Admins, Power Users, Engineers (scope creation, builder UI, pre-fill) |
| AI Deployment Guide | `docs/guides/AI-DEPLOYMENT-GUIDE.md` | DevOps |
| Azure AI Resources | `docs/architecture/auth-AI-azure-resources.md` | Infrastructure |
| ADR-013 | `.claude/adr/ADR-013.md` | Architecture constraints |

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-03-13 | 3.4 | Added DeliverToIndex node (ActionType 41). |
| 2026-03-06 | 3.3 | Added JSON Prompt Schema (JPS) documentation: $choices dynamic enum resolution with 5 Dataverse prefix types. |
| 2026-03-03 | 3.2 | Updated for typed field mappings: UpdateRecord OData PATCH with typed coercion. |
| 2026-03-01 | 3.1 | Updated for Playbook Builder R5: three-level node type system, Code Page builder as primary. |
| 2026-02-21 | 3.0 | Created from consolidation of AI-PLAYBOOK-ARCHITECTURE.md (v2.0) and AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md (v2.0). Four-tier architecture, playbooks-as-frontend model, nodes-as-agents evolution. |
