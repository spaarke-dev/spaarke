# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: 🎉 **PHASE C SIGNED OFF** (code shipped, deployed, OBO + MI smoke checks PASS). Phase D NEXT.
> **Active Phase**: C done. Phase D (CSP / CAE / claims hardening / step-up auth, 060-064) NEXT.
> **Last Updated**: 2026-05-19 (Phase C deploy + smoke)

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Phase A + B + C all done. 32/49 tasks (65%). BFF deployed to spe-api-dev-67e2xz with all Phase C hardening live + verified via OBO + MI smoke tests. |
| **Next Action** | `continue` → Phase D task 060 (CSP + Trusted Types middleware on BFF). Phase D = client-affecting (CSP can break iframes) → MSAL browser regression required after deploy. |

## Phase C — what shipped (commits 59a9246f + c4bb4a4e + 2bce868b)

| Task | Outcome |
|---|---|
| 040 | DEFERRED — dev env, low blast radius |
| 041 | Graph → DefaultAzureCredential (default-off opt-in via `Graph__ManagedIdentity__Enabled`) |
| 042 | Dataverse → DefaultAzureCredential (13 files; base-class cascade) |
| 043 | 11 debug endpoints removed (DebugEndpointExtensions.cs DELETED) |
| 044 | HMAC-SHA256 webhook validation + DEVELOPMENT_MODE bypass removed |
| 045 | Named API key auth scheme (BuilderAdmin + Rag) |
| 046 | PostConfigure idempotency (Interlocked.CompareExchange + stacked-handler fix) |
| 047 | appsettings.template.json placeholders (TenantId + Copilot UUID) |
| 048 | Audit logging middleware (oid + appid + obo + tenantId + correlationId) |
| 049 | Rate limiting (3 new policies + fixed latent oid-keyed bug) |

## Operator actions performed (2026-05-19)

1. ✅ Granted 11 Microsoft Graph + SharePoint app role assignments to MI SP `56ae2188-c978-4734-ad16-0bc288973f20` (replicated from BFF app reg `1e40baad-...`)
2. ✅ MI already an Application User on spaarkedev1 since 2025-10-20 (`# spe-api-dev-67e2xz`, systemuserid `ebfacf6d-...`) with System Administrator — nothing to do
3. ✅ Generated + set 2 webhook signing keys on App Service (64 chars base64; plaintext for now)
4. ✅ Set `Graph__ManagedIdentity__Enabled=true` (combined with #3 into single restart)
5. ✅ Deployed Phase C code via `Deploy-BffApi.ps1` — hash-verify caught Windows file-lock failure mode + auto-recovered via Kudu zipdeploy
6. ✅ OBO smoke check: `/api/ai/chat/context-mappings/standalone` returned 200 + JSON
7. ✅ MI Dataverse smoke check: `/healthz/dataverse/doc/<fake-id>` returned expected "not found" via the new `DefaultAzureCredential` path

## Carryovers (non-blocking)

### From Phase B (still open)
- Task 031 deferred — 3 pre-existing wizard payload bugs (CreateMatter N:N, CreateProject + CreateWorkAssignment createRecord)
- SemanticSearch Code Page blocked by `@lexical/react` `.prod.mjs` webpack issue
- 4 PCFs missing `eslint` devDep
- Deploy-SpaarkeAi.ps1 CREATE branch is buggy
- Duplicate `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js`
- `Spaarke.UI.Components/src/services/document-upload/*` + `useAiSummary.ts` still build raw Bearer in implementation
- SpeDocumentViewer residual dead `accessToken: string` props
- `@spaarke/ui-components` jest tests can't run
- DocumentUploadWizard `bffTokenProvider` prop still wired through wizard tree
- PlaybookBuilder/services/authService.ts deprecated, zero consumers
- SpaarkeAi blob: ERR_FILE_NOT_FOUND (minor)

### From Phase C (new)
- **Task 040 deferred** — dev env, low urgency. Revisit at prod-readiness.
- **Webhook sender reconfig deferred** — Communication + Email webhook endpoints return 401 until Microsoft Graph subscription + Dataverse Service Endpoint are reconfigured to sign with the new keys. Dev doesn't actively use webhooks.
- **`/healthz/dataverse/doc/{id}` endpoint** — still live; debug-ish but lives under `/healthz/`. Outside task 043's strict scope.
- **spaarke-demo BFF Application User** — BFF + now MI not added there. If BFF starts calling demo's Dataverse, add MI as Application User.

## D-AUTH-7 exception sites (canonical list for Phase E task 070)

8 sites — same as Phase B carryover (no Phase C changes).

## Memory entries (6 total)

1. `feedback-dual-env-deploys`
2. `feedback-name-collision-in-consumer-authinit`
3. `feedback-proactive-parallel-dispatch`
4. `feedback-third-party-sdk-accesstoken-is-ok`
5. `project-auth-v2-baseline-msal-bug`
6. `feedback-question-urgency-for-dev-only-infra-tasks` ← added Phase C

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0: 5/5 ✅
- Phase A: 7/7 ✅
- Phase B: 11/11 ✅ + 1 deferred (031)
- Phase C: 9/10 ✅ + 1 deferred (040)
- Overall: **32/49 tasks (65%)** — Phase D/E/F remaining
- BFF deployed: `spe-api-dev-67e2xz` Phase C live + smoke-tested
- Client surfaces (Phase B): 6 PCFs + 13 Code Pages on BOTH envs

## Next: Phase D (CSP / CAE / security 060-064) — 5 tasks, ~11h

- 060 (D-Parallel-1, depends Phase C): Add CSP + Trusted Types middleware on BFF
- 061 (D-Parallel-1): Enable Continuous Access Evaluation (CAE) on Microsoft.Identity.Web
- 062 (D-Parallel-1): Identity claims hardening — replace email/upn with oid as canonical identity
- 063 (D-Parallel-1): Step-up auth scaffolding (`[RequiresStepUp]` + middleware)
- 064 (D-Parallel-1): Refresh token rotation integration test

**Per project CLAUDE.md risk-tier**: Phase D affects browser auth (CSP can block MSAL iframes; CAE forces re-auth). MSAL browser regression IS required after Phase D deploy.

After Phase D: Phase E (CI 070-072), Phase F (docs / ADR-027), B4 (Office Add-ins), B-addendum 031.
