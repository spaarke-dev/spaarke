# AI Summary and Analysis Enhancements

> **Status**: Ready for Implementation
> **Created**: 2026-01-06
> **Updated**: 2026-01-06 (Owner clarifications added)
> **Related PR**: #102 (authorization fix)
> **Related Enhancement**: ENH-013

---

## Executive Summary

This project addresses two interconnected issues:

1. **Authorization Inconsistency**: The AI Summary feature was failing with 403 errors due to a different authorization approach than AI Analysis. A temporary workaround (Phase 1 scaffolding) was applied, but a proper fix is needed.

2. **Service Duplication**: Two separate AI services exist (`DocumentIntelligenceService` and `AnalysisOrchestrationService`) with overlapping functionality, creating code duplication and maintenance burden.

**Goal**: Unify AI Summary (now called **Document Profile**) and AI Analysis into a single orchestration service, with proper FullUAC authorization, using the existing Playbook/Output scope entities for flexible output configuration.

**Key Insight**: Document Profile is NOT a special case—it's just another Playbook execution with a different trigger point (auto on upload) and UI context (File Upload PCF Tab 2).

---

## Part 1: Problem Analysis

### 1.1 Authorization Issue (Root Cause)

**Symptom**: 403 Forbidden error (`sdap.access.deny.insufficient_rights`) on AI Summary during document upload.

**Timeline**:
1. User uploads file → SPE upload succeeds ✅
2. Dataverse `sprk_document` record created ✅
3. AI Summary API called → **403 error** ❌

**Root Cause Discovery**:

| Filter | Endpoint | Behavior |
|--------|----------|----------|
| `AnalysisAuthorizationFilter` | `/api/ai/analysis/*` | **Phase 1 scaffolding** - skips UAC, relies on Dataverse |
| `AiAuthorizationFilter` | `/api/ai/document-intelligence/*` | **Full UAC check** - calls `RetrievePrincipalAccess` |

The `AiAuthorizationFilter` was calling `RetrievePrincipalAccess` for a just-created document, which failed because:

1. **Replication lag**: Dataverse record not fully propagated (~1-2 seconds)
2. **404 response**: `sprk_documents({id})` not found
3. **Empty permissions**: Returns `AccessRights.None`
4. **Denied**: `OperationAccessRule` denies due to insufficient rights

