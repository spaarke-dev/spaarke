# Task 076 — Teams package and registration for A9

Date: 2026-10-19. Rigor: **FULL**. The POML says STANDARD, but the task carries the `auth` tag, so it gets two adversarial passes. Model: Opus (session).

## What changed

| File | Change |
|---|---|
| `.github/workflows/deploy-teams-app.yml` | Adds a **"Check required environment variables"** preflight before install, build and packaging. It is a copy of `deploy-external-spa.yml`'s check and now requires `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`: it must be set, be a GUID, and differ from `VITE_TEAMS_MSAL_CLIENT_ID`, case-insensitive. The build step now passes `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`. The manifest validation also checks that every `staticTabs[].contentUrl` is https and that its host is in `validDomains`. The A8 comments now say A9: the Teams sign-in uses the multitenant client, and the A8 client is used only for the guest-token link. |
| `appPackage/env.dev.json`, `env.prod.json` | `$comment` rewritten for A9. Adds a `WORKFORCE_HOME_CLIENT_ID` slot holding the all-zero placeholder: dev is filled after task 059 registers the client; prod is the shared multitenant client, supplied by the owner or provisioning. `AAD_APP_CLIENT_ID` stays as the A8 client (guest-token link only). Nothing reads these files (grep), and neither is in the zip. |
| `tasks/059-dev-entra-and-settings-a9.poml` | The multitenant client's redirect list now includes `{origin}/auth-callback.html` (the 058 silent retry and the 075 popup land there: `POPUP_REDIRECT_PATH`, `msal-config.ts:60`), plus the localhost equivalent. Before, it listed only the origin, localhost and `brk-multihub`. |

**`manifest.json` / `manifest.prod.json`: unchanged, deliberately.**
- A Teams manifest names a client only through `webApplicationInfo`, which A8/A9 forbid (and the AADSTS700016 lesson).
- The multitenant client therefore reaches Teams through the bundle, not the manifest.
- The SPA domain is already in `validDomains` for both environments.
- Changing nothing means no manifest version bump and no catalog re-approval.

So design §12's "the manifest gains the client and domain" is met this way (the design note is now corrected):
- **domain:** present, and now checked by CI;
- **client:** in the SPA bundle that `deploy-external-spa.yml` ships to the Static Web App, whose preflight is the authoritative check. The Teams workflow's own build is a sanity build that is thrown away (the zip holds only the manifest and icons), so its preflight stops a package from being published from a mis-configured environment but cannot vouch for the deployed bundle;
- in the manifest: deliberately nothing.

## Verification

- **actionlint 1.7.7** (pinned release; SHA-256 checked against the release checksums) exits 0 on `deploy-teams-app.yml` and `deploy-external-spa.yml`. shellcheck is not installed, so the `run:` scripts were executed instead (below).
- **Preflight**: extracted from the parsed YAML and run under `bash -eo pipefail` with synthetic GUIDs:

  | Case | Exit |
  |---|---|
  | all valid | 0 |
  | home client missing | 1 |
  | home client not a GUID | 1 |
  | home client = A8 id (other case) | 1 |
  | A8 client missing | 1 |
  | `VITE_BACKENDS` only | 0 |
  | no backend | 1 |

- **Stage → validate → icons → package → verify**, with jq 1.7.1 (pinned, SHA-256 checked) and a Python zip shim:
  - dev: 3-file zip, exit 0. The zipped `manifest.json` is byte for byte the source. `validDomains` = `green-dune-0c4f1221e.7.azurestaticapps.net`, `spaarke-bff-dev.azurewebsites.net`, `teams.microsoft.com`, `*.cloud.microsoft`. `webApplicationInfo` absent.
  - prod (synthetic `TEAMS_APP_ID`): exit 0. id rendered, `validDomains` = `external.spaarke.com`, `teams.microsoft.com`, `*.cloud.microsoft`.
  - Negatives, each exit 1 before packaging, with the expected message:
    - SPA host removed from `validDomains`;
    - `webApplicationInfo` added;
    - `http://` contentUrl;
    - prod with no `TEAMS_APP_ID`.
