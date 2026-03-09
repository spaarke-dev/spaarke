# Spaarke AI Platform: Strategy, Architecture & Roadmap

> **Version**: 1.0
> **Date**: February 20, 2026
> **Status**: Draft — Assessment & Planning
> **Audience**: Engineering, Product, Architecture
> **Related**: [AI Architecture Guide](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) · [AI Strategy](../../docs/architecture/SPAARKE-AI-STRATEGY.md) · [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md)

---

## 1. Executive Overview

### 1.1 Vision

Build a world-class legal AI platform that delivers both **analysis-focused intelligence** (document review, contract analysis, response drafting, risk detection) and **operations intelligence with agentic process automation** (matter lifecycle automation, compliance workflows, cross-document portfolio intelligence).

### 1.2 Strategic Position: The Fast Follower Advantage

Established legal AI vendors — Harvey ($160M+ raised), Ivo ($55M), CoCounsel (Thomson Reuters R&D) — have invested hundreds of millions building proprietary AI infrastructure. Spaarke's thesis is that by building on the Microsoft ecosystem and leveraging the rapid commoditization of AI capabilities, we can deliver competitive or superior solutions at orders of magnitude lower cost.

**Why this works in February 2026:**

- **AI capabilities have commoditized.** Azure OpenAI provides the same GPT-4o models that Harvey uses, at API pricing. text-embedding-3-large at 3072 dimensions delivers retrieval quality that required custom model training 18 months ago.
- **Orchestration frameworks have matured.** Microsoft's Semantic Kernel and Agent Framework (GA Q1-Q2 2026) provide production-grade multi-agent orchestration that Harvey built from scratch.
- **The Microsoft ecosystem is Spaarke's moat.** Spaarke is embedded in Dataverse where legal teams manage matters, contacts, and documents. Competitors are standalone tools requiring context switching. When a Spaarke AI agent finds a contract risk, it creates a Dataverse task, assigns it to the responsible lawyer, and updates the matter — all within the same system.
- **Two-tier deployment provides enterprise flexibility** that standalone SaaS vendors cannot match. Model 2 (customer-hosted) gives regulated enterprises full data sovereignty with AI resources in their own Azure tenant.

### 1.3 Competitive Landscape (February 2026)

| Vendor | Investment | Core Capability | Key Limitation |
|--------|-----------|-----------------|----------------|
| **Harvey** | $160M+ | 18K+ custom workflows, 200+ legal knowledge sources, advanced reasoning, shared workspaces | Standalone platform; no practice management integration; expensive per-seat |
| **Ivo** | $55M | 400+ specialized AI agents per review, 75% faster reviews, "contract intelligence" dashboards | CLM-replacement positioning; no integration with existing practice management |
| **CoCounsel** | TR R&D budget | Agentic Deep Research grounded in Westlaw/Practical Law, 10K-doc bulk review, workflow builder | Locked to Thomson Reuters content; expensive; poor customization |
| **Spellbook** | ~$25M | Word-native redlining, 2,300+ clauses, custom playbooks, industry benchmarks | Law-firm focused only; no platform/CRM integration; limited to Word |
| **LexisNexis Protege** | Corporate R&D | Multi-agent orchestration (4 specialized agents), deep legal research | Locked to Lexis content ecosystem; traditional licensing |

**Industry trajectory (analyst consensus):**
- Zero-touch contracting for low-risk agreements
- Surgical redlining at 95% accuracy with firm-style matching
- Agentic workflows moving from pilot to production infrastructure
- In-house legal AI adoption doubled from 23% to 52% in 12 months
- Multi-agent systems for complex legal workflows (research + analysis + drafting as coordinated agents)

### 1.4 Spaarke's Structural Advantages

1. **Microsoft Ecosystem Native** — Embedded in Dataverse where legal teams already work. AI runs inside the practice management workflow, not alongside it.
2. **SharePoint Embedded** — Document management built-in via SpeFileStore with OBO auth, version history, and Graph API.
3. **Office Add-in Framework** — Word and Outlook adapters with shared auth, SSE streaming, and task pane components already built.
4. **Playbook Engine** — PlaybookExecutionEngine with DAG execution graph and typed node executors (AI, Condition, Task, Email, Record Update, Deliver Output).
5. **Two-Tier Deployment** — Model 1 (Spaarke-hosted SaaS) and Model 2 (customer-hosted) with full Bicep IaC.
6. **Cost Structure** — Azure AI API pricing vs. competitor custom infrastructure. 10-100x lower build cost.

---

## 2. Current State Assessment

### 2.1 What Exists Today

| Component | Status | Key Assets |
|-----------|--------|------------|
| **AI Infrastructure** | Production | Azure OpenAI (gpt-4o-mini, text-embedding-3-large), AI Search (standard, semantic), Doc Intelligence (S0), AI Foundry Hub+Project (deployed, Prompt Flows not activated) |
| **Retrieval Pipeline** | Production | `RagService` (hybrid BM25 + vector + RRF), `SemanticSearchService` with reranking, `EmbeddingCache` (Redis), `FileIndexingService`, dual vector indices (1536+3072-dim) |
| **Scope System** | Partial | Three-tier scope system (Actions/Skills/Knowledge) built, `ScopeResolverService` exists. Dataverse persistence incomplete. **Zero seed data.** |
| **Orchestration** | Production | `PlaybookExecutionEngine` with DAG graph, 6 node executor types, `AiPlaybookBuilderService` with AI-assisted builder |
| **Tool Handlers** | Production | 10+ handlers: ClauseAnalyzer, RiskDetector, EntityExtractor, DocumentClassifier, DateExtractor, FinancialCalculator, SummaryHandler, etc. |
| **PCF Controls** | Production | AnalysisWorkspace, SemanticSearchControl (full UI with filters/infinite scroll), AnalysisBuilder, DocumentRelationshipViewer |
| **Office Add-ins** | Foundation | Word + Outlook adapters with shared auth (NAA + Dialog), SSE client, task pane shell components |
| **Telemetry** | Basic | `AiTelemetry`, `RagTelemetry`, `CacheMetrics` (OpenTelemetry counters/histograms) |
| **Deployment** | Production | Bicep stacks for Model 1 (shared) and Model 2 (customer-hosted), KnowledgeDeploymentService with Shared/Dedicated/CustomerOwned routing |

### 2.2 Critical Gaps

| Gap | Impact | Blocks |
|-----|--------|--------|
| **No clause-aware chunking** | `TextChunkingService` does character-position chunking with sentence boundaries only. Clauses split across chunks produce incoherent retrieval. | Retrieval quality for all legal use cases |
| **Zero seed data** | No Actions, Skills, Knowledge sources, or Playbooks in Dataverse. Entire playbook/analysis pipeline unusable for demos or production. | User-facing value demonstration |
| **No evaluation harness** | `eval-config.yaml` skeleton exists but metric scripts missing. No gold datasets. Cannot measure improvement. | Data-driven iteration on any AI component |
| **Query preprocessing is NoOp** | `IQueryPreprocessor` interface exists, implementation is `NoOpQueryPreprocessor`. No query expansion, no legal term normalization. | Search quality |
| **No metadata enrichment** | Documents indexed without clause category, jurisdiction, contract type. Limits filtering precision. | Client ontology overlays, precision search |
| **No structured output validation** | Tool handlers return unvalidated text. No citation enforcement. No iterative correction. | Output reliability and trust |
| **No in-document AI experience** | Word add-in exists but has no AI features. No redlining, no in-document analysis panel. | Competing with Spellbook's core value proposition |
| **Hand-rolled agent pattern** | `BuilderAgentService` uses raw OpenAI function-calling. Works for one use case but doesn't scale to multiple agent types. | Multi-agent workflows |
| **AI Foundry dormant** | Hub+Project deployed but Prompt Flows not activated. Evaluation pipeline non-functional. | Quality measurement, prompt versioning |
| **No legal knowledge packs** | No clause taxonomy, risk taxonomy, standard positions, or golden examples. | Domain-specific AI quality |

---

## 3. Microsoft Service Architecture