**Current Workaround** (PR #102):
Updated `AiAuthorizationFilter` to skip UAC check (match `AnalysisAuthorizationFilter`).

```csharp
// Phase 1 Scaffolding: Skip SPE/UAC authorization, rely on Dataverse security.
_logger?.LogDebug("AI document-intelligence authorization: User {UserId} accessing {Count} document(s) (Phase 1: skipping UAC check)", userId, documentIds.Count);
return await next(context);
```

**Why This Is Insufficient**:
- OBO flow protects streaming endpoints (user context enforces SPE access)
- **Background jobs use app-only auth** - bypasses OBO, no user permissions enforced
- If summary ever moves to background processing, security gap opens

### 1.2 Service Duplication Issue

**Current Architecture**:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AI Feature Architecture                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─────────────────────┐          ┌──────────────────────────────┐  │
│  │    AI Summary       │          │      AI Analysis             │  │
│  │  (Document Intel)   │          │   (Analysis Orchestration)   │  │
│  ├─────────────────────┤          ├──────────────────────────────┤  │
│  │ Endpoint:           │          │ Endpoint:                    │  │
│  │ /api/ai/document-   │          │ /api/ai/analysis/execute     │  │
│  │ intelligence/analyze│          │                              │  │
│  ├─────────────────────┤          ├──────────────────────────────┤  │
│  │ Service:            │          │ Service:                     │  │
│  │ DocumentIntelligence│          │ AnalysisOrchestration        │  │
│  │ Service             │          │ Service                      │  │
│  ├─────────────────────┤          ├──────────────────────────────┤  │
│  │ Filter:             │          │ Filter:                      │  │
│  │ AiAuthorizationFilt │          │ AnalysisAuthorization        │  │
│  │ er                  │          │ Filter                       │  │
│  ├─────────────────────┤          ├──────────────────────────────┤  │
│  │ Features:           │          │ Features:                    │  │
│  │ • Summarize         │          │ • Actions & Playbooks        │  │
│  │ • Extract entities  │          │ • Skills, Knowledge, Tools   │  │
│  │ • Keywords          │          │ • Chat continuation          │  │
│  │ • Document type     │          │ • Save working doc           │  │
│  │                     │          │ • Export (Email, Teams, PDF) │  │
│  ├─────────────────────┤          ├──────────────────────────────┤  │
│  │ Storage:            │          │ Storage:                     │  │
│  │ Updates             │          │ Creates sprk_analysis        │  │
│  │ sprk_document       │          │ records                      │  │
│  │ fields              │          │                              │  │
│  └─────────────────────┘          └──────────────────────────────┘  │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Why Two Services Exist** (Historical):

| Aspect | AI Summary | AI Analysis | Reason |
|--------|------------|-------------|--------|
| **Trigger** | Automatic (after upload) | User-initiated | Different UX |
| **Interaction** | Fire-and-forget | Conversational | Different complexity |
| **Output** | Fixed fields on Document | Flexible working doc | Different data models |
| **Performance** | Must be fast | Can be slower | Different SLAs |
| **History** | Evolved first | Added later | Historical accident |

**Problems with Current Architecture**:

1. **Code duplication**: Both services call OpenAI, extract text, stream SSE
2. **Inconsistent authorization**: Different filters with different behaviors
3. **Inconsistent configuration**: Different endpoints, different options
4. **Maintenance burden**: Bug fixes need to be applied in two places
5. **Testing overhead**: Two sets of tests for similar functionality

---

## Part 2: Proposed Solution

### 2.1 Unified Architecture

**Concept**: Document Profile is just another Playbook execution—same underlying system as Analysis Builder.

```
┌─────────────────────────────────────────────────────────────────────┐
│                   Unified AI Architecture (Phase 2)                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │              AnalysisOrchestrationService                     │  │
│  │  (Unified AI execution engine)                                │  │
│  ├───────────────────────────────────────────────────────────────┤  │
│  │                                                                │  │
│  │  ┌─────────────────────────┐  ┌─────────────────────────────┐ │  │
│  │  │   Document Profile      │  │   Analysis Workspace        │ │  │
│  │  │   (Auto on Upload)      │  │   (User-Initiated)          │ │  │
│  │  ├─────────────────────────┤  ├─────────────────────────────┤ │  │
│  │  │ • Auto-trigger          │  │ • User clicks "Analyze"     │ │  │
│  │  │ • Tab 2 in File Upload  │  │ • Full workspace UI         │ │  │
│  │  │ • Code refs playbook    │  │ • User picks playbook       │ │  │
│  │  │   by name               │  │ • Chat continuation         │ │  │
│  │  │ • Dual storage:         │  │ • Save/export options       │ │  │
│  │  │   analysisoutput +      │  │                             │ │  │
│  │  │   document fields       │  │                             │ │  │
│  │  └───────────┬─────────────┘  └──────────────┬──────────────┘ │  │
│  │              │                               │                │  │
│  │              └───────────────┬───────────────┘                │  │
│  │                              │                                │  │
│  │                  ┌───────────▼───────────┐                    │  │
│  │                  │   Shared Components   │                    │  │
│  │                  ├───────────────────────┤                    │  │
│  │                  │ • ITextExtractor      │                    │  │
│  │                  │ • IOpenAiClient       │                    │  │
│  │                  │ • SSE Streaming       │                    │  │
│  │                  │ • Entity Extraction   │                    │  │
│  │                  │ • FullUAC Auth        │                    │  │
│  │                  │ • Storage w/ Retry    │                    │  │
│  │                  └───────────────────────┘                    │  │
│  │                                                               │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Document Profile as Standard Playbook

**Key Insight**: Document Profile is just another Playbook/Analysis—the same underlying system as Analysis Builder → Analysis Workspace. The only differences are:

| Aspect | Analysis Builder | Document Profile |
|--------|------------------|------------------|
| **Trigger** | User clicks "Analyze" | Auto on file upload |
| **Playbook Selection** | User picks in UI | Code references "Document Profile" playbook by name |
| **UI Context** | Analysis Workspace (full) | File Upload PCF - Tab 2 |
| **Output Storage** | `sprk_analysis` + `sprk_analysisoutput` | Same + ALSO maps to `sprk_document` fields |

**"Document Profile" Playbook** (configurable in Dataverse UI):

```yaml
Name: Document Profile
Description: Auto-generated document summary on upload
Is Public: true

Output Types (via sprk_aioutputtype):
  - TL;DR (maps to sprk_document.sprk_tldr)
  - Summary (maps to sprk_document.sprk_summary)
  - Keywords (maps to sprk_document.sprk_keywords)
  - Document Type (maps to sprk_document.sprk_documenttype)
  - Entities (JSON - maps to sprk_document.sprk_entities)
```

**Code Reference Pattern**:
```csharp
// Code looks up playbook by name - configurable in UI
var playbook = await _playbookService.GetByNameAsync("Document Profile", ct);
```

**Benefits**:
1. **Configurable outputs** - Modify playbook and output types in Dataverse UI
2. **No special code path** - Uses same `AnalysisOrchestrationService` pipeline
3. **Dual storage** - Outputs in `sprk_analysisoutput` AND mapped to `sprk_document` fields
4. **Unified authorization** - Single FullUAC authorization service

### 2.3 Authorization Model

**Critical Clarification**: The AI process runs on SPE file content—it does NOT require Document ID. Document ID is only needed for **storage** (creating `sprk_analysis`, updating `sprk_document`).

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AI Execution Flow                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. SPE File Content (via OBO)    ← AI runs here, no Document ID   │
│           ↓                                                         │
│  2. AI Pipeline (OpenAI)          ← Generates outputs              │
│           ↓                                                         │
│  3. Storage (needs Document ID)   ← UAC + retry logic here         │
│      • Create sprk_analysis                                         │
│      • Create sprk_analysisoutput                                   │
│      • Update sprk_document fields (Document Profile only)          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Authorization Strategy**: Use **FullUAC** mode (security requirement). UAC is also needed for non-Dataverse contexts (Office.js add-ins, web applications).

**Unified Authorization Service**:

```csharp
public interface IAiAuthorizationService
{
    /// <summary>
    /// Authorize AI operation for current user context.
    /// Uses FullUAC mode with retry for eventual consistency.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        IEnumerable<Guid> documentIds,
        CancellationToken cancellationToken);
}
```

**Timing Issue Fix** (applies to storage step only):

The retry logic handles Dataverse replication lag for newly-created documents:

```csharp
// Retry with exponential backoff for storage operations
var policy = Policy
    .Handle<DocumentNotFoundException>()
    .Or<DataverseException>(ex => ex.StatusCode == 404)
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

