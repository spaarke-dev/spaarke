# Task 070: dev SPA + dev Teams package (record)

Owner direction 2026-10-20: "070-071-072 - yes proceed".

## SPA (dev SWA)

- Live: master `28274e04e` (PR #2141, all R3 work to date), `deploy-external-spa.yml` run `39468172374`, 2026-10-20 17:40Z, owner-approved ("yes can deploy SPA"). The record is in `task-059-dev-setup-record.md` ("SPA redeploy from master").
- Host `green-dune-0c4f1221e.7.azurestaticapps.net`. `/assets/app.js` contains `workforce_guest_use_home_signin`; `/auth-callback.html` returns 200.
- Preconditions (POML): the `external-spa-dev` environment has all the VITE_* variables (read 2026-10-20: VITE_BACKENDS, VITE_BFF_API_URL, VITE_MSAL_AUTHORITY/BFF_SCOPE/CLIENT_ID/TENANT_ID, VITE_TEAMS_MSAL_BFF_SCOPE/CLIENT_ID/TENANT_ID, VITE_WORKFORCE_HOME_MSAL_CLIENT_ID). The CIAM BFF scope/redirect precondition is met: partner sign-in works (owner, 2026-10-20).
- Rollback: tag `external-spa-dev-rollback-1908696a8` → `gh workflow run deploy-external-spa.yml --ref external-spa-dev-rollback-1908696a8 -f environment=dev`.

## Dev Teams package

- `deploy-teams-app.yml` run `39489551799` (ref master at `c3ec5d6e5`, environment dev): success. It only builds and publishes an artifact; nothing live changes until the owner uploads it.
- Artifact `teams-app-package` → `appPackage.zip` (2,468 bytes). Manifest: id `23610794-67de-4e6c-be61-ff80cc8cbe7f`, version `1.2.0`, short name "Spaarke", static tab `https://green-dune-0c4f1221e.7.azurestaticapps.net`, no `webApplicationInfo`, validDomains = the SWA host, `spaarke-bff-dev.azurewebsites.net`, `teams.microsoft.com`, `*.cloud.microsoft`.
- **Owner step:** Teams admin center (Spaarke tenant) → Teams apps → Manage apps → find "Spaarke" (id `23610794…`) → Upload new version, or upload as a new custom app if it is not there yet. Then open it in Teams desktop and web.
- Rollback: upload the previous package version (or remove the app); the SPA is unaffected.

## Smoke test (POML step 4): pending, owner-driven

| Check | Browser | Teams |
|---|---|---|
| Workforce sign-in | ✅ Tracy Cheatham (DC member) registered via "Sign in with your company account", 2026-10-20 | pending |
| Partner sign-in | ✅ owner, 2026-10-20 | n/a (partners are browser-only) |
| Open a record (detail modal) | pending | pending |
| Submit one wizard | pending | pending |
| Send one message; the bell shows it | pending | pending |
