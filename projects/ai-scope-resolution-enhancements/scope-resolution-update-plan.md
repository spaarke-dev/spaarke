# Scope Resolution Update Plan - Comprehensive Approach

> **Project**: Fix Scope Resolution Across All Scope Types
> **Created**: 2026-01-29
> **Status**: Planning Phase
> **Related**: ai-playbook-scope-editor-PCF design.md

---

## Executive Summary

Update the scope resolution architecture across all scope types (Tools, Skills, Knowledge, Actions) to eliminate stub dictionary anti-patterns, support configuration-driven extensibility, and enable runtime handler discovery. This plan addresses the root cause of the "Playbook has no tools configured" error and ensures future scope additions work without code deployment.

**Key Outcomes:**
1. All scopes loaded from Dataverse (no stub dictionaries)
2. GenericAnalysisHandler supports custom tools without code deployment
3. Handler discovery API enables frontend validation
4. Graceful fallback when handlers not found
5. Consistent resolution pattern across all scope types

---

## Current State Analysis

### Problem: Stub Dictionary Anti-Pattern

**Root Cause:**
- [ScopeResolverService.cs](src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs:25-129) contains hardcoded stub dictionaries with fake GUIDs
- [PlaybookService.cs](src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs) loads **real** GUIDs from Dataverse N:N relationships
- **Mismatch** → ScopeResolverService returns null → Tools/Skills/Knowledge/Actions not found → Analysis fails

**Evidence:**
```
Dead-letter queue error:
"Playbook 'Document Profile' has no tools configured"

Root cause trace:
1. PlaybookService.LoadToolIdsAsync() returns Guid("abc-123-real-guid-from-dataverse")
2. ScopeResolverService.GetToolAsync(Guid("abc-123-real-guid-from-dataverse"))
3. Checks _stubTools dictionary with fake GUIDs → Not found → Returns null
4. Playbook has empty Tools collection → Analysis fails
```

### Affected Code Sections

| Service | Method | Issue | Lines |
|---------|--------|-------|-------|
| ScopeResolverService | GetToolAsync | Uses stub dictionary | 860-906 |
| ScopeResolverService | GetSkillAsync | Uses stub dictionary | Not yet querying Dataverse |
| ScopeResolverService | GetKnowledgeAsync | Uses stub dictionary | Not yet querying Dataverse |
| ScopeResolverService | GetActionAsync | Uses stub dictionary | Not yet querying Dataverse |
| AppOnlyAnalysisService | ExecuteToolsAsync | Handler resolution by type only (no HandlerClass check) | 403-410 (FIXED) |
| AnalysisOrchestrationService | ExecuteToolsAsync | Handler resolution by type only (no HandlerClass check) | 1281-1284 (FIXED) |
| AiAnalysisNodeExecutor | ExecuteAsync | Handler resolution by HandlerClass (no fallback) | 60-64, 112-121 (ENHANCED) |

### Already Completed (Part 1)

✅ **Enhanced handler resolution with fallback:**
- AppOnlyAnalysisService now checks HandlerClass first, lists available handlers on error, falls back to GenericAnalysisHandler
- AnalysisOrchestrationService same pattern
- AiAnalysisNodeExecutor lists available handlers in error messages

---

## Architecture Vision

### Three-Tier Scope Resolution

```
┌────────────────────────────────────────────────────────────────┐
│  Tier 1: Configuration (Dataverse - Source of Truth)           │
│  - sprk_analysistool, sprk_promptfragment, sprk_systemprompt,  │
│    sprk_content                                                 │
│  - Must work without code deployment (new records auto-work)   │
│  - HandlerClass NULL → Defaults to generic handler              │
└────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────┐
│  Tier 2: Generic Execution (Handles 95% of Cases)              │
│  - GenericAnalysisHandler (Tools)                              │
│  - GenericSkillHandler (Skills - future)                       │
│  - GenericKnowledgeHandler (Knowledge - future)                │
│  - Reads configuration JSON, executes via AI prompts            │
│  - No arbitrary code execution (security safe)                  │
└────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────┐
│  Tier 3: Custom Handlers (Complex Scenarios Only)              │
│  - EntityExtractorHandler, SummaryHandler, etc.                │
│  - Registered in DI at startup                                  │
│  - Discoverable via IToolHandlerRegistry                        │
│  - Optional - specified in HandlerClass field                   │
└────────────────────────────────────────────────────────────────┘
```

