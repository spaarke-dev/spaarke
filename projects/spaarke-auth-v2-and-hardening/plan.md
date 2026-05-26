# Implementation Plan — Spaarke Auth v2 + Hardening

> **Authoritative scope**: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) §5.
> **Total estimate**: ~68 hours across 8 phases. With parallelization: ~5-6 working days.

## Architecture summary

See [architecture.md](architecture.md) and [audit doc §4](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md#4-target-architecture). Key principles:

- **Function-based auth contract**: no `token: string` in public API. `authenticatedFetch` + `getAccessToken` (SSE edge case) + `useAuth()` hook.
- **Pluggable strategies**: `BrowserMsalStrategy` (Dataverse) + `OfficeNaaStrategy` (Office Add-ins). Drops Bridge + Xrm strategies.
- **MSAL.localStorage** for cross-tab/iframe sharing. **BroadcastChannel for invalidation only**.
- **MSAL invariants INV-1..INV-7** preserved by literal lift from current `SpaarkeAuthProvider`.
- **Per-tenant deployment** is the threat model. Each customer's tenant. No cross-customer patterns.

## Phase ordering

Phases run mostly sequentially with some parallelization:

```
Pre-flight (sequential)
  ↓
Phase A — Library rebuild (sequential within, ~11h)
  ↓
Phase B — Consumer migration (parallel-rich, ~16h)
Phase C — Server hardening (parallel with B, ~19h)
Phase D — Security hardening (parallel with B/C, ~11h)
Phase E — CI / Bundling (parallel with B/C/D, ~7h)
  ↓
Phase B4 — Office Add-ins (after A complete, ~5h)
  ↓
Phase F — Docs + ADR (~4h)
```

Single coherent PR (recommended) or three logical cycles (B, then C/D/E, then B4/F).

## Phase 0 — Pre-flight conflict markers (5 tasks, ~90 min)

**Purpose**: Prevent agents and humans from following stale auth guidance during the refactor. Three-layer enforcement: filename surgery + STOP banners + project CLAUDE.md prohibition.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 001 | Rename DEPRECATED files (`msal-client.md`, `spaarke-auth-initialization.md`) + update INDEX.md references | No (.claude/) | 15 min |
| 002 | Apply STOP banners to 5 partially-superseded pattern + constraint + architecture docs | No (.claude/) | 30 min |
| 003 | Add prohibition section to project CLAUDE.md (already present in this file — verify) | No (.claude/) | 10 min |
| 004 | Update root CLAUDE.md §15 Pointers to reference AUDIT-FINDINGS-AUTH-SYSTEM.md | No (.claude/) | 10 min |
| 005 | Add entry to .claude/CHANGELOG.md documenting v2 in-progress markers | No (.claude/) | 5 min |

**Acceptance**: All affected pattern files have STOP banners. `DEPRECATED-` prefixed files exist. Project + root CLAUDE.md updated. Single PR titled "auth-v2-pre-flight-markers".

## Phase A — Core library rebuild (7 tasks, ~11h)

**Purpose**: Rebuild `@spaarke/auth` with pluggable strategy pattern, `useAuth()` hook, function-based contract. Preserve MSAL invariants INV-1..INV-7 by literal code lift.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 010 | Define `AuthStrategy` interface + token result types; lift MSAL config logic unchanged | No | 2h |
| 011 | Implement `BrowserMsalStrategy` (Dataverse PCFs + Code Pages path) | After 010 | 2h |
| 012 | Implement in-memory cache wrapper with JWT exp validation (5-min buffer) | After 010, parallel with 011 | 2h |
| 013 | Implement `useAuth()` hook returning `{isAuthenticated, getAccessToken, authenticatedFetch, tenantId, logout}` | After 011 + 012 | 2h |
| 014 | Implement `logout()` API: MSAL logout + cache clear + BroadcastChannel invalidation + POST /api/auth/logout | After 013 | 1h |
| 015 | Add version stamp + BroadcastChannel invalidation listener | After 013 | 1h |
| 016 | Write strategy + cache unit tests | After 011 + 012 | 1h |

**Acceptance**: `@spaarke/auth` v2 builds cleanly. MSAL regression test passes (INV-1..INV-7 verified). All public exports use function-based contract. `accessToken: string` does not appear in public types.

## Phase B — Consumer migration (11 tasks, ~16h)

**Purpose**: Migrate all ~30 client consumers from `accessToken: string` snapshot pattern to function-based `useAuth()` + `authenticatedFetch`.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 020 | Migrate `AiSessionProvider` context to function-based API | After Phase A | 2h |
| 021 | Migrate SpaarkeAi `App.tsx` to stop snapshotting | After 020 | 1h |
| 022 | Migrate SpaarkeAi panes: ConversationPane, WelcomePanel, ChatHistoryPanel, WorkspaceLandingWidget, ChatPanel, FeedbackButtons, ThreePaneShell | After 020, parallel with 021 | 3h |
| 023 | Refactor `SprkChat` API: drop `accessToken` prop, require `authenticatedFetch` + `getAccessToken`; update 3 hooks (useChatSession, useChatPlaybooks, useChatContextMapping); `useSseStream` calls `getAccessToken()` per-stream-open | After 020 | 2h |
| 024 | Migrate PlaybookBuilder Code Page (aiPlaybookService.ts, dataverseClient.ts, templateStore.ts) | After 023 | 2h |
| 025 | Migrate DocumentRelationshipViewer Code Page (VisualizationApiService.ts) | After 023, parallel with 024 | 1h |
| 026 | Migrate AnalysisWorkspace Code Page (analysisApi.ts and remaining paths) | After 023, parallel with 024 | 2h |
| 027 | Migrate SemanticSearch Code Page + External SPA | After 023, parallel with 024-026 | 1.5h |
| 028 | Verify + rebuild all PCFs (UniversalDatasetGrid, UniversalQuickCreate, SpeDocumentViewer, others) | After 020 | 1h |
| 029 | Update `bffDataServiceAdapter` docs to function-based example | After 023 | 0.5h |
| 030 | Delete duplicate `buildBffApiUrl` from PCF `environmentVariables.ts`; import from `@spaarke/auth` | After Phase A | 0.5h |

**Acceptance**: No `accessToken: string` props anywhere in `src/client/`. All consumers compile + pass tests. MSAL regression test still passes.

## Phase C — Server hardening (10 tasks, ~19h)

**Purpose**: Rotate secrets to Key Vault, migrate to managed identity, remove debug endpoints, formalize API key auth, HMAC webhook signatures, audit middleware.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 040 | Rotate `AzureAd__ClientSecret` + `AgentToken__ClientSecret`; convert App Service config entries to Key Vault references; brief App Service restart | No (deploy) | 2h |
| 041 | Migrate Graph app-only auth from `ClientSecretCredential` to `DefaultAzureCredential` (managed identity) | Yes | 2h |
| 042 | Migrate Dataverse service identity from `ClientSecretCredential` to `DefaultAzureCredential` (multiple job handlers) | Yes | 2h |
| 043 | Remove `/debug/*` endpoints (7 routes including `/debug/token`); compile-time exclude or delete entirely | Yes | 1h |
| 044 | Replace webhook `clientState` string match with HMAC-SHA256 signature validation; remove `DEVELOPMENT_MODE` bypass | Yes | 3h |
| 045 | Formalize named API key auth scheme; replace ad-hoc header validation on `/api/admin/builder-scope/import`, `/api/ai/rag/*` | Yes | 3h |
| 046 | Add idempotency guard in `PostConfigure<JwtBearerOptions>` (AuthorizationModule.cs:29-48) | Yes | 1h |
| 047 | Fix `appsettings.template.json`: `TenantId: "common"` → `#{TENANT_ID}#`; parameterize Copilot audience UUID | Yes | 1h |
| 048 | Audit logging middleware: enrich every authenticated request with `oid`, `appid`, `obo`, `tenantId`, `correlationId` into structured log scope | Yes | 3h |
| 049 | Rate limiting policies on anonymous + API key endpoints | Yes | 2h |

**Acceptance**: No plain-text secrets in App Service config. No client secrets in code (managed identity for all server outbound). No `/debug/*` endpoints reachable. Webhook signatures verified. API key scheme is a named `AddAuthentication("ApiKey", ...)` registration.

## Phase D — Reasonable security hardening (5 tasks, ~11h)

**Purpose**: Defense-in-depth for enterprise expectations. CSP, CAE, claims hardening, step-up auth scaffolding.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 060 | Add CSP + Trusted Types middleware on BFF; strict policy `script-src 'self'`, no inline, no eval | Yes (server) | 3h |
| 061 | Enable Continuous Access Evaluation (CAE) on Microsoft.Identity.Web | Yes (server) | 2h |
| 062 | Identity claims hardening audit: grep codebase for `email`/`upn`/`preferred_username` claims used as canonical identity; replace with `oid` | Yes | 3h |
| 063 | Step-up auth scaffolding: `[RequiresStepUp]` attribute + middleware; apply to 2-3 sensitive ops as proof points | Yes (server) | 2h |
| 064 | Refresh token rotation integration test (confirms MSAL issues new RT on each refresh) | Yes | 1h |

**Acceptance**: CSP headers present in responses. CAE configured. Audit log writes use `oid` everywhere. Step-up middleware returns 401 with claims challenge on tagged endpoints.

## Phase B4 — Office Add-ins consolidation (4 tasks, ~5h)

**Purpose**: Integrate Office Add-ins NAA auth into `@spaarke/auth` as a pluggable strategy. Fix staleness bugs in SseClient + SaveFlow.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 080 | Implement `OfficeNaaStrategy` in `@spaarke/auth`; deprecate parallel `authConfig.ts` | After Phase A | 2h |
| 081 | Fix Office Add-in `SseClient.ts:78` staleness (EventSource cannot auto-retry on 401) | After 080 | 1h |
| 082 | Fix Office Add-in `useSaveFlow.ts:526,864` staleness (snapshot from hook closure) | After 080, parallel with 081 | 1h |
| 083 | Rebuild + deploy Outlook + Word Add-in bundles | After 080-082 | 1h |

**Acceptance**: Office Add-ins use `@spaarke/auth` `useAuth()` API. No standalone `authConfig.ts` MSAL setup. Add-in bundles rebuilt and deployed.

## Phase E — CI / Bundling hygiene (3 tasks, ~7h)

**Purpose**: Prevent regression and secret leaks via automated checks.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 070 | Gitleaks GitHub Action workflow; blocks merge on detection | Yes | 2h |
| 071 | Auth regression test pack: Playwright script that runs the spaarke-sso-binding ritual against a synthetic consumer | Yes | 4h |
| 072 | Dependabot config for npm + nuget | Yes | 1h |

**Acceptance**: Every PR runs gitleaks + auth regression test. Dependabot opens PRs for security updates.

## Phase F — Engineering canonical docs (5 tasks, ~4h)

**Purpose**: Document the v2 architecture so future agents and humans use the correct patterns.

| ID | Task | Parallel-safe | Est. |
|---|---|---|---|
| 090 | Draft `ADR-027: Spaarke Auth Architecture` | No (.claude/) | 2h |
| 091 | Update `.claude/patterns/auth/spaarke-sso-binding.md`: cascade section retired; INV-1..INV-7 stay; point to ADR-027 | No (.claude/) | 0.5h |
| 092 | Update `.claude/constraints/auth.md`: function-based contract MUST rule; cascade MUST rules retired | No (.claude/) | 0.5h |
| 093 | Write `docs/guides/auth-deployment-setup.md`: new-environment setup checklist (4 client env vars + 8 server settings + Key Vault) | No | 0.5h |
| 094 | Retire `DEPRECATED-*` files; update CHANGELOG | No (.claude/) | 0.5h |

**Acceptance**: ADR-027 approved. Patterns and constraints aligned with v2. Deployment guide complete and mechanically followable.

## Pre-PR checklist

- [ ] All 49 tasks marked complete in TASK-INDEX.md
- [ ] MSAL binding regression test passes (clear-and-reopen ritual)
- [ ] Idle-then-resume test passes (>80 min idle, return, chat succeeds)
- [ ] No `accessToken: string` or `token: string` in any public API (grep verified)
- [ ] No plain-text secrets in App Service config (`az webapp config appsettings list` verified)
- [ ] Gitleaks passes in CI
- [ ] All ~30 consumer bundles rebuilt + deployed
- [ ] ADR-027 reviewed and approved

## Phasing recommendation

**Option 1 — Single coherent PR** (recommended): all phases in one push. ~5-6 working days. Minimizes INV-8 risk (consumers all in sync).

**Option 2 — Three logical cycles**:
- Cycle 1: Pre-flight + Phase A + Phase B (~21h) — fixes the user-visible 401
- Cycle 2: Phases C + D + E (~37h) — server hardening + CI
- Cycle 3: Phase B4 + Phase F (~9h) — Office Add-ins + docs

Each cycle is independently shippable but you pay the consumer-rebuild cost twice.
