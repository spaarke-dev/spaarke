# Finance Intelligence Module R1 — AI Context

## Project Status

| Field | Value |
|-------|-------|
| **Status** | 🚧 **IMPLEMENTATION COMPLETE - PENDING DEPLOYMENT** |
| **Implementation Complete** | 2026-02-12 |
| **Last Updated** | 2026-02-12 |
| **Tasks Completed** | 34/35 (97%) - Task 090 pending post-deployment |
| **Next Action** | Deploy to dev environment and run validation |

## Quick Reference

### Key Files
| File | Purpose |
|------|---------|
| [spec.md](spec.md) | AI implementation specification (source of truth) |
| [plan.md](plan.md) | Implementation plan with WBS |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task status overview |
| [current-task.md](current-task.md) | Active task state (for context recovery) |
| [Design Document](Spaarke_Finance_Intelligence_MVP_Design%202.md) | Original design specification |

### Project Metadata
| Field | Value |
|-------|-------|
| Branch | `work/financial-intelligence-module-r1` |
| Project Path | `projects/financial-intelligence-module-r1/` |
| API Path | `src/server/api/Sprk.Bff.Api/` |
| PCF Path | `src/client/pcf/` |
| Solutions Path | `src/solutions/` |

## Context Loading Rules

| When Working On | Load These First |
|----------------|-----------------|
| **Any task** | This file + `current-task.md` + task POML file |
| **API endpoints** | `.claude/constraints/api.md` + `.claude/patterns/api/endpoint-definition.md` |
| **Job handlers** | `.claude/constraints/jobs.md` + `.claude/patterns/api/background-workers.md` |
| **AI services** | `.claude/constraints/ai.md` + `.claude/patterns/ai/text-extraction.md` |
| **Dataverse** | `.claude/patterns/dataverse/entity-operations.md` |
| **PCF control** | `.claude/constraints/pcf.md` + `.claude/patterns/pcf/control-initialization.md` + `.claude/patterns/pcf/theme-management.md` |
| **Caching** | `.claude/patterns/caching/distributed-cache.md` |
| **Search/RAG** | `docs/guides/RAG-ARCHITECTURE.md` |

---

## MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

**Trigger phrases** → Invoke `task-execute`:
- "work on task X", "continue", "next task", "keep going", "resume task X"

**Why**: task-execute ensures knowledge files are loaded, checkpointing occurs, quality gates run, and progress is recoverable.

---

## Key Technical Constraints

### AI Processing
- MUST use `GetStructuredCompletionAsync<T>` for classification and extraction (constrained decoding, not prompt engineering)
- MUST NOT log document content, extracted text, or prompts (ADR-015)
- MUST NOT place document bytes in job payloads (ADR-015)
- VisibilityState is ALWAYS set deterministically in handler code, NEVER by LLM
- Classification model: `gpt-4o-mini`; Extraction model: `gpt-4o`
- `ClassificationResult` contains AI output only — entity matching is handler-side post-AI-call
- Extraction handler reads reviewer corrections from Dataverse records, NOT from job payload

### Job Handlers
- All handlers MUST be idempotent (alternate keys for upsert)
- BillingEvent alt key: `sprk_invoiceid` + `sprk_linesequence` (NOT correlationId)
- SpendSnapshot alt key: `sprk_matterid` + `sprk_periodtype` + `sprk_periodkey` + `sprk_bucketkey` + `sprk_visibilityfilter`
- All jobs use standard `JobContract` envelope with `CorrelationId` propagation
- `InvoiceExtractionJobHandler` sets `sprk_invoice.sprk_status = Reviewed` on success
- Feature flag: `AutoClassifyAttachments` on `EmailProcessingOptions` (default: false)

### Snapshot/Signal Logic
- MVP: Month + ToDate periods only (Quarter/Year post-MVP)
- Velocity: MoM only for MVP (QoQ/YoY post-MVP)
- VelocityPct = (current - prior) / prior * 100; null when prior = 0
- Budget warning threshold: 80% (configurable)
- Velocity spike threshold: 50% increase (configurable)

### PCF Panel
- Fluent UI v9 exclusively (`@fluentui/react-components`)
- React 16 APIs only (`ReactDOM.render`, NOT `createRoot`)
- Bundle < 5MB; `platform-library` declaration in manifest
- Dark mode + high contrast required
- Import shared components from `@spaarke/ui-components`

### Endpoints
- All finance endpoints under `/api/finance/` route group
- Endpoint authorization filters (NOT global middleware)
- ProblemDetails for all errors with stable errorCode
- Async operations: 202 + jobId + statusUrl

### Caching
- Finance summary: Redis key `matter:{matterId}:finance-summary`, TTL 5min
- Invalidation: Explicit delete after snapshot write (TTL is safety net only)
- Use `IDistributedCache` (StackExchangeRedisCache adds `sdap:` prefix automatically)

