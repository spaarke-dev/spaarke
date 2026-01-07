# ENH-013: AI Authorization Fix and Service Unification

> **Status**: Analysis Complete
> **Priority**: Medium
> **Created**: 2026-01-06
> **Related PR**: #102

---

## Executive Summary

The AI Summary feature on document upload was failing with `403 Forbidden` errors (`sdap.access.deny.insufficient_rights`). Investigation revealed an architectural inconsistency between two AI services that should use the same authorization approach. This document analyzes the root cause, documents the fix, and proposes a plan to unify the AI services.

---

## Part 1: Issue Analysis

### Symptom

When uploading documents via the `UniversalDocumentUpload` PCF control, the AI Summary feature returned:

```json
{
  "status": 403,
  "title": "Forbidden",
  "extensions": {
    "reasonCode": "sdap.access.deny.insufficient_rights"
  }
}
```

### Timeline

1. User uploads file → SPE upload succeeds ✅
2. Dataverse `sprk_document` record created ✅
3. AI Summary API called with `documentId` → **403 error** ❌

### Root Cause

**Two AI authorization filters with different behaviors:**

| Filter | Endpoint | Behavior |
|--------|----------|----------|
| `AnalysisAuthorizationFilter` | `/api/ai/analysis/*` | **Phase 1 scaffolding** - skips UAC, relies on Dataverse |
| `AiAuthorizationFilter` | `/api/ai/document-intelligence/*` | **Full UAC check** - calls `RetrievePrincipalAccess` |

The `AiAuthorizationFilter` was calling `RetrievePrincipalAccess` for a just-created document, which failed because:

1. **Replication lag**: Dataverse record not fully propagated
2. **404 response**: `sprk_documents({id})` not found
3. **Empty permissions**: Returns `AccessRights.None`
4. **Denied**: `OperationAccessRule` denies due to insufficient rights

### Evidence

The `DataverseAccessDataSource.QueryUserPermissionsAsync()` at lines 223-250 silently converts 404/403 responses to empty permissions:

```csharp
if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    // Returns empty list → AccessRights.None → Denied
    return new List<PermissionRecord>();
}
```

### Fix Applied (PR #102)

Updated `AiAuthorizationFilter` to match `AnalysisAuthorizationFilter` behavior:

```csharp
// Phase 1 Scaffolding: Skip SPE/UAC authorization, rely on Dataverse security.
// This matches AnalysisAuthorizationFilter behavior for consistency.
_logger?.LogDebug("AI document-intelligence authorization: User {UserId} accessing {Count} document(s) (Phase 1: skipping UAC check)", userId, documentIds.Count);

return await next(context);
```

---

## Part 2: Diagnostic Logging Added

To understand the exact failure mode for Phase 2 implementation, diagnostic logging was added to `DataverseAccessDataSource`:

| Log Tag | Level | When |
|---------|-------|------|
| `[UAC-DIAG] GetUserAccessAsync START` | Info | Entry point |
| `[UAC-DIAG] RetrievePrincipalAccess` | Info | Before Dataverse call |
| `[UAC-DIAG] RetrievePrincipalAccess FAILED` | Warning | On 403/404/error |
| `[UAC-DIAG] Access denied` | Warning | With specific failure reason |
| `[UAC-DIAG] RetrievePrincipalAccess SUCCESS` | Info | With rights returned |

**To test**: Re-enable UAC check in `AiAuthorizationFilter` and upload a document. Check App Insights for `[UAC-DIAG]` logs.

---

## Part 3: Architectural Analysis

### Current State: Two Separate AI Services

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

### Why Two Services?

| Aspect | AI Summary | AI Analysis | Reason for Separation |
|--------|------------|-------------|----------------------|
| **Trigger** | Automatic (after upload) | User-initiated | Different UX patterns |
| **Interaction** | Fire-and-forget | Conversational | Different complexity |
| **Output** | Fixed fields on Document | Flexible working doc | Different data models |
| **Performance** | Must be fast | Can be slower | Different SLAs |
| **History** | Evolved first | Added later | Historical accident |

### Problems with Current Architecture

