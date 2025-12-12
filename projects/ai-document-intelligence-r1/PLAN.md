# Project Plan: AI Document Intelligence – Analysis Feature

> **Last Updated**: 2025-12-11  
> **Status**: Ready for Tasks  
> **Spec**: [SPEC.md](SPEC.md)

---

## 1. Executive Summary

**Purpose**: Implement AI-driven Analysis feature enabling users to execute configurable analyses on documents with streaming chat interface, leveraging Microsoft AI Foundry and hybrid RAG deployment.

**Scope**: 
- 8 new Dataverse entities (Analysis, Action, Skill, Knowledge, Tool, KnowledgeDeployment, WorkingVersion, EmailMetadata)
- 4 new API endpoints with SSE streaming
- Analysis Builder and Analysis Workspace UIs (Custom Pages + PCF)
- Azure AI Foundry infrastructure with Prompt Flow orchestration
- Hybrid RAG (3 deployment models: Shared, Dedicated, CustomerOwned)
- Multi-tenant parameterization (Environment Variables + Bicep)
- Export capabilities (DOCX, PDF, Email, Teams)

**Timeline**: 10 weeks | **Estimated Effort**: 625 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Minimal API pattern for all endpoints
- **ADR-003**: Lean Authorization with endpoint filters
- **ADR-007**: SpeFileStore facade for all file access
- **ADR-008**: Per-resource authorization filters
- **ADR-013**: AI feature architecture principles

