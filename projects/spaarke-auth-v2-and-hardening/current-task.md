# Current Task — Spaarke Auth v2 + Hardening (CONTEXT RECOVERY)

> **Project**: spaarke-auth-v2-and-hardening
> **Branch**: `work/spaarke-auth-v2-and-hardening`
> **Worktree**: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
> **Last Updated**: 2026-05-19 (post-compaction context handoff)
> **Status**: Phase A + B + C + B4 + F **all CODE COMPLETE**; deployment guide updated; awaiting Exchange policy propagation + final commit + merge

---

## 🎯 NEXT ACTION (cold start: type `continue`)

The user's two most recent explicit requests have been completed in code:

1. ✅ **`docs/guides/auth-deployment-setup.md`** — new §7 "Exchange Online — ApplicationAccessPolicy for mailbox access" added covering scope group, dual-principal policy creation (BFF app reg + MI), `Test-ApplicationAccessPolicy`, propagation note, runbook for adding mailboxes. Smoke-test §9d added. Cheat sheet updated.
2. ✅ **This file** — rewritten as comprehensive context-recovery.

**Immediate next steps on resumption (pick one):**

| Option | When to choose | Action |
|---|---|---|
| **A. Verify propagation** | ≥20 min has passed since 2026-05-19 Exchange policy creation | `az webapp log tail --name spe-api-dev-67e2xz --resource-group rg-spe-dev` and grep for `InboundPollingBackupService` / `ErrorAccessDenied` — should be clear |
| **B. Commit Phase F + B4 + docs + bug fixes** | User says "commit" or "merge" | Single conventional commit covering ADR-028, sso-binding, constraints, archive, deployment guide, OfficeNaaStrategy, SseClient, useSaveFlow, sprk_DocumentOperations.js, POML status updates |
| **C. Investigate Bug B (RagService filter)** | User wants the AI Search "Invalid expression" error fixed | Pre-existing schema mismatch; see "Outstanding Bugs" §B below |
| **D. Investigate Bug C (SendToIndex)** | User has retested SendToIndex with deployed `sprk_DocumentOperations.js` fix and pasted the actual `errorMessage` | Diagnose root cause from real server message |
| **E. Spin up auth-v3-hardening** | After merge to master | Project pipeline for Phase D (CSP+CAE+claims+step-up) + Phase E (CI: gitleaks + regression Playwright + Dependabot) |

---

## Pre-compaction conversation context (what was happening)

Session started with `continue` after `/clear`. Major events in compaction order:

1. **Phase B (consumer migrations)** — Waves 1–5 covering 6 PCFs + 13 Code Pages + 3 standalone JS bug fixes, deployed dual-env (spaarkedev1 + spaarke-demo). Signed off.
2. **Phase C (server hardening)** — 9/10 tasks; 040 deferred ("dev env, low blast radius"). Signed off.
3. **User insisted Phase F (docs) before merge**: *"We do need to update all of our Auth related documentation including ADRs, patterns, constraints, architecture docs etc."*
4. **User insisted Phase D become its own `auth-v3-hardening` project**.
5. **User reported "Failed to Index" error** on SendToIndex; debug revealed (a) `sprk_DocumentOperations.js` was reading non-existent `data.results[0].error` field instead of real `errorMessage`; (b) BFF `InboundPollingBackupService` was logging 403s after MI was enabled.
6. **User pushed back on flag-revert "quick fix"**: *"but isn't this quick fix short circuiting the entire reason we did this Auth project?"* → I flipped `Graph__ManagedIdentity__Enabled` back to `true` and we set up Exchange ApplicationAccessPolicy properly.
7. **Phase F + B4 + bug fixes all completed** but **NOT YET COMMITTED**.
8. **Operator action**: Exchange policies created for BFF app reg AND MI; `Test-ApplicationAccessPolicy` returned `Granted` for testuser1@spaarke.com; awaiting up-to-30-min propagation.
9. **Final user requests**: add email-access section to deployment guide + create this context file.

---

## What is committed (master ← work/spaarke-auth-v2-and-hardening, NOT YET merged)

