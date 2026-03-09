# AI Platform Foundation — Phase 1 Design

> **Version**: 1.2
> **Created**: February 21, 2026
> **Updated**: February 22, 2026
> **Status**: Draft — Pending Review
> **Project Directory**: `projects/ai-spaarke-platform-enhancements-r1`
> **Architecture Reference**: `docs/architecture/AI-ARCHITECTURE.md` (v3.0)
> **Strategy Reference**: `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` (v1.0)
> **Source Projects Consolidated**: `ai-document-intelligence-r5`, `ai-playbook-scope-editor-PCF`, `sk-analysis-chat-design.md`, `AI-Platform-Strategy-and-Architecture.md`

---

## Executive Summary

Phase 1 delivers the **foundation required for a viable AI product at June 2026 launch**. Today, the Spaarke AI platform has substantial working infrastructure (Azure OpenAI, AI Search, PlaybookExecutionEngine, 10+ tool handlers, RagService, AnalysisWorkspace PCF, Office Add-in foundation) but **critical gaps**: no seed data, no clause-aware chunking, no knowledge bases, no evaluation harness, no conversational AI component, and a hand-rolled chat that doesn't scale.

This project consolidates work from multiple planned projects into four coherent workstreams:

| Workstream | Source | What It Delivers |
|------------|--------|-----------------|
| **A. Retrieval Foundation + LlamaParse** | ai-document-intelligence-r5 (absorbed) | Clause-aware chunking, dual-parser router, indexing pipeline, knowledge base management |
| **B. Scope Library & Seed Data** | ENH-009/010/011 + ai-playbook-scope-editor-PCF (absorbed) | System scopes, seed playbooks, scope editor PCF |
| **C. SprkChat & Agent Framework** | sk-analysis-chat-design.md (absorbed), ENH-001–004 | Agent Framework integration, shared SprkChat component, analysis surface deployment |
| **D. End-to-End Validation** | ENH-012 + new evaluation harness | Playbook end-to-end testing, quality measurement baseline |

**Deferred to Phase 2+:**
- Deviation detection, ambiguity detection (ENH-005, ENH-006)
- Multi-user annotations, version comparison (ENH-007, ENH-008)
- Document relationship visuals (ai-document-relationship-visuals) — depends on similarity improvements
- Workspace + Document Studio + Word Add-in SprkChat surfaces (Phase 2)
- Client-specific overlays (Phase 4)

---

## Problem Statement

### Current State — What's Already Built

The platform has **substantial production infrastructure** already deployed:

| Component | Status | What Exists |
|-----------|--------|-------------|
| **Azure OpenAI** | Production | gpt-4o-mini, text-embedding-3-large (3072-dim), gpt-4o available |
| **Azure AI Search** | Production | Standard + semantic ranking, dual vector indices (1536+3072-dim) |
| **Document Intelligence** | Production | S0 tier, Read model (needs upgrade to Layout model) |
| **PlaybookExecutionEngine** | Production | DAG graph execution, 6 node executor types, batch + conversational modes |
| **Tool Handlers** | Production | 10+ handlers: ClauseAnalyzer, RiskDetector, EntityExtractor, DocumentClassifier, DateExtractor, FinancialCalculator, SummaryHandler, GenericAnalysisHandler |
| **RagService** | Production | Hybrid BM25 + vector + RRF search, `EmbeddingCache` (Redis, 7-day TTL), `KnowledgeDeploymentService` with multi-tenant routing |
| **Scope Resolution** | Complete | `ScopeResolverService` resolves all 4 types from Dataverse |
| **Handler Discovery API** | Built | `GET /api/ai/handlers` with `IToolHandlerRegistry`, Redis caching (5-min TTL) |
| **SemanticSearchControl PCF** | Complete | Full UI with filters, infinite scroll, dark mode |
| **AnalysisWorkspace PCF** | Production | Document preview, analysis output, basic chat, playbook selection |
| **Office Add-ins** | Foundation | Word + Outlook adapters with shared auth (NAA + Dialog), SSE client, task pane shells |
| **Telemetry** | Basic | `AiTelemetry`, `RagTelemetry`, `CacheMetrics` (OpenTelemetry counters/histograms) |
| **Deployment Infrastructure** | Production | Bicep stacks for Model 1 (Spaarke-hosted) and Model 2 (customer-hosted) |
| **AI Foundry** | Dormant | Hub + Project deployed, Prompt Flows not activated |

### Critical Gaps

| Gap | Impact | Current State |
|-----|--------|---------------|
| **Zero seed data** | Entire pipeline unusable — no Actions, Skills, Knowledge, or Playbooks in Dataverse | Infrastructure exists but is empty |
| **No clause-aware chunking** | `TextChunkingService` does character-position chunking; clauses split across chunks = incoherent retrieval | Only simple character splits |
| **Query preprocessing is NoOp** | `IQueryPreprocessor` interface exists, implementation is `NoOpQueryPreprocessor`; first-500-chars used as RAG query | No semantic query strategy |
| **No evaluation harness** | Cannot measure if improvements help or hurt; `eval-config.yaml` skeleton exists, no metric scripts | Blind iteration |
| **No structured output validation** | Tool handlers return unvalidated text; no citation enforcement | No quality assurance on outputs |
| **Hand-rolled chat** | `AnalysisOrchestrationService.ContinueAnalysisAsync()` uses flat prompt concatenation; no tool use, no context switching, no memory management, tightly coupled to AnalysisWorkspace | Cannot scale to other surfaces |
| **No knowledge packs** | No clause taxonomy, risk taxonomy, standard positions, or golden examples | Domain-specific quality impossible |
| **No scope management UX** | Admins have no validated editor for tool configs or prompt content | Error-prone manual data entry |

### Why This Matters

Without Phase 1:
- **No playbooks ship** — the primary product differentiator is empty
- **No knowledge-grounded analysis** — AI answers lack domain context
- **No quality measurement** — changes are blind (no before/after comparison)
- **No conversational AI** — chat limited to flat prompts against raw document text
- **No scope management UX** — admins can't create or configure AI primitives

### Success Criteria (June 2026 Launch Gate)

| # | Criterion | Measurement |
|---|-----------|-------------|
| 1 | 10 pre-built playbooks ship with product | Playbooks selectable in AnalysisWorkspace |
| 2 | 8 system Actions, 10 Skills, 10 Knowledge sources seeded | Dataverse records resolve during analysis |
| 3 | Clause-aware chunking deployed with LlamaParse option | Chunks respect section/paragraph boundaries |
| 4 | Knowledge base management operational | Admin can CRUD knowledge sources with embeddings |
| 5 | Hybrid retrieval measurably improves over keyword-only | Evaluation harness shows Recall@10 >= 0.7 |
| 6 | End-to-end playbook workflow passes for all 10 playbooks | Automated test suite green |
| 7 | Scope editor PCF deployed | Admins can configure scopes with validation |
| 8 | SprkChat deployed in AnalysisWorkspace | Agent Framework-powered chat with context switching, tool use, predefined prompts |
| 9 | LlamaParse dual-parser evaluated | Complex legal documents parse with >90% accuracy |

