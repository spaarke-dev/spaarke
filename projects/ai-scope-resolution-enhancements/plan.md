# AI Scope Resolution Enhancements - Implementation Plan

> **Version**: 1.0
> **Created**: 2026-01-29
> **Status**: Ready for Execution

---

## Architecture Context

### Discovered Resources

**ADRs (6):**
- `.claude/adr/ADR-001-minimal-api.md` - BackgroundService patterns
- `.claude/adr/ADR-004-job-contract.md` - Job handler interface
- `.claude/adr/ADR-010-di-minimalism.md` - DI registration patterns
- `.claude/adr/ADR-013-ai-architecture.md` - AI tool framework
- `.claude/adr/ADR-014-ai-caching.md` - Handler metadata caching
- `.claude/adr/ADR-017-job-status.md` - Job status persistence

**Patterns (6):**
- `.claude/patterns/ai/analysis-scopes.md` - Three-tier scope system
- `.claude/patterns/api/background-workers.md` - Job handler registration
- `.claude/patterns/dataverse/web-api-client.md` - Dataverse Web API client
- `.claude/patterns/api/service-registration.md` - Feature module DI
- `.claude/patterns/api/endpoint-definition.md` - Minimal API endpoints
- `.claude/patterns/testing/unit-test-structure.md` - Unit test AAA pattern

**Scripts (4):**
- `scripts/Deploy-BffApi.ps1` - API deployment
- `scripts/Test-SdapBffApi.ps1` - API testing
- `scripts/Debug-OfficeWorkers.ps1` - Worker debugging
- `scripts/Diagnose-AiSummaryService.ps1` - AI service diagnosis

**Knowledge Docs (3):**
- `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md` - User guide
- `docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md` - Architecture overview
- `docs/architecture/AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md` - Scope design

---

## Phase Breakdown

### Phase 0: Fix Job Handler Registration (CRITICAL)

**Objective**: Resolve "No handler registered for job type" dead-letter error

**Duration**: 1 day

**Deliverables**:
1. Identify why AppOnlyDocumentAnalysisJobHandler isn't registered
2. Add correct DI registration in WorkersModule or Program.cs
3. Verify Service Bus processor discovers handler
4. Test end-to-end with email processing
5. Confirm no dead-letter errors

**Acceptance Criteria**:
- AppOnlyDocumentAnalysis jobs processed successfully
- Logs show handler executing (not "No handler registered")
- sprk_analysis records created

**Files to Modify**:
- `src/server/api/Sprk.Bff.Api/Program.cs` (or WorkersModule)
- `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` (verify)

---

### Phase 1: Complete Tool Resolution

**Objective**: Verify tool resolution from Dataverse works end-to-end

**Duration**: 1 day

**Deliverables**:
1. Test GetToolAsync queries Dataverse correctly
2. Verify GenericAnalysisHandler fallback works
3. Confirm handler resolution logs show available handlers
4. Deploy and test with real playbook

**Acceptance Criteria**:
- Tools loaded from Dataverse (not stubs)
- Logs show: "[GET TOOL] Loaded tool from Dataverse: {ToolName}"
- Fallback to GenericAnalysisHandler works when handler not found

**Files to Verify**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` (GetToolAsync)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs`

---

### Phase 2: Implement Skill Resolution

**Objective**: Replace GetSkillAsync stub with Dataverse query

**Duration**: 2-3 days

**Deliverables**:
1. Create SkillEntity and SkillTypeReference DTOs
2. Implement GetSkillAsync with Dataverse Web API query
3. Map sprk_SkillTypeId lookup to Category
4. Add logging for skill resolution
5. Unit tests for GetSkillAsync

**Acceptance Criteria**:
- GetSkillAsync queries `sprk_promptfragments({id})?$expand=sprk_SkillTypeId`
- PromptFragment mapped to AnalysisSkill.PromptFragment
- Category mapped from lookup name
- Returns null only if skill doesn't exist in Dataverse

