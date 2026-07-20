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

## DI-028-02 — external-spa does not build in this worktree (pre-existing, blocks task 014)

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
