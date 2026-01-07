# AI Summary and Analysis Enhancements

> **Status**: Research & Design
> **Created**: 2026-01-06
> **Related PR**: #102 (authorization fix)
> **Related Enhancement**: ENH-013

---

## Executive Summary

This project addresses two interconnected issues:

1. **Authorization Inconsistency**: The AI Summary feature was failing with 403 errors due to a different authorization approach than AI Analysis. A temporary workaround (Phase 1 scaffolding) was applied, but a proper fix is needed.

2. **Service Duplication**: Two separate AI services exist (`DocumentIntelligenceService` and `AnalysisOrchestrationService`) with overlapping functionality, creating code duplication and maintenance burden.

**Goal**: Unify AI Summary and AI Analysis into a single orchestration service, with proper authorization, using Playbook/Output scopes for flexible output configuration.

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

**Concept**: Summary is just a "simple playbook" with auto-persist to Document record.

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
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌───────────────┐  │  │
│  │  │ Simple Mode     │  │ Standard Mode   │  │ Playbook Mode │  │  │
│  │  │ (Auto-Summary)  │  │ (Interactive)   │  │ (Configured)  │  │  │
│  │  ├─────────────────┤  ├─────────────────┤  ├───────────────┤  │  │
│  │  │ • No chat       │  │ • Chat enabled  │  │ • Full config │  │  │
│  │  │ • Fixed output  │  │ • Save/export   │  │ • Tools       │  │  │
│  │  │ • Auto-persist  │  │ • User-driven   │  │ • Skills      │  │  │
│  │  │   to Document   │  │   persist       │  │ • Knowledge   │  │  │
│  │  └────────┬────────┘  └────────┬────────┘  └───────┬───────┘  │  │
│  │           │                    │                    │          │  │
│  │           └────────────────────┼────────────────────┘          │  │
│  │                                │                               │  │
│  │                    ┌───────────▼───────────┐                   │  │
│  │                    │   Shared Components   │                   │  │
│  │                    ├───────────────────────┤                   │  │
│  │                    │ • ITextExtractor      │                   │  │
│  │                    │ • IOpenAiClient       │                   │  │
│  │                    │ • SSE Streaming       │                   │  │
│  │                    │ • Entity Extraction   │                   │  │
│  │                    │ • UAC Authorization   │                   │  │
│  │                    └───────────────────────┘                   │  │
│  │                                                                │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Summary as Simple Playbook

**Key Insight**: Summary outputs (TL;DR, Keywords, Entities, Summary) can be configured as Output Scopes on a playbook.

**"Auto-Summary" Playbook Configuration**:

```yaml
Name: Auto-Summary
Trigger: Automatic (on document upload)
Mode: Simple (no chat, auto-persist)

Output Scopes:
  - TlDr:
      Type: text[]
      MaxItems: 3
      PersistTo: sprk_document.sprk_tldr
  - Summary:
      Type: text
      MaxLength: 2000
      PersistTo: sprk_document.sprk_summary
  - Keywords:
      Type: text
      Format: comma-separated
      PersistTo: sprk_document.sprk_keywords
  - Entities:
      Type: structured
      Schema: { organizations: [], people: [], dates: [], monetaryAmounts: [] }
      PersistTo: sprk_document.sprk_entities (JSON)

Prompt: (existing StructuredAnalysisPromptTemplate)
```

**Benefits**:
1. **Configurable outputs** - Add new output types without code changes
2. **Flexible persistence** - Choose which fields to update
3. **Consistent execution** - Same pipeline as interactive analysis
4. **Unified authorization** - Single authorization service

### 2.3 Authorization Model

**Streaming (User Context)**:
```
User Request → OBO Token → Graph API → SPE Access
                   ↓
              User's own permissions enforced by MSAL/Graph
```
*No additional UAC needed - user can only access what Graph allows.*

**Background Jobs (App-Only)**:
```
Job Enqueue → Capture user context → Validate UAC → Store auth context
                                          ↓
                                     User has access?
                                          │
                              ┌───────────┴───────────┐
                              ↓                       ↓
                         Yes: Proceed            No: Reject
                              ↓
                         Job Execute → App Token → Graph API
                                                      ↓
                                               Access as app
```
*UAC check at enqueue time prevents unauthorized background processing.*

**Unified Authorization Service**:

```csharp
public interface IAiAuthorizationService
{
    /// <summary>
    /// Authorize AI operation for current user context.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        IEnumerable<Guid> documentIds,
        AuthorizationMode mode,
        CancellationToken cancellationToken);
}

public enum AuthorizationMode
{
    /// <summary>Phase 1: Skip checks, rely on Dataverse security.</summary>
    SkipAuth,

    /// <summary>Check Dataverse RetrievePrincipalAccess for each document.</summary>
    DataverseAccess,

    /// <summary>Full UAC check with retry for eventual consistency.</summary>
    FullUac
}
```