**Code Pattern**:
```csharp
var url = $"sprk_promptfragments({skillId})?$expand=sprk_SkillTypeId($select=sprk_name)";
```

**Files to Modify**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ScopeResolverServiceTests.cs` (new)

---

### Phase 3: Implement Knowledge Resolution

**Objective**: Replace GetKnowledgeAsync stub with Dataverse query

**Duration**: 2-3 days

**Deliverables**:
1. Create KnowledgeEntity and KnowledgeTypeReference DTOs
2. Implement GetKnowledgeAsync with Dataverse Web API query
3. Map KnowledgeType from lookup name (Inline, RagIndex)
4. Handle DeploymentId for RAG type
5. Unit tests for GetKnowledgeAsync

**Acceptance Criteria**:
- GetKnowledgeAsync queries `sprk_contents({id})?$expand=sprk_KnowledgeTypeId`
- Content mapped correctly
- DeploymentId populated for RAG type
- Type mapped from lookup name

**Code Pattern**:
```csharp
var url = $"sprk_contents({knowledgeId})?$expand=sprk_KnowledgeTypeId($select=sprk_name)";
```

**Files to Modify**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ScopeResolverServiceTests.cs`

---

### Phase 4: Implement Action Resolution

**Objective**: Replace GetActionAsync stub with Dataverse query

**Duration**: 2-3 days

**Deliverables**:
1. Create ActionEntity and ActionTypeReference DTOs
2. Implement GetActionAsync with Dataverse Web API query
3. Extract SortOrder from type name numbering (e.g., "01 - Extraction" → 1)
4. Map SystemPrompt correctly
5. Unit tests for GetActionAsync

**Acceptance Criteria**:
- GetActionAsync queries `sprk_systemprompts({id})?$expand=sprk_ActionTypeId`
- SystemPrompt mapped to AnalysisAction.SystemPrompt
- SortOrder extracted from type name prefix
- Returns null only if action doesn't exist

**Code Pattern**:
```csharp
var url = $"sprk_systemprompts({actionId})?$expand=sprk_ActionTypeId($select=sprk_name)";
```

**Files to Modify**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ScopeResolverServiceTests.cs`

---

### Phase 5: Remove Stub Dictionaries

**Objective**: Delete all stub dictionary code once Dataverse queries proven

**Duration**: 1 day

**Deliverables**:
1. Delete _stubActions dictionary (lines 25-45)
2. Delete _stubSkills dictionary (lines 47-73)
3. Delete _stubKnowledge dictionary (lines 75-93)
4. Delete _stubTools dictionary (lines 95-129)
5. Update class documentation
6. Verify tests still pass

**Acceptance Criteria**:
- All stub dictionaries removed
- No references to stub data in comments
- All unit tests pass with real Dataverse queries or mocks
- No test dependencies on specific stub GUIDs

**Files to Modify**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`

---

### Phase 6: Handler Discovery API

**Objective**: Create GET /api/ai/handlers endpoint with ConfigurationSchema

**Duration**: 2-3 days

**Deliverables**:
1. Create handler discovery endpoint
2. Add ConfigurationSchema to ToolHandlerMetadata record
3. Update ALL handlers with JSON Schema Draft 07:
   - GenericAnalysisHandler
   - EntityExtractorHandler
   - SummaryHandler
   - ClauseAnalyzerHandler
   - DocumentClassifierHandler
   - RiskDetectorHandler
   - ClauseComparisonHandler
   - DateExtractorHandler
   - FinancialCalculatorHandler
4. Implement 5-minute IMemoryCache caching
5. Unit tests for endpoint

**Acceptance Criteria**:
- GET /api/ai/handlers returns 200 OK
- Response includes all handlers with configurationSchema
- Disabled handlers excluded
- Response cached for 5 minutes
- Requires authentication (401 if unauthenticated)

