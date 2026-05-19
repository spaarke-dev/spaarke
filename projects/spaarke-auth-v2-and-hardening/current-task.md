# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: Phase B started (1/11). Task 020 complete; 021 next.
> **Active Phase**: B — Consumer migration
> **Last Updated**: 2026-05-19

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Task 020 ✅ — AiSessionProvider migrated to function-based API (useAuth internal, no token prop/field). Tests 40/40 in the affected suites. |
| **Next Action** | `continue` → task 021 (SpaarkeAi App.tsx — stop snapshotting token in useEffect). Single, no parallel group, depends on 020 ✅. |

## How to Resume

```
continue
```

## Phase B progress

| # | Task | Status | Notes |
|---|------|--------|-------|
| 020 | AiSessionProvider context → function-based API | ✅ | Commit pending |
| 021 | SpaarkeAi App.tsx — stop snapshotting token | 🔲 | Next — depends on 020 |
| 022 | SpaarkeAi panes (B-Parallel-1, depends 020) | 🔲 | Parallel-safe after 021 |
| 023 | SprkChat API refactor (B-Parallel-1, depends 020) | 🔲 | Parallel-safe after 021 |
| 024 | PlaybookBuilder (B-Parallel-2, depends 023) | 🔲 | |
| 025 | DocRelationshipViewer (B-Parallel-2, depends 023) | 🔲 | |
| 026 | AnalysisWorkspace (B-Parallel-2, depends 023) | 🔲 | |
| 027 | SemanticSearch SPA (B-Parallel-2, depends 023) | 🔲 | |
| 028 | PCF rebuilds (B-Parallel-2, depends Phase A) | 🔲 | |
| 029 | bffDataServiceAdapter docs (B-Parallel-2, depends 023) | 🔲 | |
| 030 | Delete duplicate buildBffApiUrl (B-Parallel-2, depends Phase A) | 🔲 | |

## Files modified this task (020)

- `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` — interface + provider rewired to useAuth()
- `src/client/shared/Spaarke.AI.Widgets/src/providers/useAiSession.ts` — JSDoc updated
- `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx` — wrappers drop token props; new auth-surface tests
- `src/client/shared/Spaarke.AI.Widgets/src/__tests__/sse-event-routing.test.tsx` — renamed from .ts (pre-existing JSX parse failure); wrapper drops token props
- `src/client/shared/Spaarke.AI.Widgets/src/__mocks__/@spaarke/auth.ts` — adds useAuth + AuthenticatedFetchFn stubs
- `src/client/shared/Spaarke.AI.Widgets/package.json` — ts-node devDep added (needed for jest TS config)
- `projects/spaarke-auth-v2-and-hardening/tasks/020-migrate-ai-session-provider.poml` — status: completed
- `projects/spaarke-auth-v2-and-hardening/tasks/TASK-INDEX.md` — 020 ✅, totals updated to 13/49

## Predicted consumer breakage to fix in 021/022/023

| File | Line | Pattern | Owner task |
|---|---|---|---|
| `src/solutions/SpaarkeAi/src/components/ChatHistoryPanel.tsx` | 155 | `{ bffBaseUrl, token, isAuthenticated, ... } = useAiSession()` | 022 |
| `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` | 411 | `{ bffBaseUrl, token, ... } = useAiSession()` | 022 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx` | 284 | `{ bffBaseUrl, token, ... } = useAiSession()` | 022 |
| `src/client/shared/Spaarke.AI.Widgets/src/components/FeedbackButtons.tsx` | 76 | `token: string \| null` prop — callers will no longer have a token to pass | 022 (or 023 if API refactor cleanups roll it up) |
| `src/solutions/SpaarkeAi/src/App.tsx` | wherever it passes `token` to AiSessionProvider | now-removed prop | 021 |

## Resume Plan for Task 021

- Read `src/solutions/SpaarkeAi/src/App.tsx`
- Remove the `token` / `isAuthenticated` useState/useEffect that snapshots the token
- Drop the `token` and `isAuthenticated` props passed to `<AiSessionProvider>`
- App.tsx still calls `ensureAuthInitialized()` (from authInit.ts) before rendering — that's the canonical bootstrap, unchanged
- Update SpaarkeAi-specific tests if any

## 🚨 CRITICAL CARRYOVER — DON'T REPEAT THESE BUGS (still applies)

When a consumer (post-Phase-A) wires `tenantId: getRuntimeTenantId()` into initAuth, MUST use import alias to avoid name collision with locally exported async getTenantId. See memory `feedback_name_collision_in_consumer_authinit` + `project_auth_v2_baseline_msal_bug`.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0: 5/5 ✅
- Phase A: 7/7 ✅ + 2 hotfix commits (`811e98b5`, `48788c1f`)
- Phase B: 1/11 ✅ (task 020)
- Overall: 13/49 tasks (27%)
- Library version: `@spaarke/auth@2.0.0`
- AI.Widgets tests delta: +16 passing (252 → 268), +1 suite (7 → 8), -1 failing suite (10 → 9)