| Commit | Scope |
|---|---|
| `939e0392` | docs(auth-v2): Phase C SIGN-OFF — deployed + smoke-tested |
| `c4bb4a4e` | feat(auth-v2): Phase C Wave 2 — tasks 041, 042, 049 |
| `59a9246f` | feat(auth-v2): Phase C Wave 1 — server hardening tasks 043–048 |
| `33c91fe6` | feat(auth-v2): Phase B SIGN-OFF — Waves 3–5 + bug fixes + dual-env deploys |
| `1cbb299b` | feat(auth-v2): Phase B Wave 2 — 5 consumers migrated (024,025,026,027,029) |
| `918b0830` | feat(auth-v2): Phase B Wave 1 — 5 consumers migrated (021,022,023,028,030) |
| `6b6106f6` | feat(auth-v2): AiSessionProvider → function-based contract (task 020) |
| `48788c1f` | fix(auth-v2): hotfix v2 — name collision in SpaarkeAi authInit + defensive guard |
| `811e98b5` | fix(auth-v2): popup-on-startup hotfix — 4-part fix for task 011 incomplete |
| `3e46f0ad` | feat(auth-v2): Phase A finale — strategy/cache tests + cleanup (task 016) |

---

## What is UNCOMMITTED (work staged in worktree, ready to commit)

### Phase F — Documentation (4 tasks, all complete in source)

| File | Change | Task |
|---|---|---|
| `.claude/adr/ADR-028-spaarke-auth-architecture.md` | **NEW** (~150 lines) — renumbered from ADR-027 (slot taken by subscription-isolation ADR); canonical concise ADR for Spaarke Auth v2: function-based contract, MSAL invariants, MI, named API key schemes, HMAC webhooks, audit middleware | 090 |
| `.claude/patterns/auth/spaarke-sso-binding.md` | **MODIFIED** — removed STOP banner; retired 6-strategy cascade section; promoted INV-1..INV-8 as numbered list with rationale per invariant (added INV-4 sprk_TenantId env var primary, INV-5 UPN-not-display-name, INV-6 omit authority, INV-7 one PCA via getAuthProvider) | 091 |
| `.claude/constraints/auth.md` | **MODIFIED** — removed STOP banner; "Source ADRs: ADR-028 (canonical), ADR-003, ADR-004, ADR-008, ADR-009"; new Client v2 function-based contract section + Server Phase C hardening section | 092 |
| `.claude/adr/INDEX.md` | **MODIFIED** — added ADR-028 row + updated "Working with auth" guidance | 090 |
| `.claude/archive/2026-05-19/DEPRECATED-msal-client.md` | **NEW** (git mv) | 094 |
| `.claude/archive/2026-05-19/DEPRECATED-spaarke-auth-initialization.md` | **NEW** (git mv) | 094 |
| `.claude/patterns/auth/DEPRECATED-msal-client.md` | **DELETED** (moved to archive) | 094 |
| `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md` | **DELETED** (moved to archive) | 094 |
| `docs/guides/auth-deployment-setup.md` | **NEW** — full operator checklist; ~520 lines with the new §7 Exchange ApplicationAccessPolicy section, §9 smoke tests (5 tests incl. 9d EXO), updated Appendix A cheat sheet | 093 |

### Phase B4 — Office Add-ins (3 tasks, all complete in source)

| File | Change | Task |
|---|---|---|
| `src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts` | **NEW** (~340 lines) — implements `AuthStrategy`; NAA-first via `createNestablePublicClientApplication` with fallback to standard PCA; JWT exp + 5-min buffer symmetric with `BrowserMsalStrategy`; exposes `IOfficeNaaConfig` | 080 |
| `src/client/shared/Spaarke.Auth/src/strategies/AuthStrategy.ts` | **MODIFIED** — logout JSDoc replaced "TBD" with NAA-broker contract | 080 |
| `src/client/shared/Spaarke.Auth/src/index.ts` | **MODIFIED** — exports `OfficeNaaStrategy` + `IOfficeNaaConfig` | 080 |
| `src/client/office-addins/shared/taskpane/services/SseClient.ts` | **MODIFIED** — `SseClientOptions.accessToken: string` → `getAccessToken: AccessTokenGetter`; per-stream call; 401-retry reconnect (max 3); D-AUTH-7 justification comment at raw Bearer header | 081 |
| `src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts` | **MODIFIED** — removed `accessToken: string` from `pollJobStatus` + `startJobTracking`; inline `await getAccessToken()` at lines 530 and 894; 2 documented D-AUTH-7 exceptions at lines 539 and 895 | 082 |
| `src/client/office-addins/shared/auth/{authConfig,NaaAuthService,DialogAuthService,index}.ts` | **MODIFIED** (deprecation comments only; not deleted — deletion deferred until 083 swaps entry points) | 080 |
| `src/client/office-addins/package-lock.json` | **MODIFIED** (touched by 080 npm install — verify before commit) | 080 |

