# TASK-INDEX — Spaarke Auth v2 + Hardening

> **Total Tasks**: 6/49 complete
> **Status**: In Progress (Phase 0)
> **Last Updated**: 2026-05-18
> **Authoritative scope**: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)

## Status Legend
- :black_square_button: Not Started
- :arrows_counterclockwise: In Progress
- :white_check_mark: Complete
- :no_entry: Blocked

## Parallel Group Legend

Tasks within the same parallel group (e.g., `B-Parallel-1`) can run concurrently as separate `task-execute` invocations in a single Claude Code message. Sub-agents launched via Agent tool CANNOT write to `.claude/` paths (root CLAUDE.md §3) — tasks marked `Main-Only` must run in the main session.

---

## Phase 0 — Pre-flight (5 tasks, ~90 min)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 001 | Rename DEPRECATED-msal-client.md + DEPRECATED-spaarke-auth-initialization.md; update INDEX references | :white_check_mark: | Main-Only (.claude/) | none |
| 002 | Apply STOP banners to 5 partially-superseded pattern/constraint/architecture docs | :white_check_mark: | Main-Only (.claude/) | 001 |
| 003 | Verify + finalize project CLAUDE.md prohibition section | :white_check_mark: | Main-Only (.claude/) | 002 |
| 004 | Update root CLAUDE.md §15 Pointers to reference AUDIT-FINDINGS-AUTH-SYSTEM.md | :white_check_mark: | Main-Only (.claude/) | 003 |
| 005 | Add entry to .claude/CHANGELOG.md documenting v2 in-progress markers | :white_check_mark: | Main-Only (.claude/) | 004 |

**Phase gate**: MSAL regression test still passes after PR merge. No agent or human should follow stale guidance during the remaining phases.

---

## Phase A — Core library rebuild (7 tasks, ~11h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 010 | Define AuthStrategy interface + token result types; preserve MSAL config logic by literal lift | :white_check_mark: | No | Phase 0 |
| 011 | Implement BrowserMsalStrategy (Dataverse PCFs + Code Pages path) | :black_square_button: | A-Parallel-1 | 010 |
| 012 | Implement in-memory cache wrapper with JWT exp validation (5-min buffer) | :black_square_button: | A-Parallel-1 | 010 |
| 013 | Implement useAuth() hook returning {isAuthenticated, getAccessToken, authenticatedFetch, tenantId, logout} | :black_square_button: | No | 011, 012 |
| 014 | Implement logout() API: MSAL logout + cache clear + BroadcastChannel invalidation + POST /api/auth/logout endpoint | :black_square_button: | A-Parallel-2 | 013 |
| 015 | Add version stamp on SpaarkeAuthProvider + BroadcastChannel invalidation listener | :black_square_button: | A-Parallel-2 | 013 |
| 016 | Write strategy + cache unit tests | :black_square_button: | A-Parallel-2 | 011, 012 |

**Phase gate**: `@spaarke/auth` v2 builds cleanly. All public exports use function-based contract. `accessToken: string` does not appear in public types. MSAL regression test passes.

---

## Phase B — Consumer migration (11 tasks, ~16h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 020 | Migrate AiSessionProvider context to function-based API | :black_square_button: | No | Phase A |
| 021 | Migrate SpaarkeAi App.tsx (stop snapshotting token in useEffect) | :black_square_button: | No | 020 |
| 022 | Migrate SpaarkeAi panes (ConversationPane, WelcomePanel, ChatHistoryPanel, WorkspaceLandingWidget, ChatPanel, FeedbackButtons, ThreePaneShell) | :black_square_button: | B-Parallel-1 | 020 |
| 023 | Refactor SprkChat API: drop accessToken prop, require authenticatedFetch + getAccessToken; update 3 hooks (useChatSession, useChatPlaybooks, useChatContextMapping); useSseStream calls getAccessToken() per-stream-open | :black_square_button: | B-Parallel-1 | 020 |
| 024 | Migrate PlaybookBuilder Code Page (aiPlaybookService.ts, dataverseClient.ts, templateStore.ts) | :black_square_button: | B-Parallel-2 | 023 |
| 025 | Migrate DocumentRelationshipViewer Code Page (VisualizationApiService.ts) | :black_square_button: | B-Parallel-2 | 023 |
| 026 | Migrate AnalysisWorkspace Code Page (analysisApi.ts and remaining paths) | :black_square_button: | B-Parallel-2 | 023 |
| 027 | Migrate SemanticSearch Code Page + External SPA (MsalAuthProvider, authInit, bff-client) | :black_square_button: | B-Parallel-2 | 023 |
| 028 | Verify + rebuild all PCFs (UniversalDatasetGrid, UniversalQuickCreate, SpeDocumentViewer, others) | :black_square_button: | B-Parallel-2 | Phase A |
| 029 | Update bffDataServiceAdapter docs/example to function-based pattern | :black_square_button: | B-Parallel-2 | 023 |
| 030 | Delete duplicate buildBffApiUrl from PCF environmentVariables.ts; import from @spaarke/auth | :black_square_button: | B-Parallel-2 | Phase A |

