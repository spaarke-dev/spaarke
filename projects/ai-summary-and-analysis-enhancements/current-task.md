# Current Task State - AI Summary and Analysis Enhancements

> **Last Updated**: 2026-01-06 (by project-pipeline)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none |
| **Step** | Project initialized, ready for task 001 |
| **Status** | not-started |
| **Next Action** | Execute task 001 (Create IAiAuthorizationService interface) |

### Files Created This Session
- `projects/ai-summary-and-analysis-enhancements/README.md` - Project overview
- `projects/ai-summary-and-analysis-enhancements/plan.md` - Implementation plan
- `projects/ai-summary-and-analysis-enhancements/CLAUDE.md` - AI context
- `projects/ai-summary-and-analysis-enhancements/tasks/*.poml` - Task files

### Critical Context
Document Profile is NOT a special case—it's just another Playbook execution with:
- Different trigger (auto on upload)
- Different UI context (File Upload Tab 2)
- Additional storage (maps to sprk_document fields)

---

## Full State (Detailed)

### Project Summary

Unify AI Summary (Document Profile) and AI Analysis into single orchestration service with FullUAC authorization.

### Implementation Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 2.1 | Unify Authorization (FullUAC + retry) | not-started |
| 2.2 | Document Profile Playbook Support | not-started |
| 2.3 | Migrate AI Summary Endpoint | not-started |
| 2.4 | Cleanup (immediately after deployment) | not-started |

### Key Decisions (Owner Clarifications 2026-01-06)

1. **Terminology**: "Document Profile" (not "Auto-Summary" or "Simple Mode")
2. **Authorization**: FullUAC mode (security requirement)
3. **Retry**: Storage operations only, 3x with exponential backoff
4. **Failure**: Soft failure - outputs preserved in sprk_analysisoutput
5. **Entities**: Use existing (sprk_analysisplaybook, sprk_aioutputtype, sprk_analysisoutput)
6. **Cleanup**: Immediately after deployment verified

---

## Session History

### 2026-01-06 Session
- Investigated 403 error on AI Summary
- Applied Phase 1 scaffolding workaround (PR #102)
- Created spec.md with detailed design
- Conducted owner interview - captured 7 key decisions
- Updated spec.md with Document Profile terminology
- Ran project-pipeline to generate artifacts and tasks