1. **Code duplication**: Both services call OpenAI, extract text, stream SSE
2. **Inconsistent authorization**: Different filters with different behaviors
3. **Inconsistent configuration**: Different endpoints, different options
4. **Maintenance burden**: Bug fixes need to be applied in two places
5. **Testing overhead**: Two sets of tests for similar functionality

---

## Part 4: Unification Plan

### Proposed Architecture: Summary as a Simple Playbook

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
│  │  │ to Document     │  │   persist       │  │ • Knowledge   │  │  │
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

### Migration Steps

#### Phase 2.1: Unify Authorization (2-3 days)

1. Create `AiAuthorizationService` with configurable modes:
   - `SkipAuth` (Phase 1 scaffolding)
   - `DataverseOnly` (check Dataverse access)
   - `FullUAC` (RetrievePrincipalAccess)

2. Both filters use the same service with configuration

3. Fix timing issue (options):
   - Add retry with backoff
   - Add delay after document creation
   - Use eventual consistency pattern

#### Phase 2.2: Unify Services (1 week)

1. Add "Simple Mode" to `AnalysisOrchestrationService`:
   ```csharp
   public record AnalysisExecuteRequest
   {
       // Existing fields...

       /// <summary>
       /// When true, uses simplified flow: no chat, auto-persist to Document record.
       /// </summary>
       public bool SimpleMode { get; init; } = false;
   }
   ```

2. Create "Auto-Summary" playbook in Dataverse:
   - Action: Summarize
   - Output: Summary, TL;DR, Keywords, Entities
   - Persist: Auto-update sprk_document fields

3. Deprecate `DocumentIntelligenceService`:
   - Add `[Obsolete]` attribute
   - Redirect calls to `AnalysisOrchestrationService` with SimpleMode

#### Phase 2.3: Migrate Endpoints (2-3 days)

1. Keep `/api/ai/document-intelligence/analyze` for backward compatibility
2. Internally route to `AnalysisOrchestrationService.ExecuteAnalysisAsync(request with { SimpleMode = true })`
3. Update PCF to use unified endpoint (optional)

#### Phase 2.4: Cleanup (1 day)

1. Remove `DocumentIntelligenceService` and `IDocumentIntelligenceService`
2. Remove `AiAuthorizationFilter` (use unified filter)
3. Update documentation

### Success Criteria

| Criteria | Metric |
|----------|--------|
| Single authorization implementation | 1 filter class |
| Single AI service | 1 orchestration service |
| Backward compatibility | Existing PCF works unchanged |
| Performance maintained | Summary < 5s |
| Test coverage | ≥80% on unified service |

---

## Part 5: Immediate Actions

### Already Done (PR #102)

- [x] Fix 403 error by aligning authorization filters
- [x] Add diagnostic logging for UAC debugging
- [x] Remove legacy authorization rules
- [x] Create UAC architecture documentation

### Next Steps

1. **Monitor diagnostics**: Watch `[UAC-DIAG]` logs in App Insights
2. **Create Phase 2 tasks**: If approved, create tasks for unification
3. **Test timing hypothesis**: Re-enable UAC with delay to confirm root cause

---

## Appendix A: Files Changed (PR #102)

| File | Change |
|------|--------|
| `AiAuthorizationFilter.cs` | Skip UAC check (Phase 1 scaffolding) |
| `DataverseAccessDataSource.cs` | Add `[UAC-DIAG]` diagnostic logging |
| `SpaarkeCore.cs` | Remove TeamMembershipRule from DI |
| `TeamMembershipRule.cs` | Deleted (redundant) |
| `ExplicitGrantRule.cs` | Deleted (redundant) |
| `ExplicitDenyRule.cs` | Deleted (redundant) |
| `uac-access-control.md` | New UAC architecture docs |
| `AuthorizationTests.cs` | Update to use valid operations |

## Appendix B: Related Resources

- [UAC Architecture](../architecture/uac-access-control.md)
- [Auth Constraints](../../.claude/constraints/auth.md)
- [ADR-003: Authorization Seams](../../.claude/adr/ADR-003-lean-authorization-seams.md)
- [ADR-013: AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)
