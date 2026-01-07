# Task Index - AI Summary and Analysis Enhancements

> **Last Updated**: 2026-01-07
> **Total Tasks**: 24 (revised after removing backward compatibility)
> **Status**: ✅ **COMPLETE** (24/24 completed - 100%)

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

## Phase 2.3: Simplified Migration (Revised - No Backward Compatibility)

**Decision:** Removed backward compatibility approach per DECISION-BACKWARD-COMPATIBILITY.md (2026-01-07)

| # | Task | Status | Dependencies | Notes |
|---|------|--------|--------------|-------|
| 020 | [Remove DocumentIntelligenceService](020-remove-old-service.poml) | ✅ | 019 | Deleted old service entirely |
| 021 | [Update PCF to New Unified Endpoint](021-update-pcf-to-new-endpoint.poml) | ✅ | 020 | PCF now calls /api/ai/analysis/execute |
| 022 | [Identify Forms/Pages Using Control](022-update-forms-and-pages.poml) | ✅ | 021 | Created deployment inventory |
| 023 | [Integration Tests - New Endpoint Only](023-integration-tests-new-endpoint.poml) | ✅ | 022 | 15 tests covering SSE, playbooks, soft failure |
| 024 | [Deploy API + PCF Together](024-deploy-api-and-pcf-together.poml) | ✅ | 023 | Deployed to Dev (manual testing pending) |

**Phase 2.4 (Cleanup) removed** - No longer needed since old code deleted immediately

---

## Phase 2.3 (OLD) - Superseded Tasks

These tasks were superseded by the decision to remove backward compatibility:

| # | OLD Task | Status | Reason Superseded |
|---|----------|--------|-------------------|
| ~~020~~ | ~~Route Document Intelligence Endpoint~~ | ❌ Superseded | Old endpoint deleted instead of routed |
| ~~021~~ | ~~Implement Request/Response Mapping~~ | ❌ Superseded | No mapping needed (old code deleted) |
| ~~022~~ | ~~Add [Obsolete] Attributes~~ | ❌ Superseded | No deprecation (immediate deletion) |
| ~~023~~ | ~~Backward Compatibility Tests~~ | ❌ Superseded | No backward compat to test |

---

## Wrap-Up (1 task)

| # | Task | Status | Dependencies |
|---|------|--------|--------------|
| 090 | [Project Wrap-Up](090-project-wrap-up.poml) | 🔲 | 024 |

---

## Critical Path (Revised)

```
001 → 002 → 003 → 007 → 010 → 011/012 → 013 → 014/016 → 017 → 018 → 019 → 020 → 021 → 022 → 023 → 024 → 090
            ↓
           004/005/006
```

**Simplified:** Phase 2.4 (030-034) removed after backward compatibility decision.

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
