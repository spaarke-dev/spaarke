---
description: Deploy the BFF API (Sprk.Bff.Api) to Azure App Service using the deployment script
tags: [deploy, azure, api, bff, app-service]
techStack: [dotnet, azure, app-service]
appliesTo: ["deploy bff", "deploy api", "publish bff", "bff deploy", "update bff api"]
alwaysApply: false
---

# BFF API Deployment

> **Category**: Operations
> **Last Updated**: May 12, 2026 (added SHA-256 verification + auto-recover after observing silent deploy failures on Windows App Service)

Deploy the BFF API (`Sprk.Bff.Api`) to Azure App Service.

> 🚨 **CRITICAL — Silent Deploy Failures (May 2026)**: `az webapp deploy --type zip` has been observed to return HTTP 200 + Kudu `status=4 success` while NOT actually replacing the DLLs on disk. The running .NET host holds file locks on the DLLs and Windows refuses overwrites — but the deploy mechanism reports success anyway. **Never trust the success message alone.** Always verify with SHA-256 hash comparison via Kudu VFS. The hardened `Deploy-BffApi.ps1` does this automatically; manual deploys MUST verify (see Manual Verification section below).

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
| Deploy reports success but old code runs (SILENT FILE LOCK FAILURE) | The running .NET host on Windows App Service held file locks on `Sprk.Bff.Api.dll`. `az webapp deploy --type zip` returned 200 and Kudu logged "Deployment successful" but the DLL on disk was never replaced. **First observed May 12, 2026.** | The hardened `Deploy-BffApi.ps1` now compares local + remote SHA-256 hashes for 6 critical files after every deploy and auto-recovers with stop → Kudu zipdeploy → start. If you must deploy manually, always verify: see "Manual verification" below. |
| Persistent old code after restart | Deployment didn't register | Use Kudu Zip Push Deploy: `curl -X POST "https://{app}.scm.azurewebsites.net/api/zipdeploy?isAsync=true" -H "Authorization: Bearer $(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)" -H "Content-Type: application/zip" --data-binary @publish.zip` — stop the app first if files are locked. |

### Why "deploy succeeded" can be a lie (May 12, 2026)

On Windows App Service, the running .NET host process opens `Sprk.Bff.Api.dll` and other DLLs as memory-mapped files. Windows then refuses to overwrite these locked files. `az webapp deploy --type zip` (via Kudu OneDeploy) is tolerant of partial overwrite failures: it logs *"Clean deploying to C:\home\site\wwwroot"* and *"Deployment successful"* regardless. The HTTP 200 + Kudu `status=4` tells you Kudu RECEIVED and PROCESSED the zip — it does NOT confirm files were replaced.

**This had not been observed before May 2026** despite hundreds of successful BFF deploys between Dec 2025 and Apr 2026. The root cause of the new behavior is not fully understood (likely a Windows App Service platform change in worker process file-handle management). The hardened script's hash-verify + auto-recover is the defensive workaround.

**The reliable test for any BFF deploy: SHA-256 hash of critical files via Kudu VFS API.**

### Manual verification (if not using the hardened script)

```powershell
$mgmtToken = az account get-access-token --resource https://management.azure.com --query accessToken -o tsv
$kuduHeaders = @{ Authorization = "Bearer $mgmtToken" }
$kuduBase = "https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/site/wwwroot"

foreach ($f in 'Sprk.Bff.Api.dll','Sprk.Bff.Api.deps.json','Spaarke.Core.dll','Spaarke.Dataverse.dll','web.config') {
    $local = (Get-FileHash -Algorithm SHA256 -Path "deploy/api-publish/$f").Hash
    $tmp = New-TemporaryFile
    Invoke-WebRequest -Uri "$kuduBase/$f" -Headers $kuduHeaders -OutFile $tmp -UseBasicParsing
    $remote = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash
    Remove-Item $tmp
    $match = if ($local -eq $remote) { "MATCH" } else { "MISMATCH" }
    Write-Host "${match}: $f"
}
```