### Unified Resolution Pattern (All Scope Types)

**Pattern:**
1. **Load from Dataverse** (no stubs) using HttpClient + Web API
2. **Expand lookups** ($expand for type relationships)
3. **Map to domain model** (AnalysisTool, AnalysisSkill, AnalysisKnowledge, AnalysisAction)
4. **Resolve handler** (if applicable):
   - Check HandlerClass field first
   - If not found, fall back to GenericHandler
   - If no GenericHandler, fall back to type-based lookup
5. **Return null only if entity doesn't exist in Dataverse**

---

## Implementation Plan

### Phase 1: Complete Tool Resolution (MOSTLY DONE)

**Status:** 80% complete

**Remaining Tasks:**

#### Task 1.1: Deploy Tool Resolution Fix
- ✅ Code changes complete (GetToolAsync queries Dataverse)
- ⏳ Deploy publish.zip to spe-api-dev-67e2xz
- ⏳ Test with real playbook execution
- ⏳ Verify fallback to GenericAnalysisHandler works

**Acceptance Criteria:**
- Email processing creates sprk_analysis records (no dead-letter errors)
- Logs show: "Loaded tool from Dataverse: {ToolName}"
- If handler not found, logs show: "Available handlers: [...]"

#### Task 1.2: Verify GenericAnalysisHandler Registration
- Check that GenericAnalysisHandler is registered in DI
- Verify it's discoverable via IToolHandlerRegistry
- Test direct handler lookup: `registry.GetHandler("GenericAnalysisHandler")`

**Acceptance Criteria:**
- GenericAnalysisHandler appears in registry.GetRegisteredHandlerIds()
- Handler can be retrieved by ID
- Handler executes successfully with valid configuration

---

### Phase 2: Implement Skill Resolution (Dataverse Query)

**Duration:** 2-3 days

**Objective:** Replace `GetSkillAsync` stub dictionary with Dataverse query pattern (same as GetToolAsync).

#### Task 2.1: Update GetSkillAsync Method

**File:** [ScopeResolverService.cs](src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs)

**Current Implementation:**
```csharp
public Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
{
    if (_stubSkills.TryGetValue(skillId, out var skill))
        return Task.FromResult<AnalysisSkill?>(skill);

    _logger.LogWarning("Skill {SkillId} not found in stub data", skillId);
    return Task.FromResult<AnalysisSkill?>(null);
}
```

**New Implementation Pattern:**
```csharp
public async Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
{
    _logger.LogInformation("[GET SKILL] Loading skill {SkillId} from Dataverse", skillId);

    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_promptfragments({skillId})?$expand=sprk_SkillTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning("[GET SKILL] Skill {SkillId} not found in Dataverse", skillId);
        return null;
    }

    response.EnsureSuccessStatusCode();

    var entity = await response.Content.ReadFromJsonAsync<SkillEntity>(cancellationToken);
    if (entity == null)
    {
        _logger.LogWarning("[GET SKILL] Failed to deserialize skill {SkillId}", skillId);
        return null;
    }

    var skill = new AnalysisSkill
    {
        Id = entity.Id,
        Name = entity.Name ?? "Unnamed Skill",
        Description = entity.Description,
        PromptFragment = entity.PromptFragment,
        Category = entity.SkillTypeId?.Name ?? "General",
        OwnerType = ScopeOwnerType.System,
        IsImmutable = false
    };

    _logger.LogInformation("[GET SKILL] Loaded skill from Dataverse: {SkillName} (Category: {Category})",
        skill.Name, skill.Category);

    return skill;
}
```

**DTO Classes:**
```csharp
private class SkillEntity
{
    [JsonPropertyName("sprk_promptfragmentid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_promptfragment")]
    public string? PromptFragment { get; set; }

    [JsonPropertyName("sprk_SkillTypeId")]
    public SkillTypeReference? SkillTypeId { get; set; }
}

private class SkillTypeReference
{
    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }
}
```

