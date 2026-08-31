# Task 044 — Pillar B add-in deploy: verification + operator runbook

> **Status**: OPERATOR-GATED (deploy + live smoke cannot run headless). Pre-deploy verification done 2026-08-31.
> **Why gated**: production build needs operator env secrets; SWA publish is an outward-facing push to a shared origin; Success Criterion 7 (Entra NAA sign-in in a live Office client) requires a human at an Office host.

## Pre-deploy verification (done, this session)

| Check | Result |
|---|---|
| Add-in code (040 + 042 + auth-v4 fix `77f61574b`) on master | ✅ `git log origin/master..HEAD -- src/client/office-addins/` is **empty** — all merged |
| Manifest origin parameterized (no hardcoded SWA origin) | ✅ Source manifests use `https://localhost:3000`; webpack templates → `ADDIN_BASE_URL` (defaults to SWA origin in prod, `webpack.config.js:53-54`). `spaarke.com` refs are only `websiteUrl`/`privacyUrl`/`termsOfUseUrl` branding — correct |
| Toolchain present | ✅ `az` authenticated (ralph.schroeder@spaarke.com / Spaarke Dev); SWA CLI 2.0.7 |
| Production build runnable headless | ❌ webpack bails (`webpack.config.js:22` `missingVars`) without `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, `BFF_API_BASE_URL` — secrets held by the deploy environment, not sourced here |

## Deploy target

| Resource | Value |
|---|---|
| Static Web App | `spaarke-office-addins` |
| Resource group | `spe-infrastructure-westus2` |
| Origin | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` |
| Intended mechanism | GitHub Actions `deploy-office-addins.yml` (holds the env secrets; auditable) — per `deploy-results-2026-08-13.md` |

## Operator procedure (run when at a live Office host for UAT)

1. **Deploy** — dispatch the workflow (preferred; has the secrets):
   ```bash
   gh workflow run deploy-office-addins.yml --ref master
   gh run watch $(gh run list --workflow=deploy-office-addins.yml -L1 --json databaseId --jq '.[0].databaseId')
   ```
   *(Direct-deploy alternative for dev iteration — only with the four env vars exported: `.\scripts\Deploy-OfficeAddins.ps1` per the `office-addins-deploy` skill.)*
2. **Verify served manifest** carries the real origin (cache-busted):
   ```bash
   curl "https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/manifest.xml?v=$(date +%H%M%S)" | grep -iE 'localhost|azurestaticapps'
   ```
   Expect `azurestaticapps` origin, **no** `localhost`. Bump manifest version before re-uploading to M365 Admin Center (M365 rejects same-version re-uploads).
3. **Runtime smoke (Success Criterion 7)** — sideload the deployed add-in in Outlook (and Word), sign in via Entra NAA, make one JSON BFF call. Confirm sign-in succeeds and the call returns 2xx.
4. **On sign-in failure** — STOP + escalate (POML §escalation): treat as Entra registration / SWA origin / consent config, not a code fix. Record consent/redirect/scope/origin detail here; do **not** redeploy on repeat.
5. **On success** — append the deployment record (target SWA, URL, manifest URLs, timestamp, manifest version) below, then flip TASK-INDEX 044 → ✅ and re-run the 090 wrap-up README→Complete flip.

## Deployment record

- **2026-08-31 14:12 UTC** — operator dispatched `deploy-office-addins.yml` (from `master`); run `33401242927` **completed / success** (build + SWA deploy). URL: https://github.com/spaarke-dev/spaarke/actions/runs/33401242927
- **Served manifests verified** (cache-busted curl):
  - `outlook/manifest.json` → 200, version **1.0.20**, functional origin = `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` (no `localhost`; `spaarke.com` = branding only) ✅
  - `word/manifest.xml` → 200, version **1.0.4.0**, origin = deployed SWA ✅
  - `outlook/taskpane.html` → 200 ✅
- **Note**: Word manifest is served at `word/manifest.xml` (not `word/word-manifest.xml`).

### STILL PENDING — operator live smoke (Success Criterion 7)
The only remaining step is interactive and needs a live Office host:
1. Sideload the Outlook add-in — either upload `outlook/manifest.json` in **M365 Admin Center → Integrated Apps → Upload custom app** (bump version if re-uploading; propagation 5–15 min), OR sideload directly in **Outlook on the web → Get Add-ins → My add-ins → Add a custom add-in → Add from URL**: `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/manifest.json`.
2. Open the add-in taskpane, **sign in** (Entra NAA), confirm the taskpane loads and a BFF call succeeds.
3. Repeat for Word (`https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml`).
4. On success → flip TASK-INDEX **044 → ✅** + close 090 (README→Complete). On sign-in failure → STOP, treat as Entra/consent/origin config, record detail here.
