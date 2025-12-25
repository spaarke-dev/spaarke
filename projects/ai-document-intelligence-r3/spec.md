# AI Document Intelligence R3 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: December 25, 2025
> **Source**: README.md (project scope definition)

## Executive Summary

R3 delivers **advanced AI capabilities** and **production readiness** for the Analysis feature, including Hybrid RAG infrastructure with multiple deployment models, a playbook system for reusable analysis configurations, multi-format export (DOCX, PDF, Email, Teams), performance optimization, monitoring, and production deployment with customer deployment guide.

## Scope

### In Scope

#### Phase 3: Scope System & Hybrid RAG
- Model-driven forms for Actions/Skills/Knowledge/Tools admin
- Admin views with filtering and search
- Azure AI Search index deployment (shared model)
- IKnowledgeDeploymentService with 3 deployment models (Shared, Dedicated, CustomerOwned)
- IRagService with hybrid search and semantic ranking
- Cross-tenant authentication for customer-owned indexes
- Embedding generation and caching
- IAnalysisToolHandler interface and dynamic loading
- Sample tools: EntityExtractor, ClauseAnalyzer, DocumentClassifier
- Seed data: 5 Actions, 10 Skills, 5 Knowledge sources

#### Phase 4: Playbooks & Export
- Playbook entity forms and views
- Save analysis configuration as playbook
- Load playbooks in Analysis Builder
- Private vs. public playbook sharing
- Markdown-to-DOCX converter (OpenXML SDK)
- PDF conversion Azure Function
- Email integration via Power Apps email entity
- Teams integration via Graph API
- Sample Power Automate flows

#### Phase 5: Production Readiness
- Redis caching for Scopes and RAG results
- Prompt compression for large documents
- Load testing (100+ concurrent users)
- AI Foundry evaluation pipeline integration
- Application Insights telemetry and distributed tracing
- Cost tracking per customer
- Usage, performance, error, and cost dashboards
- Circuit breaker for AI services
- Security review and penetration testing
- Production deployment
- Customer deployment guide

### Out of Scope

