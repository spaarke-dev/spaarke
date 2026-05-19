# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: Phase B Wave 1 complete (6/11 of Phase B). Wave 2 next.
> **Active Phase**: B — Consumer migration
> **Last Updated**: 2026-05-19 (post-Wave-1)

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Wave 1 complete + validated: ui-components rebuilds clean; SpaarkeAi vite builds 1.9 MB single-file HTML; all 6 PCFs rebuild prod bundles. Tasks 020, 021, 022, 023, 028, 030 ✅. |
| **Next Action** | `continue` → dispatch Wave 2: tasks 024 + 025 + 026 + 027 + 029 in parallel (all depend on 023's new SprkChat contract). |

## How to Resume

```
continue
```

## Phase B progress (6/11)

| # | Task | Status | Notes |
|---|------|--------|-------|
| 020 | AiSessionProvider context → function-based API | ✅ | Commit `6b6106f6` |
| 021 | SpaarkeAi App.tsx — stop snapshotting token | ✅ | Wave 1 |
| 022 | SpaarkeAi panes + FeedbackButtons | ✅ | Wave 1; also deleted 4 R1 legacy files (LeftPane, ChatPanel, OutputPanel, SourcePanel) |
| 023 | SprkChat API refactor + 3 hooks + useSseStream | ✅ | Wave 1; new prop signature: `authenticatedFetch` + `getAccessToken` (replaces `accessToken`) |
| 028 | Verify + rebuild all PCFs (6 of them, not 3) | ✅ | Wave 1; all already v2-clean; infra fixes: build:prod scripts on 2 PCFs + eslint devDep on UniversalDatasetGrid |
| 030 | Delete duplicate buildBffApiUrl | ✅ | Wave 1; duplicate was dead code (no PCF actually imported it) |
| 024 | PlaybookBuilder (depends 023) | 🔲 | Wave 2 |
| 025 | DocRelationshipViewer (depends 023) | 🔲 | Wave 2 |
| 026 | AnalysisWorkspace (depends 023) | 🔲 | Wave 2 |
| 027 | SemanticSearch SPA (depends 023) | 🔲 | Wave 2 |
| 029 | bffDataServiceAdapter docs (depends 023) | 🔲 | Wave 2 |

## SprkChat new prop signature (from task 023 — Wave 2 consumers must adopt)

```tsx
import { useAuth } from '@spaarke/auth';
const { authenticatedFetch, getAccessToken } = useAuth();

<SprkChat
  apiBaseUrl={bffBaseUrl}
  authenticatedFetch={authenticatedFetch}  // REQUIRED — replaces accessToken
  getAccessToken={getAccessToken}          // REQUIRED — used by SSE streams
  // all other props unchanged
/>
```

Types now exported from `@spaarke/ui-components`:
- `AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>`
- `AccessTokenGetter   = () => Promise<string>`

## Wave 1 carryovers (track for later cleanup tasks)

1. **DocumentRelationshipViewer/authInit.ts:55** — sets explicit `authority: https://login.microsoftonline.com/${tenantId}`. All other PCFs intentionally omit this per the 2026-05-13 popup-regression fix. Potential silent SSO regression. Owner: task 025 should audit while migrating that PCF, OR a dedicated audit follow-up.
2. **SpeDocumentViewer** — residual dead `accessToken: string` props in `useCheckoutFlow.ts:55`, `useDocumentPreview.ts:56`, `types.ts:218`, and deprecated `AuthService.ts`. Not in active data flow (host doesn't pass them). Owner: future cleanup, or rolled into task 029.
3. **ESLint Bearer-literal rule needs path-based allowlist** for the 4 SSE/XHR exception sites that legitimately need raw `Authorization: Bearer ${token}` because the wrapper APIs can't handle XHR or EventSource: `SprkChat.tsx` (handlePlanProceed + handleEditorRefine), `SprkChatUploadZone.tsx` (2 XHR uploads), `useSseStream.ts` (main fetch). Each carries a `// Auth v2 (D-AUTH-7):` justification comment. Owner: the ESLint rule task (task 070 area).
4. **Pre-existing test infra issue in @spaarke/ui-components** — package has React 16 peer deps but `@testing-library/react@14` requires React 18's `react-dom/client`. Jest tests cannot run there as a result. Confirmed pre-existing (not caused by Wave 1). Owner: infra fix follow-up.
5. **Pre-existing dead import** `SseStreamStatus` removed from `useAiSummary.ts:12` during Wave 1 integration to unblock ui-components build (single-line scoped fix).

## 🚨 CRITICAL CARRYOVER — DON'T REPEAT THESE BUGS (still applies)

When a consumer wires `tenantId: getRuntimeTenantId()` into `initAuth({...})`, MUST use import alias to avoid name collision with locally-exported async `getTenantId`. See memory `feedback_name_collision_in_consumer_authinit` + `project_auth_v2_baseline_msal_bug`. Wave 2 consumers (024-027) — read their `authInit.ts` and grep for the name-collision pattern FIRST.

## Wave 2 dispatch plan (next)

Parallel batch of 5: tasks 024 + 025 + 026 + 027 + 029. All depend on 023's new SprkChat contract (now landed). All touch different Code Pages or shared adapter docs — file-disjoint. Dispatch via Agent sub-agents same as Wave 1.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0: 5/5 ✅
- Phase A: 7/7 ✅ + 2 hotfix commits (`811e98b5`, `48788c1f`)
- Phase B: 6/11 ✅ (020 + Wave 1: 021/022/023/028/030)
- Overall: 18/49 tasks (37%)
- Library versions: `@spaarke/auth@2.0.0`, `@spaarke/ui-components@2.0.0` (rebuilt this wave)
- Integration verified: ui-components `npm run build` clean; SpaarkeAi `vite build` succeeds (1.9 MB HTML, 2,718 modules)
- PCFs verified: all 6 (UniversalDatasetGrid, SpeDocumentViewer, SemanticSearchControl, RelatedDocumentCount, EmailProcessingMonitor, DocumentRelationshipViewer) build prod bundles cleanly against @spaarke/auth v2

## After Wave 2

- Phase B will be 11/11 ✅
- MSAL regression test → user runs after Wave 1+2 deploy (deferred from per-task to per-wave per project CLAUDE.md risk-tier rule)
- Then Phase C (server hardening 040-049), Phase D (CSP/CAE/security 060-064), Phase E (CI 070-071), Phase F (docs/ADR-027 090)