---

## Scope

### In Scope

#### Workstream A: Retrieval Foundation + LlamaParse

**Purpose**: Deliver clause-aware document processing with dual-parser support so that RAG retrieval returns coherent, section-aligned chunks.

**Absorbs**: `projects/ai-document-intelligence-r5` (RAG-ARCHITECTURE-DESIGN.md)

| Deliverable | Description | Priority |
|-------------|-------------|----------|
| **A1. Query Strategy** | Replace `NoOpQueryPreprocessor` and "first 500 chars" query with `RagQueryBuilder` using Summary + Entities from `DocumentAnalysisResult`. Cost: negligible (~$0.08/mo vs $0.025/mo at 10K analyses). | P0 |
| **A2. Chunking Service** | `IDocumentChunker` + `SemanticDocumentChunker` — split at section/paragraph boundaries. Default 1500 tokens with 200-token overlap. Switch Document Intelligence from Read → Layout model for structure-aware extraction. | P0 |
| **A3. LlamaParse Dual-Parser Router** | `DocumentParserRouter` + `LlamaParseClient` — routes complex legal docs (contracts, leases, scanned, >30 pages) to LlamaParse API; simple docs stay with Azure Doc Intelligence. Fallback guarantee: system works without LlamaParse. | P0 |
| **A4. Indexing Pipeline** | `RagIndexingPipeline` wired to analysis completion via Service Bus — auto-index documents after analysis | P0 |
| **A5. Knowledge Base Management** | `KnowledgeBaseEndpoints` (CRUD + document management + test-search + health) + Dataverse admin views | P1 |
| **A6. Two-Index Architecture** | Knowledge Base index (curated, 10–100 docs, 512-token chunks) + Discovery Index (all analyzed docs, 1000s+, 1024-token chunks) | P1 |
| **A7. Retrieval Instrumentation** | Log query text, retrieved chunks, scores, latency per request for evaluation harness | P1 |

**Technical Design (Dual-Parser Router)**:

```
Document Upload → SPE Storage → DocumentParserRouter
                                  ├── Simple Documents → Azure Doc Intelligence (fast, cheap, ~$0.01/page)
                                  └── Complex Documents → LlamaParse API (higher accuracy, ~$0.02-0.04/page)
                                          │
                                          ▼
                                   Unified ParsedDocument
                                   (text + structure + metadata)
                                          │
                               ┌──────────┴──────────┐
                               ▼                     ▼
                         AI Analysis            RAG Chunking
                         (Chat Agent)           (Indexing Pipeline)
```

```csharp
public class DocumentParserRouter
{
    public async Task<ParsedDocument> ParseDocumentAsync(
        DocumentMetadata metadata, Stream content, CancellationToken ct)
    {
        if (ShouldUseLlamaParse(metadata))
            return await ParseWithLlamaParseAsync(metadata, content, ct);
        return await _azureDocIntel.ExtractAsync(content, ct);
    }

    private bool ShouldUseLlamaParse(DocumentMetadata meta)
        => meta.DocumentType is "contract" or "lease" or "agreement" or "amendment"
           || meta.HasTables
           || meta.IsScanned
           || meta.PageCount > 30
           || meta.PlaybookRequiresHighAccuracy;
}
```

**LlamaParse Cost Model** (per 20-page contract):
| Tier | Cost/Page | 20-Page Contract | When Used |
|------|-----------|-----------------|-----------|
| Azure Doc Intelligence | ~$0.01 | ~$0.20 | Simple text-heavy documents |
| LlamaParse Cost Effective | ~$0.005 | ~$0.10 | Text-heavy, no tables |
| LlamaParse Agentic | ~$0.02 | ~$0.40 | Complex legal docs, nested tables |
| LlamaParse Agentic Plus | ~$0.04 | ~$0.80 | Scanned docs, spatial layouts |

Negligible vs. Azure OpenAI analysis costs (~$0.50–2.00 per analysis run).

**Technical Design (Chunking + Query Strategy)**:

```
Existing (broken):
  Document text → first 500 chars as query → poor relevance

New flow:
  Document → DocumentParserRouter (Azure Doc Intel or LlamaParse)
           → SemanticDocumentChunker
                ├── Detect section boundaries (headings, numbered lists, tables)
                ├── Split at paragraph level within sections
                ├── Enforce chunk size limits (1500 tokens default)
                ├── Add overlap window (200 tokens)
                └── Enrich metadata per chunk:
                      • section_title, section_number
                      • page_number, paragraph_index
                      • document_id, tenant_id
                      • contract_type, governing_law (from analysis)
           → EmbeddingService (text-embedding-3-small, 1536 dims, 7-day Redis cache)
           → Azure AI Search (upsert to appropriate index)

  Query → RagQueryBuilder
           ├── Priority 1: analysis.Summary (best semantic representation)
           ├── Priority 2: analysis.Entities.DocumentType (category matching)
           ├── Priority 3: Top 5 organizations + references (specific matching)
           ├── Priority 4: Top 5 keywords (topic matching)
           └── Fallback: Intelligent sampling (3 sections × 300 chars)
```

**Technical Design (Two-Index)**:

```
┌──────────────────────────────────┐  ┌──────────────────────────────────┐
│  spaarke-knowledge-index          │  │  spaarke-discovery-index          │
│  (Curated Knowledge Bases)        │  │  (All Analyzed Documents)         │
├──────────────────────────────────┤  ├──────────────────────────────────┤
│  Purpose: RAG grounding for       │  │  Purpose: "Find Similar" and      │
│           analysis prompts        │  │           document discovery      │
│  Size: 10–100 docs per tenant     │  │  Size: 1000s+ per tenant          │
│  Trigger: Admin adds to KB        │  │  Trigger: Auto after analysis     │
│  Query: By knowledge_source_id    │  │  Query: Semantic similarity       │
│  Chunk size: 512 tokens           │  │  Chunk size: 1024 tokens          │
│  Metadata: source_id, category    │  │  Metadata: doc_type, matter_id    │
└──────────────────────────────────┘  └──────────────────────────────────┘
```

**Files to Create**:
- `Services/Ai/RagQueryBuilder.cs`
- `Services/Ai/IDocumentChunker.cs` (interface)
- `Services/Ai/SemanticDocumentChunker.cs`
- `Services/Ai/IRagIndexingPipeline.cs` (interface)
- `Services/Ai/RagIndexingPipeline.cs`
- `Services/Ai/DocumentParserRouter.cs`
- `Services/Ai/LlamaParseClient.cs`
- `Services/Jobs/Handlers/RagIndexingJobHandler.cs`
- `Api/Ai/KnowledgeBaseEndpoints.cs`
- `Models/Ai/DocumentChunk.cs`
- `Models/Ai/ParsedDocument.cs`
- `Configuration/RagIndexingOptions.cs`