## Canonical Implementations to Follow

| What | File | Notes |
|------|------|-------|
| Job handler | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Enqueue chaining pattern |
| Job handler | `Services/Jobs/Handlers/RagIndexingJobHandler.cs` | Indexing pattern |
| Job contract | `Services/Jobs/JobContract.cs` | Standard envelope |
| Job interface | `Services/Jobs/IJobHandler.cs` | Handler interface |
| OpenAI client | `Services/Ai/OpenAiClient.cs` | Extend with `GetStructuredCompletionAsync<T>` |
| Playbook loading | `Services/Ai/PlaybookService.cs` | Load prompts from Dataverse |
| Text extraction | `Services/Ai/TextExtractorService.cs` | PDF/Office/image extraction |
| Entity matching | `Services/RecordMatching/RecordMatchService.cs` | Multi-signal confidence scoring |
| DI module | `Infrastructure/DI/SpaarkeCore.cs` | Module extension method pattern |
| Endpoint group | `Api/Ai/AnalysisEndpoints.cs` | Minimal API MapGroup pattern |
| Auth filter | `Api/Filters/DocumentAuthorizationFilter.cs` | Resource authorization |
| Options class | `Configuration/EmailProcessingOptions.cs` | Options with validation |
| Redis cache | `Services/Ai/EmbeddingCache.cs` | IDistributedCache pattern |

## Decisions Made

| Decision | Choice | Date | Rationale |
|----------|--------|------|-----------|
| BillingEvent alt key | `invoiceId + lineSequence` (no correlationId) | 2026-02-11 | Re-extraction creates new correlationId; old key would create duplicates |
| SpendSnapshot alt key | 5-field composite | 2026-02-11 | Enables idempotent upsert on re-generation |
| Invoice status transition | ExtractionJobHandler sets Reviewed | 2026-02-11 | Single handler owns transition |
| Snapshot periods (MVP) | Month + ToDate only | 2026-02-11 | Quarter/Year additive post-MVP |
| Velocity (MVP) | MoM hardcoded | 2026-02-11 | QoQ/YoY when Quarter/Year periods added |
| ClassificationResult shape | AI output only (no MatterSuggestion) | 2026-02-11 | Entity matching is handler-side, not AI |
| Reviewer corrections | Handler reads from Dataverse records | 2026-02-11 | ADR-015: IDs only in payloads |
| Review queue UX | Dataverse view only | 2026-02-11 | PCF Dataset control is future upgrade |
| Finance PCF panel | In scope for R1 | 2026-02-11 | Owner requested full panel |
| Invoice indexing | New InvoiceIndexing JobType | 2026-02-11 | Not reusing generic RagIndexing |
| Chunk enrichment | Contextual metadata prepended | 2026-02-11 | Better semantic ranking for financial queries |
| Budget entity name | `sprk_budget` (NOT `sprk_budgetplan`) | 2026-02-11 | Actual entity name differs from design doc |
| Budget status mapping | Use existing `sprk_budgetstatus` (map Active→Open(2) in code) | 2026-02-11 | 8-option choice already exists; no schema change |
| Lookup naming convention | Bare names (no `id` suffix): `sprk_matter`, `sprk_invoice` | 2026-02-11 | Matches existing Dataverse convention |
| Invoice record type | `sprk_regardingrecordtype` (Lookup→`sprk_recordtype_ref`) | 2026-02-11 | Uses existing reference table, not a Choice field |
| Invoice status field | `sprk_invoicestatus` (not `sprk_status`) | 2026-02-11 | Avoids collision with generic status fields |
| Reviewer identity | `sprk_invoicereviewedby` (Lookup→`sprk_contact`) | 2026-02-11 | Contact entity, not systemuser |
| Document field count | 16 fields on `sprk_document` (not 13) | 2026-02-11 | Design doc adds reviewedby, reviewedon, relatedvendororg |

## Implementation Notes

- `Program.cs` will need 1 line: `builder.Services.AddFinanceModule(builder.Configuration);`
- PR #143 (playbook node builder) also touches `Program.cs` — rebase after merge
- Playbook prompts stored in Dataverse `sprk_playbook` records, NOT in source code
- Invoice index uses `text-embedding-3-large` (3072 dimensions) — same as production
- `BudgetPlan.sprk_status` transitioned manually for MVP (Draft/Active/Closed)

## Implementation Complete (Deployment Pending)

**Implementation Complete**: 2026-02-12

### Deliverables Summary
- ✅ **Phase 1 (Foundation)**: 9 tasks - Dataverse schema, structured output capability, prompt templates
- ✅ **Phase 2 (AI + Handlers)**: 13 tasks - Classification, extraction, entity matching, snapshot generation, signals
- ✅ **Phase 3 (RAG + Search)**: 5 tasks - Invoice search index, indexing handler, search service
- ✅ **Phase 4 (Integration + Polish)**: 7 tasks - Finance summary endpoint, VisualHost charts, tuning guides, integration tests
- ✅ **Wrap-up**: 1 task - Final verification and documentation

