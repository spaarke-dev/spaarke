---
description: Deploy the BFF API (Sprk.Bff.Api) to Azure App Service using the deployment script
tags: [deploy, azure, api, bff, app-service]
techStack: [dotnet, azure, app-service]
appliesTo: ["deploy bff", "deploy api", "publish bff", "bff deploy", "update bff api"]
alwaysApply: false
exemplar: scripts/Deploy-BffApi.ps1
last-reviewed: 2026-05-16
---

# BFF API Deployment

> **Category**: Operations
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-B — `leave-alone-justified`; extracted future-migration section to references/; otherwise gold-standard incident-grounded skill)
> **Exemplar rationale**: The deploy script `Deploy-BffApi.ps1` IS the canonical operational pattern — it's exercised every deploy. Recent change history is the most useful reference.
> **Hardened 2026-05-14**: Default health-check window 120 s (Linux cold-start tolerance); hash-verify + auto-recover for silent file-lock failures; FAILURE-MODES.md G-2 documents the incident.

Deploy the BFF API (`Sprk.Bff.Api`) to Azure App Service.

> 🚨 **CRITICAL — Silent Deploy Failures (May 2026)**: `az webapp deploy --type zip` has been observed to return HTTP 200 + Kudu `status=4 success` while NOT actually replacing the DLLs on disk. The running .NET host holds file locks on the DLLs and Windows refuses overwrites — but the deploy mechanism reports success anyway. **Never trust the success message alone.** Always verify with SHA-256 hash comparison via Kudu VFS. The hardened `Deploy-BffApi.ps1` does this automatically; manual deploys MUST verify (see Manual Verification section below).

> 🟡 **Linux App Service cold-start note (2026-05-14)**: After a `stop → Kudu zipdeploy → start` cycle (the script's auto-recover path), Linux App Service often takes **90–120 seconds** to respond to `/healthz`. Before 2026-05-14 the script's default health window was 60 s and reported a false failure here even though the deploy was successful and hash-verify had passed. The default is now **24 retries × 5 s = 120 s**, overridable via `-MaxHealthCheckRetries`. If hash-verify passes but `/healthz` still times out, the deploy is correct — the app is just still booting. Re-run `curl /healthz` manually after another 30–60 s instead of redeploying.

> ✅ **The two-layer safety net**: (1) **Hash-verify** catches the silent-success-but-not-replaced case (Windows file lock). (2) **Health check** catches the case where files are correct but startup fails. Both run after every deploy. If hash-verify **fails**, the deploy is broken — the script auto-recovers via Kudu zipdeploy. If hash-verify **passes** but health check times out, the deploy is **correct** — the app needs more time to start. Never re-deploy in response to a healthz-only failure when hash-verify succeeded.

---

## Quick Reference

| Item | Value |
|------|-------|
| Project Path | `src/server/api/Sprk.Bff.Api/` |
| App Service | `spe-api-dev-67e2xz` |
| Resource Group | `spe-infrastructure-westus2` |
| Health Check | `https://spe-api-dev-67e2xz.azurewebsites.net/healthz` |
| Deploy Script | `scripts/Deploy-BffApi.ps1` |
| Auth setup (operator runbook) | [`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) — 10-section runbook incl. §7 Exchange ApplicationAccessPolicy |
| Canonical auth ADR | [`ADR-028`](../../adr/ADR-028-spaarke-auth-architecture.md) — function-based contract, MI, HMAC webhooks, named API keys |

---

## Auth Setup (post-deploy verification)

After every fresh-env deploy OR cutover involving MI/auth changes, verify per [`auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) §9 smoke tests:
- §9a `/healthz` returns 200
- §9b OBO endpoint round-trip (proves JWT validation + OBO exchange)
- §9c `/healthz/dataverse/doc/{id}` (proves MI → Dataverse)
- §9d EXO mailbox access — no 403 in `InboundPollingBackupService` logs (if Email/Communication enabled)
- §9e Browser MSAL regression on any Spaarke PCF/Code Page (no popup, tenant-specific authority)

**Common post-deploy auth failure modes** (not deploy-script issues):
- MI deploy succeeds but Graph 403 → MI missing `Sites.Selected` or other app role grants. See `auth-deployment-setup.md` §5.
- MI deploy succeeds but Dataverse 401 → MI not registered as Dataverse Application User in the target env. See §6.
- Graph `Mail.*` returns `ErrorAccessDenied` → Exchange `ApplicationAccessPolicy` not configured for BFF MI or app reg. See §7.

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
  Deployment complete (or: auto-recovered via Kudu zipdeploy)
[4/4] Verifying file replacement on server...
  All 6 critical files match local build (SHA-256 verified)   # <-- the silent-failure guard
[5/4] Verifying health endpoint...
  health check passed!                                         # <-- up to 120 s on Linux
```

**Reading the output**:
- "All 6 critical files match" = **deploy is genuinely complete**. The new DLLs are on disk.
- "health check passed" = the app started up successfully.
- "All 6 critical files match" + "health check failed after 24 attempts" = **deploy is correct, app is slow to boot**. Wait another 30–60 s and `curl /healthz` manually. Do NOT redeploy.
- "Hash MISMATCH on file X" = real deploy failure. Script auto-recovers via stop → Kudu zipdeploy → start.

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

> **Detailed migration procedure**: [`references/run-from-package-migration.md`](references/run-from-package-migration.md)

**Summary**: The current hash-verify + auto-recover pattern is reliable but inelegant — every deploy that hits a file lock pays a stop/start cycle. Long-term, migrate the App Service to **Run-From-Package mode** (mounted read-only zip; file locks become impossible).

**Status**: Queued, not yet performed. No urgency — the hardened script handles current pain reliably. See the linked reference doc for the full risk-managed migration procedure (7 steps, things that break / don't break under Run-From-Package, rollback path).

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| `az webapp deploy --type zip` returns 200 + "Deployment successful" but DLLs not actually replaced | Running .NET host holds file locks on `Sprk.Bff.Api.dll`; Windows silently refuses overwrites; OneDeploy reports success regardless | **2026-05-14 G-2 incident.** Always verify with SHA-256 hash via Kudu VFS. The hardened `Deploy-BffApi.ps1` does this automatically + auto-recovers via stop → Kudu zipdeploy → start. NEVER trust deploy "success" alone. |
| Health check fails at 60s but the deploy actually succeeded | Default window sized for Windows warm-restart; Linux App Service cold-start is 90-120s | Default is now 120s (24 retries × 5s). Hash-verify success + healthz timeout = deploy correct, still booting. Wait one more cycle before declaring failure. |
| Package size < 40 MB after publish | Incomplete zip — missing nested DLLs because publish ran from `/tmp` or outside project tree | Always publish from `src/server/api/Sprk.Bff.Api/` (not external dirs). Verify package is 55-65 MB. |
| Health check passes but specific endpoints return 404 | Incomplete package: route handler couldn't compile at startup due to missing DLL | Test specific endpoints behind `.RequireAuthorization()` — should return 401 (route found, auth needed), NEVER 404. If 404, deploy is incomplete. |
| MSB3030 error during publish | Nested `publish/publish/` directory from leftover prior publish | Delete `src/server/api/Sprk.Bff.Api/publish/` before re-publishing. The script does this automatically (Step 1). |

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