**Acceptance Criteria:**
- GetSkillAsync queries Dataverse Web API
- Skill loaded with correct PromptFragment
- Category mapped from sprk_SkillTypeId lookup
- Logs show successful load or not found

---

### Phase 3: Implement Knowledge Resolution (Dataverse Query)

**Duration:** 2-3 days

**Objective:** Replace `GetKnowledgeAsync` stub dictionary with Dataverse query pattern.

#### Task 3.1: Update GetKnowledgeAsync Method

**New Implementation Pattern:**
```csharp
public async Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken cancellationToken)
{
    _logger.LogInformation("[GET KNOWLEDGE] Loading knowledge {KnowledgeId} from Dataverse", knowledgeId);

    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_contents({knowledgeId})?$expand=sprk_KnowledgeTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning("[GET KNOWLEDGE] Knowledge {KnowledgeId} not found in Dataverse", knowledgeId);
        return null;
    }

    response.EnsureSuccessStatusCode();

    var entity = await response.Content.ReadFromJsonAsync<KnowledgeEntity>(cancellationToken);
    if (entity == null)
    {
        _logger.LogWarning("[GET KNOWLEDGE] Failed to deserialize knowledge {KnowledgeId}", knowledgeId);
        return null;
    }

    // Map KnowledgeType from lookup name
    var knowledgeType = MapKnowledgeTypeName(entity.KnowledgeTypeId?.Name ?? "");

    var knowledge = new AnalysisKnowledge
    {
        Id = entity.Id,
        Name = entity.Name ?? "Unnamed Knowledge",
        Description = entity.Description,
        Type = knowledgeType,
        Content = entity.Content,
        DeploymentId = entity.DeploymentId,
        OwnerType = ScopeOwnerType.System,
        IsImmutable = false
    };

    _logger.LogInformation("[GET KNOWLEDGE] Loaded knowledge from Dataverse: {KnowledgeName} (Type: {Type})",
        knowledge.Name, knowledge.Type);

    return knowledge;
}

private static KnowledgeType MapKnowledgeTypeName(string typeName)
{
    return typeName switch
    {
        string s when s.Contains("Standards", StringComparison.OrdinalIgnoreCase) => KnowledgeType.Inline,
        string s when s.Contains("Regulations", StringComparison.OrdinalIgnoreCase) => KnowledgeType.RagIndex,
        _ => KnowledgeType.Inline // Default
    };
}
```

**DTO Classes:**
```csharp
private class KnowledgeEntity
{
    [JsonPropertyName("sprk_contentid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_content")]
    public string? Content { get; set; }

    [JsonPropertyName("sprk_deploymentid")]
    public Guid? DeploymentId { get; set; }

    [JsonPropertyName("sprk_KnowledgeTypeId")]
    public KnowledgeTypeReference? KnowledgeTypeId { get; set; }
}

private class KnowledgeTypeReference
{
    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }
}
```

**Acceptance Criteria:**
- GetKnowledgeAsync queries Dataverse Web API
- Knowledge loaded with correct Content and Type
- DeploymentId populated for RAG type
- Type mapped from sprk_KnowledgeTypeId lookup

---

### Phase 4: Implement Action Resolution (Dataverse Query)

**Duration:** 2-3 days

**Objective:** Replace `GetActionAsync` stub dictionary with Dataverse query pattern.

#### Task 4.1: Update GetActionAsync Method

**New Implementation Pattern:**
```csharp
public async Task<AnalysisAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken)
{
    _logger.LogInformation("[GET ACTION] Loading action {ActionId} from Dataverse", actionId);

    await EnsureAuthenticatedAsync(cancellationToken);

    var url = $"sprk_systemprompts({actionId})?$expand=sprk_ActionTypeId($select=sprk_name)";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning("[GET ACTION] Action {ActionId} not found in Dataverse", actionId);
        return null;
    }

    response.EnsureSuccessStatusCode();

    var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
    if (entity == null)
    {
        _logger.LogWarning("[GET ACTION] Failed to deserialize action {ActionId}", actionId);
        return null;
    }

    // Extract sort order from type name (format: "01 - Extraction" → 1)
    int sortOrder = 0;
    if (!string.IsNullOrEmpty(entity.ActionTypeId?.Name))
    {
        var match = System.Text.RegularExpressions.Regex.Match(entity.ActionTypeId.Name, @"^(\d+)\s*-");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var order))
        {
            sortOrder = order;
        }
    }

    var action = new AnalysisAction
    {
        Id = entity.Id,
        Name = entity.Name ?? "Unnamed Action",
        Description = entity.Description,
        SystemPrompt = entity.SystemPrompt,
        SortOrder = sortOrder,
        OwnerType = ScopeOwnerType.System,
        IsImmutable = false
    };

    _logger.LogInformation("[GET ACTION] Loaded action from Dataverse: {ActionName} (SortOrder: {SortOrder})",
        action.Name, action.SortOrder);

    return action;
}
```

