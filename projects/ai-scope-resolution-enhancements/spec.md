# AI Scope Resolution Enhancements - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-29
> **Source**: scope-resolution-update-plan.md

## Executive Summary

Update the scope resolution architecture across all scope types (Tools, Skills, Knowledge, Actions) to eliminate stub dictionary anti-patterns and fix the "No handler registered for job type" error. This project enables configuration-driven extensibility, runtime handler discovery, and ensures all scopes are loaded dynamically from Dataverse without code deployment.

**Business Value**: Users can create new AI playbook scopes in Dataverse and they work immediately without waiting for backend deployments. Eliminates dead-letter queue errors blocking document analysis.

---

## Scope

### In Scope

1. **Fix Job Handler Registration** (Critical - addresses "NoHandler" dead-letter error)
   - Register `AppOnlyDocumentAnalysis` job handler in DI
   - Verify job handler discovery via Service Bus processor
   - Test end-to-end with actual email processing

2. **Complete Tool Resolution** (Phase 1 - 80% done)
   - Deploy existing tool resolution changes to dev environment
   - Test GenericAnalysisHandler fallback behavior
   - Verify Dataverse queries working in production

3. **Implement Skill Resolution** (Phase 2)
   - Replace `GetSkillAsync` stub dictionary with Dataverse Web API query
   - Add DTO classes (SkillEntity, SkillTypeReference)
   - Map to domain model (AnalysisSkill)

4. **Implement Knowledge Resolution** (Phase 3)
   - Replace `GetKnowledgeAsync` stub dictionary with Dataverse Web API query
   - Add DTO classes (KnowledgeEntity, KnowledgeTypeReference)
   - Map KnowledgeType from lookup name

5. **Implement Action Resolution** (Phase 4)
   - Replace `GetActionAsync` stub dictionary with Dataverse Web API query
   - Add DTO classes (ActionEntity, ActionTypeReference)
   - Extract SortOrder from type name numbering

6. **Remove Stub Dictionaries** (Phase 5)
   - Delete all stub dictionaries from ScopeResolverService.cs (lines 25-129)
   - Update class documentation
   - Ensure no test dependencies on stubs

7. **Implement Handler Discovery API** (Phase 6)
   - Create `GET /api/ai/handlers` endpoint
   - Add ConfigurationSchema to ToolHandlerMetadata
   - Update ALL handlers with configuration JSON Schema
   - Implement 5-minute caching with IMemoryCache

8. **Comprehensive Testing** (Phase 7)
   - Unit tests for all scope resolution methods
   - Integration tests for handler discovery API
   - Manual user testing across all document creation flows
   - End-to-end playbook execution tests

9. **Deployment and Monitoring** (Phase 8)
   - Deploy to dev environment (spe-api-dev-67e2xz)
   - Monitor dead-letter queue, scope resolution failures, handler warnings
   - Measure performance (scope resolution <200ms p95)

### Out of Scope

