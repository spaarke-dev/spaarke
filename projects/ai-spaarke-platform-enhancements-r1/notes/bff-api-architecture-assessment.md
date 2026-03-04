# BFF API Architecture Assessment

> **Date**: 2026-03-04
> **Context**: The Sprk.Bff.Api was originally built as a SharePoint Embedded (SPE) gateway for the Dataverse-SPE integration. It has expanded to serve AI services, Office Add-ins, email processing, workspace intelligence, finance modules, and communication features. This assessment evaluates the current state and recommends enhancements for long-term viability.

---

## 1. Current Scope of BFF API

### Original Design Intent
A thin backend-for-frontend proxying SharePoint Embedded operations through Microsoft Graph, with Dataverse as the metadata store. The BFF pattern was chosen to:
- Keep secrets (Graph client credentials, SPE app registration) server-side
- Perform OBO token exchange for user-context file operations
- Apply resource-level authorization via endpoint filters (ADR-008)

### Actual Current Scope

The API has grown to serve **7 distinct functional domains** across **120+ endpoints** with **99+ DI registrations** in a single `Program.cs` of **1,881 lines**.

| Domain | Endpoints | Services | External Dependencies | Original? |
|--------|-----------|----------|-----------------------|-----------|
| **SPE / File Operations** | ~25 | SpeFileStore, UploadSessionManager, DriveItemOperations | Graph API, Azure AD (OBO) | Yes |
| **Dataverse Documents** | ~10 | IDataverseService, DataverseAccessDataSource | Dataverse Web API | Yes |
| **AI Platform** | ~40+ | OpenAiClient, RagService, ChatSessionManager, AnalysisOrchestrationService, SprkChatAgentFactory, DocumentIntelligenceService, SemanticSearchService | Azure OpenAI, Azure AI Search, Azure Document Intelligence | No |
| **Office Add-ins** | ~10 | IOfficeService, JobStatusService, SseHelper | Graph API, Service Bus | No |
| **Email / Communication** | ~10 | EmailService, CommunicationService, EmailToDocumentJobHandler | Graph API (mail), Service Bus | No |
| **Finance Intelligence** | ~6 | FinanceService, InvoiceExtractionJobHandler, ScorecardCalculatorService | Document Intelligence, AI Search | No |
| **Workspace / Portfolio** | ~10 | PortfolioService, BriefingService, PriorityScoringService, TodoGenerationService | Azure OpenAI (optional), Dataverse | No |
| **Background Jobs** | 13 handlers | ServiceBusJobProcessor + handlers | Service Bus, all upstream services | Partially |
| **Admin / System** | ~8 | BuilderScopeAdmin, RecordMatchingAdmin, ResilienceEndpoints | AI Search, Dataverse | No |

### Cross-Cutting Infrastructure

| Concern | Implementation | Scale |
|---------|---------------|-------|
| Authentication | Azure AD JWT + OBO token exchange | Every endpoint |
| Authorization | 12 endpoint filters, 26 policies | Per-resource |
| Caching | Redis (distributed) + RequestCache (per-request) | Token cache, UAC, embeddings, portfolio |
| Resilience | Polly retry/circuit breaker per external service | Graph, OpenAI, AI Search |
| Observability | OpenTelemetry (5 custom meters), Application Insights | Full pipeline |
| Rate Limiting | 4 named limiters + Office-specific rate filters | AI and Office endpoints |
| Background Processing | ServiceBusJobProcessor (1 processor, 13+ handler types) | All async work |

---

## 2. Current and Potential Future Issues

### 2a. Issues Present Today

**Single-Process Blast Radius**
All domains share one App Service instance. A memory leak in the RAG indexing pipeline, a thread pool starvation in document intelligence, or an unhandled exception in email polling can take down SPE file operations — the core value proposition. There is no isolation between tenants or between domains within the process.

**Program.cs Complexity (1,881 lines)**
While modularized into `AddXxxModule()` extensions, the composition root is a single file that registers 99+ services, configures 26 auth policies, sets up 4 rate limiters, and maps 32 endpoint groups. This makes it difficult to understand startup behavior, increases merge conflict risk, and makes it easy to introduce registration order bugs.