**Files to Modify**:
- `src/server/api/Sprk.Bff.Api/Endpoints/AiEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerMetadata.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/*.cs` (all handlers)

---

### Phase 7: Testing & Validation

**Objective**: Comprehensive testing across all scope types and flows

**Duration**: 3-4 days

**Deliverables**:

**Unit Tests**:
1. ScopeResolverService tests (all 4 scope types)
2. Handler discovery endpoint tests
3. Handler resolution fallback tests

**Integration Tests**:
1. End-to-end playbook execution tests
2. Handler discovery API tests

**User Testing (Manual)**:
1. File upload using UniversalDocumentUpload → Verify analysis
2. Email-to-document automation → Verify analysis
3. Outlook add-in document save → Verify analysis
4. Word add-in document save → Verify analysis

**Test Cases**:
- Tool with valid HandlerClass → Handler found
- Tool with invalid HandlerClass → Falls back to GenericAnalysisHandler
- Tool with null HandlerClass → Uses GenericAnalysisHandler
- Tool with non-existent GUID → Returns null
- (Same patterns for Skill, Knowledge, Action)

**Files to Modify**:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ScopeResolverServiceTests.cs`
- `tests/integration/Sprk.Bff.Api.Integration.Tests/` (new files)

---

### Phase 8: Deployment & Monitoring

**Objective**: Deploy to dev environment and monitor success metrics

**Duration**: 2 days

**Deliverables**:
1. Build and package API
2. Deploy to spe-api-dev-67e2xz
3. Monitor Application Insights for 24-48 hours
4. Verify success criteria met
5. Document any issues and fixes

**Monitoring Metrics**:
| Metric | Threshold | Action if Exceeded |
|--------|-----------|-------------------|
| Dead-letter queue messages | > 5/hour | Investigate |
| Scope resolution failures | > 2% | Review logs |
| Handler not found warnings | > 10/hour | Check registration |
| Analysis success rate | < 95% | Investigate configs |

**Log Queries**:
- `"[GET TOOL] Loading tool"` → Verify Dataverse queries working
- `"Available handlers:"` → Track fallback frequency
- `"Handler not found"` → Identify misconfigured tools

**Scripts to Use**:
- `scripts/Deploy-BffApi.ps1`
- `scripts/Test-SdapBffApi.ps1`

---

## Task Decomposition Summary

| Phase | Tasks | Parallel? | Dependencies |
|-------|-------|-----------|--------------|
| Phase 0 | 001-005 | No | None |
| Phase 1 | 010-013 | No | Phase 0 |
| Phase 2 | 020-024 | **Yes** | Phase 1 |
| Phase 3 | 030-034 | **Yes** | Phase 1 |
| Phase 4 | 040-044 | **Yes** | Phase 1 |
| Phase 5 | 050-052 | No | Phases 2-4 |
| Phase 6 | 060-069 | No | Phase 5 |
| Phase 7 | 070-079 | Partial | Phase 6 |
| Phase 8 | 080-082 | No | Phase 7 |

**Parallel Execution Groups**:
- **Group A**: Tasks 020-024, 030-034, 040-044 (Skill, Knowledge, Action resolution - after Phase 1)
- **Group B**: Tasks 063-069 (Handler schema updates - can be parallelized within Phase 6)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Dataverse query performance | Add caching if needed (Redis) |
| Schema mismatch (field names) | Test with real Dataverse data |
| Handler not found in production | Fallback to GenericAnalysisHandler (already implemented) |
| Breaking existing analyses | Comprehensive testing before stub removal |

---

## References

- [spec.md](spec.md) - Full requirements specification
- [scope-resolution-update-plan.md](scope-resolution-update-plan.md) - Original design document
- `.claude/adr/ADR-013-ai-architecture.md` - AI architecture constraints
- `.claude/patterns/api/background-workers.md` - Job handler patterns

---

*Generated by project-pipeline skill on 2026-01-29*