- **Vite sanity build** with synthetic values and the home client set: built, the home id is in `dist/assets/app.js`, and `check-dist` is clean. `dist/` was deleted afterwards.
- No workflow run (none needed; a run is owner-approved and would wait on 059's variable).

## Provisioning

Sent 2026-10-19 to `spaarke-wt-customer-provisioning-orchest-bb`. It confirms their A9 mapping items 5 and 7, from `notes/coordination/2026-10-13-from-spa-r3-adr028-a9-workforce-signin.md`:
- **Item 5**: confirmed, with one **addition**. The production multitenant client needs `https://external.spaarke.com/auth-callback.html` as well as the origin and `brk-multihub://external.spaarke.com`.
- **Item 7**: Teams admin approval is a manual onboarding gate, confirmed. The workflow builds only; catalog publishing is an owner or customer-admin action.

## Owner setup this adds (for 059 and production)

- `external-spa-dev`: `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` = the dev multitenant client (059 step 7), and `VITE_BACKENDS` with `homeTenantIds` (059 step 7b). Until the client id is set, the Teams workflow fails at the preflight, as `deploy-external-spa.yml` already does. No preflight checks `homeTenantIds`.
- `external-spa-prod`: the same variable = the shared production multitenant client.

## Review (two adversarial passes, Opus) and fix round 1

Pass 1 (pipeline soundness) and pass 2 (identity contract and documentation) ran in parallel on the first change set.

| # | Class | Finding | Fix |
|---|---|---|---|
| 1 | F1 | `match()` used `grep -Eq`, which matches line by line: a value such as `"<guid>
junk"` passed the GUID, scope and authority checks in **both** workflows (the same flaw as in `deploy-external-spa.yml` since 043). The SPA then throws at run time. | `match()` uses bash `[[ ${!1} =~ $2 ]]` (whole string) in both workflows. Re-run: newline cases for the home id, scope and authority now exit 1 in both. |
| 2 | F1 | 059 never set `VITE_BACKENDS` with `homeTenantIds`: the one-entry `VITE_BFF_API_URL` fallback has none, so every A9 sign-in (browser and Teams) would show "not set up". | 059 gains step 7b and a criterion: one entry with `customerKey` `default`, workforce `bffAppId`, `ciamBffAppId` (without it CIAM would request the workforce app, AADSTS500207) and `homeTenantIds` [bc3aa7f4], then an SPA redeploy. |
| 3 | F3 | No test guarded the Teams preflight or the new manifest checks. | `production-package.test.ts`: the preflight test runs for both workflows and requires the home client and the whole-string match; a new test asserts the webApplicationInfo, https and validDomains checks sit before "Package Teams app", and `need TEAMS_APP_ID` in the preflight; `env.prod.json` `WORKFORCE_HOME_CLIENT_ID` stays a placeholder. |
| 4 | F3 | Comments claimed the preflight protects the shipped tab, and that the SPA "refuses a build" (it refuses at run time). | Reworded in both workflows; this note corrected. |
| 5 | F4 | The env `$comment`s dropped the A8 client's redirects (`brk-multihub://{host}`, `/auth-callback.html`), still needed by the Teams NAA silent link and the link popup. | Restored, with "do not prune them". |
| 6 | F4 | "nothing reads this file" was false (`production-package.test.ts` reads `env.prod.json`). | "no build or packaging step reads this file". |
| 7 | K→fixed | The prod `TEAMS_APP_ID` check ran only after the build. | Also checked in the Teams preflight (the stage step keeps its check). |
| 8 | F4 | "Used only for the guest-token link" sat over all three `VITE_TEAMS_MSAL_*`; only the client id is link-only (the tenant id also excludes Spaarke's tenant from `homeTenantIds`; the scope feeds the fallback). | Comment split per variable. |
| 9 | F4 (075 drift) | Stale text still said the A8 client serves the Teams tab or the home client is browser-only. The `getWorkforceHomeClientConfig` error told a Teams user the variable is "required only" for the browser. | `config.ts` (error message, two doc comments; removed `getTeamsWorkforceEnvConfig`, which had no callers and a false comment), `msal-auth.ts:721`, `vite-env.d.ts`, `.env.example`, and `a9-bff-design.md` §8.2 (redirects) and §12 (manifest wording). |

Re-check after the fixes:
- actionlint: exit 0.
- Preflight re-run on both workflows: 11 cases each, including the newline and prod `TEAMS_APP_ID` cases.
- SPA: typecheck and lint clean. `production-package.test.ts` passes 24/24. ExternalSpa ArchTests (the source scan) pass 231/231.
- Full vitest: the first run had 1 failure in 1191. The log kept only the summary, so its name is not known. The rerun passed 1191/1191, and the failure did not reproduce. The change set touches no runtime logic besides one error-message string.

## Known limits

- **Unverified live:** NAA plus `brk-multihub` on the multitenant client, Teams mobile, and per-customer admin approval. Task 071 checks them live.
- **K2:** a package is not tied to a bundle version. Run `deploy-external-spa.yml` for the same environment before publishing a Teams package that depends on it (074/071 order: 059 → SPA deploy → package).
- **K3:** `vars.*` falls back to repository- or organisation-level variables, so a repo-level value could fill a missing environment variable and no preflight can tell. Owner check: none of these names should exist at repo or org scope.
- **K4:** the manifest jq checks reject (never admit) some valid shapes Spaarke does not use: a built-in tab with no `contentUrl`, an uppercase or trailing-dot host, a host covered only by a wildcard `validDomains` entry.
- **K:** both preflights require the two `*_BFF_SCOPE` variables even when `VITE_BACKENDS` is set (stricter than needed; identical in both workflows).
