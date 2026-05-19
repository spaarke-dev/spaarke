# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: 🎉 **PHASE B COMPLETE (11/11)**. MSAL regression test pending. Phase C next.
> **Active Phase**: B done. Phase C (server hardening 040-049) NEXT.
> **Last Updated**: 2026-05-19 (post-Wave-2)

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Phase B 11/11 ✅. Wave 1 committed (`918b0830`); Wave 2 pending commit. All Code Page + PCF + shared lib consumers migrated to function-based @spaarke/auth contract. ui-components rebuilds clean; SpaarkeAi vite builds 1.9 MB single-file HTML. |
| **Next Action** | User: deploy the Wave 1+2 changes (SpaarkeAi, AnalysisWorkspace, DocumentRelationshipViewer, PlaybookBuilder, SemanticSearch Code Pages + 6 PCFs) and run the MSAL regression test from `.claude/patterns/auth/spaarke-sso-binding.md`. Then `continue` → Phase C (server hardening, tasks 040-049). |

## How to Resume

```
continue
```

## Phase B FINAL — 11/11 ✅

| # | Task | Status | Wave | Commit |
|---|------|--------|------|--------|
| 020 | AiSessionProvider context → function-based API | ✅ | pre-wave | `6b6106f6` |
| 021 | SpaarkeAi App.tsx — stop snapshotting token | ✅ | Wave 1 | `918b0830` |
| 022 | SpaarkeAi panes + FeedbackButtons + R1 legacy cleanup | ✅ | Wave 1 | `918b0830` |
| 023 | SprkChat API refactor + 3 hooks + useSseStream | ✅ | Wave 1 | `918b0830` |
| 028 | Verify + rebuild all 6 PCFs against v2 | ✅ | Wave 1 | `918b0830` |
| 030 | Delete duplicate buildBffApiUrl (was dead code) | ✅ | Wave 1 | `918b0830` |
| 024 | PlaybookBuilder migration | ✅ | Wave 2 | pending |
| 025 | DocumentRelationshipViewer migration | ✅ | Wave 2 | pending |
| 026 | AnalysisWorkspace migration (incl. ChatPanel SprkChat fix + dead authService deletion) | ✅ | Wave 2 | pending |
| 027 | SemanticSearch (deleted deprecated auth) + External SPA (out-of-scope documented) | ✅ | Wave 2 | pending |
| 029 | bffDataServiceAdapter docs (also bffUploadServiceAdapter + adapters/index.ts) | ✅ | Wave 2 | pending |

## D-AUTH-7 exception sites (raw `Authorization: Bearer ${token}` legitimately required)

After Phase B, these are the ONLY places raw Bearer headers exist. Each has a `// Auth v2 (D-AUTH-7):` justification comment. The ESLint allowlist task (Phase E task 070-area) needs this list:

| File | Reason |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` (2 sites) | SSE: handlePlanProceed + handleEditorRefine |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatUploadZone.tsx` (2 sites) | XHR uploads (authenticatedFetch can't wrap XHR) |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` | Main SSE fetch entry |
| `src/client/code-pages/PlaybookBuilder/src/services/aiPlaybookService.ts` | SSE (mirror of SprkChat pattern) |
| `src/client/code-pages/PlaybookBuilder/src/services/dataverseClient.ts` | Dataverse Web API (different scope — `authenticatedFetch` is BFF-scoped via buildBffApiUrl, would route to wrong host) |
| `src/client/code-pages/AnalysisWorkspace/src/services/analysisApi.ts::executeAnalysis` | SSE |
| `src/client/external-spa/src/auth/bff-client.ts::executeFetch` | External SPA out-of-scope for @spaarke/auth (B2B portal — sessionStorage vs localStorage threat model split documented in-file) |

## Carryovers (logged for follow-up; NOT blockers for Phase B)

### Code/runtime carryovers
1. **`Spaarke.UI.Components/src/services/document-upload/`** — `SdapApiClient.ts`, `ODataDataverseClient.ts`, `NavMapClient.ts` + `hooks/useAiSummary.ts` still build raw Bearer headers in implementation code. Need migration to `authenticatedFetch` (or D-AUTH-7 justification if they have legitimate exception reasons). Flagged by task 029.
2. **`src/client/code-pages/PlaybookBuilder/src/services/authService.ts`** — deprecated, zero consumers in PlaybookBuilder. Safe to delete in a cleanup PR. Flagged by task 024.
3. **`src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts`** — references legacy patterns. NOT a Phase B consumer (LegalWorkspace was not in the migration list). Audit at Phase D security cleanup.
4. **`src/client/code-pages/SpeDocumentViewer`** — residual dead `accessToken: string` props in hooks (`useCheckoutFlow.ts`, `useDocumentPreview.ts`, `types.ts`) + deprecated `AuthService.ts`. Not in active data flow. Cleanup candidate.

### Build/tooling carryovers
5. **SemanticSearch webpack build broken** by `@lexical/react` `.prod.mjs` resolution (Can't resolve `react/jsx-runtime`). Confirmed pre-existing (not caused by Wave 2). Needs webpack `extensionAlias` config OR `@spaarke/ui-components` packaging fix. Blocks SemanticSearch redeploy until fixed. Flagged by task 027.
6. **`@spaarke/ui-components` jest tests can't run** — package has React 16 peer deps but `@testing-library/react@14` requires React 18's `react-dom/client`. Confirmed pre-existing. Tests don't run; relies on consumer-side test coverage. Flagged by task 023.
7. **AnalysisWorkspace `src/__tests__/useAnalysisLoader.test.ts`** still uses old `token: string | null` option shape — pre-existing broken in this package. Flagged by task 026.
8. **ESLint Bearer-literal rule path-based allowlist** needs to be authored (Phase E task 070-area) covering all 9 D-AUTH-7 exception sites listed above. SSE + XHR + Dataverse Web API + external SPA are all legitimate exception classes.

### Carryovers RESOLVED during Wave 2
- ✅ **Wave 1 carryover #1 (DocumentRelationshipViewer explicit authority)** — closed as false positive by task 025. Active `authInit.ts` does NOT set explicit authority; only the deprecated `services/auth/msalConfig.ts` does, and it's not imported by the active code path.
- ✅ **Wave 1 carryover (ChatPanel.tsx SprkChat consumer breakage)** — fixed by task 026 (passed `authenticatedFetch` + `getAccessToken` per the new SprkChat contract).

## 🚨 CRITICAL CARRYOVER (still applies for future consumer additions)

Any NEW consumer that wires `tenantId: getRuntimeTenantId()` into `initAuth({...})` MUST use import alias to avoid name collision with locally-exported async `getTenantId`. See memory `feedback_name_collision_in_consumer_authinit` + `project_auth_v2_baseline_msal_bug`. The 4 Wave 2 consumers (024 PlaybookBuilder, 025 DocRelationshipViewer, 026 AnalysisWorkspace, 027 SemanticSearch) all turned out to NOT have this collision pattern (only SpaarkeAi did) — but new consumers should check.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0: 5/5 ✅
- Phase A: 7/7 ✅ + 2 hotfix commits (`811e98b5`, `48788c1f`)
- Phase B: **11/11 ✅** (Wave 1 commit `918b0830`; Wave 2 pending commit)
- Overall: **23/49 tasks (47%)**
- Library versions: `@spaarke/auth@2.0.0`, `@spaarke/ui-components@2.0.0`
- Integration verified each wave: ui-components rebuild clean; SpaarkeAi vite builds; 6 PCFs build prod bundles; 3 of 4 code pages build clean (SemanticSearch blocked by pre-existing webpack issue — see carryover #5).
- Deleted forbidden legacy files: 4 (Wave 2) — `AnalysisWorkspace/services/authService.ts` (had `__SPAARKE_BFF_TOKEN__`), `AnalysisWorkspace/config/msalConfig.ts`, `SemanticSearch/services/auth/MsalAuthProvider.ts`, `SemanticSearch/services/auth/msalConfig.ts`.
- Deleted R1 legacy files: 4 (Wave 1) — SpaarkeAi's `LeftPane.tsx`, `ChatPanel.tsx`, `OutputPanel.tsx`, `SourcePanel.tsx` (were using `useStandaloneAi`).

## Next: Phase C (server hardening 040-049)

10 tasks, ~19h. Phase gate: no plain-text secrets in App Service config; managed identity for all server outbound; no `/debug/*` endpoints; webhook signatures verified; API key scheme formalized. Many parallel-safe within phase (C-Parallel-1 + C-Parallel-2 groups). Then Phase D (CSP/CAE/security 060-064), Phase E (CI 070-072), Phase F (docs/ADR-027 090).

**Per project CLAUDE.md risk-tier rule**: Phase C is server-only — MSAL browser regression test is NOT required (do an OBO smoke check against the deployed BFF instead). Manual MSAL test IS required after Phase B Wave 1+2 deploys.
