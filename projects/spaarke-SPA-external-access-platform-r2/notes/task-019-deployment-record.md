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

## Remaining verification — OWNER (live authenticated E2E)
Cannot be done by the agent (needs credentials/tenant/Teams):
- [ ] CIAM sign-in in a browser → reaches the workspace launcher; entitled widgets render.
- [ ] Workforce sign-in → reaches the launcher.
- [ ] Teams personal tab → silent Teams SSO, Teams dark theme, module data loads; CSP frame-ancestors live (no X-Frame-Options).

## Notes
- CSP `frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft` is in the deployed `staticwebapp.config.json`.
- Shared-infra caveat: `spaarke-bff-dev` is shared (~13 active BFF projects, last-deploy-wins). This deploy pushed `master + R2 external-access`; if another project had unmerged work deployed to dev for testing, it was overwritten (owner accepted proceeding after conflict-check).
