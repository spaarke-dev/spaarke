---
description: Build and deploy a Vite/React SPA to Dataverse as a Power Pages web resource
tags: [deploy, power-pages, spa, webresource, dataverse, react, vite]
techStack: [react, typescript, vite, fluent-ui-v9, dataverse]
appliesTo: ["**/external-spa/**", "deploy power pages", "power page deploy", "deploy spa", "deploy external workspace"]
alwaysApply: false
---

# Power Page Deploy

> **Category**: Operations (Tier 3)
> **Last Updated**: March 2026

Build and deploy a Vite + React 18 SPA to Dataverse as a web resource for Power Pages hosting. This skill handles the full pipeline: build → base64 encode → Dataverse Web API create/update → publish.

---

## Quick Reference

| Item | Value |
|------|-------|
| Source | `src/client/external-spa/` |
| Build tool | Vite (`vite build`) with `vite-plugin-singlefile` |
| Deployable output | `src/client/external-spa/dist/index.html` (single self-contained HTML) |
| Deploy script | `scripts/Deploy-ExternalWorkspaceSpa.ps1` |
| Dataverse web resource name | `sprk_externalworkspace` |
| Web resource type | `1` (Webpage HTML) |
| Deploy method | Dataverse Web API (create or update) + PublishXml |
| Auth method | `az account get-access-token` (Azure CLI, not PAC CLI) |
| Target org | `https://spaarkedev1.crm.dynamics.com` |

**When to Use**:
- "deploy power pages", "power page deploy", `/power-page-deploy`
- "deploy external workspace", "deploy external spa", "deploy spa"
- After modifying files in `src/client/external-spa/`
- After completing external SPA tasks in the sdap-secure-project-module

**When NOT to Use**:
- Deploying PCF controls → Use `pcf-deploy`
- Deploying Code Page dialogs (webpack-based) → Use `code-page-deploy`
- Deploying BFF API → Use `bff-deploy`
- Deploying the internal Corporate Workspace → Use `scripts/Deploy-CorporateWorkspace.ps1` directly

---

## Prerequisites

1. **Azure CLI authenticated** to the correct subscription:
   ```bash
   az account show --query "{name:name, user:user.name}" -o tsv
   # Expected: Spaarke SPE Subscription 1  ralph.schroeder@spaarke.com
   ```
   If not: `az login`

2. **Node.js installed** with `npm` available

3. **`src/client/external-spa/.env.local` exists** with:
   ```
   VITE_BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
   VITE_PORTAL_URL=https://spaarkedev1.powerappsportals.com
   VITE_PORTAL_CLIENT_ID=<client-id from Power Pages identity provider>
   ```
   Copy from `.env.example` for first-time setup.

---

## Deployment Procedure

### Step 1: Confirm Auth

```bash
az account show --query "{name:name, user:user.name}" -o tsv
```

If wrong account: `az login` or `az account set --subscription "Spaarke SPE Subscription 1"`

### Step 2: Build the SPA

```bash
cd src/client/external-spa
npm install          # first time or after package.json changes
npm run build        # produces dist/index.html via vite + vite-plugin-singlefile
```

**Expected output:**
```
✓ 2109 modules transformed.
dist/index.html  ~800 kB │ gzip: ~220 kB
✓ built in ~7s
```