### 3.1 Services: Extend, Enhance, and Add

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                        SPAARKE PRODUCT BOUNDARY                               │
│                                                                               │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │                     Sprk.Bff.Api (.NET 8)                               │ │
│  │                                                                         │ │
│  │  NEW: Microsoft.SemanticKernel (NuGet — in-process library)             │ │
│  │  ├── Agent definitions (Review, Drafting, Research, Quality)            │ │
│  │  ├── Plugins wrapping existing services as KernelFunctions              │ │
│  │  └── Multi-agent patterns (Sequential, Handoff) via Agent Framework     │ │
│  │                                                                         │ │
│  │  EXISTING: Service layer (architecture unchanged)                       │ │
│  │  ├── OpenAiClient (Azure.AI.OpenAI)                                    │ │
│  │  ├── RagService (Azure.Search.Documents)                               │ │
│  │  ├── TextExtractorService (Doc Intelligence)                           │ │
│  │  ├── SpeFileStore (Graph API)                                          │ │
│  │  └── DataverseClient                                                   │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                               │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │                     Azure Services (Infrastructure)                     │ │
│  │                                                                         │ │
│  │  EXTEND                           ENHANCE                              │ │
│  │  ┌────────────────────────┐       ┌────────────────────────┐           │ │
│  │  │ Azure OpenAI           │       │ AI Search              │           │ │
│  │  │ + gpt-4o deployment    │       │ + synonym maps         │           │ │
│  │  │ + structured outputs   │       │ + scoring profiles     │           │ │
│  │  │ + increased TPM        │       │ + enriched schema v3   │           │ │
│  │  └────────────────────────┘       │ + metadata fields      │           │ │
│  │  ┌────────────────────────┐       └────────────────────────┘           │ │
│  │  │ Doc Intelligence       │       ┌────────────────────────┐           │ │
│  │  │ + Layout model         │       │ AI Foundry             │           │ │
│  │  │ + key-value extraction │       │ + activate Prompt Flows│           │ │
│  │  │ + structure output     │       │ + evaluation pipeline  │           │ │
│  │  └────────────────────────┘       │ + gold datasets        │           │ │
│  │                                   │ + tracing integration  │           │ │
│  │  NO CHANGE                        └────────────────────────┘           │ │
│  │  • Service Bus     • Redis                                             │ │
│  │  • Key Vault       • App Service    EVALUATE (later)                   │ │
│  │  • Storage         • SPE            • Azure AI Agent Service           │ │
│  │                                     • Custom Doc Intel models          │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                               │
│  Client Surface (extend)                                                      │
│  ├── PCF Controls (Dataverse) — AnalysisWorkspace, SemanticSearch, etc.      │
│  ├── Word Add-in — ADD: AI review panel + redlining                          │
│  └── Outlook Add-in — ADD: document intake agent                             │
│                                                                               │
│  NOT USED (and why)                                                           │
│  ✗ Copilot Studio — Microsoft product, not embeddable in our product         │
│  ✗ Power Automate — internal automation tool, not productizable              │
│  ✗ Power Pages — consumer portal, not enterprise legal workflow              │
│  ✗ Azure Functions — ADR-001 prohibits; BFF + Service Bus covers needs       │
│  ✗ Durable Functions — same; Agent Framework replaces this pattern           │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Service Change Detail

#### Azure OpenAI (EXTEND)

| Change | Description | Both Models |
|--------|-------------|-------------|
| Add gpt-4o deployment | Dev environment has only gpt-4o-mini. Agentic workflows need full reasoning capability. Model 1 Bicep already provisions gpt-4o at 150K TPM; Model 2 at 80K TPM. | Config change; Bicep already supports this |
| Enable structured outputs | `response_format: { type: "json_schema" }` guarantees JSON schema compliance. Replaces hand-built validation loops. | Code change in `OpenAiClient` |
| Increase TPM capacity | Multi-step agent workflows make 3-8 sequential LLM calls per user action. Current 10K TPM will bottleneck. | Bicep parameter change |

**Architecture impact**: None. `OpenAiClient` in BFF remains the single access point. ADR-013 holds.

#### Azure AI Search (ENHANCE)

| Change | Description | Both Models |
|--------|-------------|-------------|
| Index schema v3 | Add fields: `clauseCategory`, `jurisdictionCode`, `contractType`, `parties[]`, `sectionPath`, `governingLaw`. Enable precision filtering. | New index schema definition; update `KnowledgeDocument` model |
| Synonym maps | Legal terminology normalization at the index level. "Indemnify" = "hold harmless" = "defend". Native AI Search feature — no custom code. | JSON configuration uploaded via API or Bicep |
| Scoring profiles | Boost recent documents, exact clause category matches, same-jurisdiction results. | Index configuration change |
| Semantic config tuning | Use `sectionPath` + `clauseCategory` as semantic title fields (currently uses `fileName`). | Schema update |

**Architecture impact**: None. `RagService` and `SemanticSearchService` stay the same. Index schema changes are backward-compatible (additive fields).

#### Azure Document Intelligence (ENHANCE)

| Change | Description | Both Models |
|--------|-------------|-------------|
| Switch to Layout model | Returns document structure: headings, paragraphs, tables, section hierarchy, bounding regions. Foundation for clause-aware chunking. | Change `AnalyzeDocument("prebuilt-read")` → `AnalyzeDocument("prebuilt-layout")` |
| Key-value pair extraction | Layout model identifies key-value pairs (e.g., "Governing Law: State of New York"). Feeds metadata enrichment without LLM calls. | Parse `keyValuePairs[]` from response |
| Custom classification model (Phase 3+) | Train document type classifier for legal document types. Faster and cheaper than LLM classification. | AI Foundry training + model deployment |

**Architecture impact**: None. `TextExtractorService` wrapper stays the same. Output model gets richer.

#### Azure AI Foundry — Hub + Project (ACTIVATE)

Currently deployed but dormant. See **Section 4** for detailed treatment of AI Foundry's three roles.

#### Application Insights (ENHANCE)

| Change | Description |
|--------|-------------|
| Semantic Kernel OpenTelemetry integration | SK has built-in OTel instrumentation. Every kernel function call, LLM invocation, and tool dispatch is automatically traced. Wire to existing App Insights. |
| Agent execution traces | End-to-end visibility: user request → agent orchestration → tool calls → LLM calls → response. Critical for debugging multi-agent behavior. |
| AI-specific dashboards | Token usage per customer, latency per agent type, retrieval quality metrics. Extend existing `AiTelemetry`/`RagTelemetry`. |

---

## 4. AI Foundry: Product Infrastructure, Not Internal Tooling

### 4.1 What AI Foundry Is for Spaarke

AI Foundry is **infrastructure that powers our product** — not an end-user tool, not a development environment, not a replacement for our BFF architecture.

The distinction matters because Microsoft positions AI Foundry alongside Copilot Studio and Power Automate in their marketing. For Spaarke:

| Microsoft Service | Spaarke Usage | Why |
|-------------------|---------------|-----|
| **Copilot Studio** | NOT USED | It's a Microsoft product your customers would interact with directly. Cannot be white-labeled, embedded in your product, or deployed to customer tenants without Microsoft licensing per user. |
| **Power Automate** | NOT USED | Internal automation tool. Flows run in Microsoft's infrastructure, not yours. Cannot package as part of your product. Requires per-user licensing. |
| **AI Foundry** | USED as infrastructure | Foundry is Azure infrastructure (like a database or search service). You deploy it in your subscription (or customer's subscription), you call its APIs from your code, and your customers never see or interact with it directly. |

### 4.2 AI Foundry's Three Roles

#### Role A: Evaluation & Quality Pipeline (Immediate — Phase 1)

This is the highest-value near-term use. Without measurement, every change to chunking, retrieval, or prompts is a shot in the dark.

```
Production Path (serves users)           Evaluation Path (serves developers)
┌──────────────────────────────┐        ┌───────────────────────────────────┐
│  Sprk.Bff.Api                │        │  AI Foundry Project               │
│  ┌────────────────────────┐  │        │  ┌─────────────────────────────┐  │
│  │ OpenAiClient           │  │  same  │  │ Prompt Flow: analysis-exec  │  │
│  │ RagService             │──┼─Azure──┼──│ Prompt Flow: analysis-cont  │  │
│  │ TextExtractorService   │  │ OpenAI │  │                             │  │
│  │                        │  │  + AI  │  │ Evaluation Runs:            │  │
│  │ (handles user requests)│  │ Search │  │  gold-datasets/*.jsonl      │  │
│  └────────────────────────┘  │        │  │  metrics/format_compliance  │  │
│                              │        │  │  metrics/completeness       │  │
│                              │        │  │  metrics/citation_accuracy  │  │
│                              │        │  │  metrics/clause_coverage    │  │
│                              │        │  │                             │  │
│                              │        │  │ Baseline Comparisons:       │  │
│                              │        │  │  v1.0-baseline.json         │  │
│                              │        │  │  v2.0-post-chunking.json    │  │
│                              │        │  │  v3.0-post-enrichment.json  │  │
│                              │        │  │                             │  │
│                              │        │  │ (measures quality)          │  │
│                              │        │  └─────────────────────────────┘  │
└──────────────────────────────┘        └───────────────────────────────────┘
```

**What we build:**
- Gold datasets for top 3 use cases: NDA review, contract analysis, document summary
- Custom evaluation metrics: `format_compliance.py`, `completeness.py`, `citation_accuracy.py`, `clause_coverage.py`
- Baseline snapshots before and after each improvement (chunking, metadata enrichment, query preprocessing)
- Automated regression testing: run evaluation suite before each release

**Deployment model impact:**
- **Model 1**: Single AI Foundry Project in Spaarke's tenant runs all evaluations
- **Model 2**: Customer can optionally deploy AI Foundry (`enableAiFoundry=true` in model2-full.bicep). If deployed, customer can run evaluations against their own data. If not deployed, Spaarke provides evaluation baselines from Model 1 testing.

#### Role B: Prompt Flow as Managed Endpoints (Phase 2+)

Once Prompt Flows are validated through evaluation, deploy them as managed inference endpoints. The BFF calls the Prompt Flow endpoint instead of making raw OpenAI calls for complex analysis workflows.

**Benefits:**
- **Versioned prompt management** — Update prompt templates, system instructions, and flow logic without redeploying the BFF API. Ship prompt improvements independently of code releases.
- **Token tracking per flow** — Built-in per-invocation token usage reporting. Enables accurate per-customer billing in Model 1.
- **A/B testing** — Route percentage of traffic to new flow version, compare quality metrics.
- **Separation of concerns** — Prompt engineering (flow definitions) decoupled from application engineering (BFF code).

```csharp
// Current: BFF calls OpenAI directly
var result = await _openAiClient.GetCompletionAsync(systemPrompt, userPrompt, options);

// Future: BFF calls Prompt Flow endpoint for complex workflows
var result = await _promptFlowClient.InvokeAsync("analysis-execute", new {
    document_text = extractedText,
    action_system_prompt = playbook.SystemPrompt,
    skills_instructions = resolvedSkills,
    knowledge_context = ragContext,
    output_format = "structured_json"
});
// Simple calls (chat, quick extraction) continue to use OpenAI directly
```

**Deployment model impact:**
- **Model 1**: Prompt Flows deployed in Spaarke's AI Foundry Project. BFF calls Spaarke's Prompt Flow endpoint. All customers share the same flows.
- **Model 2**: If customer has AI Foundry enabled, Prompt Flows are deployed in their Project. BFF calls their local endpoint. **Customer can customize prompt templates** — this is a key product differentiator vs. competitors. If AI Foundry is not enabled, BFF falls back to direct OpenAI calls (current behavior), and prompts are managed in code.

**Configuration pattern:**
```json
// Model 1 (Spaarke-hosted): BFF app settings
{
  "Ai__UsePromptFlows": true,
  "Ai__PromptFlowEndpoint": "https://sprksharedprod-aif-proj.westus2.inference.ml.azure.com",
  "Ai__PromptFlowKey": "@Microsoft.KeyVault(VaultName=sprksharedprod-kv;SecretName=promptflow-key)"
}

// Model 2 (customer-hosted): BFF app settings
{
  "Ai__UsePromptFlows": true,  // or false if customer doesn't deploy AI Foundry
  "Ai__PromptFlowEndpoint": "https://sprkcustprod-aif-proj.eastus.inference.ml.azure.com",
  "Ai__PromptFlowKey": "@Microsoft.KeyVault(VaultName=sprkcustprod-kv;SecretName=promptflow-key)"
}
```

#### Role C: Tracing & Observability (Phase 3+, with Agent Framework)

When multi-agent workflows execute, debugging "why did the agent produce this output?" requires seeing the full execution chain: what was retrieved, what was sent to the model, what tool was called, what the model returned, what happened next.

AI Foundry provides distributed tracing for AI operations that integrates with Application Insights:

```
User: "Review this NDA"
  └─ Orchestrator Agent
       ├─ Intake Agent
       │    ├─ TextExtractorService (Doc Intelligence Layout) → 2.3s
       │    ├─ DocumentClassifier (gpt-4o) → 1.1s, 450 tokens
       │    └─ MetadataEnrichment (gpt-4o-mini) → 0.8s, 320 tokens
       ├─ Review Agent
       │    ├─ RagService.SearchAsync("indemnification clause") → 0.4s, 12 results
       │    ├─ ClauseAnalysis (gpt-4o) → 3.2s, 2100 tokens
       │    ├─ RagService.SearchAsync("limitation of liability") → 0.3s, 8 results
       │    └─ DeviationScoring (gpt-4o) → 1.8s, 1400 tokens
       ├─ Drafting Agent
       │    ├─ RedlineSuggestion (gpt-4o) → 2.1s, 1800 tokens
       │    └─ NegotiationSummary (gpt-4o-mini) → 1.0s, 600 tokens
       └─ Quality Agent
            ├─ CitationValidation (rule engine) → 0.1s
            └─ OutputSchemaValidation (rule engine) → 0.05s

Total: 13.2s, 6670 tokens, $0.034 cost
```

**Deployment model impact:**
- **Model 1**: Traces flow to Spaarke's Application Insights. Spaarke engineering uses traces for debugging and optimization. Aggregate metrics (not raw traces) exposed to customers in dashboards.
- **Model 2**: Traces flow to customer's Application Insights. Customer has full visibility into AI operations in their tenant. Enables customer IT teams to monitor, audit, and optimize independently.

### 4.3 AI Foundry Deployment Model Matrix

| AI Foundry Capability | Model 1 (Spaarke-Hosted) | Model 2 (Customer-Hosted, AI Foundry enabled) | Model 2 (Customer-Hosted, AI Foundry disabled) |
|----------------------|--------------------------|----------------------------------------------|-----------------------------------------------|
| **Evaluation pipeline** | Spaarke runs; results inform product quality | Customer can run against their data | Not available; Spaarke provides baselines |
| **Prompt Flow endpoints** | Spaarke's endpoint; shared across customers | Customer's endpoint; customizable prompts | BFF calls OpenAI directly; prompts in code |
| **Tracing** | Spaarke's App Insights | Customer's App Insights | Standard App Insights only (no AI chain tracing) |
| **Model experiments** | Spaarke tests new models centrally | Customer can experiment with models | Customer manages OpenAI deployments via Azure Portal |
| **Custom metric scripts** | Maintained by Spaarke | Deployed with Prompt Flows; customer can extend | Not applicable |

---

## 5. Semantic Kernel & Agent Framework: Product Architecture

### 5.1 Why Semantic Kernel (Not Copilot Studio, Not Raw SDK)

The choice of orchestration layer is a critical architectural decision. Here's why Semantic Kernel is the right answer for a product company:

| Dimension | Raw Azure.AI.OpenAI SDK (current) | Copilot Studio | Semantic Kernel + Agent Framework |
|-----------|-----------------------------------|----------------|-----------------------------------|
| **Runtime** | Runs in our BFF process | Microsoft-hosted cloud service | Runs in our BFF process |
| **Packaging** | Our code | Microsoft product; cannot redistribute | NuGet library; ships as part of our product |
| **Multi-tenant** | Our code handles isolation | Per-tenant Microsoft licensing | Our code handles isolation |
| **Customization** | Full control but re-inventing the wheel | Low-code designer; limited | Full .NET code with production patterns |
| **Multi-agent** | Must build from scratch (see `BuilderAgentService`) | Basic orchestration only | Built-in patterns: Sequential, Concurrent, Handoff, Group Chat |
| **Agent memory** | Must build | Microsoft-managed | Pluggable: Redis, Dataverse, custom |
| **Tool dispatch** | Manual function-calling loop | Connector-based | Automatic with KernelFunction dispatch |
| **Tracing** | Manual OTel instrumentation | Microsoft dashboard | Built-in OpenTelemetry; integrates with App Insights |
| **Customer billing** | Manual token counting | Per-user Microsoft license cost passed to customer | Built-in token tracking; we control billing model |
| **Cost to customer** | Azure API consumption only | Microsoft license + Azure consumption | Azure API consumption only |
| **Brand** | Spaarke | "Built with Copilot" branding | Spaarke (customers never see SK) |

**Bottom line**: Semantic Kernel gives us the same programming model as Copilot Studio's internals, running inside our own process, packaged as our product, at our price point.

### 5.2 How Semantic Kernel Fits the Existing Architecture

Semantic Kernel is an **in-process library** (NuGet package). It does not change the BFF architecture. It replaces the hand-rolled agent loop with a production-grade orchestration framework.

```
BEFORE (current)                          AFTER (with Semantic Kernel)
┌─────────────────────────┐              ┌──────────────────────────────┐
│ AnalysisEndpoints.cs    │              │ AnalysisEndpoints.cs         │
│         │               │              │         │                    │
│         ▼               │              │         ▼                    │
│ AnalysisOrchestration   │              │ Semantic Kernel              │
│   Service.cs            │              │   ┌──────────────────┐       │
│   (manual orchestration)│              │   │ ReviewAgent      │       │
│         │               │              │   │   plugins:       │       │
│    ┌────┼────┐          │              │   │   - ClauseAnalyze│       │
│    ▼    ▼    ▼          │              │   │   - RiskDetect   │       │
│ OpenAi Rag  Text        │              │   │   - RagSearch    │       │
│ Client Svc  Extract     │              │   │   - Deviation    │       │
│                         │              │   └────────┬─────────┘       │
│                         │              │            │ auto-dispatches  │
│                         │              │       ┌────┼────┐            │
│                         │              │       ▼    ▼    ▼            │
│                         │              │    OpenAi Rag  Text          │
│                         │              │    Client Svc  Extract       │
│                         │              │    (SAME existing services)  │
└─────────────────────────┘              └──────────────────────────────┘
```

**Key points:**
- Existing services (`OpenAiClient`, `RagService`, `SpeFileStore`, `DataverseClient`) become Kernel Plugins
- Existing tool handlers (`ClauseAnalyzerHandler`, `RiskDetectorHandler`, etc.) become KernelFunctions
- Existing SSE streaming pattern stays the same
- Existing endpoint filter authorization stays the same
- ADR-001 (Minimal API), ADR-008 (endpoint filters), ADR-010 (DI minimalism) all hold

### 5.3 Plugin Architecture: Wrapping Existing Services

Semantic Kernel's plugin system maps directly to Spaarke's existing service architecture:

```
Spaarke Service              →  Semantic Kernel Plugin
─────────────────────────────────────────────────────────
RagService                   →  RetrievalPlugin
  .SearchAsync()                 [KernelFunction("search_knowledge")]
  .IndexDocumentAsync()          [KernelFunction("index_document")]

SpeFileStore                 →  DocumentPlugin
  .DownloadFileAsUserAsync()     [KernelFunction("get_document")]
  .GetFileMetadataAsync()        [KernelFunction("get_metadata")]

DataverseClient              →  DataversePlugin
  .CreateRecordAsync()           [KernelFunction("create_record")]
  .UpdateRecordAsync()           [KernelFunction("update_record")]
  .QueryAsync()                  [KernelFunction("query_records")]

ClauseAnalyzerHandler        →  AnalysisPlugin
RiskDetectorHandler               [KernelFunction("analyze_clause")]
EntityExtractorHandler             [KernelFunction("detect_risks")]
DocumentClassifierHandler          [KernelFunction("extract_entities")]
                                   [KernelFunction("classify_document")]

SemanticSearchService        →  SearchPlugin
  .SearchAsync()                 [KernelFunction("semantic_search")]

RedlineGenerationService     →  DraftingPlugin (NEW)
  .GenerateRedlineAsync()        [KernelFunction("suggest_redline")]
  .GenerateAlternativeAsync()    [KernelFunction("suggest_alternative")]
```

The agent decides which functions to call based on its instructions and the current task. This is what Ivo achieves with 400 specialized agents — but implemented as composable plugins rather than monolithic agents.

### 5.4 Multi-Agent Patterns for Legal Workflows

The Microsoft Agent Framework (Semantic Kernel's multi-agent extension) provides pre-built orchestration patterns:

| Pattern | Legal Use Case | How It Works |
|---------|---------------|--------------|
| **Sequential** | Full contract review | Intake → Review → Draft → Quality. Each agent's output is the next agent's input. |
| **Concurrent** | Multi-clause analysis | 5 clauses analyzed simultaneously by 5 instances of the Review Agent. Results merged. |
| **Handoff** | Escalation | Review Agent detects high-risk clause → hands off to Senior Review Agent with elevated model (gpt-4o vs. mini). |
| **Group Chat** | Complex negotiation analysis | Review Agent + Drafting Agent + Research Agent collaborate on complex clause with competing positions. |

**Example: Full Contract Review (Sequential + Concurrent hybrid)**

```
┌─────────────────────────────────────────────────────────────────────┐
│  Playbook: "Full Contract Review"                                    │
│                                                                      │
│  Phase 1: INTAKE (Sequential)                                        │
│  ┌────────────────────────────────────────────────────────┐         │
│  │ Intake Agent                                            │         │
│  │  1. TextExtractorService (Layout model)                 │         │
│  │  2. DocumentClassifier → "NDA"                          │         │
│  │  3. MetadataEnrichment → {parties, jurisdiction, dates} │         │
│  │  4. Structure extraction → [Section 1, Section 2, ...]  │         │
│  │  Output: StructuredDocument                             │         │
│  └────────────────────┬───────────────────────────────────┘         │
│                       │                                              │
│  Phase 2: REVIEW (Concurrent — one per section)                      │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────┐       │
│  │ Review §1  │ │ Review §2  │ │ Review §3  │ │ Review §N  │       │
│  │            │ │            │ │            │ │            │       │
│  │ • Clause   │ │ • Clause   │ │ • Clause   │ │ • Clause   │       │
│  │   analysis │ │   analysis │ │   analysis │ │   analysis │       │
│  │ • RAG      │ │ • RAG      │ │ • RAG      │ │ • RAG      │       │
│  │   precedent│ │   precedent│ │   precedent│ │   precedent│       │
│  │ • Deviation│ │ • Deviation│ │ • Deviation│ │ • Deviation│       │
│  │   scoring  │ │   scoring  │ │   scoring  │ │   scoring  │       │
│  └─────┬──────┘ └─────┬──────┘ └─────┬──────┘ └─────┬──────┘       │
│        └──────────────┬┴──────────────┘              │              │
│                       │ merge                        │              │
│  Phase 3: DRAFT (Sequential)                                         │
│  ┌────────────────────▼───────────────────────────────┐             │
│  │ Drafting Agent                                      │             │
│  │  • Generate redline suggestions for flagged clauses │             │
│  │  • Propose alternative language per risk level      │             │
│  │  • Create negotiation position summary              │             │
│  │  Output: RedlineSuggestion[] + narrative            │             │
│  └────────────────────┬───────────────────────────────┘             │
│                       │                                              │
│  Phase 4: QUALITY (Sequential)                                       │
│  ┌────────────────────▼───────────────────────────────┐             │
│  │ Quality Agent                                       │             │
│  │  • Validate all citations reference actual document │             │
│  │  • Check output schema compliance                   │             │
│  │  • Verify no hallucinated clause references         │             │
│  │  • Score overall confidence                         │             │
│  │  Output: QualityReport + validated ContractReview   │             │
│  └────────────────────────────────────────────────────┘             │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.5 Deployment Model Impact: Semantic Kernel

Semantic Kernel is a NuGet library inside the BFF. This means:

| Aspect | Model 1 (Spaarke-Hosted) | Model 2 (Customer-Hosted) |
|--------|--------------------------|---------------------------|
| **Where it runs** | Spaarke's App Service | Customer's App Service |
| **Which OpenAI it calls** | Spaarke's Azure OpenAI (shared) | Customer's Azure OpenAI (dedicated) |
| **Which AI Search it queries** | Spaarke's AI Search (shared or dedicated index) | Customer's AI Search (dedicated service) |
| **Agent configuration** | Spaarke-managed; same for all customers (customized via playbooks) | Same codebase; customer controls model selection and TPM via their Azure portal |
| **Plugin availability** | All plugins available | All plugins available (same BFF binary) |
| **Custom plugins** | Not customer-configurable (Spaarke develops) | Not customer-configurable (same binary). Customization is via playbooks and configuration, not code. |
| **Cost** | Spaarke's Azure consumption; metered/bundled to customer | Customer's Azure consumption; customer pays directly |

**Key insight**: Agent behavior is customized through **playbooks and configuration** (stored in Dataverse), not through code changes. Both deployment models run the same BFF binary. The difference is which Azure resources it points to.

### 5.6 Agent Service (Azure AI Foundry) — Evaluation for Future Use

Azure AI Agent Service is a managed service in AI Foundry that hosts agents with persistent state, file search, and code interpreter. It went GA in May 2025.

**Why we're NOT adopting it now:**

| Agent Service Feature | Spaarke Equivalent | Assessment |
|----------------------|-------------------|------------|
| Persistent threads | Dataverse entities + Redis | We already have state management tailored to our data model |
| File search | RagService + AI Search | Our retrieval pipeline is more sophisticated (clause-aware, metadata-enriched) |
| Code interpreter | Not needed for legal AI | Legal workflows don't require runtime code execution |
| Managed hosting | BFF App Service | We control the hosting; don't need Microsoft to manage agent runtime |

**When we would adopt it**: If we build a "chat with your portfolio" feature where users maintain long-running (days/weeks) conversations with their entire contract corpus, Agent Service's managed thread persistence becomes attractive. This is Phase 5 territory.

**Deployment model consideration**: Agent Service would require AI Foundry in both Model 1 and Model 2, making it a mandatory dependency rather than optional. This increases Model 2 deployment complexity and cost.

---

## 6. Two-Tier Deployment Architecture for AI Platform

### 6.1 Current Deployment Models

#### Model 1: Spaarke-Hosted SaaS (Multi-Tenant)

All Azure resources in Spaarke's subscription. Customers are guests in Spaarke's Entra ID tenant. Logical isolation via per-customer AI Search indexes and `tenantId` OData filters.

```
Spaarke's Azure Subscription
┌─────────────────────────────────────────────────────────────────────┐
│  Resource Group: rg-spaarke-shared-{env}                             │
│                                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │ App Service   │  │ Azure OpenAI │  │ AI Search    │              │
│  │ (shared BFF)  │  │ (shared)     │  │ (shared svc) │              │
│  │ MULTI_TENANT  │  │ gpt-4o: 150K │  │              │              │
│  │ =true         │  │ embed: 350K  │  │ Index: cust-A│              │
│  └──────────────┘  └──────────────┘  │ Index: cust-B│              │
│                                       │ Index: cust-C│              │
│  ┌──────────────┐  ┌──────────────┐  └──────────────┘              │
│  │ Doc Intel    │  │ AI Foundry   │                                 │
│  │ (shared)     │  │ Hub + Project│  ┌──────────────┐              │
│  └──────────────┘  │ (eval+flows) │  │ Key Vault    │              │
│                     └──────────────┘  │ (shared,     │              │
│  ┌──────────────┐  ┌──────────────┐  │  namespaced) │              │
│  │ Redis        │  │ Service Bus  │  └──────────────┘              │
│  │ (shared,     │  │ (shared)     │                                 │
│  │  prefixed)   │  └──────────────┘                                 │
│  └──────────────┘                                                    │
└─────────────────────────────────────────────────────────────────────┘
```

#### Model 2: Customer-Hosted (Dedicated Tenant)

All Azure resources in customer's subscription. Customer's native Entra ID users. Physical isolation — complete data sovereignty.

```
Customer's Azure Subscription
┌─────────────────────────────────────────────────────────────────────┐
│  Resource Group: rg-spaarke-{customerId}-{env}                       │
│                                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │ App Service   │  │ Azure OpenAI │  │ AI Search    │              │
│  │ (dedicated)   │  │ (dedicated)  │  │ (dedicated)  │              │
│  │               │  │ gpt-4o: 80K  │  │              │              │
│  │               │  │ embed: 200K  │  │ Single index │              │
│  └──────────────┘  └──────────────┘  └──────────────┘              │
│                                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │ Doc Intel    │  │ AI Foundry   │  │ Key Vault    │              │
│  │ (dedicated)  │  │ (optional)   │  │ (dedicated)  │              │
│  └──────────────┘  │ Hub+Project  │  └──────────────┘              │
│                     │ if enabled   │                                 │
│  ┌──────────────┐  └──────────────┘  ┌──────────────┐              │
│  │ Redis        │  ┌──────────────┐  │ Storage      │              │
│  │ (dedicated)  │  │ Service Bus  │  │ (dedicated)  │              │
│  └──────────────┘  │ (dedicated)  │  └──────────────┘              │
│                     └──────────────┘                                 │
└─────────────────────────────────────────────────────────────────────┘
```

### 6.2 New Components: Deployment Model Impact

Each new AI platform component must work in both deployment models. Here's the complete mapping:

| New Component | Model 1 (Spaarke-Hosted) | Model 2 (Customer-Hosted) | Implementation Strategy |
|--------------|--------------------------|---------------------------|-------------------------|
| **Semantic Kernel** | In BFF process; calls Spaarke's OpenAI | In BFF process; calls customer's OpenAI | NuGet library in BFF. Endpoint configured via app settings. No deployment difference. |
| **Agent Framework** | In BFF process; same as SK | In BFF process; same as SK | Same binary, different config. |
| **Clause-aware chunking** | BFF service; calls Spaarke's Doc Intelligence | BFF service; calls customer's Doc Intelligence | `TextExtractorService` enhancement. Endpoint configured via app settings. |
| **Index schema v3** | Deploy to Spaarke's AI Search; per-customer indexes | Deploy to customer's AI Search; single index | Separate deployment script. Model 1: run once, apply to all new indexes. Model 2: run during customer deployment. |
| **Synonym maps** | Deploy to Spaarke's AI Search | Deploy to customer's AI Search | JSON config; deployed via script alongside index schema. |
| **Evaluation pipeline** | Spaarke's AI Foundry Project | Customer's AI Foundry Project (if enabled) | Prompt Flows + eval scripts packaged for deployment. Model 2: included in deployment package when `enableAiFoundry=true`. |
| **Prompt Flow endpoints** | Spaarke deploys and manages | Customer deploys (Spaarke provides package) | Flow definitions shipped as deployment artifacts. Model 2 customer runs deployment script. |
| **Knowledge packs** | Indexed in Spaarke's AI Search, per-customer index | Indexed in customer's AI Search | Content shipped as data package. Indexing runs through same `FileIndexingService`. |
| **Seed data** | Created in Spaarke's shared Dataverse | Created in customer's Dataverse | Dataverse solution package. Imported via `pac solution import`. |
| **Word add-in AI features** | Calls Spaarke's BFF endpoint | Calls customer's BFF endpoint | Add-in manifest configured per deployment. Same code, different API URL. |
| **Scoring profiles** | Configured on Spaarke's AI Search | Configured on customer's AI Search | Included in index schema definition. |
| **AI tracing** | Spaarke's App Insights | Customer's App Insights | OpenTelemetry configuration. Endpoint from app settings. |

### 6.3 Configuration Abstraction Layer

The BFF must be deployment-model-agnostic. All AI infrastructure differences are resolved through configuration, not code:

```csharp
// Configuration pattern — same code path, different endpoints
public class AiPlatformOptions
{
    // Core AI services (resolved from app settings / Key Vault)
    public string OpenAiEndpoint { get; set; }           // Model 1: Spaarke's; Model 2: Customer's
    public string AiSearchEndpoint { get; set; }         // Model 1: Spaarke's; Model 2: Customer's
    public string DocIntelEndpoint { get; set; }         // Model 1: Spaarke's; Model 2: Customer's

    // AI Foundry (optional in Model 2)
    public bool UsePromptFlows { get; set; }             // true if AI Foundry active
    public string PromptFlowEndpoint { get; set; }       // AI Foundry inference endpoint
    public string PromptFlowKey { get; set; }            // Key Vault reference

    // RAG deployment model
    public RagDeploymentModel DefaultRagModel { get; set; }  // Shared | Dedicated | CustomerOwned

    // Model selection
    public string PrimaryModel { get; set; } = "gpt-4o";        // Agent reasoning
    public string SecondaryModel { get; set; } = "gpt-4o-mini"; // Simple extraction
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";

    // Feature flags for gradual rollout
    public bool EnableAgentWorkflows { get; set; }       // Phase 3 gate
    public bool EnableClauseAwareChunking { get; set; }  // Phase 1 gate
    public bool EnableMetadataEnrichment { get; set; }   // Phase 1 gate
    public bool EnableRedlineGeneration { get; set; }    // Phase 2 gate
}
```

### 6.4 Bicep Infrastructure Updates

#### Model 1 Additions

```bicep
// Add to model1-shared.bicep

// AI Foundry (always enabled for Model 1)
module aiFoundry '../modules/ai-foundry-hub.bicep' = {
  scope: rg
  name: 'aiFoundry-shared'
  params: {
    hubName: '${baseName}-aif-hub'
    projectName: '${baseName}-aif-proj'
    // ... existing params
  }
}

// Synonym map deployment (post-provisioning script)
// index schema v3 deployment (post-provisioning script)
// Prompt Flow deployment (post-provisioning script)
```

#### Model 2 Additions

```bicep
// model2-full.bicep already has enableAiFoundry param
// No new Azure resources required — SK runs in existing App Service
// New deployment scripts needed:
//   - deploy-index-v3.sh (index schema + synonym maps + scoring profiles)
//   - deploy-prompt-flows.sh (if enableAiFoundry=true)
//   - deploy-seed-data.sh (Dataverse solution import)
//   - deploy-knowledge-packs.sh (index content)
```

### 6.5 Customer Control Matrix (Model 2)

What Model 2 customers can configure vs. what is Spaarke-managed code:

| Capability | Customer Controls | Spaarke Controls |
|-----------|-------------------|-------------------|
| **Model selection** | Azure Portal: add/change model deployments, adjust TPM | BFF code determines which deployment name to use |
| **Content filtering** | Azure Portal: configure OpenAI content filters | BFF code sends requests; filters are applied at Azure level |
| **Playbooks** | Dataverse: create/edit playbooks, skills, actions, knowledge sources | Playbook execution engine logic |
| **Prompt templates** | AI Foundry (if enabled): edit Prompt Flow templates | Flow orchestration logic |
| **Index configuration** | Azure Portal: view index, adjust replicas/partitions | Index schema definition; chunking/enrichment logic |
| **Evaluation** | AI Foundry (if enabled): run evals, add gold datasets | Evaluation metric definitions |
| **Monitoring** | Azure Portal: App Insights dashboards, alerts | OpenTelemetry instrumentation |
| **Agent behavior** | Configuration via Dataverse (playbooks, thresholds) | Agent orchestration code |
| **Knowledge packs** | Add/remove documents; Spaarke provides base packs | Indexing pipeline, chunk strategy |
| **Billing** | Azure cost management (direct Azure billing) | N/A — no Spaarke billing for Model 2 AI |

---

## 7. ADR Impact and New ADR Requirements

### 7.1 Existing ADR Assessment

| ADR | Status | Impact |
|-----|--------|--------|
| ADR-001 (Minimal API + BackgroundService) | **No change** | SK runs in-process. No new services. |
| ADR-007 (SpeFileStore facade) | **No change** | SK plugins call SpeFileStore. |
| ADR-008 (Endpoint filters) | **No change** | New agent endpoints use same filter pattern. |
| ADR-009 (Redis-first caching) | **No change** | Agent intermediate results cached in Redis. |
| ADR-010 (DI minimalism ≤15) | **Monitor** | Kernel registration is 1-2 entries. Plugins are options, not DI entries. Audit after implementation. |
| ADR-013 (AI Architecture) | **Needs amendment** | Add Semantic Kernel as orchestration layer. BFF remains single access point. |
| ADR-014 (AI Caching — Proposed) | **Accept and extend** | Add caching for agent intermediate results, evaluation baselines. |
| ADR-016 (Rate Limits — Proposed) | **Accept and extend** | Agent workflows multiply LLM calls; rate limiting must account for multi-call patterns. |

### 7.2 New ADRs Required

| New ADR | Purpose | Key Decisions |
|---------|---------|---------------|
| **ADR-017: Agent Orchestration Strategy** | Documents adoption of Semantic Kernel + Agent Framework over Copilot Studio, raw SDK, or third-party frameworks | Why SK; plugin architecture; multi-agent patterns; deployment model implications |
| **ADR-018: Evaluation & Quality Pipeline** | Documents AI Foundry evaluation approach, gold dataset management, regression testing | Metric definitions; baseline management; release gates |
| **ADR-019: Legal Knowledge Architecture** | Documents knowledge pack structure, taxonomy approach, client overlay model | Clause taxonomy schema; how knowledge packs are versioned and deployed |

---

## 8. Phased Implementation Plan

### Overview

The roadmap is organized into five phases. Each phase is scoped as an independent project that can be turned into its own `design.md` and `spec.md` via the `/design-to-spec` → `/project-pipeline` workflow.

```
Phase 1                 Phase 2                Phase 3
Retrieval &             Word AI &              Agentic
Evaluation              Redlining              Orchestration
(8-10 weeks)            (6-8 weeks)            (8-10 weeks)
     │                      │                      │
     ▼                      ▼                      ▼
┌─────────┐           ┌─────────┐           ┌─────────┐
│ Clause-  │           │ Word    │           │ Semantic │
│ aware    │           │ add-in  │           │ Kernel   │
│ chunking │           │ AI      │           │ agents   │
│          │           │ panel   │           │          │
│ Metadata │           │         │           │ Multi-   │
│ enrich   │           │ Redline │           │ agent    │
│          │           │ gen     │           │ review   │
│ Eval     │           │         │           │          │
│ pipeline │           │ Chat    │           │ Human-   │
│          │           │ context │           │ in-loop  │
│ Seed     │           │         │           │          │
│ data     │           │ Export  │           │ Agent    │
│          │           │         │           │ tracing  │
│ Index v3 │           │         │           │          │
└─────────┘           └─────────┘           └─────────┘
     │                      │                      │
     └──────────────────────┴──────────────────────┘
                            │
                 Phase 4                Phase 5
                 Client Config &        Operations
                 Intelligence           Intelligence
                 (6-8 weeks)            (8-10 weeks)
                      │                      │
                 ┌─────────┐           ┌─────────┐
                 │ Client   │           │ Matter  │
                 │ overlay  │           │ lifecycle│
                 │ system   │           │ agents  │
                 │          │           │          │
                 │ Playbook │           │ Compliance│
                 │ custom   │           │ workflows│
                 │          │           │          │
                 │ Portfolio │           │ Cross-doc│
                 │ analytics│           │ intel    │
                 │          │           │          │
                 │ Per-client│           │ Billing  │
                 │ eval     │           │ intel    │
                 └─────────┘           └─────────┘
```

Dependencies:
- Phase 2 depends on Phase 1 (needs enriched retrieval for comparison/redlining)
- Phase 3 depends on Phase 1 (needs evaluation harness to measure agent quality)
- Phase 4 depends on Phase 1 + Phase 3 (needs enrichment + agent framework)
- Phase 5 depends on Phase 3 (needs agent orchestration)
- Phase 2 and Phase 3 can run in parallel after Phase 1

---

### Phase 1: Retrieval Excellence & Evaluation Foundation

**Duration**: 8-10 weeks
**Branch**: `work/ai-retrieval-excellence-r1`
**Prerequisites**: None (builds on existing infrastructure)

#### 1.1 Objective

Transform the retrieval pipeline from generic text search to legal-domain-optimized retrieval with measurable quality. Establish the evaluation infrastructure that all future phases depend on. Activate the dormant scope system with seed data.

#### 1.2 Scope

**New Azure service features used:**
- Document Intelligence Layout model (structure extraction)
- AI Search synonym maps, scoring profiles, enriched index schema v3
- AI Foundry evaluation pipeline (Prompt Flows, metrics, gold datasets)

**New NuGet packages:** None (Phase 1 uses existing SDKs)

**Existing services modified:**
- `TextExtractorService` — Switch from Read to Layout model; return structured output
- `TextChunkingService` — Replace with `StructureAwareChunkingService` using Layout model output
- `FileIndexingService` — Add metadata enrichment step
- `RagService` — Update for index v3 schema
- `SemanticSearchService` — Implement query preprocessing; update for new fields
- `KnowledgeDeploymentService` — Complete Dataverse persistence (currently in-memory)

**New services:**
- `StructureAwareChunkingService` — Clause/section-aware chunking using document structure
- `MetadataEnrichmentService` — Extract clause category, jurisdiction, parties, contract type
- `QueryPreprocessorService` — Replace NoOp with legal synonym expansion and intent classification
- `EvaluationService` — BFF-side evaluation runner (calls AI Foundry evaluation API)

**Infrastructure changes:**
- AI Search index v3 schema (additive fields: `clauseCategory`, `jurisdictionCode`, `contractType`, `parties`, `sectionPath`, `governingLaw`)
- AI Search synonym maps (legal terminology)
- AI Search scoring profiles (recency boost, clause category boost)
- AI Foundry Prompt Flow deployment (analysis-execute, analysis-continue)
- AI Foundry evaluation metrics (format_compliance.py, completeness.py, citation_accuracy.py, clause_coverage.py)
- Gold datasets for NDA review, contract analysis, document summary

**Dataverse changes:**
- Seed data: 5 Actions, 10 Skills, 5 Knowledge Sources, 5 Playbooks (ENH-009 through ENH-011)
- `sprk_aiknowledgedeployment` entity persistence (complete the TODO in KnowledgeDeploymentService)

#### 1.3 Deployment Model Considerations

| Component | Model 1 | Model 2 |
|-----------|---------|---------|
| Index v3 schema | Deploy to shared AI Search; migrate existing per-customer indexes | Include in deployment scripts; new index for new customers |
| Synonym maps | Upload to shared AI Search | Include in deployment scripts |
| Doc Intelligence Layout | Use shared Doc Intelligence (same endpoint, different API call) | Use customer's Doc Intelligence (same code, different endpoint) |
| AI Foundry evaluation | Deploy to Spaarke's AI Foundry Project | Deploy if `enableAiFoundry=true`; otherwise N/A |
| Seed data | Import Dataverse solution to shared environment | Include in customer deployment Dataverse solution |

#### 1.4 Success Criteria

- Retrieval recall@10 improves by ≥20% on gold dataset vs. current baseline
- Clause-aware chunking eliminates cross-clause splits (measured on 50 sample legal docs)
- Evaluation pipeline runs automatically; produces baseline comparison report
- All 5 sample playbooks execute end-to-end (ENH-012)
- Index v3 migration is backward-compatible (no existing functionality broken)

#### 1.5 Estimated Task Breakdown

| Area | Estimated Tasks | Key Components |
|------|----------------|----------------|
| Structure-aware extraction | 8-10 | Layout model integration, structure parser, section hierarchy builder |
| Clause-aware chunking | 6-8 | Chunking strategy, overlap handling, section boundary preservation |
| Metadata enrichment | 6-8 | Extraction pipeline, clause categorization, jurisdiction detection |
| Index v3 + synonym maps | 5-7 | Schema definition, migration scripts, scoring profiles, synonym maps |
| Query preprocessing | 4-6 | Synonym expansion, intent classification, entity detection in queries |
| Evaluation pipeline | 8-10 | Gold datasets, metric scripts, baseline snapshots, Prompt Flow deployment, regression runner |
| Seed data | 6-8 | Actions, Skills, Knowledge, Playbooks in Dataverse; content indexing |
| KnowledgeDeployment persistence | 3-4 | Dataverse entity CRUD, cache invalidation |
| Integration testing | 5-7 | End-to-end playbook tests, retrieval quality tests, deployment model tests |
| **Total** | **~55-70 tasks** | |

#### 1.6 ADR Requirements

- ADR-013 amendment: Document Layout model adoption (not a new ADR — extends existing)
- ADR-018: Evaluation & Quality Pipeline (new)

---

### Phase 2: Word Add-in AI & Redlining

**Duration**: 6-8 weeks
**Branch**: `work/ai-word-addin-r1`
**Prerequisites**: Phase 1 (enriched retrieval for clause comparison and redline suggestions)

#### 2.1 Objective

Deliver an in-document AI experience in Microsoft Word that matches Spellbook's core value proposition: clause-by-clause analysis, redline suggestions, and chat-with-document — all connected to the Spaarke playbook engine and knowledge base.

#### 2.2 Scope

**Existing client framework used:**
- Word add-in (`src/client/office-addins/word/`) — WordAdapter, manifest, task pane shell
- Shared add-in infrastructure — SSE client, auth (NAA + Dialog), entity picker, theme hooks

**New BFF API endpoints:**
- `/api/ai/word/analyze` — Analyze document content (clause-by-clause) using playbook, return structured results
- `/api/ai/word/redline` — Generate redline suggestion for specific clause (returns Track Changes XML)
- `/api/ai/word/chat` — Chat with document or analysis context (SSE streaming)
- `/api/ai/word/suggest-alternative` — Generate alternative clause language at specified risk level

**New BFF services:**
- `RedlineGenerationService` — Generate Word-compatible tracked changes for clause modifications
- `ClauseLocationService` — Map analysis findings to specific document locations (page, paragraph, character offset)
- `WordDocumentService` — Word OOXML manipulation (insert tracked changes, comments)

**Word add-in components:**
- AI Analysis Panel — Sidebar showing clause-by-clause findings, risk indicators, deviation scores
- Redline Overlay — Click finding → navigate to clause in document → show suggested change
- Chat Panel — Context-switchable chat (document content vs. analysis results) with predefined prompts
- Playbook Selector — Choose analysis playbook from Dataverse
- Export View — Export analysis as comments, tracked changes, or separate report

#### 2.3 Deployment Model Considerations

| Component | Model 1 | Model 2 |
|-----------|---------|---------|
| Word add-in | Deployed to Spaarke's Static Web App; manifest points to shared BFF | Deployed to customer's Static Web App; manifest points to customer's BFF |
| BFF endpoints | Shared BFF; tenant-scoped authorization | Customer's dedicated BFF |
| Playbook data | Retrieved from Spaarke's Dataverse (customer's tenant data) | Retrieved from customer's Dataverse |
| Add-in manifest | Spaarke publishes to AppSource or tenant catalog | Customer deploys via their M365 admin center |

#### 2.4 Success Criteria

- Clause-by-clause analysis displays in Word sidebar with click-to-navigate
- Redline suggestions insert as tracked changes in document (preserving authorship)
- Chat works in both document and analysis context with context switching
- Predefined prompts appear based on playbook and document type
- Works with both deployment models

#### 2.5 Estimated Task Breakdown

| Area | Estimated Tasks | Key Components |
|------|----------------|----------------|
| BFF API endpoints | 6-8 | Analyze, redline, chat, suggest-alternative endpoints + filters |
| RedlineGenerationService | 5-7 | Track Changes XML generation, clause diffing, Word OOXML manipulation |
| ClauseLocationService | 3-4 | Document position mapping, paragraph/character offset calculation |
| Word add-in AI panel | 8-10 | Analysis sidebar, finding cards, risk indicators, navigation integration |
| Chat panel | 5-7 | Context switching, SSE streaming, predefined prompts, history |
| Playbook integration | 3-4 | Playbook selector, skill configuration, knowledge source binding |
| Export functionality | 3-4 | Comments export, tracked changes export, report export |
| Add-in deployment | 3-4 | Manifest configuration, Static Web App deployment, both models |
| Testing | 5-7 | Unit tests, integration tests, Word API compatibility tests |
| **Total** | **~42-55 tasks** | |

#### 2.6 ADR Requirements

- No new ADRs required. Follows existing ADR-001 (Minimal API), ADR-008 (endpoint filters), ADR-021 (Fluent v9 for any UI components).

---

### Phase 3: Agentic Orchestration

**Duration**: 8-10 weeks
**Branch**: `work/ai-agent-orchestration-r1`
**Prerequisites**: Phase 1 (evaluation harness for measuring agent quality)

#### 3.1 Objective

Replace hand-rolled orchestration with Microsoft Semantic Kernel and Agent Framework, enabling multi-agent legal workflows that match CoCounsel's agentic capabilities and Ivo's multi-agent approach — running inside the Spaarke BFF.

#### 3.2 Scope

**New NuGet packages:**
- `Microsoft.SemanticKernel` — Core kernel, chat completion, embeddings, function calling
- `Microsoft.SemanticKernel.Agents.Core` — Agent definitions, ChatCompletionAgent
- `Microsoft.SemanticKernel.Agents.OpenAI` — Azure OpenAI agent integration
- `Microsoft.SemanticKernel.Connectors.AzureOpenAI` — Azure OpenAI connector
- `Microsoft.Extensions.AI` — Microsoft.Extensions.AI abstractions (unified AI interface)

**Plugin architecture (wrapping existing services):**
- `RetrievalPlugin` — Wraps `RagService`, `SemanticSearchService`
- `DocumentPlugin` — Wraps `SpeFileStore`, `TextExtractorService`
- `DataversePlugin` — Wraps `DataverseClient`
- `AnalysisPlugin` — Wraps existing tool handlers (ClauseAnalyzer, RiskDetector, EntityExtractor, etc.)
- `DraftingPlugin` — Wraps `RedlineGenerationService` (from Phase 2)
- `SearchPlugin` — Wraps `SemanticSearchService`

**Agent definitions:**
- `IntakeAgent` — Document classification, metadata extraction, structure analysis
- `ReviewAgent` — Clause-by-clause analysis, RAG precedent search, deviation scoring, risk identification
- `DraftingAgent` — Redline suggestions, alternative language, negotiation summaries
- `QualityAgent` — Citation validation, schema compliance, hallucination detection, confidence scoring
- `ResearchAgent` — Deep RAG search, cross-document comparison, precedent analysis

**Orchestration patterns:**
- Sequential pipeline (Intake → Review → Draft → Quality)
- Concurrent review (parallel clause analysis)
- Handoff (escalation from mini to full model for complex clauses)
- Human-in-the-loop checkpoints (agent proposes → lawyer reviews → agent continues)

**BFF modifications:**
- Kernel and agent DI registration
- Migrate `BuilderAgentService` from raw function-calling to Semantic Kernel agent
- Migrate `AnalysisOrchestrationService` to use SK pipeline
- Agent execution tracing via OpenTelemetry → Application Insights

**New BFF API endpoints:**
- `/api/ai/agent/execute` — Execute multi-agent workflow (SSE streaming)
- `/api/ai/agent/checkpoint` — Human-in-the-loop approval/rejection
- `/api/ai/agent/status` — Agent execution status for async workflows

#### 3.3 Deployment Model Considerations

| Component | Model 1 | Model 2 |
|-----------|---------|---------|
| Semantic Kernel | In-process in shared BFF | In-process in customer's BFF |
| Agent → OpenAI calls | Spaarke's Azure OpenAI | Customer's Azure OpenAI |
| Agent → AI Search calls | Spaarke's AI Search | Customer's AI Search |
| Agent tracing | Spaarke's App Insights (via AI Foundry tracing) | Customer's App Insights |
| Plugin configuration | Same plugins, different endpoints via config | Same plugins, different endpoints via config |
| Agent definitions | Same for all customers; behavior varies via playbook config | Same binary; playbook config in customer's Dataverse |

**Critical**: Semantic Kernel is a library, not a service. No new Azure resources. No deployment model divergence. Same BFF binary runs in both models — the difference is which Azure endpoints the kernel connects to (resolved from app settings).

#### 3.4 Migration Strategy

The adoption is **incremental, not big-bang**:

1. **Week 1-2**: Add SK NuGet packages. Register Kernel in DI. Create first plugin (RetrievalPlugin wrapping RagService). Keep all existing code working.
2. **Week 3-4**: Migrate `BuilderAgentService` from raw function-calling to ChatCompletionAgent with plugins. Both paths work during transition.
3. **Week 5-6**: Create IntakeAgent, ReviewAgent. Run them in Sequential pattern for existing "analyze document" workflow. Compare output quality against baseline (Phase 1 evaluation harness).
4. **Week 7-8**: Add DraftingAgent, QualityAgent. Implement full multi-agent pipeline. Enable concurrent review pattern.
5. **Week 9-10**: Human-in-the-loop checkpoints. Agent tracing integration. Feature-flag rollout (`EnableAgentWorkflows`).

At no point does existing functionality break. Feature flags gate new agent workflows. Evaluation pipeline (Phase 1) measures quality throughout.

#### 3.5 Success Criteria

- `BuilderAgentService` migrated to Semantic Kernel with identical output quality
- Multi-agent contract review workflow executes end-to-end
- Agent execution traces visible in Application Insights
- Quality Agent catches ≥90% of citation errors and schema violations
- Concurrent clause review reduces wall-clock time by ≥40% for documents with 5+ sections
- Feature-flagged: can be disabled per-customer without code changes

#### 3.6 Estimated Task Breakdown

| Area | Estimated Tasks | Key Components |
|------|----------------|----------------|
| Kernel + DI setup | 4-5 | NuGet packages, Kernel registration, connector configuration, feature flags |
| Plugin creation | 8-10 | RetrievalPlugin, DocumentPlugin, DataversePlugin, AnalysisPlugin, DraftingPlugin, SearchPlugin |
| Agent definitions | 6-8 | IntakeAgent, ReviewAgent, DraftingAgent, QualityAgent, ResearchAgent |
| Orchestration patterns | 5-7 | Sequential pipeline, concurrent review, handoff, human-in-the-loop |
| BuilderAgent migration | 4-5 | Port BuilderAgentService to SK; validate identical behavior |
| AnalysisOrchestration migration | 5-7 | Port existing orchestration to SK pipeline; compare quality |
| Agent tracing | 3-4 | OpenTelemetry integration, App Insights dashboards, trace correlation |
| API endpoints | 4-5 | Execute, checkpoint, status endpoints + authorization filters |
| Evaluation | 4-6 | Agent-specific gold datasets, multi-step quality metrics, regression tests |
| Integration testing | 5-7 | End-to-end workflows, deployment model tests, feature flag tests |
| **Total** | **~50-65 tasks** | |

#### 3.7 ADR Requirements

- ADR-017: Agent Orchestration Strategy (new — documents SK adoption, plugin architecture, why not Copilot Studio)
- ADR-013 amendment: Add Semantic Kernel as orchestration layer

---

### Phase 4: Client Configuration & Intelligence

**Duration**: 6-8 weeks
**Branch**: `work/ai-client-config-r1`
**Prerequisites**: Phase 1 (metadata enrichment), Phase 3 (agent framework)

#### 4.1 Objective

Enable client-specific AI accuracy without bespoke engineering. Build portfolio-level analytics that transform document analysis from a per-document tool into a strategic intelligence platform.

#### 4.2 Scope

**Client configuration system:**
- Client standards intake UI (PCF control for uploading standard clause library)
- Client taxonomy overlay system (client-specific clause categories layered on Spaarke base)
- Per-client playbook templates (inherit from Spaarke base, override standard positions)
- Per-client risk thresholds and severity rules
- Per-client escalation workflow mapping

**Portfolio intelligence:**
- Analytics dashboards (PCF controls on Dataverse views)
- Risk distribution analysis across contract portfolio
- Deviation trend tracking over time
- Clause frequency analysis across document corpus
- Cross-document relationship intelligence (extend DocumentRelationshipViewer)

**Dataverse entities:**
- `sprk_clientstandard` — Client-specific standard positions
- `sprk_clienttaxonomy` — Client taxonomy overlay definitions
- `sprk_portfolioinsight` — Computed portfolio analytics records
- `sprk_deviationtrend` — Deviation tracking over time

**Per-client evaluation:**
- Client-specific gold datasets
- Accuracy measurement against client's standards (not just generic)

#### 4.3 Deployment Model Considerations

| Component | Model 1 | Model 2 |
|-----------|---------|---------|
| Client standards | Stored in Spaarke's Dataverse; tenant-scoped | Stored in customer's Dataverse; native |
| Taxonomy overlays | Indexed in per-customer AI Search index with client-specific metadata | Indexed in customer's AI Search; full control |
| Portfolio analytics | Computed by shared BFF; results stored per-tenant in Dataverse | Computed by customer's BFF; stored in customer's Dataverse |
| Per-client evaluation | Spaarke runs against tenant-scoped data | Customer runs against their own data (if AI Foundry enabled) |
| Custom playbooks | Created by customer users; stored in Spaarke Dataverse; shared within tenant | Created by customer users; stored in customer Dataverse |

**Model 2 advantage**: Customer-hosted deployments get full control over taxonomy, standards, and evaluation — this is a key selling point for enterprise customers who want data sovereignty over their legal standards.

#### 4.4 Success Criteria

- Client standards intake workflow works end-to-end (upload → index → available in playbooks)
- Taxonomy overlay correctly modifies clause categorization for client-specific terms
- Portfolio dashboard shows risk distribution, deviation trends, clause frequency
- Per-client evaluation shows accuracy improvement over generic baseline
- At least one pilot client configured and validated

#### 4.5 Estimated Task Breakdown

| Area | Estimated Tasks |
|------|----------------|
| Client standards intake | 8-10 |
| Taxonomy overlay system | 6-8 |
| Per-client playbook customization | 5-7 |
| Portfolio analytics dashboards | 8-10 |
| Cross-document intelligence | 5-7 |
| Dataverse entities + forms | 5-7 |
| Per-client evaluation | 4-6 |
| Testing | 5-7 |
| **Total** | **~45-60 tasks** |

#### 4.6 ADR Requirements

- ADR-019: Legal Knowledge Architecture (new — taxonomy structure, overlay model, versioning)

---

### Phase 5: Operations Intelligence & Agentic Automation

**Duration**: 8-10 weeks
**Branch**: `work/ai-operations-intelligence-r1`
**Prerequisites**: Phase 3 (agent framework), Phase 4 (client configuration)

#### 5.1 Objective

Extend beyond document analysis into operational automation — matter lifecycle management, compliance monitoring, and cross-system workflow orchestration. This is where Spaarke's Dataverse integration creates capabilities no standalone AI vendor can match.

#### 5.2 Scope

**Operational agents (built on Phase 3 Agent Framework):**
- `MatterIntakeAgent` — Automated matter intake: classify incoming documents, extract key entities, create matter record, assign to appropriate team, set up document containers
- `DeadlineMonitorAgent` — Background agent: scan upcoming deadlines across matters, generate proactive alerts, recommend actions
- `ComplianceWorkflowAgent` — Regulatory compliance: monitor document corpus for policy changes, flag affected contracts, recommend review workflows
- `BillingIntelligenceAgent` — Invoice processing: extract line items, validate against engagement terms, flag anomalies, generate reports

**Cross-document intelligence:**
- Portfolio-wide clause analysis ("show me all indemnification clauses across our NDA portfolio")
- Cluster detection (identify groups of contracts with similar unusual terms)
- Trend analysis (how negotiation positions have shifted over time)

**Workflow integration (using existing Dataverse + Service Bus):**
- Task creation from agent findings (Dataverse tasks)
- Approval routing for high-risk items
- Notification workflows (email via existing export services)
- Status tracking and SLA monitoring

**New BFF services:**
- `MatterIntakeOrchestrator` — SK agent pipeline for matter intake
- `DeadlineMonitorService` — Background service scanning upcoming deadlines
- `ComplianceMonitorService` — Background service for policy change detection
- `PortfolioAnalyticsService` — Cross-document aggregation and trend analysis

#### 5.3 Deployment Model Considerations

| Component | Model 1 | Model 2 |
|-----------|---------|---------|
| Background agents | Run in shared BFF; tenant-scoped | Run in customer's BFF |
| Deadline/compliance monitoring | Shared Service Bus queue; tenant-filtered | Customer's dedicated Service Bus |
| Cross-document queries | Tenant-filtered AI Search queries | Customer's AI Search (full corpus) |
| Task creation | Customer's Dataverse (via tenant-scoped DataverseClient) | Customer's Dataverse (direct) |
| Email notifications | Via shared BFF email service | Via customer's BFF email service |

**Model 2 advantage**: Background agents have full access to customer's document corpus without cross-tenant concerns. Compliance monitoring can scan the entire corpus efficiently.

#### 5.4 Success Criteria

- Matter intake agent creates matter record with correct classification and assignment in <30 seconds
- Deadline monitor catches 100% of upcoming deadlines (7-day and 30-day windows)
- Cross-document portfolio query returns relevant results across 1000+ documents
- Agent workflows produce auditable trails in Dataverse

#### 5.5 Estimated Task Breakdown

| Area | Estimated Tasks |
|------|----------------|
| MatterIntakeAgent | 8-10 |
| DeadlineMonitorAgent | 6-8 |
| ComplianceWorkflowAgent | 6-8 |
| BillingIntelligenceAgent | 6-8 |
| Cross-document intelligence | 8-10 |
| Workflow integration | 5-7 |
| Background service infrastructure | 4-6 |
| Testing | 6-8 |
| **Total** | **~50-65 tasks** |

#### 5.6 ADR Requirements

- Evaluate whether ADR-004 (Job Contract) needs amendment for long-running agent workflows
- Evaluate whether Agent Service (Azure AI Foundry) should be adopted for persistent agent threads

---

## 9. Technology Adoption Timeline

| Quarter | Technology | Action |
|---------|-----------|--------|
| **Q1 2026** (Phase 1) | Doc Intelligence Layout model | Switch from Read to Layout |
| **Q1 2026** (Phase 1) | AI Search synonym maps + scoring profiles | Configure on existing service |
| **Q1 2026** (Phase 1) | AI Foundry evaluation pipeline | Activate dormant infrastructure |
| **Q1 2026** (Phase 1) | OpenAI structured outputs | Enable in OpenAiClient |
| **Q2 2026** (Phase 2) | Word JavaScript API (Track Changes) | Extend Word add-in |
| **Q2 2026** (Phase 3) | Microsoft.SemanticKernel | Add NuGet package to BFF |
| **Q2 2026** (Phase 3) | Microsoft Agent Framework | Multi-agent patterns |
| **Q2 2026** (Phase 3) | AI Foundry tracing | Agent execution observability |
| **Q3 2026** (Phase 4) | AI Foundry Prompt Flow endpoints | Managed prompt versioning |
| **Q3 2026** (Phase 4) | Custom Doc Intelligence models | Train legal document classifier |
| **Q4 2026** (Phase 5) | Azure AI Agent Service | Evaluate for persistent agent threads |
| **Q4 2026** (Phase 5) | MCP (Model Context Protocol) | Evaluate for external tool integration |

---

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Semantic Kernel GA delays or breaking changes | Medium | High | Pin to stable version; abstract behind internal interfaces; Agent Framework patterns can be implemented with base SK if needed |
| Gold dataset quality insufficient for evaluation | Medium | High | Start with small, curated datasets; expand iteratively; involve legal domain experts in dataset creation |
| Clause-aware chunking doesn't improve retrieval significantly | Low | Medium | Evaluate with A/B testing (Phase 1 evaluation pipeline) before committing to full migration |
| Word add-in Track Changes API limitations | Medium | Medium | Prototype early; fall back to comments-based approach if tracked changes are too limited |
| Model 2 customers unwilling to deploy AI Foundry | High | Low | AI Foundry is optional in Model 2; all core functionality works without it (BFF calls OpenAI directly) |
| Token cost escalation from multi-agent workflows | Medium | Medium | Implement cost controls: token budgets per workflow, model tiering (mini for simple steps, full for reasoning), caching of intermediate results |
| Legal knowledge pack curation effort underestimated | High | Medium | Start with 3 practice areas (NDA, commercial contract, lease); source from public legal repositories where possible; expand incrementally |

---

## Appendix A: Reference Sources

- [Harvey AI Platform](https://www.harvey.ai/) · [Harvey Top 5 Releases 2025](https://www.harvey.ai/blog/top-5-product-releases-of-2025) · [Harvey $160M Raise](https://siliconangle.com/2025/12/04/ai-focused-legal-startup-harvey-raises-160m-expand-platform-capabilities/)
- [Ivo AI Platform](https://www.ivo.ai/) · [Ivo $55M Series B](https://www.ivo.ai/blog/ivo-raises-55m-to-bring-ai-contract-intelligence-to-every-in-house-legal-team)
- [CoCounsel Legal Launch](https://www.prnewswire.com/news-releases/thomson-reuters-launches-cocounsel-legal-transforming-legal-work-with-agentic-ai-and-deep-research-302521761.html)
- [Spellbook Legal AI](https://www.spellbook.legal/) · [Spellbook Contract Review](https://www.spellbook.legal/features/review)
- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) · [Semantic Kernel Agent Architecture](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-architecture)
- [Azure AI Agent Service Labs](https://github.com/Azure/azure-ai-agents-labs)
- [Model Context Protocol](https://modelcontextprotocol.io/) · [Agentic AI Foundation](https://www.anthropic.com/news/donating-the-model-context-protocol-and-establishing-of-the-agentic-ai-foundation)
- [2026 Legal AI Predictions](https://natlawreview.com/article/ten-ai-predictions-2026-what-leading-analysts-say-legal-teams-should-expect)
- [2026 Legal Tech Trends](https://www.summize.com/resources/2026-legal-tech-trends-ai-clm-and-smarter-workflows)

---

## Appendix B: Glossary

| Term | Definition |
|------|-----------|
| **BFF** | Backend-for-Frontend — Sprk.Bff.Api, the single .NET 8 orchestration API |
| **Model 1** | Spaarke-hosted SaaS deployment; all Azure resources in Spaarke's subscription |
| **Model 2** | Customer-hosted deployment; all Azure resources in customer's Azure subscription |
| **Semantic Kernel (SK)** | Microsoft's open-source .NET SDK for building AI agents; runs in-process |
| **Agent Framework** | Multi-agent orchestration extension for Semantic Kernel (Sequential, Concurrent, Handoff patterns) |
| **AI Foundry** | Azure platform service for AI project management, evaluation, prompt flows, and tracing |
| **Copilot Studio** | Microsoft's low-code agent builder product (NOT used by Spaarke — cannot be productized) |
| **Prompt Flow** | Visual DAG-based prompt orchestration within AI Foundry; deployable as managed endpoints |
| **KernelFunction** | A function registered with Semantic Kernel that an agent can invoke autonomously |
| **Plugin** | A collection of KernelFunctions grouped by domain (e.g., RetrievalPlugin, AnalysisPlugin) |
| **SPE** | SharePoint Embedded — document storage platform via Microsoft Graph |
| **POML** | Plan-Oriented Markup Language — Spaarke's task file format |
| **Gold Dataset** | Curated set of input documents + expected outputs for measuring AI quality |

---

*End of Document*
