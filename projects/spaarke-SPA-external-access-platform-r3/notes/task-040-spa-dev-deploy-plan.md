# Task 040 step 4: external SPA deploy to dev (plan)

> Status: **PLAN** (2026-10-13). Owner said "yes let's proceed" to preparing it; nothing below has run against Entra, GitHub Actions or the SWA.
> Why now: the word-add-in-r1 Dewey Cheatham retest needs the workforce sign-in of task 040 on dev. The live dev SPA still signs in through `/organizations` with the dev BFF app as its client.

## What changes on dev

| | Live today (`885d0c5f9`, deployed 2026-10-08) | After this deploy (master) |
|---|---|---|
| Workforce client | `1e40baad` (the dev BFF app doubling as client), authority `/organizations` | "Spaarke Workspace" `9f1da340`, authority `login.microsoftonline.com/a221a95e...` |
| Workforce scope | `api://1e40baad/access_as_user` | `api://1e40baad/user_impersonation` |
| CIAM scope | `api://4a4d5126/SDAP.Access` | `api://4a4d5126/user_impersonation` |
| Values from | hard-coded in the workflow | GitHub environment `external-spa-dev` variables (task 043) |

GitHub `external-spa-dev` variables read 2026-10-13 (masked): `VITE_BFF_API_URL` = `https://spaarke-bff-dev.azurewebsites.net`; `VITE_MSAL_AUTHORITY` = `https://spaarkeextid.ciamlogin.com/7052feba...`; `VITE_MSAL_CLIENT_ID` `bd57e54e...`; `VITE_MSAL_TENANT_ID` `7052feba...`; `VITE_MSAL_BFF_SCOPE` `api://4a4d5126.../user_impersonation`; `VITE_TEAMS_MSAL_CLIENT_ID` `9f1da340...`; `VITE_TEAMS_MSAL_TENANT_ID` `a221a95e...`; `VITE_TEAMS_MSAL_BFF_SCOPE` `api://1e40baad.../user_impersonation`. `VITE_BACKENDS` unset (one-entry fallback). Secret `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` present (repository level). These match the BFF: `Ciam__Audience` = `4a4d5126...`, workforce app `1e40baad`.

Not in scope: the Teams package (`deploy-teams-app.yml`) and the FR-21 Teams live check (task 071).

## S0: prerequisites (read-only)

| # | Who | Check | Why |
|---|---|---|---|
| S0a | **Owner, Entra admin center** (the CIAM tenant needs an interactive MFA sign-in; CLI sign-in to `spaarkeextid` is refused, T240d S1) | Tenant `spaarkeextid` -> App registrations -> `4a4d5126-91b0-4865-8e3a-134b7209013e` -> **Expose an API**: scope `user_impersonation` exists and is **Enabled**, and `bd57e54e-...` is an authorized client application for it. Manifest: `requestedAccessTokenVersion` = 2 | `config/spaarke-resources.yaml:260` and `config/environments.json:68` still list only `SDAP.Access`. If `user_impersonation` is missing, the new build breaks CIAM sign-in on dev |
| S0b | Session | `az ad app permission list-grants --id 9f1da340-c647-4266-9d89-e09f10700593 --show-resource-name -o table` and `az ad sp show --id 9f1da340-c647-4266-9d89-e09f10700593 --query "{enabled:accountEnabled, assignmentRequired:appRoleAssignmentRequired}" -o table` (Spaarke tenant, current sign-in) | Whether members and the guest get a consent prompt or are blocked on the new client |
| S0c | Session (local, **done 2026-10-13: all pass**) | Master merged into R3 (`790a0a523`); in `src/client/external-spa`: typecheck 0, lint 0, vitest 993/993 (35 files), build 0, check:dist clean | The workflow builds master; master gained 5 shared-component files since R3 merged |

**If S0a finds the scope missing (owner, portal, live change):** add scope `user_impersonation` (admin consent only, Enabled) on `4a4d5126` and authorize `bd57e54e` for it. Leave `SDAP.Access` in place: the rollback build uses it. Rollback of the fix: disable the new scope, then delete it.

## S1: deploy (one command)

```powershell
gh workflow run deploy-external-spa.yml --ref master -f environment=dev
gh run list --workflow deploy-external-spa.yml -L 1 --json databaseId,status --jq '.[0].databaseId'   # then:
gh run watch <run id> --exit-status
```