await policy.ExecuteAsync(async () =>
{
    // UAC check + storage to Dataverse
    await StoreAnalysisResultsAsync(documentId, outputs, ct);
});
```

**Note**: If performance issues arise with FullUAC, revisit authorization mode selection.

---

## Part 3: Implementation Plan

### Phase 2.1: Unify Authorization

**Scope**: Create unified authorization service with FullUAC mode.

| Task | Description |
|------|-------------|
| Create `IAiAuthorizationService` | Interface for unified authorization |
| Create `AiAuthorizationService` | Implementation with FullUAC mode |
| Update `AnalysisAuthorizationFilter` | Use `IAiAuthorizationService` |
| Update `AiAuthorizationFilter` | Use `IAiAuthorizationService` |
| Add storage retry logic | Exponential backoff for Dataverse operations |
| Add tests | Unit tests for authorization and retry |

### Phase 2.2: Add Document Profile Playbook Support

**Scope**: Enable `AnalysisOrchestrationService` to handle Document Profile execution.

| Task | Description |
|------|-------------|
| Create "Document Profile" playbook seed data | Dataverse record with output types |
| Implement playbook lookup by name | `GetByNameAsync("Document Profile")` |
| Implement dual storage | `sprk_analysisoutput` + `sprk_document` field mapping |
| Add failure handling | Retry 3x with exponential backoff, then soft failure |
| Add tests | Integration tests for Document Profile flow |

### Phase 2.3: Migrate AI Summary Endpoint

**Scope**: Route existing endpoint to unified service.

| Task | Description |
|------|-------------|
| Keep `/api/ai/document-intelligence/analyze` | Backward compatibility |
| Route internally to `AnalysisOrchestrationService` | With playbook="Document Profile" |
| Update PCF control | Use unified endpoint (optional) |
| Deprecate `DocumentIntelligenceService` | Add `[Obsolete]` attribute |
| Add tests | Verify backward compatibility |

### Phase 2.4: Cleanup (Immediately After Deployment)

**Scope**: Remove deprecated code immediately after full solution is deployed and working.

| Task | Description |
|------|-------------|
| Remove `IDocumentIntelligenceService` | Interface no longer needed |
| Remove `DocumentIntelligenceService` | Implementation no longer needed |
| Remove `AiAuthorizationFilter` | Use unified filter |
| Update DI registrations | Remove old services |
| Update documentation | Reflect new architecture |

**Timing**: Cleanup happens immediately after Phase 2.3 deployment is verified working—no waiting period.

---

## Part 4: Output Types Design (Using Existing Entities)

### 4.1 Existing Dataverse Schema

**Use existing entities** (no new entities needed):

| Entity | Logical Name | Purpose |
|--------|--------------|---------|
| Analysis Playbook | `sprk_analysisplaybook` | Playbook definitions (incl. "Document Profile") |
| AI Output Type | `sprk_aioutputtype` | Output type definitions (TL;DR, Summary, etc.) |
| Analysis Output | `sprk_analysisoutput` | Actual output values per analysis |
| Analysis | `sprk_analysis` | Analysis session record |
| Document | `sprk_document` | Document record (target for field mapping) |

### 4.2 Entity Relationships

```
sprk_analysisplaybook (e.g., "Document Profile")
       │
       │ 1:N
       ▼
