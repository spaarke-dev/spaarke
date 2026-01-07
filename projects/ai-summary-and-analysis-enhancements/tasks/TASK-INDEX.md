# Task Index - AI Summary and Analysis Enhancements

> **Last Updated**: 2026-01-07
> **Total Tasks**: 27
> **Status**: In Progress (12/27 completed)

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Blocked |

---

## Phase 2.1: Unify Authorization (7 tasks)

| # | Task | Status | Dependencies |
|---|------|--------|--------------|
| 001 | [Create IAiAuthorizationService Interface](001-create-authorization-interface.poml) | ✅ | none |
| 002 | [Implement AiAuthorizationService with FullUAC](002-implement-authorization-service.poml) | ✅ | 001 |
| 003 | [Add Polly Retry Policy for Storage](003-add-retry-policy.poml) | ✅ | 002 |
| 004 | [Update AnalysisAuthorizationFilter](004-update-analysis-authorization-filter.poml) | ✅ | 002 |
| 005 | [Update AiAuthorizationFilter](005-update-ai-authorization-filter.poml) | ✅ | 002 |
| 006 | [Unit Tests for Authorization Service](006-unit-tests-authorization.poml) | ✅ | 002 |
| 007 | [Integration Tests for Retry Scenarios](007-integration-tests-retry.poml) | ✅ | 003 |

---

## Phase 2.2: Document Profile Playbook Support (10 tasks)

| # | Task | Status | Dependencies |
|---|------|--------|--------------|
| 010 | [Create Document Profile Playbook Seed Data](010-create-document-profile-playbook.poml) | ✅ | 007 |
| 011 | [Create Output Type Seed Data](011-create-output-type-seed-data.poml) | ✅ | 010 |
| 012 | [Implement Playbook Lookup by Name](012-implement-playbook-lookup.poml) | ✅ | 010 |
| 013 | [Implement Dual Storage](013-implement-dual-storage.poml) | ✅ | 012 |
| 014 | [Implement Field Mapping Logic](014-implement-field-mapping.poml) | ✅ | 013 |
| 015 | [Create DocumentProfileResult Model](015-create-document-profile-result.poml) | ✅ | none |
| 016 | [Implement Soft Failure Handling](016-implement-soft-failure.poml) | ✅ | 013, 015 |
| 017 | [Integration Tests for Document Profile](017-integration-tests-document-profile.poml) | ✅ | 016 |
| 018 | [Update SSE Response Format for Partial Storage](018-update-sse-response-format.poml) | ✅ | 016 |
| 019 | [Update PCF to Handle Soft Failure](019-update-pcf-soft-failure-handling.poml) | ✅ | 018 |

---

## Phase 2.3: Endpoint Migration (5 tasks)

| # | Task | Status | Dependencies |
|---|------|--------|--------------|
| 020 | [Route Document Intelligence Endpoint](020-route-document-intelligence-endpoint.poml) | 🔲 | 017 |
| 021 | [Implement Request/Response Mapping](021-implement-request-mapping.poml) | 🔲 | 020 |
| 022 | [Add [Obsolete] Attributes](022-add-obsolete-attributes.poml) | 🔲 | 020 |
| 023 | [Backward Compatibility Tests](023-backward-compatibility-tests.poml) | 🔲 | 021 |
| 024 | [Deploy Phase 2.3 and Verify](024-deploy-and-verify.poml) | 🔲 | 023 |

---

## Phase 2.4: Cleanup (5 tasks)

| # | Task | Status | Dependencies |
|---|------|--------|--------------|
| 030 | [Remove IDocumentIntelligenceService](030-remove-document-intelligence-interface.poml) | 🔲 | 024 |
| 031 | [Remove DocumentIntelligenceService](031-remove-document-intelligence-service.poml) | 🔲 | 030 |
| 032 | [Remove AiAuthorizationFilter](032-remove-ai-authorization-filter.poml) | 🔲 | 031 |
| 033 | [Update DI Registrations](033-update-di-registrations.poml) | 🔲 | 032 |
| 034 | [Update ADR-013 Documentation](034-update-adr-documentation.poml) | 🔲 | 033 |

---

## Wrap-Up (1 task)

| # | Task | Status | Dependencies |
|---|------|--------|--------------|
| 090 | [Project Wrap-Up](090-project-wrap-up.poml) | 🔲 | 034 |

---

## Critical Path

```
001 → 002 → 003 → 007 → 010 → 011/012 → 013 → 014/016 → 017 → 018 → 019 → 020 → 021 → 023 → 024 → 030-034 → 090
            ↓
           004/005/006
```

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 002 | FullUAC performance | Profile, add metrics |
| 003 | Retry timing | Test actual lag scenarios |
| 013 | Data consistency | Transactional approach |
| 019 | PCF UI changes | Test dark mode, dismissible warning |
| 020 | Breaking changes | Extensive backward compat tests |

---

## Summary by Phase

| Phase | Tasks | Focus |
|-------|-------|-------|
| 2.1 | 7 | Authorization unification |
| 2.2 | 10 | Document Profile support + PCF updates |
| 2.3 | 5 | Endpoint migration |
| 2.4 | 5 | Code cleanup |
| Wrap-up | 1 | Project closure |
| **Total** | **28** | |