**Files to Modify**:
- `Services/Ai/AnalysisOrchestrationService.cs` — Replace first-500-chars query (line ~674) with `RagQueryBuilder`
- `Services/Ai/DocumentIntelligenceService.cs` — Wire dual-parser router + indexing pipeline after analysis
- `Program.cs` — Register new services and endpoints

---

#### Workstream B: Scope Library & Seed Data

**Purpose**: Populate the Scope Library with production-quality system scopes and provide admin tooling for scope management.

**Absorbs**: ENH-009, ENH-010, ENH-011 + `projects/ai-playbook-scope-editor-PCF` (design.md)

| Deliverable | Description | Priority |
|-------------|-------------|----------|
| **B1. System Actions (8)** | Dataverse records ACT-001–008 with system prompts, linked to tool handlers | P0 |
| **B2. System Skills (10)** | Dataverse records SKL-001–010 with specialized prompt fragments | P0 |
| **B3. System Knowledge Sources (10)** | Dataverse records KNW-001–010 with reference content, embeddings generated, indexed to knowledge index | P0 |
| **B4. System Tools (8+)** | Dataverse records TL-001–008+ with handler class names and JSON Schema configuration | P0 |
| **B5. Pre-Built Playbooks (10)** | Dataverse records PB-001–010 with canvas JSON, node definitions, scope references | P0 |
| **B6. Scope Editor PCF** | `ScopeConfigEditorPCF` — adaptive control with handler dropdown, JSON config editor (CodeMirror + AJV), markdown editor with preview | P1 |
| **B7. Handler Discovery API** | Verify/enhance `GET /api/ai/handlers` (already built in scope-resolution project); ensure ConfigurationSchema returns JSON Schema per handler | P1 |

**Seed Data Catalog** (from `docs/architecture/AI-ARCHITECTURE.md`):

**Actions (System Prompts)**:

| ID | Name | Description |
|----|------|-------------|
| ACT-001 | Contract Analysis | System prompt for comprehensive contract review |
| ACT-002 | NDA Review | System prompt for NDA-specific analysis |
| ACT-003 | Lease Review | System prompt for commercial/residential lease analysis |
| ACT-004 | Employment Review | System prompt for employment agreement analysis |
| ACT-005 | Invoice Validation | System prompt for invoice data extraction |
| ACT-006 | SLA Analysis | System prompt for SLA evaluation |
| ACT-007 | Due Diligence | System prompt for M&A due diligence |
| ACT-008 | Compliance Review | System prompt for regulatory compliance |

**Skills (Prompt Fragments)**:

| ID | Name | Description |
|----|------|-------------|
| SKL-001 | Entity Extraction | Extract parties, dates, amounts, defined terms |
| SKL-002 | Clause Analysis | Identify and categorize contract clauses |
| SKL-003 | Risk Detection | Identify risks with severity scoring |
| SKL-004 | Obligation Mapping | Extract obligations and deadlines |
| SKL-005 | Document Summary | Generate executive summary |
| SKL-006 | Key Terms | Extract critical terms and definitions |
| SKL-007 | NDA-Specific | Non-compete, non-solicit, term, jurisdiction |
| SKL-008 | Lease-Specific | Rent, CAM, renewal, termination provisions |
| SKL-009 | Financial Terms | Payment schedules, penalties, caps |
| SKL-010 | Compliance Check | Regulatory requirement verification |

**Knowledge Sources**:

| ID | Name | Content Description |
|----|------|---------------------|
| KNW-001 | Standard Contract Terms | Organization standard clause library |
| KNW-002 | NDA Best Practices | NDA review guidelines and common issues |
| KNW-003 | Lease Review Guide | Commercial lease analysis framework |
| KNW-004 | Risk Categories | Risk classification taxonomy |
| KNW-005 | Regulatory Guidelines | Industry compliance requirements |
| KNW-006 | Employment Law | Employment agreement standards |
| KNW-007 | SLA Standards | Service level best practices |
| KNW-008 | Due Diligence Checklist | M&A review requirements |
| KNW-009 | Invoice Standards | Invoice processing requirements |
| KNW-010 | Defined Terms | Standard legal definitions and acronyms |

**Playbooks** (Canvas JSON compositions):

| ID | Name | Nodes | Scopes Used |
|----|------|-------|-------------|
| PB-001 | Quick Document Review | 3 AI nodes | ACT-001, SKL-005, TL-001 |
| PB-002 | Full Contract Analysis | 6 AI + 2 workflow | ACT-001, SKL-001–006, KNW-001, TL-001–003 |
| PB-003 | NDA Review | 4 AI + 1 workflow | ACT-002, SKL-007, KNW-002, TL-001–002 |
| PB-004 | Lease Review | 5 AI + 1 workflow | ACT-003, SKL-008, KNW-003, TL-001–003 |
| PB-005 | Employment Contract Review | 4 AI + 1 workflow | ACT-004, SKL-001–004, KNW-006, TL-001–002 |
| PB-006 | Invoice Validation | 3 AI nodes | ACT-005, SKL-009, KNW-009, TL-004 |
| PB-007 | SLA Analysis | 4 AI + 1 workflow | ACT-006, SKL-010, KNW-007, TL-001–002 |
| PB-008 | Due Diligence Review | 5 AI + 2 workflow | ACT-007, SKL-001–005, KNW-008, TL-001–003 |
| PB-009 | Compliance Review | 4 AI + 1 workflow | ACT-008, SKL-010, KNW-005, TL-001–002 |
| PB-010 | Risk-Focused Scan | 3 AI nodes | ACT-001, SKL-003, TL-001 |

**Scope Editor PCF Architecture** (from `ai-playbook-scope-editor-PCF/design.md`):

The `ScopeConfigEditorPCF` is a unified, adaptive PCF control deployed to 4 scope entity forms:

| Entity | Logical Name | Editor Mode |
|--------|-------------|-------------|
| Analysis Tool | `sprk_analysistool` | Handler dropdown + JSON config editor |
| Prompt Fragment (Skill) | `sprk_promptfragment` | Markdown editor with variable placeholders |
| System Prompt (Action) | `sprk_systemprompt` | Markdown editor with preview |
| Knowledge Content | `sprk_content` | Adaptive: Markdown (Inline) or JSON (RAG) |