**Timing Issue Fix** (for Full UAC mode):

```csharp
// Option 1: Retry with exponential backoff
var policy = Policy
    .Handle<DocumentNotFoundException>()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

// Option 2: Delay before first check
await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
var result = await CheckAccessAsync(documentId);
```

---

## Part 3: Implementation Plan

### Phase 2.1: Unify Authorization

**Scope**: Create unified authorization service used by both endpoints.

| Task | Description |
|------|-------------|
| Create `IAiAuthorizationService` | Interface with configurable modes |
| Create `AiAuthorizationService` | Implementation with SkipAuth, DataverseAccess, FullUac modes |
| Update `AnalysisAuthorizationFilter` | Use `IAiAuthorizationService` |
| Update `AiAuthorizationFilter` | Use `IAiAuthorizationService` |
| Add timing fix | Retry logic for newly-created documents |
| Add tests | Unit tests for all authorization modes |

### Phase 2.2: Add Simple Mode to Analysis

**Scope**: Enable `AnalysisOrchestrationService` to handle summary-style requests.

| Task | Description |
|------|-------------|
| Add `SimpleMode` property to `AnalysisExecuteRequest` | Flag for auto-persist behavior |
| Create "Auto-Summary" playbook seed data | Dataverse record with output scopes |
| Implement output scope persistence | Update `sprk_document` fields based on scope config |
| Add endpoint for simple mode | `/api/ai/analysis/simple` or use existing with flag |
| Add tests | Integration tests for simple mode flow |

### Phase 2.3: Migrate AI Summary Endpoint

**Scope**: Route existing endpoint to unified service.

| Task | Description |
|------|-------------|
| Keep `/api/ai/document-intelligence/analyze` | Backward compatibility |
| Route internally to `AnalysisOrchestrationService` | With `SimpleMode = true` |
| Update PCF control | Use unified endpoint (optional) |
| Deprecate `DocumentIntelligenceService` | Add `[Obsolete]` attribute |
| Add tests | Verify backward compatibility |

### Phase 2.4: Cleanup

**Scope**: Remove deprecated code.

| Task | Description |
|------|-------------|
| Remove `IDocumentIntelligenceService` | Interface no longer needed |
| Remove `DocumentIntelligenceService` | Implementation no longer needed |
| Remove `AiAuthorizationFilter` | Use unified filter |
| Update DI registrations | Remove old services |
| Update documentation | Reflect new architecture |

---

## Part 4: Output Scopes Design

### 4.1 Schema

```csharp
/// <summary>
/// Output scope configuration for analysis results.
/// Defines what output to generate and where to persist it.
/// </summary>
public record OutputScope
{
    /// <summary>Unique identifier for this scope.</summary>
    public required Guid Id { get; init; }

    /// <summary>Display name (e.g., "TL;DR", "Keywords").</summary>
    public required string Name { get; init; }

    /// <summary>Output type: text, text[], structured.</summary>
    public required OutputType Type { get; init; }

    /// <summary>For text arrays, max items.</summary>
    public int? MaxItems { get; init; }

    /// <summary>For text, max characters.</summary>
    public int? MaxLength { get; init; }

    /// <summary>For structured, JSON schema.</summary>
    public string? Schema { get; init; }

    /// <summary>Target field for persistence (e.g., "sprk_document.sprk_summary").</summary>
    public string? PersistTo { get; init; }

    /// <summary>Prompt fragment for this output.</summary>
    public string? PromptFragment { get; init; }
}

public enum OutputType
{
    Text,
    TextArray,
    Structured
}
```

### 4.2 Dataverse Model

**Existing entities to modify**:
- `sprk_playbook` - Add relationship to Output Scopes

**New entity**:
- `sprk_outputscope` - Output scope definitions

### 4.3 Default Output Scopes (Seed Data)

| Name | Type | MaxItems | PersistTo |
|------|------|----------|-----------|
| TL;DR | TextArray | 3 | sprk_document.sprk_tldr |
| Summary | Text | 2000 | sprk_document.sprk_summary |
| Keywords | Text | - | sprk_document.sprk_keywords |
| Document Type | Text | - | sprk_document.sprk_documenttype |
| Entities | Structured | - | sprk_document.sprk_entities |

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
- `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisExecuteRequest.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`

### New
- `src/server/api/Sprk.Bff.Api/Services/Ai/IAiAuthorizationService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs`
- `src/server/api/Sprk.Bff.Api/Models/Ai/OutputScope.cs`

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

## Next Steps

1. **Review this spec** with stakeholder
2. **Create project artifacts** via `/project-pipeline`
3. **Generate task files** for each phase
4. **Begin Phase 2.1** (Unify Authorization)
