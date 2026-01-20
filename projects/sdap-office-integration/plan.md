# SDAP Office Integration - Implementation Plan

> **Status**: Planning
> **Created**: 2026-01-20
> **Source**: [spec.md](spec.md)

---

## Executive Summary

### Purpose

Build Outlook and Word add-ins that enable users to save emails, attachments, and documents directly to Spaarke DMS with association to business entities (Matter, Project, Invoice, Account, Contact), and share documents from Spaarke in Outlook compose.

### Scope

- Two Office add-ins (Outlook + Word) sharing common task pane UI
- Backend APIs for save, search, quick create, and share operations
- Background workers for upload finalization, profiling, and indexing
- Dataverse schema extensions for email artifacts and job tracking

### Estimated Effort

Based on complexity and WBS: **45-60 development days**

---

## Architecture Context

### Key Architectural Constraints

| Constraint | Source | Impact |
|------------|--------|--------|
| Minimal API pattern | ADR-001 | All `/office/*` endpoints use Minimal API |
| No Azure Functions | ADR-001 | Use BackgroundService + Service Bus |
| SpeFileStore facade | ADR-007 | All SPE operations via facade, no Graph SDK exposure |
| Endpoint filters | ADR-008 | Authorization via filters, not global middleware |
| Fluent UI v9 exclusive | ADR-021 | No v8, design tokens only, dark mode required |
| DI minimalism | ADR-010 | ≤15 non-framework registrations |
| Job contract | ADR-004 | Standard job schema, idempotent handlers |

### Technology Stack

| Component | Technology |
|-----------|------------|
| Add-in UI | React 18 + Fluent UI v9 + Office.js |
| Authentication | NAA (MSAL.js 3.x `createNestablePublicClientApplication`) |
| Outlook Manifest | Unified JSON manifest (GA) |
| Word Manifest | XML add-in-only manifest (unified is preview) |
| Backend | .NET 8 Minimal API |
| Background Jobs | BackgroundService + Azure Service Bus |
| Caching | Redis via IDistributedCache |
| File Storage | SharePoint Embedded via SpeFileStore |
| Database | Dataverse |

### Integration Points

- **Spaarke BFF API**: Existing API receives new `/office/*` endpoints
- **SpeFileStore**: Existing facade for SPE operations
- **UAC Module**: Existing authorization module
- **Service Bus**: Existing infrastructure for async jobs
- **External Portal (Future)**: Stub APIs for invitation creation

---

## Discovered Resources

### Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions |
| ADR-004 | Async Job Contract | Job schema, idempotency |
| ADR-007 | SpeFileStore Facade | No Graph SDK types in DTOs |
| ADR-008 | Endpoint Filters | Resource authorization via filters |
| ADR-010 | DI Minimalism | ≤15 registrations |
| ADR-012 | Shared Component Library | Import from @spaarke/ui-components |
| ADR-019 | ProblemDetails | RFC 7807 error responses |
| ADR-021 | Fluent UI v9 | Design tokens, dark mode |

### Applicable Skills

- `dataverse-create-schema` - For EmailArtifact, AttachmentArtifact, ProcessingJob
- `dataverse-deploy` - Solution deployment
- `azure-deploy` - BFF API deployment
- `code-review` - Quality gates
- `adr-check` - ADR compliance validation

### Code Patterns

- `.claude/patterns/api/endpoint-definition.md`
- `.claude/patterns/api/background-workers.md`
- `.claude/patterns/api/endpoint-filters.md`
- `.claude/patterns/api/error-handling.md`
- `.claude/patterns/auth/msal-client.md`
- `.claude/patterns/auth/obo-flow.md`

### Knowledge Docs

