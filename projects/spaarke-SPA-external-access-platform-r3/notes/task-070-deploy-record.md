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
- **Correction 2026-10-20:** the Teams tab serves only tenants listed in VITE_BACKENDS `homeTenantIds` (dev: Dewey Cheatham only; `main.tsx` Teams bootstrap → "This organization is not set up for Spaarke."). So the smoke runs in the **Dewey Cheatham** tenant, as a DC user. Uploaded in the Spaarke tenant, the app opens to "not set up" by design.
- **Owner step (original text, tenant corrected above):** Teams admin center (Spaarke tenant) → Teams apps → Manage apps → find "Spaarke" (id `23610794…`) → Upload new version, or upload as a new custom app if it is not there yet. Then open it in Teams desktop and web.
- Rollback: upload the previous package version (or remove the app); the SPA is unaffected.

## Smoke test (POML step 4): pending, owner-driven

| Check | Browser | Teams |
|---|---|---|
| Workforce sign-in | ✅ Tracy Cheatham (DC member) registered via "Sign in with your company account", 2026-10-20 | pending |
| Partner sign-in | ✅ owner, 2026-10-20 | n/a (partners are browser-only) |
| Open a record (detail modal) | pending | pending |
| Submit one wizard | pending | pending |
| Send one message; the bell shows it | pending | pending |

## SPA redeploy 2026-10-21 (#2166 fix + UAC #2078/#2122 SPA)

- Owner: "yes approved--2 reviews and SPA deploy" (2026-10-21). UAC agreed its #2078/#2122 SPA changes go live with it (dev BFF 4f5f22694 already enforces them, `writeAccessPerRecord: true`).
- Fix: PR #2175 (merge `3531ed65e`, 02:58:58Z). Two top-tier adversarial reviews: no defect on a real path; follow-ups applied (deploy-workflow guard: `VITE_MSAL_TENANT_ID` must be the authority's tenant). SPA suite: every file passes run alone; the 4 failures in one full parallel run pass alone (known load flakiness). tsc clean.
- Deploy: `deploy-external-spa.yml` run `39536100531`, ref master `3531ed65e`, environment dev, success 03:02:16Z. Live: `/` 200, `/auth-callback.html` 200, `/assets/app.js` contains `accountFilter` and `writeAccessPerRecord`.
- Rollback: tag `external-spa-dev-rollback-28274e04e` → `gh workflow run deploy-external-spa.yml --ref external-spa-dev-rollback-28274e04e -f environment=dev`.
- Owner smoke (pending): partner sign-in (normal window); the old failing path (company account → "not set up" → "choose a different sign-in" → Partner); Ralph Dewey workspace; UAC: on a ViewOnly project (the admin has 22; any but `b12496d1…`) upload / new to-do / new event hidden or disabled and no 403 in the network panel; on FullAccess `b12496d1…` they show; no new `useAccessLevel` console errors.


## Teams live checks: progress 2026-10-29

- **Spaarke tenant (unknown directory):** the package was uploaded and the app added. The tab shows "Your organization is not set up for Spaarke" as designed: the Teams startup reads the tenant first and makes no token request or backend call (`main.tsx` `bootstrapTeams`). Client not yet stated by the owner.
- **Dewey Cheatham:** the Teams admin center refused `admin@…` with `INVALID_PRIVILEGE` (open, with the owner; roles to be checked as active). The app was nevertheless added in Teams, and the owner reports "the SPA workforce portal works". Server (App Insights):
  - 16:44–16:49Z: one home account was refused registration five times (`registration_email_missing`: no email attribute; the form issue is ISS-023 / #2449);
  - 16:51:12Z: `POST /registration` 201, a new WorkforceHome contact created (1 contact with an identity plane created since 16:00Z);
  - after that, the account resolves as a ContactOnly workforce principal with 0 roots (a new contact with no grants).
- **Test artifact:** that new contact is probably the Dewey Cheatham admin account registering itself. Remove it after testing if it should not stay.
- **Still to run:** Tracy Cheatham (workforce contact) and Ralph Dewey (licensed user) in Teams desktop, web and mobile.
- **Tracy Cheatham in Teams (owner: pass):** 16:56:48–16:57:04Z, resolved to her existing WorkforceHome contact (10 resolutions, no second contact, no denials).
- **Ralph Dewey in Teams (owner: pass):** 16:58:10–16:58:28Z, resolved through the stored home link to the licensed systemuser (6 resolutions, no profile form).
- No `/api/v1/external/*` errors after 16:51:10Z. Every error in the 45-minute window was the registration-without-email sequence above.
- **Client (owner, 2026-10-29):** all of today's Teams runs used **Teams web**: the Spaarke-tenant not-set-up check, the new-employee registration, Tracy and Ralph Dewey. Desktop and mobile are still to run.
