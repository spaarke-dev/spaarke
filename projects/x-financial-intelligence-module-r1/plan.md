# Finance Intelligence Module R1 — Implementation Plan

## Executive Summary

| Field | Value |
|-------|-------|
| **Purpose** | Deliver Finance Intelligence MVP — AI invoice classification, human review, billing fact extraction, spend analytics, invoice search, and Finance PCF panel |
| **Scope** | 4 new job handlers, 2 AI playbooks, 6 new Dataverse entities, 4 BFF endpoints, 1 PCF control, 1 AI Search index, 1 DI module |
| **Estimated Effort** | ~60–80 tasks across 4 phases |
| **Branch** | `work/financial-intelligence-module-r1` |

## Architecture Context

### Applicable ADRs

| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions |
| ADR-002 | Thin Dataverse plugins | No business logic in plugins |
| ADR-004 | Async job contract | Idempotent handlers, correlation IDs, standard envelope |
| ADR-006 | PCF over webresources | No new JS webresources |
| ADR-008 | Endpoint filters for auth | No global auth middleware |
| ADR-009 | Redis-first caching | `IDistributedCache`; explicit invalidation |
| ADR-010 | DI minimalism | ≤15 registrations per feature module |
| ADR-011 | Dataset PCF over subgrids | (Future review queue upgrade) |
| ADR-012 | Shared component library | `@spaarke/ui-components`; Fluent v9; 90%+ coverage |
| ADR-013 | AI architecture | Extend BFF; structured output via `IOpenAiClient` |
| ADR-014 | AI caching/reuse | Versioned prompts; tenant-scoped cache keys |
| ADR-015 | Data governance | No content in logs; IDs only in payloads |
| ADR-016 | Rate limits/backpressure | Bounded concurrency; explicit timeouts |
| ADR-017 | Job status contract | 202 + jobId + statusUrl |
| ADR-019 | ProblemDetails errors | Stable errorCode + correlationId |
| ADR-020 | Versioning | Tolerant reader; prompt versioning |
| ADR-021 | Fluent UI v9 design | React 16 APIs; dark mode; no hard-coded colors |

### Discovered Resources

#### Constraints (load first)
| File | Topics |
|------|--------|
| `.claude/constraints/ai.md` | AI architecture, caching, data governance, rate limits |
| `.claude/constraints/jobs.md` | Background job patterns, Service Bus, idempotency |
| `.claude/constraints/api.md` | Minimal API, endpoint filters, DI, error handling |
| `.claude/constraints/pcf.md` | PCF controls, Fluent UI v9, React 16 |
| `.claude/constraints/data.md` | Data access, Redis caching patterns |

#### Patterns (load for implementation)
| File | Topics |
|------|--------|
| `.claude/patterns/api/background-workers.md` | Job Contract, handler implementation, idempotency |
| `.claude/patterns/api/endpoint-definition.md` | Minimal API endpoint groups, route mapping |
| `.claude/patterns/api/endpoint-filters.md` | Authorization filters |
| `.claude/patterns/api/error-handling.md` | ProblemDetails, error codes |
| `.claude/patterns/api/service-registration.md` | DI modules, Options pattern |
| `.claude/patterns/ai/streaming-endpoints.md` | SSE, circuit breaker, AI endpoints |
| `.claude/patterns/ai/text-extraction.md` | Document Intelligence, file download |
| `.claude/patterns/caching/distributed-cache.md` | Redis GetOrCreate, versioned keys, TTL |
| `.claude/patterns/dataverse/entity-operations.md` | Late-bound CRUD, OptionSetValue, DTO mapping |
| `.claude/patterns/pcf/control-initialization.md` | React 16 render, FluentProvider, lifecycle |
| `.claude/patterns/pcf/theme-management.md` | Dark mode, Fluent v9 theming |

#### Knowledge Docs (load when needed)
| File | Topics |
|------|--------|
| `docs/guides/SPAARKE-AI-ARCHITECTURE.md` | AI architecture overview, Tool Framework |
| `docs/guides/RAG-ARCHITECTURE.md` | Hybrid search, multi-tenant, embedding cache |
| `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md` | Email pipeline being extended |
| `docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md` | Playbook system, structured extraction |
| `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md` | Creating playbook records in Dataverse |