```
ScopeConfigEditorPCF (Top-level)
├── EntityTypeDetector — auto-detect entity type from Xrm context
├── EditorSelector (switch)
│   ├── ToolConfigEditor (sprk_analysistool)
│   │   ├── HandlerSelector (Fluent UI Dropdown, populated from GET /api/ai/handlers)
│   │   ├── HandlerMetadataDisplay (description, parameters, supported types)
│   │   ├── JSONConfigEditor (CodeMirror + AJV schema validation)
│   │   └── ValidationDisplay (inline errors with line numbers)
│   ├── MarkdownEditor (sprk_promptfragment, sprk_systemprompt)
│   │   ├── Editor pane (syntax highlighting)
│   │   ├── Preview pane (rendered markdown)
│   │   └── VariablePlaceholderHelper ({document}, {parameters}, etc.)
│   └── KnowledgeContentEditor (sprk_content)
│       ├── TypeSelector (Inline vs RAG)
│       ├── MarkdownEditor (if Inline)
│       └── JSONConfigEditor (if RAG, with RAG schema)
└── ValidationService (API client, 5-min localStorage cache)
```

---

#### Workstream C: SprkChat & Agent Framework

**Purpose**: Replace the hand-rolled chat with a production-grade, reusable conversational AI component built on Microsoft Agent Framework. Deploy to AnalysisWorkspace as first surface.

**Absorbs**: `sk-analysis-chat-design.md`, ENH-001 (Context Switching), ENH-002 (Predefined Prompts), ENH-004 (AI Wording Refinement)

**Why now**: Microsoft Agent Framework RC shipped February 19, 2026 (GA expected end of Q1 2026). Zero existing Semantic Kernel code in Spaarke — no migration cost, clean adoption. The current chat is a scaling bottleneck: flat prompt construction, no tool use, no memory management, tightly coupled to AnalysisWorkspace.

| Deliverable | Description | Priority |
|-------------|-------------|----------|
| **C1. Agent Framework Integration** | Add `Microsoft.Extensions.AI` + `Microsoft.Agents.AI` NuGet packages. Register `IChatClient` (Azure OpenAI provider) in DI. Multi-provider support (Azure OpenAI default, OpenAI/Claude/Bedrock configurable). | P0 |
| **C2. SprkChat BFF Service** | `SprkChatAgent` + `SprkChatAgentFactory` + `IChatContextProvider` interface. Chat session management (Dataverse + Redis). Conversation summarization for memory. | P0 |
| **C3. Chat Tools** | `DocumentSearchTools`, `AnalysisQueryTools`, `KnowledgeRetrievalTools`, `TextRefinementTools` — registered as `AIFunction` via `AIFunctionFactory.Create()`. No `[KernelFunction]` — plain methods with `[Description]` attributes. | P0 |
| **C4. Chat Endpoints** | Unified `/api/ai/chat/sessions/*` endpoints replacing old `/api/ai/analysis/{id}/continue`. SSE streaming for message + refinement. | P0 |
| **C5. SprkChat React Component** | Shared component in `@spaarke/ui-components`. Props-driven deployment to any surface. Context switching (Document/Analysis/Hybrid). Predefined prompts (Copilot-style chips). | P0 |
| **C6. AnalysisWorkspace Integration** | Deploy SprkChat into AnalysisWorkspace PCF replacing current chat. Context switching, predefined prompts, highlight-and-refine. | P0 |
| **C7. Agent Middleware** | TelemetryMiddleware (token tracking), CostControlMiddleware (per-session budget), ContentSafetyMiddleware (PII detection), AuditMiddleware (compliance logging). | P1 |

**Agent Framework Architecture** (no Semantic Kernel `Kernel` object):

```csharp
// Agent Framework eliminates the SK Kernel — agents created directly from providers
var agent = _chatClient.AsAIAgent(
    instructions: systemPrompt,         // Dynamic from IChatContextProvider
    tools: tools,                        // AIFunction list from tool classes
    defaultOptions: new ChatClientAgentRunOptions(new()
    {
        Temperature = 0.3f,
        MaxOutputTokens = 4096
    })
);

// Tool registration — plain methods, not [KernelFunction]
[Description("Search the original document for specific text, clauses, or sections")]
public async Task<string> SearchDocumentAsync(
    [Description("Search query")] string query,
    [Description("Max results")] int maxResults = 3)
{
    var results = await _ragService.SearchAsync(...);
    return FormatSearchResults(results);
}

// Tools registered via factory
var tools = new List<AIFunction>
{
    AIFunctionFactory.Create(_documentSearchTools.SearchDocumentAsync),
    AIFunctionFactory.Create(_analysisQueryTools.QueryFindingsAsync),
    AIFunctionFactory.Create(_knowledgeTools.SearchKnowledgeBaseAsync),
    AIFunctionFactory.Create(_refinementTools.RefineTextAsync),
};
```

**Server-Side Structure**:

```
Services/Ai/Chat/
├── SprkChatAgent.cs                    ← AIAgent wrapper
├── SprkChatAgentFactory.cs             ← Creates agent per session (selects IChatContextProvider)
├── IChatContextProvider.cs             ← Interface: BuildSystemPrompt, GetDocumentContexts, GetPredefinedPrompts
├── ChatSessionManager.cs              ← Session state (Dataverse sprk_aichatsession + Redis hot cache)
├── ChatHistoryManager.cs              ← History summarization + persistence (10 active, summarize at 15, cap at 50)
├── ContextSwitcher.cs                 ← Document vs. Analysis vs. Hybrid mode

Services/Ai/Chat/Contexts/
├── AnalysisChatContext.cs             ← Context: document + analysis output (Phase 1)
├── WorkspaceChatContext.cs            ← Context: matter + documents (Phase 2)
├── DocumentStudioChatContext.cs       ← Context: DOCX + redlines (Phase 2)
├── GenericDocumentChatContext.cs       ← Context: single document, Word add-in (Phase 2)

Services/Ai/Chat/Tools/
├── DocumentSearchTools.cs             ← Search within document (RAG)
├── AnalysisQueryTools.cs              ← Query structured findings
├── KnowledgeRetrievalTools.cs         ← RAG knowledge base search
├── TextRefinementTools.cs             ← Rewrite/improve passages (ENH-004)

Services/Ai/Chat/Middleware/
├── TelemetryMiddleware.cs
├── CostControlMiddleware.cs
├── ContentSafetyMiddleware.cs
└── AuditMiddleware.cs

Api/Ai/
└── ChatEndpoints.cs                   ← Unified chat API
```

**Client-Side Structure** (shared component in `@spaarke/ui-components`):