sprk_aioutputtype (e.g., "TL;DR", "Summary", "Keywords")
       │
       │ (referenced by)
       ▼
sprk_analysisoutput (actual values per execution)
       │
       │ N:1
       ▼
sprk_analysis (execution session)
       │
       │ N:1
       ▼
sprk_document (source document, also receives field mapping)
```

### 4.3 Document Profile Output Types (Seed Data)

Configure via Dataverse UI on the "Document Profile" playbook:

| Output Type Name | Data Type | Maps to Document Field |
|------------------|-----------|------------------------|
| TL;DR | Text (multi-line) | `sprk_document.sprk_tldr` |
| Summary | Text (multi-line) | `sprk_document.sprk_summary` |
| Keywords | Text | `sprk_document.sprk_keywords` |
| Document Type | Text | `sprk_document.sprk_documenttype` |
| Entities | Text (JSON) | `sprk_document.sprk_entities` |

### 4.4 Field Mapping Logic

For Document Profile playbook only, output values are stored in TWO places:
1. **Standard**: `sprk_analysisoutput` records (linked to `sprk_analysis`)
2. **Additional**: Mapped to `sprk_document` fields (for quick access in Document views)

```csharp
// After AI execution completes
foreach (var output in analysisOutputs)
{
    // 1. Standard storage
    await CreateAnalysisOutputAsync(analysisId, output);

    // 2. If Document Profile playbook, also map to document fields
    if (playbook.Name == "Document Profile" && output.Type.FieldMapping != null)
    {
        await UpdateDocumentFieldAsync(documentId, output.Type.FieldMapping, output.Value);
    }
}
```

### 4.5 Failure Handling

**Retry Strategy** (for storage operations only):

```csharp
// Retry 3x with exponential backoff for Dataverse storage
var policy = Policy
    .Handle<DataverseException>(ex => ex.StatusCode == 404 || ex.StatusCode == 503)
    .Or<DocumentNotFoundException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (ex, delay, attempt, ctx) =>
        {
            _logger.LogWarning("Storage retry {Attempt}/3 after {Delay}s: {Error}",
                attempt, delay.TotalSeconds, ex.Message);
        });