**DI Registration Drift Beyond ADR-010**
ADR-010 specifies ≤15 non-framework DI registrations. The AI module alone adds 12+ registrations. The total count (99+) far exceeds the original intent, even accounting for module grouping. This signals that the API has outgrown the original "minimal composition" philosophy.

**In-Memory State Loss (OPS-05)**
Analysis results and working documents are stored in-memory. Any App Service restart, scale event, or deployment loses all active user sessions. This is documented as a known gap but remains the #1 production blocker.

**Background Job Contention**
A single `ServiceBusJobProcessor` routes all 13+ job types through one consumer. Heavy RAG indexing or batch email processing competes with time-sensitive invoice extraction and chat session jobs for the same thread pool and Service Bus receiver concurrency.

**HostContext Propagation Fragility**
The `ChatHostContext` (entity type, ID, workspace type) must flow through 6+ layers from endpoint to AI Search query. There's no compile-time enforcement — a missed propagation silently breaks entity-scoped search.

### 2b. Emerging Risks (6-12 Month Horizon)

**Scaling Ceiling**
App Service horizontal scaling (adding instances) scales *everything together*. AI workloads (GPU-bound, high-latency) have fundamentally different scaling profiles than SPE operations (I/O-bound, low-latency). Paying for compute to serve file downloads at the scale needed for AI batch processing is wasteful.

**Deployment Coupling**
Any change to any domain requires redeploying the entire API. A bug fix in communication endpoints means restarting all active AI streaming sessions. As feature velocity increases across teams, deployment frequency will create availability pressure.

**Cold Start & Startup Time**
With 99+ service registrations, multiple HTTP client configurations, Service Bus processors, and health checks, cold start time will grow. This impacts slot swaps during deployment and auto-scale responsiveness.

**Token Budget Accumulation**
As AI features expand (more playbooks, longer context windows, multi-document analysis), the aggregate Azure OpenAI token consumption grows without natural boundaries between domains. Cost attribution becomes difficult when everything runs in one process.

**Team Scaling**
With one codebase and one deployment unit, multiple developers working on different domains (AI, finance, communication) will increasingly step on each other. The merge-conflict risk in `Program.cs` and shared infrastructure files grows linearly with team size.

---

## 3. Pros and Cons of Current Architecture

### Pros

| Advantage | Detail |
|-----------|--------|
| **Operational Simplicity** | One App Service, one deployment, one monitoring dashboard. No service mesh, no inter-service auth, no distributed tracing complexity. |
| **Shared Cross-Cutting Concerns** | Auth, caching, resilience, telemetry configured once and applied everywhere. No duplication across services. |
| **Service Coordination is Trivial** | AI analysis needs file access? Just inject `SpeFileStore`. No HTTP calls between services, no eventual consistency, no service discovery. |
| **Transactional Consistency** | Operations that span SPE + Dataverse + AI (e.g., upload → extract → index → analyze) happen in-process with shared state. No saga patterns needed. |
| **Low Latency for Composed Operations** | Chat tool invocations that need to search documents, read metadata, and run RAG queries do so in-process without network hops. |
| **Team Velocity (Small Team)** | For a team of 1-3, one repo and one service is faster to develop, debug, and deploy than microservices. |
| **ADR Compliance** | ADR-001 explicitly mandates this architecture. ADR-013 extends it for AI. The current state is *by design*. |

### Cons

| Disadvantage | Detail | Severity |
|--------------|--------|----------|
| **No Domain Isolation** | AI failure can crash file operations. No bulkheading. | High |
| **Scaling Granularity** | Can't scale AI independently of SPE operations. | Medium (growing) |
| **Deployment Risk** | Every deploy restarts everything. No canary for individual domains. | Medium |
| **Cognitive Load** | 1,881-line Program.cs. New developer needs to understand all domains to work on one. | Medium |
| **DI Graph Complexity** | 99+ registrations make it hard to reason about lifetime management, especially scoped-in-singleton issues. | Medium |
| **Background Job Contention** | One processor for all job types. No priority queue. | Medium |
| **Cold Start Time** | Growing with each added module. | Low (growing) |
| **Test Isolation** | Integration tests require bootstrapping the entire application. Can't test AI independently. | Medium |

