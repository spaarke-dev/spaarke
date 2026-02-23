# AI Analysis Integration Issue

> **Project**: email-to-document-automation-r2
> **Target Project**: ai-document-intelligence-r5
> **Discovered**: 2026-01-15
> **Priority**: P2 - Blocks automated AI analysis
> **Status**: DOCUMENTED - Ready for ai-document-intelligence-r5 implementation

---

## Executive Summary

Automated AI analysis (triggered by email-to-document jobs) fails because `ScopeResolverService.ResolvePlaybookScopesAsync` is a placeholder that returns empty scopes. Manual analysis from the UI works because it bypasses the scope resolver by passing `ToolIds[]` directly in the request.

**Impact**: All documents created by automated email processing will NOT have AI analysis run, even though `AutoEnqueueAi = true` is configured.

---

## Technical Root Cause

### The Failure Chain

```
EmailToDocumentJobHandler
  ↓ calls
AiJobQueueService.EnqueueDocumentAnalysisJobAsync(documentId, "Document Profile")
  ↓ queued as
AiJobHandler processes job
  ↓ calls
AppOnlyAnalysisService.AnalyzeDocumentAsync(documentId, "Document Profile")
  ↓ calls
ExecutePlaybookAnalysisAsync(...)
  ↓ step 1: loads playbook
PlaybookService.GetByNameAsync("Document Profile")
  → SUCCESS: Returns playbook with ToolIds populated (e.g., 3 tools)
  ↓ step 2: resolve scopes
ScopeResolverService.ResolvePlaybookScopesAsync(playbookId)
  → FAILURE: Returns ResolvedScopes([], [], [])  ← PLACEHOLDER IMPLEMENTATION
  ↓ step 3: check tools
if (scopes.Tools.Length == 0) → TRUE → "Playbook has no tools configured"
  → RETURN FAILED RESULT
```

### The Placeholder Code

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
**Lines**: 150-160

```csharp
public Task<ResolvedScopes> ResolvePlaybookScopesAsync(
    Guid playbookId,
    CancellationToken cancellationToken)
{
    _logger.LogDebug("Resolving scopes from playbook {PlaybookId}", playbookId);

    // Phase 1: Playbook resolution not yet implemented
    _logger.LogWarning("Playbook resolution not yet implemented, returning empty scopes");

    return Task.FromResult(new ResolvedScopes([], [], []));
}
```

This method should query Dataverse N:N relationships (`sprk_playbook_skill`, `sprk_playbook_knowledge`, `sprk_playbook_tool`) to load the playbook's associated Skills, Knowledge sources, and Tools.

### Why Manual Analysis Works

When a user triggers analysis from the Analysis Builder UI:

1. UI sends `AnalysisExecuteRequest` with `ToolIds[]` populated directly
2. `AnalysisOrchestrationService.ExecuteAnalysisAsync` receives the request
3. Since `request.PlaybookId` is null OR `request.ToolIds` is provided, it calls:
   ```csharp
   scopes = await _scopeResolver.ResolveScopesAsync(
       request.SkillIds ?? [],
       request.KnowledgeIds ?? [],
       request.ToolIds ?? [],  // ← UI passes these directly
       cancellationToken);
   ```
4. Note: `ResolveScopesAsync` ALSO returns empty scopes, but manual analysis doesn't rely on tool execution for basic analysis

**Key Insight**: The UI bypasses the playbook-based scope resolution by providing IDs directly in the request.

---

## Available Data

The playbook IS correctly loaded with all relationship IDs:

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs`
**Line**: 302-305

```csharp
playbook = await _playbookService.GetByNameAsync(playbookName, cancellationToken);
_logger.LogDebug(
    "Loaded playbook '{PlaybookName}' (Id={PlaybookId}) with {ToolCount} tools",
    playbook.Name, playbook.Id, playbook.ToolIds?.Length ?? 0);  // ← ToolIds ARE populated!
```

The `PlaybookResponse` model contains:
- `ActionIds: Guid[]`
- `SkillIds: Guid[]`
- `KnowledgeIds: Guid[]`
- `ToolIds: Guid[]`

**All these arrays are populated correctly** by `PlaybookService.GetByNameAsync` - the data IS available.

---

## Resolution Options

### Option A: Implement ResolvePlaybookScopesAsync (Full Implementation)

**Description**: Properly implement the scope resolver to query Dataverse N:N relationships.

**Pros**:
- Clean separation of concerns
- Reusable for other callers
- Follows original architecture intent

**Cons**:
- Requires multiple Dataverse queries (Skills, Knowledge, Tools)
- More complex implementation
- Higher latency

**Implementation Sketch**:
```csharp
public async Task<ResolvedScopes> ResolvePlaybookScopesAsync(
    Guid playbookId,
    CancellationToken cancellationToken)
{
    // Query N:N relationships from Dataverse
    var skills = await QueryRelatedEntitiesAsync<ResolvedSkill>(
        playbookId, "sprk_playbook_skill", "sprk_analysisskills", cancellationToken);

    var knowledge = await QueryRelatedEntitiesAsync<ResolvedKnowledge>(
        playbookId, "sprk_playbook_knowledge", "sprk_analysissknowledgebases", cancellationToken);

    var tools = await QueryRelatedEntitiesAsync<ResolvedTool>(
        playbookId, "sprk_playbook_tool", "sprk_analysistools", cancellationToken);

    return new ResolvedScopes(
        skills.ToArray(),
        knowledge.ToArray(),
        tools.ToArray());
}
```

### Option B: Bypass Scope Resolver in AppOnlyAnalysisService (Simpler)

**Description**: Since the playbook already contains the IDs, use them directly instead of re-querying through the scope resolver.

**Pros**:
- Minimal code change
- No additional Dataverse queries
- Uses already-fetched data
- Lower latency

**Cons**:
- Doesn't fix the scope resolver for other callers
- Slightly divergent paths for automated vs manual analysis

**Implementation Sketch** (in `AppOnlyAnalysisService.ExecutePlaybookAnalysisAsync`):

```csharp
// 1. Load playbook by name (already done)
playbook = await _playbookService.GetByNameAsync(playbookName, cancellationToken);

