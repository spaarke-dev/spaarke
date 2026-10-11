# Task 043 - Production SPA build + production Teams package in CI (FR-20): notes

**Date:** 2026-10-11 - **Rigor:** STANDARD - **Status at hand-off:** code + tests done, NOT committed (the main session commits by path). **STOPPED at the POML escalation trigger:** the owner has not created the GitHub environments/variables or the production Teams app id (section 3 lists exactly what to create). POML step 5 (a `workflow_dispatch` dev run) needs owner approval and was not run; POML step 6 (TASK-INDEX / POML status / drift script) is outside this agent's edit scope.

Binding: spec FR-20; design A1 (prod appPackage, own manifest + prod Teams app id), A2 (origins `external.spaarke.com`, `brk-multihub://external.spaarke.com`), A4 (deploy to `swa-spaarke-external-spa-prod`); ADR-028 A7 (no `webApplicationInfo`, tenant-pinned workforce authority, `user_impersonation` only); owner rule 2026-10-08 "baked dev values CANNOT be part of the package".

## 1. What changed

| File | Change |
|---|---|
| `.github/workflows/deploy-external-spa.yml` | New `environment` input (`dev` default, `prod`). The job runs in GitHub environment `external-spa-dev` / `external-spa-prod`. Every `VITE_*` value is `${{ vars.<same name> }}` (no literal). New fail-closed **preflight** step before the build (required vars present; GUID / `api://{guid}/user_impersonation` / `https://` / `*.ciamlogin.com` / JSON-list formats; no value is named in the workflow). Two deploy steps: dev with `secrets.AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` (unchanged secret name), prod with `secrets.AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD`; each `if:`-guarded, no `||` fall-back between them. Trigger stays `workflow_dispatch` only. |
| `.github/workflows/deploy-teams-app.yml` | Same `environment` input and job environment. Sanity build reads the same `vars.*`. New step **Stage manifest for the target environment**: dev copies `manifest.json` byte for byte to `appPackage/build/stage/manifest.json`; prod validates `vars.TEAMS_APP_ID` (set, GUID, not all-zero) and renders `manifest.prod.json` with `jq '.id = $id'` into the same path. The validate step now runs on the **staged** file (all existing checks, including `jq -e 'has("webApplicationInfo") | not'`, plus "no unrendered `${{` placeholder"). The zip is built from `build/stage/manifest.json` + the two icons. Artifact names: dev unchanged (`teams-app-package-build`, `teams-app-package`); prod `teams-app-package-prod-build`, `teams-app-package-prod`. Deploy-job summary text differs for prod (task 073 gated). Trigger stays `workflow_dispatch` only. |
| `src/client/external-spa/appPackage/manifest.prod.json` (new) | Own `id` = `${{TEAMS_APP_ID}}` placeholder; `staticTabs` `contentUrl`/`websiteUrl` = `https://external.spaarke.com`; `validDomains` = `external.spaarke.com`, `teams.microsoft.com`, `*.cloud.microsoft`; **no** `webApplicationInfo`; `version` 1.0.0; same icons, name, description, tab `entityId`, `permissions` as dev. |
| `src/client/external-spa/appPackage/env.prod.json` (new) | Toolkit-style environment file (not in the zip): `SPA_BASE_URL` = `https://external.spaarke.com`; `TEAMS_APP_ID`, `AAD_APP_CLIENT_ID`, `AAD_APP_TENANT_ID` are all-zero placeholders (never a real id). No customer BFF URL/scope (chosen at run time, FR-17). |
| `src/client/external-spa/.env.production` | Header comment now says every value comes from the GitHub environment variables; added the optional `VITE_BACKENDS=#{BACKENDS}#` placeholder (treated as unset). |
| `src/client/external-spa/tests/production-package.test.ts` (new, 15 tests) | Shipped-config guard (see section 5). |
| `tests/backend-selection.test.ts`, `tests/workforce-auth.test.ts` | Four existing shipped-config assertions updated for the new layout: the workflows' values are `${{ vars.KEY }}` expressions, not literals (the scope-format assertion moved to the preflight step, asserted by `toContain`), and the `webApplicationInfo` check runs in `build/stage`. The `(\S+)` value capture in the "EVERY workflow" test became `(.+?)` (a `${{ vars.X }}` value contains spaces). |