### Bug fixes (separate from Phase F / B4)

| File | Change |
|---|---|
| `src/client/webresources/js/sprk_DocumentOperations.js` | **MODIFIED** — changed `data.results[0].error` → `data.results[0].errorMessage` (real server field name); added per-result error iteration in partial-success path; **already deployed to both envs** |

### POML status updates (all moved to `completed`)

`090-draft-adr-027.poml` (notes: "renumbered to ADR-028"), `091-update-sso-binding-doc.poml`, `092-update-constraints-auth.poml`, `093-deployment-setup-guide.poml`, `094-retire-deprecated-files.poml`, `080-office-naa-strategy.poml`, `082-fix-save-flow-staleness.poml`. Also `tasks/TASK-INDEX.md` updated.

### Build artifacts (NOT to commit — `.gitignore` should cover these but currently in `git status -M`)

`deploy/api-publish/Spaarke.Core.dll/.pdb`, `Spaarke.Dataverse.dll/.pdb`, `Sprk.Bff.Api.deps.json/.dll/.exe/.pdb` — these are publish output from the BFF deploy script. They should NOT be in the commit; verify `.gitignore` or use `git restore deploy/api-publish/*` before staging.

---

## Operator actions performed 2026-05-19 (durable runbook entries)

1. **Granted 11 Microsoft Graph + SharePoint app role assignments** to MI SP `56ae2188-c978-4734-ad16-0bc288973f20` (replicated from BFF app reg `1e40baad-...`).
2. **Dataverse Application User for MI**: already existed on spaarkedev1 since 2025-10-20 (`# spe-api-dev-67e2xz`, systemuserid `ebfacf6d-...`) with System Administrator role.
3. **Webhook signing keys**: generated 2 × 64-char base64 keys, set on App Service as plain-text appSettings (Key Vault references deferred to v3).
4. **`Graph__ManagedIdentity__Enabled=true`** set on App Service.
5. **BFF code deployed** via `Deploy-BffApi.ps1` — hash-verify caught Windows file-lock failure mode + auto-recovered via Kudu zipdeploy.
6. **OBO smoke check**: `/api/ai/chat/context-mappings/standalone` returned 200 + JSON.
7. **MI Dataverse smoke check**: `/healthz/dataverse/doc/<fake-id>` returned expected "not found" via the new `DefaultAzureCredential` path.
8. **Exchange ApplicationAccessPolicy** (new, post-MI-enable):
   - Created `Spaarke Email Access` mail-enabled security group: `spaarke-central-email@spaarke.com`
   - Created `New-ApplicationAccessPolicy` for **BFF app reg** scoped to that group
   - Created `New-ApplicationAccessPolicy` for **MI appId `6bbcfa82-...`** scoped to that group
   - `Test-ApplicationAccessPolicy -Identity testuser1@spaarke.com -AppId <MI-appId>` returned `Granted`
   - **`Graph__ManagedIdentity__Enabled` re-enabled to `true`** (after policy creation)
   - App Service restarted; HTTP 200 on `/healthz`
9. **`sprk_DocumentOperations.js` field-name fix deployed** to both spaarkedev1 + spaarke-demo via `Deploy-WebResourceInline.ps1`.

---

## Outstanding Bugs / Loose Ends

### Bug A — InboundPollingBackupService 403s (in-flight, awaiting propagation)

- **Status**: Exchange policies created; awaiting up-to-30-min propagation per Microsoft docs.
- **Verification**: re-tail `az webapp log tail --name spe-api-dev-67e2xz --resource-group rg-spe-dev` ≥20 min after policy creation; should be clear of `ErrorAccessDenied` / `Access to OData is disabled`.
- **Authoritative live-check**: `Test-ApplicationAccessPolicy` returning `Granted` (already confirmed for testuser1@spaarke.com + MI). If still failing past 30 min, recheck §7c (both BFF and MI policies created) and §7d.

### Bug B — RagService `privilege_group_ids` filter (pre-existing, NOT Phase C regression)

- **Symptom**: `Azure.RequestFailedException: Invalid expression: Could not find a property named 'privilege_group_ids' on type 'search.document'`
- **Cause**: RagService search filter references a field that does not exist in the Azure AI Search index schema.
- **Scope**: Separate fix; either rename to actual field name or add `privilege_group_ids` to the index schema. Not auth-related.
- **Where to start**: search `src/server/api/Sprk.Bff.Api/Services/Ai/` for `privilege_group_ids`.