**DTO Classes:**
```csharp
private class ActionEntity
{
    [JsonPropertyName("sprk_systempromptid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_systemprompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("sprk_ActionTypeId")]
    public ActionTypeReference? ActionTypeId { get; set; }
}

private class ActionTypeReference
{
    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }
}
```

**Acceptance Criteria:**
- GetActionAsync queries Dataverse Web API
- Action loaded with correct SystemPrompt
- SortOrder extracted from type name numbering
- Logs show successful load or not found

---

### Phase 5: Remove Stub Dictionaries

**Duration:** 1 day (cleanup)

**Objective:** Delete all stub dictionary code once Dataverse queries are proven.

#### Task 5.1: Remove Stub Data

**File:** [ScopeResolverService.cs](src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs:25-129)

**Actions:**
1. Delete `_stubActions` dictionary (lines 25-45)
2. Delete `_stubSkills` dictionary (lines 47-73)
3. Delete `_stubKnowledge` dictionary (lines 75-93)
4. Delete `_stubTools` dictionary (lines 95-129)
5. Update class documentation to remove "stub data" references

**Acceptance Criteria:**
- All stub dictionaries removed
- No references to stub data in comments
- All tests still pass (use real Dataverse or mocks)

---

### Phase 6: Implement Handler Discovery API

**Duration:** 2-3 days

**Objective:** Create GET /api/ai/handlers endpoint for frontend handler discovery and validation.

#### Task 6.1: Create AiHandlers Endpoint

**File:** `src/server/api/Sprk.Bff.Api/Endpoints/AiEndpoints.cs`

**Implementation:**
```csharp
/// <summary>
/// Gets metadata for all registered tool handlers.
/// Used by frontend for handler discovery and validation.
/// </summary>
app.MapGet("/api/ai/handlers", async (
    IToolHandlerRegistry registry,
    IMemoryCache cache,
    CancellationToken cancellationToken) =>
{
    // Cache for 5 minutes (handlers rarely change at runtime)
    var cacheKey = "ai:handlers:metadata";
    if (cache.TryGetValue(cacheKey, out object? cachedValue))
    {
        return Results.Ok(cachedValue);
    }

    var handlers = registry.GetAllHandlerInfo();
    var response = new
    {
        handlers = handlers.Select(h => new
        {
            handlerId = h.HandlerId,
            name = h.Metadata.Name,
            description = h.Metadata.Description,
            version = h.Metadata.Version,
            supportedToolTypes = h.SupportedToolTypes.Select(t => t.ToString()).ToArray(),
            supportedInputTypes = h.Metadata.SupportedInputTypes,
            parameters = h.Metadata.Parameters.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                type = p.Type.ToString(),
                required = p.Required,
                defaultValue = p.DefaultValue
            }).ToArray(),
            isEnabled = h.IsEnabled
        }).ToArray()
    };

    cache.Set(cacheKey, response, TimeSpan.FromMinutes(5));

    return Results.Ok(response);
})
.WithName("GetToolHandlers")
.WithTags("AI")
.Produces<object>(200)
.WithDescription("Gets metadata for all registered tool handlers")
.RequireAuthorization(); // Requires authentication
```

**Acceptance Criteria:**
- Endpoint returns handler metadata in correct format
- Response cached for 5 minutes
- All handlers from registry included
- Disabled handlers excluded
- Swagger documentation generated

