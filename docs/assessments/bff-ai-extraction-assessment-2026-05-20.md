# BFF AI Extraction Assessment

> **Date**: 2026-05-20
> **Author**: AI assessment per [`projects/sdap-bff-api-remediation-fix/CC-PROMPT-bff-extraction-assessment.md`](../../projects/sdap-bff-api-remediation-fix/CC-PROMPT-bff-extraction-assessment.md)
> **Method**: Read-only code analysis, 4 parallel investigations across composition, dependency coupling, operational characteristics, and release cadence. No code was modified.
> **Status**: Findings for owner review. Recommendation is advisory.
> **Related**:
> - [ADR-013](../adr/ADR-013-ai-architecture.md) (current binding decision — "extend BFF for AI, no separate microservice")
> - [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §22.2 (extraction triggers, defers to Phase 3+)
> - [`projects/sdap-bff-api-remediation-fix/design.md`](../../projects/sdap-bff-api-remediation-fix/design.md) (BFF publish-debt remediation, currently paused for this assessment)

---

## Summary

The Spaarke BFF API is structurally AI-dominant: **AI code is 69% of `Services/` LOC** (73,028 of 105,526), **~57% of DI registrations** (~140 of ~244), and ~61% of unit test methods. All long-running / streaming endpoints (6+ SSE routes) are AI; zero CRUD endpoints stream. AI commits dominate the last 30 days (23 AI-only vs 0 CRUD-only). On size, velocity, and operational profile, the codebase looks ready for extraction.

However, the **architectural boundary is not clean**. There are **20 inbound CRUD→AI dependencies** — Finance (3), Workspace (4), Jobs (6 AI-coupled handlers), Dataverse (2), plus 5 endpoints/filters — that would have to be HTTP-ified, rewritten, or moved before extraction could happen safely. The estimated refactoring cost (per dependency analysis) is 3–4 weeks. The natural extraction surface — an MCP server (`Sprk.Insights.Mcp`) designed in the Insights Engine architecture — **does not exist yet**.

**Recommendation: Defer full extraction with re-assessment after Insights Engine Phase 1 lands (≈3–6 months).** Use the in-progress BFF remediation project to do "extraction-readiness" preparation in parallel — refactor the 20 CRUD→AI dependencies through documented interface boundaries, isolate AI job handlers in the codebase, mark the AI module subtree as an extraction candidate. This unlocks both immediate value (BFF remediation completes) and future option value (when the Insights Engine §22.2 quantitative triggers fire — or this structural assessment is rerun and concurs — extraction is cheap because the boundary is already clean).

Three quantitative triggers (>40% AI by LOC; long-running ops are AI; release cadence divergence) **strongly support extraction**. Two (dependency profile favorability; PR-scope independence) are **PARTIAL** — favorable directionally but not yet at thresholds. One (MCP server exists with minimal CRUD deps) is **N/A** — the MCP server is designed but not implemented. Doing the extract-readiness prep work now is the action that's right whether the final decision is "extract in 6 months" or "defer indefinitely."

---

## 1. Codebase composition

### Totals

| Metric | Value |
|---|---|
| Total LOC in `Sprk.Bff.Api` | **183,463** (834 C# files, excluding obj/bin) |
| LOC in `Services/` | **105,526** (57.5% of total) |
| LOC in `Services/Ai/` | **73,028** (69.2% of Services, ~40% of project total) |
| AI classes (top-level types) in `Services/Ai/` | **478** (~73% of all Services classes) |
| Total endpoint files | 74 (61 in `Api/`, 13 in `Endpoints/`) |
| AI endpoint files | **25 (~34%)** under `/api/ai/*` |
| CRUD endpoint files | 47 (~63%) — Documents, Workspace, SpeAdmin, Finance, Communication, Office, Email, External, Reporting, Registration, etc. |
| Total DI registrations | **244+** (across `Program.cs` + 30 DI module files) |
| AI DI registrations | **~140 (57%)** — AnalysisServicesModule (45), AiChatModule (35), AiCapabilitiesModule (15), AiModule (15), AgentModule (15), AiPersistenceModule (12), AiSafetyModule (8) |
| CRUD DI registrations | **~80 (33%)** — Finance (8), Workspace (10), Communication (12), Email (8), Office (8), Registration (8), SpeAdmin (12), Documents (5), ExternalAccess (6) |
| Shared DI registrations | ~24 (10%) — Authorization, Graph, Cache, SpaarkeCore, Telemetry, JobProcessing framework, CORS, RateLimiting |

### LOC by Services/ subfolder

| Folder | LOC | Files | Classes | Category |
|---|---:|---:|---:|---|
| **Ai** | 73,028 | 292 | 478 | AI |
| Jobs | 6,357 | 24 | 42 | Shared framework (but ~8 of 13 handlers are AI-coupled) |
| Communication | 5,140 | 33 | 40 | CRUD |
| Office | 3,771 | 9 | 19 | CRUD |
| Workspace | 3,393 | 10 | 18 | CRUD (with AI prefill/briefing) |
| Finance | 3,280 | 16 | 32 | CRUD (with AI-driven invoice analysis) |
| Email | 2,794 | 8 | 27 | CRUD |
| Registration | 2,249 | 10 | 19 | CRUD |
| SpeAdmin | 1,356 | 5 | <10 | CRUD |
| Scopes | 1,120 | 3 | <10 | Shared (configuration) |
| RecordMatching | 910 | 5 | <10 | CRUD (ML-flavored, gated by DocumentIntelligence flag) |
| Dataverse | 165 | 2 | <10 | Shared |

### Pre-release packages (risk surface)

The Sprk.Bff.Api csproj contains **3 pre-release AI packages** with documented chain-compat pinning rationale:

- `Azure.AI.Projects 1.0.0-beta.8`
- `Microsoft.Agents.AI 1.0.0-rc1`
- `Azure.AI.OpenAI 2.8.0-beta.1`

All three are AI-scoped. Their churn risk is contained to the AI subsystem but currently shares a deploy pipeline with CRUD.

---

## 2. Dependency coupling

### A. AI → outside (extraction cost: outbound)

| Bucket | Count | Notes |
|---|---:|---|
| **AI → Shared infrastructure** | ~60 injections | `IDistributedCache` (14), `ILogger` (15+), `IHttpClientFactory` (8), `IGraphClientFactory` (3), `IMemoryCache` (6), `IOptions<T>` (8+), `IHttpContextAccessor` (6), `IServiceProvider` (5). **Low extraction cost** — duplicate-or-share via library. |
| **AI → CRUD** | **27 direct dependencies** | `IDocumentDataverseService` (8 files), `IGenericEntityService` (9), `IAnalysisDataverseService` (3), `IFieldMappingDataverseService` (3), CommunicationService (1), RecordMatching (1), Jobs enqueue (2). **Medium extraction cost** — must either HTTP-ify or move document/dataverse interfaces into shared library. |
| **AI → other AI** | ~120 | Internally cohesive — would move together. Includes `IOpenAiClient` (23 consumers), `IScopeResolverService` (15), `IPlaybookService` (8), `ITemplateEngine`/`IChatClient`/`IRagService`/`ITextExtractor` (6–10 each). |

### B. CRUD → AI (extraction cost: inbound — the real blockers)

| Module | Inbound deps | What couples |
|---|---:|---|
| **Finance** | 3 files | `InvoiceAnalysisService` injects `IOpenAiClient` + `IPlaybookService`; `InvoiceSearchService` uses semantic search; tool handlers (`InvoiceExtractionToolHandler`, `FinancialCalculationToolHandler`) live in `Finance/Tools/` but invoke AI |
| **Workspace** | 4 files | `BriefingService` injects `IOpenAiClient` (optional, with 3s timeout fallback — good design); `MatterPreFillService` + `ProjectPreFillService` use AI for suggestions; `WorkspaceAiService` is a composite orchestrator |
| **Jobs** | 6 handlers | `AppOnlyDocumentAnalysisJobHandler`, `EmailAnalysisJobHandler`, `AttachmentClassificationJobHandler`, `RagIndexingJobHandler`, `InvoiceExtractionJobHandler`, `ProfileSummaryJobHandler` — all assume analysis orchestration is local |
| **Dataverse** | 2 files | `DataverseUpdateHandler` imports `Sprk.Bff.Api.Services.Ai` |
| **Endpoints + Filters** | 5+ | Authorization filters (`AiAuthorizationFilter`, `AnalysisAuthorizationFilter`, `PlaybookAuthorizationFilter`) cross the boundary by design |
| **TOTAL** | **20 inbound service deps** | These are the extraction blockers. Each requires either HTTP-ification, code movement into the AI service, or a shared interface library |

### C. Project references

- **`Spaarke.Dataverse`**: used by 24 AI files + 13 CRUD files. Shared, but AI-heavier. Either becomes a shared library both services depend on (likely), or a Dataverse-access HTTP boundary is built (complex).
- **`Spaarke.Core`**: 0 explicit imports from AI folders (sampling). Genuinely shared utility code; safe.

### D. Shared infrastructure that would need extraction handling

| Component | AI consumers | Extraction approach |
|---|---:|---|
| `IDistributedCache` (Redis) | 14 | Share via config (both services point at same Redis) |
| `IHttpClientFactory` | 8 | Standard .NET; each service has its own registry |
| `IGraphClientFactory` | 3 | Duplicate config or share via library |
| `IDataverseClient` | indirect via interfaces | **Critical decision point** — share Dataverse access library OR build Dataverse-access HTTP boundary |
| `IOpenAiClient` | 23 | Stays in AI service entirely |
| Telemetry helpers | 3+ | Duplicate per service (each has its own OpenTelemetry stack) |

### E. Boundary cleanliness rating

**MODERATE-TO-TANGLED (6/10).** Sub-agent's verbatim assessment: AI is largely interface-driven (good), but Finance/Workspace/Jobs all hardwire to AI interfaces (bad). Tool handlers living inside AI but extending CRUD behavior (`SendCommunicationToolHandler` etc.) is a cohesion violation. Estimated **3–4 weeks of refactoring** to clean the boundary before any extraction could happen safely.

---

## 3. Operational characteristics

### Long-running / streaming endpoints

| Endpoint | Profile | Category |
|---|---|---|
| `POST /api/ai/chat/sessions/{id}/messages` | SSE streaming, token-by-token, timeout disabled, X-Accel-Buffering disabled for <500ms TTFB | AI |
| `POST /api/ai/chat/sessions/{id}/refine` | SSE streaming | AI |
| `POST /api/ai/analysis/execute` | SSE streaming | AI |
| `POST /api/ai/analysis/{id}/continue` | SSE streaming | AI |
| `POST /api/ai/playbooks/{id}/run` | SSE streaming with multi-step tool execution | AI |
| `POST /api/ai/playbook-builder/assistant` | SSE streaming | AI |
| `POST /api/ai/daily-briefing` | SSE streaming (inferred) | AI |

**All 6+ streaming endpoints are AI. Zero CRUD streaming endpoints.** This is the single strongest operational divergence signal.

### Hosted services / BackgroundServices

| Total | 16 |
|---|---|
| AI | 9 (`PlaybookIndexingBackgroundService`, `CapabilityManifestInitializer`, `ManifestRefreshService`, `PlaybookSchedulerService`, `ServiceBusJobProcessor`, `DocumentVectorBackfillService`, `EmbeddingMigrationService`, `ScheduledRagIndexingService`, `RecordSyncJob`, `TodoGenerationService`) |
| CRUD | 7 (`DemoExpirationService`, `InboundPollingBackupService`, `DailySendCountResetService`, `CommunicationJobProcessor`, `SpeDashboardSyncService`, `IndexingWorkerHostedService`) |

`ServiceBusJobProcessor` is categorized AI here because of the 13 job handlers it dispatches to, ~8 are AI-related (analysis, RAG indexing, embedding migration, profile summary, email analysis, invoice extraction).

### Named HttpClient inventory

**10+ named HttpClients**, 8 of them AI-scoped (AnalysisActionService, AnalysisSkillService, AnalysisKnowledgeService, AnalysisToolService, ScopeResolverService, PlaybookService, NodeService, PlaybookSharingService, LlamaParseClient, DataverseIndexSyncService). Polly policies are largely absent from registrations — resilience relies on SDK-level retry. The LLM SDKs (IChatClient, Azure SDK) bypass HttpClient registration entirely.

### Memory hotspots

**No corpus-scale in-memory caches identified.** All corpus-sized data (documents, embeddings, records) lives in Redis or AI Search. In-process state is metadata-scale only:
- `CapabilityManifest`: bounded by capability metadata record count (dozens–hundreds)
- `OrchestratorPromptBuilder`: 1 entry per manifest hash (~1–10 KB)
- `NodeExecutorRegistry`, `ToolHandlerRegistry`: bounded by static type count
- `ReportingProfileManager` (CRUD): bounded by Power BI report count

This is a favorable signal — memory profile does not diverge dramatically between AI and CRUD workloads today. Extraction would NOT be driven by RAM pressure.

---

## 4. Release cadence

### Git log analysis (today: 2026-05-20)

| Folder | 30d commits | 30d LOC | 30d authors | 90d commits | 90d LOC | 180d commits | 180d LOC |
|---|---:|---:|---:|---:|---:|---:|---:|
| **AI** | **23** | **17,719** | 2 | 93 | 69,694 | **150** | **121,242** |
| Office | 0 | 0 | 0 | 3 | 1,978 | 11 | 6,432 |
| SpeAdmin | 0 | 0 | 0 | 1 | 1,590 | 1 | 1,590 |
| Workspace | 0 | 0 | 0 | 9 | 2,340 | 11 | 4,806 |
| Communication | 0 | 88 | 1 | 22 | 6,439 | 22 | 6,439 |
| Finance | 0 | 0 | 0 | 3 | 61 | 12 | 4,191 |
| **All CRUD combined** | **<1 avg** | **88** | <2 | ~38 | ~12,400 | ~57 | ~23,458 |

**AI vs CRUD ratio over 180 days**: 121,242 / 23,458 = **5.2× LOC churn**.
**30-day velocity ratio**: AI 23 commits vs CRUD ~1 = **>20×**.

### PR-scope analysis (last 30 commits to `src/server/api/Sprk.Bff.Api/`)

- **AI-only**: 19 (63%)
- **CRUD-only**: 0 (0%)
- **Mixed (AI + CRUD)**: 0 (0%)
- **Cross-cutting** (Program.cs, csproj, config, tests, docs): 11 (37%)

Single-domain rate: 63% — above the 50% interleaving threshold, below the 70% independence threshold. Notable: **zero CRUD-only commits in the last 30**. CRUD is essentially dormant; AI is the only active development surface in the BFF.

### Feature flag inventory

**6 independent AI feature gates**:
- `Analysis:Enabled` (master switch)
- `Analysis:MultiDocumentEnabled` (Phase 2 feature)
- `DocumentIntelligence:Enabled` (master switch)
- `DocumentIntelligence:StreamingEnabled` (SSE vs background)
- `DocumentIntelligence:RecordMatchingEnabled` (Phase 2 feature)
- `Analysis:EnableDocxExport` / `EnablePdfExport` / `EnableEmailExport` / `EnableTeamsExport` (per-channel)

**CRUD feature gates: 0** — Office, Workspace, Communication, Finance have no toggles.

This asymmetry means **AI can be disabled mid-deploy without touching CRUD**, but **any CRUD change forces AI to be deployed (or rolled back) as collateral**. The release pressure is one-directional.

### Author overlap

3 of 4 distinct AI authors also commit to CRUD over 180 days. Author overlap rated **HIGH**. Extraction would not benefit from existing team specialization — the same humans context-switch between domains today and would do so across two repos.

---

## 5. Test surface

### Unit tests (`tests/unit/Sprk.Bff.Api.Tests/`)

| Metric | Value |
|---|---|
| Total test files | 255 |
| Total `[Fact]` + `[Theory]` | **4,557** (4,311 + 246) |
| AI tests (estimated) | **~2,800 (~61%)** — concentrated in `Services/Ai/` (121 files) + `Api/Ai/` (17 files) |
| CRUD tests (estimated) | ~1,400 (~31%) |
| Shared / infrastructure / filters | ~357 (~8%) |

### Integration tests (`Spe.Integration.Tests`)

| Metric | Value |
|---|---|
| Files | 24 |
| Test methods | 236 |
| External deps (AI) | Azure OpenAI, AI Search, Cosmos DB, Microsoft Graph |
| External deps (CRUD) | Dataverse, Service Bus, Microsoft Graph |
| Strategy | Real Azure services for OpenAI/Search/Dataverse; Service Bus often mocked |

### Shared fixtures

- `WebApplicationFactory<Program>` test host applies to both AI and CRUD tests
- `TenantAuthorizationFilter` + idempotency filter tests cross both
- `IDocumentStorageResolver`, `IDataverseClient` mocked in unit, real in integration
- Extraction would need to either split the WebApplicationFactory (one per service) or build a "fake" remote service for cross-service tests

---

## 6. MCP server status

**MCP server does not exist in the BFF codebase.** Zero matches for `mcp`, `MCP`, or `ModelContextProtocol` across `src/server/api/Sprk.Bff.Api/`.

**Designed in [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §15** as `Sprk.Insights.Mcp`, scoped for:
- **Tools**: `predict_matter_cost`, `find_comparable_matters`, `assess_matter_risks`, `summarize_matter_closure`, `get_matter_facts`, `analyze_spend_trends`
- **Resources**: read-only Engine views
- **Prompts**: honesty-contract fragments
- **Auth**: OBO; data trimming via existing `accessibleMatterSet`
- **Dependencies by design**: HIGH on AI/Insights code, LOW on CRUD

This is **interpretive signal**:
1. The Insights Engine subsystem (the natural extraction target) is itself pre-implementation
2. No natural extraction surface exists in code yet
3. When the MCP server lands, it would be the obvious first thing to extract — but it's a separate service from "extract Services/Ai/** wholesale"

---

## 7. Trigger evaluation

The CC-PROMPT defined five quantitative triggers. Evaluation against the evidence:

### Trigger 1: AI code is >40% of `Sprk.Bff.Api` by line count or class count

**YES — strongly.**
- **LOC**: AI is 69% of `Services/` (73,028 of 105,526). At project level, AI in `Services/Ai/` alone is ~40% of total 183,463 LOC.
- **Classes**: 478 AI classes in `Services/Ai/` = ~73% of all Services classes.
- **DI registrations**: ~140 of ~244 = 57%.

All three measurements clear the 40% threshold; LOC and class count clear it by ~30 percentage points.

### Trigger 2: Cross-subsystem dependency profile is favorable for extraction

**PARTIAL.**
- AI→Shared (60): healthy, expected, low extraction cost.
- AI→CRUD (27): medium — concentrated on Dataverse / document metadata access (`IDocumentDataverseService` 8 uses, `IGenericEntityService` 9 uses). Manageable if `Spaarke.Dataverse` becomes a shared lib.
- **CRUD→AI (20): the real problem.** Finance (3), Workspace (4), Jobs (6), Dataverse (2), Endpoints/Filters (5+). Each one needs to be HTTP-ified, moved, or refactored through an interface boundary before extraction is safe.
- Sub-agent's qualitative rating: MODERATE-TO-TANGLED (6/10), ~3–4 weeks of refactoring required.

Directionally favorable (most AI code is well-encapsulated internally), but not yet at the threshold where extraction is "lift and shift."

### Trigger 3: Background services or long-running operations in the BFF are predominantly AI-related

**YES — overwhelmingly.**
- 6+ streaming SSE endpoints: 100% AI.
- 0 CRUD streaming endpoints.
- 16 hosted services: 9 AI (56%), 7 CRUD.
- `ServiceBusJobProcessor` (technically shared infrastructure) dispatches to 13 handlers, ~8 of which are AI-coupled.

Streaming traffic is the strongest single signal — the BFF's request-handling profile already has two divergent shapes (request-response for CRUD, stream-for-minutes for AI).

### Trigger 4: >70% of recent BFF PRs touch only AI or only CRUD, rarely both

**PARTIAL.**
- 63% single-domain (19 AI-only + 0 CRUD-only / 30) — below 70% but well above 50%.
- 0% mixed (zero commits touch both AI and CRUD in the same change).
- 37% cross-cutting (Program.cs, csproj, tests, docs).

**The 0% mixed rate is more interesting than the 63% headline.** When commits aren't single-domain, they're cross-cutting infrastructure — never AI+CRUD interleaved. Extraction would not split any existing PRs in half. The 37% cross-cutting commits would still be cross-cutting in a two-service world (you'd still need to touch BFF.csproj AND Insights.csproj for shared changes).

### Trigger 5: MCP server (if it exists) has zero or minimal CRUD-code dependencies

**N/A.**
- MCP server does not exist in code.
- Designed in INSIGHTS-ENGINE-ARCHITECTURE.md §15 with intentionally low CRUD coupling (HIGH AI, LOW CRUD).
- When the MCP server lands as part of Insights Engine Phase 1–2, it will be the natural first extraction unit — easier than extracting `Services/Ai/**` wholesale.

### Trigger summary

| # | Trigger | Verdict | Evidence strength |
|---|---|---|---|
| 1 | AI >40% by LOC/class | **YES** | Very strong (69% / ~73%) |
| 2 | Dependency profile favorable | **PARTIAL** | Mixed — internal cohesion good, inbound coupling problematic |
| 3 | Long-running ops are AI | **YES** | Very strong (100% of streaming) |
| 4 | PR scope >70% single-domain | **PARTIAL** | 63% — just below threshold; 0% mixed is notable |
| 5 | MCP server has minimal CRUD deps | **N/A** | Designed but not built |

**Score**: 2 strong YES, 2 PARTIAL, 1 N/A. The codebase is structurally ready in size/operational shape but not yet ready in boundary cleanliness.

---

## 8. Recommendation rationale

### Recommendation: Defer full extraction. Reassess in 3–6 months after Insights Engine Phase 1 lands.

In the meantime, use the in-progress BFF remediation project ([`projects/sdap-bff-api-remediation-fix/design.md`](../../projects/sdap-bff-api-remediation-fix/design.md)) to do **extraction-readiness preparation** as a parallel outcome.

### Why not extract now

1. **The boundary isn't clean.** 20 inbound CRUD→AI dependencies need refactoring whether we extract or not. Doing it inside one process is safer than doing it across an HTTP boundary while also building a new service. Extraction-with-refactoring is two simultaneous risks.

2. **No quantitative §22.2 trigger has fired.** [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §22.2 binds extraction to specific reactive triggers: p95 latency degradation ≥30%, Engine compute ≥40% of BFF spend, or new surfaces needing Engine access without full BFF. This assessment surfaces **structural** triggers (size, operational shape, coupling) but does not show that the BFF is currently struggling operationally. Acting on structural-only signal contradicts the architecture document's explicit decision policy.

3. **The natural extraction surface (MCP server) doesn't exist yet.** When `Sprk.Insights.Mcp` lands as part of Insights Engine Phase 1–2, it provides a clean, small extraction unit that validates extraction patterns at low risk. Extracting `Services/Ai/**` wholesale first means doing the hard thing before the easy thing.

4. **Author overlap is HIGH.** Same humans commit to both AI and CRUD. Extraction does not unlock team specialization — it just makes the same people work across two repos. Social cost is real and not offset by the team-shape benefit extraction usually provides.

5. **Insights Engine Phase 1 will sharpen, not blur, the surface.** Engine code will land with explicit Fact/Observation/Inference taxonomy and architectural separation by design. Reassessing AFTER Phase 1 lands gives a clearer extraction target. Extracting BEFORE Phase 1 lands means re-extracting (or large in-flight rework) when Phase 1's structure is added.

### Why not defer indefinitely (and just do BFF remediation)

1. **The signals are strong enough to act on, just not extract on.** 69% AI by LOC, 5.2× churn ratio, 100% of streaming AI — these are not small differences. Pretending the BFF is "balanced" would be denying clear evidence.

2. **The 20 CRUD→AI dependencies are wrong even within one process.** Finance using `IPlaybookService` directly, Workspace `BriefingService` injecting `IOpenAiClient` — these are violations of clean architecture inside a single service, never mind across services. Refactoring them improves the codebase even if extraction never happens.

3. **Pre-release AI packages are a real risk.** `Azure.AI.Projects beta.8`, `Microsoft.Agents.AI rc1`, `Azure.AI.OpenAI 2.8.0-beta.1` all churn fast. Containing their blast radius via an architectural seam (even one that's not yet a service boundary) reduces deploy risk for the entire BFF.

4. **Once Insights Engine §22.2 triggers fire (latency, compute), there's no time to do the prep work — you're already in reactive mode.** The cost of doing the prep work proactively is small; the cost of doing it under operational pressure is large.

### "Extraction-readiness" scope — what to add to BFF remediation

Add this as **Outcome E** to [`projects/sdap-bff-api-remediation-fix/design.md`](../../projects/sdap-bff-api-remediation-fix/design.md):

1. **Refactor the 20 CRUD→AI inbound dependencies** through a documented `Sprk.Bff.Api.Ai.PublicContracts` namespace. Inside one process today; potentially the inter-service contract tomorrow.
2. **Separate AI vs CRUD job handlers in `JobProcessingModule`** — they currently coexist; mark them with explicit attribution. ~8 AI-coupled handlers should be discoverable as such.
3. **Mark `Services/Ai/**` + `AnalysisServicesModule` + `AiChat/Safety/Capabilities/Persistence/AgentModule` as an "extraction candidate" subtree** in `Sprk.Bff.Api/CLAUDE.md`. Future contributors know what's in vs out of the AI surface.
4. **Add publish-side ADR-029 wording** that codifies: "BFF is single deployable today; AI extraction is Phase 3+ per Insights Engine §22.2; structural readiness assessment runs annually."
5. **Set a re-assessment date** — 2026-11-20 (6 months out, or sooner if Insights Engine Phase 1 lands sooner). Re-run this exact prompt; compare findings.

This adds ~3–5 days of work to BFF remediation but yields: clean inbound boundary + documented extraction candidate + dated future reassessment + ADR alignment. It's the highest-leverage thing to do regardless of the eventual extraction decision.

### What would change this recommendation

The recommendation flips to "**MCP-only extraction now**" if any of the following becomes true in the next 6 months:

- `Sprk.Insights.Mcp` is shipped as part of Insights Engine Phase 1 (the MCP server has natural low CRUD coupling and is a small first extraction)
- BFF p95 latency degrades ≥30% (triggers Insights Engine §22.2 condition A)
- Engine compute usage ≥40% of total BFF spend (triggers §22.2 condition B)
- A new external surface needs Engine access without the full BFF (triggers §22.2 condition C)

The recommendation flips to "**full extraction now**" only if BOTH a §22.2 trigger fires AND the boundary-cleanup work from this design's Outcome E has already completed.

---

## 9. Risks and caveats

| Risk | Mitigation |
|---|---|
| **Classification of `Communication`, `Workspace`, `Finance` as "CRUD"** is partly judgment. They have AI features (briefing generation, invoice analysis, etc.). If reclassified as AI, the AI share grows further (potentially to 75%+ of LOC). The CRUD-only commit count (already 0 in 30 days) does not change. | Recommendation stands either way — the structural picture is unambiguous. |
| **Author overlap data** treats `spaarke-dev` (bot) and `Spaarke Dev` as separate from `spaarke-dev` (human). Real human author count may be smaller. | Doesn't affect recommendation — high overlap regardless. |
| **20 inbound CRUD→AI dependency count** is from grep + import analysis. Reflection-loaded dependencies and dynamic DI string types may be missed. Real count may be higher, never lower. | Phase 1 of BFF remediation will run a runtime reflection probe that can validate this number. |
| **63% single-domain PR rate** is from last 30 commits only. A longer window may show higher or lower interleaving. The 30-commit window covers ~5 weeks of activity. | Recommendation does not hinge on PR-scope independence — it's PARTIAL evidence, not load-bearing. |
| **`Spaarke.Dataverse` is shared today**. Extraction would force a decision: shared library vs Dataverse HTTP boundary. Either is a substantial architectural change in its own right. | This is a known cost; flagged for Phase 2 of any future extraction project. |
| **This assessment is structural, not operational.** It doesn't measure actual production p95 latency, RAM pressure, error budgets, or cost. The Insights Engine §22.2 triggers are operational — they may fire (or not) independently of this assessment's signals. | Re-run this prompt AND check App Insights / cost dashboards before any future extraction commitment. |
| **The recommendation depends on Insights Engine Phase 1 landing within the next 6 months.** If Phase 1 is deprioritized or stretches >12 months, the structural debt accumulates further and the case for extraction strengthens. | Re-assessment date (2026-11-20) is unconditional — even if Phase 1 hasn't shipped, rerun then. |
| **No evaluation of cost/ROI of extraction.** Extraction has real costs (new App Service, new deploy pipeline, new observability, new auth boundary, new SDK contract, ongoing maintenance of 2 services). This assessment focuses on technical readiness only. | Owner should weigh extraction costs against the size/operational/cadence benefits identified. |

---

## 10. Sources

| Source | Used for |
|---|---|
| `src/server/api/Sprk.Bff.Api/` (read-only file analysis) | Sections 1, 2, 3, 5 |
| `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` | Pre-release packages, project refs |
| `src/server/api/Sprk.Bff.Api/CLAUDE.md` | DI count baseline, Kiota constraint |
| `tests/unit/Sprk.Bff.Api.Tests/` | Test surface counts |
| `git log` on `Services/Ai/**` and CRUD folders | Release cadence |
| [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) | MCP server design, §22.2 triggers |
| [ADR-013](../adr/ADR-013-ai-architecture.md) | Current binding decision |
| [ADR-001](../adr/ADR-001-minimal-api.md) | Single Minimal API constraint |
| [ADR-010](../adr/ADR-010-di-minimalism.md) | DI minimalism baseline |
| [`projects/sdap-bff-api-remediation-fix/design.md`](../../projects/sdap-bff-api-remediation-fix/design.md) | Parallel project context |

---

*End of assessment. Owner decision required on: (a) accept "defer + extract-readiness prep" recommendation, (b) overruled and proceed with extraction now, (c) overruled and proceed with BFF remediation as originally scoped (no extract-readiness prep). The choice shapes the next revision of design.md.*