Not touched: `manifest.json`, `env.dev.json` (dev outputs unchanged), TASK-INDEX, POMLs, `.claude/`, .NET.

## 2. Decisions

1. **Environment variables, one name per value, resolved per environment.** The same `VITE_*` names exist in both GitHub environments, so the workflows contain no `dev`/`prod` branching for values and no identifier at all. Vite already prefers real process env over `.env.production`, so no token-substitution step (the `#{...}#` placeholders in `.env.production` stay as the "unset" marker).
2. **Two deploy steps, not one expression.** `a && secretA || secretB` would deploy a prod build to the dev SWA with the dev token if the prod secret were empty. Each step is `if:`-guarded; an empty prod token makes the deploy action fail. The dev secret keeps its existing name (`AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV`, repository or environment scope both resolve), so dev needs no secret change.
3. **The SWA is selected by the token.** The Azure SWA deployment token belongs to exactly one SWA, so "deploy to `swa-spaarke-external-spa-prod`" = the owner creates `AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD` from that SWA's token. The SWA name cannot be a literal here (and the workflow cannot verify which SWA a token belongs to): the owner must take the token from `swa-spaarke-external-spa-prod` only.
4. **Preflight in the SPA workflow, not in the Teams workflow.** The Teams sanity build produces nothing that is shipped (only `manifest` + icons are packaged), so it builds with whatever the environment holds. The SPA build is deployed, so it fails closed on a missing/malformed variable. Prod additionally fails if no backend is configured (`VITE_BACKENDS` or `VITE_BFF_API_URL`).
5. **Prod manifest id is substituted, not committed.** `${{TEAMS_APP_ID}}` follows the Toolkit placeholder convention used by `env.*.json`. The Teams workflow refuses an unset, non-GUID or all-zero id and refuses to package a manifest that still contains a `${{`. (In a workflow `run:` block a literal `${{` is parsed as an expression - the guard uses `[$][{][{]`.)
6. **Prod `validDomains` is minimal** (`external.spaarke.com`, `teams.microsoft.com`, `*.cloud.microsoft`). Dev also lists its BFF host; on prod the BFF is per customer and chosen at run time (FR-17), so no customer host is listed. **Open question for the owner / T240c:** if a Teams webview check requires each customer BFF host in `validDomains`, the list must be extended per customer (or the manifest rendered from a variable) - not verified here, it needs the first prod install.
7. **`webApplicationInfo`:** neither manifest has it; the jq guard (A7) runs on the staged manifest of either environment, and tests assert the property is absent from both committed manifests.
8. **Dependabot PR #909** (setup-node 4 -> 7, touches both workflows) is still OPEN: its change is kept out of this diff (`setup-node@v4` / `@v6` left as they were). Expect a trivial rebase conflict on those one-line `uses:` lines when #909 merges.
9. **Prod Teams package version** starts at `1.0.0` (a new app id; dev is `1.2.0`).

## 3. OWNER STEPS (nothing below was created by this task)

The dev workflow dispatch **fails at the preflight step** (and a Teams dev package would be built from empty variables) until the `external-spa-dev` variables exist - create them **before** the next dispatch/merge-to-use.

### 3.1 Environment `external-spa-dev` (values = what the workflows used to hard-code)

| Kind | Name | Dev value |
|---|---|---|
| Variable | `VITE_BFF_API_URL` | `https://spaarke-bff-dev.azurewebsites.net` |
| Variable | `VITE_BACKENDS` | leave unset (optional JSON list; dev used the one-entry fallback) |
| Variable | `VITE_MSAL_AUTHORITY` | `https://spaarkeextid.ciamlogin.com/7052feba-bfc4-43e0-b09e-65014b429131` |
| Variable | `VITE_MSAL_CLIENT_ID` | `bd57e54e-b339-4500-b55c-e451009fd907` |
| Variable | `VITE_MSAL_TENANT_ID` | `7052feba-bfc4-43e0-b09e-65014b429131` |
| Variable | `VITE_MSAL_BFF_SCOPE` | `api://4a4d5126-91b0-4865-8e3a-134b7209013e/user_impersonation` |
| Variable | `VITE_TEAMS_MSAL_CLIENT_ID` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` (INTERIM - the dev BFF app id until the "Spaarke Workspace" client exists; task 040 notes section 5) |
| Variable | `VITE_TEAMS_MSAL_TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Variable | `VITE_TEAMS_MSAL_BFF_SCOPE` | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |
| Secret | `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` | already exists as a repository secret; nothing to do (may optionally be moved into the environment) |

