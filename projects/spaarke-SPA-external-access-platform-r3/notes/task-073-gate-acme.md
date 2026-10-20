# Task 073 gate: production SPA (external.spaarke.com) for the ACME customer test

Owner direction 2026-10-20: 073 must work with provisioning-r1's ACME customer deployment test. Facts below come from provisioning (read-only, 2026-10-20), not re-read by R3.

## What 073 is

One production build of the external SPA at `external.spaarke.com`, serving every customer, plus the production Teams package. Each sign-in is routed to that customer's stamp BFF. Partners (CIAM) and customer employees (workforce, A9) both use it.

## ACME stamp (`sprk-acme-prod-api`, BFF build 2026.10.16-2): gate status

| Gate item | Status for ACME | Owner of the fix |
|---|---|---|
| Stamp BFF app `spaarke-bff-api-acme` (`b4882848…`), multitenant, `user_impersonation` | Exists (H3) | — |
| A9 v2 access tokens (`requestedAccessTokenVersion` 2) | **Missing** (null = v1). Needs a stamp BFF build containing R3 077 or later first | Provisioning (H3), after the stamp BFF is upgraded |
| External SPA workforce client pre-authorized on the stamp app | **Missing** (only the Copilot agent and the Office add-in) | Provisioning: add to `EntraAppRegOptions__PreAuthorizedClientAppIds__N`, re-run H3. R3 sends the final client id |
| `WorkforceIdentity__CustomerTenantIds__0` | Set: `78195073…` (acmecorpdemo) | — |
| `ExternalAccess__CustomerBusinessUnitId` | **Missing**; registration answers 500 `registration_owner_unconfigured` without it. Value known: ACME customer BU `c3f22cfd…` | Provisioning: add to the H9 settings catalog |
| `removeUnverifiedEmailClaim` = true (ISS-013) | **Missing** | Provisioning (H3) |
| Customer-tenant consent for the workforce client | **Not done**. Must be a delegated grant for the workforce client only, never the v1 `/adminconsent` that also consents the BFF app's Graph application permissions (finding C-1) | Design first (provisioning + R3), then ACME's admin |
| CIAM BFF app for the stamp (partners on stamps, T240d) | **Not built** | T240d; gate is UAC task 185 |
| Routing (T240c): VITE_BACKENDS build-time list vs a sign-in directory endpoint | **Owner decision.** A build-time list means a rebuild per new customer; T240c's design is a directory endpoint | Owner |
| Shared mailbox (#1562) | Done: `sprk-acme-mail@demo.spaarke.com` | — |
| DNS `external.spaarke.com` | Not started | Owner |
| `external-spa-prod` GitHub environment + `TEAMS_APP_ID` (task 043) | **Missing** (no variables) | Owner |

## Note

Today ACME users are B2B guests in the Spaarke tenant (identity preset B2BGuest). With UAC #2128 on a stamp build, a customer-tenant guest is told to use home sign-in, so ACME's workforce test needs the A9 rows above before that build reaches ACME. Provisioning has been told.