### Bug C — SendToIndex actual root cause (user re-testing)

- **Symptom**: PCF showed "Failed to Index please try again" with empty error detail.
- **First fix shipped**: `sprk_DocumentOperations.js` was reading the WRONG field (`data.results[0].error` instead of real server field `data.results[0].errorMessage`). After deploy, the actual server error will be visible.
- **Pending**: user retests SendToIndex with deployed fix → pastes `results[0].errorMessage` → root-cause diagnosis. Bug B may be related (RagService filter could be the underlying cause); confirm by inspecting actual server message.

### Carryover loose ends (non-blocking, deferred)

| Item | Notes |
|---|---|
| Task 031 (3 wizard payload bugs) | Pre-existing; CreateMatter N:N, CreateProject + CreateWorkAssignment createRecord; deferred per user. Not auth-related. |
| Task 040 deferred | Dev env, low blast radius. Moved to V3 Phase G (`spaarke-auth-v3-and-hardening` project). |
| Webhook sender reconfig deferred | Communication + Email webhooks return 401 until Microsoft Graph subscription + Dataverse Service Endpoint are reconfigured to sign with new keys. Dev doesn't actively use webhooks. |
| `/healthz/dataverse/doc/{id}` endpoint | Still live (debug-ish under `/healthz/`). Outside task 043's strict scope. |
| spaarke-demo BFF Application User | BFF + MI not added to spaarke-demo. If BFF starts calling demo Dataverse, add MI. |
| `Deploy-SpaarkeAi.ps1` CREATE branch | Buggy first-time create; `Deploy-WebResourceInline.ps1` is the workaround. |
| `Spaarke.UI.Components/src/services/document-upload/*` + `useAiSummary.ts` | Still build raw Bearer in implementation (D-AUTH-7 exception sites). Moved to V3 Phase H. |
| DocumentUploadWizard `bffTokenProvider` prop | Still wired through wizard tree (residual). Moved to V3 Phase H. |
| PlaybookBuilder/services/authService.ts | Deprecated, zero consumers; can be deleted in cleanup pass. Moved to V3 Phase H. |
| Duplicate `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js` | Duplicate file — verify which is canonical before next ribbon change. |
| UniversalQuickCreate PCF still uses local `MsalAuthProvider.ts` | Only remaining pre-v2 PCF (audit 2026-05-19 confirmed SSC + DRV both PCF and Code Page are migrated). Moved to V3 Phase H as cleanup target. |

### Resolved carryovers (kept here briefly to note resolution, then remove)

| Item | Resolution |
|---|---|
| ~~Task 083 (Office Add-ins deploy)~~ | ✅ Deployed 2026-05-19 to SWA, manifest 1.0.15.0 (commit `e649f244` merge / `6e8ead1c` close-out). |
| ~~SemanticSearch Code Page blocked by `@lexical/react` `.prod.mjs` webpack issue~~ | ✅ Fixed 2026-05-20 via deep-import in `ThemeProvider.ts` (commit `4495857f` / merge `4866682d`). SemanticSearch rebuilt + dual-env deployed; popup eliminated; user-verified. |
| ~~4 PCFs missing eslint devDep~~ (RDC, EPM, UniversalQuickCreate, VisualHost) | ✅ Fixed in commit `f67e6cca fix(pcf): add missing eslint to 4 PCFs` (other-project commit, on master). |

---

## D-AUTH-7 exception sites (canonical list — 8 sites for Phase E task 070)

Unchanged from Phase B carryover. To be enumerated/audited as part of Phase E (auth-v3-hardening project, when spun up).

---

## Memory entries created this session (7 total)