If build fails:
- Missing `.env.local` → copy from `.env.example` (VITE vars can be empty for build; they're runtime)
- TypeScript errors → fix before deploying

### Step 3: Run the Deploy Script

```bash
pwsh -File scripts/Deploy-ExternalWorkspaceSpa.ps1
```

The script automatically:
1. Gets an Azure AD access token scoped to Dataverse (`https://spaarkedev1.crm.dynamics.com`)
2. Reads `dist/index.html` and base64-encodes it
3. **Checks** if `sprk_externalworkspace` already exists in Dataverse
4. **Creates** (first deploy) or **Updates** (subsequent deploys) the web resource
5. **Publishes** via `PublishXml` action

**Expected output (first deploy):**
```
[1/5] Getting access token...      Token acquired
[2/5] Reading dist/index.html...   Read 783 KB
[3/5] Checking for existing...     Not found — creating new web resource...
[4/5] Creating web resource...     Created: {guid}
[5/5] Publishing...                Published

Deployment Complete!
Web Resource : sprk_externalworkspace
URL          : https://spaarkedev1.crm.dynamics.com/WebResources/sprk_externalworkspace
```

**Expected output (subsequent deploys):**
```
[3/5] Checking for existing...     Found: {guid} — updating...
[4/5] Updating web resource...     Updated
[5/5] Publishing...                Published
```

### Step 4: Verify

Open the web resource URL directly to confirm the SPA loads:
```
https://spaarkedev1.crm.dynamics.com/WebResources/sprk_externalworkspace
```

Expected: React SPA loads, `<title>Secure Project Workspace</title>`, no console errors.

---

## One-Liner for Rebuild + Redeploy

```bash
cd src/client/external-spa && npm run build && cd ../../.. && pwsh -File scripts/Deploy-ExternalWorkspaceSpa.ps1
```

---

## Script Reference: Deploy-ExternalWorkspaceSpa.ps1

Location: `scripts/Deploy-ExternalWorkspaceSpa.ps1`

The script contains all hardcoded values for the dev environment. To target a different environment, update these variables at the top:

| Variable | Dev Value | Notes |
|----------|-----------|-------|
| `$orgUrl` | `https://spaarkedev1.crm.dynamics.com` | Dataverse org URL |
| `$webResourceName` | `sprk_externalworkspace` | Must match existing resource (if updating) |
| `$webResourceDisplayName` | `Spaarke External Workspace SPA` | Display name in Power Apps |

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Failed to get access token` | Azure CLI not logged in | `az login` |
| `Failed to get access token` | Wrong subscription | `az account set --subscription "Spaarke SPE Subscription 1"` |
| `dist/index.html not found` | Build not run | `cd src/client/external-spa && npm run build` |
| Build produces multiple files | `vite-plugin-singlefile` not active | Verify `vite.config.ts` has `viteSingleFile()` plugin |
| SPA loads blank after deploy | Old `.env.local` values / wrong BFF URL | Check `VITE_BFF_API_URL` in `.env.local` |
| `pac pages upload-code-site` fails with YamlDotNet error | Known PAC CLI 1.46.x bug | Use `Deploy-ExternalWorkspaceSpa.ps1` instead (Web API approach) |
| 401 on Dataverse API | Token expired mid-script | Re-run script (token is re-acquired each run) |
| SPA loads but API calls fail | CORS / BFF not deployed | Deploy BFF API first (`bff-deploy`), check CORS settings |
| Power Pages auth redirect loop | Entra External ID not configured | Follow `notes/phase3-task020-entra-external-id-config.md` |

---

## Power Pages Site Setup (Manual — One-Time)

The web resource `sprk_externalworkspace` is deployed to Dataverse but still needs a Power Pages site to serve it externally. This is a one-time manual setup:

1. Go to [make.powerpages.microsoft.com](https://make.powerpages.microsoft.com)
2. Select **SPAARKE DEV 1** environment
3. Create a new site (or activate an existing inactive site)
4. In the site, create a **Page** with URL `/workspace`
5. Set the page template to embed the web resource:
   - Template type: **Custom** or **Blank**
   - Add a **Liquid** template pointing to `{{ webresource('sprk_externalworkspace') }}`
6. Configure **Entra External ID** as the identity provider (see `notes/phase3-task020-entra-external-id-config.md`)
7. Configure **Web API site settings** for all 5 Dataverse tables (see `notes/phase3-task022-web-api-site-settings.md`)
8. Configure **CSP/CORS** settings (see `notes/phase3-task023-csp-cors-security.md`)

After initial setup, all future redeployments use `Deploy-ExternalWorkspaceSpa.ps1` only — no Power Pages portal changes needed.

---

## Architecture Notes

```
Browser (external user)
  │
  ▼
Power Pages site (spaarkedev1.powerappsportals.com)
  │  serves sprk_externalworkspace web resource
  ▼
React 18 SPA (Vite, Fluent UI v9, HashRouter)
  │
  ├─── Power Pages Web API (/_api/*)
  │    └── Dataverse OData — reads projects, documents, events
  │
  └─── BFF API (spe-api-dev-67e2xz.azurewebsites.net)
       └── POST /api/v1/external-access/* — grant, revoke, invite, close
       └── GET  /api/v1/external/me       — user context + access levels
```

**Why Web API, not `pac pages upload-code-site`?**

PAC CLI's `upload-code-site` command has a `YamlDotNet` assembly version conflict in version 1.46.x that causes it to fail when no Power Pages site exists. The Dataverse Web API approach (`webresourceset` PATCH + `PublishXml`) is more reliable and matches the pattern used by all other web resource deploy scripts in this repo (`Deploy-CorporateWorkspace.ps1`, `Deploy-EventsPage.ps1`, etc.).

---

## Related Skills

| Skill | When to Use |
|-------|-------------|
| `bff-deploy` | Deploy the BFF API that the SPA calls |
| `code-page-deploy` | Deploy webpack-based Code Page dialogs (not this SPA) |
| `pcf-deploy` | Deploy PCF controls for internal Dynamics forms |
| `dataverse-deploy` | General Dataverse solution/plugin deployment |

---

## Related Files

| File | Purpose |
|------|---------|
| `scripts/Deploy-ExternalWorkspaceSpa.ps1` | The deploy script — runs the full pipeline |
| `src/client/external-spa/vite.config.ts` | Vite config with `viteSingleFile` plugin |
| `src/client/external-spa/.env.example` | Environment variable template |
| `src/client/external-spa/powerpages.config.json` | PAC CLI config (used if PAC bug is fixed) |
| `notes/phase5-task050-spa-deployment-guide.md` | Full Power Pages site setup guide |
| `notes/phase7-task075-final-deployment-validation.md` | Complete deployment runbook (all phases) |

---

## Related ADRs

| ADR | Relevance |
|-----|-----------|
| [ADR-021](../../adr/ADR-021-fluent-design-system.md) | Fluent UI v9, dark mode, semantic tokens |
| [ADR-022](../../adr/ADR-022-pcf-platform-libraries.md) | Code Pages bundle React 18 (createRoot, not platform-provided) |

---

*For Claude Code: Use this skill whenever the user asks to deploy the external workspace SPA or mentions `/power-page-deploy`. The deploy script handles create vs. update automatically — always run it after `npm run build`.*
