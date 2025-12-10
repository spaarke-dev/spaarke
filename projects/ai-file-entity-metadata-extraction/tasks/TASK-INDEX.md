# Task Index: AI File Entity Metadata Extraction

> **Last Updated**: 2025-12-09
>
> **Total Tasks**: 26
> **Completed**: 15

## Phase 1a: Structured AI Output + Service Rename

| ID | Title | Status | Dependencies | Tags |
|----|-------|--------|--------------|------|
| 001 | [Rename service files](./001-rename-service-files.poml) | ✅ completed | none | bff-api, refactoring |
| 002 | [Rename options class](./002-rename-options-class.poml) | ✅ completed | none | bff-api, refactoring |
| 003 | [Update endpoint paths](./003-update-endpoint-paths.poml) | ✅ completed | 001 | bff-api, minimal-api |
| 004 | [Update DI registrations](./004-update-di-registrations.poml) | ✅ completed | 001, 002 | bff-api, di |
| 005 | [Create response models](./005-create-response-models.poml) | ✅ completed | none | bff-api, models |
| 006 | [Update AI prompt template](./006-update-ai-prompt.poml) | ✅ completed | 005 | bff-api, azure-openai |
| 007 | [Add JSON parsing with fallback](./007-add-json-parsing.poml) | ✅ completed | 005, 006 | bff-api, services |
| 008 | [Update SSE streaming](./008-update-sse-streaming.poml) | ✅ completed | 007 | bff-api, endpoints |
| 009 | [Unit tests for Phase 1a](./009-unit-tests-phase1a.poml) | ✅ completed | 001-008 | bff-api, unit-test |

## Phase 1b: Dataverse + PCF Integration + Email Support

| ID | Title | Status | Dependencies | Tags |
|----|-------|--------|--------------|------|
| 010 | [Add Dataverse fields](./010-add-dataverse-fields.poml) | ✅ completed | Phase 1a | dataverse, solution |
| 011 | [Configure Relevance Search](./011-configure-relevance-search.poml) | ✅ completed | 010 | dataverse, config |
| 012 | [Update PCF useAiSummary hook](./012-update-useaisummary-hook.poml) | ✅ completed | Phase 1a | pcf, react |
| 013 | [Update AiSummaryPanel for TL;DR](./013-update-aisummarypanel.poml) | ✅ completed | 012 | pcf, react, fluent-ui |
| 014 | [Add keyword tags display](./014-add-keyword-tags.poml) | ✅ completed | 013 | pcf, fluent-ui |
| 015 | [Add entities collapsible section](./015-add-entities-section.poml) | ✅ completed | 013 | pcf, fluent-ui |
| 016 | [Add EmailExtractorService](./016-add-email-extractor.poml) | 🔲 not-started | none | bff-api, services |
| 017 | [Update Dataverse save logic](./017-update-dataverse-save.poml) | 🔲 not-started | 010 | bff-api, dataverse |
| 018 | [Integration tests Phase 1b](./018-integration-tests-phase1b.poml) | 🔲 not-started | 010-017 | integration-test |

## Phase 1 Deployment

| ID | Title | Status | Dependencies | Tags |
|----|-------|--------|--------------|------|
| 019 | [Deploy Phase 1 Components](./019-deploy-phase1.poml) | 🔲 not-started | 009, 018 | deploy, azure, dataverse, pcf |

## Phase 2: Record Matching Service

| ID | Title | Status | Dependencies | Tags |
|----|-------|--------|--------------|------|
| 020 | [Provision Azure AI Search](./020-provision-ai-search.poml) | 🔲 not-started | 019 | azure, azure-search, bicep |
| 021 | [Create index schema](./021-create-index-schema.poml) | 🔲 not-started | 020 | azure-search, config |
| 022 | [Implement DataverseIndexSyncService](./022-dataverse-index-sync.poml) | 🔲 not-started | 021 | bff-api, azure-search |
| 023 | [Implement RecordMatchService](./023-record-match-service.poml) | 🔲 not-started | 022 | bff-api, azure-search |
| 024 | [Add match endpoints](./024-add-match-endpoints.poml) | 🔲 not-started | 023 | bff-api, minimal-api |
| 025 | [Add RecordTypeSelector PCF](./025-record-type-selector.poml) | 🔲 not-started | 024 | pcf, fluent-ui |
| 026 | [Add RecordMatchSuggestions PCF](./026-record-match-suggestions.poml) | 🔲 not-started | 024 | pcf, fluent-ui |
| 027 | [Add one-click association](./027-one-click-association.poml) | 🔲 not-started | 026 | pcf, dataverse |
| 028 | [End-to-end tests Phase 2](./028-e2e-tests-phase2.poml) | 🔲 not-started | 020-027 | e2e-test |

## Phase 2 Deployment

| ID | Title | Status | Dependencies | Tags |
|----|-------|--------|--------------|------|
| 029 | [Deploy Phase 2 Components](./029-deploy-phase2.poml) | 🔲 not-started | 028 | deploy, azure, dataverse, pcf |

## Project Wrap-up

| ID | Title | Status | Dependencies | Tags |
|----|-------|--------|--------------|------|
| 090 | [Project wrap-up](./090-project-wrap-up.poml) | 🔲 not-started | 029 | docs, cleanup |

---

## Execution Order

### Recommended Start Sequence
1. **Parallel start**: Tasks 001, 002, 005, 016 (no dependencies)
2. After 001+002: Task 003, 004
3. After 005: Task 006
4. Continue following dependency chain

### Critical Path (Updated with Deployments)
001 → 003 → 008 → 009 → Phase 1b → **019 (Deploy P1)** → Phase 2 → **029 (Deploy P2)** → 090

### Deployment Gates
- **Task 019**: Must pass before Phase 2 begins (validates P1 in real environment)
- **Task 029**: Must pass before project wrap-up (validates full functionality)

---

## Status Legend
- 🔲 not-started
- 🔄 in-progress
- ⏸️ blocked
- ✅ completed
- ⏭️ deferred
