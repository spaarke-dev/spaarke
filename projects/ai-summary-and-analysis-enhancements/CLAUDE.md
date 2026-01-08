# CLAUDE.md - AI Summary and Analysis Enhancements

> **Project-Specific AI Context**
> **Last Updated**: 2026-01-06

---

## Project Context

This project unifies AI Summary (Document Profile) and AI Analysis into a single orchestration service.

### Key Insight

**Document Profile is NOT a special case**—it's just another Playbook execution:
- Same pipeline as Analysis Builder
- Different trigger (auto on upload vs. user-initiated)
- Different UI context (File Upload Tab 2 vs. Analysis Workspace)
- Additional storage (also maps to `sprk_document` fields)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints
- ❌ No checkpointing - lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Applicable ADRs

| ADR | Why Applicable |
|-----|----------------|
| **ADR-013** | Primary - AI Architecture patterns |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-001** | Minimal API patterns for endpoints |
| **ADR-010** | DI minimalism - keep registrations minimal |
| **ADR-014** | AI caching strategy |
| **ADR-015** | AI data governance |

### Key Constraints (from ADRs)

- **MUST** use endpoint filters for AI authorization (ADR-008)
- **MUST** follow Minimal API patterns (ADR-001)
- **MUST NOT** create separate AI microservice (ADR-013)
- **MUST NOT** call Azure AI services directly from PCF (ADR-013)

---

## Implementation Constraints

### Authorization

- **Use FullUAC mode** (security requirement)
- **Retry for storage only** - AI execution doesn't need Document ID
- **Exponential backoff**: 2s → 4s → 8s (3 retries)

### Storage

- **Dual storage** for Document Profile:
  1. Standard: `sprk_analysisoutput` records
  2. Additional: Map to `sprk_document` fields
- **Use existing entities** (no new schema):
  - `sprk_analysisplaybook`
  - `sprk_aioutputtype`
  - `sprk_analysisoutput`

### Failure Handling

- **Soft failure** after 3 retries
- Outputs preserved in `sprk_analysisoutput`
- User message points to Analysis tab

### Cleanup

- **Immediately** after deployment verified
- No deprecation waiting period

---

## Quick Reference: File Locations

### Services to Modify

| File | Path | Purpose |
|------|------|---------|
| AnalysisOrchestrationService | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Extend for Document Profile |
| AnalysisAuthorizationFilter | `src/server/api/Sprk.Bff.Api/Api/Filters/` | Use unified auth service |
| AiAuthorizationFilter | `src/server/api/Sprk.Bff.Api/Api/Filters/` | Use unified auth service |
| SpaarkeCore | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | DI registrations |

### Services to Create

| File | Path | Purpose |
|------|------|---------|
| IAiAuthorizationService | `Services/Ai/` | Unified auth interface |
| AiAuthorizationService | `Services/Ai/` | FullUAC implementation |
| DocumentProfileResult | `Models/Ai/` | Response model |

### Services to Remove (Phase 2.4)

| File | Reason |
|------|--------|
| IDocumentIntelligenceService | Merged into AnalysisOrchestrationService |
| DocumentIntelligenceService | Merged into AnalysisOrchestrationService |
| AiAuthorizationFilter | Merged into unified filter |

---

## Code Patterns

### Playbook Lookup by Name

```csharp
// Code references playbook by name - configurable in UI
var playbook = await _playbookService.GetByNameAsync("Document Profile", ct);
```

### Retry Policy

```csharp
var policy = Policy
    .Handle<DataverseException>(ex => ex.StatusCode == 404 || ex.StatusCode == 503)
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
```

### Dual Storage

```csharp
// 1. Standard storage
await CreateAnalysisOutputAsync(analysisId, output);

// 2. Document field mapping (Document Profile only)
if (playbook.Name == "Document Profile" && output.Type.FieldMapping != null)
{
    await UpdateDocumentFieldAsync(documentId, output.Type.FieldMapping, output.Value);
}
```

---

## Testing Notes

- Test FullUAC with new documents (replication lag scenario)
- Test retry exhaustion → soft failure path
- Test backward compatibility with existing PCF
- Test dual storage consistency

---

## Related Resources

- [Spec](./spec.md) - Full design specification
- [Plan](./plan.md) - Implementation plan with WBS
- [Task Index](./tasks/TASK-INDEX.md) - Task registry