**From Spec**:
- Reuse existing services: `IOpenAiClient`, `ITextExtractor`, `SpeFileStore`, `IDataverseService`
- SSE streaming for real-time responses
- Session-based working document versioning in SPE
- Multi-tenant ready from Phase 1 (Environment Variables + Bicep)
- Email via Power Apps `email` entity (not direct Graph API)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Microsoft AI Foundry with Prompt Flow | Visual prompt engineering, built-in evaluation, monitoring | Requires Foundry Hub deployment, Prompt Flow design |
| Hybrid RAG (3 deployment models) | Flexibility for different customer needs (cost vs. isolation) | New `sprk_knowledgedeployment` entity, dynamic service routing |
| Session-based working versions in SPE | Leverages existing file infrastructure, enables version history | New `sprk_analysisworkingversion` entity, SPE storage pattern |
| Multi-tenant via Environment Variables | Enables customer deployment in their own tenant | Zero hard-coded config, Bicep parameterization |
| Email via Power Apps entity | Better MDA integration, email templates, compliance | Creates draft email for user review before send |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/dataverse-deploy/` - Solution deployment, PCF controls, web resources
- `.claude/skills/task-execute/` - Task execution with knowledge loading
- `.claude/skills/adr-aware/` - Automatic ADR loading based on resource type

**Knowledge Articles**:
- `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md` - **CRITICAL for PCF deployment** (version bumping)
- `docs/ai-knowledge/architecture/sdap-bff-api-patterns.md` - BFF API patterns
- `docs/ai-knowledge/reference/power-apps-custom-pages.md` - Custom Page development
- `docs/adr/ADR-001-minimal-api-endpoints.md` - Minimal API pattern
- `docs/adr/ADR-007-spefilestore-facade.md` - File access patterns
- `docs/adr/ADR-013-ai-architecture.md` - AI feature architecture

**Reusable Code**:
- `src/sprk.bff.api/Services/` - Existing service patterns for OpenAI, Dataverse, SPE
- `src/pcf-controls/SpeFileViewer/` - PCF control patterns (React + Fluent UI)
- `infrastructure/bicep/` - Existing infrastructure templates

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Core Infrastructure (Week 1-2)
└─ Multi-tenant parameterization (Environment Variables + Bicep)
└─ Azure AI Foundry deployment (Hub + Prompt Flow)
└─ Dataverse entities (8 entities + relationships)
└─ BFF API endpoints (4 endpoints with SSE streaming)

Phase 2: UI Components (Week 3-4)
└─ Document form customizations
└─ Analysis Builder modal
└─ Analysis Workspace custom page
└─ PCF components with SSE streaming

Phase 3: Scope System & Hybrid RAG (Week 5-6)
└─ Admin UI for Actions/Skills/Knowledge/Tools
└─ Hybrid RAG infrastructure (3 deployment models)
└─ Knowledge retrieval service
└─ Tool handler framework

Phase 4: Playbooks & Export (Week 7-8)
└─ Playbook management (save/load/share)
└─ Export infrastructure (DOCX/PDF)
└─ Email integration (Power Apps email entity)
└─ Teams integration (Graph API)

Phase 5: Production Readiness (Week 9-10)
└─ Performance optimization (caching, compression)
└─ Azure AI Foundry evaluation pipeline
└─ Monitoring and telemetry (Application Insights)
└─ Production deployment + customer deployment guide
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (needs API endpoints)
- Phase 4 BLOCKED BY Phase 3 (needs scope system)
- Phase 5 BLOCKED BY Phase 1-4 (needs complete system)

**High-Risk Items:**
- Azure AI Foundry setup (Week 1) - Mitigation: Start immediately, allocate 3 days
- Hybrid RAG cross-tenant auth (Week 5) - Mitigation: POC in Week 1
- PCF environment variable access (Week 3) - Mitigation: Test early with dev env vars
- Customer deployment guide validation (Week 10) - Mitigation: Test with external user in Week 9

---

## 4. Phase Breakdown

### Phase 1: Core Infrastructure (Week 1-2)

**Objectives:**
1. Establish multi-tenant parameterization (Environment Variables + Bicep)
2. Deploy Azure AI Foundry infrastructure with Prompt Flow
3. Create Dataverse entities and relationships
4. Implement BFF API endpoints with SSE streaming

**Deliverables:**
- [ ] 15 Environment Variables created in Dataverse solution
- [ ] Bicep parameter template (`infrastructure/bicep/customer-deployment.bicepparam`)
- [ ] Token-replacement `appsettings.json` template
- [ ] Azure AI Foundry Hub + Project deployed (parameterized)
- [ ] 2 Prompt Flows created (analysis-execute, analysis-continue)
- [ ] 8 Dataverse entities with relationships + security roles
- [ ] BFF API: `AnalysisEndpoints.cs` with 4 endpoints
- [ ] BFF API: 4 new services implemented
- [ ] Dataverse solution exported and deployment tested

**Critical Tasks:**
- Environment Variables creation (MUST BE FIRST)
- Azure AI Foundry Bicep template (BLOCKS AI integration)
- Dataverse entities (BLOCKS all other phases)

**Inputs**: SPEC.md, existing BFF API patterns, ADRs 001/007/013

**Outputs**: Dataverse solution, Bicep templates, BFF API code, configuration templates, deployment guide

### Phase 2: UI Components (Week 3-4)

**Objectives:**
1. Enable users to create analyses from Document form
2. Build Analysis Builder configuration modal
3. Build Analysis Workspace two-column layout with SSE streaming
4. Verify PCF uses Environment Variables (no hard-coded URLs)

**Deliverables:**
- [ ] Document form customization (Analysis tab + grid + command button)
- [ ] Analysis Builder custom page
- [ ] Analysis Workspace custom page
- [ ] New PCF control: `AnalysisWorkspace` (React + TypeScript + SSE)
- [ ] PCF manifest with environment variable access
- [ ] Monaco editor integration for working document
- [ ] Solution with UI components exported

**Critical Tasks:**
- PCF environment variable access pattern (MUST work for multi-tenant)
- SSE client implementation (BLOCKS real-time updates)

**Inputs**: Phase 1 deliverables, PCF patterns, Custom Page guides

**Outputs**: PCF control, Custom Pages, form customizations, UI documentation

### Phase 3: Scope System & Hybrid RAG (Week 5-6)

**Objectives:**
1. Enable admins to configure Actions, Skills, Knowledge, Tools
2. Deploy hybrid RAG infrastructure (3 deployment models)
3. Implement knowledge retrieval service
4. Build tool handler framework

**Deliverables:**
- [ ] Model-driven forms for Action/Skill/Knowledge/Tool entities
- [ ] Azure AI Search index deployed (shared model)
- [ ] `IKnowledgeDeploymentService` with multi-model support
- [ ] `IRagService` with hybrid search + semantic ranking
- [ ] Cross-tenant authentication for customer-owned indexes
- [ ] `IAnalysisToolHandler` interface and dynamic loading
- [ ] 3 sample tools: EntityExtractor, ClauseAnalyzer, DocumentClassifier
- [ ] Seed data: 5 Actions, 10 Skills, 5 Knowledge sources
- [ ] Evaluation pipeline configured in Azure AI Foundry

**Critical Tasks:**
- Hybrid RAG infrastructure (ENABLES knowledge grounding)
- Cross-tenant auth POC (HIGH RISK)

**Inputs**: Phase 1 deliverables, Azure AI Search service

**Outputs**: RAG service, knowledge deployment service, tool handlers, seed data, evaluation config

### Phase 4: Playbooks & Export (Week 7-8)

**Objectives:**
1. Enable users to save and reuse analysis configurations
2. Export analyses to multiple formats (DOCX, PDF, Email, Teams)
3. Integrate with Power Apps email entity

**Deliverables:**
- [ ] Playbook entity forms and views
- [ ] "Save as Playbook" functionality with sharing (private vs. public)
- [ ] 5 default Playbooks created
- [ ] `IDocumentExportService` with format converters
- [ ] Markdown-to-DOCX converter (OpenXML SDK)
- [ ] Markdown-to-PDF converter (Azure Functions)
- [ ] `IEmailActivityService` for email record creation
- [ ] Teams message posting via Graph API
- [ ] Sample Power Automate flows

**Critical Tasks:**
- Email integration with server-side sync (REQUIRES testing with Exchange)
- PDF conversion service deployment (BLOCKS PDF export)

**Inputs**: Phase 1-3 deliverables, Microsoft Graph API docs

**Outputs**: Export services, email service, Teams service, PDF converter function, sample flows

### Phase 5: Production Readiness & Evaluation (Week 9-10)

**Objectives:**
1. Optimize performance for production scale
2. Configure Azure AI Foundry evaluation pipeline
3. Implement comprehensive monitoring
4. Deploy to production and validate customer deployment guide

**Deliverables:**
- [ ] Redis caching for Scopes and RAG results
- [ ] Prompt compression for large documents
- [ ] Load testing results (100+ concurrent users)
- [ ] Evaluation pipeline running nightly with quality metrics dashboard
- [ ] Application Insights custom events + distributed tracing
- [ ] Cost tracking per customer
- [ ] Dashboards: usage, performance, errors, costs
- [ ] Circuit breaker for AI services
- [ ] Security review completed (penetration test)
- [ ] Production deployment completed
- [ ] **Customer deployment guide validated by external user**

**Critical Tasks:**
- Load testing (VALIDATES scale assumptions)
- Customer deployment guide validation (CRITICAL for Model 2 deployment)

**Inputs**: Phase 1-4 deliverables, Application Insights, Azure AI Foundry evaluation SDK

**Outputs**: Performance report, evaluation dashboard, monitoring dashboards, production runbook, customer deployment guide, user/admin guides, video tutorials

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure AI Foundry Hub | GA | Low | Proven service |
| Azure OpenAI gpt-4o-mini | GA | Low | Fallback to gpt-4o |
| Azure AI Search | GA | Low | Proven service |
| Power Apps Custom Pages | GA | Medium | Newer feature - test early |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Existing BFF API | `src/sprk.bff.api/` | Production |
| SpeFileStore | `src/sprk.bff.api/Services/` | Production |
| IOpenAiClient | `src/sprk.bff.api/Services/` | Production |
| ADRs | `docs/adr/` | Current |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- All service layer methods
- Endpoint filters (authorization)
- Tool handlers

**Integration Tests**:
- API endpoints end-to-end
- SSE streaming with real AI responses
- File save to SPE
- RAG knowledge retrieval

**E2E Tests**:
- Create Analysis from Document form
- Execute Analysis with default settings
- Refine Analysis via chat
- Export Analysis to Email

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] Azure AI Foundry Hub provisioned via parameterized Bicep
- [ ] All 15 Environment Variables created
- [ ] Zero hard-coded values in BFF API
- [ ] Bicep deployment succeeds in external test subscription
- [ ] All 8 Dataverse entities created
- [ ] SSE streaming works for `/execute` endpoint
- [ ] Unit test coverage > 80%

**Phase 2:**
- [ ] Analysis Workspace two-column layout works
- [ ] PCF reads API URL from Environment Variable
- [ ] SSE streaming works in Custom Page
- [ ] Monaco editor saves changes correctly

**Phase 3:**
- [ ] Hybrid RAG works for all 3 deployment models
- [ ] RAG retrieval latency < 500ms P95
- [ ] Cross-tenant auth works for Model 3
- [ ] Tool handlers execute without errors

**Phase 4:**
- [ ] Playbooks save and load correctly
- [ ] Export to DOCX creates valid documents
- [ ] Email activity created with correct metadata

**Phase 5:**
- [ ] System handles 100+ concurrent analyses
- [ ] P95 latency < 2s for SSE stream start
- [ ] Production deployment successful
- [ ] Customer deployment guide validated

### Business Acceptance

- [ ] 50% of test documents have analyses within 30 days
- [ ] 80% of analyses reach "Completed" status
- [ ] Token costs stay within $0.10/document budget
- [ ] User satisfaction score > 4/5

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Azure AI Foundry setup complexity | Medium | High | Start Week 1, allocate 3 days |
| R2 | Hybrid RAG cross-tenant auth | High | High | POC in Week 1, dedicated task |
| R3 | PCF environment variable access | Medium | High | Test early with dev env vars |
| R4 | Customer deployment guide validation | Medium | Critical | Draft early, test in Week 9 |
| R5 | Token costs exceed budget | Low | Medium | Monitor usage, implement alerts |
| R6 | RAG performance issues | Medium | Medium | Pre-index, cache embeddings |

---

## 9. Next Steps

1. **Review this PLAN.md** with team
2. **Run** `/task-create ai-document-intelligence-r1` to generate task files
3. **Begin** Phase 1 implementation

---

**Status**: Ready for Tasks  
**Next Action**: Generate task files with `/task-create`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