- `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md`
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md`
- `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md`

---

## Implementation Approach

### Phase Structure Overview

| Phase | Focus | Estimated Effort |
|-------|-------|------------------|
| 1 | Foundation & Setup | 5-7 days |
| 2 | Dataverse Schema | 3-4 days |
| 3 | Backend API Development | 10-14 days |
| 4 | Office Add-in Development | 12-16 days |
| 5 | Background Workers | 5-7 days |
| 6 | Integration & Testing | 6-8 days |
| 7 | Deployment & Go-Live | 3-4 days |

### Critical Path

1. Phase 1 (Foundation) → Phase 2 (Schema) → Phase 3 (APIs) → Phase 5 (Workers)
2. Phase 1 (Foundation) → Phase 4 (Add-ins) connects to Phase 3 APIs
3. Phase 6 (Integration) requires all prior phases
4. Phase 7 (Deployment) is final

### Key Dependencies

- Azure AD app registrations must be created before Phase 4 (add-in auth)
- Dataverse schema (Phase 2) must be deployed before Phase 3 (API development)
- APIs (Phase 3) must be available before Phase 4 add-in integration
- Workers (Phase 5) can partially overlap with Phase 4

---

## WBS (Work Breakdown Structure)

### Phase 1: Foundation & Setup

**Objectives**: Establish project infrastructure, app registrations, and development environment.

**Deliverables**:
1. Azure AD app registration for Office add-in (public client)
2. Azure AD app registration update for BFF API (confidential client, OBO scopes)
3. Office add-in project structure (Outlook + Word folders)
4. Manifest files (unified JSON for Outlook, XML for Word)
5. Development environment setup (local debugging configuration)
6. Shared task pane React project with Fluent UI v9

**Inputs**:
- spec.md (requirements)
- Existing BFF API codebase
- Azure subscription access

**Outputs**:
- Configured app registrations
- Skeleton add-in projects
- Working local debug environment

**Dependencies**: None (first phase)

---

### Phase 2: Dataverse Schema

**Objectives**: Create Dataverse tables for email artifacts and job tracking.

**Deliverables**:
1. EmailArtifact table with all columns and indexes
2. AttachmentArtifact table with all columns and indexes
3. ProcessingJob table (per ADR-004) with all columns and indexes
4. Relationships between tables (lookups)
5. Security roles for table access
6. Solution packaging and deployment

**Inputs**:
- Schema definitions from spec.md
- ADR-004 job contract requirements
- dataverse-create-schema skill

**Outputs**:
- Deployed Dataverse solution
- Tables with proper indexes
- Security roles configured

**Dependencies**: Phase 1 (environment setup)

---

### Phase 3: Backend API Development

**Objectives**: Implement all `/office/*` API endpoints.

**Deliverables**:
1. POST `/office/save` - Submit email/doc for filing
2. GET `/office/jobs/{jobId}` - Job status polling
3. GET `/office/jobs/{jobId}/stream` - SSE stream for job status
4. GET `/office/search/entities` - Search association targets
5. GET `/office/search/documents` - Search for sharing
6. POST `/office/quickcreate/{entityType}` - Inline entity creation
7. POST `/office/share/links` - Generate share links
8. POST `/office/share/attach` - Attachment package for compose
9. GET `/office/recent` - Recent items
10. Idempotency middleware (SHA256 key + Redis)
11. Rate limiting per endpoint
12. Error handling (ProblemDetails, OFFICE_001-015 codes)
13. Authorization filters for all endpoints

**Inputs**:
- API contracts from spec.md
- ADR-001, ADR-008, ADR-019 constraints
- Existing SpeFileStore, UAC modules

**Outputs**:
- Working API endpoints
- Integration tests
- API documentation

**Dependencies**: Phase 2 (Dataverse schema for job tracking)

---

### Phase 4: Office Add-in Development

**Objectives**: Build Outlook and Word add-ins with shared task pane UI.

**Deliverables**:
1. NAA authentication service (MSAL.js 3.x)
2. Dialog API fallback for unsupported clients
3. Host adapter interface (IHostAdapter)
4. Outlook adapter (email/attachment extraction)
5. Word adapter (document extraction)
6. Shared API client service
7. Task pane shell with FluentProvider
8. Save flow UI (entity picker, attachment selector, options)
9. Share flow UI (document search, link/attach options)
10. Quick Create dialog UI
11. Job status display component (SSE + polling)
12. Error handling and notifications
13. Dark mode and high-contrast support
14. Accessibility (keyboard nav, screen reader)
15. Outlook manifest configuration (unified JSON)
16. Word manifest configuration (XML)

**Inputs**:
- UI requirements from spec.md
- ADR-021 (Fluent UI v9) constraints
- Office.js requirement sets (Mailbox 1.8+, WordApi 1.3+)

**Outputs**:
- Working Outlook add-in
- Working Word add-in
- Manual testing in Office clients

**Dependencies**: Phase 1 (project setup), Phase 3 (APIs for integration)

---

### Phase 5: Background Workers

**Objectives**: Implement async job processing for document upload and AI features.

**Deliverables**:
1. Upload finalization worker (move temp → permanent, update records)
2. Profile summary worker (AI document profile)
3. Indexing worker (RAG index updates)
4. Deep analysis worker (optional, policy-gated)
5. Job status update service (for SSE broadcast)
6. Error handling and retry logic
7. Dead letter queue handling

**Inputs**:
- ADR-004 job contract
- Existing AI pipeline patterns
- Service Bus configuration

**Outputs**:
- Working background workers
- Job status updates flowing
- Retry logic tested

**Dependencies**: Phase 2 (ProcessingJob table), Phase 3 (API creates jobs)

---

### Phase 6: Integration & Testing

**Objectives**: End-to-end integration and comprehensive testing.

**Deliverables**:
1. E2E tests: Outlook save flow (email + attachments)
2. E2E tests: Word save flow
3. E2E tests: Share flow (links and attachments)
4. E2E tests: Quick Create flow
5. Integration tests: SSE job status
6. Integration tests: Duplicate detection
7. Integration tests: Rate limiting
8. Accessibility audit (WCAG 2.1 AA)
9. Performance testing (response times)
10. Security review (auth flows, permissions)

**Inputs**:
- Completed Phases 3, 4, 5
- Test environments (dev, test)

**Outputs**:
- Test reports
- Accessibility audit report
- Performance benchmarks
- Security review findings

**Dependencies**: Phases 3, 4, 5 (all features complete)

---

### Phase 7: Deployment & Go-Live

**Objectives**: Deploy to production and document for users.

**Deliverables**:
1. Production deployment of BFF API updates
2. Add-in deployment to Microsoft 365 admin center
3. User documentation (how to install, use)
4. Admin documentation (deployment, configuration)
5. Monitoring and alerting setup
6. Go-live validation
7. Project wrap-up and lessons learned

**Inputs**:
- Completed Phase 6 (testing passed)
- Production environment access
- Admin center access

**Outputs**:
- Production-deployed add-ins
- User/admin documentation
- Monitoring configured
- Project complete

**Dependencies**: Phase 6 (all tests pass)

---

## Dependencies

### External Dependencies

| Dependency | Required For | Notes |
|------------|--------------|-------|
| Azure AD admin consent | Phase 1 | App registration permissions |
| Microsoft 365 admin center | Phase 7 | Add-in deployment |
| Office.js CDN | Phase 4 | Runtime library |
| Service Bus (existing) | Phase 5 | Queue infrastructure |

### Internal Dependencies

| Dependency | Required For | Notes |
|------------|--------------|-------|
| SpeFileStore | Phase 3, 5 | File operations |
| UAC module | Phase 3 | Authorization checks |
| Existing Document entity | Phase 2, 3 | Core data model |
| Shared UI components | Phase 4 | @spaarke/ui-components |

---

## Testing Strategy

### Unit Testing

- API endpoint handlers (mock dependencies)
- Add-in services (mock Office.js, API client)
- Background workers (mock Service Bus, SpeFileStore)
- Coverage target: 80%

### Integration Testing

- API endpoints with real Dataverse (dev environment)
- Workers with real Service Bus (dev queue)
- Add-in with real API (local + dev)

### End-to-End Testing

- Manual testing in New Outlook (Windows, Mac)
- Manual testing in Outlook Web
- Manual testing in Word Desktop (Windows, Mac)
- Manual testing in Word Web
- Accessibility testing with screen readers

### Acceptance Testing

- Per graduation criteria in README.md
- User acceptance with stakeholders

---

## Acceptance Criteria

See [README.md](README.md) Graduation Criteria section for the complete list.

**Key Criteria Summary**:
1. Both add-ins install and load correctly
2. NAA authentication works silently
3. Save flow creates documents with correct associations
4. Quick Create creates entities inline
5. SSE updates within 1 second
6. Duplicate detection works
7. Share flow inserts links and attaches copies
8. Error handling returns ProblemDetails
9. Dark mode and accessibility compliance

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| NAA not supported in all clients | Medium | Medium | Dialog API fallback implemented |
| Unified manifest preview for Word | High | Medium | Use XML manifest for Word (production-ready) |
| Large attachment handling | Medium | Medium | Client-side retrieval with size limits, future server-side option |
| SSE connection drops | Medium | Low | Polling fallback at 3-second intervals |
| Office.js API changes | Low | Medium | Pin requirement set versions, test in preview channels |
| External Portal not ready | Medium | Low | Stub invitation APIs, graceful degradation |

---

## Next Steps

1. **Run task-create** to decompose this plan into executable task files
2. **Review tasks** in TASK-INDEX.md
3. **Begin Phase 1** - Foundation & Setup

---

*This plan is generated from spec.md. For task-level details, see [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) after running task-create.*