---

## 4. Recommended Enhancements

### Tier 1: Immediate (Within Current Architecture)

These changes preserve the monolith while addressing the most pressing concerns. **No new services required.**

#### 1.1 Modular Program.cs Decomposition

**Problem**: 1,881-line composition root.

**Solution**: Split `Program.cs` into domain-specific startup modules using the existing `AddXxxModule()` pattern, but go further:

```
Infrastructure/Startup/
├── CoreStartup.cs           // Auth, caching, resilience, health checks
├── SpeStartup.cs            // Graph client, SpeFileStore, document endpoints
├── AiStartup.cs             // OpenAI, RAG, chat, analysis endpoints
├── OfficeStartup.cs         // Office add-in services + endpoints
├── CommunicationStartup.cs  // Email, communication endpoints
├── FinanceStartup.cs        // Finance services + endpoints
├── WorkspaceStartup.cs      // Workspace, portfolio endpoints
└── WorkersStartup.cs        // All background service registration
```

`Program.cs` reduces to:
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddCoreServices();
builder.AddSpeModule();
builder.AddAiModule();
builder.AddOfficeModule();
builder.AddCommunicationModule();
builder.AddFinanceModule();
builder.AddWorkspaceModule();
builder.AddWorkerModule();
var app = builder.Build();
app.MapCoreEndpoints();
app.MapSpeEndpoints();
// ... etc
app.Run();
```

**Effort**: ~4-6 hours. Zero behavior change. Immediate clarity improvement.

#### 1.2 Background Job Prioritization

**Problem**: Single ServiceBusJobProcessor, all jobs compete equally.

**Solution**: Separate Service Bus queues by priority tier:

| Queue | Job Types | Concurrency |
|-------|-----------|-------------|
| `sdap-jobs-critical` | Document operations, user-initiated analysis | 5 |
| `sdap-jobs-standard` | Email processing, invoice extraction | 3 |
| `sdap-jobs-bulk` | RAG batch indexing, record matching sync, vector backfill | 2 |

Each queue gets its own `BackgroundService` processor with independent concurrency limits.

**Effort**: ~8 hours. Prevents bulk work from starving user-facing operations.

#### 1.3 Feature Flags for Domain Isolation

**Problem**: All domains always active. Can't disable AI during an incident.

**Solution**: Extend the existing `DocumentIntelligence:Enabled` pattern to all domains:

```json
{
  "Modules": {
    "Ai": { "Enabled": true },
    "Office": { "Enabled": true },
    "Finance": { "Enabled": true },
    "Communication": { "Enabled": true },
    "Workspace": { "Enabled": true }
  }
}
```

When disabled: endpoints return 503, DI modules skip registration, background jobs skip processing.

**Effort**: ~4 hours. Enables runtime domain isolation without redeployment.

#### 1.4 Resolve In-Memory State (OPS-05)

**Problem**: Analysis results lost on restart.

**Solution**: This is already identified as OPS-05 in post-deployment-work.md. **Prioritize it as the #1 production blocker.** Implement `WorkingDocumentService` backed by Redis (hot) + Dataverse (durable).

**Effort**: Already scoped in OPS-05.

### Tier 2: Medium-Term (Structured Monolith)

These changes introduce internal boundaries while keeping a single deployment. **Think "modular monolith" not "microservices."**

#### 2.1 Domain Module Contracts

**Problem**: Services reach across domains without clear interfaces.

**Solution**: Define explicit contracts between domains:

```
Modules/
├── Spe/
│   ├── ISpeModule.cs          // Public contract: GetFile, Upload, ListChildren
│   └── Internal/              // Private: SpeFileStore, GraphClientFactory
├── Ai/
│   ├── IAiModule.cs           // Public contract: Analyze, Search, Chat
│   └── Internal/              // Private: RagService, ChatSessionManager
├── Finance/
│   ├── IFinanceModule.cs      // Public contract: ClassifyInvoice, GetSummary
│   └── Internal/              // Private: extraction handlers
```

Cross-domain calls go through the public interface. This enables future extraction without changing consumers.

**Effort**: ~2-3 weeks across incremental PRs. Establishes the seams for future separation.

#### 2.2 Request-Scoped Resource Budgets

**Problem**: No per-request limits on downstream calls.

**Solution**: Implement a `ResourceBudget` scoped per-request that tracks:
- Graph API calls made (limit: 10 per request)
- OpenAI tokens consumed (limit: configurable per endpoint)
- AI Search queries executed (limit: 5 per request)

Exceeding budget returns 429 with clear error. Prevents a single chat message from consuming unlimited downstream resources.

**Effort**: ~1 week. Critical for cost control as AI usage scales.

#### 2.3 HostContext Compile-Time Safety

**Problem**: ChatHostContext propagation relies on runtime convention.

**Solution**: Use a required constructor parameter or `IHostContextAccessor` (similar pattern to `IHttpContextAccessor`) that makes the dependency explicit. Any service needing entity context must declare it — compilation fails if missing.

**Effort**: ~1 week. Eliminates a class of silent bugs.

### Tier 3: Future State (If/When Scale Demands)

These changes should only be considered when **measured metrics** show the monolith is a bottleneck. **Not before.**

#### 3.1 Extract AI as Separate App Service (Only If Needed)

**Trigger**: AI workload requires independent scaling (>60% CPU attributed to AI), or deployment frequency of AI features conflicts with SPE stability requirements.

**Approach**:
- Extract `AiStartup.cs` into `Sprk.Ai.Api` App Service
- AI service calls SPE service via internal HTTP (not Graph directly)
- Shared auth via Azure AD app-to-app token exchange
- Shared Redis for cross-service caching

**Why not now**: The coordination cost (service auth, network latency, distributed tracing, deployment orchestration) exceeds the benefit at current scale. The Tier 1/2 enhancements address all current pain points without this complexity.

#### 3.2 Container Apps for Background Jobs (Only If Needed)

**Trigger**: Background job processing needs independent scaling, or job cold start times become unacceptable.

**Approach**: Move `ServiceBusJobProcessor` and all handlers to Azure Container Apps with KEDA scaling based on queue depth.

**Why not now**: Service Bus + BackgroundService handles current volume. Queue-per-priority (Tier 1.2) addresses contention.

---

## Summary

| Question | Answer |
|----------|--------|
| **Is the BFF out of scope?** | Yes, it has grown well beyond its original SPE gateway purpose. It now serves 7 domains with 120+ endpoints. |
| **Is this a problem today?** | Moderate. The main risks are in-memory state loss (OPS-05), job contention, and cognitive complexity — not performance or availability. |
| **Should we split into microservices?** | **No, not yet.** The coordination benefits of the monolith (shared auth, in-process service calls, simple deployment) outweigh the isolation benefits of splitting — at current team size and scale. |
| **What should we do?** | **Tier 1 enhancements** (modular startup, job prioritization, feature flags, fix OPS-05) give 80% of the benefit at 10% of the cost. **Tier 2** (domain contracts, resource budgets) creates the seams for future extraction if ever needed. |

### Recommended Sequence

| Priority | Enhancement | Effort | Impact |
|----------|-------------|--------|--------|
| 1 | OPS-05: Persistent state (Redis + Dataverse) | Already scoped | Removes #1 production blocker |
| 2 | 1.1: Modular Program.cs decomposition | 4-6 hours | Immediate clarity, reduces merge conflicts |
| 3 | 1.3: Feature flags per domain | 4 hours | Runtime isolation, incident response |
| 4 | 1.2: Priority job queues | 8 hours | Prevents bulk work starving user operations |
| 5 | 2.1: Domain module contracts | 2-3 weeks | Future-proofing, testability |
| 6 | 2.2: Request-scoped resource budgets | 1 week | Cost control for AI scaling |
| 7 | 2.3: HostContext compile-time safety | 1 week | Eliminates silent propagation bugs |

**Key principle**: Invest in internal structure, not external separation. A well-structured monolith is better than premature microservices.