#### Task 6.2: Add Configuration Schema to Handler Metadata

**Objective:** Extend ToolHandlerMetadata to include JSON schema for configuration validation.

**File:** Update IAnalysisToolHandler and ToolHandlerMetadata classes

**Changes:**
```csharp
public record ToolHandlerMetadata(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> SupportedInputTypes,
    IReadOnlyList<ToolParameterDefinition> Parameters,
    object? ConfigurationSchema = null // NEW: JSON Schema for configuration
);
```

**Update each handler** to include schema in Metadata:

**Example - EntityExtractorHandler:**
```csharp
public ToolHandlerMetadata Metadata { get; } = new(
    Name: "Entity Extractor",
    Description: "Extracts structured entities from document text using AI",
    Version: "1.0.0",
    SupportedInputTypes: new[] { "text/plain", "application/pdf" },
    Parameters: new[]
    {
        new ToolParameterDefinition("entityTypes", "Types of entities to extract", ToolParameterType.Array, Required: true),
        new ToolParameterDefinition("confidenceThreshold", "Minimum confidence (0.0-1.0)", ToolParameterType.Decimal, Required: false, DefaultValue: 0.7)
    },
    ConfigurationSchema: new
    {
        schema = "http://json-schema.org/draft-07/schema#",
        type = "object",
        properties = new
        {
            entityTypes = new { type = "array", items = new { type = "string" }, minItems = 1 },
            confidenceThreshold = new { type = "number", minimum = 0.0, maximum = 1.0 }
        },
        required = new[] { "entityTypes" }
    }
);
```

**Acceptance Criteria:**
- All handlers include ConfigurationSchema in metadata
- Schema follows JSON Schema Draft 07 specification
- API returns schema in GET /api/ai/handlers response

---

### Phase 7: Testing and Validation

**Duration:** 3-4 days

**Objective:** Comprehensive testing across all scope types and handler resolution scenarios.

#### Test Suite 1: Scope Resolution (All Types)

**Test Cases:**

1. **Tool Resolution:**
   - ✅ Load tool with valid HandlerClass → Handler found
   - ✅ Load tool with invalid HandlerClass → Falls back to GenericAnalysisHandler
   - ✅ Load tool with null HandlerClass → Uses type-based lookup
   - ✅ Load tool with non-existent GUID → Returns null

2. **Skill Resolution:**
   - Load skill with valid GUID → Returns AnalysisSkill with PromptFragment
   - Load skill with non-existent GUID → Returns null
   - Verify PromptFragment applied in playbook execution

3. **Knowledge Resolution:**
   - Load Inline knowledge → Returns with Content populated
   - Load RAG knowledge → Returns with DeploymentId populated
   - Verify knowledge used in analysis context

4. **Action Resolution:**
   - Load action with valid GUID → Returns AnalysisAction with SystemPrompt
   - Load action with non-existent GUID → Returns null
   - Verify SortOrder extracted from type name

#### Test Suite 2: Handler Discovery API

**Test Cases:**

1. **API Response:**
   - GET /api/ai/handlers returns 200 OK
   - Response includes all registered handlers
   - Disabled handlers excluded
   - ConfigurationSchema included for each handler

2. **Caching:**
   - First request hits registry
   - Second request within 5 minutes returns cached
   - Cache expires after 5 minutes

3. **Authentication:**
   - Unauthenticated request returns 401
   - Authenticated request succeeds

#### Test Suite 3: End-to-End Playbook Execution

**Test Cases:**

1. **Document Profile Playbook:**
   - Create email via Outlook add-in
   - Verify UploadFinalizationWorker completes
   - Verify ProfileSummaryWorker executes analysis
   - Verify sprk_analysis record created
   - Verify tool results populated
   - Verify no dead-letter errors

2. **Custom Tool Playbook:**
   - Create playbook with custom tool (HandlerClass: GenericAnalysisHandler)
   - Configure tool with custom operation (e.g., "extract")
   - Execute analysis
   - Verify GenericAnalysisHandler executes
   - Verify structured output created

3. **Mixed Scopes Playbook:**
   - Create playbook with Action + Skills + Tools + Knowledge
   - Execute analysis
   - Verify all scopes loaded from Dataverse
   - Verify combined prompt constructed correctly
   - Verify analysis results include all scope contributions