(Repository-level variables with the same names would also resolve for dev, but the intent is one environment per target.)

### 3.2 Environment `external-spa-prod`

| Kind | Name | Value |
|---|---|---|
| Variable | `VITE_BFF_API_URL` | owner supplies (one-entry fallback) - or set `VITE_BACKENDS` instead; at least one of the two is required |
| Variable | `VITE_BACKENDS` | optional JSON list `[{"customerKey","displayName","bffBaseUrl","bffAppId","ciamBffAppId"?}]` - owner supplies until the T240c directory ships |
| Variable | `VITE_MSAL_AUTHORITY` | owner supplies - `https://{subdomain}.ciamlogin.com/{ciam tenant id}` of the production CIAM tenant |
| Variable | `VITE_MSAL_CLIENT_ID` | owner supplies - production CIAM SPA client (after spike S1 / T240d) |
| Variable | `VITE_MSAL_TENANT_ID` | owner supplies - production CIAM tenant id (GUID) |
| Variable | `VITE_MSAL_BFF_SCOPE` | owner supplies - `api://{customer BFF app id}/user_impersonation` (only that scope is accepted, ADR-028 A7) |
| Variable | `VITE_TEAMS_MSAL_CLIENT_ID` | owner supplies - the production single-tenant Spaarke workforce client |
| Variable | `VITE_TEAMS_MSAL_TENANT_ID` | owner supplies - Spaarke's Entra tenant id (GUID; `organizations`/`common` are refused) |
| Variable | `VITE_TEAMS_MSAL_BFF_SCOPE` | owner supplies - `api://{customer BFF app id}/user_impersonation` |
| Variable | `TEAMS_APP_ID` | owner supplies - the production Teams app id (GUID, not the all-zero placeholder) |
| Secret | `AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD` | owner supplies - the deployment token of `swa-spaarke-external-spa-prod` (and only that SWA) |

Recommended protection rules (the POML does not require them; they make a prod dispatch a deliberate act): required reviewers on `external-spa-prod`; deployment branches limited to `master`.

### 3.3 Outside GitHub (needed before the prod build is useful; not needed for the artefacts to build)

- Register on the production workforce client the SPA redirect URIs `brk-multihub://external.spaarke.com` and `https://external.spaarke.com/auth-callback.html` (design A2) and send its client id to provisioning (H3).
- Production CIAM SPA client redirect `https://external.spaarke.com` and `https://external.spaarke.com/auth-callback.html` (T240d / spike S1).
- `swa-spaarke-external-spa-prod` + custom domain + DNS for `external.spaarke.com`; every customer backend lists that one origin in CORS (provisioning 240a). Go-live itself is task 073.

## 4. Acceptance evidence

| Criterion | Status | Evidence |
|---|---|---|
| Both workflows pass actionlint | **Partly met** - `actionlint` is not installed here | Both files parse with js-yaml 4.3.2 (triggers `workflow_dispatch` only; jobs `build_and_deploy` / `build_and_package`+`deploy`); the preflight and stage `run:` scripts were extracted from the parsed YAML and executed under bash (section 6). **Run `actionlint` in the PR check / locally before merging.** |
| Dev run produces the same SPA and Teams package as before | **Waiting on owner** (step 5 needs approval + the dev environment) | Static: dev staging is `cp manifest.json build/stage/manifest.json` (verified byte-identical with `cmp`), same zip members (`manifest.json`, `color.png`, `outline.png`), dev artifact names unchanged; dev values equal the old literals (section 3.1). Live verification plan in section 7. |
| Prod path builds from prod-environment values only; grep of both workflows finds no URL, client id or scope literal | **Met (static)**; the live prod build waits on the owner | `production-package.test.ts`: no GUID (other than the all-zero compare), no `azurewebsites.net` / `azurestaticapps.net` / `external.spaarke.com` / `login.microsoftonline.com`, no `https://host` URL, no `api://<guid>` in either workflow (comments excluded); every `VITE_*` is `${{ vars.<same> }}`. |
| Prod Teams package contains `manifest.prod.json` content with `external.spaarke.com`, no `webApplicationInfo` | **Met (static)**; the live package waits on `TEAMS_APP_ID` | Tests on `manifest.prod.json` (origin on tab + domains, no dev host, no `webApplicationInfo`, id placeholder != dev id); the stage script was executed with a jq shim and a sample GUID: rendered `id` = the GUID, `contentUrl` = `https://external.spaarke.com`, no `webApplicationInfo`; unset / non-GUID / all-zero ids exit 1. |
| The owner's variable/secret list is in the task notes | **Met** | Section 3. |

