---
description: Deploy the BFF API (Sprk.Bff.Api) to Azure App Service using the deployment script
tags: [deploy, azure, api, bff, app-service]
techStack: [dotnet, azure, app-service]
appliesTo: ["deploy bff", "deploy api", "publish bff", "bff deploy", "update bff api"]
alwaysApply: false
---

# BFF API Deployment

> **Category**: Operations
> **Last Updated**: February 2026

Deploy the BFF API (`Sprk.Bff.Api`) to Azure App Service.

---

## Quick Reference

| Item | Value |
|------|-------|
| Project Path | `src/server/api/Sprk.Bff.Api/` |
| App Service | `spe-api-dev-67e2xz` |
| Resource Group | `spe-infrastructure-westus2` |
| Health Check | `https://spe-api-dev-67e2xz.azurewebsites.net/healthz` |
| Deploy Script | `scripts/Deploy-BffApi.ps1` |

---

## Path Map

All paths are relative to the repository root. **Claude Code MUST use these exact paths.**

| Purpose | Path |
|---------|------|
| **Source project** | `src/server/api/Sprk.Bff.Api/` |
| **Project file** | `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` |
| **Deploy script** | `scripts/Deploy-BffApi.ps1` |
| **Publish output** | `src/server/api/Sprk.Bff.Api/publish/` |
| **Deployment zip** | `src/server/api/Sprk.Bff.Api/publish.zip` |

### Forbidden Paths (NEVER use for BFF builds)

- ❌ `/tmp/` or any system temp directory
- ❌ `$HOME/` or user home directory
- ❌ Any path outside the project tree (`src/server/api/Sprk.Bff.Api/`)

**Why**: Publishing to external directories produces incomplete packages (~22 MB) missing nested DLLs. The correct package from the project directory is ~61 MB.

---

## MANDATORY: Use the Deployment Script

**ALWAYS use the deployment script.** Do NOT manually run `dotnet publish` + `Compress-Archive` + `az webapp deploy`.

```powershell
# Full build and deploy (~1 min)
.\scripts\Deploy-BffApi.ps1

# Deploy existing build (faster, ~30 sec)
.\scripts\Deploy-BffApi.ps1 -SkipBuild
```

### Why the Script Is Required

The deployment script handles critical packaging steps that are easy to get wrong manually:

1. **Publishes from the project directory** — `dotnet publish -c Release -o ./publish` must run from `src/server/api/Sprk.Bff.Api/` so all dependencies resolve correctly
2. **Creates a complete zip** — `Compress-Archive -Path "$PublishPath\*"` from the publish folder includes ALL nested directories and DLLs (~61 MB). A manual zip from `/tmp` or other locations may produce an incomplete package (~22 MB) that silently drops endpoints.
3. **Waits for restart** — 10-second pause after deployment before health check
4. **Verifies health** — Retries health check up to 6 times

### What Happens With Incomplete Packages

If the zip is missing DLLs (e.g., created from wrong directory or with wrong glob pattern):
- The app starts and `/healthz` returns 200 (Healthy)
- Some endpoints work (those with fewer dependencies)
- Other endpoints silently return 404 (the route handler can't be compiled at startup)
- **This is extremely hard to diagnose** — the health check passes but endpoints are missing

---

## Procedure

### Step 1: Clean Previous Publish Output

```powershell
# Remove stale publish directory to prevent circular nesting
Remove-Item -Recurse -Force src/server/api/Sprk.Bff.Api/publish -ErrorAction SilentlyContinue
```

**Why**: If a previous `publish` folder exists inside the project, `dotnet publish -o ./publish` creates a nested `publish/publish/` structure that causes MSB3030 errors.

### Step 2: Deploy

```powershell
.\scripts\Deploy-BffApi.ps1
```

Expected output:
```
=== BFF API Deployment ===
[1/4] Building API...
  Build successful
[2/4] Creating deployment package...
  Package created: ~61 MB          # <-- VERIFY: must be 55-65 MB
[3/4] Deploying to Azure...
  Deployment complete
[4/4] Verifying deployment...
  Health check passed!
```

### Step 3: Verify Endpoints

After deployment, verify that the specific endpoints you changed actually respond:

```bash
# Unauthenticated test — expect 401 (route found, needs auth)
curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/test/preview-url
# Expected: 401

# If you get 404, the route is NOT registered — package is incomplete
```

**Key verification rule**: Any endpoint behind `.RequireAuthorization()` should return **401** without a token. If it returns **404**, the route didn't register (incomplete deployment).

---

## Manual Quick Deploy (User-Performed)

When the user wants to deploy manually for fastest iteration:

1. **Build**: Run in terminal from repo root:
   ```powershell
   .\scripts\Deploy-BffApi.ps1
   ```
2. **Verify**: Check output shows `~61 MB` package and `Health check passed!`
3. **Test endpoint**:
   ```bash
   curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/api/{your-endpoint}
   # Expect 401 (auth required) — NOT 404
   ```

### When the User Says "I'll deploy manually" or "just build it"

- Run `dotnet build src/server/api/Sprk.Bff.Api/` to verify compilation succeeds
- Tell the user: "Run `.\scripts\Deploy-BffApi.ps1` from the repo root to deploy"
- Do NOT attempt to deploy yourself unless explicitly asked

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Package size < 40 MB | Incomplete zip (missing DLLs) | Delete `publish/` dir, re-run script |
| MSB3030 error during publish | Nested `publish/publish/` | Delete `src/server/api/Sprk.Bff.Api/publish/` |
| Health check passes but endpoints 404 | Incomplete package | Re-deploy with script, verify ~61 MB |
| Deploy succeeds but old code runs | Azure caching | Restart app: `az webapp restart -g spe-infrastructure-westus2 -n spe-api-dev-67e2xz` |
| Persistent old code after restart | Deployment didn't register | Use Kudu Zip Push Deploy as fallback (see azure-deploy skill) |

---

## Anti-Patterns (DO NOT)

- **DO NOT** run `dotnet publish` to `/tmp` or any directory outside the project tree
- **DO NOT** use `Compress-Archive -Path './*'` from a temp directory — it may not capture nested subdirectories correctly on Windows
- **DO NOT** skip the health check verification step
- **DO NOT** assume the deployment worked just because `az webapp deploy` returned success
- **DO NOT** use `az webapp deploy` without `--async false` — the async default may report success before the deployment completes

---

## Related Skills

| Skill | When to Use |
|-------|-------------|
| `azure-deploy` | Full Azure infrastructure, App Settings, Key Vault operations |
| `dataverse-deploy` | PCF controls, solutions, web resources |
| `pcf-deploy` | PCF-specific deployment to Dataverse |
| `code-page-deploy` | Code Page web resource build and deployment |
