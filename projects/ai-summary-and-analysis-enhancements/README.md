# AI Summary and Analysis Enhancements

> **Status**: In Progress
> **Created**: 2026-01-06
> **Branch**: `feature/ai-summary-and-analysis-enhancements`

---

## Overview

This project unifies AI Summary (now called **Document Profile**) and AI Analysis into a single orchestration service with proper FullUAC authorization.

### Problem Statement

1. **Authorization Inconsistency**: AI Summary fails with 403 errors due to different authorization approach than AI Analysis
2. **Service Duplication**: Two separate AI services (`DocumentIntelligenceService` and `AnalysisOrchestrationService`) with overlapping functionality

### Solution

- Create unified `IAiAuthorizationService` with FullUAC mode
- Document Profile is just another Playbook execution (same pipeline as Analysis)
- Use existing Dataverse entities (`sprk_analysisplaybook`, `sprk_aioutputtype`, `sprk_analysisoutput`)
- Dual storage: outputs in `sprk_analysisoutput` AND mapped to `sprk_document` fields

---

## Key Insight

**Document Profile is NOT a special case**—it's just another Playbook execution with:
- Different trigger point (auto on upload vs. user-initiated)
- Different UI context (File Upload PCF Tab 2 vs. Analysis Workspace)
- Additional storage (also maps to `sprk_document` fields)

---

## Implementation Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 2.1 | Unify Authorization (FullUAC + retry) | Not Started |
| 2.2 | Add Document Profile Playbook Support | Not Started |
| 2.3 | Migrate AI Summary Endpoint | Not Started |
| 2.4 | Cleanup (immediately after deployment) | Not Started |

---

## Graduation Criteria

- [ ] Single authorization implementation (1 service class)
- [ ] Single AI orchestration service (no DocumentIntelligenceService)
- [ ] Backward compatibility (existing PCF works unchanged)
- [ ] Performance maintained (Summary < 5s end-to-end)
- [ ] Configurable outputs via Playbook/Output Types
- [ ] Test coverage >= 80% on unified service
- [ ] ADR-013 documentation updated

---

## Technical Details

### Authorization Flow

```
SPE File Content (via OBO)     ← AI runs here, no Document ID needed
        ↓
AI Pipeline (OpenAI)           ← Generates outputs
        ↓
Storage (needs Document ID)    ← FullUAC + retry logic here
```

### Failure Handling

- Retry 3x with exponential backoff (2s → 4s → 8s)
- Soft failure: outputs preserved in `sprk_analysisoutput`
- User message: "Document Profile completed. View full results in Analysis tab."

### Files Affected

**Modified:**
- `AiAuthorizationFilter.cs`, `AnalysisAuthorizationFilter.cs`
- `AnalysisEndpoints.cs`, `AnalysisOrchestrationService.cs`
- `SpaarkeCore.cs` (DI)

**New:**
- `IAiAuthorizationService.cs`, `AiAuthorizationService.cs`
- `DocumentProfileResult.cs`

**Deleted (Phase 2.4):**
- `IDocumentIntelligenceService.cs`, `DocumentIntelligenceService.cs`
- `AiAuthorizationFilter.cs` (merged into unified filter)

---

## Related Resources

- [Spec Document](./spec.md)
- [Implementation Plan](./plan.md)
- [ENH-013](../../docs/enhancements/ENH-013-ai-authorization-and-service-unification.md)
- [ADR-013: AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)
- [UAC Architecture](../../docs/architecture/uac-access-control.md)
- [PR #102: Original fix](https://github.com/spaarke-dev/spaarke/pull/102)
