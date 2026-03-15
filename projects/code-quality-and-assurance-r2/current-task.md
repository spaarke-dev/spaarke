# Current Task — code-quality-and-assurance-r2

## Active Task
- **Task ID**: 020 (completed)
- **Status**: Task 020 completed. Next pending: 021 and 022 (parallel group C, both unblocked now).
- **Started**: —

## Quick Recovery
If resuming after compaction or new session:
1. Read this file
2. Read TASK-INDEX.md for overall progress
3. Say "continue" to pick up next pending task

| Field | Value |
|-------|-------|
| **Task** | 021 - Extract useAnalysisData + useAnalysisExecution hooks |
| **Step** | 0: Not started |
| **Status** | not-started |
| **Next Action** | Begin task 021 or 022 (parallel group C — both unblocked by 020 completion) |

## Progress
- Task 001: Fix 3 Unbounded Static Dictionaries — COMPLETED 2026-03-14
- Task 002: Replace new HttpClient() with IHttpClientFactory — COMPLETED (prior session)
- Task 003: Fix No-Op Arch Tests + Add Plugin Assembly Coverage — COMPLETED 2026-03-14
- Task 004: Delete Dead MsalAuthProvider.ts + Create Shared Logger — COMPLETED 2026-03-15
- Task 010: Decompose OfficeService.cs → 4 Focused Services — COMPLETED 2026-03-15
- Task 012: Segregate IDataverseService into 9 Focused Interfaces — COMPLETED 2026-03-15
- Task 020: Extract useAuth + useDocumentResolution Hooks — COMPLETED 2026-03-15
- Task 031: Document BaseProxyPlugin ADR-002 Violations — COMPLETED 2026-03-15

## Files Modified (Task 020)
- `src/client/pcf/AnalysisWorkspace/control/hooks/useAuth.ts` (created — auth initialization, token acquisition, SprkChat token refresh)
- `src/client/pcf/AnalysisWorkspace/control/hooks/useDocumentResolution.ts` (created — document/container/file ID resolution from Dataverse)
- `src/client/pcf/AnalysisWorkspace/control/hooks/index.ts` (updated — exports for new hooks)
- `src/client/pcf/AnalysisWorkspace/control/components/AnalysisWorkspaceApp.tsx` (modified — replaced inline state+effects with hook calls)
- `projects/code-quality-and-assurance-r2/tasks/TASK-INDEX.md` (status update)
- `projects/code-quality-and-assurance-r2/tasks/020-extract-auth-document-hooks.poml` (status update)

## Notes (Task 020)
- useAuth hook absorbs: isAuthInitialized state, sprkChatAccessToken state, sprkChatSessionId state, auth check effect, getAccessToken callback, SprkChat token refresh interval
- useDocumentResolution hook absorbs: resolvedDocumentId/ContainerId/FileId/DocumentName state, playbookId state, and provides resolveFromDocumentId callback
- loadAnalysis now calls resolveFromDocumentId(docId) instead of inline Dataverse query
- getAuthProvider import retained in AnalysisWorkspaceApp.tsx — still used directly in executeAnalysis (separate concern)
- Build shows 0 errors in AnalysisWorkspace files; pre-existing error in PlaybookBuilderHost (TestResultPreview.tsx useMemo conditional call) causes overall build to fail — not related to task 020
- ESLint: 0 errors, 6 pre-existing warnings in AnalysisWorkspaceApp.tsx (all missing dependency warnings)