```
SprkChat/
├── SprkChat.tsx                       ← Main component (reusable across all surfaces)
├── SprkChatProvider.tsx               ← Context provider + API client
├── ChatMessage.tsx                    ← Individual message with citations
├── ChatInput.tsx                      ← Input with predefined prompt chips
├── ChatContextSwitch.tsx              ← Document/Analysis/Hybrid toggle
├── PredefinedPrompts.tsx              ← Copilot-style suggestion chips
├── RefinementToolbar.tsx              ← Floating toolbar for highlight-and-refine
├── RefinementPreview.tsx              ← Diff preview with accept/reject
├── CitationLink.tsx                   ← Clickable citation rendering
├── ToolCallIndicator.tsx              ← "Searching..." / "Analyzing..." display
├── hooks/
│   ├── useSprkChat.ts                 ← Core chat state + SSE streaming
│   ├── useChatContext.ts              ← Context switching logic
│   ├── usePredefinedPrompts.ts        ← Prompt suggestions
│   ├── useRefinement.ts              ← Highlight-and-refine state
│   └── useChatHistory.ts             ← History pagination
└── types.ts                           ← ISprkChatProps, IChatMessage, etc.
```

**SprkChat Component Interface**:

```typescript
interface ISprkChatProps {
    sessionId: string;
    contextType: "analysis" | "workspace" | "document-studio" | "document" | "matter";
    contextData: Record<string, unknown>;
    apiBaseUrl: string;
    playbookId?: string;
    onRefinement?: (refinement: IRefinementResult) => void;
    onDocumentReference?: (reference: IDocumentReference) => void;
    onAnalysisUpdate?: (update: IAnalysisUpdate) => void;
    compact?: boolean;                  // Word add-in mode (350px)
    theme?: "light" | "dark" | "auto";
    defaultContextMode?: string;
    externalSelection?: IExternalSelection | null;  // Host pushes selected text
}
```

**Highlight-and-Refine (ENH-004)**:

```
User Flow:
1. Select text in Analysis Output panel
2. Floating toolbar: [Refine Wording] [Expand] [Simplify] [Make Stronger] [Cite] [Ask AI]
3. Click action → POST /api/ai/chat/sessions/{id}/refine (SSE stream)
4. Agent calls tools (search document, search knowledge) if needed
5. Diff preview with accept/reject
6. Working document updates on accept
```

**Chat History Management**:

```
ActiveWindowSize:        10 recent messages in full detail
SummarizationThreshold:  15 messages triggers summarization
MaxTotalMessages:        50 before archiving
Storage:                 Dataverse (sprk_aichatmessage) + Redis hot cache (tenant-prefixed)
Summary:                 Stored separately in sprk_aichatsummary
```

**Phase 1 Scope for SprkChat** (Analysis surface only):
- Agent Framework NuGet + IChatClient DI registration
- SprkChatAgent + SprkChatAgentFactory
- AnalysisChatContext provider (document + analysis output)
- ChatEndpoints (sessions, message, refine, suggestions, history)
- ChatSessionManager + ChatHistoryManager
- Core tools: DocumentSearchTools, AnalysisQueryTools, KnowledgeRetrievalTools, TextRefinementTools
- SprkChat React component in @spaarke/ui-components
- Integration into AnalysisWorkspace PCF (replace current chat)
- Context switching (Document/Analysis/Hybrid)
- Predefined prompts
- Highlight-and-refine

**Deferred to Phase 2 (additional surfaces)**:
- WorkspaceChatContext (matter + documents)
- DocumentStudioChatContext (DOCX + redlines)
- GenericDocumentChatContext (Word add-in)
- MatterTools, ExportTools, ClauseComparisonTools

---

#### Workstream D: End-to-End Validation

**Purpose**: Prove the platform works end-to-end and establish quality measurement baseline.

| Deliverable | Description | Priority |
|-------------|-------------|----------|
| **D1. Test Document Corpus** | 10+ sample documents (NDA, contract, lease, invoice, SLA, employment, complex, malformed) | P0 |
| **D2. End-to-End Playbook Tests** | Automated test for each playbook: upload → analyze → verify output structure → verify citations | P0 |
| **D3. Evaluation Harness** | Gold dataset + scoring pipeline: Recall@K, nDCG@K for retrieval; output validation scoring; citation accuracy; clause coverage | P1 |
| **D4. Quality Baseline** | Run all 10 playbooks against test corpus, record scores as "before" baseline | P1 |
| **D5. Negative Testing** | Missing skills, empty knowledge sources, handler timeouts, malformed documents | P1 |
| **D6. SprkChat Evaluation** | Chat answer accuracy (grounded in document), response includes source citation (>80%), context switch adoption, response latency (<2s first token) | P1 |

**Evaluation Harness Design**:

