# Finance Intelligence Module R1 — AI Context

## Project Status

| Field | Value |
|-------|-------|
| **Phase** | Phase 1 — Foundation |
| **Last Updated** | 2026-02-11 |
| **Current Task** | None (project initialized, awaiting task 001) |
| **Next Action** | Execute task 001 |

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

## Implementation Notes

- `Program.cs` will need 1 line: `builder.Services.AddFinanceModule(builder.Configuration);`
- PR #143 (playbook node builder) also touches `Program.cs` — rebase after merge
- Playbook prompts stored in Dataverse `sprk_playbook` records, NOT in source code
- Invoice index uses `text-embedding-3-large` (3072 dimensions) — same as production
- `BudgetPlan.sprk_status` transitioned manually for MVP (Draft/Active/Closed)

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