- Core API development (completed in R1)
- Dataverse entity creation (completed in R1)
- PCF control development (completed in R1, deployed in R2)
- Custom page creation (completed in R2)
- Form customizations (completed in R2)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/` - RAG services, export services, tool handlers
- `src/solutions/` - Admin forms, views, seed data
- `infrastructure/bicep/` - Azure AI Search, Azure Functions
- `infrastructure/ai-foundry/` - Evaluation pipeline
- Azure App Service - Production deployment
- Azure Monitor - Telemetry and dashboards

## Requirements

### Functional Requirements

#### Hybrid RAG
1. **FR-01**: Shared RAG index filters by tenant - Acceptance: Query returns only tenant's knowledge
2. **FR-02**: Dedicated RAG index per customer - Acceptance: Customer has isolated index
3. **FR-03**: Customer-owned RAG uses customer's Azure subscription - Acceptance: Cross-tenant auth works
4. **FR-04**: RAG retrieval latency < 500ms P95 - Acceptance: Performance test passes
5. **FR-05**: Embeddings cached in Redis - Acceptance: Cache hit rate > 80%

#### Tool Framework
6. **FR-06**: Tools load dynamically from configuration - Acceptance: No code change to add tool
7. **FR-07**: EntityExtractor extracts named entities - Acceptance: Returns structured JSON
8. **FR-08**: ClauseAnalyzer identifies contract clauses - Acceptance: Returns clause list with risk scores
9. **FR-09**: DocumentClassifier categorizes documents - Acceptance: Returns category with confidence

#### Playbooks
10. **FR-10**: Save current configuration as playbook - Acceptance: Playbook record created
11. **FR-11**: Load playbook populates builder - Acceptance: All scopes selected from playbook
12. **FR-12**: Private playbooks visible only to owner - Acceptance: Security role enforced
13. **FR-13**: Public playbooks visible to all users - Acceptance: Appears in shared list

#### Export
14. **FR-14**: Export to DOCX creates valid document - Acceptance: Opens in Word without errors
15. **FR-15**: Export to PDF creates valid document - Acceptance: Opens in PDF reader
16. **FR-16**: Export to Email creates draft activity - Acceptance: Email appears in timeline
17. **FR-17**: Export to Teams posts message - Acceptance: Message appears in channel

#### Production
18. **FR-18**: System handles 100+ concurrent analyses - Acceptance: Load test passes
19. **FR-19**: Dashboards show usage metrics - Acceptance: Charts display real data
20. **FR-20**: Alerts fire on anomalies - Acceptance: Alert triggered on test error spike

### Non-Functional Requirements

- **NFR-01**: RAG retrieval latency < 500ms P95
- **NFR-02**: SSE stream start < 2s P95
- **NFR-03**: Token costs < $0.10 per document
- **NFR-04**: System uptime > 99.5%
- **NFR-05**: No critical security issues in penetration test

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern - All new endpoints use Minimal API
- **ADR-009**: Redis-first caching - Cache RAG results and scopes
- **ADR-013**: AI Architecture - Tool framework follows AI Tool pattern
- **ADR-014**: AI Evaluation - Use AI Foundry evaluation pipeline
- **ADR-015**: AI Observability - Application Insights for telemetry
- **ADR-016**: AI Security - Cross-tenant auth, data isolation

### MUST Rules

- MUST use Azure AI Search for RAG (not custom vector DB)
- MUST support all 3 deployment models (Shared, Dedicated, CustomerOwned)
- MUST cache embeddings and RAG results in Redis
- MUST use OpenXML SDK for DOCX generation (no third-party libs)
- MUST use Azure Functions for PDF conversion
- MUST use Power Apps email entity (not direct Graph API for email)
- MUST implement circuit breaker for AI service calls
- MUST track token usage and costs per customer

### Existing Patterns

- See `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentIntelligenceService.cs` for AI service pattern
- See `.claude/patterns/ai/streaming-endpoints.md` for SSE pattern
- See `.claude/patterns/caching/distributed-cache.md` for Redis pattern

## Success Criteria

### Technical
1. [ ] Hybrid RAG works for all 3 deployment models - Verify: Test each model
2. [ ] RAG retrieval latency < 500ms P95 - Verify: Load test report
3. [ ] Playbooks save and load correctly - Verify: Round-trip test
4. [ ] Export to DOCX/PDF creates valid documents - Verify: Open in respective apps
5. [ ] Email activity created with correct metadata - Verify: Check timeline
6. [ ] System handles 100+ concurrent analyses - Verify: Load test passes
7. [ ] P95 latency < 2s for SSE stream start - Verify: Performance metrics
8. [ ] Production deployment successful - Verify: Health check passes

### Business
9. [ ] Customer deployment guide validated by external user - Verify: External test
10. [ ] Token costs within $0.10/document budget - Verify: Cost dashboard
11. [ ] No critical security issues from penetration test - Verify: Security report

## Dependencies

### Prerequisites (from R1 and R2)

| Requirement | Source | Status |
|-------------|--------|--------|
| All R1 infrastructure complete | R1 | Required |
| All R2 UI components deployed | R2 | Required |
| Analysis workflow working end-to-end | R2 | Required |

### External Dependencies

- Azure AI Search service (capacity for indexes)
- Azure OpenAI embedding model (text-embedding-3-small)
- Azure Functions runtime
- Microsoft Graph API (Teams integration)
- External test user for deployment guide validation

## Existing Code Status

### AI Foundry Infrastructure (Deployed but Not Integrated)

| Component | Location | Status |
|-----------|----------|--------|
| AI Foundry Hub | Azure: sprkspaarkedev-aif-hub | Deployed |
| AI Foundry Project | Azure: sprkspaarkedev-aif-proj | Deployed |
| Prompt Flow: analysis-execute | infrastructure/ai-foundry/prompt-flows/ | Template created |
| Prompt Flow: analysis-continue | infrastructure/ai-foundry/prompt-flows/ | Template created |
| Evaluation Config | infrastructure/ai-foundry/evaluation/ | Config created |

### BFF AI Services (Partial Implementation)

| Service | Location | Status |
|---------|----------|--------|
| ScopeResolverService.cs | Services/Ai/ | Created, needs RAG enhancement |
| WorkingDocumentService.cs | Services/Ai/ | Created, needs SPE integration |
| AnalysisContextBuilder.cs | Services/Ai/ | Created |

### Azure AI Search (Deployed for Record Matching)

| Resource | Status | Notes |
|----------|--------|-------|
| spaarke-search-dev | Deployed | Currently for record matching only |
| spaarke-records-index | Deployed | Records index, not RAG |

## Risk Register

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Cross-tenant RAG auth complexity | High | High | POC early, dedicated task |
| AI Foundry evaluation integration | Medium | Medium | Test with sample flows first |
| PDF conversion function | Low | Medium | Fallback to server-side library |
| Customer deployment validation | Medium | Critical | Draft guide early, test in Phase 5 week 1 |

## Existing Implementation (DO NOT RECREATE)

> **CRITICAL**: The following resources already exist. Tasks should EXTEND and INTEGRATE, not recreate.

### AI Foundry Infrastructure (Deployed but Not Integrated)

| Component | Location | Status |
|-----------|----------|--------|
| AI Foundry Hub | Azure: sprkspaarkedev-aif-hub | DEPLOYED |
| AI Foundry Project | Azure: sprkspaarkedev-aif-proj | DEPLOYED |
| Prompt Flow: analysis-execute | `infrastructure/ai-foundry/prompt-flows/` | TEMPLATE ONLY |
| Prompt Flow: analysis-continue | `infrastructure/ai-foundry/prompt-flows/` | TEMPLATE ONLY |
| Evaluation Config | `infrastructure/ai-foundry/evaluation/` | CONFIG CREATED |

### BFF AI Services (Partial - Need RAG Enhancement)

| Service | Path | Status |
|---------|------|--------|
| ScopeResolverService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | COMPLETE - needs RAG enhancement |
| IScopeResolverService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | COMPLETE |
| WorkingDocumentService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/WorkingDocumentService.cs` | COMPLETE - needs SPE integration |
| IWorkingDocumentService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IWorkingDocumentService.cs` | COMPLETE |
| AnalysisContextBuilder.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | COMPLETE |
| IAnalysisContextBuilder.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisContextBuilder.cs` | COMPLETE |
| AnalysisOrchestrationService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | COMPLETE |

