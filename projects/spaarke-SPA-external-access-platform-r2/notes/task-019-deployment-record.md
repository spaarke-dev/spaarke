# Task 019 — P1 Deployment Record

> Deploy module-host shell + Teams package + R2 BFF. Executed 2026-08-07 **from the worktree** (not GitHub CI).

## Deviation from POML: worktree deploy, not CI (owner directive)
The POML prescribed `deploy-external-spa.yml` / `deploy-bff-api.yml` (CI `workflow_dispatch`). Per owner directive (2026-08-07), Spaarke deploys **from the worktree directly** (local build + push to Azure), not via the CI pipelines. Both surfaces were deployed from this worktree with the same targets/env the CI would have used. (Preference saved to agent memory.)

## Pre-deploy gates (owner-requested)
- Worktree updated to master: HEAD `8e324ffc5`, **0 behind origin/master**, pushed.
- `/conflict-check`: **no deploy blocker**. Only `smart-todo-decoupling-r3` shares the surface — BFF files **disjoint**; one client overlap on `WorkspaceHomePage.tsx` = a **merge-to-master** coordination item (not deploy-time). Open PRs: none touch external-access.

## BFF → spaarke-bff-dev
- Command: `scripts\Deploy-BffApi.ps1` (dev defaults) — Release build → zip → `az webapp deploy` → `/healthz` verify.
- Subscription: Spaarke Development (`ralph.schroeder@spaarke.com`); RG `rg-spaarke-dev`; App Service `spaarke-bff-dev`.
- Result: **success** — package 48.38 MB; 4 critical files SHA-256-verified on server; `/healthz` **passed**.
- Effect: R2 BFF now live — `/api/v1/external` module-data surface (015/016) present; `/api/v1/collab` removed (018). This also satisfies the teams-app-r1 principal-agnostic BFF prerequisite (our branch = master incl. teams-app-r1 FR-22 + R2).

## Client → swa-spaarke-external-spa-dev
- Build: `npm install --legacy-peer-deps` + `npm run build` with the CI `VITE_*` env (BFF URL `https://spaarke-bff-dev.azurewebsites.net`, CIAM authority/client/tenant/scope, Teams workforce client/scope — all non-secret identifiers). `staticwebapp.config.json` staged into `dist/` (CSP frame-ancestors for Teams).
- Deploy: `@azure/static-web-apps-cli deploy ./dist --env production` with the SWA deploy token from `az staticwebapp secrets list` (token captured to a var, never echoed — NFR-03).
- Result: **success** → **https://green-dune-0c4f1221e.7.azurestaticapps.net** (SWA `swa-spaarke-external-spa-dev`, rg-spaarke-dev).

## Post-deploy smoke checks (unauthenticated)
| Check | Result | Expect |
|---|---|---|
| BFF `/healthz` | 200 | 200 ✅ |
| BFF `/ping` | 200 | 200 ✅ |
| BFF `/api/v1/external/me` (no token) | 401 | 401 (exists, needs auth) ✅ |
| BFF `/api/v1/collab/me` (no token) | **404** | 404 — **REMOVED by task 018, confirmed live** ✅ |
| SWA `/` | 200 | 200 ✅ |
| SWA `/project/abc` (deep link) | 200 | 200 via navigationFallback ✅ |

## UAT round 1 (2026-08-07) — findings + fix
Owner UAT of the deployed SWA surfaced:
1. Shell renders correctly (branded header, Quick Start + widget tabs, Ask Legal pane).
2. App showed "Jane Smith (Mock)" with NO login prompt.
3. Clicking Projects triggered a real MSAL login (ralph.schroeder@spaarke.com).
4. Data grids mount but show no data.

**Root cause of #2/#3 — DEV MOCK leaked into the worktree build.** `src/client/external-spa/.env.local` (gitignored, local-dev) contains `VITE_DEV_MOCK=true`. Vite loads `.env.local` for `npm run build`, so the first deployed bundle baked in mock mode (hardcoded mock user + AuthGuard short-circuit), while the data client (`BffDataverseClient`) still did real MSAL token acquisition → mid-app login. CI is unaffected (`.env.local` not in the repo).

**Fix (redeployed 2026-08-07):** rebuilt with `VITE_DEV_MOCK=false` (process env overrides `.env.local`) — Vite tree-shook the mock branch out (`grep -c externalfirm.com dist/assets/app.js` → 0). Redeployed to the SWA. Now the real dual-plane bootstrap runs (realm chooser → CIAM/workforce login); header shows the real signed-in user. Memory: `deploy-from-worktree-not-ci.md` updated with the `.env.local` gotcha.