## 5. Tests

`tests/production-package.test.ts` (15): manual-only trigger + `dev|prod` input defaulting to dev (both workflows); job environment expression (both); no identifier/URL/scope literal (both); every `VITE_*` is `vars.<same name>` (both); deploy steps use the matching secret with no `||` fall-back; the preflight runs before the build; the Teams stage/render/zip/artifact-name contract; `manifest.prod.json` shape; `manifest.json` (dev) stays on the dev SWA; `env.prod.json` placeholders; render result has no placeholder left.
**Fail before / pass after:** against the HEAD workflows (restored after the run) the three suites ran **15 failed / 143 passed** (11 in the new file, 2 in `workforce-auth.test.ts`, 2 in `backend-selection.test.ts`); with the change: all pass.

## 6. Command results (in `src/client/external-spa`)

- `npm run typecheck` - clean. `npm run lint` - clean (0 problems).
- `npx vitest run` - **11 files, 247 tests passed** (was 232 before the 15 new tests).
- `npm run build` - succeeded (existing chunk-size warning only).
- Manifests: `manifest.json`, `manifest.prod.json`, `env.dev.json`, `env.prod.json` all parse as JSON; none has `webApplicationInfo`.
- `actionlint` and `jq` are not installed on this machine: workflows validated with js-yaml, the two shell scripts run under bash with a `jq` shim for `.id = $id` (preflight: valid / backends-only / nothing set / bad tenant+scope+backends -> exit 0 / 0 / 1 / 1).
- (Side effect, cleaned up: the Windows `python3` shim auto-installed a Python into an untracked `Python/` folder in the worktree root while the preflight ran locally; I deleted it. On `ubuntu-latest` `python3` is real.)

## 7. Dev-path verification plan (POML step 5 - owner approval required, not run)

1. Create `external-spa-dev` (section 3.1).
2. Dispatch **Deploy Teams App Package** with `environment=dev` (builds and publishes an artifact only; nothing is deployed): download `teams-app-package`, `unzip -l` shows exactly `manifest.json`, `color.png`, `outline.png`; `diff <(unzip -p appPackage.zip manifest.json) src/client/external-spa/appPackage/manifest.json` is empty.
3. Dispatch **Deploy External SPA** with `environment=dev`: preflight passes; deploy lands on `swa-spaarke-external-spa-dev`; the deployed bundle contains the section 3.1 dev values (same as the previous build) and `auth-callback.html` + `staticwebapp.config.json` are present.
4. Prod dry run once section 3.2 exists: dispatch the Teams workflow with `environment=prod` first (artifact only): `unzip -p` the manifest shows the owner's app id, `https://external.spaarke.com`, no `webApplicationInfo`. Dispatch the SPA prod run only when go-live (task 073) is approved - it deploys to `swa-spaarke-external-spa-prod`.

## 8. Rollback

Revert the commit. Dev is restored by the old literal values (git history of both workflows; they equal the section 3.1 table) and the unchanged `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` secret. The new files (`manifest.prod.json`, `env.prod.json`, `production-package.test.ts`) are additive. A bad prod deploy is rolled back by re-deploying the previous build to the prod SWA (no dev data is involved); the owner-created GitHub environments/variables/secrets can stay.

## 9. Noticed, not changed

- `src/client/external-spa/README.md` (Environment Variables section) says the values come from `.env.example`; it does not mention the GitHub environments - a one-line pointer to this file would help.
- The dev manifest `id` and URLs in `appPackage/manifest.json` are literals (not rendered from `env.dev.json`); `env.dev.json` is documentation only. Left as is to keep dev output unchanged.
- The Teams workflow's sanity build does not run the SPA preflight (decision 4).