---

### Phase 8: Deployment and Monitoring

**Duration:** 2 days

**Objective:** Safe deployment with monitoring and rollback plan.

#### Task 8.1: Deployment Strategy

**Environment Order:**
1. **Dev** (spe-api-dev-67e2xz)
2. **Staging** (after 2-3 days monitoring in dev)
3. **Production** (after 1 week monitoring in staging)

**Deployment Steps:**
1. Build and package changes
2. Run smoke tests locally
3. Deploy to dev environment
4. Run integration tests
5. Monitor for 24-48 hours
6. Promote to staging
7. Repeat monitoring
8. Promote to production

#### Task 8.2: Monitoring

**Metrics to Track:**

| Metric | Threshold | Action if Exceeded |
|--------|-----------|-------------------|
| Dead-letter queue messages | > 5/hour | Investigate, rollback if critical |
| Scope resolution failures | > 2% | Review logs, fix data issues |
| Handler not found warnings | > 10/hour | Check handler registration |
| API response time (GET /api/ai/handlers) | > 500ms | Verify cache working |
| Analysis success rate | < 95% | Investigate playbook configs |

**Log Monitoring:**

- Search for: `"[GET TOOL] Loading tool"` → Verify Dataverse queries working
- Search for: `"Available handlers:"` → Track fallback frequency
- Search for: `"Handler not found"` → Identify misconfigured tools
- Search for: `"Loaded tool from Dataverse"` → Confirm successful loads

#### Task 8.3: Rollback Plan

**Trigger Conditions:**
- Critical errors in scope resolution (> 10% failure rate)
- Performance degradation (> 2x latency increase)
- Data corruption or loss

**Rollback Steps:**
1. Revert API deployment to previous version
2. Verify previous version health
3. Notify team and stakeholders
4. Investigate root cause
5. Fix and re-deploy

---

## Risk Assessment

### High Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| Dataverse query performance | Analysis latency increases | Add caching (Redis), index optimization |
| Schema mismatch (field names) | Deserialization failures | Thorough testing with real Dataverse data, schema validation |
| Handler not found (production) | Tool execution failures | Fallback to GenericAnalysisHandler (already implemented) |

### Medium Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| API caching issues | Stale handler metadata | 5-minute TTL, manual cache invalidation endpoint |
| Migration timing (stub removal) | Breaking changes | Deploy Dataverse queries first, remove stubs later (phased approach) |
| Testing coverage gaps | Bugs in production | Comprehensive test suite (Phases 7) |

### Low Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| Documentation drift | User confusion | Update docs alongside code changes |
| Increased API calls | Cost increase | Caching strategy reduces calls by 95% |

---

## Success Criteria

### Functional

- ✅ All scopes (Tools, Skills, Knowledge, Actions) loaded from Dataverse
- ✅ No stub dictionaries remain in codebase
- ✅ GenericAnalysisHandler executes custom tools successfully
- ✅ Handler discovery API returns all registered handlers
- ✅ Dead-letter queue errors eliminated (< 1/day)

### Performance

- ✅ Scope resolution latency < 200ms (p95)
- ✅ GET /api/ai/handlers response < 100ms (cached)
- ✅ Analysis success rate > 98%
- ✅ No performance regression vs. stub dictionaries

### User Experience

- ✅ Users can add new tools in Dataverse and they work immediately
- ✅ Helpful error messages when handler not found (lists available handlers)
- ✅ PCF control (Phase 2) enables frontend validation
- ✅ Zero code deployment required for new scope configurations

---

## Timeline

| Phase | Duration | Dependencies | Deliverables |
|-------|----------|--------------|--------------|
| Phase 1 | 1 day | None (mostly done) | Tool resolution deployed and tested |
| Phase 2 | 2-3 days | Phase 1 | Skill resolution complete |
| Phase 3 | 2-3 days | Phase 1 | Knowledge resolution complete |
| Phase 4 | 2-3 days | Phase 1 | Action resolution complete |
| Phase 5 | 1 day | Phases 2-4 | Stub dictionaries removed |
| Phase 6 | 2-3 days | Phase 5 | Handler discovery API live |
| Phase 7 | 3-4 days | Phases 1-6 | All tests passing |
| Phase 8 | 2 days | Phase 7 | Production deployment |
| **Total** | **15-20 days** | | All scope types working, no stubs |

