# Email-to-Document Automation R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-13
> **Source**: DESIGN.md v1.1

## Executive Summary

R2 enhancements for Email-to-Document Automation: fix user access to app-uploaded files, extract attachments as child documents, enable background AI analysis, and add UI for processing existing emails.

## Scope

### In Scope
- **Phase 1**: API-proxied download endpoint (`GET /api/v1/documents/{id}/download`)
- **Phase 2**: Attachment extraction with filtering (signature logos, tracking pixels, calendar files)
- **Phase 3**: AppOnlyAnalysisService for background AI analysis
- **Phase 4**: Email Analysis Playbook (extract-combine-analyze approach)
- **Phase 5**: UI/PCF enhancements (ribbon toolbar for existing/sent emails)

### Out of Scope
- New monitoring dashboards
- Explicit R1 rework (unless refinement needed for R2)

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Api/` - New download endpoint
- `src/server/api/Sprk.Bff.Api/Filters/` - New authorization filter
- `src/server/api/Sprk.Bff.Api/Services/Email/` - Attachment extraction
- `src/server/api/Sprk.Bff.Api/Services/Analysis/` - AppOnlyAnalysisService
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` - Enhanced job handlers
- Dataverse `email` entity - New AI output fields
- Dataverse solution - Ribbon customizations

## Requirements

### Functional Requirements

**Phase 1: Download Endpoint**
1. **FR-01**: Download endpoint proxies SPE files via app-only auth - Acceptance: Users can download .eml files
2. **FR-02**: Authorization via Dataverse permissions - Acceptance: 403 for unauthorized users
3. **FR-03**: Audit logging for all downloads - Acceptance: Events in Application Insights

**Phase 2: Attachment Processing**
4. **FR-04**: Extract attachments from .eml using MimeKit - Acceptance: Child Documents created
5. **FR-05**: Filter noise (signatures, tracking pixels, calendars) - Acceptance: Only meaningful attachments uploaded
6. **FR-06**: Parent-child relationship via `sprk_ParentDocumentLookup` - Acceptance: Query returns hierarchy
7. **FR-07**: Configurable filter rules - Acceptance: Settings in `EmailProcessingOptions`

**Phase 3: AppOnlyAnalysisService**
8. **FR-08**: App-only analysis for background-uploaded documents - Acceptance: Document Profile created
9. **FR-09**: Email entity AI fields populated - Acceptance: TL;DR, Summary, Keywords, Entities stored
10. **FR-10**: Job handler integration via Service Bus - Acceptance: `AppOnlyDocumentAnalysis` job type works

**Phase 4: Email Analysis Playbook**
11. **FR-11**: Extract-combine-analyze approach - Acceptance: Single AI call with full context
12. **FR-12**: Context size management - Acceptance: Large emails handled gracefully
13. **FR-13**: Output matches Document Profile schema - Acceptance: Same fields on email entity

**Phase 5: UI/PCF**
14. **FR-14**: Ribbon button for processing existing emails - Acceptance: User can trigger from email form
15. **FR-15**: Ribbon button for sent emails - Acceptance: Outbound emails can be archived

### Non-Functional Requirements
- **NFR-01**: Download latency P95 < 2s
- **NFR-02**: Attachment extraction success > 99%
- **NFR-03**: App-only analysis success > 95%
- **NFR-04**: Email analysis completion < 5 min
- **NFR-05**: Max attachment size 250MB (SPE limit)
- **NFR-06**: Streaming downloads (no full buffering)

## Technical Constraints

### Applicable ADRs
- **ADR-001**: Minimal API pattern for new endpoints
- **ADR-004**: Standard JobContract schema for new job types
- **ADR-007**: SpeFileStore facade for file downloads
- **ADR-008**: Endpoint filters for authorization
- **ADR-010**: DI minimalism (≤15 registrations) - adds 2 services

### MUST Rules
- ✅ MUST use endpoint filter for download authorization (not middleware)
- ✅ MUST stream file response (not buffer in memory)
- ✅ MUST follow existing JobContract schema
- ✅ MUST use MimeKit for attachment extraction (existing dependency)
- ❌ MUST NOT make HTTP calls from Dataverse plugins

### Existing Patterns to Follow
- `EmailToDocumentJobHandler` - Job handler pattern
- `DocumentAuthorizationFilter` - Endpoint filter pattern
- `EmailTelemetry` - Telemetry naming conventions
- `AnalysisOrchestrationService` - Analysis service pattern (OBO reference)

## Success Criteria

1. [ ] Users can download .eml files uploaded by email processing - Verify: Manual test
2. [ ] Attachments extracted and uploaded as child Documents - Verify: Query `sprk_ParentDocumentLookup`
3. [ ] AI analysis works for app-uploaded documents - Verify: Document Profile populated
4. [ ] Email analysis combines email + attachments - Verify: Email entity AI fields populated
5. [ ] Ribbon buttons work for existing/sent emails - Verify: Manual test from email form
6. [ ] All metrics meet NFR targets - Verify: Application Insights queries

## Dependencies

### Prerequisites
- Email-to-Document R1 complete (PR #104)
- Existing email processing pipeline operational

### External Dependencies
- **Playbook Module**: Email Analysis Playbook creation requires coordination

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| R1 Scope | Explicit R1 rework needed? | No, unless refinement needed for R2 | Focus on R2 features |
| UI/PCF | Include ribbon/UI changes? | Yes, ribbon toolbar for existing/sent emails | Added Phase 5 |
| Dashboard | Need monitoring dashboard? | No | Out of scope |
| Attachment Limits | Max file size? | Follow SPE limits (250MB) | Config setting added |
| Playbook Location | Where stored? | Existing `sprk_playbook` entity | Coordinate with Playbook module |

## Assumptions

- Existing `sprk_playbook` entity schema supports Email Analysis Playbook
- MimeKit handles all common email attachment formats
- Graph API 250MB limit sufficient for email attachments

## Unresolved Questions

- [ ] Playbook module timeline - coordinate for Phase 4 dependency

---
*AI-optimized specification. Original: DESIGN.md*
