---
description: Build and deploy Spaarke Office Add-ins (Outlook, Word) to Azure Static Web Apps via SWA CLI
tags: [deploy, office-addins, static-web-app, swa, outlook, word, manifest]
techStack: [react, typescript, webpack, swa-cli, azure-static-web-apps, m365-admin-center]
appliesTo: ["src/client/office-addins/**", "deploy office add-in", "deploy outlook addin", "deploy word addin", "office add-in deploy", "swa deploy"]
alwaysApply: false
exemplar: scripts/Deploy-OfficeAddins.ps1
last-reviewed: 2026-05-17
---

# Office Add-ins Deploy

> **Category**: Operations (Tier 3)
> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2c — extracted from `azure-deploy` as a focused single-purpose skill)
> **Exemplar rationale**: `scripts/Deploy-OfficeAddins.ps1` IS the canonical operational pattern — it's exercised on every Office Add-in deploy and contains all the SWA-spinner-workaround logic.

---

## Purpose

Deploy Spaarke Office Add-ins (Outlook, Word) to Azure Static Web App via SWA CLI. This is a niche workflow — the Office Add-ins team iterates on add-in changes (taskpane UI, manifest) and needs fast dev-mode deploys; production deploys go through GitHub Actions.

**Separation of concerns**: This skill is for Office Add-in deploys ONLY. For other Azure work, see `azure-deploy` (Infrastructure + Key Vault), `bff-deploy` (BFF API), `code-page-deploy` (React Code Pages), `pcf-deploy` (PCF controls), `power-page-deploy` (Power Pages Code Site).

---

## When to Use

- Deploying Office Add-in changes (Outlook, Word)
- Updating taskpane UI or manifest
- Dev iteration on add-in features
- User says "deploy office add-in", "deploy outlook addin", "deploy word addin", "swa deploy"

**When NOT to Use**:
- Other Azure resources → `azure-deploy`
- BFF API → `bff-deploy`
- React Code Pages → `code-page-deploy`
- Production Office Add-in release → use the GitHub Actions workflow (`.github/workflows/deploy-office-addins.yml`) instead of this skill

---

## Resource Reference

| Resource | Value |
|----------|-------|
| Static Web App Name | `spaarke-office-addins` |
| Resource Group | `spe-infrastructure-westus2` |
| URL | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` |
| Source Directory | `src/client/office-addins` |
| Dist Directory | `src/client/office-addins/dist` |
| GitHub Actions Workflow | `.github/workflows/deploy-office-addins.yml` |

---

## Dev Deployment (Recommended for Iteration)

**Always use the deployment script** — it handles all the complexity (spinner workaround, fresh token, build-or-skip):

```powershell
# Full build and deploy
.\scripts\Deploy-OfficeAddins.ps1

# Skip build (use existing dist)
.\scripts\Deploy-OfficeAddins.ps1 -SkipBuild

# Deploy to preview environment
.\scripts\Deploy-OfficeAddins.ps1 -Environment preview

# Verbose output
.\scripts\Deploy-OfficeAddins.ps1 -Verbose
```

**What the script does:**
1. Builds webpack production bundle (unless `-SkipBuild`)
2. Gets fresh deployment token from Azure (`az staticwebapp secrets list`)
3. Deploys using SWA CLI with the spinner workaround (`Start-Process` + log redirect)
4. Verifies deployment with a cache-busted curl
5. Reports manifest version from the deployed manifest.xml

---

## Manual Deployment (Alternative — only if script unavailable)

```powershell
# Navigate to office-addins directory
cd src/client/office-addins

# 1. Build production bundle
npx webpack --mode production

# 2. Get fresh deployment token (tokens can expire/rotate)
$token = az staticwebapp secrets list `
  --name spaarke-office-addins `
  --resource-group spe-infrastructure-westus2 `
  --query properties.apiKey -o tsv

# 3. Deploy with output to log file (avoids SWA spinner hanging issue)
Start-Process -FilePath 'powershell.exe' `
  -ArgumentList "-NoProfile","-Command","npx swa deploy ./dist --deployment-token $token --env production *> deploy.log" `
  -NoNewWindow -Wait

# 4. Check deployment result
Get-Content deploy.log

# 5. Verify deployment (use cache-busting param)
curl "https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/manifest.xml?v=$(Get-Date -Format 'HHmmss')"
```

---

## Why Direct Deploy Instead of GitHub Actions

| Aspect | Direct Deploy (this skill) | GitHub Actions (`deploy-office-addins.yml`) |
|--------|---------------------------|-----------------|
| Speed | ~30 seconds | 2-5 minutes (CI + deploy) |
| Requires PR | No | Yes (merge to master) |
| Best for | Dev iteration, debugging | Production releases |
| Token | Fresh each time | Stored in GitHub secrets |

**Use Direct Deploy when**:
- Testing UI changes
- Debugging add-in issues
- Rapid iteration cycles
- Any work that doesn't need PR review

**Use GitHub Actions when**:
- Ready for production release
- Changes have been code-reviewed
- Want deployment audit trail

---

## Manifest Upload After Deploy

After deploying code changes, if `manifest.xml` changed:

1. Download manifest: `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/manifest.xml`
2. Upload to M365 Admin Center → Integrated Apps → Upload custom app
3. Wait 5-15 minutes for propagation to Outlook clients

**Note**: Manifest version must be incremented (e.g., 1.0.1.0 → 1.0.2.0) for M365 to accept updates. Same Add-in version with the same manifest version = M365 rejects.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| SWA CLI spinner hangs forever | `npx swa deploy` spinner blocks stdout/stderr | Use `Start-Process` with log redirect (deployment script does this automatically). Never invoke `swa deploy` directly in a foreground PowerShell. |
| Deployment token rejected | Tokens rotate periodically (Azure refreshes them) | Get a fresh token before every deploy via `az staticwebapp secrets list`. The script does this. NEVER cache the token in env vars. |
| Deployment "succeeds" but content is stale | Azure CDN caching | Verify with cache-busting URL: `?v=$(Get-Date -Format 'HHmmss')`. Wait 1-2 min for CDN purge. Force-refresh in Outlook (close/reopen client). |
| 404 on deployed files | Wrong `dist` path (e.g., `./build` instead of `./dist`) | Verify `src/client/office-addins/dist/` contains expected files (`outlook/manifest.xml`, `outlook/taskpane.html`, etc.) BEFORE deploy. |
| Manifest accepted by SWA but Outlook clients show old version | Manifest version not incremented | M365 rejects manifest uploads with same version. ALWAYS bump version (1.0.1.0 → 1.0.2.0) before re-uploading to M365 Admin Center. |
| Production deploy needed but skill is for dev iteration | Confusion about which path to use | This skill = dev iteration via SWA CLI. Production = GitHub Actions (`deploy-office-addins.yml`). Don't deploy production via this manual path. |

---

## Related Skills

- `azure-deploy` — Azure infrastructure (Bicep) and Key Vault Secrets (this skill was extracted from azure-deploy 2026-05-17)
- `bff-deploy` — BFF API deployment
- `code-page-deploy` — React Code Pages
- `pcf-deploy` — PCF controls
- `power-page-deploy` — Power Pages Code Sites

---

*For Claude Code: This skill is specifically for Office Add-ins (Outlook, Word) deployment to the Azure Static Web App. For any other Azure work, use `azure-deploy`. This is a focused single-purpose skill extracted from `azure-deploy` in Phase 2b Wave 2c.*
