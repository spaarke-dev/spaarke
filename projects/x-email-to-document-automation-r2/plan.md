# Email-to-Document Automation R2 - Implementation Plan

> **Version**: 1.0
> **Created**: 2026-01-13
> **Spec Reference**: [spec.md](spec.md)

---

## Executive Summary

### Purpose
Enhance Email-to-Document Automation (R1) with user-accessible downloads, attachment extraction, background AI analysis, and manual processing UI.

### Scope
Five phases targeting: download endpoint, attachment processing, app-only AI analysis, email playbook, and ribbon UI.

### Estimated Effort
15-20 task days across 5 phases

---

## Architecture Context

### Key Architectural Constraints

| Constraint | Source | Impact |
|------------|--------|--------|
| Minimal API pattern for endpoints | ADR-001 | Download endpoint must use Minimal API |
| Standard JobContract schema | ADR-004 | New job types must follow schema |
| SpeFileStore facade for file ops | ADR-007 | Downloads proxy through SpeFileStore |
| Endpoint filters for authorization | ADR-008 | Download auth via filter, not middleware |
| DI minimalism (≤15 registrations) | ADR-010 | R2 adds ~2 services |
| No HTTP from plugins | ADR-002 | UI triggers via web resource, not plugin |

### Technology Stack
- **Backend**: .NET 8 Minimal API, MimeKit, Service Bus
- **Frontend**: Dataverse Ribbon (JavaScript web resource)
- **Storage**: SharePoint Embedded (SPE), Dataverse
- **AI**: Azure OpenAI via existing infrastructure

### Discovered Resources

**ADRs (5)**:
- ADR-001: Minimal API pattern
- ADR-004: Job Contract schema
- ADR-007: SpeFileStore facade
- ADR-008: Endpoint filters
- ADR-010: DI minimalism

**Skills (4)**:
- `dataverse-deploy` - Solution deployment
- `ribbon-edit` - Ribbon customization
- `adr-aware` - Auto-applied ADR loading
- `spaarke-conventions` - Auto-applied coding standards

**Knowledge Docs (3)**:
- [EMAIL-TO-DOCUMENT-ARCHITECTURE.md](../../docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md) - R1 reference
- [SPAARKE-AI-ARCHITECTURE.md](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) - AI patterns
- [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](../../docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) - Ribbon patterns

**Existing Code Patterns**:
- `EmailToDocumentJobHandler.cs` - Job handler pattern
- `EmailToEmlConverter.cs` - Email conversion pattern
- `EmailAttachmentProcessor.cs` - Attachment processing pattern
- `EmailEndpoints.cs` - Endpoint pattern

**Scripts (2)**:
- `Deploy-PCFWebResources.ps1` - Web resource deployment
- `Test-SdapBffApi.ps1` - API testing

---

## Implementation Approach

### Phase Structure

```
Phase 1: Download Endpoint (Foundation)
    ↓
Phase 2: Attachment Processing (Builds on R1 patterns)
    ↓
Phase 3: AppOnlyAnalysisService (Independent AI service)
    ↓
Phase 4: Email Analysis Playbook (Uses Phase 3)
    ↓
Phase 5: Ribbon UI (Triggers Phases 2-4)
```

### Critical Path
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5

### Parallel Opportunities
- Phase 2 and Phase 3 can proceed in parallel after Phase 1
- Phase 5 ribbon work can start in parallel with Phase 4 playbook

---

## WBS (Work Breakdown Structure)

### Phase 1: Download Endpoint

**Objective**: Enable users to download .eml files uploaded via app-only auth

**Deliverables**:
1. `GET /api/v1/documents/{id}/download` endpoint
2. `DocumentDownloadAuthorizationFilter` for Dataverse permission check
3. Streaming response (no memory buffering)
4. Audit logging to Application Insights
5. Unit tests for endpoint and filter

**Inputs**:
- Existing `SpeFileStore` facade
- Existing authorization patterns

**Outputs**:
- Working download endpoint
- Users can download .eml files

**Dependencies**: None (foundation phase)

**Acceptance Criteria**:
- FR-01: Download endpoint proxies SPE files via app-only auth
- FR-02: 403 returned for unauthorized users
- FR-03: Audit events in Application Insights
- NFR-01: P95 latency < 2s
- NFR-06: Streaming downloads (no full buffering)

---

### Phase 2: Attachment Processing

**Objective**: Extract email attachments as child Documents

**Deliverables**:
1. Enhance `EmailToEmlConverter` with attachment extraction method
2. Attachment filter service (noise filtering)
3. Modified `EmailToDocumentJobHandler` to process attachments
4. Parent-child relationship via `sprk_ParentDocumentLookup`
5. Configurable filter rules in `EmailProcessingOptions`
6. Unit tests for attachment extraction and filtering

**Inputs**:
- Existing `EmailToEmlConverter`
- Existing `EmailToDocumentJobHandler`
- MimeKit dependency

**Outputs**:
- Attachments uploaded as separate Documents
- Parent-child relationships established
- Noise filtered (signatures, tracking pixels, calendars)

**Dependencies**: Phase 1 (download endpoint for testing)