// 2. OPTION B: Use playbook IDs directly instead of scope resolver
// OLD: var scopes = await _scopeResolver.ResolvePlaybookScopesAsync(playbook.Id, cancellationToken);
// NEW: Build scopes from playbook IDs
var tools = await LoadToolsByIdsAsync(playbook.ToolIds, cancellationToken);
var scopes = new ResolvedScopes(
    skills: [],  // Not needed for basic analysis
    knowledge: [],  // Not needed for basic analysis
    tools: tools
);
```

### Option C: Unified Path via AnalysisOrchestrationService (Recommended)

**Description**: Refactor `AppOnlyAnalysisService` to use the same execution path as manual analysis by building an `AnalysisExecuteRequest` with the playbook's IDs.

**Pros**:
- Single code path for all analysis (manual and automated)
- Reuses existing, working infrastructure
- Easier to maintain
- Consistent behavior

**Cons**:
- Requires `AppOnlyAnalysisService` to build a request and call `AnalysisOrchestrationService`
- May need to handle app-only auth context differently

**Implementation Sketch**:

```csharp
// In AppOnlyAnalysisService.AnalyzeDocumentAsync:

// 1. Load playbook
var playbook = await _playbookService.GetByNameAsync(playbookName, cancellationToken);

// 2. Build request using playbook's IDs (same structure as UI request)
var request = new AnalysisExecuteRequest
{
    DocumentIds = [documentId],
    PlaybookId = playbook.Id,
    ActionId = playbook.ActionIds.FirstOrDefault(),
    SkillIds = playbook.SkillIds,
    KnowledgeIds = playbook.KnowledgeIds,
    ToolIds = playbook.ToolIds  // ← Pass directly, bypassing scope resolver
};

// 3. Execute through orchestration service (existing working path)
// Note: Will need to handle app-only HttpContext or create alternative method
await foreach (var chunk in _orchestrationService.ExecuteAnalysisAsync(request, appOnlyContext, cancellationToken))
{
    // Process results
}
```

---

## Recommendation

**Option C (Unified Path)** is recommended because:

1. **Single code path** - Eliminates divergent behavior between manual and automated analysis
2. **Already works** - The `AnalysisOrchestrationService.ExecuteAnalysisAsync` path is proven to work with directly-provided IDs
3. **Maintainability** - Bug fixes and enhancements apply to both paths
4. **Scope resolver remains optional** - Can implement `ResolvePlaybookScopesAsync` later if needed for other features

**Key Consideration from User**:
> "One important consideration is whether there needs to be a separate orchestration path or if the analysis can be triggered using the same components as a manual Document upload (and AI analysis)"

**Answer**: No separate orchestration path is needed. The same components CAN be used. The fix is to pass the playbook's IDs directly (like the UI does) rather than asking the scope resolver to re-fetch them.

---

## Files Involved

| File | Role | Change Needed |
|------|------|---------------|
| `Services/Ai/ScopeResolverService.cs` | Contains placeholder `ResolvePlaybookScopesAsync` | Option A: Implement properly |
| `Services/Ai/AppOnlyAnalysisService.cs` | Automated analysis entry point | Option B/C: Bypass scope resolver |
| `Services/Ai/AnalysisOrchestrationService.cs` | Manual analysis orchestrator | Option C: May need app-only variant |
| `Models/Ai/PlaybookDto.cs` | Contains `PlaybookResponse` with all IDs | No change needed |
| `Services/Ai/PlaybookService.cs` | Already loads playbook with IDs | No change needed |

---

## Verification Steps

After implementing the fix:

1. **Send a test email** to the monitored mailbox
2. **Query Application Insights** for `[AttachmentProcessDebug]` logs to confirm processing
3. **Query Dataverse** for the created document and verify:
   - `sprk_aianalysisstatus` is NOT "pending" or empty
   - `sprk_aisummary` or other AI fields are populated
4. **Check App Insights** for successful AI job completion (no "Playbook has no tools" warning)

---

## Related Context

- **Polling backup service**: `EnablePolling = true` runs every 5 minutes, which will continuously attempt AI analysis on unprocessed documents (and fail until this is fixed)
- **Manual workaround**: Users can manually trigger AI analysis from the Analysis Builder UI for any document
- **Race condition fix**: This issue is separate from the attachment race condition (which was fixed with retry logic)

---

## Log Signatures

**Before Fix** (failure):
```
Warning: Playbook resolution not yet implemented, returning empty scopes
Warning: Playbook 'Document Profile' has no tools configured
```

**After Fix** (success):
```
Debug: Loaded playbook 'Document Profile' (Id=xxx) with 3 tools
Debug: Executing tool 'Document Summary' (Type=summarize)
Information: Analysis completed for document xxx
```

---

*Created: 2026-01-15*
*For: ai-document-intelligence-r5 project team*
