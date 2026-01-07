# Implementation Plan: AI Summary and Analysis Enhancements

> **Project**: ai-summary-and-analysis-enhancements
> **Created**: 2026-01-06
> **Estimated Tasks**: ~25-30 tasks across 4 phases

---

## Architecture Context

### Discovered Resources

| Type | Resources |
|------|-----------|
| **Primary ADRs** | ADR-013 (AI Architecture), ADR-008 (Endpoint Filters) |
| **Supporting ADRs** | ADR-001 (Minimal API), ADR-010 (DI), ADR-014 (AI Caching), ADR-015 (Data Governance) |
| **Knowledge Docs** | SPAARKE-AI-ARCHITECTURE.md, uac-access-control.md |
| **Existing Services** | AnalysisOrchestrationService, DocumentIntelligenceService |
| **Existing Entities** | sprk_analysisplaybook, sprk_aioutputtype, sprk_analysisoutput |

### Key Design Decisions

1. **Authorization**: FullUAC mode (security requirement)
2. **Terminology**: "Document Profile" not "Auto-Summary" or "Simple Mode"
3. **Storage**: Dual storage (analysisoutput + document field mapping)
4. **Failure**: Retry 3x → soft failure with outputs preserved
5. **Cleanup**: Immediately after deployment verified

---

## Phase Breakdown

### Phase 2.1: Unify Authorization (7 tasks)

**Objective**: Create unified authorization service with FullUAC mode and retry logic.

**Deliverables**:
1. `IAiAuthorizationService` interface
2. `AiAuthorizationService` implementation with FullUAC
3. Updated `AnalysisAuthorizationFilter` using unified service
4. Updated `AiAuthorizationFilter` using unified service
5. Storage retry logic (Polly exponential backoff)
6. Unit tests for authorization service
7. Integration tests for retry scenarios

**Dependencies**: None (starting point)

**Risks**:
- FullUAC may have performance impact → Monitor and tune
- Retry logic timing → Profile actual replication lag

---

### Phase 2.2: Add Document Profile Playbook Support (10 tasks)

**Objective**: Enable AnalysisOrchestrationService to handle Document Profile execution, including PCF updates for soft failure handling.

**Deliverables**:
1. "Document Profile" playbook seed data (Dataverse)
2. Output type seed data (TL;DR, Summary, Keywords, etc.)
3. Playbook lookup by name (`GetByNameAsync`)
4. Dual storage implementation (analysisoutput + document fields)
5. Field mapping logic for Document Profile
6. `DocumentProfileResult` model
7. Soft failure handling
8. Integration tests for Document Profile flow
9. **SSE response format update** (add partialStorage flag)
10. **PCF updates** (display warning on soft failure)

**Dependencies**: Phase 2.1 (authorization service)

**Risks**:
- Entity relationship complexity → Use existing schema
- Field mapping correctness → Add validation
- PCF UI changes → Test dark mode compliance (ADR-021)

---

### Phase 2.3: Migrate AI Summary Endpoint (5 tasks)

**Objective**: Route existing endpoint to unified service with backward compatibility.

**Deliverables**:
1. Internal routing to `AnalysisOrchestrationService`
2. Request/response mapping for backward compatibility
3. Updated PCF control (optional - evaluate if needed)
4. `[Obsolete]` attribute on `DocumentIntelligenceService`
5. Backward compatibility tests

**Dependencies**: Phase 2.2 (Document Profile support)

**Risks**:
- Breaking existing consumers → Extensive testing
- API contract changes → None (keep same contract)

---

### Phase 2.4: Cleanup (5 tasks)

**Objective**: Remove deprecated code immediately after deployment verified.

**Deliverables**:
1. Remove `IDocumentIntelligenceService` interface
2. Remove `DocumentIntelligenceService` implementation
3. Remove `AiAuthorizationFilter` (merged into unified)
4. Update DI registrations in `SpaarkeCore.cs`
5. Update ADR-013 documentation

**Dependencies**: Phase 2.3 deployed and verified working

**Timing**: Immediately after verification - no waiting period

---

## Work Breakdown Structure

```
ai-summary-and-analysis-enhancements/
├── Phase 2.1: Unify Authorization
│   ├── 001 Create IAiAuthorizationService interface
│   ├── 002 Implement AiAuthorizationService with FullUAC
│   ├── 003 Add Polly retry policy for storage
│   ├── 004 Update AnalysisAuthorizationFilter
│   ├── 005 Update AiAuthorizationFilter
│   ├── 006 Unit tests for authorization service
│   └── 007 Integration tests for retry scenarios
│
├── Phase 2.2: Document Profile Support
│   ├── 010 Create Document Profile playbook seed data
│   ├── 011 Create output type seed data
│   ├── 012 Implement playbook lookup by name
│   ├── 013 Implement dual storage (analysisoutput + document)
│   ├── 014 Implement field mapping logic
│   ├── 015 Create DocumentProfileResult model
│   ├── 016 Implement soft failure handling
│   ├── 017 Integration tests for Document Profile
│   ├── 018 Update SSE response format (partialStorage flag)
│   └── 019 Update PCF for soft failure warning
│
├── Phase 2.3: Endpoint Migration
│   ├── 020 Route document-intelligence to unified service
│   ├── 021 Implement request/response mapping
│   ├── 022 Add [Obsolete] attributes to deprecated services
│   ├── 023 Backward compatibility tests
│   └── 024 Deploy and verify
│
├── Phase 2.4: Cleanup
│   ├── 030 Remove IDocumentIntelligenceService
│   ├── 031 Remove DocumentIntelligenceService
│   ├── 032 Remove AiAuthorizationFilter
│   ├── 033 Update DI registrations
│   └── 034 Update ADR-013 documentation
│
└── 090 Project wrap-up
```

---

## Critical Path

```
001 → 002 → 003 → 004 → 005 → 006/007 (parallel)
                              ↓
                           010 → 011 → 012 → 013 → 014 → 015 → 016 → 017
                                                                    ↓
                                                                 020 → 021 → 022 → 023 → 024
                                                                                          ↓
                                                                                       030-034
```

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 002 FullUAC implementation | Performance | Profile, add metrics |
| 003 Retry policy | Timing windows | Test with actual lag scenarios |
| 013 Dual storage | Data consistency | Transactional approach |
| 020 Endpoint migration | Breaking changes | Extensive backward compat tests |

---

## References

- [Spec Document](./spec.md)
- [ADR-013: AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)
- [ADR-008: Endpoint Filters](../../.claude/adr/ADR-008-endpoint-filters.md)
- [UAC Architecture](../../docs/architecture/uac-access-control.md)
- [AI Architecture Guide](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