**#4 (no data) — EXPECTED P1 behavior, not a bug:**
- **Tier-1 entitlements (`/me`) are still MOCKED** — `me-client.ts` returns a hardcoded per-plane payload by design (task 012); the real endpoint is **P2 task 022**. So the default tab set + any `/me` persona name are placeholder in P1.
- **Data grids call the REAL BFF Tier-2** (`/api/v1/external/api/dataverse/*`, per-caller record scope). Empty = **fail-closed** for a caller with no `sprk_externalrecordaccess` grants. ralph.schroeder@spaarke.com has no project grants → "no access to any projects" is correct.
- **To see data in UAT**: grant a test CIAM/workforce user access to a test `sprk_project` (admin endpoint `/api/v1/external-access/invite-and-grant` or `/grant`), then re-test. This is provisioning + P2-adjacent, not a P1 code defect.
- Note: standalone **workforce** browser login (realm chooser "My organization") depends on the workforce-plane auth policy = **P2 task 024**; the CIAM ("Partner") path is the primary P1-tested path.

## UAT round 2 (2026-08-07) — access model + partner provisioning
Owner UAT continued; two outcomes delivered.

### (2) System-user path ("My organization") — access-model fix shipped
Empty grids for a workforce **system-user** were root-caused to the design-§5 rule "systemuser = ADR-034 membership only" (contact grants ignored). Owner directive → **parallel workforce/contact access**: system-user accessible set now = membership ∪ the caller's OWN contact grants (project-scoped). Code + tests + redeploy done (commit `ed991bc79`; see `access-model-systemuser-contact-grant-union.md`). **Verified end-to-end via Dataverse**: systemuser `1d02f31c` → `sprk_primarycontact` `8e9918a9` (spaarke.com contact) → active Full-Access grants to **Project 1** (`b12496d1…`) + project `3e34a21a` → both now surface. Owner to confirm in-browser.

### (1) Partner path (hotmail) — CIAM account provisioned
- Dataverse pre-state (reads): hotmail contact `2e419a4f…` existed with a Full-Access grant to **Project 1**, `sprk_externalobjectid` null (email-resolvable).
- **CIAM sign-in bug found (hand to operator / P4 task 042)**: the "Partner" sign-in page shows *"This account does not exist … `<aadSelfSignup>create a new one</aadSelfSignup>`"* with the self-signup tag rendered as **literal text** → self-service sign-up is NOT properly enabled in the CIAM user flow. External self-onboarding is broken.
- **Provisioned via the invite endpoint** (`POST /api/v1/external-access/invite`, workforce token from `az`, audience `api://1e40baad…`): after fixing a config gap (below), returned `200 {status:"Provisioned"}`; oid `06646385-cbbc-4321-a458-f631e0096328` bound to the contact. Onboarding email sent to hotmail (non-fatal path). Password is delivered via SSPR "Forgot password".
- **Owner last step**: "Partner" → sign in as `ralph.schroeder@hotmail.com` → set password (onboarding email or "Forgot password"/SSPR) → email+oid resolves the contact → **Project 1** appears. ⚠️ SSPR must be configured in the CIAM user flow — verify (P4 task 042); if reset is unavailable, that's the next CIAM config gap.

### Shared-dev config change made (document → runbook)
- `spaarke-bff-dev` was **missing all `ExternalAccess__*` settings**; `ExternalAccess:PortalUrl` is required by the invite handler (threw `InvalidOperationException` → 500). **Set** `ExternalAccess__PortalUrl=https://green-dune-0c4f1221e.7.azurestaticapps.net` via `az webapp config appsettings set` + restart. This belongs in `docs/guides/auth-deployment-setup.md` §3 (operator App Service settings) for all environments. (CIAM provisioner was already configured: `Ciam__GraphProvisioner__*` + cert `ciam-graph-provisioner-cert`.)

## Remaining verification — OWNER (live authenticated E2E)
Cannot be done by the agent (needs credentials/tenant/Teams):
- [ ] CIAM sign-in in a browser → reaches the workspace launcher; entitled widgets render.
- [ ] Workforce sign-in → reaches the launcher.
- [ ] Teams personal tab → silent Teams SSO, Teams dark theme, module data loads; CSP frame-ancestors live (no X-Frame-Options).

## Notes
- CSP `frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft` is in the deployed `staticwebapp.config.json`.
- Shared-infra caveat: `spaarke-bff-dev` is shared (~13 active BFF projects, last-deploy-wins). This deploy pushed `master + R2 external-access`; if another project had unmerged work deployed to dev for testing, it was overwritten (owner accepted proceeding after conflict-check).