await policy.ExecuteAsync(async () =>
{
    await StoreAnalysisResultsAsync(documentId, outputs, ct);
});
```

**Soft Failure** (after 3 retries):

If storage fails after 3 retries, the AI outputs are **NOT lost**:
1. Outputs are already stored in `sprk_analysisoutput` (linked to `sprk_analysis`)
2. Only the field mapping to `sprk_document` may have failed
3. User sees: **"Document Profile completed. Some fields could not be updated. View full results in the Analysis tab."**
4. User can still access outputs via Analysis Workspace

```csharp
// Soft failure handling
catch (Exception ex) when (retriesExhausted)
{
    _logger.LogWarning("Storage failed after retries. Analysis {AnalysisId} outputs preserved.", analysisId);

    // Return partial success
    return new DocumentProfileResult
    {
        Success = true,  // AI completed successfully
        PartialStorage = true,
        Message = "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab.",
        AnalysisId = analysisId  // User can navigate to full results
    };
}
```

---

## Part 5: Success Criteria

| Criteria | Metric |
|----------|--------|
| Single authorization implementation | 1 service class |
| Single AI orchestration service | 1 service (no DocumentIntelligenceService) |
| Backward compatibility | Existing PCF works unchanged |
| Performance maintained | Summary < 5s end-to-end |
| Configurable outputs | Via Playbook/Output Scopes |
| Test coverage | ≥80% on unified service |
| Documentation updated | ADR-013 reflects new architecture |

---

## Part 6: Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Breaking existing PCF | High | Keep endpoint, route internally |
| Performance regression | Medium | Profile before/after, optimize if needed |
| Timing issue not fully resolved | Medium | Add retry + delay as fallback |
| Scope creep | Medium | Strict phase boundaries |

---

## Part 7: Files Affected

### Modified
- `src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs`
- `src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs`
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`

### New
- `src/server/api/Sprk.Bff.Api/Services/Ai/IAiAuthorizationService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs`
- `src/server/api/Sprk.Bff.Api/Models/Ai/DocumentProfileResult.cs`

### Deleted (Phase 2.4)
- `src/server/api/Sprk.Bff.Api/Services/Ai/IDocumentIntelligenceService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentIntelligenceService.cs`
- `src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs`

---

## Part 8: Related Resources

- [ENH-013: AI Authorization Fix and Service Unification](../../docs/enhancements/ENH-013-ai-authorization-and-service-unification.md)
- [UAC Architecture](../../docs/architecture/uac-access-control.md)
- [ADR-013: AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)
- [PR #102: Authorization fix](https://github.com/spaarke-dev/spaarke/pull/102)

---

## Part 9: Owner Clarifications (2026-01-06)

The following decisions were made during spec review with the product owner:

### Q1: Terminology
**Decision**: Rename "Auto-Summary" to **"Document Profile Playbook"** for consistency with Playbook/Analysis terminology.

### Q2: Authorization Mode
**Decision**: Use **FullUAC** mode (security requirement). UAC is needed for:
- Security enforcement on AI operations
- Non-Dataverse contexts (Office.js add-ins, web applications)
- Future background job security

**Note**: If performance issues arise, revisit authorization mode selection.

### Q3: Timing Fix Scope
**Decision**: Retry logic applies to **storage operations only**, not AI execution:
- AI runs on SPE file content (no Document ID needed)
- Document ID only needed for storage step
- Exponential backoff: 2s → 4s → 8s (3 retries)

### Q4: Failure Behavior
**Decision**: After 3 retries, use **soft failure**:
- AI outputs are preserved in `sprk_analysisoutput`
- User sees: "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab."
- User can access outputs via Analysis Workspace

### Q5: Playbook Reference
**Decision**: Code references "Document Profile" playbook **by name**:
- Lookup: `GetByNameAsync("Document Profile")`
- Configurable in Dataverse UI (can change output types, prompts)
- No hard-coded playbook IDs

### Q6: Existing Entities
**Decision**: Use **existing entities** (no new schema):
- `sprk_analysisplaybook` - Playbook definitions
- `sprk_aioutputtype` - Output type definitions
- `sprk_analysisoutput` - Output values

### Q7: Cleanup Timing
**Decision**: Cleanup happens **immediately** after deployment is verified working:
- No waiting period or deprecation window
- Remove deprecated services as soon as unified solution is deployed and tested

### Key Architectural Insight
**Document Profile is NOT a special case**—it's just another Playbook execution with:
- Different trigger point (auto on upload vs. user-initiated)
- Different UI context (File Upload PCF Tab 2 vs. Analysis Workspace)
- Additional storage (also maps to `sprk_document` fields)

The AI process runs on SPE file content and does not require Document ID. Document ID is only needed for storage operations.

---

## Next Steps

1. ~~**Review this spec** with stakeholder~~ ✅ Done (2026-01-06)
2. **Create project artifacts** via `/project-pipeline`
3. **Generate task files** for each phase
4. **Begin Phase 2.1** (Unify Authorization)
