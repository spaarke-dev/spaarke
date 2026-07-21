# Deferrals & Issues — spaarke-SPA-external-access-platform-r1

> Source of truth for deferred work + newly-discovered issues (per project CLAUDE.md
> "Deferrals & Issues" section). Every entry names a concrete failing behavior/contract (§11).

---

## DI-028-01 — CIAM SPA + BFF-API app registrations (ops prerequisite for live external auth) — ✅ RESOLVED 2026-07-20

- **RESOLVED 2026-07-20**: Owner registered both apps in the CIAM tenant + granted the KV role:
  - **Spaarke External BFF API** `4a4d5126-91b0-4865-8e3a-134b7209013e` — exposes scope `SDAP.Access` (App ID URI `api://4a4d5126-…`). → BFF `Ciam:ClientId` + `Ciam:Audience`; `VITE_MSAL_BFF_SCOPE`.
  - **Spaarke External Workspace SPA** `bd57e54e-b339-4500-b55c-e451009fd907` — SPA platform; SDAP.Access + Graph User.Read granted + admin-consented; added to `SpaarkeExternalSignIn` user flow. → `VITE_MSAL_CLIENT_ID`.
  - BFF managed identity granted **Key Vault Secrets User** on `spaarke-spekvcert` (unblocks the task-022 cert load for 031).
  - Wired into repo 2026-07-20: `.env.development`, `config/environments.json` (dev.ciam.spaClientId/bffApiClientId/bffApiScope + deploy-substitution mapping), `config/spaarke-resources.yaml` (external_identity.app_registrations.bff_api + .spa_client).
  - REMAINING (verify at deploy): the deploy pipeline must substitute the BFF `#{CIAM_*}#` placeholders from the mapping recorded in `environments.json` dev.ciam; confirm the v2 `aud` (client-id GUID vs `api://…`) against a live token. Live E2E sign-in is the deferred Phase-2 spike.
  - **CONFIRMED AT DEPLOY 2026-07-20 (task 031)**: after deploying to `spaarke-bff-dev`, `GET /api/v1/external/me` + the external `/content` route return **500 (not 401)** while admin `/api/v1/external-access/invite-and-grant` returns 401. Verified root cause: `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev` shows **NO `Ciam:*` settings** — the "Ciam" JwtBearer scheme builds authority `https://{Ciam:Instance}/{Ciam:TenantId}/v2.0` from null config → `InvalidOperationException` on challenge → 500. **Fix (owner action, auth-sensitive)**: set App Service `Ciam:*` settings — `Ciam:Instance=https://spaarkeextid.ciamlogin.com`, `Ciam:TenantId=7052feba-bfc4-43e0-b09e-65014b429131`, `Ciam:ClientId=4a4d5126-91b0-4865-8e3a-134b7209013e`, `Ciam:Audience=api://4a4d5126-…` (confirm GUID-vs-URI vs a live token), `Ciam:Domain=spaarkeextid.onmicrosoft.com`, `Ciam:GraphProvisioner:ClientId=<provisioner app e63e6eb1-…>`, `Ciam:GraphProvisioner:CertificateName=<KV cert name>` (cert from KV by name — not a plaintext secret). Then `/api/v1/external/*` should be 401 unauthenticated; full sign-in is the live-E2E spike (task 040 parity).

- **Discovered**: 2026-07-19 (task 028).
- **Concrete failing behavior**: The external SPA is now config-pointed at the CIAM authority, but
  the CIAM tenant (`spaarkeextid`, 7052feba-…) currently contains ONLY the `ciam_graph_provisioner`
  app (e63e6eb1-…). There is **no SPA public-client app** and **no BFF-API app** registered in the
  CIAM tenant. Until both exist, a real CIAM login cannot mint a token, and the BFF `Ciam` scheme
  (task 020) has no real `Ciam:ClientId`/`Ciam:Audience` to validate against. `.env.development`
  carries `TODO_CIAM_SPA_CLIENT_ID` / `api://TODO_CIAM_BFF_API_APP_ID/SDAP.Access` markers, and the
  BFF `Ciam` config still uses `#{CIAM_API_APP_ID}#` / `#{CIAM_API_AUDIENCE}#` placeholders.
- **Needed (portal/CLI ops in the CIAM tenant — same class as tasks 001/002)**:
  1. Register a **SPA public-client** app in the CIAM tenant; add the SWA + `http://localhost:3000`
     redirect URIs (SPA platform). Record its client id → `VITE_MSAL_CLIENT_ID` / pipeline var
     `MSAL_CLIENT_ID`, and `VITE_MSAL_AUTHORITY` (already set).
  2. Register a **BFF-API** app in the CIAM tenant; Expose an API → scope (e.g. `SDAP.Access`);
     record `api://{app-id}/SDAP.Access` → `VITE_MSAL_BFF_SCOPE` / `MSAL_BFF_SCOPE`, and set the
     BFF `Ciam:ClientId` + `Ciam:Audience` (task 020 placeholders) to this app.
  3. Grant the SPA app delegated permission to the BFF-API scope (+ admin consent).
  4. Associate the SPA app with the `SpaarkeExternalSignIn` user flow (resources.yaml line 172).