#### Canonical Implementations (follow these patterns)
| File | Pattern |
|------|---------|
| `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Job handler with enqueue chaining |
| `Services/Jobs/Handlers/RagIndexingJobHandler.cs` | RAG indexing handler |
| `Services/Jobs/IJobHandler.cs` | Job handler interface |
| `Services/Jobs/JobContract.cs` | Job contract envelope |
| `Services/Ai/OpenAiClient.cs` | OpenAI client (extend with structured output) |
| `Services/Ai/IOpenAiClient.cs` | OpenAI client interface |
| `Services/Ai/AppOnlyAnalysisService.cs` | App-only AI processing |
| `Services/Ai/PlaybookService.cs` | Playbook loading from Dataverse |
| `Services/Ai/TextExtractorService.cs` | Text extraction from documents |
| `Services/RecordMatching/RecordMatchService.cs` | Entity matching with confidence scoring |
| `Infrastructure/DI/SpaarkeCore.cs` | DI module pattern |
| `Infrastructure/DI/DocumentsModule.cs` | DI module pattern |
| `Infrastructure/DI/WorkersModule.cs` | DI module pattern |
| `Services/Ai/EmbeddingCache.cs` | Redis caching pattern for AI |
| `Api/Ai/AnalysisEndpoints.cs` | Minimal API endpoint group |
| `Api/Filters/DocumentAuthorizationFilter.cs` | Endpoint authorization filter |
| `Configuration/EmailProcessingOptions.cs` | Options pattern with validation |

#### Scripts (for deployment/testing)
| Script | Purpose |
|--------|---------|
| `scripts/seed-data/Deploy-*.ps1` | Deploy playbook records to Dataverse |
| `scripts/seed-data/playbooks.json` | Playbook seed data format |
| `scripts/Deploy-BffApi.ps1` | BFF API deployment |
| `scripts/Deploy-PCFWebResources.ps1` | PCF control deployment |
| `scripts/Test-SdapBffApi.ps1` | API health check testing |

### Integration Points

| System | Integration | Direction |
|--------|-------------|-----------|
| Email-to-Document pipeline | Enqueue classification after attachment creation | Outbound (new enqueue) |
| `IRecordMatchService` | Entity matching for matter/vendor suggestions | Reuse existing |
| `TextExtractorService` | Document text extraction for classification/extraction | Reuse existing |
| `IOpenAiClient` | Structured output for classification/extraction | Extend (new method) |
| `IPlaybookService` | Load prompt templates from Dataverse | Reuse existing |
| `ISpeFileOperations` | Download attachments from SPE | Reuse existing |
| `ITextChunkingService` | Chunk invoice text for indexing | Reuse existing |
| Azure AI Search | Dedicated invoice index | New index |
| Redis | Finance summary caching | New cache keys |
| Dataverse | 6 new entities, 13 fields on existing entity | New schema |

## Phase Breakdown (WBS)

### Phase 1: Foundation (Dataverse Schema + AI Platform Capability)

**Priority**: P0 — Everything else depends on this
**Objective**: Establish Dataverse schema, structured output capability, and playbook records

**Deliverables**:
1. Dataverse schema: 6 new entities (`sprk_invoice`, `sprk_billingevent`, `sprk_budgetplan`, `sprk_budgetbucket`, `sprk_spendsnapshot`, `sprk_spendsignal`) with relationships and alternate keys
2. Dataverse schema: 13 new fields on `sprk_document` (classification, hints, associations)
3. Dataverse views: Invoice Review Queue, Active Invoices
4. `GetStructuredCompletionAsync<T>` method on `IOpenAiClient` / `OpenAiClient`
5. Unit tests for structured output method
6. C# record types: `ClassificationResult`, `ExtractionResult`, `InvoiceHints`, `InvoiceHeader`, `BillingEventLine` + JSON schemas
7. "Attachment Classification" prompt template (system + user prompts)
8. "Invoice Extraction" prompt template (system + user prompts)
9. Playbook records created in Dataverse
10. `FinanceOptions` configuration class with validation
11. `AddFinanceModule()` DI registration
12. Update ADR-010 with per-module ceiling constraint

**Inputs**: spec.md, design document, existing `IOpenAiClient` interface
**Outputs**: Deployed Dataverse schema, working structured output, playbook records
**Dependencies**: None (this is the foundation)

### Phase 2: AI Services + Job Handlers

**Priority**: P0 — Core pipeline implementation
**Objective**: Implement the complete classification → review → extraction → snapshot pipeline

**Deliverables**:
1. `IInvoiceAnalysisService` / `InvoiceAnalysisService` (classification + extraction methods)
2. `AttachmentClassificationJobHandler` (classify, write hints, entity matching)
3. Entity matching signal aggregation (multi-signal: reference number, vendor org, parent email, keyword — reuses `IRecordMatchService`)
4. Modify `EmailToDocumentJobHandler` to enqueue classification (+ `AutoClassifyAttachments` feature flag)
5. `InvoiceReviewService` + `POST /api/finance/invoice-review/confirm` endpoint
6. `POST /api/finance/invoice-review/reject` endpoint
7. `InvoiceExtractionJobHandler` (extract facts, create invoice + billing events, enqueue downstream)
8. `SpendSnapshotService` (aggregate billing events, compute variance/velocity)
9. `SignalEvaluationService` (budget exceeded/warning, velocity spike detection)
10. `SpendSnapshotGenerationJobHandler` (compute snapshots, create signals, invalidate Redis)
11. Unit tests: SpendSnapshot aggregation (monthly/ToDate, variance, MoM velocity, idempotent upsert)
12. Unit tests: Signal evaluation rules (budget thresholds, velocity spike)
13. Finance endpoint authorization filter

**Inputs**: Phase 1 outputs (schema, structured output, playbook records, DI module)
**Outputs**: Working pipeline from classification through snapshot generation
**Dependencies**: Phase 1 complete

### Phase 3: Invoice RAG + Search

**Priority**: P1 — Search capability
**Objective**: Index confirmed invoices with financial metadata and provide hybrid search

**Deliverables**:
1. Invoice AI Search index schema definition (JSON + Bicep)
2. Deploy invoice index to Azure AI Search (`spaarke-invoices-{tenantId}`)
3. `InvoiceIndexingJobHandler` (text extraction, chunking, contextual metadata enrichment, embedding, index upsert)
4. `InvoiceSearchService` + `GET /api/finance/invoices/search` endpoint (hybrid: keyword + vector + semantic ranking)
5. Wire invoice indexing into extraction job chain (enqueue from `InvoiceExtractionJobHandler`)

**Inputs**: Phase 2 outputs (extraction produces invoices with metadata)
**Outputs**: Searchable invoice index, search endpoint
**Dependencies**: Phase 2 (extraction must produce invoice records)

### Phase 4: PCF Panel + Integration + Polish

**Priority**: P1 — User-facing UI and end-to-end validation
**Objective**: Finance Intelligence PCF panel, finance summary endpoint, prompt tuning, integration tests

**Deliverables**:
1. `GET /api/finance/matters/{matterId}/summary` endpoint (Redis-cached, 5-min TTL + explicit invalidation)
2. Finance Intelligence PCF panel (budget gauge, spend timeline, active signals, invoice history)
3. PCF panel Fluent UI v9 theming (dark mode, high contrast)
4. Tune classification confidence thresholds with test documents
5. Tune extraction prompts with real invoice samples
6. Version playbook prompts after tuning
7. Invoice Review Queue Dataverse view configuration
8. End-to-end integration tests (classification → review → extraction → snapshot → indexing)
9. Project wrap-up (README status update, lessons learned)

**Inputs**: Phases 1-3 complete
**Outputs**: Complete working system, tuned prompts, integration tests
**Dependencies**: Phase 3 complete (for full pipeline testing)

## Dependencies

### External Dependencies

| Dependency | Required For | Status |
|------------|-------------|--------|
| Azure OpenAI (`gpt-4o-mini` deployment) | Classification | Available |
| Azure OpenAI (`gpt-4o` deployment) | Extraction | Available |
| Azure OpenAI (`text-embedding-3-large`) | Invoice indexing | Available |
| Azure AI Search | Invoice index creation | Available |
| Dataverse Dev Environment | Schema deployment | Available |
| Azure Document Intelligence | Text extraction from PDFs | Available |
| Redis instance | Finance summary caching | Available |

### Internal Dependencies

| Dependency | Required For | Status |
|------------|-------------|--------|
| `EmailToDocumentJobHandler` | Classification enqueue point | Exists |
| `IRecordMatchService` | Entity matching | Exists |
| `TextExtractorService` | Document text extraction | Exists |
| `IOpenAiClient` / `OpenAiClient` | Structured output extension | Exists (extend) |
| `IPlaybookService` | Prompt template loading | Exists |
| `ISpeFileOperations` | SPE file download | Exists |
| `ITextChunkingService` | Invoice text chunking | Exists |

## Testing Strategy

| Level | Scope | Priority |
|-------|-------|----------|
| **Unit tests** | SpendSnapshot aggregation (deterministic math) | P0 — highest priority |
| **Unit tests** | Signal evaluation rules (threshold logic) | P0 |
| **Unit tests** | Structured output method | P0 |
| **Unit tests** | Service logic (InvoiceAnalysis, InvoiceReview) | P1 |
| **Integration tests** | Full pipeline: classification → review → extraction → snapshot | P1 |
| **Integration tests** | Invoice indexing + search | P1 |
| **UI tests** | PCF panel renders, dark mode compliance | P2 |

## Acceptance Criteria

See [spec.md § Success Criteria](spec.md#success-criteria) — 13 criteria covering functional requirements, unit tests, and integration tests.

## Risk Register

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Classification accuracy too low for production | High | Medium | Feature flag (default off); tune with real data in Phase 4 |
| Extraction line item parsing inconsistent | Medium | Medium | Single-line fallback (total as one Fee line); structured output enforcement |
| `Program.cs` merge conflict with PR #143 | Low | High | Rebase after PR merges; only 1 line added (`AddFinanceModule`) |
| Invoice index schema needs changes | Low | Low | Separate index allows independent schema evolution |
| PCF bundle size exceeds 5MB | Medium | Low | Use `platform-library` declaration; tree-shake imports |

## Next Steps

1. Generate task files from this plan (Step 3 of project-pipeline)
2. Create initial commit with project artifacts
3. Begin Phase 1 task execution