- PCF control implementation (separate project: ai-playbook-scope-editor-PCF)
- Frontend UI changes (handler validation UI)
- Dataverse schema changes (existing schema sufficient)
- Staging environment setup (dev-only deployment)
- New Application Insights alert configuration (use existing queries)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` - Replace stubs with Dataverse queries
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs` - Already loads real GUIDs from N:N relationships
- `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` - Handler resolution (already enhanced)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` - Handler resolution (already enhanced)
- `src/server/api/Sprk.Bff.Api/Workers/Office/AiAnalysisNodeExecutor.cs` - Handler resolution (already enhanced)
- `src/server/api/Sprk.Bff.Api/Program.cs` - Job handler registration (DI)
- `src/server/api/Sprk.Bff.Api/Endpoints/AiEndpoints.cs` - New handler discovery endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerMetadata.cs` - Add ConfigurationSchema property
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/*.cs` - Update all handlers with ConfigurationSchema

---

## Requirements

### Functional Requirements

#### FR-01: Fix Job Handler Registration
**Description**: Register AppOnlyDocumentAnalysis job handler so Service Bus messages are processed successfully.

**Acceptance**:
- No "NoHandler" dead-letter errors for `JobType: AppOnlyDocumentAnalysis`
- Email processing completes successfully end-to-end
- sprk_analysis records created with tool results
- Logs show handler executing (not "No handler registered")

#### FR-02: Scope Resolution from Dataverse (All Types)
**Description**: All scope types (Tools, Skills, Knowledge, Actions) loaded dynamically from Dataverse Web API, no stub dictionaries.

**Acceptance**:
- GetToolAsync queries `sprk_analysistools` with `$expand=sprk_ToolTypeId`
- GetSkillAsync queries `sprk_promptfragments` with `$expand=sprk_SkillTypeId`
- GetKnowledgeAsync queries `sprk_contents` with `$expand=sprk_KnowledgeTypeId`
- GetActionAsync queries `sprk_systemprompts` with `$expand=sprk_ActionTypeId`
- Null returned only when entity doesn't exist in Dataverse (not missing from stub)
- Logs show: `[GET {TYPE}] Loaded {name} from Dataverse`

#### FR-03: Handler Resolution with Fallback
**Description**: Tool handler resolution checks HandlerClass field first, falls back to GenericAnalysisHandler, provides helpful error messages.

**Acceptance**:
- If `sprk_handlerclass` specified and found → Use custom handler
- If `sprk_handlerclass` specified but not found → Log available handlers, fall back to GenericAnalysisHandler
- If `sprk_handlerclass` NULL → Use type-based lookup or GenericAnalysisHandler
- Logs list all registered handlers when handler not found
- Analysis succeeds even when custom handler missing (fallback works)

#### FR-04: Handler Discovery API
**Description**: GET /api/ai/handlers endpoint returns metadata for all registered handlers including ConfigurationSchema.

**Acceptance**:
- Endpoint returns 200 OK with handler metadata array
- Each handler includes: handlerId, name, description, version, supportedToolTypes, supportedInputTypes, parameters, configurationSchema, isEnabled
- Response cached for 5 minutes
- Endpoint requires authentication (401 if unauthenticated)
- Disabled handlers excluded from response
- Swagger documentation generated

#### FR-05: Configuration Schema for All Handlers
**Description**: All tool handlers include JSON Schema Draft 07 configuration schema in metadata.

**Acceptance**:
- GenericAnalysisHandler includes schema for operations (extract, classify, validate, generate, transform, analyze)
- EntityExtractorHandler includes schema for entityTypes, confidenceThreshold
- SummaryHandler includes schema for maxLength, format
- ClauseAnalyzerHandler includes schema for clauseTypes, comparisonMode
- DocumentClassifierHandler includes schema for categories, threshold
- RiskDetectorHandler includes schema for riskCategories, severity
- ClauseComparisonHandler includes schema for baselineDocumentId
- DateExtractorHandler includes schema for dateFormats, timezone
- FinancialCalculatorHandler includes schema for calculationType, precision
- All schemas follow JSON Schema Draft 07 specification
- Schemas include type, properties, required fields, validation rules

### Non-Functional Requirements

#### NFR-01: Performance
- Scope resolution latency < 200ms (p95)
- GET /api/ai/handlers response < 100ms (cached)
- No performance regression vs. stub dictionaries
- Analysis success rate > 98%

#### NFR-02: Reliability
- Dead-letter queue errors < 1/day (down from current ~5-10/hour)
- Scope resolution failure rate < 2%
- Handler not found warnings < 10/hour
- Idempotent handler operations (safe under retries)

#### NFR-03: Maintainability
- Zero code deployment required for new scope configurations
- Users can add tools/skills/knowledge/actions in Dataverse and they work immediately
- Helpful error messages (list available handlers when not found)
- Consistent resolution pattern across all scope types

#### NFR-04: Observability
- Log scope loads: `[GET {TYPE}] Loading {scopeType} {id} from Dataverse`
- Log successful loads: `[GET {TYPE}] Loaded {name} from Dataverse: {details}`
- Log handler fallback: `Handler '{class}' not found. Available: [{list}]. Falling back...`
- Log handler not found: `No handler registered for job type '{type}'`
- All logs include CorrelationId for tracing

---

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API + BackgroundService - Use BackgroundService workers, no Azure Functions
- **ADR-004**: Job Contract - Use standard job contract schema, idempotent handlers, propagate CorrelationId
- **ADR-010**: DI Minimalism - Keep DI registrations ≤15 non-framework lines, use feature modules
- **ADR-013**: AI Architecture - Extend Sprk.Bff.Api with AI endpoints, use SpeFileStore for file access
- **ADR-014**: AI Caching - Use Redis for expensive AI results (handler metadata uses IMemoryCache for 5min TTL)
- **ADR-017**: Job Status - Persist status transitions, return 202 with jobId and status URL

### MUST Rules

#### From ADR-001 (Minimal API + BackgroundService)
- ✅ MUST use BackgroundService + Service Bus for async work
- ✅ MUST register all services in single `Program.cs` middleware pipeline
- ✅ MUST return `ProblemDetails` for all API errors
- ❌ MUST NOT introduce Azure Functions projects or packages

#### From ADR-004 (Job Contract)
- ✅ MUST implement handlers as idempotent (safe under at-least-once)
- ✅ MUST use deterministic IdempotencyKey patterns
- ✅ MUST propagate CorrelationId from original request
- ✅ MUST emit JobOutcome events (Completed, Failed, Poisoned)
- ❌ MUST NOT place document bytes or large blobs in payload
- ❌ MUST NOT assume exactly-once delivery

#### From ADR-010 (DI Minimalism)
- ✅ MUST register concretes by default (not interfaces)
- ✅ MUST use feature module extensions (`AddSpaarkeCore`, `AddWorkersModule`)
- ✅ MUST keep DI registrations ≤15 non-framework lines
- ❌ MUST NOT create interfaces without genuine seam requirement
- ❌ MUST NOT inline registrations (use feature modules)

#### From ADR-013 (AI Architecture)
- ✅ MUST follow ADR-001 Minimal API patterns for AI endpoints
- ✅ MUST use endpoint filters for AI authorization (ADR-008)
- ✅ MUST access files through SpeFileStore only (ADR-007)
- ✅ MUST apply rate limiting to all AI endpoints
- ❌ MUST NOT create separate AI microservice
- ❌ MUST NOT call Azure AI services directly from PCF

### Existing Patterns to Follow

#### Dataverse Web API Query Pattern (from existing GetToolAsync)
```csharp
var url = $"sprk_analysistools({toolId})?$expand=sprk_ToolTypeId($select=sprk_name)";
var response = await _httpClient.GetAsync(url, cancellationToken);

if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    _logger.LogWarning("[GET TOOL] Tool {ToolId} not found in Dataverse", toolId);
    return null;
}

response.EnsureSuccessStatusCode();

var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
// Map to domain model...
```

**Reference**: `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs:860-906` (Tool resolution already implemented)

#### Handler Resolution with Fallback (from existing AppOnlyAnalysisService)
```csharp
// 1. Check sprk_handlerclass field first
if (!string.IsNullOrWhiteSpace(tool.HandlerClass))
{
    handler = registry.GetHandler(tool.HandlerClass);
}

// 2. Fall back to GenericAnalysisHandler
if (handler == null)
{
    _logger.LogWarning("Handler '{HandlerClass}' not found. Available: [{Available}]. Falling back...",
        tool.HandlerClass, string.Join(", ", registry.GetRegisteredHandlerIds()));
    handler = registry.GetHandler("GenericAnalysisHandler");
}

// 3. Type-based lookup (if no HandlerClass specified)
else
{
    var handlers = registry.GetHandlersByType(tool.Type);
    handler = handlers.FirstOrDefault();
}
```

**Reference**: `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs:403-410` (Already enhanced)

#### Job Handler Registration Pattern
```csharp
// In Program.cs or feature module
services.AddSingleton<IJobHandler, AppOnlyDocumentAnalysisJobHandler>();
services.AddSingleton<IJobHandler, ProfileSummaryJobHandler>();
// ... other handlers
```

**Reference**: See ADR-004 Job Contract pattern

---

## Success Criteria

### Functional Completeness
- [ ] Job handler registration fixed (NoHandler error resolved)
- [ ] All scopes (Tools, Skills, Knowledge, Actions) loaded from Dataverse
- [ ] No stub dictionaries remain in codebase
- [ ] GenericAnalysisHandler executes custom tools successfully
- [ ] Handler discovery API returns all registered handlers with ConfigurationSchema
- [ ] All handlers updated with JSON Schema

### Performance Targets
- [ ] Scope resolution latency < 200ms (p95)
- [ ] GET /api/ai/handlers response < 100ms (cached)
- [ ] Analysis success rate > 98%
- [ ] No performance regression vs. baseline

### Reliability Targets
- [ ] Dead-letter queue errors < 1/day (currently ~5-10/hour)
- [ ] Scope resolution failure rate < 2%
- [ ] Handler not found warnings < 10/hour
- [ ] Zero breaking changes to existing analyses

### User Experience
- [ ] Users can add new tools in Dataverse and they work immediately (no deployment)
- [ ] Helpful error messages when handler not found (lists available handlers)
- [ ] End-to-end testing across all document creation flows passes:
  - [ ] File upload using UniversalDocumentUpload
  - [ ] Email-to-document automation
  - [ ] Outlook add-in document save
  - [ ] Word add-in document save

---

## Dependencies

### Prerequisites

- [x] Phase 1 tool resolution code already deployed (needs testing)
- [x] Handler resolution fallback already implemented in AppOnlyAnalysisService, AnalysisOrchestrationService, AiAnalysisNodeExecutor
- [ ] GenericAnalysisHandler registered in DI (verify)
- [ ] IToolHandlerRegistry functional (verify)

### External Dependencies

- Dataverse entities: sprk_analysistool, sprk_promptfragment, sprk_systemprompt, sprk_content
- Dataverse lookup entities: sprk_aitooltype, sprk_aiskilltype, sprk_analysisactiontype, sprk_aiknowledgetype
- Azure Service Bus queue (for job processing)
- Azure App Service (spe-api-dev-67e2xz) for deployment

### Related Projects

- **ai-playbook-scope-editor-PCF** (separate project) - Will consume handler discovery API for frontend validation

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| **Phase 1 Status** | Has Phase 1 deployment already been completed? | Deployed but has new NoHandler error - must resolve job registration | Task 001 will fix job handler registration, then verify Phase 1 tool resolution works |
| **Work Mode** | Full-time or part-time? | Full-time dedicated work | Tasks structured for parallel execution where possible (e.g., Phases 2-4 can run in parallel after Phase 1) |
| **Testing Approach** | Automated or manual testing? | Unit tests for code components + manual user testing across all document creation flows | Phase 7 includes both xUnit integration tests and checklist-based user testing |
| **ConfigurationSchema Scope** | All handlers or incremental? | ALL handlers should be updated | Phase 6 must update all 9 handlers with JSON Schema (not just Generic or Entity) |
| **Environment** | Staging environment exists? | No staging - dev environment only, treated as production-ready | Deployment tasks target dev only (spe-api-dev-67e2xz), no staging promotion steps |
| **Monitoring** | Need new alerts? | Application Insights is sufficient | Phase 8 monitoring uses existing AI queries, no new alert configuration |

---

## Assumptions

*Proceeding with these assumptions (owner accepted defaults):*

- **Deployment Frequency**: Changes deployed incrementally after each phase validation (not one big-bang deployment)
- **Rollback Strategy**: Revert via Azure App Service deployment slots if critical errors occur (>10% failure rate)
- **Cache Invalidation**: 5-minute TTL for handler metadata is sufficient (no manual invalidation endpoint needed initially)
- **Test Data**: Real Dataverse data used for testing (not mocks or synthetic data)
- **Documentation Updates**: HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md already updated (separate work completed)

---

## Unresolved Questions

*None - all blocking questions answered by owner.*

---

## Implementation Phases (High-Level)

**Note**: Detailed task breakdown will be generated by project-pipeline skill.

### Phase 0: Job Handler Registration Fix (CRITICAL - 1 day)
- Fix NoHandler dead-letter error
- Register AppOnlyDocumentAnalysisJobHandler in DI
- Test end-to-end with email processing

### Phase 1: Complete Tool Resolution (1 day)
- Deploy existing code to dev
- Test GenericAnalysisHandler fallback
- Verify Dataverse queries working

### Phase 2-4: Scope Resolution (6-9 days - CAN RUN IN PARALLEL)
- Phase 2: Skill resolution (2-3 days)
- Phase 3: Knowledge resolution (2-3 days)
- Phase 4: Action resolution (2-3 days)

### Phase 5: Stub Removal (1 day)
- Delete all stub dictionaries
- Update documentation

### Phase 6: Handler Discovery API (2-3 days)
- Create endpoint
- Update all handlers with ConfigurationSchema
- Implement caching

### Phase 7: Testing (3-4 days)
- Unit tests
- Integration tests
- User testing across all flows

### Phase 8: Deployment & Monitoring (2 days)
- Deploy to dev
- Monitor metrics
- Verify success criteria

**Total Estimated Duration**: 15-20 business days (full-time dedicated work)

---

## Task Execution Strategy

**Parallel Execution Opportunities**:
- After Phase 1 complete: Phases 2, 3, 4 can execute in parallel (Skill, Knowledge, Action resolution are independent)
- After Phase 6 complete: Handler ConfigurationSchema updates can be parallelized (9 handlers)
- During Phase 7: Unit tests and user testing can overlap

**Sequential Dependencies**:
- Phase 0 → Phase 1 (must fix job handler before tool resolution works)
- Phases 2-4 → Phase 5 (must complete all resolutions before removing stubs)
- Phase 5 → Phase 6 (must remove stubs before adding discovery API)
- Phase 6 → Phase 7 (must complete API before comprehensive testing)
- Phase 7 → Phase 8 (must pass tests before deployment)

---

*AI-optimized specification. Original design: scope-resolution-update-plan.md*