Path: `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke-wt-spaarke-auth-v2-and-hardening\memory\`

1. `feedback-dual-env-deploys` — Always deploy to BOTH spaarkedev1 + spaarke-demo, serial, dev first.
2. `feedback-name-collision-in-consumer-authinit` — Every consumer migration adding a field to `initAuth()` config MUST check `authInit.ts` for sync-import-vs-async-export name collisions.
3. `feedback-proactive-parallel-dispatch` — On `continue`, identify largest unblocked + parallel-safe batch and dispatch in ONE message.
4. `feedback-third-party-sdk-accesstoken-is-ok` — Third-party SDK constructors that require `accessToken: string` are D-AUTH-7 exceptions, not violations.
5. `project-auth-v2-baseline-msal-bug` — Phase 0 MSAL popup bug (2 root causes: display-name fallback + missing `sprk_TenantId`); hotfix shipped.
6. `feedback-question-urgency-for-dev-only-infra-tasks` — Don't over-urgency infra tasks in dev with no external users/data.
7. `feedback-dont-quick-fix-by-reverting-feature-flags` — NEVER quick-fix by reverting a feature flag and calling the feature "delivered."

---

## Key facts / IDs (for reference)

| Item | Value |
|---|---|
| Branch | `work/spaarke-auth-v2-and-hardening` |
| Worktree | `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening` |
| Subscription (BFF) | `Spaarke Devlopment Environment` (note typo in name) |
| BFF App Service (dev) | `spe-api-dev-67e2xz` in RG `rg-spe-dev` |
| BFF App Service (demo) | `spaarke-demo` |
| BFF app registration | App ID `1e40baad-...` |
| BFF MI Service Principal | Object ID `56ae2188-c978-4734-ad16-0bc288973f20`; AppId `6bbcfa82-...` |
| Microsoft Graph SP (constant) | `00000003-0000-0000-c000-000000000000` |
| Key Vault | `<kv-name-dev>.vault.azure.net` (MI has Key Vault Secrets User role) |
| Exchange scope group | `Spaarke Email Access` / `spaarke-central-email@spaarke.com` |
| In-scope mailboxes (current) | `testuser1@spaarke.com`, `mailbox-central@spaarke.com` |
| Out-of-scope (correct) | `ralph.schroeder@spaarke.com` (accessed via OBO only, not app-only) |
| Dataverse env (dev) | spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) |
| Dataverse env (demo) | spaarke-demo |
| Test users | testuser1@spaarke.com (auth tester); ralph.schroeder@spaarke.com (project owner) |

---

## Phase status summary

| Phase | Tasks | Status | Notes |
|---|---|---|---|
| **0 — Pre-flight** | 5/5 ✅ | Done | docs/STOP banners |
| **A — Library** | 7/7 ✅ | Done | useAuth, AuthStrategy, BrowserMsalStrategy, hotfixes 011 |
| **B — Consumers (Dataverse)** | 11/11 ✅ + 1 deferred (031) | Done | 6 PCFs + 13 Code Pages + 3 JS, dual-env deploys |
| **B4 — Consumers (Office Add-ins)** | 3/3 ✅ (080, 081, 082) | Source complete, NOT committed | Task 083 (deploy) not scheduled |
| **C — Server hardening** | 9/10 ✅ + 1 deferred (040) | Deployed + smoke-tested | Exchange policy ops complete; awaiting propagation |
| **D — Security middleware** | — | **MOVED to `auth-v3-hardening` project** | 060 CSP, 061 CAE, 062 claims, 063 step-up, 064 RT rotation |
| **E — CI** | — | **MOVED to `auth-v3-hardening` project** | 070 gitleaks, 071 regression Playwright, 072 Dependabot |
| **F — Docs** | 4/4 ✅ (090, 091, 092, 093, 094) | Source complete, NOT committed | ADR-028 (renumbered), sso-binding, constraints, archive, deployment guide |

**Overall completion (this project's revised scope, with D+E moved out)**: 39/40 in-scope tasks (97.5%); 1 deferred (031 wizards), 1 follow-on (083 Office deploy).

---

## On resumption: recommended `continue` flow

```
1. Read this file (you're here)
2. Verify `git status` matches the "What is UNCOMMITTED" table
3. Check propagation: az webapp log tail ... | grep ErrorAccessDenied
   - If clear: proceed to commit
   - If still 403s: wait, re-run Test-ApplicationAccessPolicy, recheck §7c
4. Strip build artifacts from staging: git restore deploy/api-publish/
5. Stage + commit (conventional commit, one or two commits depending on user preference):
   - feat(auth-v2): Phase F + B4 + bug fixes — finalize Auth v2 + Hardening
   - OR split into: docs(auth-v2): Phase F + B4 feat + fix commits
6. Merge work/spaarke-auth-v2-and-hardening → master (after user approval)
7. Spin up `auth-v3-hardening` project for Phase D + E content
8. Investigate Bug B (RagService filter) — separate fix
9. Resolve Bug C (SendToIndex) when user pastes actual errorMessage
```

---

*This file IS the recovery checkpoint. All work state is recoverable from this + TASK-INDEX.md + git status. No information is held only in conversation context.*