If any file shows MISMATCH, the deploy did NOT replace it. Recover with stop → Kudu zipdeploy → start (see auto-recover logic in the hardened script).

---

## Future Migration: WEBSITE_RUN_FROM_PACKAGE (planned, not yet executed)

The current mitigation (hash verify + auto-recover) is reliable but inelegant — every deploy that hits a file lock pays a stop/start cycle. The long-term fix is to migrate the App Service to **Run-From-Package mode**, where the deployed zip is mounted as a read-only filesystem and wwwroot is never written to. File locks become impossible because there are no physical files to lock.

**Status**: queued, not yet performed. The hardened script handles the current pain reliably so there's no urgency, but the migration eliminates the failure class entirely.

### Risk-managed migration procedure

When ready to switch, follow these steps in order. Do NOT skip steps — each catches a class of breakage.

1. **Audit assumptions of mutable wwwroot**:
   - Search the repo for any code that writes to `wwwroot/`, `/home/site/wwwroot/`, `/site/wwwroot/`, or `D:\home\site\wwwroot`. None should exist in BFF runtime code (the BFF doesn't self-modify), but check.
   - Search Kudu console history for ad-hoc edits (e.g., someone hand-editing `web.config` or `appsettings.json` in the portal). Document and re-apply those as build-time changes.
   - Check CI/CD pipelines for assumptions about deploy targets.

2. **Test on a staging slot first** (NOT directly on dev):
   ```bash
   # Create a staging slot if it doesn't exist
   az webapp deployment slot create --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 --slot staging-rfp-test

   # Enable run-from-package on the staging slot only
   az webapp config appsettings set --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 --slot staging-rfp-test \
     --settings WEBSITE_RUN_FROM_PACKAGE=1

   # Deploy to the staging slot with the hardened script
   .\scripts\Deploy-BffApi.ps1 -UseSlotDeploy -SlotName staging-rfp-test
   ```

3. **Smoke test the staging slot for at least one full workday**:
   - All BFF endpoints respond
   - Sign-in flow works
   - File upload + AI processing works end-to-end
   - Background workers (Service Bus consumers) still process messages
   - No new errors in Application Insights vs. the production slot baseline

4. **Verify the deploy mechanism itself**:
   - Make a small text-only change (e.g., a log message)
   - Re-deploy to the staging slot
   - Confirm the new code is running (use `/swagger` or a known endpoint that returns the changed text)
   - The hardened script's hash-verify should still PASS — it just verifies files inside the mounted zip rather than on wwwroot

5. **Cutover**:
   - Enable `WEBSITE_RUN_FROM_PACKAGE=1` on the production slot
   - Deploy with the hardened script
   - Verify hash-match and health endpoints
   - Monitor Application Insights for 15-30 min for new error patterns

6. **Document in this skill**: once cutover is complete and stable, update this section to note the new mode is live + remove the "future migration" framing. Note any operational quirks discovered.

7. **Rollback path** (keep handy during migration):
   ```bash
   # If run-from-package causes problems, disable it and redeploy
   az webapp config appsettings delete --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --setting-names WEBSITE_RUN_FROM_PACKAGE
   .\scripts\Deploy-BffApi.ps1
   ```

### Things that break under Run-From-Package

- Hot-editing files in Kudu console (wwwroot is read-only) — must use proper deploy
- Anything that writes to `wwwroot/` at runtime (logs, generated files) — should already be writing to `/home/LogFiles/` or `/home/site/logs/`, but verify
- Diagnostic file uploads via Kudu VFS PUT — read-only

### Things that DON'T break

- `/home/data/`, `/home/LogFiles/`, `/home/site/deployments/` — all remain writable
- Application Insights, Service Bus, Key Vault — unaffected
- Slot deploys + swap — fully supported
- The hardened deploy script — its hash-verify works identically because Kudu VFS transparently reads from the mounted zip

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
