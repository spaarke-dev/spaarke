# Current Task State - AI Summary and Analysis Enhancements

> **Last Updated**: 2026-01-06 23:50 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none (ready to start 001) |
| **Step** | Project pipeline complete |
| **Status** | not-started |
| **Next Action** | Say "work on task 001" to start |

### Git Status
- **Branch**: `feature/ai-summary-and-analysis-enhancements`
- **PR**: [#103](https://github.com/spaarke-dev/spaarke/pull/103) (draft)
- **Last Commit**: `8cd774c` - Added PCF tasks for soft failure handling

### Files Created This Session
- `README.md` - Project overview
- `plan.md` - Implementation plan (28 tasks across 4 phases)
- `CLAUDE.md` - AI context file
- `tasks/*.poml` - 28 task files
- `tasks/TASK-INDEX.md` - Task registry with status

### Critical Context
**Document Profile is NOT a special case**—it's just another Playbook execution with:
- Different trigger (auto on upload)
- Different UI context (File Upload Tab 2)
- Additional storage (maps to sprk_document fields)

---

## Full State (Detailed)

### Project Summary

Unify AI Summary (Document Profile) and AI Analysis into single orchestration service with FullUAC authorization.

### Implementation Phases

| Phase | Tasks | Description | Status |
|-------|-------|-------------|--------|
| 2.1 | 7 | Unify Authorization (FullUAC + retry) | not-started |
| 2.2 | 10 | Document Profile Playbook Support + PCF | not-started |
| 2.3 | 5 | Migrate AI Summary Endpoint | not-started |
| 2.4 | 5 | Cleanup (immediately after deployment) | not-started |
| Wrap-up | 1 | Project closure | not-started |

### Key Decisions (Owner Clarifications 2026-01-06)

1. **Terminology**: "Document Profile" (not "Auto-Summary" or "Simple Mode")
2. **Authorization**: FullUAC mode (security requirement)
3. **Retry**: Storage operations only, 3x with exponential backoff (2s, 4s, 8s)
4. **Failure**: Soft failure - outputs preserved in sprk_analysisoutput
5. **Entities**: Use existing (sprk_analysisplaybook, sprk_aioutputtype, sprk_analysisoutput)
6. **Cleanup**: Immediately after deployment verified
7. **PCF Updates**: Display warning MessageBar on soft failure (added after review)

### Task 001 Details

**File**: `tasks/001-create-authorization-interface.poml`
**Goal**: Create IAiAuthorizationService interface for unified AI authorization
**Key Files to Read**:
- `.claude/adr/ADR-008-endpoint-filters.md`
- `.claude/adr/ADR-013-ai-architecture.md`
- `src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs`
- `src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs`

---

## Session History

### 2026-01-06 Session
- Investigated 403 error on AI Summary
- Applied Phase 1 scaffolding workaround (PR #102)
- Created spec.md with detailed design
- Conducted owner interview - captured 7 key decisions
- Updated spec.md with Document Profile terminology
- Ran project-pipeline to generate artifacts and tasks
- Added PCF tasks (018, 019) after user pointed out missing UI updates
- Checkpoint saved before compaction