### Key Achievements
1. **Structured Output Foundation**: Extended `IOpenAiClient` with `GetStructuredCompletionAsync<T>` - reusable platform capability
2. **Idempotent Job Pipeline**: 4 new job handlers (Classification, Extraction, Snapshot, Indexing) with composite alternate keys
3. **Contextual Metadata Enrichment**: Semantic search quality improvement via metadata prepending before vectorization
4. **Hybrid VisualHost Architecture**: Replaced custom PCF (Tasks 041-044) with denormalized fields + native charts
5. **VisibilityState Determinism**: Set in code, never by AI — prevents hallucination of workflow states
6. **Entity Matching Integration**: Invoice-specific signals added to existing `IRecordMatchService`
7. **Redis-Cached Summary**: 5-min TTL with explicit invalidation after snapshot generation
8. **Comprehensive Testing**: Unit tests (SpendSnapshot, SignalEvaluation) + Integration test guide (9 scenarios, 680+ lines)

### Architectural Pivot: VisualHost (2026-02-11)
**Decision**: Replaced custom Finance Intelligence PCF control (Tasks 041, 043, 044) with hybrid approach:
- Added 6 denormalized finance fields to `sprk_matter` and `sprk_project`
- Created 2 VisualHost chart definitions (Budget Utilization Gauge, Monthly Spend Timeline)
- Modified `SpendSnapshotGenerationJobHandler` to update parent entity fields

**Rationale**:
- Simpler implementation (configuration vs custom code)
- Native Dataverse VisualHost integration (existing investment)
- Hybrid approach: current values on parent entity + historical snapshots in separate tables
- Extensible: BFF API provides foundation for future custom dashboards

**Impact**: Reduced implementation by ~16 hours while maintaining all functional requirements

### Verification Results
All 13 acceptance criteria from [spec.md](spec.md) verified and documented in [notes/verification-results.md](notes/verification-results.md):
- ✅ Email ingestion with SPE files
- ✅ Classification populates document fields
- ✅ Review queue view filters candidates
- ✅ Confirm endpoint triggers extraction
- ✅ Extraction creates billing events
- ✅ Snapshot + signals + cache invalidation
- ✅ Invoice indexing with metadata
- ✅ Finance visualization via VisualHost
- ✅ Rejected candidates retained
- ✅ Async operations return 202
- ✅ Unit tests for aggregation + signals
- ✅ Integration tests for full pipeline

### Deployment Checklist
1. Import Dataverse solution (6 entities, extended sprk_document, 2 views)
2. Deploy Azure AI Search invoice index (`infrastructure/ai-search/deploy-invoice-index.bicep`)
3. Create playbook records (classification + extraction prompts)
4. Deploy BFF API code to App Service
5. Import VisualHost chart definitions (`infrastructure/dataverse/charts/`)
6. Enable feature flag: `AutoClassifyAttachments` in appsettings.json
7. Run post-deployment validation (see [notes/verification-results.md](notes/verification-results.md))

### Post-MVP Considerations
- **Quarter/Year Snapshot Periods**: Currently hardcoded to Month + ToDate; add Quarter/Year periods and QoQ/YoY velocity
- **PCF Dataset Review Queue**: Upgrade from Dataverse view to PCF Dataset control for bulk review actions
- **Multi-Currency Conversion**: Store original currency only for MVP; add conversion logic post-MVP
- **Law Department Dashboard**: React 18 Custom Page (separate project, not constrained by PCF React 16)
- **Classification/Extraction Tuning**: Use guides in [notes/](notes/) to tune prompts with real invoice samples

See [notes/lessons-learned.md](notes/lessons-learned.md) for project retrospective and insights.

## Resources

### Applicable ADRs
ADR-001, ADR-002, ADR-004, ADR-006, ADR-008, ADR-009, ADR-010, ADR-011, ADR-012, ADR-013, ADR-014, ADR-015, ADR-016, ADR-017, ADR-019, ADR-020, ADR-021

### Pattern Files
- `.claude/patterns/api/background-workers.md`
- `.claude/patterns/api/endpoint-definition.md`
- `.claude/patterns/api/error-handling.md`
- `.claude/patterns/api/service-registration.md`
- `.claude/patterns/ai/text-extraction.md`
- `.claude/patterns/caching/distributed-cache.md`
- `.claude/patterns/dataverse/entity-operations.md`
- `.claude/patterns/pcf/control-initialization.md`
- `.claude/patterns/pcf/theme-management.md`

### Constraint Files
- `.claude/constraints/ai.md`
- `.claude/constraints/jobs.md`
- `.claude/constraints/api.md`
- `.claude/constraints/pcf.md`
- `.claude/constraints/data.md`