- **Blocks**: live CIAM E2E (deferred Phase-2 spike — non-blocking per spec), task 014 parity test,
  task 031 deploy substitution values.
- **Owner action**: portal/CLI in the CIAM tenant (cannot be minted headlessly — CIAM Graph/ad ops
  require interactive admin, same limitation as 001/002).

## DI-028-02 — external-spa does not build in this worktree — ✅ RESOLVED 2026-07-20

- **RESOLVED 2026-07-20** (alongside task 030). Fix confined to `src/client/external-spa/**` (no shared-lib
  change — the shared `@spaarke/ui-components` is consumed **from source** via the vite alias + tsconfig paths).
  Independently verified: `npx tsc --noEmit` exit 0; `npx vite build` succeeds (emits `dist/assets/app.js`,
  1.07 MB / 291 KB gzip). Changes:
  - `tsconfig.json` — `paths` `@spaarke/ui-components` depth `../../shared` → `../shared` (was one level too deep).
  - `package.json` — dep `file:../../shared` → `file:../shared`; added `@microsoft/applicationinsights-web ^3.3.0`
    (transitive dep the shared source imports; absent from external-spa's tree). `package-lock.json` regenerated
    via `npm install --legacy-peer-deps --no-audit --no-fund` (re-links the shared symlink).
  - `src/App.tsx` — the ROOT cause beyond the path fix: a **bare barrel import** (`export *`) made tsc deep-check
    the ENTIRE shared lib under external-spa's stricter `noUnusedLocals`/`noUnusedParameters` (shared's tsconfig
    has these `false`; every other consumer points tsc at built `dist/index.d.ts` which `skipLibCheck` skips, but
    no `dist` exists). Narrowed to the self-contained subpath `@spaarke/ui-components/utils/themeStorage`.
  - Six components (SectionCard/AiToolbar/DocumentLibrary/EventsCalendar/SemanticSearch/SmartTodo) — the
    `Type 'string' is not assignable to type 'undefined'` errors were a REAL (non-path) fix: Griffel types the
    CSS border *longhands* as `never`; replaced with `shorthands.border(...)`/`shorthands.borderColor(...)`.
  - `InviteUserDialog.tsx` — DTO drift from B2B removal (025/029): removed `invitationCode`/`expiryDate` display
    (fields no longer on `InviteUserResponse`), replaced with a CIAM onboarding-sent confirmation (no invite code
    in the broker-only model). Reviewed + approved.
  - Removed genuinely-unused imports across DocumentUploadPage/ProjectPage/WorkspaceHomePage; added ambient stub
    `src/types/sdap-client.d.ts` for `@spaarke/sdap-client`.
  - **Unblocks tasks 011/012/014** (external-spa now builds). `config.ts` + `auth/msal-config.ts` (task-028
    deliverables — sessionStorage per-tab isolation, CIAM `knownAuthorities`) untouched.

- **Discovered**: 2026-07-19 (task 028; pre-dates this task — reproduces on clean HEAD).
- **Concrete failing behavior**: `npx tsc --noEmit` reports ~48 errors and `vite build` fails, all
  OUTSIDE the auth-config scope of task 028:
  - `vite build` → Rollup cannot resolve `@microsoft/applicationinsights-web` imported by the shared
    `Spaarke.UI.Components/src/services/AppInsightsService.ts` (workspace dep not installed/linked).
  - `tsc` → missing `@spaarke/ui-components/components/{Wizard,FileUpload,PlaybookLibraryShell}` and
    `@spaarke/ui-components/utils/adapters/*` subpath declarations (shared lib not built/linked), plus
    unused-import / implicit-any lint-level errors in `DocumentUploadPage.tsx`, `PlaybookLibraryPage.tsx`,
    `ProjectPage.tsx`, `WorkspaceHomePage.tsx`, `SmartTodo.tsx`.
  - (Fixed in 028 because the file was in scope: `msal-config.ts` `storeAuthStateInCookie` — invalid in
    `@azure/msal-browser@^5.5.0` `CacheOptions`; removed.)
- **Impact**: task 014 (deploy external-spa to SWA + verify parity) cannot produce a build until the
  shared `@spaarke/ui-components` is built/linked in this worktree and the appinsights dep + subpath
  exports are resolved (monorepo linkage; CLAUDE.md notes Vite-solution `npm ci` fragility).
- **Owner action**: address as part of task 014 (Phase 1) — build/link the shared lib, resolve the
  appinsights import, clean the pre-existing page-level type errors. Not in 028 scope (auth config only).

## DI-030-01 — two 030 acceptance sub-criteria need live CIAM+Dataverse E2E (documented §6.5 Path-A deviation)