### Azure AI Search (Deployed for Record Matching)

| Resource | Status | Notes |
|----------|--------|-------|
| spaarke-search-dev | DEPLOYED | Currently for record matching only |
| spaarke-records-index | DEPLOYED | Records index, not RAG |

### Bicep Infrastructure

| File | Path | Status |
|------|------|--------|
| ai-foundry.bicepparam | `infrastructure/bicep/ai-foundry.bicepparam` | EXISTS |
| ai-search.bicep | `infrastructure/bicep/modules/ai-search.bicep` | EXISTS - may need RAG additions |

### Prerequisites from R1 and R2 (MUST BE COMPLETE)

| Requirement | Source | Action Required |
|-------------|--------|-----------------|
| All R1 infrastructure complete | R1 | VERIFY R1 complete first |
| All R2 UI components deployed | R2 | VERIFY R2 complete first |
| Analysis workflow working end-to-end | R2 | VERIFY R2 complete first |

## Task Type Guidelines

When generating tasks, use these guidelines:

| Existing Status | Task Type | Task Action |
|-----------------|-----------|-------------|
| DEPLOYED | Integrate | Connect to existing resource, add new capabilities |
| COMPLETE | Extend | Add new features to existing code |
| TEMPLATE ONLY | Complete | Finish implementation from template |
| EXISTS | Verify + Extend | Check current state, add RAG/export features |
| (not listed) | Create | New implementation needed |

## Questions/Clarifications

- [ ] Which Azure AI Search SKU is needed for multi-tenant RAG?
- [ ] Is there an existing PDF generation library preference?
- [ ] What Teams channels should be available for posting?
- [ ] Who is the external user for deployment guide validation?

---

*AI-optimized specification. Original: README.md*
