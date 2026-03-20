---
description: Build and deploy a Vite/React SPA to Power Pages as a Code Site
---

# Power Page Deploy

> **Category**: Operations (Tier 3)
> **Last Updated**: March 2026

Build and deploy a Vite + React 18 SPA to Power Pages as a **Code Site**. This skill handles the full pipeline: build → `pac pages upload-code-site` → portal update.

---

## Quick Reference

| Item | Value |
|------|-------|
| Source | `src/client/external-spa/` |
| Build tool | Vite (`vite build`) — IIFE format |
| Deployable output | `dist/assets/app.js` (~1 MB IIFE) + `dist/index.html` (entry HTML) |
| Deploy script | `scripts/Deploy-PowerPages.ps1` |
| Deploy command | `pac pages upload-code-site` |
| Site root (new format) | `src/client/external-spa/.site-download/spaarke-external-workspace/` |
| Portal name | `Spaarke External Workspace` |
| Portal URL | `https://sprk-external-workspace.powerappsportals.com` |
| Website ID | `a79315b5-d91e-4e27-b016-439ad439babe` |
| Deploy method | Power Pages Code Site (`mspp_webfiles` in Dataverse) |
| Auth method | PAC CLI (`pac auth create --url https://spaarkedev1.crm.dynamics.com`) |
| Target org | `https://spaarkedev1.crm.dynamics.com` |
| PAC CLI version | 2.4.1+ (new `.powerpages-site/` format required) |

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

## Architecture

```
src/client/external-spa/
├── src/                        ← React source files
├── dist/                       ← Output of `npm run build`
│   ├── index.html              ← Small HTML entry point (~776 bytes)
│   └── assets/
│       └── app.js              ← IIFE bundle (~1 MB, all JS inlined)
├── .site-download/             ← New-format Code Site workspace (PAC CLI 2.4.x)
│   └── spaarke-external-workspace/
│       └── .powerpages-site/   ← PAC CLI new format portal metadata + web-files
│           ├── web-files/      ← Existing portal files (app.js, index.html, etc.)
│           └── ...             ← Page templates, web roles, site settings
├── powerpages.config.json      ← Legacy site config (kept for reference)
└── .env.production.local       ← VITE_DEV_MOCK=false + real credentials
```

**Portal**: `https://sprk-external-workspace.powerappsportals.com`
**Storage**: Files stored as `mspp_webfiles` in Dataverse, content in Azure Blob Storage.
**Deployment**: `pac pages upload-code-site` is the ONLY way to update the portal content.

> ⚠️ `Deploy-ExternalWorkspaceSpa.ps1` deploys to a **DIFFERENT** target — the Dataverse web resource `sprk_externalworkspace` (accessible at `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_externalworkspace` for internal/admin use only). It does **NOT** update the public portal at `sprk-external-workspace.powerappsportals.com`.

---

## Prerequisites

1. **PAC CLI authenticated** to the correct environment:
   ```powershell
   pac auth list
   # Should show: ralph.schroeder@spaarke.com → spaarkedev1
   ```
   If expired or not present:
   ```powershell
   pac auth create --url https://spaarkedev1.crm.dynamics.com
   # Opens browser for interactive login
   ```
   PAC CLI tokens expire — re-auth whenever you see "unauthorized" errors or uploads complete silently without updating the portal.

2. **Node.js installed** with `npm` available

3. **`src/client/external-spa/.env.production.local` exists** with real values:
   ```
   VITE_BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
   VITE_MSAL_CLIENT_ID=f306885a-8251-492c-8d3e-34d7b476ffd0
   VITE_MSAL_TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
   VITE_MSAL_BFF_SCOPE=api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access
   VITE_DEV_MOCK=false
   ```
   > **Critical**: `.env.local` has `VITE_DEV_MOCK=true` for local dev. `.env.production.local` must override it with `VITE_DEV_MOCK=false` or mock mode ships to the portal.

4. **`.site-download/spaarke-external-workspace/` exists** in the `external-spa` folder.
   This is the new-format Code Site workspace created by `pac pages download-code-site`.
   It is committed to the repo — if missing on a fresh clone, the deploy script recreates it automatically.

---

## Deployment Procedure

### Recommended: Use the Deploy Script

Run from the **repository root**:

```powershell
.\scripts\Deploy-PowerPages.ps1
```

The script handles everything:
1. `npm run build` in `src/client/external-spa`
2. Checks for `.site-download/` folder (recreates via `download-code-site` if missing)
3. `pac pages upload-code-site` with correct paths

**Skip build** if already built:
```powershell
.\scripts\Deploy-PowerPages.ps1 -SkipBuild
```

---

### Manual Steps (if needed)

#### Step 1: Confirm PAC CLI Auth

```powershell
pac auth list
```

Expected output: a profile for `ralph.schroeder@spaarke.com` with org `spaarkedev1`.

If no profile or token expired:
```powershell
pac auth create --url https://spaarkedev1.crm.dynamics.com
```

#### Step 2: Build the SPA

```bash
cd src/client/external-spa
npm run build
```

**Expected output:**
```
vite v5.4.x building for production...
✓ 2270 modules transformed.
dist/index.html       0.77 kB │ gzip:   0.43 kB
dist/assets/app.js  1,012.92 kB │ gzip: 274.41 kB
✓ built in ~8s
```

Verify `VITE_DEV_MOCK=false` is active (set in `.env.production.local`).

#### Step 3: Upload to Power Pages Code Site

Run from the **repository root**:

```powershell
pac pages upload-code-site `
    --rootPath "src/client/external-spa/.site-download/spaarke-external-workspace" `
    --compiledPath "src/client/external-spa/dist" `
    --siteName "Spaarke External Workspace"