The workflow fails closed before building if any variable is missing or mis-shaped, scans `dist/` (`check:dist`), and uploads to `swa-spaarke-external-spa-dev`. **Impact:** everyone using the dev SPA signs in through the new client from the next page load; their cached `/organizations` sessions no longer apply.

## S2: verify

| # | Who | Check | Expected |
|---|---|---|---|
| S2a | Session | `curl.exe -s https://green-dune-0c4f1221e.7.azurestaticapps.net/auth-callback.html` | The MSAL bridge page, not the SPA's `index.html` (today it returns the index with 200) |
| S2b | Owner, browser | Workforce member sign-in at `https://green-dune-0c4f1221e.7.azurestaticapps.net/` | Token `tid` = `a221a95e...`, `aud` = the dev BFF app, `scp` has `user_impersonation`; the SPA loads |
| S2c | Owner, browser | Guest sign-in (`ralph@deweycheatham...`, the Dewey Cheatham retest) | `tid` = Spaarke tenant, `acct` = 1; the SPA loads as the guest's systemuser |
| S2d | Owner, browser | CIAM partner sign-in | Token `aud` = `4a4d5126...`, `iss` = `https://7052feba....ciamlogin.com/.../v2.0`; the SPA loads |

## Rollback

Trigger: workforce or CIAM sign-in on dev broken and not fixable by configuration within a few minutes.

```powershell
git tag external-spa-dev-rollback-885d0c5f9 885d0c5f9
git push origin external-spa-dev-rollback-885d0c5f9
gh workflow run deploy-external-spa.yml --ref external-spa-dev-rollback-885d0c5f9   # that version has no inputs
```

This rebuilds exactly the live build: the workflow at `885d0c5f9` hard-codes the old values (`deploy-external-spa.yml:47,51-52` there) and uses the same repository secret. It still signs in, because task 040 step 2 added `user_impersonation` pre-authorizations without removing the `access_as_user` ones. A Static Web App keeps no previous production build, so a rebuild is the only rollback.

## Record

- 2026-10-13 S0b (owner-approved): `9f1da340` enabled, `appRoleAssignmentRequired` false, no declared permissions, no consent grants. Tenant user-consent policy includes `microsoft-user-default-recommended` and `microsoft-user-default-allow-consent-apps`, so members and guests can consent to the sign-in scopes themselves: at worst a one-time consent prompt, not a block.
- 2026-10-13 S0c: done (above).
- 2026-10-13 **S1 deploy HELD.** (1) S0a not yet confirmed by the owner. (2) word-add-in-r1 reported a same-day UAC owner decision (round 97, UAC task 186 POML `4dcb3d661` on `work/unified-access-control-r2`, not on master): Model 1 workforce sign-in stays home-tenant `/organizations`, the BFF accepts tokens whose `tid` is in `CustomerTenantIds`, no B2B guests, self-registered workforce contacts. That contradicts ADR-028 A8, which this deploy implements. The deploy waits until the owner says which model stands.
- 2026-10-13 **S0a done (owner, portal, spaarkeextid).**
  - API app `4a4d5126` (Spaarke External BFF API): `user_impersonation` exists and is Enabled ("Admins and users"); `SDAP.Access` is kept; `requestedAccessTokenVersion` = 2.
  - The authorized client `bd57e54e` had no scope ticked; the owner updated it.
  - SPA app `bd57e54e` (Spaarke External Workspace SPA), API permissions: `User.Read`, `SDAP.Access` and `user_impersonation` are delegated and **granted with admin consent** for Spaarke External Access.
  - S1 stays held only for the A9 re-plan of 040.
- 2026-10-13 **S1 deployed (owner: "deploy SPA").** Run `38420637859` of `deploy-external-spa.yml` (ref master `05356cbf7`, environment dev) succeeded. The A9 re-plan no longer holds it: licensed users stay on A8 and CIAM is unchanged; workforce contacts use the A8 path on dev until the A9 build.
- 2026-10-13 **S2a PASS:** `/auth-callback.html` returns 200, the "Spaarke Authentication" MSAL bridge page, not the SPA index. S2b–S2d (browser sign-ins) are with the owner.