```
┌──────────────────────────────────────────────────────────────┐
│  Evaluation Pipeline                                          │
│                                                               │
│  ┌─────────────┐     ┌──────────────┐     ┌──────────────┐  │
│  │ Gold Dataset │────▶│ Run Analysis │────▶│ Score Output │  │
│  │ (10+ docs   │     │ (per playbook)│     │              │  │
│  │  + expected  │     │              │     │ • Recall@10   │  │
│  │  outputs)    │     │              │     │ • nDCG@10     │  │
│  └─────────────┘     └──────────────┘     │ • Format OK   │  │
│                                            │ • Citations   │  │
│                                            │ • Clause cov. │  │
│                                            │ • Latency     │  │
│                                            └──────────────┘  │
│                                                    │          │
│                                            ┌──────▼───────┐  │
│                                            │ Baseline     │  │
│                                            │ Report       │  │
│                                            │ (JSON + MD)  │  │
│                                            └──────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### Out of Scope (Deferred)

| Item | Reason | Target Phase |
|------|--------|-------------|
| **SprkChat: Workspace surface** | Requires WorkspaceChatContext + MatterTools | Phase 2 |
| **SprkChat: Document Studio surface** | Requires DocumentStudioChatContext + OOXML integration | Phase 2 |
| **SprkChat: Word Add-in surface** | Requires compact mode + GenericDocumentChatContext | Phase 2 |
| **Deviation Detection** (ENH-005) | Requires knowledge pack maturity; build on Phase 1 knowledge bases | Phase 2 |
| **Ambiguity Detection** (ENH-006) | New tool handler; depends on structured output validation | Phase 2 |
| **Multi-User Annotations** (ENH-007) | UX feature; not blocking for launch | Phase 4 |
| **Document Version Comparison** (ENH-008) | Requires document similarity infrastructure | Phase 2 |
| **Prompt Library Management** (ENH-003) | UX feature; can use predefined prompts initially | Phase 2 |
| **Document Relationship Visuals** | Depends on similarity graph improvements | Phase 2 |
| **Find Similar Documents UI** | Depends on discovery index population | Phase 2 |
| **Client-Specific Overlays** | Requires base system proven first | Phase 4 |
| **Multi-agent orchestration** | Sequential/concurrent/handoff patterns; Phase 1 uses single-agent SprkChat | Phase 3 |

---

## Technical Approach

### Architecture Alignment

All work fits within the established four-tier architecture:

| Tier | Phase 1 Work |
|------|-------------|
| **Tier 1: Scope Library** | Seed all system scopes (B1–B5); scope editor PCF (B6) |
| **Tier 2: Composition** | Pre-built playbooks (B5); SprkChat as composition pattern (C5–C6); end-to-end validation (D2) |
| **Tier 3: Runtime** | Agent Framework integration (C1–C2); indexing pipeline (A4) |
| **Tier 4: Infrastructure** | Two-index architecture (A6); chunking service (A2); LlamaParse integration (A3) |

### Key Technical Decisions

**TD-1: Chunking — Semantic boundary detection with dual parser**
- Switch Document Intelligence from Read → Layout model for structure-aware extraction
- LlamaParse for complex legal documents (contracts, leases, scanned, >30 pages)
- `DocumentParserRouter` selects parser based on document metadata
- Split at section boundaries first, then paragraph boundaries within sections
- Default 1500 tokens with 200-token overlap
- Fallback guarantee: system works without LlamaParse
- Rationale: Coherent chunks improve retrieval quality; LlamaParse significantly outperforms OCR on nested tables and multi-column layouts

**TD-2: Two-index architecture for different retrieval patterns**
- Knowledge index: small, curated, queried by knowledge_source_id + semantic similarity
- Discovery index: large, auto-populated, queried by document similarity
- Same Azure AI Search service, different index configurations
- Rationale: Different access patterns require different chunk sizes and metadata schemas

**TD-3: Seed data as Dataverse records (not hardcoded)**
- All system scopes stored as Dataverse records with SYS- prefix
- ScopeResolverService loads from Dataverse via Web API (already implemented)
- Playbook canvas JSON stored in Dataverse field
- Rationale: Consistent with scope ownership model; customers can extend via SaveAs pattern

**TD-4: Evaluation harness as BFF API endpoints + CLI runner**
- `POST /api/ai/evaluation/run` — runs evaluation suite
- `GET /api/ai/evaluation/results/{runId}` — gets results
- Results stored in Dataverse (sprk_aievaluationrun, sprk_aievaluationresult)
- CLI script for CI/CD: `dotnet run --project tools/EvalRunner`
- Rationale: API-first; Dataverse storage enables trend analysis

**TD-5: Scope editor as single adaptive PCF control**
- One PCF control deployed to all 4 scope entity forms
- Auto-detects entity type from Xrm context
- Handler dropdown from existing `GET /api/ai/handlers`
- CodeMirror for JSON (lighter than Monaco, fits PCF 1MB bundle limit) + AJV for schema validation
- Rationale: Single control reduces maintenance; adaptive behavior covers all scope types

**TD-6: Agent Framework (not Semantic Kernel Kernel object)**
- Use `Microsoft.Extensions.AI` (`IChatClient`) + `Microsoft.Agents.AI` (`AIAgent`)
- Agent created via `chatClient.AsAIAgent()` — no SK `Kernel` object
- Tools registered as plain methods with `[Description]` via `AIFunctionFactory.Create()`
- Multi-provider: same `IChatClient` interface supports Azure OpenAI (default), OpenAI, Claude, Bedrock
- Runs in-process in BFF — no new Azure resources, same binary for Model 1 and Model 2
- Rationale: Zero SK legacy; Agent Framework is the production successor; cleaner API; multi-provider support

**TD-7: SprkChat as shared component (not embedded)**
- SprkChat lives in `@spaarke/ui-components`, not in AnalysisWorkspace directly
- Props-driven deployment: `contextType`, `contextData`, `onRefinement`, etc.
- Each surface provides its own `IChatContextProvider` server-side and passes context via props client-side
- Rationale: Single component deployed to 5+ surfaces without code duplication

### Dependencies

```
Workstream A (retrieval)  ──────────────────────▶  Enables D3, D4
     │
     ├── A1: Query strategy ──▶ A4 (indexing pipeline uses smart queries)
     ├── A2: Chunking service ──▶ A4, A6 (chunks feed into both indexes)
     ├── A3: LlamaParse router ──▶ A2 (chunker receives parsed output)
     ├── A4: Indexing pipeline ──▶ A6 (populates both indexes)
     ├── A5: KB management ──▶ B3 (knowledge sources need management UI)
     └── A6: Two-index arch ──▶ Independent (operational concern)

Workstream B (seed data)  ──────────────────────▶  Enables D1, D2
     │
     ├── B1–B4: System scopes ──▶ B5 (playbooks reference scopes)
     ├── B5: Playbooks ──▶ D2 (end-to-end tests run playbooks)
     ├── B6: Scope editor PCF ──▶ Independent (admin tooling)
     └── B7: Handler discovery ──▶ B6 (editor uses handler API)

Workstream C (SprkChat)  ──────────────────────▶  Enables D6
     │
     ├── C1: Agent Framework ──▶ C2 (SprkChat service needs IChatClient)
     ├── C2: BFF service ──▶ C3, C4 (tools and endpoints need agent)
     ├── C3: Chat tools ──▶ C6 (analysis surface uses tools)
     ├── C4: Chat endpoints ──▶ C5 (React component calls endpoints)
     ├── C5: SprkChat React ──▶ C6 (AnalysisWorkspace embeds component)
     └── C6: AnalysisWorkspace ──▶ D6 (chat evaluation needs working chat)

Workstream D (validation)  ─────────────────────▶  Launch gate
     │
     ├── D1: Test corpus ──▶ D2, D3 (tests need documents)
     ├── D2: E2E tests ──▶ D4 (baseline comes from running tests)
     ├── D3: Eval harness ──▶ D4 (baseline needs scoring pipeline)
     ├── D4: Quality baseline ──▶ LAUNCH GATE
     └── D6: SprkChat eval ──▶ LAUNCH GATE
```

### Execution Order

```
Phase 1a: Foundation (Week 1–5, parallel tracks)
  Track 1 — Retrieval:
  ├── A3: LlamaParse dual-parser router
  ├── A1: Query strategy fix (RagQueryBuilder)
  ├── A2: Chunking service (SemanticDocumentChunker)
  ├── A4: Indexing pipeline (depends on A1, A2, A3)
  ├── B7: Handler discovery API (verify existing)
  └── B6: Scope editor PCF (depends on B7)

  Track 2 — Agent Framework + SprkChat:
  ├── C1: Agent Framework NuGet + IChatClient DI
  ├── C2: SprkChatAgent + SprkChatAgentFactory + ChatSessionManager
  ├── C3: Chat tools (DocumentSearch, AnalysisQuery, Knowledge, Refinement)
  ├── C4: ChatEndpoints (sessions, message, refine, suggestions, history)
  ├── C5: SprkChat React component in @spaarke/ui-components
  └── C7: Agent middleware (telemetry, cost control, safety)