**Acceptance Criteria**:
- FR-04: Extract attachments using MimeKit
- FR-05: Filter noise attachments
- FR-06: Parent-child relationship queryable
- FR-07: Configurable filter rules
- NFR-02: Extraction success > 99%
- NFR-05: Max attachment size 250MB

---

### Phase 3: AppOnlyAnalysisService

**Objective**: Enable AI analysis for app-uploaded documents

**Deliverables**:
1. `AppOnlyAnalysisService` class
2. App-only token acquisition for SPE access
3. `AppOnlyDocumentAnalysis` job type and handler
4. Integration with existing AI components (OpenAiClient, TextExtractorService)
5. Email entity AI fields population
6. Unit tests

**Inputs**:
- Existing `AnalysisOrchestrationService` (reference pattern)
- Existing AI infrastructure

**Outputs**:
- Background AI analysis without user context
- Document Profile created for app-uploaded files

**Dependencies**: Phase 2 (attachments to analyze)

**Acceptance Criteria**:
- FR-08: App-only analysis creates Document Profile
- FR-09: Email entity AI fields populated
- FR-10: Job handler integration via Service Bus
- NFR-03: AI analysis success > 95%

---

### Phase 4: Email Analysis Playbook

**Objective**: Combine email + attachments for comprehensive AI analysis

**Deliverables**:
1. Email Analysis Playbook record in Dataverse
2. Extract-combine-analyze approach implementation
3. Context size management for large emails
4. Email entity field population (TL;DR, Summary, Keywords, Entities)
5. Integration tests

**Inputs**:
- Phase 3 AppOnlyAnalysisService
- Existing playbook infrastructure

**Outputs**:
- Single AI call with full email context
- Email entity AI fields populated

**Dependencies**: Phase 3 (AppOnlyAnalysisService)

**Acceptance Criteria**:
- FR-11: Extract-combine-analyze approach
- FR-12: Large emails handled gracefully
- FR-13: Output matches Document Profile schema
- NFR-04: Email analysis completion < 5 min

---

### Phase 5: UI/Ribbon Enhancements

**Objective**: Manual processing for existing/sent emails

**Deliverables**:
1. Ribbon button "Archive Email" on email form
2. Ribbon button for sent emails
3. JavaScript web resource for button handlers
4. Integration with existing endpoints
5. Manual testing checklist

**Inputs**:
- Phase 1-4 endpoints and services
- Existing ribbon patterns

**Outputs**:
- Users can manually trigger email archival
- Sent emails can be processed

**Dependencies**: Phases 1-4 (backend services)

**Acceptance Criteria**:
- FR-14: Ribbon button for existing emails
- FR-15: Ribbon button for sent emails
- Button handlers call appropriate endpoints

---

## Dependencies

### External Dependencies

| Dependency | Type | Impact | Mitigation |
|------------|------|--------|------------|
| Playbook Module | Coordination | Phase 4 requires playbook entity | Design playbook schema early |
| MimeKit | Library | Attachment extraction | Already in use for R1 |
| Graph API | Service | SPE file access | Standard patterns exist |

### Internal Dependencies

| From Phase | To Phase | Dependency Type |
|------------|----------|-----------------|
| 1 | 2 | Download endpoint for testing |
| 2 | 3 | Attachments as analysis input |
| 3 | 4 | AppOnlyAnalysisService for playbook |
| 1-4 | 5 | Backend services for UI |

---

## Testing Strategy

### Unit Testing
- Download endpoint authorization
- Attachment extraction and filtering
- AppOnlyAnalysisService token handling
- Playbook execution logic

### Integration Testing
- End-to-end download flow
- Attachment → Document creation flow
- AI analysis job processing
- Email Analysis Playbook execution

### Acceptance Testing
- Manual download of .eml files
- Attachment hierarchy in Dataverse
- AI fields populated on email entity
- Ribbon buttons functional in browser

---

## Acceptance Criteria (Summary)

| ID | Requirement | Verification |
|----|-------------|--------------|
| FR-01 | Download endpoint proxies SPE files | Manual test |
| FR-02 | Authorization via Dataverse permissions | Unit test + manual |
| FR-03 | Audit logging | Application Insights query |
| FR-04 | Attachment extraction via MimeKit | Unit test |
| FR-05 | Noise filtering | Unit test |
| FR-06 | Parent-child relationship | Dataverse query |
| FR-07 | Configurable filter rules | Config test |
| FR-08 | App-only analysis | Integration test |
| FR-09 | Email entity AI fields | Manual verification |
| FR-10 | Job handler integration | Integration test |
| FR-11 | Extract-combine-analyze | Integration test |
| FR-12 | Context size management | Large email test |
| FR-13 | Output matches schema | Schema validation |
| FR-14 | Ribbon button (existing) | Manual test |
| FR-15 | Ribbon button (sent) | Manual test |

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Graph API rate limits on downloads | Medium | Medium | Implement retry with backoff |
| Large attachments timeout | Low | Medium | Streaming + chunked upload |
| Playbook module not ready | Medium | High | Design independent playbook schema |
| Ribbon customization complexity | Low | Low | Use existing patterns, ribbon-edit skill |

---

## Next Steps

1. Review and approve this plan
2. Run `/task-create` to decompose into executable task files
3. Create feature branch
4. Begin Phase 1 implementation

---

*Last Updated: 2026-01-13*