- **Discovered**: 2026-07-20 (task 030).
- **Delivered (in-process, KEEP path `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs`)**:
  6 tests / 7 cases, all pass. Covers POML criteria 2 (provisioner idempotency), 4 (grant writes
  `sprk_externalrecordaccess` + invalidates cache + no synthetic SPE), 5 (download authz-before-stream
  POSITIVE + NEGATIVE — the NFR-03 centerpiece: 403 with `IDocumentStorageResolver.GetSpePointersAsync`
  AND `ISpeFileOperations.DownloadFileAsync` asserted `Times.Never`), 6 (no banned shapes), 7 (all pass),
  and criterion 1's unauthenticated→401 (CiamExternal policy).
- **Concrete gap (2 sub-criteria)**:
  1. **Criterion 1 "invalid-*issuer* → 401" (real JWT validation)**: the fixture replaces the real "Ciam"
     JwtBearer scheme with a fake handler (real JWT issuer/signature validation needs live OIDC metadata +
     signing keys). The *policy* behavior (unauthenticated → 401) is tested; genuine invalid-issuer rejection
     is a live-token property.
  2. **Criterion 3 "oid match resolves; email mismatch denied when oid bound" (real resolution logic)**: the
     fixture *controls* `ExternalParticipationService.ResolveExternalContactAsync` via the virtual seam (to
     drive the download authz scenarios), so it does not exercise the REAL oid/email decision logic. That logic
     lives in raw-`HttpClient`→Dataverse calls — mocking that boundary is B1-banned (`Mock<HttpMessageHandler>`)
     and there is no live CIAM/Dataverse test tenant in-session.
- **Why Path-A (not a silent skip)**: both properties are genuinely un-testable in-process under ADR-038 bans
  without live infra; they belong to the already-planned **live-E2E Phase-2 spike** (non-blocking per spec —
  "the architecture is already GREEN"). Writing an in-process test for them would be false-green.
- **Owner action (at the live-E2E spike / task 040 parity)**: assert (a) a token with a wrong issuer is 401 on
  `/api/v1/external/*`, and (b) an oid-bound Contact is not hijacked by a mismatched-email token. Real CIAM
  sign-in + Dataverse tenant required.
- **Production test seams added by 030 (backward-compatible, additive)**: `virtual` on
  `ExternalParticipationService.{ResolveExternalContactAsync,GetParticipationsAsync}`,
  `ExternalDataService.GetDocumentProjectAndNameAsync`, and `DataverseWebApiClient.{RetrieveAsync,CreateAsync,
  UpdateAsync,QueryAsync}`; download endpoint injects `ISpeFileOperations` (existing interface) instead of
  concrete `SpeFileStore`. No new interfaces, no new runtime surface, zero publish-size delta.

## DI-025-01 — CIAM provisioner partial-failure hardening (create-ok / persist-fail window)

- **Discovered**: 2026-07-19 (task 025).
- **Concrete failing behavior**: In `InviteExternalUserEndpoint`, if `CreateCiamUserAsync` succeeds but
  the subsequent `UpdateAsync` (persist oid to `Contact.sprk_externalobjectid`) fails, the CIAM account
  exists but no oid is bound to the Contact. The request returns 500 (oid IS logged for manual recovery).
  A re-invoke sees `existingOid == null` and attempts a second `POST /users` for the same email identity
  — which the CIAM tenant REJECTS as a duplicate identity (409), so no duplicate account is created, but
  the flow is stuck at 500 until an operator manually binds the logged oid. Also: the onboarding email is
  skipped on the idempotent (already-provisioned) path, so a first-attempt email-send failure cannot be
  retried via re-invoke.
- **Safe today**: no duplicate CIAM account is ever created (unique email identity + idempotency gate);
  the window is a narrow Dataverse-PATCH failure and the oid is logged.
- **Hardening (post-R1)**: on `POST /users` conflict, look up the existing CIAM user by email identity to
  recover its oid and continue (persist + email) — makes the flow self-healing. Consider an optional
  "resend onboarding" affordance for the already-provisioned path. Out of 025's prescriptive scope.

## DI-029-01 — core-user "Invite to Secure Workspace" UI (ribbon/MDA button) + dark-mode test

- **Discovered**: 2026-07-19 (task 029).
- **Concrete deliverable delivered**: the SERVER action `POST /api/v1/external-access/invite-and-grant`
  (one atomic onboard+grant, grantee=Contact, audited) is built + verified. Acceptance criteria 1/3/4
  are met server-side.
- **Remaining (frontend/ops — not buildable/verifiable in a .NET session)**: the actual core-user
  command surface — a Dataverse command-bar (ribbon) button OR model-driven-app command on the
  Matter/Project form that collects Project + attorney Contact + access level and calls the endpoint
  above. This is what acceptance criterion 5 (renders in Fluent v9 dark mode, ADR-021) tests, and what
  criterion 2's live sign-in exercises end-to-end. Requires ribbon XML + web-resource JS + solution
  import (Dataverse deploy). Track alongside the external-spa work / a UI task.
- **Also gated on**: DI-028-01 (CIAM app registrations) for the live sign-in half of criterion 2.