Phase 1b: Seed Data + Integration (Week 4–8)
  ├── B1: System Actions (8)
  ├── B2: System Skills (10)
  ├── B3: System Knowledge Sources (10) + embeddings (depends on A2, A4)
  ├── B4: System Tools (8)
  ├── B5: Pre-built Playbooks (10) (depends on B1–B4)
  ├── A5: Knowledge base management UI
  ├── A6: Two-index architecture
  └── C6: SprkChat → AnalysisWorkspace integration (depends on C5, B5)

Phase 1c: Validation & Launch Gate (Week 7–10)
  ├── D1: Test document corpus
  ├── D2: End-to-end playbook tests (depends on B5, A4)
  ├── D3: Evaluation harness (depends on A7)
  ├── D4: Quality baseline (depends on D2, D3)
  ├── D5: Negative testing
  └── D6: SprkChat evaluation (depends on C6)
```

### ADR Compliance

| ADR | Relevance | Compliance Approach |
|-----|-----------|-------------------|
| ADR-001 | Indexing pipeline as BackgroundService | `RagIndexingJobHandler` as job handler, not Azure Function |
| ADR-007 | SpeFileStore facade for document access | Chunking service + parser router access documents via SpeFileStore |
| ADR-008 | Endpoint filters for new API endpoints | Chat, KB management, evaluation endpoints all use endpoint filters |
| ADR-009 | Redis caching | Embedding cache (7-day TTL), handler metadata (5-min), chat sessions (hot cache) |
| ADR-010 | DI minimalism | Concrete types; verify <=15 non-framework registrations |
| ADR-013 | AI Tool Framework; extend BFF | All new services/endpoints in BFF API; Agent Framework runs in-process |
| ADR-006 | PCF over webresources | Scope editor + SprkChat are PCF-based |
| ADR-012 | Shared component library | SprkChat in `@spaarke/ui-components` |
| ADR-021 | Fluent UI v9 | All new UI uses Fluent v9; dark mode required |
| ADR-022 | React 16 platform libraries | SprkChat + scope editor target React 16 |

### New BFF API Endpoints

| Method | Path | Purpose | Workstream |
|--------|------|---------|-----------|
| GET | `/api/ai/handlers` | Handler discovery with ConfigurationSchema | B7 (already built, verify) |
| GET | `/api/ai/knowledge-bases` | List knowledge bases for tenant | A5 |
| POST | `/api/ai/knowledge-bases` | Create knowledge base | A5 |
| PUT | `/api/ai/knowledge-bases/{id}` | Update knowledge base | A5 |
| DELETE | `/api/ai/knowledge-bases/{id}` | Delete knowledge base + re-index | A5 |
| POST | `/api/ai/knowledge-bases/{id}/documents` | Add document to knowledge base | A5 |
| DELETE | `/api/ai/knowledge-bases/{id}/documents/{docId}` | Remove document from knowledge base | A5 |
| POST | `/api/ai/knowledge-bases/{id}/reindex` | Trigger re-indexing | A5 |
| GET | `/api/ai/knowledge-bases/{id}/health` | Index health metrics | A5 |
| POST | `/api/ai/knowledge-bases/{id}/test-search` | Test search quality | A5 |
| POST | `/api/ai/chat/sessions` | Create chat session | C4 |
| POST | `/api/ai/chat/sessions/{id}/message` | Send message (SSE stream) | C4 |
| POST | `/api/ai/chat/sessions/{id}/refine` | Highlight-and-refine (SSE stream) | C4 |
| POST | `/api/ai/chat/sessions/{id}/context` | Switch context mode | C4 |
| GET | `/api/ai/chat/sessions/{id}/suggestions` | Get predefined prompts | C4 |
| GET | `/api/ai/chat/sessions/{id}/history` | Paginated chat history | C4 |
| DELETE | `/api/ai/chat/sessions/{id}` | End session, cleanup | C4 |
| POST | `/api/ai/evaluation/run` | Run evaluation suite | D3 |
| GET | `/api/ai/evaluation/results/{runId}` | Get evaluation results | D3 |
| GET | `/api/ai/evaluation/baseline` | Get current quality baseline | D3 |

### New/Modified Services

| Service | Type | Purpose |
|---------|------|---------|
| `DocumentParserRouter` | New | Route to Azure Doc Intel or LlamaParse based on document metadata |
| `LlamaParseClient` | New | LlamaParse API client (upload, poll, retrieve) |
| `SemanticDocumentChunker` | New | Clause-aware document chunking |
| `IDocumentChunker` | New interface | Abstraction for chunking strategies |
| `RagQueryBuilder` | New | Build smart RAG queries from analysis metadata |
| `RagIndexingPipeline` | New | Orchestrate chunk → embed → index flow |
| `RagIndexingJobHandler` | New | Background job handler for async indexing |
| `KnowledgeBaseService` | New | CRUD operations for knowledge bases |
| `SprkChatAgent` | New | AIAgent wrapper for conversational AI |
| `SprkChatAgentFactory` | New | Creates agent per session with context-specific tools |
| `IChatContextProvider` | New interface | Context provider for different chat surfaces |
| `AnalysisChatContext` | New | Analysis surface context (document + analysis output) |
| `ChatSessionManager` | New | Session state management (Dataverse + Redis) |
| `ChatHistoryManager` | New | History summarization and persistence |
| `DocumentSearchTools` | New | Chat tool: search within document |
| `AnalysisQueryTools` | New | Chat tool: query structured findings |
| `KnowledgeRetrievalTools` | New | Chat tool: RAG knowledge base search |
| `TextRefinementTools` | New | Chat tool: rewrite/improve passages |
| `EvaluationService` | New | Run evaluation suite, score outputs |
| `EvaluationRunnerTool` | New (CLI) | Command-line evaluation runner for CI/CD |
| `RagService` | Modified | Use `RagQueryBuilder` instead of first 500 chars |
| `DocumentIntelligenceService` | Modified | Wire dual-parser router + indexing pipeline |
| `AnalysisOrchestrationService` | Modified | Replace old chat code path |

### New PCF Controls / Shared Components

| Component | Version | Type | Target |
|-----------|---------|------|--------|
| `ScopeConfigEditorPCF` | 1.0.0 | PCF Control | sprk_analysistool, sprk_promptfragment, sprk_systemprompt, sprk_content forms |
| `SprkChat` | 1.0.0 | Shared Component | `@spaarke/ui-components` → AnalysisWorkspace (Phase 1), other surfaces (Phase 2) |

---

## Consolidated Project Disposition

### Absorbed Into Phase 1

| Project | Status Before | Absorbed As | Key Content Preserved |
|---------|--------------|-------------|----------------------|
| `ai-document-intelligence-r5` | Planning (RAG-ARCHITECTURE-DESIGN.md, 61KB) | Workstream A (A1–A7) | Two-index strategy, chunking design, query strategy, indexing pipeline, KB management API |
| `ai-playbook-scope-editor-PCF` | Design phase (design.md, 30KB) | Workstream B (B6, B7) | Adaptive PCF architecture, EntityTypeDetector, handler dropdown, JSON+Markdown editors, component tree |
| `sk-analysis-chat-design.md` | Design (114KB, in platform-enhancements-r1) | Workstream C (C1–C7) | Agent Framework architecture, SprkChat component, chat tools, memory management, surfaces, highlight-and-refine |
| `AI-Platform-Strategy-and-Architecture.md` | Strategy (90KB, in platform-enhancements-r1) | Current state assessment, gap analysis, competitive landscape | 9 component blocks, existing code inventory, critical gaps |

### Completed (Prerequisites — No Longer In Scope)

| Project | Status | Outcome |
|---------|--------|---------|
| `ai-scope-resolution-enhancements` | Complete | ScopeResolverService, handler discovery API, GenericAnalysisHandler fallback — all shipped |
| `ai-semantic-search-ui-r2` | Complete | SemanticSearchControl PCF with full UI, filters, infinite scroll, dark mode — all shipped |

### Deferred

| Project | Deferred To | Reason |
|---------|------------|--------|
| `ai-document-relationship-visuals` | Phase 2 | Depends on similarity graph improvements and discovery index population |

### Enhancements Disposition

| Enhancement | Phase 1? | Disposition |
|-------------|---------|-------------|
| ENH-001: Chat Context Switching | **Yes** | Workstream C (C2, C6 — AnalysisChatContext with Document/Analysis/Hybrid modes) |
| ENH-002: Predefined Prompts | **Yes** | Workstream C (C5 — PredefinedPrompts component, Copilot-style chips) |
| ENH-003: Prompt Library | No | Deferred → Phase 2 (use predefined prompts initially) |
| ENH-004: AI Wording Refinement | **Yes** | Workstream C (C3, C6 — TextRefinementTools + highlight-and-refine UI) |
| ENH-005: Deviation Detection | No | Deferred → Phase 2 (requires knowledge pack maturity) |
| ENH-006: Ambiguity Detection | No | Deferred → Phase 2 (new tool handler) |
| ENH-007: Multi-User Annotations | No | Deferred → Phase 4 (UX feature) |
| ENH-008: Version Comparison | No | Deferred → Phase 2 (requires similarity infra) |
| ENH-009: Seed Data — Actions & Skills | **Yes** | Workstream B (B1, B2) |
| ENH-010: Seed Data — Knowledge Sources | **Yes** | Workstream B (B3) |
| ENH-011: Seed Data — Sample Playbooks | **Yes** | Workstream B (B5) |
| ENH-012: End-to-End Playbook Testing | **Yes** | Workstream D (D2) |

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Agent Framework RC has breaking changes before GA | Low | High | Pin to RC version; Agent Framework API surface is stable. Fallback: raw `IChatClient` without `AsAIAgent()` wrapper. |
| LlamaParse API reliability/latency | Medium | Medium | Dual-parser router falls back to Azure Doc Intelligence. LlamaParse is optional, not required. |
| Clause-aware chunking degrades some document types | Medium | High | Evaluate on diverse corpus; keep fallback to simple chunking |
| Seed data quality insufficient for demo | Medium | High | Iterate on prompts with real legal documents; domain expert review |
| SprkChat scope creep (additional surfaces) | Medium | Medium | Strict Phase 1 = AnalysisWorkspace only. Other surfaces Phase 2. |
| Knowledge base content too thin for meaningful RAG | Medium | Medium | Start with publicly available legal references; customer content Phase 4 |
| Scope editor PCF exceeds React 16 limitations | Low | Medium | Follow ADR-022; CodeMirror lighter than Monaco |
| Two-index architecture doubles AI Search costs | Low | Low | Both indexes share same AI Search service (S1 supports 2M chunks) |
| SprkChat memory management (summarization) accuracy | Medium | Medium | Conservative: 10-message active window, summarize only beyond 15 |

---

## Estimation Summary

| Workstream | Estimated Effort | Calendar Weeks |
|------------|-----------------|---------------|
| A: Retrieval Foundation + LlamaParse | 3–4 weeks | Weeks 1–5 |
| B: Scope Library & Seed Data | 3–4 weeks | Weeks 4–8 |
| C: SprkChat & Agent Framework | 5–6 weeks | Weeks 1–7 (parallel with A) |
| D: End-to-End Validation | 2–3 weeks | Weeks 7–10 |
| **Total** | **~10 weeks elapsed** | Parallel execution: A+C run concurrently |

---

## References

- [AI-ARCHITECTURE.md](../../docs/architecture/AI-ARCHITECTURE.md) — Four-tier architecture, scope catalog, playbook catalog
- [SPAARKE-AI-STRATEGY-AND-ROADMAP.md](../../docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md) — Strategic positioning, phased roadmap
- [RAG-ARCHITECTURE-DESIGN.md](../ai-document-intelligence-r5/RAG-ARCHITECTURE-DESIGN.md) — Detailed chunking and indexing design (absorbed into Workstream A)
- [Scope Editor Design](../ai-playbook-scope-editor-PCF/design.md) — Detailed scope editor PCF design (absorbed into Workstream B)
- [sk-analysis-chat-design.md](sk-analysis-chat-design.md) — SprkChat and Agent Framework design (absorbed into Workstream C)
- [AI-Platform-Strategy-and-Architecture.md](AI-Platform-Strategy-and-Architecture.md) — Strategic assessment, gap analysis, competitive landscape (absorbed into current state assessment)
- [enhancements.md](enhancements.md) — Full enhancement backlog with user stories
- [Microsoft Agent Framework](https://github.com/microsoft/agentframework) — RC shipped Feb 19, 2026

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-02-21 | 1.0 | Initial draft. Consolidated from enhancements.md (ENH-009–012), ai-document-intelligence-r5, ai-playbook-scope-editor-PCF. Defined 4 workstreams, 22 deliverables. |
| 2026-02-22 | 1.1 | Major update: Added SprkChat & Agent Framework workstream — Agent Framework RC available now, not deferred. Added LlamaParse dual-parser router to Retrieval workstream. Added current state assessment documenting existing completed development. Absorbed sk-analysis-chat-design.md and AI-Platform-Strategy-and-Architecture.md content. Moved ENH-001, ENH-002, ENH-004 into Phase 1. |
| 2026-02-22 | 1.2 | Removed "Close In-Flight" workstream — ai-scope-resolution-enhancements and ai-semantic-search-ui-r2 both completed independently. Relabeled workstreams: A (Retrieval), B (Scope Library), C (SprkChat), D (Validation). Updated to 4 workstreams, ~27 deliverables, ~10 weeks. |