---

## Appendices

### Appendix A: Entity Schema Reference

**sprk_analysistool:**
- `sprk_analysistoolid` (Guid)
- `sprk_name` (String 200)
- `sprk_description` (Memo 2000)
- `sprk_tooltypeid` (Lookup → sprk_aitooltype)
- `sprk_handlerclass` (String 200)
- `sprk_configuration` (Memo 100K)

**sprk_promptfragment:**
- `sprk_promptfragmentid` (Guid)
- `sprk_name` (String 200)
- `sprk_description` (Memo 2000)
- `sprk_skilltypeid` (Lookup → sprk_aiskilltype)
- `sprk_promptfragment` (Memo 100K)

**sprk_systemprompt:**
- `sprk_systempromptid` (Guid)
- `sprk_name` (String 200)
- `sprk_description` (Memo 2000)
- `sprk_actiontypeid` (Lookup → sprk_analysisactiontype)
- `sprk_systemprompt` (Memo 100K)

**sprk_content:**
- `sprk_contentid` (Guid)
- `sprk_name` (String 200)
- `sprk_description` (Memo 2000)
- `sprk_knowledgetypeid` (Lookup → sprk_aiknowledgetype)
- `sprk_content` (Memo 100K)
- `sprk_deploymentid` (Guid, nullable)

### Appendix B: Handler Registration Code

**Current (works):**
```csharp
// ToolFrameworkExtensions.cs
services.AddToolHandlersFromAssembly(Assembly.GetExecutingAssembly());
```

**Verify GenericAnalysisHandler registered:**
```csharp
// Check in Program.cs or integration test
var registry = serviceProvider.GetRequiredService<IToolHandlerRegistry>();
var handlerIds = registry.GetRegisteredHandlerIds();

// Should include:
// - EntityExtractorHandler
// - GenericAnalysisHandler
// - SummaryHandler
// - ClauseAnalyzerHandler
// - DocumentClassifierHandler
// - RiskDetectorHandler
// - ClauseComparisonHandler
// - DateExtractorHandler
// - FinancialCalculatorHandler
```

### Appendix C: Example Log Output (Success)

```
[11:23:45 INF] [GET TOOL] Loading tool abc-123-real-guid from Dataverse
[11:23:45 INF] [GET TOOL] Loaded tool from Dataverse: Entity Extractor (Type: EntityExtractor, MappedFrom: HandlerClass, HandlerClass: EntityExtractorHandler)
[11:23:46 DBG] Executing tool 'Entity Extractor' (Type=EntityExtractor, HandlerClass=EntityExtractorHandler)
[11:23:50 INF] Tool execution complete for abc-def-analysis-id: EntityExtractor in 4234ms
[11:23:50 INF] Analysis completed successfully for document xyz-789-doc-id
```

### Appendix D: Example Log Output (Fallback)

```
[11:23:45 INF] [GET TOOL] Loading tool abc-123-real-guid from Dataverse
[11:23:45 INF] [GET TOOL] Loaded tool from Dataverse: Custom Risk Tool (Type: Custom, MappedFrom: HandlerClass, HandlerClass: CustomRiskHandler)
[11:23:46 DBG] Executing tool 'Custom Risk Tool' (Type=Custom, HandlerClass=CustomRiskHandler)
[11:23:46 WRN] Custom handler 'CustomRiskHandler' not found for tool 'Custom Risk Tool'. Available handlers: [EntityExtractorHandler, GenericAnalysisHandler, SummaryHandler, ClauseAnalyzerHandler, DocumentClassifierHandler, RiskDetectorHandler, ClauseComparisonHandler, DateExtractorHandler, FinancialCalculatorHandler]. Falling back to GenericAnalysisHandler.
[11:23:50 INF] Generic tool execution complete for abc-def-analysis-id: extract in 4123ms
[11:23:50 INF] Analysis completed successfully for document xyz-789-doc-id
```

---

**End of Scope Resolution Update Plan**
