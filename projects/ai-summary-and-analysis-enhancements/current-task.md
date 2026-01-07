# Current Task State - AI Summary and Analysis Enhancements

> **Last Updated**: 2026-01-06 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Project Initialization |
| **Step** | Completed: Research design spec created |
| **Status** | completed |
| **Next Action** | Run `/project-pipeline` to generate plan.md and task files |

### Files Created This Session
- `projects/ai-summary-and-analysis-enhancements/spec.md` - Research design specification
- `projects/ai-summary-and-analysis-enhancements/current-task.md` - This file

### Critical Context
The 403 error on AI Summary was fixed with Phase 1 scaffolding (PR #102). This project will:
1. Properly fix the authorization timing issue
2. Unify AI Summary and AI Analysis into single service
3. Use Playbook/Output Scopes for configurable summary outputs

---

## Full State (Detailed)

### Background

**Original Issue**: 403 Forbidden on AI Summary during document upload.

**Root Cause**:
- `AiAuthorizationFilter` did full UAC check via `RetrievePrincipalAccess`
- For newly-created documents, this failed (404 from replication lag)
- `AnalysisAuthorizationFilter` used Phase 1 scaffolding (skip UAC)
- Inconsistency between the two filters caused the failure

**Workaround Applied** (PR #102):
- Updated `AiAuthorizationFilter` to skip UAC (match `AnalysisAuthorizationFilter`)
- Added diagnostic logging with `[UAC-DIAG]` prefix
- Removed legacy authorization rules (TeamMembershipRule, ExplicitGrantRule, ExplicitDenyRule)

### Project Goals

1. **Fix Authorization Properly**:
   - Create unified `IAiAuthorizationService`
   - Add timing fix (retry with backoff for new documents)
   - Single authorization approach for both AI services

2. **Unify AI Services**:
   - Consolidate `DocumentIntelligenceService` into `AnalysisOrchestrationService`
   - Add "Simple Mode" for auto-summary (no chat, auto-persist)
   - Keep backward compatibility with existing endpoint

3. **Output Scope Configuration**:
   - Define output scopes (TL;DR, Summary, Keywords, Entities)
   - Configure which outputs to generate per playbook
   - Auto-persist to `sprk_document` fields

### Key Files

| File | Purpose |
|------|---------|
| `IAnalysisOrchestrationService.cs` | Unified AI service interface (exists) |
| `AnalysisOrchestrationService.cs` | Unified AI service (to be extended) |
| `IDocumentIntelligenceService.cs` | To be deprecated |
| `DocumentIntelligenceService.cs` | To be merged/deprecated |
| `AiAuthorizationFilter.cs` | To be unified with `AnalysisAuthorizationFilter` |

### Implementation Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 2.1 | Unify Authorization | pending |
| 2.2 | Add Simple Mode to Analysis | pending |
| 2.3 | Migrate AI Summary Endpoint | pending |
| 2.4 | Cleanup (remove deprecated code) | pending |

---

## Session History

### 2026-01-06 Session
- Investigated 403 error on AI Summary
- Found root cause: authorization filter inconsistency + timing issue
- Applied Phase 1 scaffolding workaround
- Added diagnostic logging
- Created PR #102
- Created ENH-013 analysis document
- Created project spec.md at `projects/ai-summary-and-analysis-enhancements/`