**Phase gate**: No `accessToken: string` or `token: string` props in `src/client/`. All consumers compile + pass tests. MSAL regression test still passes.

---

## Phase C — Server hardening (10 tasks, ~19h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 040 | Rotate AzureAd__ClientSecret + AgentToken__ClientSecret; convert App Service config to Key Vault references; coordinate App Service restart | :black_square_button: | No (deploy) | Phase A |
| 041 | Migrate Graph app-only from ClientSecretCredential to DefaultAzureCredential (managed identity) | :black_square_button: | C-Parallel-1 | 040 |
| 042 | Migrate Dataverse service identity from ClientSecretCredential to DefaultAzureCredential (multiple job handlers) | :black_square_button: | C-Parallel-1 | 040 |
| 043 | Remove /debug/* endpoints (7 routes including /debug/token); #if DEBUG guard or delete entirely | :black_square_button: | C-Parallel-2 | none |
| 044 | Replace webhook clientState with HMAC-SHA256 signature validation; remove DEVELOPMENT_MODE bypass | :black_square_button: | C-Parallel-2 | none |
| 045 | Formalize named API key auth scheme; replace ad-hoc header validation on /api/admin/builder-scope/import, /api/ai/rag/* | :black_square_button: | C-Parallel-2 | none |
| 046 | Add idempotency guard in PostConfigure JwtBearerOptions (AuthorizationModule.cs:29-48) | :black_square_button: | C-Parallel-2 | none |
| 047 | Fix appsettings.template.json: TenantId common → #{TENANT_ID}#; parameterize Copilot audience UUID | :black_square_button: | C-Parallel-2 | none |
| 048 | Audit logging middleware: enrich every authenticated request with oid, appid, obo, tenantId, correlationId | :black_square_button: | C-Parallel-2 | none |
| 049 | Rate limiting policies on anonymous + API key endpoints | :black_square_button: | C-Parallel-2 | 045 |

**Phase gate**: No plain-text secrets in App Service config. No client secrets in code (managed identity for all server outbound). No /debug/* endpoints reachable. Webhook signatures verified. API key scheme is a named registration.

---

## Phase D — Reasonable security hardening (5 tasks, ~11h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 060 | Add CSP + Trusted Types middleware on BFF; strict policy script-src 'self', no inline, no eval | :black_square_button: | D-Parallel-1 | Phase C |
| 061 | Enable Continuous Access Evaluation (CAE) on Microsoft.Identity.Web | :black_square_button: | D-Parallel-1 | none |
| 062 | Identity claims hardening: grep + replace email/upn used as canonical identity with oid (Azure AD object ID) | :black_square_button: | D-Parallel-1 | none |
| 063 | Step-up auth scaffolding: [RequiresStepUp] attribute + middleware; apply to 2-3 sensitive ops as proof points | :black_square_button: | D-Parallel-1 | none |
| 064 | Refresh token rotation integration test (confirms MSAL issues new RT on each refresh) | :black_square_button: | D-Parallel-1 | Phase A |

**Phase gate**: CSP headers present in responses. CAE configured. Audit log writes use oid everywhere. Step-up middleware returns 401 with claims challenge on tagged endpoints.

---

## Phase E — CI / Bundling hygiene (3 tasks, ~7h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 070 | Gitleaks GitHub Action workflow; blocks merge on detection | :black_square_button: | E-Parallel-1 | none |
| 071 | Auth regression test pack: Playwright script that runs spaarke-sso-binding ritual against synthetic consumer | :black_square_button: | E-Parallel-1 | Phase A |
| 072 | Dependabot config for npm + nuget | :black_square_button: | E-Parallel-1 | none |

**Phase gate**: Every PR runs gitleaks + auth regression test. Dependabot opens PRs for security updates.

---

## Phase B4 — Office Add-ins consolidation (4 tasks, ~5h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 080 | Implement OfficeNaaStrategy in @spaarke/auth; deprecate parallel authConfig.ts | :black_square_button: | No | Phase A |
| 081 | Fix Office Add-in SseClient.ts:78 staleness (EventSource cannot auto-retry on 401) | :black_square_button: | B4-Parallel-1 | 080 |
| 082 | Fix Office Add-in useSaveFlow.ts:526,864 staleness (snapshot from hook closure) | :black_square_button: | B4-Parallel-1 | 080 |
| 083 | Rebuild + deploy Outlook + Word Add-in bundles | :black_square_button: | No | 081, 082 |

**Phase gate**: Office Add-ins use @spaarke/auth useAuth() API. No standalone authConfig.ts MSAL setup. Add-in bundles rebuilt and deployed.

---

## Phase F — Engineering canonical docs (5 tasks, ~4h)

| # | Task | Status | Parallel Group | Dependencies |
|---|------|--------|----------------|--------------|
| 090 | Draft ADR-027: Spaarke Auth Architecture | :black_square_button: | Main-Only (.claude/) | All prior phases |
| 091 | Update .claude/patterns/auth/spaarke-sso-binding.md: cascade section retired; INV-1..INV-7 stay; point to ADR-027 | :black_square_button: | Main-Only (.claude/) | 090 |
| 092 | Update .claude/constraints/auth.md: function-based contract MUST rule; cascade MUST rules retired | :black_square_button: | Main-Only (.claude/) | 090 |
| 093 | Write docs/guides/auth-deployment-setup.md: new-environment setup checklist | :black_square_button: | F-Parallel-1 | 090 |
| 094 | Retire DEPRECATED-* files (delete or move to .claude/archive/); update CHANGELOG | :black_square_button: | Main-Only (.claude/) | 091, 092 |

**Phase gate**: ADR-027 approved. Patterns and constraints aligned with v2. Deployment guide complete and mechanically followable.

---

## File coverage from audit §8.1

All 30+ code files identified in the audit are addressed. Cross-reference for verification:

| Audit file | Task |
|---|---|
| `src/client/shared/Spaarke.Auth/src/strategies/BridgeStrategy.ts`, `XrmStrategy.ts`, `tokenBridge.ts` | 010 (delete during library refactor) |
| `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts` | 010-013 (preserve MSAL config; rewrite cascade) |
| `src/client/shared/Spaarke.Auth/src/types.ts` | 010 |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | 020 |
| `src/solutions/SpaarkeAi/src/App.tsx` | 021 |
| `src/solutions/SpaarkeAi/src/components/{ThreePaneShell, ChatHistoryPanel, ChatPanel, WelcomePanel, WorkspaceLandingWidget, FeedbackButtons}.tsx` | 022 |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | 022 |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/{SprkChat.tsx, types.ts, hooks/useChatSession.ts, hooks/useChatPlaybooks.ts, hooks/useChatContextMapping.ts}` | 023 |
| `src/client/code-pages/PlaybookBuilder/src/services/aiPlaybookService.ts` | 024 |
| `src/client/code-pages/PlaybookBuilder/src/services/dataverseClient.ts` | 024 |
| `src/client/code-pages/PlaybookBuilder/src/stores/templateStore.ts` | 024 |
| `src/client/code-pages/DocumentRelationshipViewer/src/services/VisualizationApiService.ts` | 025 |
| `src/client/code-pages/AnalysisWorkspace/src/services/analysisApi.ts` | 026 |
| `src/client/code-pages/SemanticSearch/src/services/auth/MsalAuthProvider.ts` | 027 |
| `src/client/code-pages/SemanticSearch/src/services/authInit.ts` | 027 |
| `src/client/external-spa/src/auth/bff-client.ts` | 027 |
| `src/client/pcf/UniversalDatasetGrid/control/*`, `UniversalQuickCreate`, `SpeDocumentViewer` | 028 |
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/bffDataServiceAdapter.ts` | 029 |
| `src/client/pcf/shared/utils/environmentVariables.ts` | 030 |
| `src/client/office-addins/shared/auth/{authConfig.ts, DialogAuthService.ts, NaaAuthService.ts}` | 080 |
| `src/client/office-addins/shared/api/OfficeApiClient.ts` | 080 |
| `src/client/office-addins/shared/taskpane/services/SseClient.ts` | 081 |
| `src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts` | 082 |
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` | 040 + 047 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/DebugEndpointExtensions.cs` | 043 |
| `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` (`/debug/{documentId}`) | 043 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | 041 |
| Multiple Dataverse job handlers using `ClientSecretCredential` | 042 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` (PostConfigure) | 046 |
| Communication + Email webhook endpoints | 044 |
| `/api/admin/builder-scope/import`, `/api/ai/rag/*` API key endpoints | 045 |

---

## Execution guide

**Sequential mode** (single agent, safest):
- Execute tasks in numerical order, respecting `Dependencies` column
- Each task via `task-execute` skill (mandatory per root CLAUDE.md §4)

**Parallel mode** (multiple agents per phase batch):
- Within a phase, group tasks by `Parallel Group` column
- Tasks in the same group can run concurrently
- Use ONE Claude Code message with MULTIPLE `Skill` tool invocations (one per task) — see root CLAUDE.md §4 "Parallel Task Execution"
- Sub-agents can NOT write to `.claude/` paths (root CLAUDE.md §3) — tasks marked `Main-Only` run in main session only

**Phase gates**: after each phase completes, run the MSAL binding regression test from [`spaarke-sso-binding.md`](../../../.claude/patterns/auth/spaarke-sso-binding.md#verification-after-changes). PASS is required before moving to next phase.