```

> ⚠️ The `--rootPath` must point to `.site-download/spaarke-external-workspace` (the new-format folder with `.powerpages-site/` inside), **not** to `src/client/external-spa` directly. Pointing to `external-spa` uses the old `powerpages.config.json` format and will fail with PAC CLI 2.4.x.

**Expected output**: PAC CLI reports files uploaded. Portal updates within ~30–60 seconds.

#### Step 4: Verify

Open `https://sprk-external-workspace.powerappsportals.com` in a private browser.
Expected: SPA loads, prompts for login, shows Secure Project Workspace.

---

## One-Liner for Rebuild + Redeploy

```powershell
.\scripts\Deploy-PowerPages.ps1
```

Or manually:

```powershell
cd src/client/external-spa; npm run build; cd ..\..\..; pac pages upload-code-site --rootPath "src/client/external-spa/.site-download/spaarke-external-workspace" --compiledPath "src/client/external-spa/dist" --siteName "Spaarke External Workspace"
```

---

## PAC CLI Format Migration (One-Time — Already Done)

PAC CLI 2.4.x introduced a new Code Site format (`.powerpages-site/` folder structure) that replaces the old format (single `powerpages.config.json`). This migration was performed once in March 2026.

**What was done**:
```powershell
# 1. Download in new format
pac pages download-code-site `
    --webSiteId a79315b5-d91e-4e27-b016-439ad439babe `
    --path "src/client/external-spa/.site-download" `
    --overwrite

# 2. Upload using new format folder as rootPath
pac pages upload-code-site `
    --rootPath "src/client/external-spa/.site-download/spaarke-external-workspace" `
    --compiledPath "src/client/external-spa/dist" `
    --siteName "Spaarke External Workspace"
```

The `.site-download/spaarke-external-workspace/` folder is committed to the repo. Future uploads always use this folder as `--rootPath`.

**If you need to re-run the download** (e.g., portal metadata has changed significantly):
```powershell
pac pages download-code-site `
    --webSiteId a79315b5-d91e-4e27-b016-439ad439babe `
    --path "src/client/external-spa/.site-download" `
    --overwrite
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `pac auth list` shows expired or empty | PAC CLI token expired | Run `pac auth create --url https://spaarkedev1.crm.dynamics.com` |
| Upload completes silently, portal unchanged | Auth token expired — upload silently skipped | Re-auth with `pac auth create`, then re-upload |
| `Error: upload-code-site with old format is not supported` | `--rootPath` points to old format folder | Use `--rootPath "src/client/external-spa/.site-download/spaarke-external-workspace"` |
| `.site-download/` folder missing (fresh clone) | Folder not in repo | Run `pac pages download-code-site --webSiteId a79315b5-... --path ".site-download" --overwrite` from `external-spa/`, or just run `.\scripts\Deploy-PowerPages.ps1` (auto-downloads) |
| Portal shows blank page | JS not loading or mock mode shipped | Check browser console for errors; verify `VITE_DEV_MOCK=false` in `.env.production.local` |
| Portal shows old content | Upload succeeded but CDN cache | Hard refresh (Ctrl+Shift+R); wait 30 seconds |
| `app.js` not updating in portal | Upload skipped (auth issue) | Re-auth and re-upload; verify in Dataverse that `mspp_webfiles` `app.js` `modifiedon` changes |
| Build produces placeholder values | `.env.production.local` missing | Create file with real MSAL/BFF values — see Prerequisites |

---

## Local Development

For local development WITHOUT deploying to the portal:

```bash
cd src/client/external-spa
npm run dev   # http://localhost:3000 with VITE_DEV_MOCK=true
```

- `.env.local` sets `VITE_DEV_MOCK=true` — bypasses MSAL auth and uses mock data
- The `/_api` proxy forwards to the real Power Pages portal if `VITE_PORTAL_URL` is set
- The `/api` proxy forwards BFF API calls to `spe-api-dev-67e2xz.azurewebsites.net`

---

## Web Resource Alternative (Internal Admin Use Only)

`scripts/Deploy-ExternalWorkspaceSpa.ps1` deploys an inlined (HTML+JS) version to the Dataverse web resource `sprk_externalworkspace`.
**This does NOT update the portal.** It's only useful for testing the SPA at:
`https://spaarkedev1.crm.dynamics.com/WebResources/sprk_externalworkspace` (requires Dynamics login).

---

## Related Files

| File | Purpose |
|------|---------|
| `scripts/Deploy-PowerPages.ps1` | **Main deploy script** — build + upload to Code Site |
| `src/client/external-spa/vite.config.ts` | Vite IIFE build config, dev proxy setup |
| `src/client/external-spa/.site-download/spaarke-external-workspace/` | New-format Code Site workspace (PAC CLI 2.4.x rootPath) |
| `src/client/external-spa/powerpages.config.json` | Legacy site config (kept for reference, not used by upload) |
| `src/client/external-spa/.env.local` | Local dev overrides (`VITE_DEV_MOCK=true`) |
| `src/client/external-spa/.env.production.local` | Production build overrides (`VITE_DEV_MOCK=false` + credentials) |
| `scripts/Deploy-ExternalWorkspaceSpa.ps1` | Deploys to Dataverse web resource (NOT the portal) |

---

*For Claude Code: This skill deploys to the Power Pages Code Site at `sprk-external-workspace.powerappsportals.com`. The deploy script is `scripts/Deploy-PowerPages.ps1`. The `--rootPath` for `pac pages upload-code-site` must be `.site-download/spaarke-external-workspace` — pointing to the original `external-spa` folder will fail with PAC CLI 2.4.x. The web resource deploy script is for a different (internal) target.*
